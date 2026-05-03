using Godot;

namespace PRISM.Nodes;

// Constant gentle "breathing" used across the entire game (Balatro-style):
// every drawn object scales by ±BreatheAmp at BreatheFreq Hz, with a per-id
// phase offset so things desync — that desync is what makes the screen feel
// alive instead of robotically pulsing in lockstep.
internal static class PulseAnim
{
    public const float BreatheFreq = 2.2f;
    public const float BreatheAmp  = 0.018f;   // ±1.8% scale
    public const float WobbleFreq  = 1.7f;
    public const float WobbleAmp   = 1.5f;    // pixels

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

    public static float Scale(float t, int seed = 0,
                              float amp = BreatheAmp, float freq = BreatheFreq) =>
        1f + amp * Mathf.Sin(t * freq + Phase(seed));

    public static float Scale(float t, string id,
                              float amp = BreatheAmp, float freq = BreatheFreq) =>
        Scale(t, SeedOf(id), amp, freq);

    public static Vector2 Wobble(float t, int seed = 0,
                                 float amp = WobbleAmp, float freq = WobbleFreq)
    {
        float p = Phase(seed);
        return new Vector2(amp * Mathf.Sin(t * freq + p),
                           amp * Mathf.Sin(t * freq * 0.83f + p * 1.31f));
    }

    public static Vector2 Wobble(float t, string id,
                                 float amp = WobbleAmp, float freq = WobbleFreq) =>
        Wobble(t, SeedOf(id), amp, freq);

    // Affine transform that scales by `s` about a fixed center point —
    // lets _Draw() code keep its absolute coordinates while still pulsing.
    // Derivation: T(p) = S·(p − c) + c  ⇒  T(p) = S·p + c·(1 − s).
    public static Transform2D ScaleAround(Vector2 center, float scale) =>
        new(new Vector2(scale, 0), new Vector2(0, scale), center * (1f - scale));

    // Drives a Control's Scale around its own center. Caller passes the seed
    // so two controls with the same text don't pulse in lockstep — typically
    // the index in a list, the slot number, or a hash of the label.
    public static void ApplyTo(Control node, float t, int seed,
                               float amp = BreatheAmp, float freq = BreatheFreq)
    {
        if (node == null) return;
        float s = Scale(t, seed, amp, freq);
        var size = node.Size;
        if (size == Vector2.Zero) size = node.GetMinimumSize();
        node.PivotOffset = size * 0.5f;
        node.Scale       = new Vector2(s, s);
    }
}
