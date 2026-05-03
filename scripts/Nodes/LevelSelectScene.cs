using Godot;
using PRISM.Levels;
using PRISM.Save;

namespace PRISM.Nodes;

public partial class LevelSelectScene : Node2D
{
    private string         _selectedWorldId = "";
    private VBoxContainer? _worldsCol;
    private VBoxContainer? _levelsCol;

    public override void _Ready()
    {
        // Default to the saved world if any, otherwise the first world.
        if (!string.IsNullOrEmpty(GameSession.CurrentWorldId)
            && WorldCatalogue.FindWorld(GameSession.CurrentWorldId!) != null)
        {
            _selectedWorldId = GameSession.CurrentWorldId!;
        }
        else if (WorldCatalogue.Worlds.Count > 0)
        {
            _selectedWorldId = WorldCatalogue.Worlds[0].Id;
        }

        BuildHud();
    }

    private void BuildHud()
    {
        var hud = new CanvasLayer { Name = "HUD", Layer = 10 };
        AddChild(hud);
        hud.AddChild(MenuTheme.FullscreenBg());

        string slotTag = GameSession.ActiveSlot is int s ? $"  ·  Slot {s}" : "";
        var title = MenuTheme.MakeLabel($"Select Level{slotTag}",
                                        28, MenuTheme.TextBright, HorizontalAlignment.Center);
        title.AnchorLeft = 0; title.AnchorRight = 1;
        title.OffsetTop  = 36;
        hud.AddChild(title);

        var split = new HBoxContainer
        {
            AnchorLeft   = 0, AnchorRight  = 1,
            AnchorTop    = 0, AnchorBottom = 1,
            OffsetLeft   = 80, OffsetRight  = -80,
            OffsetTop    = 100, OffsetBottom = -90,
        };
        split.AddThemeConstantOverride("separation", 32);
        hud.AddChild(split);

        // ── Worlds column ────────────────────────────────────────────────
        _worldsCol = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        _worldsCol.AddThemeConstantOverride("separation", 8);
        split.AddChild(_worldsCol);

        // ── Levels column ────────────────────────────────────────────────
        _levelsCol = new VBoxContainer();
        _levelsCol.AddThemeConstantOverride("separation", 8);
        split.AddChild(_levelsCol);

        BuildWorldsCol();
        BuildLevelsCol();

        // ── Bottom bar ───────────────────────────────────────────────────
        var back = MenuTheme.MakeButton("Back to Main Menu", new Vector2(180, 36));
        back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
        var bar = new Control
        {
            AnchorTop = 1, AnchorBottom = 1, AnchorLeft = 0, AnchorRight = 0,
            OffsetTop = -56, OffsetLeft = 24,
        };
        bar.AddChild(back);
        hud.AddChild(bar);
    }

    private void BuildWorldsCol()
    {
        if (_worldsCol == null) return;
        foreach (var c in _worldsCol.GetChildren()) c.QueueFree();

        _worldsCol.AddChild(MenuTheme.MakeLabel("Worlds", 18, MenuTheme.TextDim));

        if (WorldCatalogue.Worlds.Count == 0)
        {
            _worldsCol.AddChild(MenuTheme.MakeLabel("(no worlds found)", 13, MenuTheme.TextFaint));
            return;
        }

        foreach (var w in WorldCatalogue.Worlds)
        {
            string id        = w.Id;
            int    completed = GameSession.Active?.CompletedCount(id) ?? 0;
            var    btn       = MenuTheme.MakeButton(
                $"  {w.DisplayName}    {completed}/{w.Levels.Count}",
                new Vector2(260, 44),
                active: id == _selectedWorldId);
            btn.Alignment = HorizontalAlignment.Left;
            btn.Pressed += () =>
            {
                if (_selectedWorldId == id) return;
                _selectedWorldId = id;
                BuildWorldsCol();
                BuildLevelsCol();
            };
            _worldsCol.AddChild(btn);
        }
    }

    private void BuildLevelsCol()
    {
        if (_levelsCol == null) return;
        foreach (var c in _levelsCol.GetChildren()) c.QueueFree();

        var world = WorldCatalogue.FindWorld(_selectedWorldId);
        _levelsCol.AddChild(MenuTheme.MakeLabel(
            world?.DisplayName ?? "(no world)", 18, MenuTheme.TextDim));

        if (world == null) return;

        foreach (var lvl in world.Levels)
        {
            int  idx       = lvl.Index;
            bool completed = GameSession.Active?.IsCompleted(world.Id, idx) ?? false;
            string mark    = completed ? "✓" : "·";

            var btn = MenuTheme.MakeButton(
                $"  {mark}  Level {idx + 1}  —  {lvl.DisplayName}",
                new Vector2(440, 40));
            btn.Alignment = HorizontalAlignment.Left;
            if (completed) StyleCompleted(btn);

            btn.Pressed += () =>
            {
                GameSession.CurrentWorldId    = world.Id;
                GameSession.CurrentLevelIndex = idx;
                GetTree().ChangeSceneToFile("res://scenes/Game.tscn");
            };
            _levelsCol.AddChild(btn);
        }
    }

    private static void StyleCompleted(Button btn)
    {
        var sb = new StyleBoxFlat
        {
            BgColor                 = MenuTheme.CompleteBg,
            BorderColor             = MenuTheme.CompleteBor,
            BorderWidthTop          = 1, BorderWidthBottom = 1,
            BorderWidthLeft         = 1, BorderWidthRight  = 1,
            CornerRadiusTopLeft     = 6, CornerRadiusTopRight    = 6,
            CornerRadiusBottomLeft  = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft       = 14, ContentMarginRight     = 14,
            ContentMarginTop        = 8,  ContentMarginBottom    = 8,
        };
        btn.AddThemeStyleboxOverride("normal", sb);
    }
}
