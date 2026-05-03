using Godot;

namespace PRISM.Nodes;

// Constant subtle "vibration" used across the entire game (Balatro-style):
// every drawn object gets a tiny rotation + minute scale oscillation around
// its own center, with a per-id phase offset so things desync. Rotation does
// most of the work — pure scale at any perceptible amplitude reads as zoom,
// rotation reads as "alive".
internal static class PulseAnim
{
    // Rotation oscillation — the dominant component of the wiggle.
    public const float RotFreq = 3.0f;
    public const float RotAmp  = 0.011f;   // radians, ≈ 0.63°

    // Scale oscillation — runs underneath the rotation at a different freq
    // so the combined motion never repeats cleanly. Kept tiny so it never
    // perceptibly "zooms".
    public const float ScaleFreq = 2.4f;
    public const float ScaleAmp  = 0.004f; // ±0.4%

    public const float WobbleFreq = 1.7f;
    public const float WobbleAmp  = 1.5f;  // pixels

    // Golden-ratio-conjugate phase distributor — coprime with Tau, so seeds
    // close in value still produce well-separated phases.
    private const float Phi = 0.6180339887f;

    public static float Phase(int seed) =>
        ((seed * Phi) - Mathf.Floor(seed * Phi)) * Mathf.Tau;

    public static float Phase(string id) => Phase(SeedOf(id));

    public static int SeedOf(string id)
    {
        int h = 0;
        if (string.IsNullOrEmpty(id)) return h;
        foreach (var ch in id) h = h * 31 + ch;
        return h;
    }

    public static Vector2 Wobble(float t, int seed = 0,
                                 float amp = WobbleAmp, float freq = WobbleFreq)
    {
        float p = Phase(seed);
        return new Vector2(amp * Mathf.Sin(t * freq + p),
                           amp * Mathf.Sin(t * freq * 0.83f + p * 1.31f));
    }

    // Combined scale + rotation about a fixed center, returned as an affine
    // transform so _Draw() callers can keep their absolute coordinates.
    //
    // Derivation: T(p) = M·(p − c) + c = M·p + (c − M·c), where M is the
    // 2×2 linear part (scale·rotation). xAxis/yAxis are M's columns.
    public static Transform2D Pulse(Vector2 center, float t, int seed,
                                    float scaleAmp = ScaleAmp,
                                    float rotAmp   = RotAmp)
    {
        float ph    = Phase(seed);
        float scale = 1f + scaleAmp * Mathf.Sin(t * ScaleFreq + ph);
        // Slight phase/freq offset on rotation so it doesn't track scale 1:1.
        float rot   = rotAmp * Mathf.Sin(t * RotFreq + ph * 1.37f);

        float cs = Mathf.Cos(rot) * scale;
        float sn = Mathf.Sin(rot) * scale;
        var xAxis = new Vector2(cs, sn);
        var yAxis = new Vector2(-sn, cs);
        // origin = c − M·c
        var origin = center - new Vector2(xAxis.X * center.X + yAxis.X * center.Y,
                                          xAxis.Y * center.X + yAxis.Y * center.Y);
        return new Transform2D(xAxis, yAxis, origin);
    }

    // Drives a Control's Scale + Rotation around its own center. Per-call
    // multipliers let title labels wiggle harder than rank-and-file buttons.
    public static void ApplyTo(Control node, float t, int seed,
                               float scaleMul = 1f, float rotMul = 1f)
    {
        if (node == null) return;
        float ph    = Phase(seed);
        float scale = 1f + (ScaleAmp * scaleMul) * Mathf.Sin(t * ScaleFreq + ph);
        float rot   = (RotAmp * rotMul) * Mathf.Sin(t * RotFreq + ph * 1.37f);

        var size = node.Size;
        if (size == Vector2.Zero) size = node.GetMinimumSize();
        node.PivotOffset = size * 0.5f;
        node.Scale       = new Vector2(scale, scale);
        node.Rotation    = rot;
    }
}
