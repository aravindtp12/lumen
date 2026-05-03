using System.Collections.Generic;

namespace PRISM.Core;

// ── Tile ──────────────────────────────────────────────────────────────────────

public class TileData
{
    public TileType Type { get; set; } = TileType.Floor;
    public ComponentData? Occupant { get; set; }
    public CrateData? Crate { get; set; }
    public PressurePlateData? PressurePlate { get; set; }
    public bool DoorOpen { get; set; }
    public string? DoorId { get; set; }

    public bool CanEnter =>
        (Type == TileType.Floor || Type == TileType.GlassWall ||
         (Type == TileType.Door && DoorOpen)) &&
        Occupant == null && Crate == null;

    public bool BlocksBeam =>
        Type == TileType.Wall || Type == TileType.Pit ||
        (Type == TileType.Door && !DoorOpen);

    public bool TransparentToBeam => Type == TileType.GlassWall;

    public TileData Clone() => new()
    {
        Type = Type,
        Occupant = Occupant?.Clone(),
        Crate = Crate?.Clone(),
        PressurePlate = PressurePlate?.Clone(),
        DoorOpen = DoorOpen,
        DoorId = DoorId
    };
}

// ── Component ─────────────────────────────────────────────────────────────────

public class ComponentData
{
    public string Id { get; set; } = "";
    public ComponentType Type { get; set; }
    public (int x, int y) Pos { get; set; }
    public int Rotation { get; set; }          // 0–315, 45° steps
    public BeamColor? Color { get; set; }      // Source / Filter / Receiver
    public bool Pushable { get; set; } = true;
    public bool Rotatable { get; set; } = true;
    public bool Powered { get; set; } = true;  // toggleable Source
    public bool FilterSubtractive { get; set; }
    public BeamColor? StoredColor { get; set; } // Absorber
    public bool AbsorberEmitting { get; set; }  // Absorber active
    public string? LinkedId { get; set; }        // pressure-plate → door/source

    public Direction Facing => DirectionHelper.FromDegrees(Rotation);

    public ComponentData Clone() => new()
    {
        Id = Id, Type = Type, Pos = Pos, Rotation = Rotation, Color = Color,
        Pushable = Pushable, Rotatable = Rotatable, Powered = Powered,
        FilterSubtractive = FilterSubtractive, StoredColor = StoredColor,
        AbsorberEmitting = AbsorberEmitting, LinkedId = LinkedId
    };
}

// ── Crate ─────────────────────────────────────────────────────────────────────

public class CrateData
{
    public string Id { get; set; } = "";
    public CrateVariant Variant { get; set; } = CrateVariant.Standard;
    public (int x, int y) Pos { get; set; }
    public BeamColor? Color { get; set; }     // ColorLocked / ColorBlocking
    public bool Illuminated { get; set; }     // ColorLocked crate: currently lit

    public CrateData Clone() => new()
    {
        Id = Id, Variant = Variant, Pos = Pos, Color = Color, Illuminated = Illuminated
    };
}

// ── Pressure Plate ────────────────────────────────────────────────────────────

public class PressurePlateData
{
    public string Id { get; set; } = "";
    public PressurePlateMode Mode { get; set; } = PressurePlateMode.Weight;
    public bool Latching { get; set; }
    public bool Active { get; set; }
    public bool Triggered { get; set; }        // latching: fired at least once
    public string[] TargetIds { get; set; } = System.Array.Empty<string>();
    public string Logic { get; set; } = "OR";  // "AND" | "OR"
    public BeamColor? RequiredColor { get; set; }

    public PressurePlateData Clone() => new()
    {
        Id = Id, Mode = Mode, Latching = Latching, Active = Active, Triggered = Triggered,
        TargetIds = (string[])TargetIds.Clone(), Logic = Logic, RequiredColor = RequiredColor
    };
}

// ── Player ────────────────────────────────────────────────────────────────────

public class PlayerData
{
    public (int x, int y) Pos { get; set; }
    public Direction Facing { get; set; } = Direction.S;

    public PlayerData Clone() => new() { Pos = Pos, Facing = Facing };
}

// ── Grid ──────────────────────────────────────────────────────────────────────

public class Grid
{
    public int Width  { get; }
    public int Height { get; }
    public PlayerData Player { get; set; }

    private readonly TileData[,] _tiles;
    private readonly Dictionary<string, ComponentData>    _compById  = new();
    private readonly Dictionary<string, CrateData>        _crateById = new();
    private readonly Dictionary<string, PressurePlateData> _plateById = new();

    public Grid(int width, int height)
    {
        Width = width; Height = height;
        _tiles = new TileData[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                _tiles[x, y] = new TileData();
        Player = new PlayerData();
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public TileData? GetTile(int x, int y) =>
        InBounds(x, y) ? _tiles[x, y] : null;

    public IEnumerable<ComponentData> AllComponents()
    {
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                if (_tiles[x, y].Occupant is { } c) yield return c;
    }

    public IEnumerable<CrateData> AllCrates()
    {
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                if (_tiles[x, y].Crate is { } c) yield return c;
    }

    public IEnumerable<PressurePlateData> AllPlates()
    {
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                if (_tiles[x, y].PressurePlate is { } p) yield return p;
    }

    // ── Mutation helpers ──────────────────────────────────────────────────────

    public void SetTileType(int x, int y, TileType t) => _tiles[x, y].Type = t;

    public void PlaceComponent(ComponentData comp)
    {
        var tile = _tiles[comp.Pos.x, comp.Pos.y];
        if (tile.Occupant is { } old) _compById.Remove(old.Id);
        tile.Occupant = comp;
        if (comp.Id != "") _compById[comp.Id] = comp;
    }

    public void RemoveComponent(int x, int y)
    {
        if (!InBounds(x, y)) return;
        var tile = _tiles[x, y];
        if (tile.Occupant is { } c) { _compById.Remove(c.Id); tile.Occupant = null; }
    }

    public void PlaceCrate(CrateData crate)
    {
        var tile = _tiles[crate.Pos.x, crate.Pos.y];
        if (tile.Crate is { } old) _crateById.Remove(old.Id);
        tile.Crate = crate;
        if (crate.Id != "") _crateById[crate.Id] = crate;
    }

    public void RemoveCrate(int x, int y)
    {
        if (!InBounds(x, y)) return;
        var tile = _tiles[x, y];
        if (tile.Crate is { } c) { _crateById.Remove(c.Id); tile.Crate = null; }
    }

    public void SetPressurePlate(int x, int y, PressurePlateData plate)
    {
        _tiles[x, y].PressurePlate = plate;
        if (plate.Id != "") _plateById[plate.Id] = plate;
    }

    public void RemovePressurePlate(int x, int y)
    {
        if (!InBounds(x, y)) return;
        var tile = _tiles[x, y];
        if (tile.PressurePlate is { } p) { _plateById.Remove(p.Id); tile.PressurePlate = null; }
    }

    public ComponentData? FindComponentById(string id) =>
        _compById.TryGetValue(id, out var c) ? c : null;

    public PressurePlateData? FindPlateById(string id) =>
        _plateById.TryGetValue(id, out var p) ? p : null;

    // ── Deep clone ────────────────────────────────────────────────────────────

    public Grid Clone()
    {
        var g = new Grid(Width, Height) { Player = Player.Clone() };
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                var clone = _tiles[x, y].Clone();
                g._tiles[x, y] = clone;
                if (clone.Occupant is { } c && c.Id != "")  g._compById[c.Id]  = c;
                if (clone.Crate    is { } cr && cr.Id != "") g._crateById[cr.Id] = cr;
                if (clone.PressurePlate is { } p && p.Id != "") g._plateById[p.Id] = p;
            }
        return g;
    }
}
