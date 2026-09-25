using System.Numerics;
using Raylib_cs;

namespace CaballeroDeTinta.Art;

enum Face { Body, Bold, Display }
enum Align { Center, Left, Right }
enum MouseMark { None, Left, Right, Wheel, Move }
enum GlyphKind { Key, Mouse, Wasd, Or }

/// <summary>Un icono de control: una tecla, el ratón, el grupo WASD o la conjunción "o".</summary>
readonly record struct Glyph(GlyphKind Kind, string Key = "", MouseMark Mouse = MouseMark.None)
{
    public static Glyph K(string key) => new(GlyphKind.Key, key);
    public static Glyph M(MouseMark mark) => new(GlyphKind.Mouse, Mouse: mark);
    public static readonly Glyph Wasd = new(GlyphKind.Wasd);
    public static readonly Glyph Or = new(GlyphKind.Or);
}

/// <summary>Aspecto de una barra de pigmento (vida, aguante, jefe) en un fotograma.</summary>
record struct VitalLook(float Value, float Lag, float Gain, Color Fill, int Seed)
{
    public float Tremble, Hatch, Pulse, Alpha = 1;
}

/// <summary>
/// El sistema visual de la interfaz: tipografía, marcos de tinta a mano alzada, teclas dibujadas,
/// iconos y el iris de cine. Todo se mide en unidades de diseño de 1280×720 multiplicadas por
/// <see cref="S"/>, así que la composición se conserva en cualquier resolución.
/// Aquí no hay estado de juego: cada pantalla decide qué dibujar y este kit decide cómo.
/// </summary>
sealed class Ui : IDisposable
{
    // ------------------------------------------------------------------ paleta de interfaz

    public static readonly Color Ivory = new(236, 226, 202, 255);
    public static readonly Color KeyDepth = new(196, 178, 146, 255);
    public static readonly Color Card = new(17, 14, 13, 255);
    public static readonly Color Blood = new(128, 30, 34, 255);      // carmesí apagado de la vida
    public static readonly Color Moss = new(104, 116, 70, 255);      // verde oliva del aguante
    public static readonly Color Brass = new(208, 170, 92, 255);     // dorado viejo de la curación
    public static readonly Color EmberDim = new(206, 104, 38, 255);  // brasa para marcar botones

    // ------------------------------------------------------------------ escala

    public float W { get; private set; }
    public float H { get; private set; }
    /// <summary>Unidades de diseño a píxeles: 1 a 1280×720, 2 a 2560×1440.</summary>
    public float S { get; private set; } = 1;
    /// <summary>Margen seguro común a todos los bordes.</summary>
    public float Margin => 34 * S;
    public Vector2 Center => new(W / 2, H / 2);
    public float Time { get; private set; }
    /// <summary>Fotograma del dibujo animado (12 por segundo): lo que "hierve" cambia a este ritmo.</summary>
    public int Frame { get; private set; }

    readonly Font _body, _bold, _display;
    readonly List<Font> _owned = [];
    readonly List<Vector2> _pts = [];

    public Ui()
    {
        // Latin-1 completo (á é í ó ú ñ ü ¿ ¡ ·  ×) más la raya y comillas tipográficas.
        int[] cps = Enumerable.Range(32, 224).Concat([0x2014, 0x2013, 0x2019, 0x201C, 0x201D, 0x2026]).ToArray();
        _body = Load(cps, 72, @"C:\Windows\Fonts\georgia.ttf", @"C:\Windows\Fonts\constan.ttf", @"C:\Windows\Fonts\times.ttf");
        _bold = Load(cps, 80, @"C:\Windows\Fonts\georgiab.ttf", @"C:\Windows\Fonts\constanb.ttf", @"C:\Windows\Fonts\timesbd.ttf");
        // Títulos: una romana de libro antiguo si el sistema la tiene.
        _display = Load(cps, 128, @"C:\Windows\Fonts\GARABD.TTF", @"C:\Windows\Fonts\BKANT.TTF", @"C:\Windows\Fonts\palab.ttf", @"C:\Windows\Fonts\georgiab.ttf");
        BeginFrame(0);
    }

    Font Load(int[] cps, int size, params string[] candidates)
    {
        foreach (string path in candidates)
        {
            if (!File.Exists(path)) continue;
            Font f = Raylib.LoadFontEx(path, size, cps, cps.Length);
            if (f.Texture.Id == 0) continue;
            // Mipmaps: el texto pequeño se reduce sin parpadeos.
            Raylib.GenTextureMipmaps(ref f.Texture);
            Raylib.SetTextureFilter(f.Texture, TextureFilter.Trilinear);
            _owned.Add(f);
            return f;
        }
        return Raylib.GetFontDefault();
    }

    public void BeginFrame(float dt)
    {
        W = Raylib.GetScreenWidth();
        H = Raylib.GetScreenHeight();
        S = MathF.Max(0.5f, MathF.Min(W / 1280f, H / 720f));
        Time += dt;
        Frame = (int)(Raylib.GetTime() * 12);
    }

    public static Color A(Color c, float a) => new(c.R, c.G, c.B, (byte)(c.A * Math.Clamp(a, 0, 1)));
    public static float Smooth(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    public static float Approach(float v, float target, float step) => v < target ? MathF.Min(target, v + step) : MathF.Max(target, v - step);

    /// <summary>Ruido entero determinista en [0, 1).</summary>
    public static float Hash(int n)
    {
        unchecked
        {
            uint x = (uint)n * 0x9E3779B1u;
            x ^= x >> 15; x *= 0x85EBCA77u; x ^= x >> 13; x *= 0xC2B2AE3Du; x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777216f;
        }
    }

    public static int Seed(string s) { int h = 17; foreach (char c in s) h = h * 31 + c; return h; }

    // ------------------------------------------------------------------ tipografía

    Font FontOf(Face f) => f switch { Face.Bold => _bold, Face.Display => _display, _ => _body };

    public Vector2 Measure(string text, float size, Face face = Face.Body, float tracking = 1) =>
        Raylib.MeasureTextEx(FontOf(face), text, size, tracking * S);

    /// <summary>Texto centrado verticalmente en <paramref name="at"/>; la alineación decide el eje X.</summary>
    public void Text(string text, Vector2 at, float size, Color color, Face face = Face.Body, float tracking = 1, Align align = Align.Center, bool shadow = false)
    {
        Font f = FontOf(face);
        Vector2 m = Raylib.MeasureTextEx(f, text, size, tracking * S);
        float x = align switch { Align.Left => at.X, Align.Right => at.X - m.X, _ => at.X - m.X / 2 };
        var p = new Vector2(MathF.Round(x), MathF.Round(at.Y - m.Y / 2));
        if (shadow) Raylib.DrawTextEx(f, text, p + new Vector2(1.2f, 1.8f) * S, size, tracking * S, A(Palette.Ink, color.A / 255f * 0.8f));
        Raylib.DrawTextEx(f, text, p, size, tracking * S, color);
    }

    /// <summary>Encabezado en versalitas espaciadas, con dos trazos de pluma a los lados.</summary>
    public void Heading(string text, Vector2 at, float size, Color color, bool dashes = true, bool shadow = false)
    {
        string up = text.ToUpperInvariant();
        float tracking = size * 0.22f / S;
        Text(up, at, size, color, Face.Display, tracking, shadow: shadow);
        if (!dashes) return;
        float half = Measure(up, size, Face.Display, tracking).X / 2;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 a = at + new Vector2(side * (half + 12 * S), size * 0.04f);
            Taper(a, a + new Vector2(side * 30 * S, 0), 1.8f * S, color);
        }
    }

    // ------------------------------------------------------------------ trazos

    /// <summary>Triángulo en el orden que acepta el culling de raylib, sea cual sea el que llegue.</summary>
    public static void Tri(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        float cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (cross > 0) Raylib.DrawTriangle(a, c, b, color);
        else Raylib.DrawTriangle(a, b, c, color);
    }

    /// <summary>Trazo de pluma que adelgaza hacia la punta.</summary>
    public static void Taper(Vector2 from, Vector2 to, float thick, Color color, int steps = 4)
    {
        for (int i = 0; i < steps; i++)
        {
            float t0 = i / (float)steps, t1 = (i + 1) / (float)steps;
            Raylib.DrawLineEx(Vector2.Lerp(from, to, t0), Vector2.Lerp(from, to, t1), MathF.Max(0.6f, thick * (1 - t0 * 0.75f)), color);
        }
    }

    /// <summary>Filete ornamental: dos trazos que se afinan, un rombo y dos puntos.</summary>
    public void Divider(Vector2 at, float half, Color color)
    {
        Taper(at - new Vector2(10 * S, 0), at - new Vector2(half, 0), 2 * S, color);
        Taper(at + new Vector2(10 * S, 0), at + new Vector2(half, 0), 2 * S, color);
        Raylib.DrawPoly(at, 4, 5.5f * S, 45, color);
        Raylib.DrawCircleV(at - new Vector2(half + 7 * S, 0), 1.6f * S, color);
        Raylib.DrawCircleV(at + new Vector2(half + 7 * S, 0), 1.6f * S, color);
    }

    /// <summary>Contorno de un rectángulo con esquinas romas y bordes que se desvían un pelo.</summary>
    List<Vector2> Perimeter(Rectangle r, int seed, float rough)
    {
        _pts.Clear();
        float cr = MathF.Min(4 * S, MathF.Min(r.Width, r.Height) * 0.3f), step = 18 * S;
        Vector2[] corners =
        [
            new(r.X + cr, r.Y), new(r.X + r.Width - cr, r.Y), new(r.X + r.Width, r.Y + cr), new(r.X + r.Width, r.Y + r.Height - cr),
            new(r.X + r.Width - cr, r.Y + r.Height), new(r.X + cr, r.Y + r.Height), new(r.X, r.Y + r.Height - cr), new(r.X, r.Y + cr),
        ];
        int k = 0;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 a = corners[i], b = corners[(i + 1) % corners.Length];
            _pts.Add(a);
            if (i % 2 == 1) continue; // chaflán de esquina: recto
            Vector2 d = b - a;
            float len = d.Length();
            int n = Math.Max(1, (int)(len / step));
            Vector2 normal = new Vector2(d.Y, -d.X) / MathF.Max(len, 1e-3f);
            for (int j = 1; j < n; j++)
                _pts.Add(a + d * (j / (float)n) + normal * (Hash(seed + k++) - 0.5f) * rough * S);
        }
        return _pts;
    }

    /// <summary>Marco de tinta dibujado a mano alrededor de un rectángulo.</summary>
    public void InkFrame(Rectangle r, float thick, Color color, int seed, float rough = 1.2f)
    {
        List<Vector2> p = Perimeter(r, seed, rough);
        for (int i = 0; i < p.Count; i++)
        {
            float t = thick * (0.8f + 0.4f * Hash(seed * 7 + i));
            Raylib.DrawLineEx(p[i], p[(i + 1) % p.Count], t, color);
            Raylib.DrawCircleV(p[i], t * 0.5f, color);
        }
    }

    /// <summary>Mancha de tinta muy suave detrás de un texto que flota sobre el mundo.</summary>
    public void Wash(Vector2 center, Vector2 radius, float alpha)
    {
        if (alpha <= 0.01f) return;
        // Muchas capas muy tenues: sin anillos visibles.
        for (int i = 0; i < 12; i++)
        {
            float k = 1 - i * 0.075f;
            Raylib.DrawEllipse((int)center.X, (int)center.Y, radius.X * k, radius.Y * k, A(Palette.Ink, 0.045f * alpha));
        }
    }

    /// <summary>
    /// Tarjeta de intertítulo: tinta casi opaca, doble filete de papel y adornos en las esquinas.
    /// Es el mismo lenguaje que el cartel del jefe, en pequeño.
    /// </summary>
    public void Panel(Rectangle r, float alpha, int seed)
    {
        if (alpha <= 0.01f) return;
        Raylib.DrawRectangleRec(new Rectangle(r.X + 6 * S, r.Y + 8 * S, r.Width, r.Height), A(Palette.Ink, 0.35f * alpha));
        Raylib.DrawRectangleRec(r, A(Card, 0.88f * alpha));
        // Fibras del papel negro, casi invisibles.
        for (int i = 0; i < 14; i++)
        {
            float y = r.Y + r.Height * Hash(seed + i * 3);
            float x0 = r.X + r.Width * Hash(seed + i * 5) * 0.6f;
            Raylib.DrawLineEx(new Vector2(x0, y), new Vector2(x0 + r.Width * (0.2f + 0.3f * Hash(seed + i)), y), 1, A(Palette.Parchment, 0.025f * alpha));
        }
        Color paper = A(Palette.Parchment, 0.9f * alpha);
        InkFrame(Inset(r, 10 * S), 2.4f * S, paper, seed, 0.9f);
        InkFrame(Inset(r, 16 * S), 1f * S, A(Palette.Parchment, 0.45f * alpha), seed + 11, 0.6f);
        foreach (Vector2 c in new[] { new Vector2(r.X, r.Y), new(r.X + r.Width, r.Y), new(r.X, r.Y + r.Height), new(r.X + r.Width, r.Y + r.Height) })
        {
            Vector2 dir = Vector2.Normalize(new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2) - c) * new Vector2(1, 1);
            Vector2 at = c + new Vector2(MathF.Sign(dir.X), MathF.Sign(dir.Y)) * 13 * S;
            Raylib.DrawPoly(at, 4, 4.5f * S, 45, paper);
            Raylib.DrawCircleV(at + new Vector2(MathF.Sign(dir.X) * 12 * S, 0), 1.4f * S, paper);
            Raylib.DrawCircleV(at + new Vector2(0, MathF.Sign(dir.Y) * 12 * S), 1.4f * S, paper);
        }
    }

    public static Rectangle Inset(Rectangle r, float d) => new(r.X + d, r.Y + d, r.Width - 2 * d, r.Height - 2 * d);

    /// <summary>
    /// El iris de cine mudo: todo lo que queda fuera de un círculo de borde irregular se tiñe de tinta.
    /// El borde "hierve" a 12 fps; <paramref name="ragged"/> alto parece tinta que invade el papel.
    /// </summary>
    public void Iris(Vector2 center, float radius, Color color, float ragged = 0.015f)
    {
        if (color.A == 0) return;
        const int n = 96;
        float far = MathF.Sqrt(W * W + H * H) + 40;
        if (radius >= far) return;
        int seed = Frame * 131;
        Span<Vector2> inner = stackalloc Vector2[n + 1];
        Span<Vector2> outer = stackalloc Vector2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            int j = i % n;
            float ang = j / (float)n * MathF.Tau;
            // Ruido de valor sobre el círculo: ondas amplias y un temblor fino.
            float coarse = Lerp(Hash(seed + j / 8), Hash(seed + (j / 8 + 1) % (n / 8)), Smooth(j % 8 / 8f));
            float fine = Hash(seed * 3 + j);
            float r = MathF.Max(0, radius * (1 + ragged * ((coarse - 0.5f) * 2.4f + (fine - 0.5f) * 0.6f)));
            var d = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            inner[i] = center + d * r;
            outer[i] = center + d * far;
        }
        for (int i = 0; i < n; i++)
        {
            Tri(inner[i], outer[i + 1], outer[i], color);
            Tri(inner[i], inner[i + 1], outer[i + 1], color);
        }
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Radio con el que el iris no tapa nada.</summary>
    public float IrisOpen => MathF.Sqrt(W * W + H * H) * 0.52f;

    // ------------------------------------------------------------------ teclas y ratón

    public float KeyHeight => 32 * S;

    public float KeyWidth(string label) => MathF.Max(32 * S, Measure(label, KeySize(label), Face.Bold, 1).X + 20 * S);

    float KeySize(string label) => (label.Length > 3 ? 13 : 16) * S;

    /// <summary>Tecla de marfil con borde de tinta y sombra dibujada. <paramref name="press"/> la hunde.</summary>
    public void Keycap(Vector2 center, string label, float alpha = 1, float press = 0)
    {
        float w = KeyWidth(label), h = KeyHeight, sink = 2.5f * S * press;
        var face = new Rectangle(center.X - w / 2, center.Y - h / 2 + sink - 1.5f * S, w, h);
        var side = new Rectangle(face.X, face.Y + 3.5f * S - sink, w, h);
        Raylib.DrawRectangleRounded(side, 0.3f, 6, A(Palette.Ink, 0.85f * alpha));
        Raylib.DrawRectangleRounded(face, 0.3f, 6, A(Ivory, alpha));
        Raylib.DrawRectangleRec(new Rectangle(face.X + 3 * S, face.Y + h - 6 * S, w - 6 * S, 3.5f * S), A(KeyDepth, 0.55f * alpha));
        InkFrame(face, 1.9f * S, A(Palette.Ink, alpha), Seed(label), 0.7f);
        Text(label, new Vector2(face.X + w / 2, face.Y + h / 2 - 1.5f * S), KeySize(label), A(Palette.Ink, alpha), Face.Bold);
    }

    public Vector2 MouseSize => new Vector2(26, 38) * S;

    /// <summary>Ratón ilustrado con el botón o la rueda que importa marcados en brasa.</summary>
    public void Mouse(Vector2 center, MouseMark mark, float alpha = 1, float press = 0)
    {
        Vector2 size = MouseSize;
        var body = new Rectangle(center.X - size.X / 2, center.Y - size.Y / 2 + 3 * S + press * 2 * S, size.X, size.Y);
        float split = body.Y + size.Y * 0.42f;
        Color ink = A(Palette.Ink, alpha), accent = A(EmberDim, alpha);

        // Cable.
        Vector2 top = new(body.X + size.X / 2, body.Y);
        Taper(top, top + new Vector2(-3 * S, -6 * S), 1.8f * S, ink, 2);
        Raylib.DrawRectangleRounded(new Rectangle(body.X, body.Y + 3.5f * S, size.X, size.Y), 0.9f, 10, A(Palette.Ink, 0.85f * alpha));
        Raylib.DrawRectangleRounded(body, 0.9f, 10, A(Ivory, alpha));
        if (mark is MouseMark.Left or MouseMark.Right)
        {
            int x = (int)(mark == MouseMark.Left ? body.X : body.X + size.X / 2);
            Raylib.BeginScissorMode(x, (int)body.Y, (int)MathF.Ceiling(size.X / 2), (int)(split - body.Y));
            Raylib.DrawRectangleRounded(body, 0.9f, 10, accent);
            Raylib.EndScissorMode();
        }
        Raylib.DrawLineEx(new Vector2(top.X, body.Y + 1), new Vector2(top.X, split), 1.6f * S, ink);
        Raylib.DrawLineEx(new Vector2(body.X + 1, split), new Vector2(body.X + size.X - 1, split), 1.6f * S, ink);
        var wheel = new Rectangle(top.X - 3 * S, body.Y + 5 * S, 6 * S, 10 * S);
        Raylib.DrawRectangleRounded(wheel, 1, 6, mark == MouseMark.Wheel ? accent : A(Ivory, alpha));
        Raylib.DrawRectangleRoundedLinesEx(wheel, 1, 6, 1.4f * S, ink);
        Raylib.DrawRectangleRoundedLinesEx(body, 0.9f, 10, 2 * S, ink);

        if (mark == MouseMark.Move)
        {
            // Cuatro flechitas: "mueve el ratón".
            for (int i = 0; i < 4; i++)
            {
                float ang = i * MathF.PI / 2;
                var d = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                var n = new Vector2(-d.Y, d.X);
                Vector2 tip = center + d * new Vector2(size.X * 0.5f + 13 * S, size.Y * 0.5f + 10 * S);
                Tri(tip, tip - d * 7 * S + n * 5 * S, tip - d * 7 * S - n * 5 * S, ink);
            }
        }
    }

    public Vector2 GlyphSize(Glyph g) => g.Kind switch
    {
        GlyphKind.Key => new Vector2(KeyWidth(g.Key), KeyHeight),
        GlyphKind.Mouse => MouseSize + (g.Mouse == MouseMark.Move ? new Vector2(40, 30) * S : new Vector2(0, 6 * S)),
        GlyphKind.Wasd => new Vector2(KeyWidth("W") * 3 + 8 * S, KeyHeight * 2 + 6 * S),
        _ => new Vector2(Measure("o", 17 * S, Face.Body).X + 6 * S, KeyHeight),
    };

    public void DrawGlyph(Glyph g, Vector2 center, float alpha = 1, float press = 0)
    {
        switch (g.Kind)
        {
            case GlyphKind.Key: Keycap(center, g.Key, alpha, press); break;
            case GlyphKind.Mouse: Mouse(center, g.Mouse, alpha, press); break;
            case GlyphKind.Or: Text("o", center, 17 * S, A(Palette.Parchment, 0.8f * alpha), Face.Body, shadow: true); break;
            case GlyphKind.Wasd:
            {
                float kw = KeyWidth("W"), gap = 4 * S, kh = KeyHeight;
                Keycap(center - new Vector2(0, (kh + gap) / 2), "W", alpha, press);
                for (int i = -1; i <= 1; i++)
                    Keycap(center + new Vector2(i * (kw + gap), (kh + gap) / 2), i switch { -1 => "A", 0 => "S", _ => "D" }, alpha, press);
                break;
            }
        }
    }

    /// <summary>Una fila de iconos centrada; devuelve su ancho.</summary>
    public float GlyphRowWidth(Glyph[] glyphs) => glyphs.Sum(g => GlyphSize(g).X) + (glyphs.Length - 1) * 8 * S;

    public void GlyphRow(Glyph[] glyphs, Vector2 center, float alpha = 1, float press = 0)
    {
        float x = center.X - GlyphRowWidth(glyphs) / 2;
        foreach (Glyph g in glyphs)
        {
            float gw = GlyphSize(g).X;
            DrawGlyph(g, new Vector2(x + gw / 2, center.Y), alpha, press);
            x += gw + 8 * S;
        }
    }

    /// <summary>
    /// Indicación contextual: la tecla y el verbo, sin rectángulo detrás.
    /// <paramref name="t"/> es su visibilidad (0-1): entra con un fundido y sube unos píxeles.
    /// </summary>
    public void Prompt(Vector2 anchor, string key, string verb, float t)
    {
        if (t <= 0.01f) return;
        float a = Smooth(t);
        Vector2 p = anchor + new Vector2(0, (1 - a) * 8 * S);
        Wash(p + new Vector2(0, 16 * S), new Vector2(95, 44) * S, 0.8f * a);
        Keycap(p, key, a);
        Heading(verb, p + new Vector2(0, 32 * S), 15 * S, A(Palette.Parchment, a), dashes: false, shadow: true);
    }

    /// <summary>Pista de pie de pantalla ("[Esc] Volver"); devuelve la zona sensible para el ratón.</summary>
    public Rectangle Hint(Vector2 center, string key, string label, float alpha = 1, float hover = 0)
    {
        float kw = KeyWidth(key), lw = Measure(label, 17 * S, Face.Body).X, gap = 10 * S;
        float total = kw + gap + lw, x = center.X - total / 2;
        if (alpha <= 0.01f) return HintRect(center, key, label);
        Keycap(new Vector2(x + kw / 2, center.Y), key, alpha, hover * 0.4f);
        Color c = A(Palette.Mix(Palette.Parchment, Ivory, hover), (0.75f + 0.25f * hover) * alpha);
        Text(label, new Vector2(x + kw + gap, center.Y), 17 * S, c, Face.Body, align: Align.Left, shadow: true);
        if (hover > 0.01f) Taper(new Vector2(x + kw + gap, center.Y + 12 * S), new Vector2(x + kw + gap + lw * hover, center.Y + 12 * S), 1.6f * S, c, 3);
        return HintRect(center, key, label);
    }

    public Rectangle HintRect(Vector2 center, string key, string label)
    {
        float total = KeyWidth(key) + 10 * S + Measure(label, 17 * S, Face.Body).X;
        return new Rectangle(center.X - total / 2 - 6 * S, center.Y - KeyHeight / 2 - 4 * S, total + 12 * S, KeyHeight + 8 * S);
    }

    // ------------------------------------------------------------------ iconos

    /// <summary>Llamita de brasa: el indicador de selección de todos los menús.</summary>
    public void Flame(Vector2 bottom, float height, float alpha = 1, float flare = 0)
    {
        int f = Frame;
        float h = height * (0.93f + 0.14f * Hash(f * 7 + 1) + flare * 0.3f), w = height * 0.3f;
        float sway = (Hash(f * 13 + 5) - 0.5f) * w * 0.8f;
        Draw(1.3f, A(Palette.Ink, 0.9f * alpha));
        Draw(1f, A(Palette.Ember, alpha));
        Draw(0.55f, A(new Color(255, 214, 130, 255), alpha));

        void Draw(float k, Color c)
        {
            Vector2 center = bottom - new Vector2(0, w * k);
            Vector2 tip = bottom - new Vector2(-sway * k, h * k);
            const int n = 10;
            Vector2 prev = tip;
            for (int i = 0; i <= n; i++)
            {
                // Semicírculo inferior, de un lado del tallo al otro.
                float ang = -MathF.PI * 0.15f + i / (float)n * MathF.PI * 1.3f;
                Vector2 p = center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * w * k;
                Tri(center, prev, p, c);
                prev = p;
            }
            Tri(center, prev, tip, c);
        }
    }

    /// <summary>
    /// Frasco de brasa. <paramref name="fill"/> es el nivel del líquido; vacío queda solo la silueta
    /// de tinta, así que se lee lleno/vacío sin depender del color.
    /// </summary>
    public void Flask(Vector2 center, float size, float fill, float alpha = 1, float glow = 1)
    {
        float rb = size * 0.34f;
        Vector2 belly = center + new Vector2(0, size * 0.14f);
        var neck = new Rectangle(center.X - rb * 0.36f, belly.Y - rb - size * 0.24f, rb * 0.72f, size * 0.28f);
        var lip = new Rectangle(center.X - rb * 0.5f, neck.Y - size * 0.03f, rb, size * 0.07f);
        var cork = new Rectangle(center.X - rb * 0.3f, lip.Y - size * 0.1f, rb * 0.6f, size * 0.11f);
        Color ink = A(Palette.Ink, alpha);

        if (fill > 0.01f && glow > 0)
            Raylib.DrawCircleV(belly, rb * 1.45f, A(Palette.Ember, 0.12f * glow * alpha * (0.85f + 0.3f * Hash(Frame * 3 + (int)center.X))));
        Raylib.DrawCircleV(belly + new Vector2(1.5f, 2.5f) * S, rb + 1.5f * S, A(Palette.Ink, 0.55f * alpha));

        // Vidrio vacío.
        Raylib.DrawCircleV(belly, rb, A(new Color(40, 32, 28, 255), 0.9f * alpha));
        Raylib.DrawRectangleRec(neck, A(new Color(40, 32, 28, 255), 0.9f * alpha));
        if (fill > 0.01f)
        {
            float level = belly.Y + rb - 2 * rb * Math.Clamp(fill, 0, 1) * 0.92f;
            Raylib.BeginScissorMode((int)(belly.X - rb - 2), (int)level, (int)(rb * 2 + 4), (int)(belly.Y + rb - level + 2));
            Raylib.DrawCircleV(belly, rb, A(Palette.Mix(Palette.Ember, Palette.Burgundy, 0.35f), alpha));
            Raylib.DrawCircleV(belly + new Vector2(0, rb * 0.2f), rb * 0.55f, A(Palette.Mix(Palette.Ember, Brass, 0.4f), alpha));
            Raylib.EndScissorMode();
            Raylib.DrawLineEx(new Vector2(belly.X - rb * 0.8f, level + 1), new Vector2(belly.X + rb * 0.8f, level + 1), 1.2f * S, A(Brass, 0.7f * alpha));
        }
        Raylib.DrawRing(belly, rb - 1.8f * S, rb + 0.4f * S, 0, 360, 24, ink);
        Raylib.DrawRectangleLinesEx(neck, 1.6f * S, ink);
        Raylib.DrawRectangleRec(lip, ink);
        Raylib.DrawRectangleRec(cork, A(Palette.Umber, alpha));
        Raylib.DrawRectangleLinesEx(cork, 1.2f * S, ink);
        // Brillo del vidrio.
        Raylib.DrawRing(belly, rb * 0.62f, rb * 0.74f, 200, 245, 8, A(Palette.Parchment, (fill > 0.01f ? 0.55f : 0.3f) * alpha));
    }

    /// <summary>Medallón de tinta a la izquierda de la vida: una gota de sangre dentro de un anillo.</summary>
    public void Crest(Vector2 c, float r, Color accent, float alpha, float glow = 0)
    {
        if (glow > 0) Raylib.DrawCircleV(c, r * 1.6f, A(Brass, 0.25f * glow * alpha));
        Raylib.DrawCircleV(c + new Vector2(1.5f, 2.5f) * S, r, A(Palette.Ink, 0.5f * alpha));
        Raylib.DrawCircleV(c, r, A(Card, alpha));
        Raylib.DrawRing(c, r - 2.2f * S, r, 0, 360, 28, A(Palette.Parchment, 0.85f * alpha));
        Raylib.DrawRing(c, r + 0.2f * S, r + 1.8f * S, 0, 360, 28, A(Palette.Ink, alpha));
        // Gota: círculo abajo y punta arriba.
        float d = r * 0.36f;
        Vector2 drop = c + new Vector2(0, d * 0.45f);
        Raylib.DrawCircleV(drop, d, A(accent, alpha));
        Tri(drop + new Vector2(-d * 0.92f, -d * 0.35f), drop + new Vector2(d * 0.92f, -d * 0.35f), drop + new Vector2(0, -d * 2.3f), A(accent, alpha));
        Raylib.DrawCircleV(drop + new Vector2(-d * 0.35f, -d * 0.1f), d * 0.25f, A(Palette.Parchment, 0.6f * alpha));
    }

    // ------------------------------------------------------------------ barras

    /// <summary>
    /// Barra de pigmento: fondo de papel quemado, rastro claro del daño, pigmento con vetas y
    /// borde irregular, y el marco de tinta encima.
    /// </summary>
    public void Vital(Rectangle r, VitalLook v)
    {
        float a = v.Alpha;
        if (a <= 0.01f) return;
        if (v.Tremble > 0)
        {
            float s = v.Tremble * 2 * S;
            r.X += (Hash(Frame * 5 + v.Seed) - 0.5f) * s;
            r.Y += (Hash(Frame * 9 + v.Seed) - 0.5f) * s;
        }
        Raylib.DrawRectangleRec(new Rectangle(r.X + 2 * S, r.Y + 3 * S, r.Width, r.Height), A(Palette.Ink, 0.5f * a));
        Raylib.DrawRectangleRec(r, A(new Color(34, 27, 24, 255), 0.92f * a));

        float fillW = r.Width * Math.Clamp(v.Value, 0, 1);
        float lagW = r.Width * Math.Clamp(v.Lag, 0, 1);
        if (lagW > fillW) Raylib.DrawRectangleRec(new Rectangle(r.X + fillW, r.Y, lagW - fillW, r.Height), A(Palette.Mix(v.Fill, Palette.Parchment, 0.4f), 0.85f * a));
        float gainW = r.Width * Math.Clamp(v.Value + v.Gain, 0, 1);
        if (gainW > fillW) Raylib.DrawRectangleRec(new Rectangle(r.X + fillW, r.Y, gainW - fillW, r.Height), A(Brass, 0.45f * a));

        if (fillW > 0.5f)
        {
            Color fill = Palette.Mix(v.Fill, Palette.Ink, 0.28f * v.Pulse);
            Raylib.DrawRectangleRec(new Rectangle(r.X, r.Y, fillW, r.Height), A(fill, a));
            // Vetas del pincel y un filo de luz arriba.
            Color vein = A(Palette.Mix(fill, Palette.Ink, 0.35f), 0.7f * a);
            for (int i = 0; i < 2; i++)
            {
                float y = r.Y + r.Height * (i == 0 ? 0.38f : 0.72f);
                float x0 = r.X + r.Width * 0.08f * Hash(v.Seed + i), x1 = r.X + fillW - 6 * S * Hash(v.Seed + i + 5);
                if (x1 > x0) Raylib.DrawLineEx(new Vector2(x0, y), new Vector2(x1, y), MathF.Max(1, r.Height * 0.08f), vein);
            }
            Raylib.DrawLineEx(new Vector2(r.X, r.Y + 1.2f * S), new Vector2(r.X + fillW, r.Y + 1.2f * S), MathF.Max(1, r.Height * 0.1f), A(Palette.Mix(fill, Palette.Parchment, 0.35f), 0.8f * a));
            // Punta del pigmento: se deshace en picos.
            int tipSeed = v.Seed * 31 + (int)(fillW / (3 * S));
            float tipX = r.X + fillW;
            for (int i = 0; i < 3; i++)
            {
                float y0 = r.Y + r.Height * i / 3f, y1 = r.Y + r.Height * (i + 1) / 3f;
                float reach = MathF.Min(r.Width - fillW, (2 + 5 * Hash(tipSeed + i)) * S);
                if (reach > 0.5f) Tri(new Vector2(tipX, y0), new Vector2(tipX, y1), new Vector2(tipX + reach, (y0 + y1) / 2), A(fill, a));
            }
        }
        if (v.Hatch > 0.01f)
        {
            // Rayado de tinta: agotado, se lee también en blanco y negro.
            Raylib.BeginScissorMode((int)r.X, (int)r.Y, (int)r.Width, (int)MathF.Ceiling(r.Height));
            for (float x = r.X - r.Height; x < r.X + r.Width; x += 5 * S)
                Raylib.DrawLineEx(new Vector2(x, r.Y + r.Height), new Vector2(x + r.Height, r.Y), 1.2f * S, A(Palette.Ink, 0.55f * v.Hatch * a));
            Raylib.EndScissorMode();
        }
        InkFrame(new Rectangle(r.X - 2 * S, r.Y - 2 * S, r.Width + 4 * S, r.Height + 4 * S), 2.2f * S, A(Palette.Ink, a), v.Seed, 0.8f);
        Raylib.DrawLineEx(new Vector2(r.X + 3 * S, r.Y - 3.4f * S), new Vector2(r.X + r.Width - 3 * S, r.Y - 3.4f * S), 1 * S, A(Palette.Parchment, 0.3f * a));
    }

    public void Dispose()
    {
        foreach (Font f in _owned) Raylib.UnloadFont(f);
    }
}
