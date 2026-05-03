using System.Collections.Generic;

namespace PRISM.Levels;

// JSON-serialisable level format.
// Every field maps 1-to-1 to the grid data model so levels are
// human-readable plain-text files.

public class LevelData
{
    public string Name        { get; set; } = "";
    public string World       { get; set; } = "";
    public int    Index       { get; set; }
    public int    Width       { get; set; }
    public int    Height      { get; set; }
    public int[]  PlayerStart { get; set; } = { 0, 0 }; // [x, y]

    public List<TileEntry>          Tiles          { get; set; } = new();
    public List<ComponentEntry>     Components     { get; set; } = new();
    public List<CrateEntry>         Crates         { get; set; } = new();
    public List<PressurePlateEntry> PressurePlates { get; set; } = new();
}

public class TileEntry
{
    public int    X    { get; set; }
    public int    Y    { get; set; }
    public string Type { get; set; } = "Floor"; // TileType name
    public string? DoorId { get; set; }
}

public class ComponentEntry
{
    public string  Id       { get; set; } = "";
    public string  Type     { get; set; } = ""; // ComponentType name
    public int     X        { get; set; }
    public int     Y        { get; set; }
    public int     Rotation { get; set; }        // degrees 0–315
    public string? Color    { get; set; }        // BeamColor name or null
    public bool    Pushable { get; set; } = true;
    public bool    Rotatable { get; set; } = true;
    public bool    Powered  { get; set; } = true;
    public bool    FilterSubtractive { get; set; }
    public string? LinkedId { get; set; }
}

public class CrateEntry
{
    public string  Id      { get; set; } = "";
    public string  Variant { get; set; } = "Standard";
    public int     X       { get; set; }
    public int     Y       { get; set; }
    public string? Color   { get; set; }
}

public class PressurePlateEntry
{
    public string   Id         { get; set; } = "";
    public int      X          { get; set; }
    public int      Y          { get; set; }
    public string   Mode       { get; set; } = "Weight";
    public bool     Latching   { get; set; }
    public string[] TargetIds  { get; set; } = System.Array.Empty<string>();
    public string   Logic      { get; set; } = "OR";
    public string?  RequiredColor { get; set; }
}
