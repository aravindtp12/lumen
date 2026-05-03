using Godot;
using PRISM.Core;
using System.Collections.Generic;
using System.Linq;

namespace PRISM.Nodes;

/// <summary>
/// Renders the entire game world with smooth animations:
/// player & component movement is lerped; mirror rotations animate;
/// beams pulse and have a flowing energy effect.
/// </summary>
public partial class WorldRenderer : Node2D
{
    public const int TileSize = 64;

    // Set by LevelScene
    public GameState? State  { get; set; }
    public Vector2   Offset  { get; set; } = Vector2.Zero;

    // ── Animation tuning ──────────────────────────────────────────────────────
    private const float MovePxPerSec      = 480f;   // ~7.5 tiles/sec
    private const float RotDegPerSec      = 720f;   // 2 full turns / sec
    private const float DoorOpenPerSec    = 3.6f;   // ~0.28s open/close
    private const float PlateSquishPerSec = 9f;     // ~0.11s squish

    // ── Visual state ──────────────────────────────────────────────────────────
    private Vector2?  _playerVis;
    private Direction _playerFacing = Direction.S;

    private class CompVis
    {
        public Vector2 Pos;
        public float   RotDeg;
        public bool    ReceiverActive;
        public float   ActivatedFor;
    }
    private readonly Dictionary<string, CompVis>     _comps        = new();
    private readonly Dictionary<string, Vector2>     _crates       = new();
    private readonly Dictionary<(int, int), float>   _doorOpenT    = new();
    private readonly Dictionary<(int, int), float>   _plateSquishT = new();

    private float _t;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(new Color(0.04f, 0.05f, 0.09f));
    }

    public override void _Process(double delta)
    {
        if (State == null) return;
        float dt = (float)delta;
        _t += dt;
        UpdateAnimations(dt);
        QueueRedraw();   // redraw every frame for smooth pulses/flow
    }

    private void UpdateAnimations(float dt)
    {
        // Player ───────
        var pTarget = TileCenter(State!.Grid.Player.Pos.x, State.Grid.Player.Pos.y);
        _playerVis  = _playerVis is null
                      ? pTarget
                      : _playerVis.Value.MoveToward(pTarget, MovePxPerSec * dt);
        _playerFacing = State.Grid.Player.Facing;

        // Components ───
        var seenComps = new HashSet<string>();
        foreach (var c in State.Grid.AllComponents())
        {
            if (string.IsNullOrEmpty(c.Id)) continue;
            seenComps.Add(c.Id);

            var target  = TileCenter(c.Pos.x, c.Pos.y);
            float rotT  = c.Rotation;
            bool active = State.LastSolve.ReceiverStates.TryGetValue(c.Id, out var on) && on;

            if (!_comps.TryGetValue(c.Id, out var v))
            {
                _comps[c.Id] = new CompVis
                {
                    Pos = target, RotDeg = rotT,
                    ReceiverActive = active,
                    ActivatedFor = active ? 0.001f : 0f
                };
                continue;
            }

            v.Pos    = v.Pos.MoveToward(target, MovePxPerSec * dt);
            v.RotDeg = LerpAngleDeg(v.RotDeg, rotT, RotDegPerSec * dt);

            if (active && !v.ReceiverActive) v.ActivatedFor = 0.001f;
            else if (active)                 v.ActivatedFor += dt;
            else                             v.ActivatedFor  = 0f;
            v.ReceiverActive = active;
        }
        foreach (var k in _comps.Keys.Where(k => !seenComps.Contains(k)).ToList())
            _comps.Remove(k);

        // Crates ───────
        var seenCrates = new HashSet<string>();
        foreach (var c in State.Grid.AllCrates())
        {
            if (string.IsNullOrEmpty(c.Id)) continue;
            seenCrates.Add(c.Id);
            var target = TileCenter(c.Pos.x, c.Pos.y);
            _crates[c.Id] = _crates.TryGetValue(c.Id, out var v)
                            ? v.MoveToward(target, MovePxPerSec * dt)
                            : target;
        }
        foreach (var k in _crates.Keys.Where(k => !seenCrates.Contains(k)).ToList())
            _crates.Remove(k);

        // Doors & plates — sweep grid once for both
        var seenDoors  = new HashSet<(int, int)>();
        var seenPlates = new HashSet<(int, int)>();
        for (int x = 0; x < State.Grid.Width; x++)
            for (int y = 0; y < State.Grid.Height; y++)
            {
                var tile = State.Grid.GetTile(x, y);
                if (tile == null) continue;

                if (tile.Type == TileType.Door)
                {
                    var key = (x, y);
                    seenDoors.Add(key);
                    float target  = tile.DoorOpen ? 1f : 0f;
                    float current = _doorOpenT.TryGetValue(key, out var dv) ? dv : target;
                    _doorOpenT[key] = Mathf.MoveToward(current, target, DoorOpenPerSec * dt);
                }

                if (tile.PressurePlate is { } plate)
                {
                    var key = (x, y);
                    seenPlates.Add(key);
                    float target  = plate.Active ? 1f : 0f;
                    float current = _plateSquishT.TryGetValue(key, out var sv) ? sv : target;
                    _plateSquishT[key] = Mathf.MoveToward(current, target, PlateSquishPerSec * dt);
                }
            }
        foreach (var k in _doorOpenT.Keys.Where(k => !seenDoors.Contains(k)).ToList())
            _doorOpenT.Remove(k);
        foreach (var k in _plateSquishT.Keys.Where(k => !seenPlates.Contains(k)).ToList())
            _plateSquishT.Remove(k);
    }

    private static float LerpAngleDeg(float current, float target, float maxStep)
    {
        current = ((current % 360f) + 360f) % 360f;
        target  = ((target  % 360f) + 360f) % 360f;
        float diff = ((target - current + 540f) % 360f) - 180f;
        if (Mathf.Abs(diff) <= maxStep) return target;
        return ((current + Mathf.Sign(diff) * maxStep) % 360f + 360f) % 360f;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void _Draw()
    {
        if (State == null) return;

        DrawTiles(State.Grid);
        DrawPressurePlates(State.Grid);
        // Components render before beams so beams visibly reach component
        // surfaces (mirror reflective line, receiver crystal, etc.) instead
        // of being occluded by the component's tile-filling frame.
        DrawComponents(State.Grid);
        DrawBeams(State.LastSolve);
        DrawCratesLayer();
        DrawPlayer();
        DrawPressurePlateActivations(State.Grid);
        DrawIntersections(State.LastSolve);
    }

    // ── Tiles ─────────────────────────────────────────────────────────────────

    private static readonly Color FloorA   = new(0.10f, 0.11f, 0.16f);
    private static readonly Color FloorB   = new(0.12f, 0.13f, 0.18f);
    private static readonly Color FloorEdge= new(0.18f, 0.19f, 0.26f);
    private static readonly Color WallBody = new(0.32f, 0.34f, 0.42f);
    private static readonly Color WallLite = new(0.55f, 0.58f, 0.68f);
    private static readonly Color WallDark = new(0.18f, 0.19f, 0.26f);

    private void DrawTiles(Grid grid)
    {
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var tile = grid.GetTile(x, y)!;
                var rect = TileRect(x, y);

                switch (tile.Type)
                {
                    case TileType.Floor:        DrawFloor(rect, x, y); break;
                    case TileType.Wall:         DrawWall(rect);        break;
                    case TileType.GlassWall:    DrawFloor(rect, x, y); DrawGlass(rect); break;
                    case TileType.Pit:          DrawPit(rect);         break;
                    case TileType.Door:
                        DrawFloor(rect, x, y);
                        float openT = _doorOpenT.TryGetValue((x, y), out var ot)
                                      ? ot : (tile.DoorOpen ? 1f : 0f);
                        DrawDoor(rect, openT, tile.DoorId);
                        break;
                }
            }
    }

    private void DrawFloor(Rect2 rect, int x, int y)
    {
        bool dark = (x + y) % 2 == 0;
        DrawRect(rect, dark ? FloorA : FloorB);
        DrawRect(rect.Grow(-1), FloorEdge, filled: false, width: 1f);
    }

    private void DrawWall(Rect2 rect)
    {
        DrawRect(rect, WallBody);
        // Bevel: top-left highlights, bottom-right shadows
        var p = rect.Position; var sz = rect.Size;
        DrawLine(p, p + new Vector2(sz.X, 0), WallLite, 2f);
        DrawLine(p, p + new Vector2(0, sz.Y), WallLite, 2f);
        DrawLine(p + new Vector2(0, sz.Y), p + sz, WallDark, 2f);
        DrawLine(p + new Vector2(sz.X, 0), p + sz, WallDark, 2f);
        // Subtle inner panel
        DrawRect(rect.Grow(-6), WallBody.Darkened(0.15f));
    }

    private void DrawGlass(Rect2 rect)
    {
        var glass = new Color(0.35f, 0.6f, 0.85f, 0.30f);
        DrawRect(rect.Grow(-3), glass);
        DrawRect(rect.Grow(-3), new Color(0.7f, 0.85f, 1f, 0.6f), filled: false, width: 1f);
        // diagonal sheen
        DrawLine(rect.Position + new Vector2(8, rect.Size.Y - 8),
                 rect.Position + new Vector2(rect.Size.X - 8, 8),
                 new Color(1, 1, 1, 0.18f), 1.5f);
    }

    private void DrawPit(Rect2 rect)
    {
        DrawRect(rect, new Color(0.02f, 0.02f, 0.04f));
        // Inner darker rect
        DrawRect(rect.Grow(-4), Colors.Black);
        // Vignette ring
        DrawRect(rect.Grow(-1), new Color(0.10f, 0.11f, 0.16f), filled: false, width: 1f);
    }

    private void DrawDoor(Rect2 rect, float openT, string? doorId)
    {
        // Jambs (top/bottom recess where the slab retracts into)
        const float jambH = 4f;
        var jambCol      = new Color(0.20f, 0.21f, 0.28f);
        var jambEdge     = new Color(0.42f, 0.44f, 0.55f);
        var topJamb      = new Rect2(rect.Position, new Vector2(rect.Size.X, jambH));
        var bottomJamb   = new Rect2(rect.Position + new Vector2(0, rect.Size.Y - jambH),
                                     new Vector2(rect.Size.X, jambH));
        DrawRect(topJamb,    jambCol);
        DrawRect(bottomJamb, jambCol);
        DrawLine(topJamb.Position + new Vector2(0, jambH),
                 topJamb.Position + new Vector2(rect.Size.X, jambH), jambEdge, 1f);
        DrawLine(bottomJamb.Position,
                 bottomJamb.Position + new Vector2(rect.Size.X, 0), jambEdge, 1f);

        // Slab tinted by the link colour shared with its pressure plate(s),
        // so the player can read the cause/effect at a glance.
        var link     = doorId != null ? LinkColor(doorId) : new Color(0.85f, 0.4f, 0.4f);
        var slabBase = new Color(link.R * 0.32f, link.G * 0.32f, link.B * 0.32f);
        var slabMid  = new Color(link.R * 0.55f, link.G * 0.55f, link.B * 0.55f);

        float slabFullH = rect.Size.Y - jambH * 2f;
        float slabH     = slabFullH * (1f - openT);

        if (slabH > 0.5f)
        {
            var slabPos  = rect.Position + new Vector2(0, jambH);
            var slabRect = new Rect2(slabPos, new Vector2(rect.Size.X, slabH));

            DrawRect(slabRect, slabBase);
            if (slabH > 4f)
            {
                DrawRect(slabRect.Grow(-2), slabMid);
                DrawRect(slabRect.Grow(-2), link, filled: false, width: 2f);
            }

            // Bright leading edge along the bottom — sells the upward motion
            if (openT > 0.02f)
            {
                var lipY = slabPos.Y + slabH - 2f;
                DrawLine(new Vector2(slabPos.X + 3f,                  lipY),
                         new Vector2(slabPos.X + rect.Size.X - 3f,    lipY),
                         new Color(1f, 0.75f, 0.55f, 0.9f), 2f);
            }
        }

        // Open slot glow when (mostly) open
        if (openT > 0.35f)
        {
            float a = Mathf.Min(1f, (openT - 0.35f) / 0.65f) * 0.55f;
            DrawLine(rect.Position + new Vector2(3f, jambH + 1f),
                     rect.Position + new Vector2(rect.Size.X - 3f, jambH + 1f),
                     new Color(0.45f, 0.95f, 0.55f, a), 1.5f);
        }
    }

    // ── Pressure plates ───────────────────────────────────────────────────────

    // Plate ↔ door pairs share a colour drawn from this palette so the
    // cause/effect is visually obvious. Picked deterministically from the
    // link id so the same id always lands on the same hue across redraws.
    private static readonly Color[] LinkPalette =
    {
        new(0.30f, 0.85f, 1.00f),  // cyan
        new(1.00f, 0.55f, 0.85f),  // pink
        new(1.00f, 0.78f, 0.30f),  // amber
        new(0.55f, 1.00f, 0.55f),  // lime
        new(0.85f, 0.55f, 1.00f),  // violet
        new(1.00f, 0.50f, 0.35f),  // coral
    };

    private static Color LinkColor(string? id)
    {
        if (string.IsNullOrEmpty(id)) return new Color(0.95f, 0.85f, 0.25f);
        int h = 0;
        foreach (var ch in id) h = h * 31 + ch;
        int idx = ((h % LinkPalette.Length) + LinkPalette.Length) % LinkPalette.Length;
        return LinkPalette[idx];
    }

    private static Color PlateLinkColor(PressurePlateData p) =>
        LinkColor(p.TargetIds.Length > 0 ? p.TargetIds[0] : null);

    private void DrawPressurePlates(Grid grid)
    {
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var p = grid.GetTile(x, y)?.PressurePlate;
                if (p == null) continue;
                var c = TileCenter(x, y);
                var rect = TileRect(x, y);

                float squish = _plateSquishT.TryGetValue((x, y), out var sv)
                               ? sv : (p.Active ? 1f : 0f);

                var hot  = PlateLinkColor(p);
                var cold = new Color(hot.R * 0.4f, hot.G * 0.4f, hot.B * 0.4f);
                var col  = cold.Lerp(hot, squish);

                // Subtle tinted floor inset so the link colour reads even when
                // an occupant fully covers the well — visible at the corners.
                var inset = new Color(hot.R, hot.G, hot.B, 0.10f);
                DrawRect(rect.Grow(-3f), inset, filled: false, width: 2f);

                // Recessed well — fixed
                DrawCircle(c, 22f, new Color(0.06f, 0.06f, 0.10f));
                DrawCircle(c, 22f, new Color(hot.R, hot.G, hot.B, 0.85f), filled: false, width: 2f);

                // Pressed plate: vertical scale shrinks, bottom stays planted in the well
                const float discR = 14f;
                float yScale = 1f - 0.55f * squish;
                var discC = c + new Vector2(0, discR * (1f - yScale));

                if (squish > 0.02f)
                {
                    var pulse = 0.4f + 0.3f * Mathf.Sin(_t * 5f);
                    DrawEllipse(discC, 18f, 18f * yScale,
                                new Color(col.R, col.G, col.B, pulse * 0.4f * squish));
                }

                DrawEllipse(discC, discR, discR * yScale, col * (0.6f + 0.4f * squish));

                // Inner highlight crease — a thin band at the top of the squished disc
                if (squish > 0.05f)
                {
                    var creaseCol = new Color(1f, 1f, 1f, 0.18f * squish);
                    DrawEllipse(discC + new Vector2(0, -discR * yScale * 0.55f),
                                discR * 0.65f, 1.2f, creaseCol);
                }
            }
    }

    // Drawn after components/crates/player so it reads through any occupant
    // sitting on the plate (otherwise activation is invisible the moment
    // something is on top of the well).
    private void DrawPressurePlateActivations(Grid grid)
    {
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var p = grid.GetTile(x, y)?.PressurePlate;
                if (p == null) continue;
                float squish = _plateSquishT.TryGetValue((x, y), out var sv)
                               ? sv : (p.Active ? 1f : 0f);
                if (squish < 0.02f) continue;

                var link = PlateLinkColor(p);
                var rect = TileRect(x, y);
                float pulse = 0.65f + 0.35f * Mathf.Sin(_t * 4f);

                // Soft inner glow rim — frames the occupant
                DrawRect(rect.Grow(-2f),
                         new Color(link.R, link.G, link.B, 0.28f * squish * pulse),
                         filled: false, width: 5f);

                // Crisp bright border at the tile boundary
                DrawRect(rect.Grow(-1f),
                         new Color(link.R, link.G, link.B, 0.95f * squish),
                         filled: false, width: 2f);

                // Corner brackets — guaranteed visible past any 52×52 crate /
                // 48×48 component / 30×30 player centred in a 64px tile.
                const float cl = 14f;
                const float t  = 2.5f;
                var brk = new Color(link.R, link.G, link.B, squish);
                var p0 = rect.Position;
                var sz = rect.Size;
                DrawLine(p0,                              p0 + new Vector2(cl, 0),       brk, t);
                DrawLine(p0,                              p0 + new Vector2(0, cl),       brk, t);
                DrawLine(p0 + new Vector2(sz.X - cl, 0),  p0 + new Vector2(sz.X, 0),     brk, t);
                DrawLine(p0 + new Vector2(sz.X, 0),       p0 + new Vector2(sz.X, cl),    brk, t);
                DrawLine(p0 + new Vector2(0, sz.Y - cl),  p0 + new Vector2(0, sz.Y),     brk, t);
                DrawLine(p0 + new Vector2(0, sz.Y),       p0 + new Vector2(cl, sz.Y),    brk, t);
                DrawLine(p0 + new Vector2(sz.X, sz.Y - cl), p0 + new Vector2(sz.X, sz.Y),  brk, t);
                DrawLine(p0 + new Vector2(sz.X - cl, sz.Y), p0 + new Vector2(sz.X, sz.Y),  brk, t);
            }
    }

    private void DrawEllipse(Vector2 center, float rx, float ry, Color color)
    {
        const int segs = 28;
        var pts = new Vector2[segs];
        for (int i = 0; i < segs; i++)
        {
            float a = i * Mathf.Tau / segs;
            pts[i] = center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        DrawColoredPolygon(pts, color);
    }

    // ── Beams ─────────────────────────────────────────────────────────────────

    private void DrawBeams(SolverResult solve)
    {
        foreach (var seg in solve.Segments)
        {
            if (seg.Tiles.Count == 0) continue;

            var col  = BeamColorHelper.ToGodotColor(seg.Color);
            var (dx, dy) = seg.Direction.ToVector();
            var dirVec = new Vector2(dx, dy);
            var from = TileCenter(seg.Start.x, seg.Start.y);
            var rawTo = TileCenter(seg.End.x, seg.End.y);

            // Anchor the visible start at the component that emitted this
            // segment. Every segment originates either from a Source's seed
            // emission or from a redirecting component (mirror/splitter/
            // prism/filter); in both cases the originator sits exactly one
            // step behind seg.Start along the beam direction.
            int srcX = seg.Start.x - dx, srcY = seg.Start.y - dy;
            if (State!.Grid.InBounds(srcX, srcY)
                && State.Grid.GetTile(srcX, srcY)?.Occupant is { } srcComp
                && _comps.TryGetValue(srcComp.Id, out var srcVis))
            {
                from = srcComp.Type == ComponentType.Source
                       ? srcVis.Pos + dirVec * 24f   // emitter cone tip
                       : srcVis.Pos;                  // surface / center
            }

            // Anchor the visible end at the obstacle that actually stops the
            // beam, so beams meet a real surface instead of stopping at a
            // floor-tile center. Three cases, in priority order:
            //   1) seg.End holds a component → anchor at component centre
            //      (beams render over components, reaching their surfaces).
            //   2) seg.End holds a crate → anchor at the crate's incident
            //      face/corner (52×52 body, half-size 26).
            //   3) The tile one step *ahead* of seg.End blocks the beam
            //      (wall, closed door, pit, or any future BlocksBeam tile).
            //      The solver places seg.End on the floor *before* such a
            //      blocker, so extend visually to that blocker's near edge.
            var endTile = State.Grid.GetTile(seg.End.x, seg.End.y);
            if (endTile?.Occupant is { } endComp
                && _comps.TryGetValue(endComp.Id, out var endVis))
            {
                rawTo = endVis.Pos;
            }
            else if (endTile?.Crate is { } endCrate
                     && _crates.TryGetValue(endCrate.Id, out var cratePos))
            {
                rawTo = cratePos - dirVec * 26f;
            }
            else
            {
                int nextX = seg.End.x + dx, nextY = seg.End.y + dy;
                if (State.Grid.InBounds(nextX, nextY)
                    && State.Grid.GetTile(nextX, nextY)?.BlocksBeam == true)
                {
                    rawTo = TileCenter(nextX, nextY) - dirVec * (TileSize / 2f);
                }
                // else: grid edge / loop / intersection — keep tile centre.
            }

            // Project the endpoint onto the ray `from + t * dirVec` so the
            // rendered beam stays exactly along seg.Direction even while the
            // originator is mid-animation. Without this, anchoring `from` to
            // a moving component (e.g., a mirror being pushed) while leaving
            // the far end at the grid endpoint causes the segment to swing
            // through intermediate angles during the lerp — visible as the
            // reflected leg tilting off-perpendicular.
            float lenSq = dirVec.LengthSquared();
            float proj = lenSq > 0 ? (rawTo - from).Dot(dirVec) / lenSq : 0f;
            var to = from + dirVec * Mathf.Max(0f, proj);

            float pulse = 0.85f + 0.15f * Mathf.Sin(_t * 5f);

            // Layered glow → core
            DrawLine(from, to, new Color(col.R, col.G, col.B, 0.06f), 24f);
            DrawLine(from, to, new Color(col.R, col.G, col.B, 0.16f), 14f);
            DrawLine(from, to, new Color(col.R, col.G, col.B, 0.55f * pulse), 6f);
            DrawLine(from, to, new Color(col.R, col.G, col.B, 0.95f * pulse), 2.5f);

            // Bright center
            var bright = col.Lerp(Colors.White, 0.55f);
            DrawLine(from, to, new Color(bright.R, bright.G, bright.B, 0.95f), 1f);

            // Flowing energy particles
            float length = (to - from).Length();
            if (length > 4f)
            {
                var dir = (to - from).Normalized();
                const float spacing = 22f;
                int particles = (int)(length / spacing);
                float flow = (_t * 70f) % spacing;
                for (int i = 0; i < particles; i++)
                {
                    float t = i * spacing + flow;
                    if (t > length) continue;
                    var p = from + dir * t;
                    var pcol = bright;
                    pcol.A = 0.85f;
                    DrawCircle(p, 1.6f, pcol);
                }
            }
        }
    }

    // ── Components ────────────────────────────────────────────────────────────

    private void DrawComponents(Grid grid)
    {
        foreach (var comp in grid.AllComponents())
        {
            if (!_comps.TryGetValue(comp.Id, out var v)) continue;

            // Constant Balatro-style breathe; phase seeded by component id so
            // adjacent components don't pulse in lockstep.
            float pulse = PulseAnim.Scale(_t, comp.Id);
            DrawSetTransformMatrix(PulseAnim.ScaleAround(v.Pos, pulse));

            switch (comp.Type)
            {
                case ComponentType.Source:    DrawSource(v.Pos, comp, v.RotDeg);              break;
                case ComponentType.Receiver:  DrawReceiver(v.Pos, comp, v.ReceiverActive, v.ActivatedFor); break;
                case ComponentType.Mirror:    DrawMirror(v.Pos, v.RotDeg);                    break;
                case ComponentType.Filter:    DrawFilter(v.Pos, comp, v.RotDeg);              break;
                case ComponentType.Splitter:  DrawSplitter(v.Pos, comp, v.RotDeg);            break;
                case ComponentType.Prism:     DrawPrism(v.Pos, comp, v.RotDeg);               break;
                case ComponentType.Mixer:     DrawMixer(v.Pos, v.RotDeg);                     break;
                case ComponentType.Absorber:  DrawAbsorber(v.Pos, comp, v.RotDeg);            break;
            }

            if (!comp.Pushable) DrawLockBadge(v.Pos);

            DrawSetTransformMatrix(Transform2D.Identity);
        }
    }

    // -- Source ---------------------------------------------------------------

    private void DrawSource(Vector2 c, ComponentData comp, float rot)
    {
        if (!comp.Color.HasValue) return;
        var col = BeamColorHelper.ToGodotColor(comp.Color.Value);
        float pulse = 0.7f + 0.3f * Mathf.Sin(_t * 4f);

        // Halo
        DrawCircle(c, 30f, new Color(col.R, col.G, col.B, 0.10f * pulse));
        DrawCircle(c, 22f, new Color(col.R, col.G, col.B, 0.20f * pulse));

        // Housing
        DrawCircle(c, 18f, new Color(0.05f, 0.05f, 0.09f));
        DrawCircle(c, 18f, col * 0.35f, filled: false, width: 2f);

        // Inner fluxes
        DrawCircle(c, 12f, col * 0.45f);
        var core = col; core.A = pulse;
        DrawCircle(c, 7f, core);
        DrawCircle(c, 3f, Colors.White);

        // Emitter cone in facing direction
        var dir = DirectionHelper.FromDegrees(Mathf.RoundToInt(rot)).ToVector();
        var fv  = new Vector2(dir.dx, dir.dy);
        if (fv.LengthSquared() > 0)
        {
            var perp  = new Vector2(-fv.Y, fv.X);
            var tip   = c + fv * 24f;
            var bs    = c + fv * 14f;
            var emit  = col; emit.A = 1f;
            DrawColoredPolygon(new[] { tip, bs + perp * 5.5f, bs - perp * 5.5f }, emit);
            // Bright tip
            DrawCircle(tip, 1.5f, Colors.White);
        }
    }

    // -- Receiver -------------------------------------------------------------

    private void DrawReceiver(Vector2 c, ComponentData comp, bool active, float activatedFor)
    {
        if (!comp.Color.HasValue) return;
        var col = BeamColorHelper.ToGodotColor(comp.Color.Value);
        const float s = 22f;

        // Diamond crystal points (top, right, bottom, left)
        var pts = new[]
        {
            c + new Vector2(0, -s), c + new Vector2(s, 0),
            c + new Vector2(0,  s), c + new Vector2(-s, 0),
        };

        // Outer glow when active
        if (active)
        {
            for (int i = 4; i >= 1; i--)
            {
                float scale = 1f + i * 0.18f;
                var glowPts = pts.Select(p => c + (p - c) * scale).ToArray();
                var gcol = col; gcol.A = 0.10f / i;
                DrawColoredPolygon(glowPts, gcol);
            }
        }

        // Body
        var bodyCol = active ? col : col * 0.20f;
        bodyCol.A   = 1f;
        DrawColoredPolygon(pts, bodyCol);

        // Inner facet
        var inner = pts.Select(p => c + (p - c) * 0.55f).ToArray();
        DrawColoredPolygon(inner, active ? Colors.White : col.Lerp(Colors.Black, 0.55f));

        // Edge lines
        var edge = active ? Colors.White : new Color(0.4f, 0.42f, 0.5f);
        for (int i = 0; i < 4; i++)
            DrawLine(pts[i], pts[(i + 1) % 4], edge, 2f);

        // Activation ring expansion (one-shot)
        if (active && activatedFor < 0.5f)
        {
            float t = activatedFor / 0.5f;
            float radius = 26f + t * 32f;
            var ring = col; ring.A = (1f - t) * 0.9f;
            DrawArc(c, radius, 0, Mathf.Tau, 32, ring, 3f);
        }

        // Sensor face notch
        var sv  = comp.Facing.Opposite().ToVector();
        var pos = c + new Vector2(sv.dx, sv.dy) * 26f;
        DrawCircle(pos, 4f, active ? Colors.White : new Color(0.4f, 0.4f, 0.5f));
    }

    // -- Mirror ---------------------------------------------------------------

    // Back-of-mirror direction for each cardinal/diagonal rotation (matches
    // ◣◤◥◢ symbols from the design doc — back is the FILLED side).
    private static Vector2 BackDirAt(int snap) => snap switch
    {
        0   => new Vector2(-1,  1).Normalized(), //  ◣ SW
        45  => new Vector2( 0,  1),              //     S
        90  => new Vector2(-1, -1).Normalized(), //  ◤ NW
        135 => new Vector2(-1,  0),              //     W
        180 => new Vector2( 1, -1).Normalized(), //  ◥ NE
        225 => new Vector2( 0, -1),              //     N
        270 => new Vector2( 1,  1).Normalized(), //  ◢ SE
        315 => new Vector2( 1,  0),              //     E
        _   => new Vector2(-1,  1).Normalized(),
    };

    private static Vector2 InterpolatedBackDir(float visRot)
    {
        float r = ((visRot % 360f) + 360f) % 360f;
        int low  = ((int)Mathf.Floor(r / 45f)) * 45;
        int high = (low + 45) % 360;
        float t  = (r - low) / 45f;
        var a = BackDirAt(low);
        var b = BackDirAt(high);
        // Slerp for proper rotation
        float angle = a.AngleTo(b);
        return a.Rotated(angle * t);
    }

    private void DrawMirror(Vector2 c, float visRot)
    {
        const float s = 28f;
        var rect  = new Rect2(c - new Vector2(s, s), new Vector2(s * 2, s * 2));

        // Tile background (frame body)
        DrawRect(rect, new Color(0.10f, 0.12f, 0.16f));

        // Reflective line direction (in screen coords): angle = 45° − visRot
        float lineAngleRad = (45f - visRot) * Mathf.Pi / 180f;
        var   lineDir      = new Vector2(Mathf.Cos(lineAngleRad), Mathf.Sin(lineAngleRad));

        // Clip line to tile bounds
        float tx = lineDir.X != 0 ? s / Mathf.Abs(lineDir.X) : float.MaxValue;
        float ty = lineDir.Y != 0 ? s / Mathf.Abs(lineDir.Y) : float.MaxValue;
        float tt = Mathf.Min(tx, ty);
        var lineFrom = c - lineDir * tt;
        var lineTo   = c + lineDir * tt;

        // Black "back" polygon — covers the half of the tile on the back side
        var backDir = InterpolatedBackDir(visRot);
        var corners = new[]
        {
            c + new Vector2(-s, -s), c + new Vector2( s, -s),
            c + new Vector2( s,  s), c + new Vector2(-s,  s),
        };
        var backPts = new List<Vector2> { lineFrom, lineTo };
        foreach (var k in corners)
            if ((k - c).Dot(backDir) > 0.001f) backPts.Add(k);
        // Sort vertices angularly around centroid for a valid convex polygon
        if (backPts.Count >= 3)
        {
            var centroid = Vector2.Zero;
            foreach (var p in backPts) centroid += p;
            centroid /= backPts.Count;
            backPts.Sort((a, b) => (a - centroid).Angle().CompareTo((b - centroid).Angle()));
            DrawColoredPolygon(backPts.ToArray(), new Color(0.01f, 0.01f, 0.02f));
        }

        // Frame outline
        DrawRect(rect, new Color(0.32f, 0.36f, 0.46f), filled: false, width: 2f);

        // Reflective surface — layered glow into a bright core
        DrawLine(lineFrom, lineTo, new Color(0.7f, 0.85f, 1f, 0.18f), 14f);
        DrawLine(lineFrom, lineTo, new Color(0.7f, 0.85f, 1f, 0.45f), 7f);
        DrawLine(lineFrom, lineTo, new Color(0.92f, 0.96f, 1f),       2.6f);
        DrawLine(lineFrom, lineTo, Colors.White,                      1f);

        // Front-side sparkle
        var frontDir = -backDir;
        DrawCircle(c + frontDir * 10f, 1.5f, new Color(1, 1, 1, 0.55f));
    }

    // -- Filter ---------------------------------------------------------------

    private void DrawFilter(Vector2 c, ComponentData comp, float rot)
    {
        if (!comp.Color.HasValue) return;
        var col = BeamColorHelper.ToGodotColor(comp.Color.Value);

        // Frame
        DrawRect(new Rect2(c - new Vector2(24, 24), new Vector2(48, 48)),
                 new Color(0.06f, 0.07f, 0.10f));
        DrawRect(new Rect2(c - new Vector2(24, 24), new Vector2(48, 48)),
                 col * 0.6f, filled: false, width: 1.5f);

        // Translucent filter glass
        var glass = col; glass.A = 0.55f;
        DrawRect(new Rect2(c - new Vector2(10, 22), new Vector2(20, 44)), glass);
        DrawRect(new Rect2(c - new Vector2(10, 22), new Vector2(20, 44)),
                 Colors.White * 0.7f, filled: false, width: 1.5f);
        // Diagonal highlight
        DrawLine(c + new Vector2(-9, -20), c + new Vector2(-9, 20),
                 new Color(1, 1, 1, 0.20f), 2f);
    }

    // -- Splitter -------------------------------------------------------------

    private void DrawSplitter(Vector2 c, ComponentData comp, float rot)
    {
        var rect = new Rect2(c - new Vector2(24, 24), new Vector2(48, 48));
        DrawRect(rect, new Color(0.10f, 0.10f, 0.06f));
        DrawRect(rect, new Color(0.85f, 0.80f, 0.40f), filled: false, width: 2f);

        // T pattern oriented to facing
        var f = DirectionHelper.FromDegrees(Mathf.RoundToInt(rot));
        var fv = new Vector2(f.ToVector().dx, f.ToVector().dy);
        var perp = new Vector2(-fv.Y, fv.X);

        var prismLine = new Color(0.95f, 0.85f, 0.35f);
        DrawLine(c - fv * 18f, c + fv * 18f, prismLine, 2.5f);
        DrawLine(c - perp * 18f, c + perp * 18f, prismLine, 2.5f);
        DrawCircle(c, 4f, Colors.White);
    }

    // -- Prism ----------------------------------------------------------------

    private void DrawPrism(Vector2 c, ComponentData comp, float rot)
    {
        // Triangle pointing in facing direction
        var f = DirectionHelper.FromDegrees(Mathf.RoundToInt(rot));
        var fv = new Vector2(f.ToVector().dx, f.ToVector().dy);
        var perp = new Vector2(-fv.Y, fv.X);

        var tip  = c + fv * 26f;
        var b1   = c - fv * 14f + perp * 22f;
        var b2   = c - fv * 14f - perp * 22f;
        var bg   = new Color(0.20f, 0.10f, 0.40f);
        DrawColoredPolygon(new[] { tip, b1, b2 }, bg);

        // Edge highlight
        DrawLine(tip, b1, new Color(0.85f, 0.7f, 1f), 2f);
        DrawLine(tip, b2, new Color(0.85f, 0.7f, 1f), 2f);
        DrawLine(b1,  b2, new Color(0.5f, 0.4f, 0.7f), 2f);

        // Tiny RGB indicators emanating
        DrawCircle(tip + fv * 3f - perp * 6f, 1.5f, BeamColorHelper.ToGodotColor(BeamColor.Red));
        DrawCircle(tip + fv * 3f,             1.5f, BeamColorHelper.ToGodotColor(BeamColor.Green));
        DrawCircle(tip + fv * 3f + perp * 6f, 1.5f, BeamColorHelper.ToGodotColor(BeamColor.Blue));
    }

    // -- Mixer ----------------------------------------------------------------

    private void DrawMixer(Vector2 c, float rot)
    {
        DrawCircle(c, 26f, new Color(0.06f, 0.07f, 0.10f));
        for (int i = 1; i <= 3; i++)
            DrawCircle(c, 26f - i * 4f, new Color(0.18f, 0.18f, 0.25f), filled: false, width: 1f);
        DrawCircle(c, 8f, new Color(0.6f, 0.6f, 0.85f));
    }

    // -- Absorber -------------------------------------------------------------

    private void DrawAbsorber(Vector2 c, ComponentData comp, float rot)
    {
        var rect = new Rect2(c - new Vector2(22, 22), new Vector2(44, 44));
        DrawRect(rect, new Color(0.06f, 0.04f, 0.10f));
        DrawRect(rect, new Color(0.6f, 0.3f, 0.7f), filled: false, width: 2f);

        if (comp.StoredColor.HasValue)
        {
            var sc = BeamColorHelper.ToGodotColor(comp.StoredColor.Value);
            DrawCircle(c, 12f, sc);
            DrawCircle(c, 6f, Colors.White);
        }
        else
        {
            DrawCircle(c, 12f, new Color(0.15f, 0.10f, 0.20f));
        }
    }

    // -- Lock badge -----------------------------------------------------------

    private void DrawLockBadge(Vector2 c)
    {
        var p = c + new Vector2(20, -20);
        DrawCircle(p, 6f, new Color(0.05f, 0.05f, 0.08f));
        DrawCircle(p, 6f, new Color(0.85f, 0.75f, 0.30f), filled: false, width: 1.5f);
        DrawRect(new Rect2(p + new Vector2(-2, -1), new Vector2(4, 4)),
                 new Color(0.85f, 0.75f, 0.30f));
    }

    // ── Crates ────────────────────────────────────────────────────────────────

    private void DrawCratesLayer()
    {
        foreach (var crate in State!.Grid.AllCrates())
        {
            if (!_crates.TryGetValue(crate.Id, out var pos)) continue;

            Color body = crate.Variant switch
            {
                CrateVariant.Glass         => new Color(0.45f, 0.75f, 1f, 0.5f),
                CrateVariant.ColorLocked   => crate.Color.HasValue
                                              ? BeamColorHelper.ToGodotColor(crate.Color.Value) * 0.6f
                                              : new Color(0.4f, 0.4f, 0.4f),
                CrateVariant.ColorBlocking => crate.Color.HasValue
                                              ? BeamColorHelper.ToGodotColor(crate.Color.Value) * 0.45f
                                              : Colors.DimGray,
                _                          => new Color(0.40f, 0.32f, 0.22f)
            };
            body.A = 1f;

            float pulse = PulseAnim.Scale(_t, crate.Id);
            DrawSetTransformMatrix(PulseAnim.ScaleAround(pos, pulse));

            var rect = new Rect2(pos - new Vector2(26, 26), new Vector2(52, 52));
            DrawRect(rect, body);
            DrawRect(rect, body.Lightened(0.25f), filled: false, width: 2f);
            // Wood-grain X
            DrawLine(rect.Position, rect.Position + rect.Size, body.Darkened(0.20f), 1f);
            DrawLine(rect.Position + new Vector2(rect.Size.X, 0),
                     rect.Position + new Vector2(0, rect.Size.Y), body.Darkened(0.20f), 1f);

            DrawSetTransformMatrix(Transform2D.Identity);
        }
    }

    // ── Player (robot) ────────────────────────────────────────────────────────

    private void DrawPlayer()
    {
        if (_playerVis is null) return;
        var c = _playerVis.Value;

        // Bob
        float bob = Mathf.Sin(_t * 3.5f) * 1.5f;
        c += new Vector2(0, bob);

        // Soft shadow — drawn outside the breathe transform so it stays
        // grounded while the body scales above it.
        DrawCircle(c + new Vector2(0, 24f - bob), 16f, new Color(0, 0, 0, 0.42f));

        // Subtle breathe (smaller amp than the rest — the player already has
        // bob + eye + antenna pulses, so layering more on top gets noisy).
        float pulse = PulseAnim.Scale(_t, "_player", amp: 0.025f);
        DrawSetTransformMatrix(PulseAnim.ScaleAround(c, pulse));

        // Palette
        var bodyCol  = new Color(0.22f, 0.24f, 0.30f);
        var bodyEdge = new Color(0.45f, 0.50f, 0.62f);
        var bodyDark = new Color(0.10f, 0.11f, 0.15f);
        var accent   = new Color(0.30f, 0.92f, 1.00f);

        // Body — rounded rect via central rect + 4 corner discs
        DrawRect(new Rect2(c + new Vector2(-15, -8), new Vector2(30, 24)), bodyCol);
        DrawRect(new Rect2(c + new Vector2(-13, -10), new Vector2(26, 28)), bodyCol);
        DrawCircle(c + new Vector2(-15, -8), 4f, bodyCol);
        DrawCircle(c + new Vector2( 15, -8), 4f, bodyCol);
        DrawCircle(c + new Vector2(-15, 16), 4f, bodyCol);
        DrawCircle(c + new Vector2( 15, 16), 4f, bodyCol);

        // Body outline (top highlight)
        DrawLine(c + new Vector2(-13, -10), c + new Vector2(13, -10), bodyEdge, 1.5f);

        // Visor / head panel — dark recess
        DrawRect(new Rect2(c + new Vector2(-11, -7), new Vector2(22, 11)), bodyDark);
        DrawRect(new Rect2(c + new Vector2(-11, -7), new Vector2(22, 11)),
                 bodyEdge * 0.7f, filled: false, width: 1f);

        // Eye — points in facing direction (cyan glow)
        var fv     = _playerFacing.ToVector();
        var eyeOff = new Vector2(fv.dx * 4f, fv.dy * 1.5f - 1.5f);
        var eyePos = c + eyeOff;

        // Glow halo (pulsing)
        float ePulse = 0.7f + 0.3f * Mathf.Sin(_t * 4.5f);
        DrawCircle(eyePos, 9f, new Color(accent.R, accent.G, accent.B, 0.18f * ePulse));
        DrawCircle(eyePos, 6f, new Color(accent.R, accent.G, accent.B, 0.35f * ePulse));
        DrawCircle(eyePos, 4f, accent);
        DrawCircle(eyePos, 2f, Colors.White);

        // Antenna with blinking tip
        DrawLine(c + new Vector2(0, -10), c + new Vector2(0, -18), bodyEdge, 1.5f);
        float ant = 0.6f + 0.4f * Mathf.Sin(_t * 6f);
        DrawCircle(c + new Vector2(0, -18), 2.5f, new Color(1f, 0.55f, 0.30f, ant));

        // Side vent details
        DrawRect(new Rect2(c + new Vector2(-15, 0), new Vector2(2, 7)), accent * 0.5f);
        DrawRect(new Rect2(c + new Vector2( 13, 0), new Vector2(2, 7)), accent * 0.5f);

        // Tread bases
        DrawRect(new Rect2(c + new Vector2(-13, 14), new Vector2(8, 4)), bodyDark);
        DrawRect(new Rect2(c + new Vector2(  5, 14), new Vector2(8, 4)), bodyDark);

        DrawSetTransformMatrix(Transform2D.Identity);
    }

    // ── Intersection sparks ───────────────────────────────────────────────────

    private void DrawIntersections(SolverResult solve)
    {
        foreach (var tile in solve.Intersections)
        {
            var c = TileCenter(tile.Item1, tile.Item2);
            float rot = (_t * 90f) % 360f;
            float r1 = 6f + Mathf.Sin(_t * 7f) * 2f;
            float r2 = 10f + Mathf.Sin(_t * 7f + 1f) * 2f;

            // Bright burst (rotating cross)
            for (int i = 0; i < 4; i++)
            {
                float a = (rot + i * 45f) * Mathf.Pi / 180f;
                var v = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                DrawLine(c - v * r2, c + v * r2, new Color(1, 1, 1, 0.6f), 1.5f);
            }
            DrawCircle(c, r1, new Color(1, 1, 1, 0.85f));
            DrawCircle(c, 3f, Colors.White);
        }
    }

    // ── Coordinates ───────────────────────────────────────────────────────────

    public Vector2 TileCenter(int x, int y) =>
        Offset + new Vector2(x * TileSize + TileSize * 0.5f,
                             y * TileSize + TileSize * 0.5f);

    private Rect2 TileRect(int x, int y) =>
        new(Offset + new Vector2(x * TileSize, y * TileSize),
            new Vector2(TileSize, TileSize));

    public (int x, int y) ScreenToTile(Vector2 pos)
    {
        var local = pos - Offset;
        return ((int)(local.X / TileSize), (int)(local.Y / TileSize));
    }
}
