namespace PRISM.Core;

public enum Direction
{
    N, S, E, W,
    NE, NW, SE, SW,
    None
}

public static class DirectionHelper
{
    private static readonly Direction[] _clockwiseOrder =
        { Direction.N, Direction.NE, Direction.E, Direction.SE,
          Direction.S, Direction.SW, Direction.W, Direction.NW };

    public static (int dx, int dy) ToVector(this Direction dir) => dir switch
    {
        Direction.N  => (0, -1),
        Direction.S  => (0,  1),
        Direction.E  => (1,  0),
        Direction.W  => (-1, 0),
        Direction.NE => (1, -1),
        Direction.NW => (-1,-1),
        Direction.SE => (1,  1),
        Direction.SW => (-1, 1),
        _            => (0,  0)
    };

    public static Direction Opposite(this Direction dir) => dir switch
    {
        Direction.N  => Direction.S,
        Direction.S  => Direction.N,
        Direction.E  => Direction.W,
        Direction.W  => Direction.E,
        Direction.NE => Direction.SW,
        Direction.NW => Direction.SE,
        Direction.SE => Direction.NW,
        Direction.SW => Direction.NE,
        _            => Direction.None
    };

    public static Direction RotateCW(this Direction dir, int steps45 = 2)
    {
        int idx = System.Array.IndexOf(_clockwiseOrder, dir);
        if (idx < 0) return dir;
        return _clockwiseOrder[(idx + steps45) % 8];
    }

    public static Direction RotateCCW(this Direction dir, int steps45 = 2)
    {
        int idx = System.Array.IndexOf(_clockwiseOrder, dir);
        if (idx < 0) return dir;
        return _clockwiseOrder[(idx - steps45 + 8) % 8];
    }

    public static bool IsCardinal(this Direction dir) =>
        dir == Direction.N || dir == Direction.S ||
        dir == Direction.E || dir == Direction.W;

    // Rotation field in degrees → Direction (for component facing)
    public static Direction FromDegrees(int degrees)
    {
        int norm = ((degrees % 360) + 360) % 360;
        return norm switch
        {
            0   => Direction.N,
            45  => Direction.NE,
            90  => Direction.E,
            135 => Direction.SE,
            180 => Direction.S,
            225 => Direction.SW,
            270 => Direction.W,
            315 => Direction.NW,
            _   => Direction.N
        };
    }

    public static int ToDegrees(this Direction dir) => dir switch
    {
        Direction.N  => 0,
        Direction.NE => 45,
        Direction.E  => 90,
        Direction.SE => 135,
        Direction.S  => 180,
        Direction.SW => 225,
        Direction.W  => 270,
        Direction.NW => 315,
        _            => 0
    };

    public static int IndexOf(Direction dir)
    {
        int idx = System.Array.IndexOf(_clockwiseOrder, dir);
        return idx < 0 ? 0 : idx;
    }
}
