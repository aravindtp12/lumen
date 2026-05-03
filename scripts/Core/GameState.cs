using System;
using System.Collections.Generic;

namespace PRISM.Core;

// ── Events ────────────────────────────────────────────────────────────────────

public enum ActionResult { Ok, Blocked }

// ── Game State ────────────────────────────────────────────────────────────────

public class GameState
{
    private const int MaxUndoDepth = 100;

    public Grid          Grid          { get; private set; }
    public SolverResult  LastSolve     { get; private set; }
    public bool          Solved        { get; private set; }
    public int           MoveCount     { get; private set; }
    public int           TotalReceivers { get; private set; }
    public int           ActiveReceivers { get; private set; }

    private readonly Grid          _initialGrid;
    private readonly Stack<Grid>   _undoStack = new();

    // Fired after every state-mutating action
    public event Action? StateChanged;

    public GameState(Grid grid)
    {
        _initialGrid = grid.Clone();
        Grid         = grid;
        Recompute();
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    /// Move the player one tile in <dir>; push component/crate if applicable.
    /// If the move is blocked, the player still turns to face <dir> so they can
    /// rotate or interact with whatever is there — turning alone is free (no
    /// move counted, no undo snapshot).
    public ActionResult MovePlayer(Direction dir)
    {
        var (dx, dy) = dir.ToVector();
        var (px, py) = Grid.Player.Pos;
        int nx = px + dx, ny = py + dy;

        bool blocked;
        if (!Grid.InBounds(nx, ny))
        {
            blocked = true;
        }
        else
        {
            var dest = Grid.GetTile(nx, ny)!;

            // Tile-level block (wall, closed door, pit) with nothing to push
            if (!dest.CanEnter && dest.Occupant == null && dest.Crate == null)
            {
                blocked = true;
            }
            // Something to push?
            else if (dest.Occupant != null || dest.Crate != null)
            {
                int px2 = nx + dx, py2 = ny + dy;
                blocked = !TryPush(nx, ny, px2, py2);
            }
            else
            {
                blocked = !dest.CanEnter;
            }
        }

        if (blocked)
        {
            if (Grid.Player.Facing != dir)
            {
                Grid.Player.Facing = dir;
                StateChanged?.Invoke();
            }
            return ActionResult.Blocked;
        }

        Snapshot();
        Grid.Player.Pos    = (nx, ny);
        Grid.Player.Facing = dir;
        MoveCount++;
        PostAction();
        return ActionResult.Ok;
    }

    /// Rotate the component adjacent to the player in <adjacentDir>.
    /// If adjacentDir is None, use the player's current facing.
    public ActionResult Rotate(Direction adjacentDir, bool counterclockwise = false)
    {
        if (adjacentDir == Direction.None) adjacentDir = Grid.Player.Facing;

        var (dx, dy) = adjacentDir.ToVector();
        var (px, py) = Grid.Player.Pos;
        int tx = px + dx, ty = py + dy;

        if (!Grid.InBounds(tx, ty)) return ActionResult.Blocked;

        var tile = Grid.GetTile(tx, ty)!;
        if (tile.Occupant is not { } comp || !comp.Rotatable) return ActionResult.Blocked;

        Snapshot();
        int step   = counterclockwise ? -45 : 45;
        comp.Rotation = ((comp.Rotation + step) % 360 + 360) % 360;
        PostAction();
        return ActionResult.Ok;
    }

    /// Undo the last reversible action.
    public bool Undo()
    {
        if (_undoStack.Count == 0) return false;
        Grid = _undoStack.Pop();
        if (MoveCount > 0) MoveCount--;
        Recompute();
        StateChanged?.Invoke();
        return true;
    }

    /// Reset puzzle to initial state.
    public void Reset()
    {
        _undoStack.Clear();
        Grid      = _initialGrid.Clone();
        MoveCount = 0;
        Recompute();
        StateChanged?.Invoke();
    }

    /// Re-run the solver and notify listeners. Used by the level editor after
    /// direct grid edits that bypass the player-action path.
    public void Refresh()
    {
        EvaluatePressurePlates();
        Recompute();
        StateChanged?.Invoke();
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private bool TryPush(int fromX, int fromY, int toX, int toY)
    {
        if (!Grid.InBounds(toX, toY)) return false;

        var from = Grid.GetTile(fromX, fromY)!;
        var to   = Grid.GetTile(toX,   toY)!;

        if (!to.CanEnter) return false;

        if (from.Occupant is { } comp)
        {
            if (!comp.Pushable) return false;
            Grid.RemoveComponent(fromX, fromY);
            comp.Pos = (toX, toY);
            Grid.PlaceComponent(comp);
        }
        else if (from.Crate is { } crate)
        {
            // ColorLocked crates only pushable when illuminated
            if (crate.Variant == CrateVariant.ColorLocked && !crate.Illuminated)
                return false;
            Grid.RemoveCrate(fromX, fromY);
            crate.Pos = (toX, toY);
            Grid.PlaceCrate(crate);
        }
        else return false;

        return true;
    }

    private void Snapshot()
    {
        if (_undoStack.Count >= MaxUndoDepth) return;
        _undoStack.Push(Grid.Clone());
    }

    private void PostAction()
    {
        EvaluatePressurePlates();
        Recompute();
        StateChanged?.Invoke();
    }

    private void EvaluatePressurePlates()
    {
        foreach (var plate in Grid.AllPlates())
        {
            bool wasActive = plate.Active;
            bool nowActive = false;

            var tileData = FindPlateTile(plate.Id);
            if (tileData == null) continue;

            switch (plate.Mode)
            {
                case PressurePlateMode.Weight:
                    // Active if player or a crate/component is sitting on the same tile
                    nowActive = Grid.Player.Pos == GetPlateTilePos(plate.Id) ||
                                tileData.Crate != null || tileData.Occupant != null;
                    break;

                case PressurePlateMode.Light:
                    // Evaluated after beam solve; skip here
                    continue;

                case PressurePlateMode.ColorLight:
                    continue;
            }

            if (plate.Latching)
            {
                if (nowActive && !plate.Triggered) { plate.Triggered = true; plate.Active = true; }
            }
            else
            {
                plate.Active = nowActive;
            }

            if (plate.Active != wasActive) ApplyPlateTargets(plate);
        }
    }

    private void ApplyPlateTargets(PressurePlateData plate)
    {
        foreach (var targetId in plate.TargetIds)
        {
            // Toggle doors linked to this plate
            for (int x = 0; x < Grid.Width; x++)
                for (int y = 0; y < Grid.Height; y++)
                {
                    var t = Grid.GetTile(x, y)!;
                    if (t.DoorId == targetId)
                        t.DoorOpen = plate.Active;
                }

            // Toggle source power
            if (Grid.FindComponentById(targetId) is { Type: ComponentType.Source } src)
                src.Powered = plate.Active;
        }
    }

    private void Recompute()
    {
        LastSolve = BeamSolver.Solve(Grid);

        // Update illumination for ColorLocked crates
        foreach (var crate in Grid.AllCrates())
        {
            if (crate.Variant != CrateVariant.ColorLocked || !crate.Color.HasValue) continue;
            crate.Illuminated = LastSolve.TileColors.TryGetValue(crate.Pos, out var colors) &&
                                colors.Contains(crate.Color.Value);
        }

        // Count receivers
        int total = 0, active = 0;
        foreach (var comp in Grid.AllComponents())
        {
            if (comp.Type != ComponentType.Receiver) continue;
            total++;
            if (LastSolve.ReceiverStates.TryGetValue(comp.Id, out bool on) && on) active++;
        }
        TotalReceivers  = total;
        ActiveReceivers = active;
        Solved          = total > 0 && active == total;
    }

    // ── Tile-lookup helpers ───────────────────────────────────────────────────

    private TileData? FindPlateTile(string plateId)
    {
        for (int x = 0; x < Grid.Width; x++)
            for (int y = 0; y < Grid.Height; y++)
                if (Grid.GetTile(x, y)?.PressurePlate?.Id == plateId)
                    return Grid.GetTile(x, y);
        return null;
    }

    private (int x, int y) GetPlateTilePos(string plateId)
    {
        for (int x = 0; x < Grid.Width; x++)
            for (int y = 0; y < Grid.Height; y++)
                if (Grid.GetTile(x, y)?.PressurePlate?.Id == plateId)
                    return (x, y);
        return (-1, -1);
    }
}
