using System.Numerics;
using Raylib_cs;

namespace CaballeroDeTinta.Art;

/// <summary>Todo lo que se dibuja sobre la película: carteles, efectos de impacto y HUD.</summary>
sealed class Cards : IDisposable
{
    readonly Font _serif, _serifBold;
    readonly bool _ownsFonts;

    public Cards()
    {
        // Letras de cartel de cine: una serif del sistema, con acentos y eñes.
        int[] codepoints = Enumerable.Range(32, 224).Append(0x2014).Append(0x2019).ToArray();
        string regular = @"C:\Windows\Fonts\georgia.ttf", bold = @"C:\Windows\Fonts\georgiab.ttf";
        if (File.Exists(regular) && File.Exists(bold))
        {
            _serif = Raylib.LoadFontEx(regular, 64, codepoints, codepoints.Length);
            _serifBold = Raylib.LoadFontEx(bold, 96, codepoints, codepoints.Length);
            Raylib.SetTextureFilter(_serif.Texture, TextureFilter.Bilinear);
            Raylib.SetTextureFilter(_serifBold.Texture, TextureFilter.Bilinear);
            _ownsFonts = true;
        }
        else
        {
            _serif = _serifBold = Raylib.GetFontDefault();
        }
    }

    public void Text(string text, Vector2 center, float size, Color color, bool bold = false, float spacing = 2f)
    {
        Font f = bold ? _serifBold : _serif;
        Vector2 m = Raylib.MeasureTextEx(f, text, size, spacing);
        Raylib.DrawTextEx(f, text, center - m / 2, size, spacing, color);
    }

    public void TextLeft(string text, Vector2 pos, float size, Color color, bool bold = false) =>
        Raylib.DrawTextEx(bold ? _serifBold : _serif, text, pos, size, 1.5f, color);

    // ------------------------------------------------------------------ carteles

    /// <summary>Segundos de pantalla negra y silencio antes de que caiga el cartel del jefe.</summary>
    public const float CardDelay = 1.1f;

    /// <summary>
    /// Cartel de presentación en blanco y negro con marco ornamental, como los intertítulos del cine mudo.
    /// Primero la pantalla se va a negro y la música calla; el cartel aparece después, de golpe.
    /// </summary>
    public void TitleCard(float t, string overline, string title, string subtitle, float seed)
    {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        float fadeIn = Math.Clamp(t / 0.25f, 0, 1);
        Raylib.DrawRectangle(0, 0, w, h, Palette.Alpha(new Color(12, 10, 10, 255), fadeIn));
        if (t < CardDelay) return;

        // El cartel "tiembla" en el proyector.
        var rng = new Random((int)seed);
        Vector2 jitter = new((float)rng.NextDouble() * 3 - 1.5f, (float)rng.NextDouble() * 3 - 1.5f);
        Vector2 c = new Vector2(w / 2f, h / 2f) + jitter;
        float cw = MathF.Min(w * 0.78f, 1000), ch = MathF.Min(h * 0.62f, 440);
        var rect = new Rectangle(c.X - cw / 2, c.Y - ch / 2, cw, ch);
        Color paper = new(232, 224, 206, 255), ink = new(18, 15, 14, 255);

        Raylib.DrawRectangleRec(rect, ink);
        Frame(rect, paper, 10);
        Frame(Inset(rect, 22), paper, 3);
        Frame(Inset(rect, 30), paper, 1.5f);
        foreach (Vector2 corner in Corners(Inset(rect, 26)))
            Flourish(corner, c, paper);

        Text(overline, new Vector2(c.X, rect.Y + ch * 0.24f), 28, paper, spacing: 6);
        Rule(new Vector2(c.X, rect.Y + ch * 0.33f), cw * 0.34f, paper);
        float grow = 1f + 0.03f * MathF.Max(0, 1 - (t - CardDelay) * 2);
        Text(title, new Vector2(c.X, c.Y + 6), MathF.Min(74, cw / MathF.Max(8, title.Length) * 1.7f) * grow, paper, bold: true, spacing: 4);
        Rule(new Vector2(c.X, rect.Y + ch * 0.67f), cw * 0.34f, paper);
        Text(subtitle, new Vector2(c.X, rect.Y + ch * 0.77f), 26, paper, spacing: 3);
    }

    static Rectangle Inset(Rectangle r, float d) => new(r.X + d, r.Y + d, r.Width - 2 * d, r.Height - 2 * d);

    static IEnumerable<Vector2> Corners(Rectangle r)
    {
        yield return new(r.X, r.Y);
        yield return new(r.X + r.Width, r.Y);
        yield return new(r.X, r.Y + r.Height);
        yield return new(r.X + r.Width, r.Y + r.Height);
    }

    static void Frame(Rectangle r, Color color, float thick) => Raylib.DrawRectangleLinesEx(r, thick, color);

    static void Rule(Vector2 center, float halfWidth, Color color)
    {
        Raylib.DrawLineEx(center - new Vector2(halfWidth, 0), center - new Vector2(12, 0), 2, color);
        Raylib.DrawLineEx(center + new Vector2(12, 0), center + new Vector2(halfWidth, 0), 2, color);
        Raylib.DrawPoly(center, 4, 7, 45, color);
    }

    static void Flourish(Vector2 corner, Vector2 center, Color color)
    {
        Vector2 dir = Vector2.Normalize(center - corner);
        Raylib.DrawPoly(corner + dir * 12, 4, 9, 45, color);
        Raylib.DrawRing(corner + dir * 12, 14, 16, 0, 360, 24, color);
        Raylib.DrawCircleV(corner + dir * 34, 3, color);
        Raylib.DrawCircleV(corner + dir * 46, 2, color);
    }

    /// <summary>Cartel final ("Y así cayó...", "Fin del primer rollo").</summary>
    public void EndCard(float t, string title, string subtitle, Color accent, float seed)
    {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        float a = Math.Clamp(t / 1.2f, 0, 1);
        Raylib.DrawRectangle(0, 0, w, h, Palette.Alpha(new Color(10, 8, 8, 255), a * 0.88f));
        if (t < 0.5f) return;
        float ta = Math.Clamp((t - 0.5f) / 0.8f, 0, 1);
        var rng = new Random((int)seed);
        Vector2 c = new(w / 2f + (float)rng.NextDouble() * 2, h / 2f + (float)rng.NextDouble() * 2);
        // Iris de cierre: un círculo de luz que se estrecha alrededor del texto.
        Raylib.DrawRing(c, 230 + (1 - ta) * 400, 2000, 0, 360, 64, new Color(6, 5, 5, 255));
        Text(title, c - new Vector2(0, 20), 58, Palette.Alpha(accent, ta), bold: true, spacing: 5);
        Rule(c + new Vector2(0, 26), 170, Palette.Alpha(Palette.Parchment, ta));
        Text(subtitle, c + new Vector2(0, 62), 24, Palette.Alpha(Palette.Parchment, ta), spacing: 3);
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

    // ------------------------------------------------------------------ HUD

    /// <summary>Barra de época: marco de tinta y relleno con borde irregular que hierve.</summary>
    public void Bar(Vector2 pos, float width, float height, float value, float lag, Color fill, float seed)
    {
        var rng = new Random((int)seed + (int)pos.Y);
        Raylib.DrawRectangleV(pos - new Vector2(3, 3), new Vector2(width + 6, height + 6), Palette.Ink);
        Raylib.DrawRectangleV(pos, new Vector2(width, height), new Color(52, 44, 38, 255));
        Raylib.DrawRectangleV(pos, new Vector2(width * Math.Clamp(lag, 0, 1), height), Palette.Parchment);
        float v = width * Math.Clamp(value, 0, 1);
        Raylib.DrawRectangleV(pos, new Vector2(v, height), fill);
        // Borde del pigmento irregular.
        for (float x = 0; x < v; x += 6)
            Raylib.DrawRectangleV(pos + new Vector2(x, height - 2 - (float)rng.NextDouble() * 3), new Vector2(6, 2), Palette.Mix(fill, Palette.Ink, 0.35f));
    }

    public void Dispose()
    {
        if (!_ownsFonts) return;
        Raylib.UnloadFont(_serif);
        Raylib.UnloadFont(_serifBold);
    }
}
