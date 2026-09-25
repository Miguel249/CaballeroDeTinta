using System.Numerics;
using Raylib_cs;

namespace CaballeroDeTinta.Art;

/// <summary>Cómo se enmarca una barra: la vida pesa, el aguante es un tallo, el jefe es un rótulo.</summary>
enum VitalStyle { Health, Stamina, Boss }

/// <summary>Aspecto de una barra de pigmento (vida, aguante, jefe) en un fotograma.</summary>
record struct VitalLook(float Value, float Lag, float Gain, Color Fill, int Seed)
{
    public float Tremble, Hatch, Pulse, Alpha = 1;
    /// <summary>Segunda fase del jefe (0-1): trazo más grueso y espinas de tinta viva.</summary>
    public float Fury;
    public VitalStyle Style;
}

/// <summary>Barras de pigmento.</summary>
sealed partial class Ui
{
    /// <summary>
    /// Barra de pigmento: pozo de papel quemado con bordes que ondulan a lo largo, rastro claro del daño,
    /// pigmento con vetas que acaba en un filo de pincel (nunca en una vertical limpia) y el marco de su estilo.
    /// La forma es fija por <see cref="VitalLook.Seed"/>: solo tiemblan los adornos.
    /// </summary>
    public void Vital(Rectangle r, VitalLook v)
    {
        float a = v.Alpha;
        if (a <= 0.01f || r.Width < 1) return;
        if (v.Tremble > 0)
        {
            float s = v.Tremble * 2 * S;
            r.X += (Hash(Frame * 5 + v.Seed) - 0.5f) * s;
            r.Y += (Hash(Frame * 9 + v.Seed) - 0.5f) * s;
        }
        bool stamina = v.Style == VitalStyle.Stamina, boss = v.Style == VitalStyle.Boss;
        float rough = (stamina ? 0.55f : boss ? 1.1f : 0.95f) * S * (1 + v.Fury * 0.5f);
        int segs = Math.Clamp((int)(r.Width / (10 * S)), 4, 95);
        Span<float> top = stackalloc float[segs + 1];
        Span<float> bot = stackalloc float[segs + 1];
        for (int i = 0; i <= segs; i++)
        {
            top[i] = r.Y + (Noise(v.Seed, i * 0.37f) - 0.5f) * 2 * rough;
            bot[i] = r.Y + r.Height + (Noise(v.Seed + 7, i * 0.37f) - 0.5f) * 2 * rough;
        }
        float dx = r.Width / segs;

        Strip(r.X, dx, top, bot, 0, r.Width, new Vector2(2, 3) * S, A(Palette.Ink, 0.5f * a));
        Strip(r.X, dx, top, bot, 0, r.Width, Vector2.Zero, A(Well, (stamina ? 0.8f : 0.94f) * a));

        float fillW = r.Width * Math.Clamp(v.Value, 0, 1);
        float lagW = r.Width * Math.Clamp(v.Lag, 0, 1);
        float gainW = r.Width * Math.Clamp(v.Value + v.Gain, 0, 1);
        if (lagW > fillW) Strip(r.X, dx, top, bot, fillW, lagW, Vector2.Zero, A(Palette.Mix(v.Fill, Palette.Parchment, 0.4f), 0.85f * a));
        if (gainW > fillW) Strip(r.X, dx, top, bot, fillW, gainW, Vector2.Zero, A(Brass, 0.45f * a));

        if (fillW > 0.5f)
        {
            Color fill = Palette.Mix(v.Fill, Palette.Ink, 0.28f * v.Pulse);
            Strip(r.X, dx, top, bot, 0, fillW, Vector2.Zero, A(fill, a));
            // Vetas del pincel y un filo de luz que sigue el borde de arriba.
            Color vein = A(Palette.Mix(fill, Palette.Ink, 0.35f), 0.7f * a);
            for (int i = 0; i < (stamina ? 1 : 2); i++)
            {
                float y = r.Y + r.Height * (stamina ? 0.6f : i == 0 ? 0.4f : 0.72f);
                float x0 = r.X + r.Width * 0.06f * Hash(v.Seed + i), x1 = r.X + fillW - 7 * S * Hash(v.Seed + i + 5);
                if (x1 - x0 > 4 * S) Stroke(new Vector2(x0, y), new Vector2(x1, y), MathF.Max(1, r.Height * 0.09f), vein, v.Seed + 40 + i, 0.35f, tipIn: 0.02f, tipOut: 0.08f);
            }
            int shine = Math.Min(segs, (int)(fillW / dx) + 1);
            if (shine >= 1)
            {
                Span<Vector2> p = stackalloc Vector2[shine + 1];
                Span<float> w = stackalloc float[shine + 1];
                for (int i = 0; i <= shine; i++)
                {
                    float x = MathF.Min(i * dx, fillW);
                    int j = Math.Min(i, segs);
                    p[i] = new Vector2(r.X + x, top[j] + 1.6f * S);
                    w[i] = MathF.Max(1, r.Height * 0.1f) * (i == 0 || i == shine ? 0.3f : 1);
                }
                Pen(p, w, A(Palette.Mix(fill, Palette.Parchment, 0.35f), 0.75f * a));
            }
            // Filo del pincel: el pigmento se acumula en una línea oscura y se deshace en picos.
            int tipSeed = v.Seed * 31 + (int)(fillW / (3 * S));
            float tipX = r.X + fillW;
            float yt = EdgeAt(top, dx, fillW), yb = EdgeAt(bot, dx, fillW);
            int rows = stamina ? 2 : 4;
            for (int i = 0; i < rows; i++)
            {
                float y0 = yt + (yb - yt) * i / rows, y1 = yt + (yb - yt) * (i + 1) / rows;
                float reach = MathF.Min(r.Width - fillW, (2 + 5 * Hash(tipSeed + i)) * S);
                if (reach > 0.5f) Tri(new Vector2(tipX, y0), new Vector2(tipX, y1), new Vector2(tipX + reach, (y0 + y1) / 2 + (Hash(tipSeed + i + 9) - 0.5f) * 2 * S), A(fill, a));
            }
            if (!stamina && fillW < r.Width - 1)
                Raylib.DrawLineEx(new Vector2(tipX - 0.6f * S, yt + 1), new Vector2(tipX + 0.8f * S, yb - 1), 1.3f * S, A(Palette.Mix(fill, Palette.Ink, 0.5f), 0.7f * a));
        }
        if (v.Hatch > 0.01f)
        {
            // Rayado de tinta: agotado, se lee también en blanco y negro.
            Raylib.BeginScissorMode((int)r.X, (int)(r.Y - rough), (int)r.Width, (int)MathF.Ceiling(r.Height + rough * 2));
            for (float x = r.X - r.Height; x < r.X + r.Width; x += 5 * S)
                Raylib.DrawLineEx(new Vector2(x, r.Y + r.Height), new Vector2(x + r.Height, r.Y), 1.2f * S, A(Palette.Ink, 0.55f * v.Hatch * a));
            Raylib.EndScissorMode();
        }

        switch (v.Style)
        {
            case VitalStyle.Stamina: StemFrame(r, top, bot, dx, v.Seed, a); break;
            default: BarFrame(r, top, bot, dx, v, a); break;
        }
    }

    static float EdgeAt(ReadOnlySpan<float> edge, float dx, float x)
    {
        int segs = edge.Length - 1;
        float f = Math.Clamp(x / dx, 0, segs);
        int i = Math.Min((int)f, segs - 1);
        return Lerp(edge[i], edge[i + 1], f - i);
    }

    /// <summary>Tira entre los dos bordes ondulados, recortada a [<paramref name="from"/>, <paramref name="to"/>].</summary>
    static void Strip(float x0, float dx, ReadOnlySpan<float> top, ReadOnlySpan<float> bot, float from, float to, Vector2 off, Color c)
    {
        if (to - from < 0.3f || c.A == 0) return;
        int segs = top.Length - 1;
        for (int i = 0; i < segs; i++)
        {
            float s0 = i * dx, s1 = s0 + dx;
            if (s1 <= from || s0 >= to) continue;
            float a0 = MathF.Max(s0, from), a1 = MathF.Min(s1, to);
            float t0 = (a0 - s0) / dx, t1 = (a1 - s0) / dx;
            Vector2 tl = new Vector2(x0 + a0, Lerp(top[i], top[i + 1], t0)) + off, tr = new Vector2(x0 + a1, Lerp(top[i], top[i + 1], t1)) + off;
            Vector2 bl = new Vector2(x0 + a0, Lerp(bot[i], bot[i + 1], t0)) + off, br = new Vector2(x0 + a1, Lerp(bot[i], bot[i + 1], t1)) + off;
            Tri(tl, tr, br, c);
            Tri(tl, br, bl, c);
        }
    }

    /// <summary>Borde de tinta que sigue el perfil ondulado, desbordando un poco por los extremos.</summary>
    void Rule(Rectangle r, ReadOnlySpan<float> edge, float dx, float shift, float thick, Color c, int seed, float overhang)
    {
        int segs = edge.Length - 1;
        Span<Vector2> p = stackalloc Vector2[segs + 3];
        Span<float> w = stackalloc float[segs + 3];
        p[0] = new Vector2(r.X - overhang, edge[0] + shift);
        w[0] = thick * 0.35f;
        for (int i = 0; i <= segs; i++)
        {
            p[i + 1] = new Vector2(r.X + i * dx, edge[i] + shift);
            w[i + 1] = thick * (0.7f + 0.6f * Noise(seed, i * 0.5f));
        }
        p[segs + 2] = new Vector2(r.X + r.Width + overhang, edge[segs] + shift);
        w[segs + 2] = thick * 0.35f;
        Pen(p, w, c);
    }

    /// <summary>Marco de la vida y del jefe: dos reglas de tinta, un corchete a la izquierda y un plumín a la derecha.</summary>
    void BarFrame(Rectangle r, ReadOnlySpan<float> top, ReadOnlySpan<float> bot, float dx, VitalLook v, float a)
    {
        bool boss = v.Style == VitalStyle.Boss;
        float thick = (boss ? 2.4f : 2.1f) * S * (1 + v.Fury * 0.45f);
        Color ink = A(Palette.Ink, a), paper = A(Palette.Parchment, a);
        Rule(r, top, dx, -1.8f * S, thick, ink, v.Seed + 50, 3 * S);
        Rule(r, bot, dx, 1.8f * S, thick, ink, v.Seed + 60, 3 * S);
        int segs = top.Length - 1;
        float yL = (top[0] + bot[0]) / 2, yR = (top[segs] + bot[segs]) / 2;
        float xR = r.X + r.Width;

        if (!boss)
        {
            // Corchete de pluma a la izquierda, del que parece salir la barra.
            Span<Vector2> p = stackalloc Vector2[5];
            Span<float> w = stackalloc float[5];
            float hTop = top[0] - 3.5f * S, hBot = bot[0] + 3.5f * S;
            for (int i = 0; i < 5; i++)
            {
                float k = i / 4f;
                p[i] = new Vector2(r.X - 2.5f * S - MathF.Sin(MathF.PI * k) * 3.5f * S, hTop + (hBot - hTop) * k);
                w[i] = thick * (0.5f + 0.9f * MathF.Sin(MathF.PI * k));
            }
            Pen(p, w, ink);
            // Plumín a la derecha y un rombo de papel que remata.
            float hr = r.Height * 0.5f + 2.5f * S;
            Tri(new Vector2(xR + 1 * S, yR - hr), new Vector2(xR + 1 * S, yR + hr), new Vector2(xR + 12 * S, yR), ink);
            Vector2 fin = new(xR + 15 * S, yR);
            Raylib.DrawPoly(fin, 4, 5.6f * S, 45, ink);
            Raylib.DrawPoly(fin, 4, 3.4f * S, 45, A(Palette.Parchment, 0.9f * a));
            Stroke(new Vector2(r.X + 4 * S, top[0] - 4.8f * S), new Vector2(xR - 4 * S, top[segs] - 4.8f * S), 1 * S, A(Palette.Parchment, 0.3f * a), v.Seed + 70, 0.4f, tipIn: 0.1f, tipOut: 0.3f);
        }
        else
        {
            // Rótulo del jefe: remates simétricos con rombo anillado, un trazo largo y una voluta.
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side < 0 ? r.X : xR, y = side < 0 ? yL : yR;
                float hr = r.Height * 0.5f + 3 * S;
                Tri(new Vector2(x - side * 1 * S, y - hr), new Vector2(x - side * 1 * S, y + hr), new Vector2(x + side * 9 * S, y), ink);
                Vector2 end = new(x + side * 15 * S, y);
                Raylib.DrawPoly(end, 4, 7.5f * S, 45, ink);
                Raylib.DrawPoly(end, 4, 4.6f * S, 45, paper);
                Raylib.DrawRing(end, 9 * S, 10.4f * S, 0, 360, 20, A(Palette.Parchment, 0.6f * a));
                Stroke(end + new Vector2(side * 11 * S, 0), end + new Vector2(side * 42 * S, 0), 2 * S, A(Palette.Parchment, 0.8f * a), v.Seed + 80 + side, 0.3f, tipIn: 0.08f, tipOut: 0.85f);
                Raylib.DrawCircleV(end + new Vector2(side * 47 * S, 0), 1.6f * S, A(Palette.Parchment, 0.8f * a));
            }
        }

        if (v.Fury > 0.01f)
        {
            // Espinas de tinta viva: brotan del marco y cambian de dibujo a 12 fps.
            int count = Math.Max(2, (int)(r.Width / (44 * S)));
            for (int i = 0; i < count; i++)
            {
                float x = (i + 0.5f) / count * r.Width;
                for (int side = -1; side <= 1; side += 2)
                {
                    float y = side < 0 ? EdgeAt(top, dx, x) - 2 * S : EdgeAt(bot, dx, x) + 2 * S;
                    float len = (3.5f + 2.5f * Hash(v.Seed + i * 7 + side) + Jit(v.Seed + i * 3 + side) * 1.2f) * S * v.Fury;
                    float lean = (Hash(v.Seed + i * 11 + side) - 0.5f) * 5 * S;
                    Tri(new Vector2(r.X + x - 2.2f * S, y), new Vector2(r.X + x + 2.2f * S, y), new Vector2(r.X + x + lean, y + side * len), ink);
                }
            }
        }
    }

    /// <summary>Marco del aguante: un tallo de tinta ligera por debajo, sin caja, con tres hojitas.</summary>
    void StemFrame(Rectangle r, ReadOnlySpan<float> top, ReadOnlySpan<float> bot, float dx, int seed, float a)
    {
        Color ink = A(Palette.Ink, 0.9f * a);
        Rule(r, bot, dx, 1.6f * S, 1.4f * S, ink, seed + 50, 4 * S);
        Rule(r, top, dx, -1.1f * S, 0.8f * S, A(Palette.Ink, 0.55f * a), seed + 60, 1 * S);
        int segs = bot.Length - 1;
        float yEnd = bot[segs] + 1.6f * S, xR = r.X + r.Width;
        // Zarcillo que se enrosca al final del tallo.
        Span<Vector2> p = stackalloc Vector2[7];
        Span<float> w = stackalloc float[7];
        for (int i = 0; i < 7; i++)
        {
            float k = i / 6f, ang = MathF.PI / 2 - k * MathF.PI * 1.3f;
            float rr = 4.5f * S * (1 - 0.5f * k);
            p[i] = new Vector2(xR + 4 * S, yEnd - 4.5f * S) + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * rr;
            w[i] = 1.3f * S * (1 - 0.6f * k);
        }
        Pen(p, w, ink);
        Leaf(new Vector2(xR + 1 * S, yEnd), -0.9f + Jit(seed + 1, true) * 0.08f, 6 * S, a);
        Leaf(new Vector2(xR - 9 * S, yEnd), 0.55f + Jit(seed + 2, true) * 0.08f, 5 * S, a);
        Leaf(new Vector2(r.X - 1 * S, yEnd), 2.6f + Jit(seed + 3, true) * 0.08f, 4.5f * S, a);
    }

    /// <summary>Hojita lanceolada: musgo con canto de tinta.</summary>
    void Leaf(Vector2 at, float angle, float len, float a)
    {
        const int n = 8;
        Span<Vector2> p = stackalloc Vector2[n];
        var d = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var nn = new Vector2(-d.Y, d.X);
        for (int i = 0; i < n; i++)
        {
            float k = i < n / 2 ? i / (float)(n / 2) : (n - i) / (float)(n / 2);
            float side = i < n / 2 ? 1 : -1;
            p[i] = at + d * len * k + nn * side * MathF.Sin(MathF.PI * k) * len * 0.3f;
        }
        Fill(p, A(Palette.Mix(Moss, Palette.Parchment, 0.15f), a));
        PenLoop(p, 0.9f * S, A(Palette.Ink, 0.9f * a), 700);
    }
}
