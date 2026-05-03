using Godot;

namespace PRISM.Core;

public enum BeamColor
{
	Red,
	Green,
	Blue,
	Yellow,
	Magenta,
	Cyan,
	White
}

public static class BeamColorHelper
{
	public static (bool r, bool g, bool b) ToRGB(BeamColor c) => c switch
	{
		BeamColor.Red     => (true,  false, false),
		BeamColor.Green   => (false, true,  false),
		BeamColor.Blue    => (false, false, true),
		BeamColor.Yellow  => (true,  true,  false),
		BeamColor.Magenta => (true,  false, true),
		BeamColor.Cyan    => (false, true,  true),
		BeamColor.White   => (true,  true,  true),
		_                 => (false, false, false)
	};

	public static BeamColor? FromRGB(bool r, bool g, bool b) => (r, g, b) switch
	{
		(true,  false, false) => BeamColor.Red,
		(false, true,  false) => BeamColor.Green,
		(false, false, true)  => BeamColor.Blue,
		(true,  true,  false) => BeamColor.Yellow,
		(true,  false, true)  => BeamColor.Magenta,
		(false, true,  true)  => BeamColor.Cyan,
		(true,  true,  true)  => BeamColor.White,
		_                     => null
	};

	public static BeamColor Combine(BeamColor a, BeamColor b)
	{
		var (ar, ag, ab) = ToRGB(a);
		var (br, bg, bb) = ToRGB(b);
		return FromRGB(ar | br, ag | bg, ab | bb) ?? BeamColor.White;
	}

	// Splits a color into its primary components for Prism
	public static BeamColor[] SplitComponents(BeamColor c)
	{
		var (r, g, b) = ToRGB(c);
		var result = new System.Collections.Generic.List<BeamColor>();
		if (r) result.Add(BeamColor.Red);
		if (g) result.Add(BeamColor.Green);
		if (b) result.Add(BeamColor.Blue);
		return result.Count > 1 ? result.ToArray() : new[] { c };
	}

	public static Color ToGodotColor(BeamColor c) => c switch
	{
		BeamColor.Red     => new Color(0.906f, 0.298f, 0.235f),
		BeamColor.Green   => new Color(0.180f, 0.800f, 0.443f),
		BeamColor.Blue    => new Color(0.204f, 0.596f, 0.859f),
		BeamColor.Yellow  => new Color(0.945f, 0.769f, 0.188f),
		BeamColor.Magenta => new Color(0.914f, 0.118f, 0.388f),
		BeamColor.Cyan    => new Color(0.000f, 0.737f, 0.831f),
		BeamColor.White   => new Color(1.000f, 1.000f, 1.000f),
		_                 => Colors.White
	};

	// Symbol pairing for accessibility (per spec §3.3)
	public static string ToSymbol(BeamColor c) => c switch
	{
		BeamColor.Red     => "▲",
		BeamColor.Green   => "■",
		BeamColor.Blue    => "●",
		BeamColor.Yellow  => "▲■",
		BeamColor.Magenta => "▲●",
		BeamColor.Cyan    => "■●",
		BeamColor.White   => "▲■●",
		_                 => "?"
	};

	public static string ToName(BeamColor c) => c switch
	{
		BeamColor.Red     => "Red",
		BeamColor.Green   => "Green",
		BeamColor.Blue    => "Blue",
		BeamColor.Yellow  => "Yellow",
		BeamColor.Magenta => "Magenta",
		BeamColor.Cyan    => "Cyan",
		BeamColor.White   => "White",
		_                 => "?"
	};
}
