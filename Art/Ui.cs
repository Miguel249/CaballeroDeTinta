using System.Numerics;
using Raylib_cs;

namespace CaballeroDeTinta.Art;

enum Face { Body, Bold, Display }
enum Align { Center, Left, Right }
enum MouseMark { None, Left, Right, Wheel, Move }
enum GlyphKind { Key, Mouse, Wasd, Or }
/// <summary>Con qué se está jugando: decide si las pistas de los menús muestran teclas o botones del mando.</summary>
enum InputGlyphMode { KeyboardMouse, Gamepad }

/// <summary>Un icono de control: una tecla, el ratón, el grupo WASD o la conjunción "o".</summary>
readonly record struct Glyph(GlyphKind Kind, string Key = "", MouseMark Mouse = MouseMark.None)
{
    public static Glyph K(string key) => new(GlyphKind.Key, key);
    public static Glyph M(MouseMark mark) => new(GlyphKind.Mouse, Mouse: mark);
    public static readonly Glyph Wasd = new(GlyphKind.Wasd);
    public static readonly Glyph Or = new(GlyphKind.Or);
}

/// <summary>
/// El sistema visual de la interfaz: tipografía, trazos de pluma, pinceladas, teclas dibujadas,
/// emblemas y el iris de cine. Todo se mide en unidades de diseño de 1280×720 multiplicadas por
/// <see cref="S"/>, así que la composición se conserva en cualquier resolución.
/// Aquí no hay estado de juego: cada pantalla decide qué dibujar y este kit decide cómo.
/// <para>
/// Lo que "hierve" (marcos, llamas, iconos, adornos) cambia de pose a 12 fps con un ciclo corto de
/// dibujos, como la animación tradicional. El texto y las zonas sensibles nunca tiemblan.
/// </para>
/// </summary>
sealed partial class Ui : IDisposable
{
    // ------------------------------------------------------------------ paleta de interfaz

    public static readonly Color Ivory = new(236, 226, 202, 255);
    public static readonly Color KeyDepth = new(196, 178, 146, 255);
    public static readonly Color Card = new(17, 14, 13, 255);
    public static readonly Color Blood = new(128, 30, 34, 255);      // carmesí apagado de la vida
    public static readonly Color Moss = new(104, 116, 70, 255);      // verde oliva del aguante
    public static readonly Color Brass = new(208, 170, 92, 255);     // dorado viejo de la curación
    public static readonly Color EmberDim = new(206, 104, 38, 255);  // brasa para marcar botones
    /// <summary>Papel quemado del fondo de las barras y del vidrio vacío.</summary>
    static readonly Color Well = Palette.Mix(Palette.Charcoal, Palette.DarkUmber, 0.18f);

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
    /// <summary>Último dispositivo usado en los menús.</summary>
    public InputGlyphMode GlyphMode { get; set; }

    readonly Font _body, _bold, _display;
    readonly List<Font> _owned = [];
    readonly UiSkin _skin;
    readonly Dictionary<string, string> _upper = [];

    public Ui()
    {
        // Latin-1 completo (á é í ó ú ñ ü ¿ ¡ ·  ×) más la raya y comillas tipográficas.
        int[] cps = Enumerable.Range(32, 224).Concat([0x2014, 0x2013, 0x2019, 0x201C, 0x201D, 0x2026]).ToArray();
        _body = Load(cps, 72, @"C:\Windows\Fonts\georgia.ttf", @"C:\Windows\Fonts\constan.ttf", @"C:\Windows\Fonts\times.ttf");
        _bold = Load(cps, 80, @"C:\Windows\Fonts\georgiab.ttf", @"C:\Windows\Fonts\constanb.ttf", @"C:\Windows\Fonts\timesbd.ttf");
        // Títulos: una romana de libro antiguo si el sistema la tiene.
        _display = Load(cps, 128, @"C:\Windows\Fonts\GARABD.TTF", @"C:\Windows\Fonts\BKANT.TTF", @"C:\Windows\Fonts\palab.ttf", @"C:\Windows\Fonts\georgiab.ttf");
        _skin = new UiSkin(Path.Combine(AppContext.BaseDirectory, "Assets", "UI"));
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
    static float Lerp(float a, float b, float t) => a + (b - a) * t;

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

    /// <summary>Ruido de valor suave en [0, 1): ondulaciones amplias en vez de dientes de sierra.</summary>
    public static float Noise(int seed, float x)
    {
        int i = (int)MathF.Floor(x);
        return Lerp(Hash(seed * 31 + i), Hash(seed * 31 + i + 1), Smooth(x - i));
    }

    public static int Seed(string s) { int h = 17; foreach (char c in s) h = h * 31 + c; return h; }

    // ------------------------------------------------------------------ hervor

    /// <summary>Pose del hervor (0-2) a 12 fps: un ciclo corto de dibujos casi iguales, nunca ruido nuevo.</summary>
    public int Boil => Frame % 3;
    /// <summary>Hervor a 6 fps para superficies grandes: menos parpadeo en bordes largos.</summary>
    public int BoilSlow => Frame / 2 % 3;
    /// <summary>Desviación en [-1, 1] que solo cambia con la pose del hervor.</summary>
    public float Jit(int seed, bool slow = false) => Hash(seed * 7919 + (slow ? BoilSlow : Boil) * 104729) * 2 - 1;

    // ------------------------------------------------------------------ tipografía

    Font FontOf(Face f) => f switch { Face.Bold => _bold, Face.Display => _display, _ => _body };

    /// <summary>Mayúsculas cacheadas: los rótulos se piden cada fotograma y no deben crear cadenas.</summary>
    public string Upper(string s)
    {
        if (!_upper.TryGetValue(s, out string? u)) _upper[s] = u = s.ToUpperInvariant();
        return u;
    }

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
        string up = Upper(text);
        float tracking = size * 0.22f / S;
        Text(up, at, size, color, Face.Display, tracking, shadow: shadow);
        if (!dashes) return;
        float half = Measure(up, size, Face.Display, tracking).X / 2;
        int seed = Seed(text);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 a = at + new Vector2(side * (half + 12 * S), size * 0.04f);
            Stroke(a, a + new Vector2(side * 30 * S, 0), 1.8f * S, color, seed + side, 0.3f, tipIn: 0.1f, tipOut: 0.8f);
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

    /// <summary>
    /// Trazo de pluma a lo largo de una polilínea, con un grosor por punto (la presión).
    /// Se rellena como una tira de triángulos: sin huecos en los codos ni puntos dobles con transparencia.
    /// </summary>
    public static void Pen(ReadOnlySpan<Vector2> pts, ReadOnlySpan<float> width, Color c)
    {
        int n = pts.Length;
        if (n < 2 || c.A == 0) return;
        Vector2 prevL = default, prevR = default;
        for (int i = 0; i < n; i++)
        {
            Vector2 d = i == 0 ? pts[1] - pts[0] : i == n - 1 ? pts[n - 1] - pts[n - 2] : pts[i + 1] - pts[i - 1];
            float len = d.Length();
            d = len < 1e-4f ? Vector2.UnitX : d / len;
            Vector2 nrm = new Vector2(-d.Y, d.X) * (width[i] * 0.5f);
            Vector2 l = pts[i] + nrm, r = pts[i] - nrm;
            if (i > 0) { Tri(prevL, prevR, r, c); Tri(prevL, r, l, c); }
            prevL = l; prevR = r;
        }
    }

    /// <summary>Contorno cerrado de grosor irregular (la mano aprieta y afloja al rodear la forma).</summary>
    public static void PenLoop(ReadOnlySpan<Vector2> pts, float thick, Color c, int seed)
    {
        int n = pts.Length;
        if (n < 3 || c.A == 0) return;
        Vector2 prevL = default, prevR = default;
        for (int i = 0; i <= n; i++)
        {
            int k = i % n;
            Vector2 d = pts[(k + 1) % n] - pts[(k - 1 + n) % n];
            float len = d.Length();
            d = len < 1e-4f ? Vector2.UnitX : d / len;
            float w = thick * (0.75f + 0.5f * Noise(seed, k * 0.6f));
            Vector2 nrm = new Vector2(-d.Y, d.X) * (w * 0.5f);
            Vector2 l = pts[k] + nrm, r = pts[k] - nrm;
            if (i > 0) { Tri(prevL, prevR, r, c); Tri(prevL, r, l, c); }
            prevL = l; prevR = r;
        }
    }

    /// <summary>
    /// Trazo recto dibujado a mano: se desvía un pelo, la presión varía y las puntas se afinan
    /// (<paramref name="tipIn"/> y <paramref name="tipOut"/> son la fracción del largo que ocupa cada afinado).
    /// La forma es fija por <paramref name="seed"/>; con <paramref name="boil"/> hierve despacio.
    /// </summary>
    public void Stroke(Vector2 a, Vector2 b, float thick, Color c, int seed, float wobble = 0.7f, bool boil = false, float tipIn = 0.22f, float tipOut = 0.22f)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 0.5f || c.A == 0) return;
        Vector2 n = new(-d.Y / len, d.X / len);
        int segs = Math.Clamp((int)(len / (9 * S)), 2, 63);
        Span<Vector2> p = stackalloc Vector2[segs + 1];
        Span<float> w = stackalloc float[segs + 1];
        int sd = boil ? seed + BoilSlow * 7919 : seed;
        float span = len / (40 * S);
        for (int i = 0; i <= segs; i++)
        {
            float t = i / (float)segs;
            float off = (Noise(sd, t * span) - 0.5f) * 2 * wobble * S * MathF.Sin(MathF.PI * t);
            float press = 0.8f + 0.4f * Noise(seed + 101, t * span * 1.3f);
            float taper = 1;
            if (tipIn > 0) taper = MathF.Min(taper, t / tipIn);
            if (tipOut > 0) taper = MathF.Min(taper, (1 - t) / tipOut);
            p[i] = a + d * t + n * off;
            w[i] = thick * press * (0.25f + 0.75f * Smooth(taper));
        }
        Pen(p, w, c);
    }

    /// <summary>Triángulos en abanico: polígonos convexos o con forma de estrella respecto a su centro.</summary>
    public static void Fill(ReadOnlySpan<Vector2> pts, Color c)
    {
        if (pts.Length < 3 || c.A == 0) return;
        Vector2 center = Vector2.Zero;
        foreach (Vector2 p in pts) center += p;
        Fill(pts, c, center / pts.Length);
    }

    public static void Fill(ReadOnlySpan<Vector2> pts, Color c, Vector2 center)
    {
        if (pts.Length < 3 || c.A == 0) return;
        for (int i = 0; i < pts.Length; i++) Tri(center, pts[i], pts[(i + 1) % pts.Length], c);
    }

    static void Shift(ReadOnlySpan<Vector2> src, Vector2 by, Span<Vector2> dst)
    {
        for (int i = 0; i < src.Length; i++) dst[i] = src[i] + by;
    }

    /// <summary>
    /// Contorno de un rectángulo de esquinas redondeadas trazado a mano. Cada esquina y cada tramo se
    /// desvían según la semilla (<paramref name="rough"/>); <paramref name="boil"/> añade el temblor de
    /// la pose actual. <paramref name="step"/> (en unidades de diseño) decide lo menudo del borde.
    /// Escribe en <paramref name="dst"/> (12 + 4·<paramref name="maxPerEdge"/> puntos como mucho) y devuelve cuántos.
    /// </summary>
    public int RoundRect(Rectangle r, float radius, int seed, float rough, float boil, Span<Vector2> dst, float step = 16, int maxPerEdge = 8)
    {
        radius = MathF.Max(0.5f, MathF.Min(radius, MathF.Min(r.Width, r.Height) * 0.5f));
        Span<Vector2> corner = stackalloc Vector2[12];
        for (int c = 0; c < 4; c++)
        {
            // Arriba-izquierda, arriba-derecha, abajo-derecha, abajo-izquierda: sentido horario en pantalla.
            Vector2 cc = c switch
            {
                0 => new(r.X + radius, r.Y + radius),
                1 => new(r.X + r.Width - radius, r.Y + radius),
                2 => new(r.X + r.Width - radius, r.Y + r.Height - radius),
                _ => new(r.X + radius, r.Y + r.Height - radius),
            };
            Vector2 jit = new Vector2(Hash(seed + c * 13) - 0.5f, Hash(seed + c * 13 + 5) - 0.5f) * rough * 1.4f * S;
            if (boil > 0) jit += new Vector2(Jit(seed + c * 3), Jit(seed + c * 3 + 1)) * boil * S;
            float a0 = MathF.PI + c * MathF.PI / 2;
            for (int j = 0; j < 3; j++)
            {
                float ang = a0 + j * MathF.PI / 4;
                corner[c * 3 + j] = cc + jit + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * radius;
            }
        }
        int k = 0, q = 0;
        for (int c = 0; c < 4; c++)
        {
            for (int j = 0; j < 3; j++) dst[k++] = corner[c * 3 + j];
            Vector2 from = corner[c * 3 + 2], to = corner[(c + 1) % 4 * 3];
            Vector2 d = to - from;
            float len = d.Length();
            int m = Math.Clamp((int)(len / (step * S)) - 1, 0, maxPerEdge);
            Vector2 normal = new Vector2(d.Y, -d.X) / MathF.Max(len, 1e-3f);
            for (int j = 1; j <= m; j++)
            {
                float off = (Hash(seed * 3 + q++) - 0.5f) * rough * S;
                if (boil > 0) off += Jit(seed * 5 + q) * boil * 0.5f * S;
                dst[k++] = from + d * (j / (float)(m + 1)) + normal * off;
            }
        }
        return k;
    }

    /// <summary>Marco de tinta dibujado a mano alrededor de un rectángulo.</summary>
    public void InkFrame(Rectangle r, float thick, Color color, int seed, float rough = 1.2f, float boil = 0)
    {
        Span<Vector2> p = stackalloc Vector2[44];
        int n = RoundRect(r, 4 * S, seed, rough, boil, p);
        PenLoop(p[..n], thick, color, seed);
    }

    /// <summary>Filete ornamental: dos trazos que se afinan, un rombo y dos puntos.</summary>
    public void Divider(Vector2 at, float half, Color color)
    {
        if (half < 1 || color.A == 0) return;
        if (_skin.Draw(UiArt.Divider, new Rectangle(at.X, at.Y, half * 2 + 20 * S, 22 * S), color)) return;
        Stroke(at - new Vector2(10 * S, 0), at - new Vector2(half, 0), 2 * S, color, 311, 0.35f, tipIn: 0.08f, tipOut: 0.85f);
        Stroke(at + new Vector2(10 * S, 0), at + new Vector2(half, 0), 2 * S, color, 312, 0.35f, tipIn: 0.08f, tipOut: 0.85f);
        Raylib.DrawPoly(at, 4, 5.5f * S, 45, color);
        Raylib.DrawCircleV(at - new Vector2(half + 7 * S, 0), 1.6f * S, color);
        Raylib.DrawCircleV(at + new Vector2(half + 7 * S, 0), 1.6f * S, color);
    }

    /// <summary>
    /// Filete con volutas: dos trazos que nacen de un rombo, se afinan y acaban enroscándose.
    /// <paramref name="t"/> (0-1) lo dibuja desde el centro; las volutas llegan al final.
    /// </summary>
    public void Flourish(Vector2 at, float half, Color color, float t = 1, int seed = 5)
    {
        if (t <= 0.01f || half < 4 || color.A == 0) return;
        if (_skin.Draw(UiArt.Divider, new Rectangle(at.X, at.Y, (half * 2 + 20 * S) * Smooth(t), 22 * S), color)) return;
        float reach = half * Smooth(t), curl = Smooth((t - 0.6f) / 0.4f);
        float r = 6.5f * S;
        const int straight = 8, spiral = 12;
        Span<Vector2> p = stackalloc Vector2[straight + spiral];
        Span<float> w = stackalloc float[straight + spiral];
        for (int side = -1; side <= 1; side += 2)
        {
            float end = reach - (curl > 0 ? r : 0);
            for (int i = 0; i < straight; i++)
            {
                float k = i / (float)(straight - 1);
                float x = 12 * S + (end - 12 * S) * k;
                p[i] = at + new Vector2(side * x, (Noise(seed + side, k * 3) - 0.5f) * 0.8f * S);
                w[i] = 2.2f * S * (1 - k * 0.45f) * (i == 0 ? 0.5f : 1);
            }
            int count = straight;
            if (curl > 0)
            {
                // La voluta sube por fuera y vuelve hacia dentro, cerrándose.
                Vector2 cc = at + new Vector2(side * end, -r);
                int nSpiral = Math.Max(2, (int)(spiral * curl));
                for (int i = 1; i <= nSpiral; i++)
                {
                    float k = i / (float)spiral;
                    float ang = MathF.PI / 2 - k * MathF.PI * 1.6f;
                    float rr = r * (1 - 0.55f * k);
                    p[count] = cc + new Vector2(side * MathF.Cos(ang) * rr, MathF.Sin(ang) * rr);
                    w[count] = 1.2f * S * (1 - k * 0.6f);
                    count++;
                }
            }
            Pen(p[..count], w[..count], color);
            if (curl > 0.95f) Raylib.DrawCircleV(p[count - 1], 1.4f * S, color);
        }
        Raylib.DrawPoly(at, 4, 5 * S, 45, color);
        Raylib.DrawPoly(at, 4, 2.2f * S, 45, A(Palette.Ink, color.A / 255f));
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

    /// <summary>Mancha de tinta de borde irregular con algunas gotas sueltas alrededor.</summary>
    public void Blot(Vector2 c, Vector2 r, Color col, int seed, float rough = 0.22f, bool boil = false)
    {
        if (col.A == 0) return;
        const int n = 48, lobe = 6;
        Span<Vector2> p = stackalloc Vector2[n];
        int bs = boil ? BoilSlow * 977 : 0;
        for (int i = 0; i < n; i++)
        {
            float ang = i / (float)n * MathF.Tau;
            float coarse = Lerp(Hash(seed + i / lobe), Hash(seed + (i / lobe + 1) % (n / lobe)), Smooth(i % lobe / (float)lobe));
            float fine = Hash(seed * 3 + i + bs);
            float k = 1 + rough * ((coarse - 0.5f) * 1.6f + (fine - 0.5f) * 0.3f);
            p[i] = c + new Vector2(MathF.Cos(ang) * r.X, MathF.Sin(ang) * r.Y) * k;
        }
        Fill(p, col, c);
        float drop = MathF.Min(1, r.Y / (40 * S));
        for (int i = 0; i < 4; i++)
        {
            float ang = Hash(seed + 50 + i) * MathF.Tau;
            float dist = 1.12f + 0.3f * Hash(seed + 60 + i);
            Raylib.DrawCircleV(c + new Vector2(MathF.Cos(ang) * r.X, MathF.Sin(ang) * r.Y) * dist, (1.5f + 3 * Hash(seed + 70 + i)) * S * drop, col);
        }
    }

    /// <summary>
    /// Pincelada ancha de <paramref name="from"/> a <paramref name="to"/>: entra redonda, carga pigmento en
    /// el cuerpo y se deshace en cerdas secas al final. <paramref name="reveal"/> (0-1) es cuánto ha
    /// recorrido el pincel. El contorno grande es fijo; solo el borde fino hierve, y despacio.
    /// Si existe la ilustración <paramref name="art"/> en Assets/UI, se dibuja esa en su lugar.
    /// </summary>
    public void Swath(Vector2 from, Vector2 to, float width, Color col, int seed, float reveal = 1, UiArt? art = null)
    {
        if (col.A == 0 || reveal <= 0.01f) return;
        Vector2 d = to - from;
        float len = d.Length();
        if (len < 1) return;
        Vector2 u = d / len, n = new(-u.Y, u.X);
        if (art is { } key && _skin.Has(key))
        {
            Vector2 mid = from + d * (reveal * 0.5f);
            _skin.Draw(key, new Rectangle(mid.X, mid.Y, len * reveal, width * 1.15f), col, MathF.Atan2(u.Y, u.X) * 180 / MathF.PI);
            return;
        }

        // El pincel se pinta por carriles (las cerdas), que comparten bordes exactos: con transparencia no
        // hay costuras. Todos cargan tinta hasta ~70 % del recorrido; luego cada carril se queda seco a un
        // largo distinto, más cortos hacia los bordes, y el final se deshace solo.
        const int segs = 40;
        int lanes = Math.Clamp((int)(width / (5 * S)), 8, 36);
        float bend = (Hash(seed + 3) - 0.5f) * 0.25f * width;
        float cap = MathF.Min(0.3f, width * 0.42f / len); // cabeza redonda
        int bs = BoilSlow * 613;
        for (int lane = 0; lane < lanes; lane++)
        {
            float f0 = -1 + 2f * lane / lanes, f1 = -1 + 2f * (lane + 1) / lanes, fc = (f0 + f1) / 2;
            float dry = 0.68f + 0.3f * Hash(seed + 500 + lane) * (1 - 0.55f * MathF.Abs(fc)) + 0.04f * Noise(seed + 7, lane * 0.8f);
            float end = MathF.Min(dry, reveal);
            int m = Math.Max(1, (int)MathF.Ceiling(segs * end));
            Vector2 p0 = default, p1 = default;
            for (int i = 0; i <= m; i++)
            {
                // Paso común a todos los carriles: los vecinos comparten exactamente sus bordes.
                float t = MathF.Min(i / (float)segs, end);
                Vector2 spine = from + d * t + n * (bend * MathF.Sin(MathF.PI * t));
                float x = MathF.Min(1, t / cap);
                float head = MathF.Sqrt(MathF.Max(0, 1 - (1 - x) * (1 - x)));
                float tail = 1 - 0.28f * Smooth((t - 0.42f) / 0.4f);
                float half = width * 0.5f * head * tail * (1 + 0.05f * MathF.Sin(t * 9 + seed));
                float up = half * (1 + 0.2f * (Noise(seed, t * 7) - 0.5f) + 0.08f * (Noise(seed + 2, t * 23) - 0.5f)) + (Hash(seed + i + bs) - 0.5f) * 0.012f * width;
                float dn = half * (1 + 0.2f * (Noise(seed + 9, t * 7) - 0.5f) + 0.08f * (Noise(seed + 11, t * 23) - 0.5f)) + (Hash(seed + 40 + i + bs) - 0.5f) * 0.012f * width;
                // Al final del carril la cerda se afina hacia su centro: quedan huecos secos entre cerdas.
                float squeeze = dry <= reveal ? 0.2f + 0.8f * Smooth((dry - t) / 0.07f) : 1;
                float a0 = fc + (f0 - fc) * squeeze, a1 = fc + (f1 - fc) * squeeze;
                Vector2 q0 = spine + n * (a0 >= 0 ? a0 * up : a0 * dn);
                Vector2 q1 = spine + n * (a1 >= 0 ? a1 * up : a1 * dn);
                if (i > 0) { Tri(p0, p1, q1, col); Tri(p0, q1, q0, col); }
                p0 = q0; p1 = q1;
            }
        }
        // Salpicaduras junto a la entrada del pincel.
        for (int k = 0; k < 3; k++)
        {
            Vector2 at = from - u * width * (0.12f + 0.22f * Hash(seed + 300 + k)) + n * (Hash(seed + 310 + k) - 0.5f) * width * 0.8f;
            Raylib.DrawCircleV(at, width * (0.012f + 0.02f * Hash(seed + 320 + k)), col);
        }
    }

    /// <summary>
    /// Tarjeta de intertítulo: papel negro de borde levemente deshilachado, doble filete de pluma y
    /// florones en las esquinas. Es el mismo lenguaje que el cartel del jefe, en pequeño.
    /// </summary>
    public void Panel(Rectangle r, float alpha, int seed)
    {
        if (alpha <= 0.01f) return;
        Span<Vector2> edge = stackalloc Vector2[172];
        int n = RoundRect(r, 3 * S, seed, 1.8f, 0, edge, 9, 40);
        Span<Vector2> page = edge[..n];
        Span<Vector2> shadow = stackalloc Vector2[n];
        Shift(page, new Vector2(6, 8) * S, shadow);
        Vector2 mid = new(r.X + r.Width / 2, r.Y + r.Height / 2);
        Fill(shadow, A(Palette.Ink, 0.35f * alpha), mid + new Vector2(6, 8) * S);
        Fill(page, A(Card, 0.9f * alpha), mid);
        // Fibras del papel negro, casi invisibles.
        for (int i = 0; i < 14; i++)
        {
            float y = r.Y + r.Height * Hash(seed + i * 3);
            float x0 = r.X + r.Width * Hash(seed + i * 5) * 0.6f;
            Raylib.DrawLineEx(new Vector2(x0, y), new Vector2(x0 + r.Width * (0.2f + 0.3f * Hash(seed + i)), y), 1, A(Palette.Parchment, 0.025f * alpha));
        }
        Color paper = A(Palette.Parchment, 0.9f * alpha);
        InkFrame(Inset(r, 11 * S), 2.3f * S, paper, seed, 0.9f, 0.35f);
        InkFrame(Inset(r, 17 * S), 0.9f * S, A(Palette.Parchment, 0.42f * alpha), seed + 11, 0.6f);
        for (int c = 0; c < 4; c++)
        {
            var inward = new Vector2(c % 2 == 0 ? 1 : -1, c < 2 ? 1 : -1);
            var corner = new Vector2(c % 2 == 0 ? r.X : r.X + r.Width, c < 2 ? r.Y : r.Y + r.Height);
            Fleuron(corner, inward, paper, seed + c);
        }
    }

    /// <summary>Florón de esquina: rombo, dos trazos que siguen los bordes y una voluta hacia dentro.</summary>
    void Fleuron(Vector2 corner, Vector2 inward, Color c, int seed)
    {
        Vector2 at = corner + inward * 13 * S;
        float rot = inward.X > 0 ? (inward.Y > 0 ? 0 : 270) : (inward.Y > 0 ? 90 : 180);
        if (_skin.Draw(UiArt.OrnamentCorner, new Rectangle(at.X + inward.X * 12 * S, at.Y + inward.Y * 12 * S, 44 * S, 44 * S), c, rot)) return;
        Raylib.DrawPoly(at, 4, 4.5f * S, 45, c);
        Stroke(at + new Vector2(inward.X * 7 * S, 0), at + new Vector2(inward.X * 32 * S, 0), 1.7f * S, c, seed, 0.25f, tipIn: 0.1f, tipOut: 0.8f);
        Stroke(at + new Vector2(0, inward.Y * 7 * S), at + new Vector2(0, inward.Y * 32 * S), 1.7f * S, c, seed + 1, 0.25f, tipIn: 0.1f, tipOut: 0.8f);
        Raylib.DrawCircleV(at + new Vector2(inward.X * 37 * S, 0), 1.4f * S, c);
        Raylib.DrawCircleV(at + new Vector2(0, inward.Y * 37 * S), 1.4f * S, c);
        // Voluta diagonal.
        Span<Vector2> p = stackalloc Vector2[6];
        Span<float> w = stackalloc float[6];
        for (int i = 0; i < 6; i++)
        {
            float k = i / 5f, ang = k * MathF.PI * 1.2f;
            p[i] = at + inward * (7 + 7 * k) * S + new Vector2(-inward.Y, inward.X) * MathF.Sin(ang) * 3.5f * S * (1 - k * 0.4f);
            w[i] = 1.4f * S * (1 - k * 0.6f);
        }
        Pen(p, w, c);
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

    /// <summary>Radio con el que el iris no tapa nada.</summary>
    public float IrisOpen => MathF.Sqrt(W * W + H * H) * 0.52f;

    public void Dispose()
    {
        foreach (Font f in _owned) Raylib.UnloadFont(f);
        _skin.Dispose();
    }
}
