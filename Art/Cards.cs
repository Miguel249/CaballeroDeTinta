using System.Numerics;
using Raylib_cs;

namespace CaballeroDeTinta.Art;

/// <summary>Lo que se dibuja sobre la película: carteles de cine y efectos de impacto. El HUD vive en <see cref="View.Hud"/>.</summary>
sealed class Cards(Ui ui)
{
    // ------------------------------------------------------------------ carteles

    /// <summary>Segundos de pantalla negra y silencio antes de que caiga el cartel del jefe.</summary>
    public const float CardDelay = 1.1f;

    /// <summary>Duración total del cartel del jefe (la simulación da paso al combate a los 4,2 s).</summary>
    public const float CardLength = 4.2f;

    /// <summary>
    /// Cartel de presentación en blanco y negro con marco ornamental, como los intertítulos del cine mudo.
    /// Primero la luz cae y la música calla; el cartel aparece de golpe, después el nombre crece,
    /// el epíteto se revela y todo se funde justo antes de que el rey despierte.
    /// </summary>
    public void TitleCard(float t, string overline, string title, string subtitle, float seed)
    {
        float w = ui.W, h = ui.H, s = ui.S;
        float fadeIn = Math.Clamp(t / 0.25f, 0, 1);
        float fadeOut = 1 - Ui.Smooth((t - (CardLength - 0.4f)) / 0.4f);
        Raylib.DrawRectangle(0, 0, (int)w, (int)h, Ui.A(new Color(12, 10, 10, 255), fadeIn * MathF.Max(fadeOut, 0.0f)));
        if (t < CardDelay) return;
        float local = t - CardDelay;

        // El cartel "tiembla" en el proyector.
        var rng = new Random((int)seed);
        Vector2 jitter = new Vector2((float)rng.NextDouble() * 3 - 1.5f, (float)rng.NextDouble() * 3 - 1.5f) * s;
        Vector2 c = ui.Center + jitter;
        float cw = MathF.Min(w * 0.78f, 1000 * s), ch = MathF.Min(h * 0.62f, 440 * s);
        var rect = new Rectangle(c.X - cw / 2, c.Y - ch / 2, cw, ch);
        Color paper = Ui.A(new Color(232, 224, 206, 255), fadeOut), ink = Ui.A(new Color(18, 15, 14, 255), fadeOut);

        Raylib.DrawRectangleRec(rect, ink);
        Raylib.DrawRectangleLinesEx(rect, 10 * s, paper);
        ui.InkFrame(Ui.Inset(rect, 22 * s), 3 * s, paper, 7, 0.8f);
        ui.InkFrame(Ui.Inset(rect, 30 * s), 1.5f * s, paper, 13, 0.5f);
        foreach (Vector2 corner in Corners(Ui.Inset(rect, 26 * s)))
            Flourish(corner, c, paper, s);

        ui.Heading(overline, new Vector2(c.X, rect.Y + ch * 0.24f), 22 * s, paper);
        ui.Divider(new Vector2(c.X, rect.Y + ch * 0.33f), cw * 0.3f, paper);
        float grow = 1f + 0.03f * MathF.Max(0, 1 - local * 2);
        string name = title.ToUpperInvariant();
        float size = MathF.Min(88 * s, cw / MathF.Max(8, name.Length) * 1.9f) * grow;
        ui.Text(name, new Vector2(c.X, c.Y + 4 * s), size, paper, Face.Display, 5);
        float sub = Ui.Smooth((local - 0.45f) / 0.35f);
        ui.Divider(new Vector2(c.X, rect.Y + ch * 0.67f), cw * 0.3f * sub, Ui.A(paper, sub));
        ui.Text(subtitle, new Vector2(c.X, rect.Y + ch * 0.77f + (1 - sub) * 6 * s), 28 * s, Ui.A(paper, sub), Face.Body, 3);
    }

    static IEnumerable<Vector2> Corners(Rectangle r)
    {
        yield return new(r.X, r.Y);
        yield return new(r.X + r.Width, r.Y);
        yield return new(r.X, r.Y + r.Height);
        yield return new(r.X + r.Width, r.Y + r.Height);
    }

    static void Flourish(Vector2 corner, Vector2 center, Color color, float s)
    {
        Vector2 dir = Vector2.Normalize(center - corner);
        Raylib.DrawPoly(corner + dir * 12 * s, 4, 9 * s, 45, color);
        Raylib.DrawRing(corner + dir * 12 * s, 14 * s, 16 * s, 0, 360, 24, color);
        Raylib.DrawCircleV(corner + dir * 34 * s, 3 * s, color);
        Raylib.DrawCircleV(corner + dir * 46 * s, 2 * s, color);
    }

    /// <summary>
    /// Cartel final ("Y así cayó...", "Fin del primer rollo"). El iris se cierra sobre el texto;
    /// con <paramref name="bleed"/> su borde se deshace como tinta que invade el papel.
    /// </summary>
    public void EndCard(float t, string title, string subtitle, Color accent, bool bleed, (string Key, string Verb)? prompt = null, float promptT = 0)
    {
        float s = ui.S;
        Vector2 c = ui.Center;
        float close = Ui.Smooth(t / 1.6f);
        Raylib.DrawRectangle(0, 0, (int)ui.W, (int)ui.H, Ui.A(new Color(10, 8, 8, 255), close * 0.45f));
        float radius = ui.IrisOpen + (MathF.Min(ui.W, ui.H) * 0.42f - ui.IrisOpen) * close;
        ui.Iris(c, radius, new Color(8, 6, 6, 255), bleed ? 0.02f + 0.07f * close : 0.012f);

        float ta = Ui.Smooth((t - 0.7f) / 0.8f);
        if (ta <= 0) return;
        ui.Text(title, c - new Vector2(0, 22 * s - (1 - ta) * 6 * s), 54 * s, Ui.A(accent, ta), Face.Display, 3, shadow: true);
        ui.Divider(c + new Vector2(0, 24 * s), 160 * s * ta, Ui.A(Palette.Parchment, ta));
        ui.Text(subtitle, c + new Vector2(0, 56 * s), 20 * s, Ui.A(Palette.Parchment, 0.9f * ta), Face.Body, 2, shadow: true);
        if (prompt is var (key, verb)) ui.Prompt(c + new Vector2(0, 112 * s), key, verb, promptT);
    }

    // ------------------------------------------------------------------ impactos

    /// <summary>Estallido de tinta: estrella irregular con el interior en papel.</summary>
    public static void InkBurst(Vector2 at, float radius, float seed, Color ink, Color paper)
    {
        var rng = new Random((int)(seed * 1000) ^ (int)at.X);
        int spikes = 11;
        var pts = new Vector2[spikes * 2];
        float rot = (float)rng.NextDouble() * MathF.PI;
        for (int i = 0; i < pts.Length; i++)
        {
            float ang = rot + i * MathF.PI / spikes;
            float r = i % 2 == 0 ? radius * (0.8f + (float)rng.NextDouble() * 0.5f) : radius * (0.35f + (float)rng.NextDouble() * 0.15f);
            pts[i] = at + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r;
        }
        for (int i = 0; i < pts.Length; i++)
            Raylib.DrawTriangle(at, pts[(i + 1) % pts.Length], pts[i], ink);
        for (int i = 0; i < pts.Length; i++)
        {
            Vector2 a = at + (pts[i] - at) * 0.55f, b = at + (pts[(i + 1) % pts.Length] - at) * 0.55f;
            Raylib.DrawTriangle(at, b, a, paper);
        }
    }

    /// <summary>Estrellita de dibujo animado (chispas al golpear piedra, golpes en la cabeza).</summary>
    public static void Star(Vector2 at, float radius, float rotation, Color color)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float ang = rotation + i * MathF.PI / 5 - MathF.PI / 2;
            float r = i % 2 == 0 ? radius : radius * 0.42f;
            pts[i] = at + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r;
        }
        for (int i = 0; i < 10; i++)
            Raylib.DrawTriangle(at, pts[(i + 1) % 10], pts[i], color);
        for (int i = 0; i < 10; i++)
            Raylib.DrawLineEx(pts[i], pts[(i + 1) % 10], 2, Palette.Ink);
    }

    /// <summary>Líneas de velocidad radiales hacia un punto.</summary>
    public static void SpeedLines(Vector2 focus, float strength, float seed)
    {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        var rng = new Random((int)seed * 7 + 1);
        float far = MathF.Max(w, h);
        for (int i = 0; i < 46; i++)
        {
            float ang = (float)(rng.NextDouble() * MathF.Tau);
            float inner = 180 + (float)rng.NextDouble() * 180;
            Vector2 d = new(MathF.Cos(ang), MathF.Sin(ang));
            Raylib.DrawLineEx(focus + d * inner, focus + d * far, 1.5f + (float)rng.NextDouble() * 4f, Palette.Alpha(Palette.Ink, strength * 0.8f));
        }
    }

    /// <summary>Grieta dibujada en pantalla: zigzag desde un punto.</summary>
    public static void Crack(Vector2 from, float length, float seed, Color color)
    {
        var rng = new Random((int)seed);
        for (int b = 0; b < 5; b++)
        {
            Vector2 p = from;
            float ang = (float)(rng.NextDouble() * MathF.Tau);
            for (int i = 0; i < 6; i++)
            {
                ang += (float)(rng.NextDouble() - 0.5) * 1.2f;
                Vector2 q = p + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * length / 6;
                Raylib.DrawLineEx(p, q, 3.5f - i * 0.5f, color);
                p = q;
            }
        }
    }
}
