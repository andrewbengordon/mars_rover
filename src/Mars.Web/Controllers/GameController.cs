﻿using Prometheus;
using System.Diagnostics;

namespace Mars.Web.Controllers;

public static class GameActivitySource
{
    public static ActivitySource Instance { get; } = new ActivitySource("Mars.Web", "1.0");
}

[ApiController]
[Route("[controller]")]
public class GameController : ControllerBase
{
    ConcurrentDictionary<string, GameManager> games;
    private readonly ConcurrentDictionary<string, string> tokenMap;
    private readonly ILogger<GameController> logger;
    private readonly MetricHelper metricHelper;

    public GameController(MultiGameHoster multiGameHoster, ILogger<GameController> logger, MetricHelper metricHelper)
    {
        this.games = multiGameHoster.Games;
        this.tokenMap = multiGameHoster.TokenMap;
        this.logger = logger;
        this.metricHelper = metricHelper;
    }

    /// <summary>
    /// Join an existing game.  You can join in the 'Joining' state, or in the 'Playing' state.
    /// </summary>
    /// <param name="gameId">What game you'd like to join</param>
    /// <param name="name">What your player name should be</param>
    /// <returns></returns>
    [HttpGet("[action]")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(JoinResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<JoinResponse> Join(string gameId, string name)
    {
        if (games.TryGetValue(gameId, out GameManager? gameManager))
        {
            try
            {
                using var activity = GameActivitySource.Instance.StartActivity("Join Game");
                var joinResult = gameManager.Game.Join(name);
                using (logger.BeginScope("ScopeUserToken: {ScopeUser} GameId: {ScopeGameId} ", joinResult.Token.Value, gameId))
                {
                    tokenMap.TryAdd(joinResult.Token.Value, gameId);
                    logger.LogWarning("Player {name} joined game {gameId}", name, gameId);
                }
                metricHelper.CumulativePlayerCounter.WithLabels(name, gameId).Inc();
                return new JoinResponse
                {
                    Token = joinResult.Token.Value,
                    StartingY = joinResult.PlayerLocation.Y,
                    StartingX = joinResult.PlayerLocation.X,
                    Neighbors = joinResult.Neighbors.ToDto(),
                    LowResolutionMap = joinResult.LowResolutionMap.ToDto(),
                    Targets = joinResult.TargetLocations.ToDto(),
                    Orientation = joinResult.Orientation.ToString(),
                    BatteryLevel = joinResult.BatteryLevel
                };
            }
            catch (TooManyPlayersException)
            {
                logger.LogError("Player {name} failed to join game {gameId}. Too many players", name, gameId);
                return Problem("Cannot join game, too many players.", statusCode: 400, title: "Too many players");
            }
        }
        else
        {
            logger.LogError("Player {name} failed to join game {gameId}. Game id not found", name, gameId);
            return Problem("Unrecognized game id.", statusCode: 400, title: "Bad Game ID");
        }
    }

    [HttpGet("[action]")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(StatusResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<StatusResponse> Status(string token)
    {
        var tokenHasGame = tokenMap.TryGetValue(token, out string? gameId);
        using (logger.BeginScope("ScopeUserToken: {ScopeUser} GameId: {ScopeGameId} ", token, gameId))
        {
            if (tokenHasGame)
            {
                if (games.TryGetValue(gameId, out var gameManager))
                {
                    if (gameManager.Game.TryTranslateToken(token, out _))
                    {
                        return new StatusResponse { Status = gameManager.Game.GameState.ToString() };
                    }
                }
            }
            logger.LogError("Unrecogized token {token}", token);
            return Problem("Unrecognized token", statusCode: 400, title: "Bad Token");
        }
    }

    /// <summary>
    /// Move the Perseverance rover.
    /// </summary>
    /// <param name="token"></param>
    /// <param name="direction">If left out, a default direction of Forward will be assumed.</param>
    /// <returns></returns>
    [HttpGet("[action]")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PerseveranceMoveResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<PerseveranceMoveResponse> MovePerseverance(string token, Direction direction)
    {
        var tokenHasGame = tokenMap.TryGetValue(token, out string? gameId);

        using (logger.BeginScope("ScopeUserToken: {ScopeUser} GameId: {ScopeGameId} ", token, gameId))
        {
            if (tokenHasGame)
            {
                if (games.TryGetValue(gameId, out var gameManager))
                {
                    PlayerToken? playerToken;
                    if (!gameManager.Game.TryTranslateToken(token, out playerToken))
                    {
                        logger.LogError("Unrecogized token {token}", token);
                        return Problem("Unrecognized token", statusCode: 400, title: "Bad Token");
                    }

                    if (gameManager.Game.GameState != GameState.Playing)
                    {
                        logger.LogError($"Could not move: Game not in Playing state.");
                        return Problem("Unable to move", statusCode: 400, title: "Game not in Playing state.");
                    }

                    try
                    {
                        var moveResult = gameManager.Game.MovePerseverance(playerToken!, direction);
                        return new PerseveranceMoveResponse
                        {
                            X = moveResult.Location.X,
                            Y = moveResult.Location.Y,
                            BatteryLevel = moveResult.BatteryLevel,
                            Neighbors = moveResult.Neighbors.ToDto(),
                            Message = moveResult.Message,
                            Orientation = moveResult.Orientation.ToString()
                        };
                    }
                    catch (Exception ex)
                    {
                        logger.LogError("Could not move: {message}", ex.Message);
                        return Problem("Unable to move", statusCode: 400, title: ex.Message);
                    }
                }

            }
            logger.LogError("Unrecogized token {token}", token);
            return Problem("Unrecognized token", statusCode: 400, title: "Bad Token");
        }
    }

    /// <summary>
    /// Move the Ingenuity helicopter.
    /// </summary>
    /// <param name="token"></param>
    /// <param name="destinationColumn"></param>
    /// <param name="destinationRow"></param>
    /// <returns></returns>
    [HttpGet("[action]")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IngenuityMoveResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<IngenuityMoveResponse> MoveIngenuity(string token, int destinationRow, int destinationColumn)
    {
        var tokenHasGame = tokenMap.TryGetValue(token, out string? gameId);
        using (logger.BeginScope("ScopeUserToken: {ScopeUser} GameId: {ScopeGameId} ", token, gameId))
        {
            if (tokenHasGame && games.TryGetValue(gameId, out var gameManager))
            {
                PlayerToken? playerToken;
                if (!gameManager.Game.TryTranslateToken(token, out playerToken))
                {
                    logger.LogError("Unrecognized token {token}", token);
                    return Problem("Unrecognized token", statusCode: 400, title: "Bad Token");
                }

                if (gameManager.Game.GameState != GameState.Playing)
                {
                    logger.LogError("Could not move: Game not in Playing state.");
                    return Problem("Unable to move", statusCode: 400, title: "Game not in Playing state.");
                }

                try
                {
                    var moveResult = gameManager.Game.MoveIngenuity(playerToken!, new MissionControl.Location(destinationRow, destinationColumn));
                    return new IngenuityMoveResponse
                    {
                        X = moveResult.Location.X,
                        Y = moveResult.Location.Y,
                        Neighbors = moveResult.Neighbors.ToDto(),
                        Message = moveResult.Message,
                        BatteryLevel = moveResult.BatteryLevel
                    };
                }
                catch (Exception ex)
                {
                    logger.LogError("Could not move: {exceptionMessage}", ex.Message);
                    return Problem("Unable to move", statusCode: 400, title: ex.Message);
                }
            }
            logger.LogError("Unrecognized token {token}", token);
            return Problem("Unrecognized token", statusCode: 400, title: "Bad Token");
        }
    }
}
