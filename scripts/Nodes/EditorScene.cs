using Godot;
using PRISM.Core;
using PRISM.Levels;
using System.Collections.Generic;
using System.Linq;

namespace PRISM.Nodes;

/// <summary>
/// Sandbox level editor. Place tiles, components, plates by clicking;
/// the same WorldRenderer + GameState used in gameplay run live, so beams
/// and door/plate animations update as you build.
/// </summary>
public partial class EditorScene : Node2D
{
    public enum Tool { Eraser, PlayerStart, Wall, Door, Source, Receiver, Mirror, Plate }

    private const int   GridW         = 15;
    private const int   GridH         = 15;
    private const float RendererScale = 0.55f;
    private const int   TileSize      = 64; // matches WorldRenderer.TileSize

    /// <summary>Hand-off grid for "play this level" from editor → gameplay.</summary>
    public static Grid? PendingPlayGrid;

    private GameState?     _state;
    private WorldRenderer? _renderer;
    private EditorOverlay? _overlay;

    private Tool      _tool        = Tool.Wall;
    private BeamColor _color       = BeamColor.Red;
    private int       _rotation    = 0;     // 0–315, 45° steps
    private string?   _linkDoorId;           // currently selected door for plate linking

    // Auto-generated id counters
    private int _sourceN, _recvN, _mirrorN, _doorN, _plateN;

    public (int x, int y) Hover { get; private set; } = (-1, -1);
    public Tool   CurrentTool  => _tool;
    public BeamColor CurrentColor => _color;

    private Label?           _statusLabel;
    private Label?           _saveLabel;
    private float            _saveLabelTimer;
    private List<Button>     _toolButtons  = new();
    private List<Button>     _colorButtons = new();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        var rendererPos = new Vector2(
            (1280f - GridW * TileSize * RendererScale) * 0.5f,
            40f + (560f - GridH * TileSize * RendererScale) * 0.5f);

        _renderer = new WorldRenderer
        {
            Position = rendererPos,
            Scale    = new Vector2(RendererScale, RendererScale),
            Name     = "Renderer",
        };
        AddChild(_renderer);

        _overlay = new EditorOverlay
        {
            Position = rendererPos,
            Scale    = new Vector2(RendererScale, RendererScale),
            Name     = "Overlay",
            Editor   = this,
        };
        AddChild(_overlay);

        // Slightly different background than gameplay (overrides WorldRenderer's setting).
        RenderingServer.SetDefaultClearColor(new Color(0.06f, 0.07f, 0.10f));

        var grid = new Grid(GridW, GridH);
        grid.Player.Pos    = (1, 1);
        grid.Player.Facing = Direction.S;
        _state = new GameState(grid);
        _state.StateChanged += UpdateStatusLabel;
        _renderer.State = _state;

        BuildHud();
        UpdateStatusLabel();
        UpdateToolButtonStyles();
        UpdateColorButtonStyles();
    }

    public override void _Process(double delta)
    {
        if (_saveLabelTimer > 0f)
        {
            _saveLabelTimer -= (float)delta;
            if (_saveLabelTimer <= 0f && _saveLabel != null) _saveLabel.Visible = false;
        }
    }

    // ── HUD ───────────────────────────────────────────────────────────────────

    private void BuildHud()
    {
        var hud = new CanvasLayer { Name = "HUD", Layer = 10 };
        AddChild(hud);

        _statusLabel = new Label
        {
            Position      = new Vector2(18, 12),
            LabelSettings = new LabelSettings
            {
                FontSize  = 16,
                FontColor = new Color(0.92f, 0.95f, 1f),
            },
        };
        hud.AddChild(_statusLabel);

        _saveLabel = new Label
        {
            Position      = new Vector2(18, 36),
            LabelSettings = new LabelSettings
            {
                FontSize  = 13,
                FontColor = new Color(0.55f, 1f, 0.65f),
            },
            Visible = false,
        };
        hud.AddChild(_saveLabel);

        // Inventory panel ------------------------------------------------------
        var panel = new Panel
        {
            Position = new Vector2(0, 600),
            Size     = new Vector2(1280, 120),
        };
        var sb = new StyleBoxFlat
        {
            BgColor          = new Color(0.08f, 0.09f, 0.13f, 0.96f),
            BorderColor      = new Color(0.20f, 0.22f, 0.30f),
            BorderWidthTop   = 2,
        };
        panel.AddThemeStyleboxOverride("panel", sb);
        hud.AddChild(panel);

        // Tools row
        var toolRow = new HBoxContainer { Position = new Vector2(18, 8) };
        toolRow.AddThemeConstantOverride("separation", 6);
        panel.AddChild(toolRow);

        AddToolButton(toolRow, "Erase",    Tool.Eraser);
        AddToolButton(toolRow, "Player",   Tool.PlayerStart);
        AddToolButton(toolRow, "Wall",     Tool.Wall);
        AddToolButton(toolRow, "Door",     Tool.Door);
        AddToolButton(toolRow, "Source",   Tool.Source);
        AddToolButton(toolRow, "Receiver", Tool.Receiver);
        AddToolButton(toolRow, "Mirror",   Tool.Mirror);
        AddToolButton(toolRow, "Plate",    Tool.Plate);

        // Color row
        var colorRow = new HBoxContainer { Position = new Vector2(18, 52) };
        colorRow.AddThemeConstantOverride("separation", 6);
        panel.AddChild(colorRow);

        var colorLbl = new Label
        {
            Text          = "Color:",
            LabelSettings = new LabelSettings { FontSize = 13, FontColor = new Color(0.6f, 0.65f, 0.75f) },
        };
        colorRow.AddChild(colorLbl);

        foreach (BeamColor c in System.Enum.GetValues<BeamColor>())
            AddColorButton(colorRow, c);

        // Hint line
        var hint = new Label
        {
            Position      = new Vector2(18, 90),
            LabelSettings = new LabelSettings { FontSize = 12, FontColor = new Color(0.5f, 0.55f, 0.66f) },
            Text          = "L-click place   R-click erase   Scroll/R rotate   C cycle color   TAB cycle door link   S save   P play   ESC main menu",
        };
        panel.AddChild(hint);
    }

    private void AddToolButton(HBoxContainer parent, string label, Tool t)
    {
        var btn = new Button
        {
            Text              = label,
            CustomMinimumSize = new Vector2(86, 36),
        };
        btn.Pressed += () =>
        {
            _tool = t;
            UpdateStatusLabel();
            UpdateToolButtonStyles();
        };
        btn.SetMeta("tool", (int)t);
        parent.AddChild(btn);
        _toolButtons.Add(btn);
    }

    private void AddColorButton(HBoxContainer parent, BeamColor c)
    {
        var godotCol = BeamColorHelper.ToGodotColor(c);
        var btn = new Button
        {
            Text              = c.ToString().Substring(0, 1),
            CustomMinimumSize = new Vector2(34, 28),
        };
        btn.Pressed += () =>
        {
            _color = c;
            UpdateStatusLabel();
            UpdateColorButtonStyles();
        };
        btn.SetMeta("beam_color", (int)c);
        parent.AddChild(btn);
        _colorButtons.Add(btn);
    }

    private void UpdateToolButtonStyles()
    {
        foreach (var b in _toolButtons)
        {
            int t = (int)b.GetMeta("tool");
            bool active = (Tool)t == _tool;
            var sb = new StyleBoxFlat
            {
                BgColor          = active ? new Color(0.30f, 0.85f, 1f, 0.30f) : new Color(0.13f, 0.15f, 0.20f),
                BorderColor      = active ? new Color(0.30f, 0.85f, 1f)         : new Color(0.25f, 0.28f, 0.36f),
                BorderWidthTop   = active ? 2 : 1,
                BorderWidthLeft  = active ? 2 : 1,
                BorderWidthRight = active ? 2 : 1,
                BorderWidthBottom= active ? 2 : 1,
                CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            };
            b.AddThemeStyleboxOverride("normal", sb);
            b.AddThemeStyleboxOverride("hover",  sb);
            b.AddThemeStyleboxOverride("pressed", sb);
        }
    }

    private void UpdateColorButtonStyles()
    {
        foreach (var b in _colorButtons)
        {
            var c = (BeamColor)(int)b.GetMeta("beam_color");
            bool active = c == _color;
            var col = BeamColorHelper.ToGodotColor(c);
            var sb = new StyleBoxFlat
            {
                BgColor          = active ? col : col * 0.55f,
                BorderColor      = active ? Colors.White : new Color(col.R, col.G, col.B, 0.6f),
                BorderWidthTop   = active ? 2 : 1,
                BorderWidthLeft  = active ? 2 : 1,
                BorderWidthRight = active ? 2 : 1,
                BorderWidthBottom= active ? 2 : 1,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            };
            b.AddThemeStyleboxOverride("normal", sb);
            b.AddThemeStyleboxOverride("hover",  sb);
            b.AddThemeStyleboxOverride("pressed", sb);
        }
    }

    private void UpdateStatusLabel()
    {
        if (_statusLabel == null || _state == null) return;
        var dir = DirectionHelper.FromDegrees(_rotation);
        _statusLabel.Text =
            $"Tool: {_tool}    Color: {_color}    Rot: {_rotation}° ({dir})    " +
            $"Link: {_linkDoorId ?? "—"}    Player: ({_state.Grid.Player.Pos.x},{_state.Grid.Player.Pos.y})    " +
            $"Receivers: {_state.ActiveReceivers}/{_state.TotalReceivers}";
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent e)
    {
        if (_state == null || _renderer == null) return;

        if (e is InputEventMouseMotion)
        {
            UpdateHover();
            return;
        }

        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            UpdateHover();
            switch (mb.ButtonIndex)
            {
                case MouseButton.Left:      Place(); break;
                case MouseButton.Right:     Erase(); break;
                case MouseButton.WheelUp:   RotateBy(45);  break;
                case MouseButton.WheelDown: RotateBy(-45); break;
            }
            return;
        }

        if (e is InputEventKey k && k.Pressed && !k.Echo) HandleKey(k);
    }

    private void UpdateHover()
    {
        var local = _renderer!.ToLocal(GetGlobalMousePosition());
        var (tx, ty) = _renderer.ScreenToTile(local);
        Hover = _state!.Grid.InBounds(tx, ty) ? (tx, ty) : (-1, -1);
        _overlay?.QueueRedraw();
    }

    private void HandleKey(InputEventKey k)
    {
        switch (k.Keycode)
        {
            case Key.R:      RotateBy(k.ShiftPressed ? -45 : 45); break;
            case Key.C:      CycleColor();        break;
            case Key.Tab:    CycleLinkDoor();     break;
            case Key.S:      Save();              break;
            case Key.P:      Play();              break;
            case Key.Escape: GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn"); break;
        }
    }

    private void RotateBy(int delta)
    {
        _rotation = ((_rotation + delta) % 360 + 360) % 360;
        UpdateStatusLabel();
        _overlay?.QueueRedraw();
    }

    private void CycleColor()
    {
        var arr = System.Enum.GetValues<BeamColor>();
        int idx = System.Array.IndexOf(arr, _color);
        _color = arr[(idx + 1) % arr.Length];
        UpdateStatusLabel();
        UpdateColorButtonStyles();
    }

    private void CycleLinkDoor()
    {
        var doors = AllDoorIds().ToList();
        if (doors.Count == 0) { _linkDoorId = null; UpdateStatusLabel(); return; }
        int idx = _linkDoorId == null ? -1 : doors.IndexOf(_linkDoorId);
        _linkDoorId = doors[(idx + 1) % doors.Count];
        UpdateStatusLabel();
    }

    private IEnumerable<string> AllDoorIds()
    {
        var grid = _state!.Grid;
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var t = grid.GetTile(x, y);
                if (t?.Type == TileType.Door && !string.IsNullOrEmpty(t.DoorId))
                    yield return t.DoorId;
            }
    }

    // ── Placement ─────────────────────────────────────────────────────────────

    private void Place()
    {
        if (Hover.x < 0 || _state == null) return;
        var grid = _state.Grid;
        var (x, y) = Hover;
        var tile = grid.GetTile(x, y)!;

        switch (_tool)
        {
            case Tool.Eraser:
                Erase();
                return;

            case Tool.PlayerStart:
                if (tile.Type == TileType.Wall || tile.Type == TileType.Pit) return;
                grid.Player.Pos = (x, y);
                break;

            case Tool.Wall:
                ClearOccupants(grid, x, y);
                grid.SetTileType(x, y, TileType.Wall);
                tile.DoorId = null;
                break;

            case Tool.Door:
                ClearOccupants(grid, x, y);
                grid.SetTileType(x, y, TileType.Door);
                _doorN++;
                tile.DoorId   = $"door_{_doorN}";
                tile.DoorOpen = false;
                _linkDoorId   = tile.DoorId;
                break;

            case Tool.Source:
                if (!EnsureFloor(tile, grid, x, y)) return;
                if (tile.Occupant != null) return;
                _sourceN++;
                grid.PlaceComponent(new ComponentData
                {
                    Id        = $"source_{_sourceN}",
                    Type      = ComponentType.Source,
                    Pos       = (x, y),
                    Rotation  = _rotation,
                    Color     = _color,
                    Pushable  = true,
                    Rotatable = true,
                    Powered   = true,
                });
                break;

            case Tool.Receiver:
                if (!EnsureFloor(tile, grid, x, y)) return;
                if (tile.Occupant != null) return;
                _recvN++;
                grid.PlaceComponent(new ComponentData
                {
                    Id        = $"recv_{_recvN}",
                    Type      = ComponentType.Receiver,
                    Pos       = (x, y),
                    Rotation  = _rotation,
                    Color     = _color,
                    Pushable  = false,
                    Rotatable = false,
                });
                break;

            case Tool.Mirror:
                if (!EnsureFloor(tile, grid, x, y)) return;
                if (tile.Occupant != null) return;
                _mirrorN++;
                grid.PlaceComponent(new ComponentData
                {
                    Id        = $"mirror_{_mirrorN}",
                    Type      = ComponentType.Mirror,
                    Pos       = (x, y),
                    Rotation  = _rotation,
                    Pushable  = true,
                    Rotatable = true,
                });
                break;

            case Tool.Plate:
                if (!EnsureFloor(tile, grid, x, y)) return;
                if (tile.PressurePlate != null) return;
                _plateN++;
                grid.SetPressurePlate(x, y, new PressurePlateData
                {
                    Id        = $"plate_{_plateN}",
                    Mode      = PressurePlateMode.Weight,
                    Latching  = false,
                    Logic     = "OR",
                    TargetIds = _linkDoorId != null ? new[] { _linkDoorId } : System.Array.Empty<string>(),
                });
                break;
        }

        _state.Refresh();
    }

    private void Erase()
    {
        if (Hover.x < 0 || _state == null) return;
        var grid = _state.Grid;
        var (x, y) = Hover;
        var tile = grid.GetTile(x, y)!;

        if (tile.Occupant != null)             grid.RemoveComponent(x, y);
        else if (tile.Crate != null)           grid.RemoveCrate(x, y);
        else if (tile.PressurePlate != null)   grid.RemovePressurePlate(x, y);
        else if (tile.Type == TileType.Door)
        {
            string? id = tile.DoorId;
            grid.SetTileType(x, y, TileType.Floor);
            tile.DoorId   = null;
            tile.DoorOpen = false;
            if (id != null)
            {
                foreach (var p in grid.AllPlates())
                    p.TargetIds = p.TargetIds.Where(t => t != id).ToArray();
                if (_linkDoorId == id) _linkDoorId = null;
            }
        }
        else if (tile.Type != TileType.Floor)
        {
            grid.SetTileType(x, y, TileType.Floor);
            tile.DoorId = null;
        }

        _state.Refresh();
    }

    private static bool EnsureFloor(PRISM.Core.TileData tile, Grid grid, int x, int y)
    {
        if (tile.Type == TileType.Wall || tile.Type == TileType.Pit) return false;
        if (tile.Type == TileType.Door)
        {
            grid.SetTileType(x, y, TileType.Floor);
            tile.DoorId = null;
        }
        return true;
    }

    private static void ClearOccupants(Grid grid, int x, int y)
    {
        var tile = grid.GetTile(x, y);
        if (tile == null) return;
        if (tile.Occupant != null)      grid.RemoveComponent(x, y);
        if (tile.Crate != null)         grid.RemoveCrate(x, y);
        if (tile.PressurePlate != null) grid.RemovePressurePlate(x, y);
    }

    // ── Save & Play ───────────────────────────────────────────────────────────

    private void Save()
    {
        if (_state == null) return;
        var path = ProjectSettings.GlobalizePath("res://levels/world1/sandbox.json");
        LevelSerializer.Save(_state.Grid, path, "Sandbox");
        GD.Print($"[Editor] Saved sandbox to {path}");
        if (_saveLabel != null)
        {
            _saveLabel.Text    = $"Saved → {path}";
            _saveLabel.Visible = true;
            _saveLabelTimer    = 3.5f;
        }
    }

    private void Play()
    {
        if (_state == null) return;
        PendingPlayGrid = _state.Grid.Clone();
        GetTree().ChangeSceneToFile("res://scenes/Game.tscn");
    }
}

/// <summary>
/// Thin overlay drawn on top of the WorldRenderer that adds editor-only
/// visuals (grid lines + hover indicator). Lives at the same Position/Scale
/// as the renderer.
/// </summary>
public partial class EditorOverlay : Node2D
{
    public EditorScene? Editor;
    private const int TileSize = 64;
    private const int W = 15;
    private const int H = 15;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (Editor == null) return;

        var gridLine = new Color(1f, 1f, 1f, 0.06f);
        for (int x = 0; x <= W; x++)
            DrawLine(new Vector2(x * TileSize, 0), new Vector2(x * TileSize, H * TileSize), gridLine, 1f);
        for (int y = 0; y <= H; y++)
            DrawLine(new Vector2(0, y * TileSize), new Vector2(W * TileSize, y * TileSize), gridLine, 1f);

        // Border
        DrawRect(new Rect2(0, 0, W * TileSize, H * TileSize),
                 new Color(0.35f, 0.40f, 0.55f, 0.6f), filled: false, width: 2f);

        var (hx, hy) = Editor.Hover;
        if (hx >= 0)
        {
            var rect = new Rect2(hx * TileSize, hy * TileSize, TileSize, TileSize);
            var col  = Editor.CurrentTool == EditorScene.Tool.Eraser
                       ? new Color(1f, 0.50f, 0.50f, 0.95f)
                       : new Color(0.40f, 0.95f, 1f, 0.95f);
            DrawRect(rect.Grow(-2), col, filled: false, width: 2.5f);

            // Soft fill
            var fill = col; fill.A = 0.10f;
            DrawRect(rect.Grow(-2), fill);
        }
    }
}
