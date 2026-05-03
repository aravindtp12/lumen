using System.Collections.Generic;
using Godot;
using PRISM.Save;

namespace PRISM.Nodes;

public partial class MainMenuScene : Node2D
{
    private float _t;
    private readonly List<(Control node, int seed, float amp)> _pulse = new();

    public override void _Ready()
    {
        GameSession.Reset();
        BuildHud();
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        foreach (var (node, seed, amp) in _pulse)
            PulseAnim.ApplyTo(node, _t, seed, amp);
    }

    private void BuildHud()
    {
        var hud = new CanvasLayer { Name = "HUD", Layer = 10 };
        AddChild(hud);
        hud.AddChild(MenuTheme.FullscreenBg());

        var title = MenuTheme.MakeLabel("PRISM", 96, MenuTheme.Title, HorizontalAlignment.Center);
        title.AnchorLeft = 0; title.AnchorRight = 1;
        title.OffsetTop  = 130;
        if (title.LabelSettings is { } ts)
        {
            ts.OutlineColor = new Color(0.4f, 0.32f, 0f, 0.9f);
            ts.OutlineSize  = 4;
        }
        hud.AddChild(title);
        _pulse.Add((title, PulseAnim.SeedOf("title"), 0.05f));

        var subtitle = MenuTheme.MakeLabel("A puzzle of light and reflection",
                                           18, MenuTheme.TextDim, HorizontalAlignment.Center);
        subtitle.AnchorLeft = 0; subtitle.AnchorRight = 1;
        subtitle.OffsetTop  = 250;
        hud.AddChild(subtitle);

        var col = new VBoxContainer
        {
            AnchorLeft  = 0.5f, AnchorRight = 0.5f,
            AnchorTop   = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft  = -130, OffsetRight = 130,
            OffsetTop   = -10, OffsetBottom = 230,
        };
        col.AddThemeConstantOverride("separation", 14);
        hud.AddChild(col);

        var newBtn  = MenuTheme.MakeButton("New Game",  new Vector2(260, 48));
        var loadBtn = MenuTheme.MakeButton("Load Game", new Vector2(260, 48));
        var editBtn = MenuTheme.MakeButton("Editor",    new Vector2(260, 40));
        var quitBtn = MenuTheme.MakeButton("Quit",      new Vector2(260, 40));

        loadBtn.Disabled = !AnySaveExists();

        newBtn.Pressed  += OnNewGame;
        loadBtn.Pressed += OnLoadGame;
        editBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/Editor.tscn");
        quitBtn.Pressed += () => GetTree().Quit();

        col.AddChild(newBtn);
        col.AddChild(loadBtn);
        col.AddChild(editBtn);
        col.AddChild(quitBtn);

        // Subtle desynced breathe across the button stack
        _pulse.Add((newBtn,  1, 0.025f));
        _pulse.Add((loadBtn, 2, 0.025f));
        _pulse.Add((editBtn, 3, 0.025f));
        _pulse.Add((quitBtn, 4, 0.025f));

        var hint = MenuTheme.MakeLabel("Mouse / arrow keys + Enter", 12, MenuTheme.TextFaint,
                                       HorizontalAlignment.Center);
        hint.AnchorLeft = 0; hint.AnchorRight = 1;
        hint.AnchorTop  = 1; hint.AnchorBottom = 1;
        hint.OffsetTop  = -28;
        hud.AddChild(hint);
    }

    private void OnNewGame()
    {
        SaveSlotsScene.Mode = SaveSlotsScene.SlotMode.New;
        GetTree().ChangeSceneToFile("res://scenes/SaveSlots.tscn");
    }

    private void OnLoadGame()
    {
        SaveSlotsScene.Mode = SaveSlotsScene.SlotMode.Load;
        GetTree().ChangeSceneToFile("res://scenes/SaveSlots.tscn");
    }

    private static bool AnySaveExists()
    {
        for (int i = 1; i <= SaveSystem.SlotCount; i++)
            if (SaveSystem.Exists(i)) return true;
        return false;
    }
}
