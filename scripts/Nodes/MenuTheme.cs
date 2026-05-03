using Godot;

namespace PRISM.Nodes;

// Shared visual helpers for the menu screens (MainMenu / SaveSlots / LevelSelect)
// so all three look like one application rather than three different prototypes.
internal static class MenuTheme
{
    public static readonly Color Bg          = new(0.04f, 0.05f, 0.08f);
    public static readonly Color Panel       = new(0.10f, 0.13f, 0.18f);
    public static readonly Color PanelHover  = new(0.16f, 0.20f, 0.27f);
    public static readonly Color Accent      = new(0.30f, 0.85f, 1.00f);
    public static readonly Color Title       = new(0.95f, 0.92f, 0.40f);
    public static readonly Color TextBright  = new(0.95f, 0.97f, 1.00f);
    public static readonly Color TextDim     = new(0.55f, 0.62f, 0.78f);
    public static readonly Color TextFaint   = new(0.40f, 0.45f, 0.55f);
    public static readonly Color CompleteBg  = new(0.20f, 0.40f, 0.25f, 0.35f);
    public static readonly Color CompleteBor = new(0.40f, 0.85f, 0.50f, 0.6f);

    public static ColorRect FullscreenBg(Color? color = null) => new()
    {
        Color        = color ?? Bg,
        AnchorRight  = 1,
        AnchorBottom = 1,
    };

    public static Label MakeLabel(string text, int fontSize, Color color,
                                  HorizontalAlignment align = HorizontalAlignment.Left)
    {
        return new Label
        {
            Text                = text,
            HorizontalAlignment = align,
            LabelSettings       = new LabelSettings { FontSize = fontSize, FontColor = color },
        };
    }

    public static Button MakeButton(string text, Vector2 minSize, bool active = false)
    {
        var btn = new Button
        {
            Text              = text,
            CustomMinimumSize = minSize,
            Alignment         = HorizontalAlignment.Center,
        };
        StyleButton(btn, active);
        return btn;
    }

    public static void StyleButton(Button btn, bool active)
    {
        var normal = new StyleBoxFlat
        {
            BgColor                  = active ? new Color(Accent.R, Accent.G, Accent.B, 0.30f) : Panel,
            BorderColor              = active ? Accent : new Color(0.25f, 0.28f, 0.36f),
            BorderWidthTop           = active ? 2 : 1,
            BorderWidthBottom        = active ? 2 : 1,
            BorderWidthLeft          = active ? 2 : 1,
            BorderWidthRight         = active ? 2 : 1,
            CornerRadiusTopLeft      = 6,
            CornerRadiusTopRight     = 6,
            CornerRadiusBottomLeft   = 6,
            CornerRadiusBottomRight  = 6,
            ContentMarginLeft        = 14,
            ContentMarginRight       = 14,
            ContentMarginTop         = 8,
            ContentMarginBottom      = 8,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = active ? new Color(Accent.R, Accent.G, Accent.B, 0.45f) : PanelHover;

        var pressed = (StyleBoxFlat)normal.Duplicate();
        pressed.BgColor = new Color(Accent.R, Accent.G, Accent.B, 0.55f);

        var disabled = (StyleBoxFlat)normal.Duplicate();
        disabled.BgColor     = new Color(0.08f, 0.09f, 0.12f);
        disabled.BorderColor = new Color(0.18f, 0.20f, 0.25f);

        btn.AddThemeStyleboxOverride("normal",   normal);
        btn.AddThemeStyleboxOverride("hover",    hover);
        btn.AddThemeStyleboxOverride("pressed",  pressed);
        btn.AddThemeStyleboxOverride("disabled", disabled);
    }
}
