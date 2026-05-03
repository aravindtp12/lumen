using System;
using Godot;
using PRISM.Core;
using PRISM.Levels;
using PRISM.Save;

namespace PRISM.Nodes;

/// <summary>
/// Root scene for a puzzle level.
/// Owns GameState, handles input, drives WorldRenderer, updates HUD.
/// The level to play comes from <see cref="GameSession"/> (set by the level
/// select screen) or, for editor playtests, from <see cref="EditorScene.PendingPlayGrid"/>.
/// </summary>
public partial class LevelScene : Node2D
{
    private enum UiState { Intro, Playing, Complete }

    private GameState?     _state;
    private WorldRenderer? _renderer;

    private bool    _playingSandbox;     // editor handoff path; no slot/world bookkeeping
    private bool    _markedSolved;       // ensures we mark completion once per visit
    private UiState _ui = UiState.Playing;

    // HUD nodes
    private Label?  _receiverLabel;
    private Label?  _moveLabel;
    private Label?  _winLabel;
    private Label?  _levelLabel;
    private Label?  _hintLabel;
    private Label?  _toastLabel;
    private float   _toastTimer;

    // Overlay nodes — Intro and Complete share the same fullscreen surface
    // and the same delay-then-fade entry animation; only the text differs.
    private Control? _overlayRoot;
    private Panel?   _overlayCard;
    private Label?   _overlayKicker;
    private Label?   _overlayTitle;
    private Label?   _overlaySubtitle;
    private Label?   _overlayPrompt;

    // Entry animation: small delay so the playfield is visible for a moment,
    // then fade the overlay in. Driven from _Process.
    private const float OverlayDelay = 0.7f;
    private const float OverlayFade  = 0.5f;
    private float       _fadeT = -1f; // -1 idle, otherwise seconds since show

    // Input repeat timing
    private Direction _heldDir       = Direction.None;
    private float     _heldTimer;
    private const float RepeatDelay  = 0.22f;
    private const float RepeatRate   = 0.10f;
    private bool        _repeatFired;

    // ── Godot lifecycle ───────────────────────────────────────────────────────

    public override void _Ready()
    {
        BuildSceneGraph();

        // If the editor handed off a grid via PendingPlayGrid, play it directly
        // instead of loading a JSON level. Consumed once.
        if (EditorScene.PendingPlayGrid is { } pending)
        {
            EditorScene.PendingPlayGrid = null;
            _playingSandbox = true;
            _state = new GameState(pending);
            _state.StateChanged += OnStateChanged;
            CenterCamera(_state.Grid);
            if (_levelLabel != null) _levelLabel.Text = "Sandbox";
            OnStateChanged();
            return;
        }

        // Otherwise we expect a session set up by the level select screen.
        if (string.IsNullOrEmpty(GameSession.CurrentWorldId))
        {
            // No session — bail back to the main menu instead of crashing.
            CallDeferred(nameof(GoToMainMenu));
            return;
        }

        LoadLevel(GameSession.CurrentWorldId!, GameSession.CurrentLevelIndex);
    }

    public override void _Process(double delta)
    {
        if (_state == null) return;
        HandleHeldMovement((float)delta);
        UpdateToast((float)delta);
        TickOverlayFade((float)delta);
    }

    // ── Scene graph ───────────────────────────────────────────────────────────

    private void BuildSceneGraph()
    {
        // Camera
        var cam = new Camera2D { Enabled = true };
        AddChild(cam);

        // World renderer
        _renderer = new WorldRenderer { Name = "WorldRenderer" };
        AddChild(_renderer);

        // HUD canvas
        var hud = new CanvasLayer { Name = "HUD", Layer = 10 };
        AddChild(hud);

        // Receiver progress
        _receiverLabel = MakeLabel(hud, "ReceiverLabel", new Vector2(18, 14),
                                   fontSize: 22, color: new Color(0.95f, 0.97f, 1f));
        // Move counter
        _moveLabel = MakeLabel(hud, "MoveLabel", new Vector2(18, 44),
                               fontSize: 15, color: new Color(0.65f, 0.7f, 0.85f));
        // Level name
        _levelLabel = MakeLabel(hud, "LevelLabel", new Vector2(18, 66),
                                fontSize: 13, color: new Color(0.45f, 0.5f, 0.65f));
        // Controls hint
        _hintLabel = MakeLabel(hud, "HintLabel", new Vector2(18, 696),
                               fontSize: 12, color: new Color(0.40f, 0.45f, 0.58f));
        _hintLabel!.Text = "WASD/Arrows Move   R / Shift+R Rotate   Z Undo   Backspace Reset   " +
                           "Ctrl+S Save   N Next   Esc Menu";

        // Save / status toast (top-right)
        _toastLabel = MakeLabel(hud, "ToastLabel", new Vector2(0, 14),
                                fontSize: 14, color: new Color(0.55f, 1f, 0.65f));
        _toastLabel.AnchorLeft = 1; _toastLabel.AnchorRight = 1;
        _toastLabel.OffsetLeft = -260; _toastLabel.OffsetRight = -18;
        _toastLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _toastLabel.Visible = false;

        // Win banner (centered)
        _winLabel = MakeLabel(hud, "WinLabel", new Vector2(0, 320),
                              fontSize: 56, color: new Color(0.95f, 0.88f, 0.30f));
        _winLabel.AnchorLeft = 0; _winLabel.AnchorRight = 1;
        _winLabel.OffsetLeft = 0; _winLabel.OffsetRight = 0;
        _winLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _winLabel.Text    = "◆ SOLVED ◆";
        _winLabel.Visible = false;
        if (_winLabel.LabelSettings is { } ws)
        {
            ws.ShadowColor = new Color(1f, 0.7f, 0f, 0.5f);
            ws.ShadowSize  = 4;
            ws.OutlineColor = new Color(0.4f, 0.25f, 0f, 0.9f);
            ws.OutlineSize  = 3;
        }

        BuildOverlay(hud);
    }

    // ── Intro / Complete overlay ──────────────────────────────────────────────

    private void BuildOverlay(CanvasLayer hud)
    {
        _overlayRoot = new Control
        {
            Name         = "Overlay",
            AnchorLeft   = 0, AnchorRight  = 1,
            AnchorTop    = 0, AnchorBottom = 1,
            MouseFilter  = Control.MouseFilterEnum.Stop,
            Visible      = false,
        };
        hud.AddChild(_overlayRoot);

        // Fullscreen card. Both Intro and Complete use this same surface;
        // they only differ in the text and (slightly) the title font size.
        _overlayCard = new Panel
        {
            AnchorLeft   = 0, AnchorRight  = 1,
            AnchorTop    = 0, AnchorBottom = 1,
        };
        var cardStyle = new StyleBoxFlat
        {
            BgColor     = new Color(0.05f, 0.07f, 0.11f, 1f),
            BorderColor = new Color(0.30f, 0.85f, 1f, 0f),
        };
        _overlayCard.AddThemeStyleboxOverride("panel", cardStyle);
        _overlayRoot.AddChild(_overlayCard);

        var col = new VBoxContainer
        {
            AnchorLeft   = 0, AnchorRight  = 1,
            AnchorTop    = 0, AnchorBottom = 1,
            OffsetLeft   = 40, OffsetRight  = -40,
            OffsetTop    = 60, OffsetBottom = -60,
        };
        col.Alignment = BoxContainer.AlignmentMode.Center;
        col.AddThemeConstantOverride("separation", 18);
        _overlayCard.AddChild(col);

        _overlayKicker   = MakeOverlayLabel("", 18, new Color(0.55f, 0.65f, 0.85f));
        _overlayTitle    = MakeOverlayLabel("", 64, new Color(0.95f, 0.97f, 1f));
        _overlaySubtitle = MakeOverlayLabel("", 18, new Color(0.65f, 0.72f, 0.86f));
        _overlayPrompt   = MakeOverlayLabel("Press Enter to continue", 16, new Color(0.95f, 0.88f, 0.30f));

        col.AddChild(_overlayKicker);
        col.AddChild(_overlayTitle);
        col.AddChild(_overlaySubtitle);

        var spacer = new Control { CustomMinimumSize = new Vector2(0, 16) };
        col.AddChild(spacer);
        col.AddChild(_overlayPrompt);
    }

    private static Label MakeOverlayLabel(string text, int fontSize, Color color)
    {
        return new Label
        {
            Text                = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings       = new LabelSettings { FontSize = fontSize, FontColor = color },
        };
    }

    /// <summary>
    /// Show the level-intro overlay. Lock input immediately, briefly let the
    /// playfield show through, then fade the overlay in.
    /// </summary>
    private void ShowIntro()
    {
        if (_overlayRoot == null || _overlayTitle == null) return;

        var entry = WorldCatalogue.GetLevel(GameSession.CurrentWorldId ?? "",
                                            GameSession.CurrentLevelIndex);
        var world = WorldCatalogue.FindWorld(GameSession.CurrentWorldId ?? "");

        _overlayKicker!.Text   = world == null
            ? $"Level {GameSession.CurrentLevelIndex + 1}"
            : $"{world.DisplayName}  ·  Level {GameSession.CurrentLevelIndex + 1}";
        _overlayTitle.Text     = entry?.DisplayName ?? "Untitled";
        _overlaySubtitle!.Text = "";
        _overlayPrompt!.Text   = "Press Enter to begin";
        if (_overlayTitle.LabelSettings is { } iTs) iTs.FontSize = 64;

        StartOverlay(UiState.Intro);
    }

    /// <summary>
    /// Begin the Complete sequence: lock input now (so the player can't undo
    /// the win), but only fade the fullscreen overlay in after a short delay
    /// so the lit beams remain visible for a moment.
    /// </summary>
    private void BeginComplete()
    {
        if (_overlayRoot == null || _overlayTitle == null) return;

        var entry = WorldCatalogue.GetLevel(GameSession.CurrentWorldId ?? "",
                                            GameSession.CurrentLevelIndex);

        _overlayKicker!.Text   = ShortLevelTag(GameSession.CurrentWorldId,
                                               GameSession.CurrentLevelIndex,
                                               entry?.DisplayName ?? "");
        _overlayTitle.Text     = "◆  Level Complete  ◆";
        _overlaySubtitle!.Text = $"Solved in {_state?.MoveCount ?? 0} moves";
        _overlayPrompt!.Text   = HasNextLevel()
            ? "Press Enter for the next level    ·    Esc for menu"
            : "Press Enter for level select";
        if (_overlayTitle.LabelSettings is { } cTs) cTs.FontSize = 72;

        StartOverlay(UiState.Complete);
    }

    private void StartOverlay(UiState target)
    {
        if (_overlayRoot == null) return;
        _overlayRoot.Modulate = new Color(1f, 1f, 1f, 0f);
        _overlayRoot.Visible  = true;
        _fadeT   = 0f;
        _ui      = target;
        _heldDir = Direction.None;
    }

    private void TickOverlayFade(float dt)
    {
        if (_overlayRoot == null || _fadeT < 0f) return;
        if (_ui != UiState.Intro && _ui != UiState.Complete) return;

        _fadeT += dt;
        float t     = (_fadeT - OverlayDelay) / OverlayFade;
        float alpha = Mathf.Clamp(t, 0f, 1f);
        _overlayRoot.Modulate = new Color(1f, 1f, 1f, alpha);
        if (_fadeT >= OverlayDelay + OverlayFade) _fadeT = -1f;
    }

    private void HideOverlay()
    {
        if (_overlayRoot != null)
        {
            _overlayRoot.Visible  = false;
            _overlayRoot.Modulate = Colors.White;
        }
        _fadeT = -1f;
        _ui    = UiState.Playing;
    }

    /// <summary>
    /// Short tag used on the Complete screen — e.g. "1-3  Beam Splitter"
    /// (world ordinal, level ordinal, level name).
    /// </summary>
    private static string ShortLevelTag(string? worldId, int levelIndex, string levelName)
    {
        int wIdx = worldId == null ? -1 : WorldCatalogue.IndexOfWorld(worldId);
        string wPart = wIdx >= 0 ? (wIdx + 1).ToString() : "?";
        return string.IsNullOrEmpty(levelName)
            ? $"{wPart}-{levelIndex + 1}"
            : $"{wPart}-{levelIndex + 1}  {levelName}";
    }

    private bool HasNextLevel()
    {
        if (GameSession.CurrentWorldId == null) return false;
        var world = WorldCatalogue.FindWorld(GameSession.CurrentWorldId);
        if (world == null) return false;
        if (GameSession.CurrentLevelIndex + 1 < world.Levels.Count) return true;
        int wIdx = WorldCatalogue.IndexOfWorld(world.Id);
        return wIdx >= 0
               && wIdx + 1 < WorldCatalogue.Worlds.Count
               && WorldCatalogue.Worlds[wIdx + 1].Levels.Count > 0;
    }

    private static Label MakeLabel(Node parent, string name, Vector2 pos,
                                   int fontSize = 16, Color? color = null)
    {
        var lbl = new Label
        {
            Name                = name,
            Position            = pos,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var settings = new LabelSettings
        {
            FontSize  = fontSize,
            FontColor = color ?? Colors.White,
        };
        lbl.LabelSettings = settings;
        parent.AddChild(lbl);
        return lbl;
    }

    // ── Level loading ─────────────────────────────────────────────────────────

    private void LoadLevel(string worldId, int index)
    {
        var entry = WorldCatalogue.GetLevel(worldId, index);
        if (entry == null) return;

        GameSession.CurrentWorldId    = worldId;
        GameSession.CurrentLevelIndex = index;
        _markedSolved = false;
        _heldDir      = Direction.None;

        var grid = LevelLoader.Load(entry.ResPath);
        _state   = new GameState(grid);
        _state.StateChanged += OnStateChanged;

        CenterCamera(grid);
        OnStateChanged();
        ShowIntro();
    }

    private void NextLevel()
    {
        if (_playingSandbox || GameSession.CurrentWorldId == null) return;

        var world = WorldCatalogue.FindWorld(GameSession.CurrentWorldId);
        if (world == null) return;

        int next = GameSession.CurrentLevelIndex + 1;
        if (next < world.Levels.Count)
        {
            LoadLevel(world.Id, next);
            return;
        }

        // Roll into the next world if there is one
        int wIdx = WorldCatalogue.IndexOfWorld(world.Id);
        if (wIdx >= 0 && wIdx + 1 < WorldCatalogue.Worlds.Count)
        {
            var nextWorld = WorldCatalogue.Worlds[wIdx + 1];
            if (nextWorld.Levels.Count > 0) LoadLevel(nextWorld.Id, 0);
        }
    }

    private void PrevLevel()
    {
        if (_playingSandbox || GameSession.CurrentWorldId == null) return;

        var world = WorldCatalogue.FindWorld(GameSession.CurrentWorldId);
        if (world == null) return;

        int prev = GameSession.CurrentLevelIndex - 1;
        if (prev >= 0)
        {
            LoadLevel(world.Id, prev);
            return;
        }

        int wIdx = WorldCatalogue.IndexOfWorld(world.Id);
        if (wIdx > 0)
        {
            var prevWorld = WorldCatalogue.Worlds[wIdx - 1];
            if (prevWorld.Levels.Count > 0)
                LoadLevel(prevWorld.Id, prevWorld.Levels.Count - 1);
        }
    }

    private void CenterCamera(Grid grid)
    {
        if (_renderer == null) return;
        float gridW = grid.Width  * WorldRenderer.TileSize;
        float gridH = grid.Height * WorldRenderer.TileSize;
        _renderer.Offset = new Vector2(-gridW * 0.5f, -gridH * 0.5f);
    }

    // ── State update ──────────────────────────────────────────────────────────

    private void OnStateChanged()
    {
        if (_state == null || _renderer == null) return;

        _renderer.State = _state;
        _renderer.QueueRedraw();

        if (_receiverLabel != null)
            _receiverLabel.Text = $"Receivers: {_state.ActiveReceivers} / {_state.TotalReceivers}";

        if (_moveLabel != null)
            _moveLabel.Text = $"Moves: {_state.MoveCount}";

        if (_levelLabel != null)
        {
            if (_playingSandbox)
            {
                _levelLabel.Text = "Sandbox";
            }
            else
            {
                var world = WorldCatalogue.FindWorld(GameSession.CurrentWorldId ?? "");
                var entry = WorldCatalogue.GetLevel(GameSession.CurrentWorldId ?? "",
                                                    GameSession.CurrentLevelIndex);
                string slotTag = GameSession.ActiveSlot is int s ? $"  ·  Slot {s}" : "";
                _levelLabel.Text = world == null || entry == null
                    ? $"Level {GameSession.CurrentLevelIndex + 1}{slotTag}"
                    : $"{world.DisplayName}  ·  Level {entry.Index + 1}: {entry.DisplayName}{slotTag}";
            }
        }

        // Sandbox playtests use the simple banner; catalogue play uses the
        // Complete overlay below.
        if (_winLabel != null)
            _winLabel.Visible = _playingSandbox && _state.Solved;

        // Catalogue path: first time we see Solved during gameplay, mark the
        // save and start the Complete overlay's fade-in sequence.
        if (!_playingSandbox && _ui == UiState.Playing && _state.Solved && !_markedSolved)
        {
            _markedSolved = true;
            MarkLevelCompleted();
            BeginComplete();
        }
        else if (!_state.Solved)
        {
            _markedSolved = false;
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override void _Input(InputEvent e)
    {
        if (_state == null) return;
        if (e is InputEventKey key && key.Pressed && !key.Echo) HandleKeyPress(key);
    }

    private void HandleKeyPress(InputEventKey key)
    {
        // Overlay state: only Enter (advance) and Esc (back) are honoured.
        if (_ui != UiState.Playing)
        {
            switch (key.Keycode)
            {
                case Key.Enter:
                case Key.KpEnter:
                    OnOverlayConfirm();
                    return;
                case Key.Escape:
                    GetTree().ChangeSceneToFile(_playingSandbox
                        ? "res://scenes/Editor.tscn"
                        : "res://scenes/LevelSelect.tscn");
                    return;
            }
            return;
        }

        // Save (Ctrl+S). Must run before the movement check below — bare `S`
        // is "move south" in WASD, so saving is bound to Ctrl+S to avoid
        // colliding with movement.
        if (key.Keycode == Key.S && key.CtrlPressed)
        {
            SaveProgress();
            return;
        }

        // Movement keys with held-repeat via _Process
        var dir = KeyToDirection(key.Keycode);
        if (dir != Direction.None)
        {
            _heldDir     = dir;
            _heldTimer   = 0f;
            _repeatFired = false;
            _state!.MovePlayer(dir);
            return;
        }

        switch (key.Keycode)
        {
            case Key.R when !key.ShiftPressed:
                _state!.Rotate(_state.Grid.Player.Facing);
                break;
            case Key.R when key.ShiftPressed:
                _state!.Rotate(_state.Grid.Player.Facing, counterclockwise: true);
                break;

            case Key.Z:
                _state!.Undo();
                break;

            case Key.Backspace:
                _state!.Reset();
                break;

            case Key.N:
                NextLevel();
                break;

            case Key.B:
                PrevLevel();
                break;

            case Key.E:
                GetTree().ChangeSceneToFile("res://scenes/Editor.tscn");
                break;

            case Key.Escape:
                GetTree().ChangeSceneToFile(_playingSandbox
                    ? "res://scenes/Editor.tscn"
                    : "res://scenes/LevelSelect.tscn");
                break;
        }
    }

    private void OnOverlayConfirm()
    {
        switch (_ui)
        {
            case UiState.Intro:
                HideOverlay();
                break;
            case UiState.Complete:
                if (HasNextLevel()) NextLevel();
                else GetTree().ChangeSceneToFile("res://scenes/LevelSelect.tscn");
                break;
        }
    }

    private void HandleHeldMovement(float dt)
    {
        if (_ui != UiState.Playing) { _heldDir = Direction.None; return; }
        if (_heldDir == Direction.None) return;

        var keycode = DirectionToKey(_heldDir);
        if (!Input.IsKeyPressed(keycode))
        {
            _heldDir = Direction.None;
            return;
        }

        _heldTimer += dt;
        float threshold = _repeatFired ? RepeatRate : RepeatDelay;
        if (_heldTimer >= threshold)
        {
            _heldTimer   = 0f;
            _repeatFired = true;
            _state?.MovePlayer(_heldDir);
        }
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: false } key)
        {
            if (KeyToDirection(key.Keycode) == _heldDir)
                _heldDir = Direction.None;
        }
    }

    private static Direction KeyToDirection(Key k) => k switch
    {
        Key.W or Key.Up    => Direction.N,
        Key.S or Key.Down  => Direction.S,
        Key.A or Key.Left  => Direction.W,
        Key.D or Key.Right => Direction.E,
        _ => Direction.None
    };

    private static Key DirectionToKey(Direction d) => d switch
    {
        Direction.N => Key.W,
        Direction.S => Key.S,
        Direction.W => Key.A,
        Direction.E => Key.D,
        _ => Key.Unknown
    };

    // ── Save & progress ───────────────────────────────────────────────────────

    private void SaveProgress()
    {
        if (_playingSandbox)
        {
            ShowToast("Sandbox — no slot to save to", new Color(1f, 0.7f, 0.5f));
            return;
        }
        if (GameSession.ActiveSlot is not int slot)
        {
            ShowToast("No save slot — start from main menu", new Color(1f, 0.7f, 0.5f));
            return;
        }

        var data = GameSession.Active ?? SaveData.NewSlot(slot);
        data.Slot           = slot;
        data.LastPlayedAt   = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        data.LastWorldId    = GameSession.CurrentWorldId ?? "";
        data.LastLevelIndex = GameSession.CurrentLevelIndex;
        if (_state != null) data.TotalMoves += _state.MoveCount;

        SaveSystem.Save(data);
        GameSession.Active = data;
        ShowToast($"Saved to slot {slot}");
    }

    private void MarkLevelCompleted()
    {
        if (GameSession.Active is not { } data) return;
        if (GameSession.ActiveSlot is not int slot) return;
        if (string.IsNullOrEmpty(GameSession.CurrentWorldId)) return;

        data.Slot         = slot;
        data.LastPlayedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        data.MarkCompleted(GameSession.CurrentWorldId!, GameSession.CurrentLevelIndex);
        SaveSystem.Save(data);
    }

    // ── Toast ─────────────────────────────────────────────────────────────────

    private void ShowToast(string text, Color? color = null)
    {
        if (_toastLabel == null) return;
        _toastLabel.Text    = text;
        _toastLabel.Visible = true;
        _toastTimer         = 2.0f;
        if (_toastLabel.LabelSettings is { } s)
            s.FontColor = color ?? new Color(0.55f, 1f, 0.65f);
    }

    private void UpdateToast(float dt)
    {
        if (_toastLabel == null || !_toastLabel.Visible) return;
        _toastTimer -= dt;
        if (_toastTimer <= 0f) _toastLabel.Visible = false;
    }

    // ── Misc ──────────────────────────────────────────────────────────────────

    private void GoToMainMenu() =>
        GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
}
