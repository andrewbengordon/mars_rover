using Prometheus;

namespace Mars.MissionControl
{
    public class MetricHelper
    {
        public Counter CumulativePlayerCounter { get; set; }
        public Counter WinnerCounter { get; set; }

        public static readonly Exemplar.LabelKey WinnerNameKey = Exemplar.Key("winner_name");

        public Counter RoverBatteryDepletedCounter { get; set; }

        public Counter PlayersMovedCounter { get; set; }
        public Counter MoveTypeCounter { get; set; }
        public Counter TurnMovesCounter { get; set; }

        public MetricHelper()
        {
            CumulativePlayerCounter = Metrics.CreateCounter("cumulative_players_joined_total", "Counts cumulative successfull joins", new CounterConfiguration
            {
                LabelNames = new[] { "player_name", "game_id" }
            });
            WinnerCounter = Metrics.CreateCounter("winner_players_total", "counts the winners", new CounterConfiguration
            {
                LabelNames = new[] { "player_name", "game_id" }
            });
            RoverBatteryDepletedCounter = Metrics.CreateCounter("rover_battery_depleted_total", "Counts number of times the rover battery hits zero", new CounterConfiguration
            {
                LabelNames = new[] { "player_name", "game_id" }
            });
            PlayersMovedCounter = Metrics.CreateCounter("players_moved_total", "Counts the number of players who have made a move in a game", new CounterConfiguration
            {
                LabelNames = new[] { "game_id" }
            });
            MoveTypeCounter = Metrics.CreateCounter("move_type_total", "Counts the number of moves in a game", new CounterConfiguration
            {
                LabelNames = new[] { "move_type" }
            });
        }
    }
}
