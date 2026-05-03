using System.Collections.Generic;
using System.Linq;

namespace PRISM.Core;

// ── Output types ──────────────────────────────────────────────────────────────

public class BeamSegment
{
	public (int x, int y) Start    { get; set; }
	public (int x, int y) End      { get; set; }
	public Direction Direction      { get; set; }
	public BeamColor Color          { get; set; }
	public TerminationReason Reason { get; set; }
	public bool Truncated           { get; set; }

	// Every tile this segment visits (Start … End inclusive)
	public List<(int x, int y)> Tiles { get; } = new();
}

public class SolverResult
{
	public List<BeamSegment> Segments        { get; } = new();
	public HashSet<(int, int)> Intersections { get; } = new();

	// Receiver id → activated
	public Dictionary<string, bool> ReceiverStates { get; } = new();

	// Tile → set of beam colors passing through (for plates / illuminated crates)
	public Dictionary<(int, int), HashSet<BeamColor>> TileColors { get; } = new();
}

// ── Solver ────────────────────────────────────────────────────────────────────

public static class BeamSolver
{
	private const int MaxSegments = 1000;

	private record struct RayKey(int X, int Y, Direction Dir, BeamColor Color);

	// Mirror interaction table: (rotation°, incomingDirection) → reflected direction
	// Beam strikes the mirror from direction inDir; "incoming face hit" = inDir.Opposite()
	// Only entries in this table produce a reflection; anything else is absorbed.
	private static readonly Dictionary<(int rot, Direction inDir), Direction> MirrorTable = new()
	{
		// 0° / 180°  →  "\" surface  (N↔W, E↔S)
		{ (0,   Direction.N), Direction.W }, { (0,   Direction.W), Direction.N },
		{ (0,   Direction.E), Direction.S }, { (0,   Direction.S), Direction.E },
		{ (180, Direction.N), Direction.W }, { (180, Direction.W), Direction.N },
		{ (180, Direction.E), Direction.S }, { (180, Direction.S), Direction.E },

		// 90° / 270°  →  "/" surface  (N↔E, W↔S)
		{ (90,  Direction.N), Direction.E }, { (90,  Direction.E), Direction.N },
		{ (90,  Direction.S), Direction.W }, { (90,  Direction.W), Direction.S },
		{ (270, Direction.N), Direction.E }, { (270, Direction.E), Direction.N },
		{ (270, Direction.S), Direction.W }, { (270, Direction.W), Direction.S },

		// 45° / 225°  →  diagonal surface producing cardinal→diagonal outputs
		{ (45,  Direction.N), Direction.NW }, { (45,  Direction.E), Direction.NE },
		{ (45,  Direction.S), Direction.SE }, { (45,  Direction.W), Direction.SW },
		{ (45,  Direction.NW), Direction.N }, { (45,  Direction.NE), Direction.E },
		{ (45,  Direction.SE), Direction.S }, { (45,  Direction.SW), Direction.W },
		{ (225, Direction.N), Direction.NW }, { (225, Direction.E), Direction.NE },
		{ (225, Direction.S), Direction.SE }, { (225, Direction.W), Direction.SW },

		// 135° / 315°  →  other diagonal
		{ (135, Direction.N), Direction.NE }, { (135, Direction.E), Direction.SE },
		{ (135, Direction.S), Direction.SW }, { (135, Direction.W), Direction.NW },
		{ (135, Direction.NE), Direction.N }, { (135, Direction.SE), Direction.E },
		{ (135, Direction.SW), Direction.S }, { (135, Direction.NW), Direction.W },
		{ (315, Direction.N), Direction.NE }, { (315, Direction.E), Direction.SE },
		{ (315, Direction.S), Direction.SW }, { (315, Direction.W), Direction.NW },
	};

	// ── Public entry point ────────────────────────────────────────────────────

	public static SolverResult Solve(Grid grid)
	{
		var visited  = new HashSet<RayKey>();
		var queue    = new Queue<(int x, int y, Direction dir, BeamColor color)>();
		var segments = new List<BeamSegment>();

		// Seed all active sources
		foreach (var comp in grid.AllComponents())
		{
			if (comp.Type == ComponentType.Source && comp.Powered && comp.Color.HasValue)
			{
				var (dx, dy) = comp.Facing.ToVector();
				int sx = comp.Pos.x + dx, sy = comp.Pos.y + dy;
				if (grid.InBounds(sx, sy))
					queue.Enqueue((sx, sy, comp.Facing, comp.Color.Value));
			}
		}

		// Pass 1 – propagate all rays
		while (queue.Count > 0 && segments.Count < MaxSegments)
		{
			var (x, y, dir, color) = queue.Dequeue();
			var key = new RayKey(x, y, dir, color);
			if (visited.Contains(key)) continue;

			var seg = Propagate(grid, x, y, dir, color, visited, queue);
			if (seg != null) segments.Add(seg);
		}

		// Pass 2 – detect intersections (2+ segments sharing a tile, not inside a Mixer)
		var tileToSegs = new Dictionary<(int, int), List<BeamSegment>>();
		foreach (var seg in segments)
			foreach (var tile in seg.Tiles)
			{
				if (!tileToSegs.ContainsKey(tile)) tileToSegs[tile] = new();
				tileToSegs[tile].Add(seg);
			}

		var intersections = new HashSet<(int, int)>();
		foreach (var (tile, segs) in tileToSegs)
		{
			if (segs.Count < 2) continue;
			var tileData = grid.GetTile(tile.Item1, tile.Item2);
			if (tileData?.Occupant?.Type == ComponentType.Mixer) continue;
			intersections.Add(tile);
		}

		// Truncate every segment at its first intersection tile
		foreach (var seg in segments)
		{
			if (seg.Truncated) continue;
			foreach (var tile in seg.Tiles)
			{
				if (!intersections.Contains(tile)) continue;
				int idx = seg.Tiles.IndexOf(tile);
				seg.End       = tile;
				seg.Reason    = TerminationReason.Intersection;
				seg.Truncated = true;
				if (idx >= 0 && idx < seg.Tiles.Count - 1)
					seg.Tiles.RemoveRange(idx + 1, seg.Tiles.Count - idx - 1);
				break;
			}
		}

		// Build result
		var result = new SolverResult();
		result.Segments.AddRange(segments);
		result.Intersections.UnionWith(intersections);

		foreach (var seg in segments)
			foreach (var tile in seg.Tiles)
			{
				if (!result.TileColors.ContainsKey(tile)) result.TileColors[tile] = new();
				result.TileColors[tile].Add(seg.Color);
			}

		// Evaluate receivers — accept light from any direction.
		foreach (var comp in grid.AllComponents())
		{
			if (comp.Type != ComponentType.Receiver || !comp.Color.HasValue) continue;

			bool active = segments.Any(s =>
				!s.Truncated &&
				s.End == comp.Pos &&
				s.Reason == TerminationReason.Receiver &&
				s.Color == comp.Color.Value);

			result.ReceiverStates[comp.Id] = active;
		}

		return result;
	}

	// ── Ray propagation ───────────────────────────────────────────────────────

	private static BeamSegment? Propagate(
		Grid grid, int startX, int startY,
		Direction dir, BeamColor color,
		HashSet<RayKey> visited,
		Queue<(int, int, Direction, BeamColor)> queue)
	{
		var seg = new BeamSegment { Start = (startX, startY), Direction = dir, Color = color };
		var (dx, dy) = dir.ToVector();
		int x = startX, y = startY;

		while (true)
		{
			if (!grid.InBounds(x, y))
			{
				seg.End = (x - dx, y - dy);
				seg.Reason = TerminationReason.GridEdge;
				return seg;
			}

			var key = new RayKey(x, y, dir, color);
			if (visited.Contains(key))
			{
				seg.End = (x, y);
				seg.Reason = TerminationReason.Loop;
				return seg;
			}
			visited.Add(key);
			seg.Tiles.Add((x, y));

			var tile = grid.GetTile(x, y)!;

			// Solid tile blocks
			if (tile.BlocksBeam)
			{
				seg.End = (x, y);
				seg.Reason = TerminationReason.Blocked;
				seg.Tiles.RemoveAt(seg.Tiles.Count - 1); // doesn't include the wall
				seg.End = (x - dx, y - dy);
				return seg;
			}

			// Glass wall: pass through silently
			if (tile.TransparentToBeam) { x += dx; y += dy; continue; }

			// Player body blocks
			if (grid.Player.Pos == (x, y))
			{
				seg.End = (x, y);
				seg.Reason = TerminationReason.Blocked;
				return seg;
			}

			// Crate
			if (tile.Crate is { } crate)
			{
				switch (crate.Variant)
				{
					case CrateVariant.Glass:
						x += dx; y += dy; continue;

					case CrateVariant.ColorBlocking:
						if (crate.Color == color)
						{
							seg.End = (x, y); seg.Reason = TerminationReason.Blocked;
							return seg;
						}
						x += dx; y += dy; continue;

					default: // Standard / ColorLocked: opaque
						seg.End = (x, y); seg.Reason = TerminationReason.Blocked;
						return seg;
				}
			}

			// Component
			if (tile.Occupant is { } comp)
			{
				seg.End = (x, y);
				return ApplyComponent(comp, dir, color, seg, queue, grid);
			}

			// Empty floor – continue
			x += dx; y += dy;
		}
	}

	private static BeamSegment ApplyComponent(
		ComponentData comp, Direction inDir, BeamColor color,
		BeamSegment seg,
		Queue<(int, int, Direction, BeamColor)> queue,
		Grid grid)
	{
		int cx = comp.Pos.x, cy = comp.Pos.y;

		switch (comp.Type)
		{
			// Sources are opaque (back/sides); beam terminates
			case ComponentType.Source:
				seg.Reason = TerminationReason.Blocked; return seg;

			// Absorber: captures and terminates
			case ComponentType.Absorber:
				seg.Reason = TerminationReason.Absorber; return seg;

			// ── Receiver ──────────────────────────────────────────────────────
			case ComponentType.Receiver:
			{
				// Receivers accept light from any direction; only the color must match.
				if (color == comp.Color)
					seg.Reason = TerminationReason.Receiver;
				else
					seg.Reason = TerminationReason.Blocked;
				return seg;
			}

			// ── Mirror ────────────────────────────────────────────────────────
			case ComponentType.Mirror:
			{
				if (MirrorTable.TryGetValue((comp.Rotation, inDir), out var outDir))
				{
					var (ox, oy) = outDir.ToVector();
					if (grid.InBounds(cx + ox, cy + oy))
						queue.Enqueue((cx + ox, cy + oy, outDir, color));
				}
				seg.Reason = TerminationReason.Blocked;
				return seg;
			}

			// ── Filter ────────────────────────────────────────────────────────
			case ComponentType.Filter:
			{
				if (!comp.Color.HasValue) { seg.Reason = TerminationReason.Blocked; return seg; }

				bool passes;
				BeamColor outColor;

				if (!comp.FilterSubtractive)
				{
					passes   = color == comp.Color.Value;
					outColor = color;
				}
				else
				{
					var (fr, fg, fb) = BeamColorHelper.ToRGB(comp.Color.Value);
					var (ir, ig, ib) = BeamColorHelper.ToRGB(color);
					bool r = ir && fr, g = ig && fg, b = ib && fb;
					var result = BeamColorHelper.FromRGB(r, g, b);
					passes   = result.HasValue;
					outColor = result ?? color;
				}

				if (passes)
				{
					var (ox, oy) = inDir.ToVector();
					if (grid.InBounds(cx + ox, cy + oy))
						queue.Enqueue((cx + ox, cy + oy, inDir, outColor));
				}
				seg.Reason = TerminationReason.Blocked;
				return seg;
			}

			// ── Splitter ──────────────────────────────────────────────────────
			case ComponentType.Splitter:
			{
				var inputFace = comp.Facing.Opposite();
				if (inDir.Opposite() != inputFace) { seg.Reason = TerminationReason.Blocked; return seg; }

				// T-Straight: continues forward + perpendicular branch
				var forward = inDir;
				var branch  = inDir.RotateCW();

				foreach (var d in new[] { forward, branch })
				{
					var (ox, oy) = d.ToVector();
					if (grid.InBounds(cx + ox, cy + oy))
						queue.Enqueue((cx + ox, cy + oy, d, color));
				}
				seg.Reason = TerminationReason.Blocked;
				return seg;
			}

			// ── Prism ─────────────────────────────────────────────────────────
			case ComponentType.Prism:
			{
				var inputFace = comp.Facing.Opposite();
				if (inDir.Opposite() != inputFace) { seg.Reason = TerminationReason.Blocked; return seg; }

				var components = BeamColorHelper.SplitComponents(color);
				if (components.Length == 1)
				{
					// Single primary passes straight through
					var (ox, oy) = inDir.ToVector();
					if (grid.InBounds(cx + ox, cy + oy))
						queue.Enqueue((cx + ox, cy + oy, inDir, components[0]));
				}
				else
				{
					// Split: R→forward, G→right, B→left
					Direction[] dirs = { inDir, inDir.RotateCW(), inDir.RotateCCW() };
					for (int i = 0; i < components.Length && i < dirs.Length; i++)
					{
						var (ox, oy) = dirs[i].ToVector();
						if (grid.InBounds(cx + ox, cy + oy))
							queue.Enqueue((cx + ox, cy + oy, dirs[i], components[i]));
					}
				}
				seg.Reason = TerminationReason.Blocked;
				return seg;
			}

			// ── Color Mixer ───────────────────────────────────────────────────
			case ComponentType.Mixer:
			{
				// Mixer combines additively – handled via TileColors in the result.
				// Beams entering input faces are absorbed here; the GameState emits
				// the combined output in a post-solve pass if needed.
				var outputFace = comp.Facing;
				if (inDir.Opposite() == outputFace)
				{
					// Beam hits output face from outside → opaque
					seg.Reason = TerminationReason.Blocked;
				}
				else
				{
					seg.Reason = TerminationReason.MixerInput;
				}
				return seg;
			}

			default:
				seg.Reason = TerminationReason.Blocked;
				return seg;
		}
	}
}
