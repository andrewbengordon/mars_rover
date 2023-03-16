﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mars.MissionControl;

public static class Helpers
{
    private static ILogger<Game> logger = NullLoggerFactory.Instance.CreateLogger<Game>();

    public static Map CreateMap(int height, int width)
    {

        var cells = new List<Cell>();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                cells.Add(new Cell(new Location(x, y), new Difficulty(1)));
            }
        }
        var map = new Map(1, cells, BuildLowResMap(cells));
        return map;
    }

    public static Board CreateBoard(int height, int width, IEnumerable<Location> targets = null)
    {
        var map = CreateMap(height, width);
        return new Board(map);
    }

    public static Game CreateGame(int height, int width, IEnumerable<Location> targets = null)
    {
        var map = Helpers.CreateMap(5, 5);
        return CreateGame(map, targets);
    }

    public static Game CreateGame(Map map, IEnumerable<Location> targets = null)
    {
        if (targets == null)
        {
            targets = new[] { new Location(map.Width / 2, map.Height / 2) };
        }

        var startOptions = new GameCreationOptions
        {
            MapWithTargets = new MapWithTargets(map, targets)
        };
        var game = new Game(startOptions, logger, null);
        return game;
    }

    public static List<LowResolutionCell> BuildLowResMap(List<Cell> cells)
    {
        var lowResTiles = new List<LowResolutionCell>();

        int height = cells.Max(c => c.Location.Y) + 1;
        int width = cells.Max(c => c.Location.X) + 1;
        int tileSize = 10;

        for (int startingX = 0; startingX < width; startingX += tileSize)
        {
            for (int startingY = 0; startingY < height; startingY += tileSize)
            {
                bool isInChunk(Location l) =>
                    l.X >= startingX && l.X < (startingX + tileSize) &&
                    l.Y >= startingY && l.Y < (startingY + tileSize);

                var innerCells = cells
                    .Where(c => isInChunk(c.Location))
                    .Select(c => c);
                var newTile = new LowResolutionCell(innerCells);
                lowResTiles.Add(newTile);
            }
        }

        return lowResTiles;
    }
}
