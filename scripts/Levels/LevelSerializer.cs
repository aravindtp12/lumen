using System.IO;
using System.Text.Json;
using PRISM.Core;

namespace PRISM.Levels;

/// Inverse of LevelLoader: turns a Grid back into the JSON format used by
/// the level catalogue. Used by the in-game level editor to persist creations.
public static class LevelSerializer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string ToJson(Grid grid, string name = "Sandbox", string world = "Sandbox", int index = 0)
    {
        var data = new LevelData
        {
            Name = name,
            World = world,
            Index = index,
            Width = grid.Width,
            Height = grid.Height,
            PlayerStart = new[] { grid.Player.Pos.x, grid.Player.Pos.y },
        };

        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var tile = grid.GetTile(x, y);
                if (tile == null) continue;
                if (tile.Type == TileType.Floor && tile.DoorId == null) continue;
                data.Tiles.Add(new TileEntry
                {
                    X = x, Y = y,
                    Type = tile.Type.ToString(),
                    DoorId = tile.DoorId,
                });
            }

        foreach (var c in grid.AllComponents())
        {
            data.Components.Add(new ComponentEntry
            {
                Id = c.Id,
                Type = c.Type.ToString(),
                X = c.Pos.x, Y = c.Pos.y,
                Rotation = c.Rotation,
                Color = c.Color?.ToString(),
                Pushable = c.Pushable,
                Rotatable = c.Rotatable,
                Powered = c.Powered,
                FilterSubtractive = c.FilterSubtractive,
                LinkedId = c.LinkedId,
            });
        }

        foreach (var crate in grid.AllCrates())
        {
            data.Crates.Add(new CrateEntry
            {
                Id = crate.Id,
                Variant = crate.Variant.ToString(),
                X = crate.Pos.x, Y = crate.Pos.y,
                Color = crate.Color?.ToString(),
            });
        }

        foreach (var plate in grid.AllPlates())
        {
            (int x, int y) pos = (-1, -1);
            for (int x = 0; x < grid.Width && pos.x < 0; x++)
                for (int y = 0; y < grid.Height && pos.x < 0; y++)
                    if (grid.GetTile(x, y)?.PressurePlate?.Id == plate.Id) pos = (x, y);

            data.PressurePlates.Add(new PressurePlateEntry
            {
                Id = plate.Id,
                X = pos.x, Y = pos.y,
                Mode = plate.Mode.ToString(),
                Latching = plate.Latching,
                TargetIds = plate.TargetIds,
                Logic = plate.Logic,
                RequiredColor = plate.RequiredColor?.ToString(),
            });
        }

        return JsonSerializer.Serialize(data, JsonOpts);
    }

    public static void Save(Grid grid, string absPath, string name = "Sandbox")
    {
        var dir = Path.GetDirectoryName(absPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(absPath, ToJson(grid, name));
    }
}
