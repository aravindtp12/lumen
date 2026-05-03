using Godot;
using PRISM.Levels;
using PRISM.Save;

namespace PRISM.Nodes;

public partial class SaveSlotsScene : Node2D
{
    public enum SlotMode { New, Load }

    /// <summary>Set by the calling scene (MainMenu) before scene change.</summary>
    public static SlotMode Mode = SlotMode.New;

    public override void _Ready() => BuildHud();

    private void BuildHud()
    {
        var hud = new CanvasLayer { Name = "HUD", Layer = 10 };
        AddChild(hud);
        hud.AddChild(MenuTheme.FullscreenBg());

        var title = MenuTheme.MakeLabel(
            Mode == SlotMode.New ? "Choose a save slot" : "Load a save",
            36, MenuTheme.TextBright, HorizontalAlignment.Center);
        title.AnchorLeft = 0; title.AnchorRight = 1;
        title.OffsetTop  = 70;
        hud.AddChild(title);

        var col = new VBoxContainer
        {
            AnchorLeft  = 0.5f, AnchorRight  = 0.5f,
            OffsetLeft  = -300, OffsetRight  = 300,
            OffsetTop   = 160,
        };
        col.AddThemeConstantOverride("separation", 14);
        hud.AddChild(col);

        for (int i = 1; i <= SaveSystem.SlotCount; i++)
        {
            int slot   = i;
            var data   = SaveSystem.Load(slot);
            col.AddChild(BuildSlotCard(slot, data));
        }

        var back = MenuTheme.MakeButton("Back", new Vector2(140, 36));
        back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
        var backWrap = new Control
        {
            AnchorTop = 1, AnchorBottom = 1, AnchorLeft = 0, AnchorRight = 0,
            OffsetTop = -56, OffsetLeft = 24,
        };
        backWrap.AddChild(back);
        hud.AddChild(backWrap);
    }

    private Control BuildSlotCard(int slot, SaveData? data)
    {
        var card = new Button
        {
            CustomMinimumSize = new Vector2(600, 86),
            Disabled          = Mode == SlotMode.Load && data == null,
            Alignment         = HorizontalAlignment.Left,
            Text              = BuildCardText(slot, data),
        };
        MenuTheme.StyleButton(card, active: false);

        // Slightly larger font for slot cards
        card.AddThemeFontSizeOverride("font_size", 15);

        card.Pressed += () => OnSlotChosen(slot, data);
        return card;
    }

    private static string BuildCardText(int slot, SaveData? data)
    {
        if (data == null) return $"   Slot {slot}\n   ─ Empty ─";

        string worldDisplay = WorldCatalogue.FindWorld(data.LastWorldId)?.DisplayName
                              ?? data.LastWorldId;
        int totalCompleted = 0;
        foreach (var kv in data.Completed) totalCompleted += kv.Value.Count;

        return $"   Slot {slot}     [ {totalCompleted} solved ]\n" +
               $"   World: {worldDisplay}    Level {data.LastLevelIndex + 1}    " +
               $"Last played: {data.LastPlayedAt}";
    }

    private void OnSlotChosen(int slot, SaveData? data)
    {
        if (Mode == SlotMode.New)
        {
            // Create a fresh save (overwrites if a save was there)
            var fresh = SaveData.NewSlot(slot);
            var firstWorld = WorldCatalogue.Worlds.Count > 0 ? WorldCatalogue.Worlds[0] : null;
            fresh.LastWorldId    = firstWorld?.Id ?? "";
            fresh.LastLevelIndex = 0;
            SaveSystem.Save(fresh);

            GameSession.ActiveSlot        = slot;
            GameSession.Active            = fresh;
            GameSession.CurrentWorldId    = fresh.LastWorldId;
            GameSession.CurrentLevelIndex = fresh.LastLevelIndex;
        }
        else
        {
            if (data == null) return;
            GameSession.ActiveSlot        = slot;
            GameSession.Active            = data;
            GameSession.CurrentWorldId    = data.LastWorldId;
            GameSession.CurrentLevelIndex = data.LastLevelIndex;
        }

        GetTree().ChangeSceneToFile("res://scenes/LevelSelect.tscn");
    }
}
