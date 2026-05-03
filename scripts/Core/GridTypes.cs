namespace PRISM.Core;

public enum TileType
{
    Floor,
    Wall,
    GlassWall,
    Door,
    Pit
}

public enum ComponentType
{
    Source,
    Receiver,
    Mirror,
    Prism,
    Splitter,
    Filter,
    Mixer,
    Absorber
}

public enum CrateVariant
{
    Standard,
    Glass,
    ColorLocked,
    ColorBlocking
}

public enum PressurePlateMode
{
    Weight,
    Light,
    ColorLight
}

public enum TerminationReason
{
    Blocked,
    Intersection,
    Receiver,
    Absorber,
    GridEdge,
    Loop,
    MixerInput
}
