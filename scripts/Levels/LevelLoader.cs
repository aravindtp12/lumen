using System;
using System.Text.Json;
using Godot;
using PRISM.Core;

namespace PRISM.Levels;

public static class LevelLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas         = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
    };

    /// <summary>Load a level from a res:// path (e.g. "res://levels/world1/level_01.json").</summary>
    public static Grid Load(string resPath)
    {
        // Godot maps res:// to the actual filesystem path at runtime
        string absPath = ProjectSettings.GlobalizePath(resPath);
        string json    = System.IO.File.ReadAllText(absPath);
        var data = JsonSerializer.Deserialize<LevelData>(json, JsonOpts)
                   ?? throw new Exception($"Failed to parse level: {resPath}");
        return Build(data);
    }

    private static Grid Build(LevelData data)
    {
        var grid = new Grid(data.Width, data.Height);

        // Player start
        grid.Player.Pos    = (data.PlayerStart[0], data.PlayerStart[1]);
        grid.Player.Facing = Direction.S;

        // Tile types (everything not listed defaults to Floor)
        foreach (var t in data.Tiles)
        {
            var type = Enum.Parse<TileType>(t.Type, ignoreCase: true);
            var tile = grid.GetTile(t.X, t.Y);
            if (tile == null) continue;
            tile.Type   = type;
            tile.DoorId = t.DoorId;
        }

        // Components
        foreach (var c in data.Components)
        {
            var comp = new ComponentData
            {
                Id               = c.Id,
                Type             = Enum.Parse<ComponentType>(c.Type, ignoreCase: true),
                Pos              = (c.X, c.Y),
                Rotation         = c.Rotation,
                Color            = c.Color != null ? Enum.Parse<BeamColor>(c.Color, true) : null,
                Pushable         = c.Pushable,
                Rotatable        = c.Rotatable,
                Powered          = c.Powered,
                FilterSubtractive = c.FilterSubtractive,
                LinkedId         = c.LinkedId,
            };
            grid.PlaceComponent(comp);
        }

        // Crates
        foreach (var c in data.Crates)
        {
            var crate = new CrateData
            {
                Id      = c.Id,
                Variant = Enum.Parse<CrateVariant>(c.Variant, ignoreCase: true),
                Pos     = (c.X, c.Y),
                Color   = c.Color != null ? Enum.Parse<BeamColor>(c.Color, true) : null,
            };
            grid.PlaceCrate(crate);
        }

        // Pressure plates
        foreach (var p in data.PressurePlates)
        {
            var plate = new PressurePlateData
            {
                Id            = p.Id,
                Mode          = Enum.Parse<PressurePlateMode>(p.Mode, ignoreCase: true),
                Latching      = p.Latching,
                TargetIds     = p.TargetIds,
                Logic         = p.Logic,
                RequiredColor = p.RequiredColor != null
                                ? Enum.Parse<BeamColor>(p.RequiredColor, true) : null,
            };
            grid.SetPressurePlate(p.X, p.Y, plate);
        }

        return grid;
    }
}
