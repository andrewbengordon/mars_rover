using Hellang.Middleware.ProblemDetails;
using Mars.Web;
using Mars.Web.Controllers;
using Microsoft.OpenApi.Models;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Serilog;
using Serilog.Exceptions;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Prometheus;
using Serilog.Sinks.Grafana.Loki;
using Serilog.Sinks.Loki;

var builder = WebApplication.CreateBuilder(args);

using var traceProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("Mars.Web"))
    .AddSource(GameActivitySource.Instance.Name)
    .AddJaegerExporter(o =>
    {
        o.Protocol = OpenTelemetry.Exporter.JaegerExportProtocol.HttpBinaryThrift;
        o.Endpoint = new Uri("http://jaeger:14268/api/traces");
    })
    .AddHttpClientInstrumentation()
    .AddAspNetCoreInstrumentation()
    .Build();

builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();

builder.Host.UseSerilog((context, loggerConfig) =>
{
    loggerConfig.WriteTo.Console()
    .Enrich.WithExceptionDetails()
    .Enrich.WithProperty("Application", context.HostingEnvironment.ApplicationName)
    .WriteTo.Seq(builder.Configuration["SeqServer"] ?? throw new ApplicationException("Unable to locate key SeqServer in configuration"))
    .WriteTo.LokiHttp(() =>
    {
        return new LokiSinkConfiguration() { LokiUrl = "http://loki:3100" };
    });
});

builder.Services.AddProblemDetails(opts =>
{
    opts.IncludeExceptionDetails = (ctx, ex) => false;
}
);

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Mars Rover", Version = "v1" });
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
});

builder.Services.AddSingleton<MultiGameHoster>();
builder.Services.AddSingleton<IMapProvider, FileSystemMapProvider>();

builder.Services.AddSingleton<MetricHelper>();

builder.Services.AddHostedService<CleanupGameService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Request.Query["token"].FirstOrDefault() ?? httpContext.Request.Headers.Host.ToString(),
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = int.Parse(builder.Configuration["ApiLimitPerSecond"] ?? throw new Exception("Unable to find ApiLimitPerSecond in config")),
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(1)
            }));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Custom Metrics to count requests for each endpoint and the method
var counter = Metrics.CreateCounter("api_path_counter_total", "Counts requests to API endpoints", new CounterConfiguration
{
    LabelNames = new[] { "method", "endpoint" }
});
app.Use((context, next) => {
    counter.WithLabels(context.Request.Method, context.Request.Path).Inc();
    return next();
});

// Use the Prometheus middleware
app.UseMetricServer();
app.UseHttpMetrics();


app.UseStaticFiles();

app.UseRouting();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Mars Rover v1");
});

app.MapBlazorHub()
   .DisableRateLimiting();

app.UseRateLimiter();

app.MapControllers();

app.MapFallbackToPage("/_Host")
   .DisableRateLimiting();

app.Run();

public partial class Program { }
