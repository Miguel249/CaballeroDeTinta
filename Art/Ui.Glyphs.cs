using System.Numerics;
using Raylib_cs;

namespace CaballeroDeTinta.Art;

/// <summary>Teclas, botones del mando, ratón y las indicaciones que los usan.</summary>
sealed partial class Ui
{
    public float KeyHeight => 32 * S;

    public float KeyWidth(string label) => MathF.Max(32 * S, Measure(label, KeySize(label), Face.Bold, 1).X + 20 * S);

    float KeySize(string label) => (label.Length > 3 ? 13 : 16) * S;

    /// <summary>
    /// Tecla de marfil dibujada: sombra desplazada, canto de tinta, cara levemente deformada y un
    /// contorno repasado dos veces. El marco hierve un pelo; la letra no se mueve nunca.
    /// <paramref name="press"/> la hunde.
    /// </summary>
    public void Keycap(Vector2 center, string label, float alpha = 1, float press = 0)
    {
        if (alpha <= 0.01f) return;
        float w = KeyWidth(label), h = KeyHeight, sink = 2.5f * S * press;
        int seed = Seed(label);
        var face = new Rectangle(center.X - w / 2, center.Y - h / 2 + sink - 1.5f * S, w, h);
        Span<Vector2> p = stackalloc Vector2[44];
        Span<Vector2> o = stackalloc Vector2[44];
        int n = RoundRect(face, 6 * S, seed, 0.9f, 0.4f, p);
        ReadOnlySpan<Vector2> q = p[..n];
        Vector2 mid = new(face.X + w / 2, face.Y + h / 2);

        Shift(q, new Vector2(3.5f * S, 7 * S - sink), o);
        Fill(o[..n], A(Palette.Ink, 0.3f * alpha), mid + new Vector2(3.5f * S, 7 * S - sink));
        Shift(q, new Vector2(0, 3.5f * S - sink), o);
        Fill(o[..n], A(Palette.Ink, 0.9f * alpha), mid + new Vector2(0, 3.5f * S - sink));
        Fill(q, A(Ivory, alpha), mid);
        // Bisel inferior en papel viejo: la tecla tiene volumen sin necesidad de degradados.
        Stroke(new Vector2(face.X + 6 * S, face.Y + h - 5 * S), new Vector2(face.X + w - 6 * S, face.Y + h - 5 * S), 3.2f * S, A(KeyDepth, 0.6f * alpha), seed + 3, 0.3f, tipIn: 0.15f, tipOut: 0.15f);
        PenLoop(q, 1.9f * S, A(Palette.Ink, alpha), seed);
        // Segundo trazo, fino y desviado: el boceto repasado.
        n = RoundRect(face, 6 * S, seed + 71, 1.4f, 0.6f, p);
        PenLoop(p[..n], 0.8f * S, A(Palette.Ink, 0.5f * alpha), seed + 71);
        Text(label, new Vector2(mid.X, mid.Y - 1.5f * S), KeySize(label), A(Palette.Ink, alpha), Face.Bold);
    }

    /// <summary>Botón redondo del mando (A, B...), con el mismo lenguaje que las teclas.</summary>
    public void PadButton(Vector2 center, string label, float alpha = 1, float press = 0)
    {
        if (alpha <= 0.01f) return;
        float r = KeyHeight * 0.55f, sink = 2.5f * S * press;
        Vector2 c = center + new Vector2(0, sink - 1.5f * S);
        int seed = Seed(label) + 5;
        const int n = 22;
        Span<Vector2> p = stackalloc Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float ang = i / (float)n * MathF.Tau;
            float k = 1 + (Hash(seed + i) - 0.5f) * 0.05f + Jit(seed + i) * 0.012f;
            p[i] = c + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r * k;
        }
        Raylib.DrawCircleV(c + new Vector2(3.5f * S, 7 * S - sink), r, A(Palette.Ink, 0.3f * alpha));
        Raylib.DrawCircleV(c + new Vector2(0, 3.5f * S - sink), r, A(Palette.Ink, 0.9f * alpha));
        Fill(p, A(Ivory, alpha), c);
        PenLoop(p, 1.9f * S, A(Palette.Ink, alpha), seed);
        Raylib.DrawRing(c, r * 0.7f, r * 0.7f + 0.9f * S, 200, 320, 10, A(KeyDepth, 0.9f * alpha));
        Text(label, c - new Vector2(0, 1 * S), 16 * S, A(Palette.Ink, alpha), Face.Bold);
    }

    public Vector2 MouseSize => new Vector2(26, 38) * S;

    /// <summary>
    /// Ratón ilustrado: cuerpo de huevo, dos botones y rueda. Lo que importa va en brasa y lleva
    /// marcas de "clic"; la rueda activa muestra flechas y el movimiento, cuatro flechas de papel.
    /// </summary>
    public void Mouse(Vector2 center, MouseMark mark, float alpha = 1, float press = 0)
    {
        if (alpha <= 0.01f) return;
        Vector2 size = MouseSize;
        Vector2 c = center + new Vector2(0, 3 * S + press * 2 * S);
        float hw = size.X / 2, hh = size.Y / 2;
        Color ink = A(Palette.Ink, alpha), paper = A(Palette.Parchment, alpha), ember = A(Palette.Ember, alpha);
        int seed = 4099 + (int)mark * 17;

        if (mark == MouseMark.Move)
        {
            // Cuatro flechas de papel con canto de tinta: se leen sobre fondo claro y oscuro.
            for (int i = 0; i < 4; i++)
            {
                float ang = i * MathF.PI / 2;
                var d = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                var nn = new Vector2(-d.Y, d.X);
                Vector2 tip = c + d * new Vector2(hw + 14 * S, hh + 11 * S) + d * Jit(seed + i) * 0.6f * S;
                Vector2 b0 = tip - d * 8 * S + nn * 5.5f * S, b1 = tip - d * 8 * S - nn * 5.5f * S;
                Tri(tip + d * 1.6f * S, b0 + (nn - d) * 1.6f * S, b1 - (nn + d) * 1.6f * S, ink);
                Tri(tip, b0, b1, paper);
                Stroke(tip - d * 8 * S, tip - d * 13 * S, 2.2f * S, ink, seed + i * 5, 0.2f, tipIn: 0, tipOut: 0.6f);
            }
        }

        // Cable.
        Vector2 top = new(c.X, c.Y - hh);
        Span<Vector2> cable = stackalloc Vector2[4];
        Span<float> cw = stackalloc float[4];
        cable[0] = top; cable[1] = top + new Vector2(1, -4) * S; cable[2] = top + new Vector2(-1.5f, -7) * S; cable[3] = top + new Vector2(-5, -8.5f) * S;
        cw[0] = 2 * S; cw[1] = 1.8f * S; cw[2] = 1.4f * S; cw[3] = 0.8f * S;
        Pen(cable, cw, ink);

        const int n = 32;
        Span<Vector2> body = stackalloc Vector2[n];
        for (int i = 0; i < n; i++)
            body[i] = Edge(-MathF.PI / 2 + i / (float)n * MathF.Tau) + new Vector2(Jit(seed + i), Jit(seed + i + 40)) * 0.3f * S;
        Span<Vector2> o = stackalloc Vector2[n];
        Shift(body, new Vector2(3 * S, 6.5f * S), o);
        Fill(o, A(Palette.Ink, 0.3f * alpha), c + new Vector2(3 * S, 6.5f * S));
        Shift(body, new Vector2(0, 3.5f * S), o);
        Fill(o, A(Palette.Ink, 0.9f * alpha), c + new Vector2(0, 3.5f * S));
        Fill(body, A(Ivory, alpha), c);

        // Botones: la línea de corte queda algo por encima de la mitad.
        float k = MathF.Pow(0.16f, 1 / 0.9f), cut = MathF.Asin(k);
        if (mark is MouseMark.Left or MouseMark.Right)
        {
            Span<Vector2> btn = stackalloc Vector2[11];
            float u0 = mark == MouseMark.Left ? MathF.PI + cut : -MathF.PI / 2;
            float u1 = mark == MouseMark.Left ? MathF.PI * 1.5f : -cut;
            btn[0] = new Vector2(c.X, c.Y - hh * 0.16f);
            for (int i = 0; i <= 9; i++) btn[i + 1] = Edge(u0 + (u1 - u0) * i / 9f);
            Vector2 mid = Vector2.Zero;
            foreach (Vector2 v in btn) mid += v;
            Fill(btn, ember, mid / btn.Length);
        }
        Stroke(Edge(MathF.PI + cut), Edge(-cut), 1.5f * S, ink, seed + 1, 0.2f, tipIn: 0.05f, tipOut: 0.05f);
        Stroke(new Vector2(c.X, c.Y - hh + 1), new Vector2(c.X, c.Y - hh * 0.16f), 1.5f * S, ink, seed + 2, 0.15f, tipIn: 0, tipOut: 0.1f);

        var wheel = new Rectangle(c.X - 3 * S, c.Y - hh + 5 * S, 6 * S, 10 * S);
        bool spin = mark == MouseMark.Wheel;
        Raylib.DrawRectangleRounded(wheel, 1, 6, spin ? ember : A(Ivory, alpha));
        if (spin)
            for (int i = 1; i <= 2; i++)
                Raylib.DrawLineEx(new Vector2(wheel.X + 1.2f * S, wheel.Y + wheel.Height * i / 3), new Vector2(wheel.X + wheel.Width - 1.2f * S, wheel.Y + wheel.Height * i / 3), 1 * S, A(Palette.Ink, 0.7f * alpha));
        Raylib.DrawRectangleRoundedLinesEx(wheel, 1, 6, 1.4f * S, ink);

        PenLoop(body, 2 * S, ink, seed);
        Shift(body, new Vector2(0.7f, -0.5f) * S, o);
        PenLoop(o, 0.7f * S, A(Palette.Ink, 0.45f * alpha), seed + 9);

        if (mark is MouseMark.Left or MouseMark.Right)
        {
            // Marcas de clic que salen de la esquina del botón.
            float side = mark == MouseMark.Left ? -1 : 1;
            Vector2 corner = Edge(mark == MouseMark.Left ? MathF.PI * 1.25f : -MathF.PI * 0.25f);
            Vector2 dir = Vector2.Normalize(corner - c);
            for (int i = -1; i <= 1; i++)
            {
                float ang = MathF.Atan2(dir.Y, dir.X) + i * 0.55f * side;
                var d = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                float len = (i == 0 ? 7 : 5) * S + Jit(seed + i + 60) * 0.7f * S;
                Vector2 a0 = corner + d * 4 * S, a1 = a0 + d * len;
                Stroke(a0, a1, 3.2f * S, ink, seed + 70 + i, 0.1f, tipIn: 0.1f, tipOut: 0.5f);
                Stroke(a0, a1, 1.7f * S, ember, seed + 70 + i, 0.1f, tipIn: 0.1f, tipOut: 0.5f);
            }
        }
        else if (spin)
        {
            // Flechas arriba y abajo junto a la rueda.
            for (int i = -1; i <= 1; i += 2)
            {
                Vector2 at = new(c.X + hw + 6 * S, wheel.Y + wheel.Height / 2 + i * 6 * S + Jit(seed + i) * 0.4f * S);
                Vector2 tip = at + new Vector2(0, i * 4 * S);
                Tri(tip + new Vector2(0, i * 1.8f * S), at + new Vector2(-5.5f, -i * 1.2f) * S, at + new Vector2(5.5f, -i * 1.2f) * S, ink);
                Tri(tip, at + new Vector2(-3.8f * S, 0), at + new Vector2(3.8f * S, 0), ember);
            }
        }

        // Silueta de huevo: más estrecha arriba, lados casi rectos.
        Vector2 Edge(float u)
        {
            float sn = MathF.Sin(u), cs = MathF.Cos(u);
            float wide = 1 + 0.1f * sn;
            return c + new Vector2(MathF.Sign(cs) * MathF.Pow(MathF.Abs(cs), 0.8f) * hw * wide, MathF.Sign(sn) * MathF.Pow(MathF.Abs(sn), 0.9f) * hh);
        }
    }

    public Vector2 GlyphSize(Glyph g) => g.Kind switch
    {
        GlyphKind.Key => new Vector2(KeyWidth(g.Key), KeyHeight),
        GlyphKind.Mouse => MouseSize + (g.Mouse == MouseMark.Move ? new Vector2(40, 30) * S : g.Mouse == MouseMark.Wheel ? new Vector2(16 * S, 6 * S) : new Vector2(0, 6 * S)),
        GlyphKind.Wasd => new Vector2(KeyWidth("W") * 3 + 8 * S, KeyHeight * 2 + 6 * S),
        _ => new Vector2(Measure("o", 17 * S, Face.Body).X + 6 * S, KeyHeight),
    };

    public void DrawGlyph(Glyph g, Vector2 center, float alpha = 1, float press = 0)
    {
        switch (g.Kind)
        {
            case GlyphKind.Key: Keycap(center, g.Key, alpha, press); break;
            case GlyphKind.Mouse:
                // La rueda activa lleva flechas a la derecha: el ratón se desplaza para centrar el conjunto.
                Mouse(center - new Vector2(g.Mouse == MouseMark.Wheel ? 8 * S : 0, 0), g.Mouse, alpha, press);
                break;
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

    /// <summary>Ancho de una fila de iconos.</summary>
    public float GlyphRowWidth(Glyph[] glyphs)
    {
        float w = (glyphs.Length - 1) * 8 * S;
        foreach (Glyph g in glyphs) w += GlyphSize(g).X;
        return w;
    }

    /// <summary>Alto del icono más alto de la fila.</summary>
    public float GlyphRowHeight(Glyph[] glyphs)
    {
        float h = 0;
        foreach (Glyph g in glyphs) h = MathF.Max(h, GlyphSize(g).Y);
        return h;
    }

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
    /// Indicación contextual reutilizable: la tecla, un punto y el verbo en versalitas
    /// ("E · Descansar", "E · Abrir", "E · Recoger"...). Sin rectángulo: una pincelada tenue la separa
    /// del mundo. <paramref name="t"/> es su visibilidad (0-1): el pincel pasa, la indicación sube unos píxeles.
    /// </summary>
    public void Prompt(Vector2 anchor, string key, string verb, float t)
    {
        if (t <= 0.01f) return;
        float a = Smooth(t);
        Vector2 size = PromptSize(key, verb);
        Vector2 p = anchor + new Vector2(0, (1 - a) * 8 * S);
        float x0 = p.X - size.X / 2, kw = KeyWidth(key), gap = 12 * S;
        int seed = Seed(verb);
        Swath(new Vector2(x0 - 28 * S, p.Y + 5 * S), new Vector2(x0 + size.X + 40 * S, p.Y - 1 * S), 54 * S, A(Palette.Ink, 0.62f * a), seed, Smooth(t * 1.3f));
        Keycap(new Vector2(x0 + kw / 2, p.Y), key, a);
        Raylib.DrawPoly(new Vector2(x0 + kw + gap, p.Y + 1 * S), 4, 2.6f * S, 45, A(Palette.Parchment, 0.85f * a));
        float fs = 16 * S;
        Text(Upper(verb), new Vector2(x0 + kw + gap * 2, p.Y + 1 * S), fs, A(Palette.Parchment, a), Face.Display, fs * 0.2f / S, Align.Left, shadow: true);
    }

    /// <summary>Tamaño que ocupa <see cref="Prompt"/>: sirve para mantenerla dentro de la pantalla.</summary>
    public Vector2 PromptSize(string key, string verb)
    {
        float fs = 16 * S;
        return new Vector2(KeyWidth(key) + 24 * S + Measure(Upper(verb), fs, Face.Display, fs * 0.2f / S).X, KeyHeight);
    }

    /// <summary>Botón del mando que corresponde a una tecla de los menús, si se está usando el mando.</summary>
    string? PadFor(string key) => GlyphMode != InputGlyphMode.Gamepad ? null : key switch
    {
        "ESC" => "B",
        "ENTER" => "A",
        _ => null,
    };

    float HintKeyWidth(string key) => PadFor(key) != null ? KeyHeight * 1.1f : KeyWidth(key);

    /// <summary>Pista de pie de pantalla ("[Esc] Volver"); devuelve la zona sensible para el ratón.</summary>
    public Rectangle Hint(Vector2 center, string key, string label, float alpha = 1, float hover = 0)
    {
        float kw = HintKeyWidth(key), lw = Measure(label, 17 * S, Face.Body).X, gap = 10 * S;
        float total = kw + gap + lw, x = center.X - total / 2;
        if (alpha <= 0.01f) return HintRect(center, key, label);
        if (PadFor(key) is { } pad) PadButton(new Vector2(x + kw / 2, center.Y), pad, alpha, hover * 0.4f);
        else Keycap(new Vector2(x + kw / 2, center.Y), key, alpha, hover * 0.4f);
        Color c = A(Palette.Mix(Palette.Parchment, Ivory, hover), (0.75f + 0.25f * hover) * alpha);
        Text(label, new Vector2(x + kw + gap, center.Y), 17 * S, c, Face.Body, align: Align.Left, shadow: true);
        if (hover > 0.01f)
            Stroke(new Vector2(x + kw + gap, center.Y + 12 * S), new Vector2(x + kw + gap + lw * Smooth(hover), center.Y + 12 * S), 1.6f * S, c, Seed(label), 0.4f, tipIn: 0.1f, tipOut: 0.4f);
        return HintRect(center, key, label);
    }

    public Rectangle HintRect(Vector2 center, string key, string label)
    {
        float total = HintKeyWidth(key) + 10 * S + Measure(label, 17 * S, Face.Body).X;
        return new Rectangle(center.X - total / 2 - 6 * S, center.Y - KeyHeight / 2 - 4 * S, total + 12 * S, KeyHeight + 8 * S);
    }
}
