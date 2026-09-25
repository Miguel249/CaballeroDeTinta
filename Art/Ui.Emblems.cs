using System.Numerics;
using Raylib_cs;

namespace CaballeroDeTinta.Art;

/// <summary>Viñetas de las secciones de controles.</summary>
enum EmblemKind { Boot, Sword, Brazier }

/// <summary>Iconos dibujados: la llama de selección, los frascos, el emblema del caballero y viñetas.</summary>
sealed partial class Ui
{
    /// <summary>
    /// Llamita de brasa: el indicador de selección de todos los menús. Cambia de dibujo a 12 fps con un
    /// ciclo de tres poses; con <paramref name="breath"/> además respira, despacio y a saltos de fotograma.
    /// </summary>
    public void Flame(Vector2 bottom, float height, float alpha = 1, float flare = 0, float breath = 0)
    {
        if (alpha <= 0.01f) return;
        int f = Boil;
        float br = breath * MathF.Sin(Frame * MathF.Tau / 18f) * 0.09f;
        float h = height * (0.95f + 0.1f * Hash(f * 7 + 1) + flare * 0.3f + br), w = height * 0.3f * (1 + br * 0.6f);
        float sway = (Hash(f * 13 + 5) - 0.5f) * w * 0.8f;
        Draw(1.3f, A(Palette.Ink, 0.9f * alpha));
        Draw(1f, A(Palette.Ember, alpha));
        Draw(0.55f, A(Palette.Mix(Palette.Ember, Ivory, 0.6f), alpha));

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
    /// Frasco de brasa: vientre redondo, hombros, cuello largo con cordel, labio y corcho.
    /// <paramref name="fill"/> es el nivel del líquido; vacío queda el vidrio oscuro con su silueta de
    /// tinta, así que se lee lleno/vacío sin depender del color.
    /// </summary>
    public void Flask(Vector2 center, float size, float fill, float alpha = 1, float glow = 1)
    {
        if (alpha <= 0.01f) return;
        fill = Math.Clamp(fill, 0, 1);
        if (_skin.Has(UiArt.Flask))
        {
            Color tint = Palette.Mix(Palette.Mix(Well, Palette.Stone, 0.3f), Color.White, fill);
            _skin.Draw(UiArt.Flask, new Rectangle(center.X, center.Y, size, size), A(tint, alpha));
            return;
        }
        float rb = size * 0.3f, nw = size * 0.12f;
        Vector2 belly = center + new Vector2(0, size * 0.17f);
        float neckTop = center.Y - size * 0.3f;
        float neckBottom = belly.Y - rb * 0.78f - size * 0.06f;
        Color ink = A(Palette.Ink, alpha);

        // Silueta: cuello, hombro izquierdo, vientre de izquierda a derecha por abajo, hombro derecho.
        const int arc = 20;
        Span<Vector2> glass = stackalloc Vector2[arc + 4];
        glass[0] = new Vector2(belly.X + nw, neckTop);
        glass[1] = new Vector2(belly.X - nw, neckTop);
        glass[2] = new Vector2(belly.X - nw, neckBottom);
        for (int i = 0; i < arc; i++)
        {
            float ang = MathF.PI + 0.9f - i / (float)(arc - 1) * (MathF.PI + 1.8f);
            glass[3 + i] = belly + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * rb;
        }
        glass[arc + 3] = new Vector2(belly.X + nw, neckBottom);

        if (fill > 0.01f && glow > 0)
            Raylib.DrawCircleV(belly, rb * 1.55f, A(Palette.Ember, 0.12f * glow * alpha * (0.85f + 0.3f * Hash(Boil * 3 + (int)center.X))));
        Span<Vector2> o = stackalloc Vector2[arc + 4];
        Shift(glass, new Vector2(1.5f, 2.5f) * S, o);
        Fill(o, A(Palette.Ink, 0.55f * alpha), belly + new Vector2(1.5f, 2.5f) * S);
        Fill(glass, A(Well, 0.95f * alpha), belly);
        if (fill > 0.01f)
        {
            float level = belly.Y + rb - 2 * rb * fill * 0.92f;
            Raylib.BeginScissorMode((int)(belly.X - rb - 2), (int)level, (int)(rb * 2 + 4), (int)(belly.Y + rb - level + 2));
            Fill(glass, A(Palette.Mix(Palette.Ember, Palette.Burgundy, 0.35f), alpha), belly);
            Raylib.DrawCircleV(belly + new Vector2(0, rb * 0.25f), rb * 0.55f, A(Palette.Mix(Palette.Ember, Brass, 0.4f), alpha));
            Raylib.EndScissorMode();
            float half = MathF.Sqrt(MathF.Max(0, rb * rb - (level - belly.Y) * (level - belly.Y))) * 0.85f;
            if (half > 1) Raylib.DrawLineEx(new Vector2(belly.X - half, level + 1), new Vector2(belly.X + half, level + 1), 1.2f * S, A(Brass, 0.75f * alpha));
        }
        float line = MathF.Max(1.3f * S, size * 0.07f);
        PenLoop(glass, line, ink, 61);
        // Cordel de latón atado al cuello.
        Raylib.DrawLineEx(new Vector2(belly.X - nw - line * 0.4f, neckBottom - size * 0.05f), new Vector2(belly.X + nw + line * 0.4f, neckBottom - size * 0.08f), MathF.Max(1, size * 0.045f), A(Brass, 0.9f * alpha));
        // Labio y corcho.
        var lip = new Rectangle(belly.X - nw * 1.55f, neckTop - size * 0.05f, nw * 3.1f, size * 0.07f);
        Raylib.DrawRectangleRounded(lip, 0.5f, 4, ink);
        var cork = new Rectangle(belly.X - nw * 0.95f, lip.Y - size * 0.1f, nw * 1.9f, size * 0.11f);
        Raylib.DrawRectangleRounded(cork, 0.3f, 4, A(Palette.Umber, alpha));
        Raylib.DrawRectangleRoundedLinesEx(cork, 0.3f, 4, MathF.Max(1, size * 0.045f), ink);
        // Brillo del vidrio: una curva de papel arriba a la izquierda.
        Raylib.DrawRing(belly, rb * 0.6f, rb * 0.6f + MathF.Max(1, size * 0.05f), 200, 250, 8, A(Palette.Parchment, (fill > 0.01f ? 0.6f : 0.3f) * alpha));
    }

    /// <summary>Punto donde asoma el corcho de <see cref="Flask"/>: de ahí salen las volutas al beber.</summary>
    public static Vector2 FlaskMouth(Vector2 center, float size) => center - new Vector2(0, size * 0.46f);

    /// <summary>
    /// El emblema del Caballero de Tinta: un sello de cera con el yelmo del caballero y, por penacho,
    /// una gota de tinta del color que se le pida. Se reconoce a 24-32 px por la silueta del yelmo.
    /// <paramref name="pulse"/> hincha la gota (poca vida); <paramref name="glow"/> es el halo de curación.
    /// </summary>
    public void Crest(Vector2 c, float r, Color accent, float alpha, float glow = 0, float pulse = 0)
    {
        if (alpha <= 0.01f) return;
        if (glow > 0) Raylib.DrawCircleV(c, r * 1.6f, A(Brass, 0.25f * glow * alpha));
        if (_skin.Draw(UiArt.Crest, new Rectangle(c.X, c.Y, r * 2.3f, r * 2.3f), A(Color.White, alpha))) return;

        // Sello: borde festoneado de cera aplastada.
        const int n = 40;
        Span<Vector2> seal = stackalloc Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float ang = i / (float)n * MathF.Tau;
            float k = 1 + 0.065f * MathF.Cos(ang * 9 + 0.7f) + (Hash(i + 911) - 0.5f) * 0.05f + Jit(i + 911, true) * 0.012f;
            seal[i] = c + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r * k;
        }
        Span<Vector2> o = stackalloc Vector2[n];
        Shift(seal, new Vector2(1.5f, 2.5f) * S, o);
        Fill(o, A(Palette.Ink, 0.55f * alpha), c + new Vector2(1.5f, 2.5f) * S);
        Fill(seal, A(Card, alpha), c);
        PenLoop(seal, 1.8f * S, A(Palette.Ink, alpha), 913);
        Raylib.DrawRing(c, r * 0.8f - 1.3f * S, r * 0.8f, 0, 360, 32, A(Palette.Parchment, 0.75f * alpha));

        // Yelmo cerrado: cúpula, cuerpo que se abre abajo, visera y respiradero.
        float hw = r * 0.33f, top = c.Y - r * 0.3f, bottom = c.Y + r * 0.5f;
        Vector2 dome = new(c.X, top + hw);
        const int d = 9;
        Span<Vector2> helm = stackalloc Vector2[d + 2];
        for (int i = 0; i < d; i++)
        {
            float ang = MathF.PI + i / (float)(d - 1) * MathF.PI;
            helm[i] = dome + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * hw;
        }
        helm[d] = new Vector2(c.X + hw * 1.14f, bottom);
        helm[d + 1] = new Vector2(c.X - hw * 1.14f, bottom);
        Color paper = A(Palette.Parchment, 0.95f * alpha);
        Fill(helm, paper);
        float slit = dome.Y + hw * 0.35f;
        Raylib.DrawLineEx(new Vector2(c.X - hw * 0.95f, slit), new Vector2(c.X + hw * 0.95f, slit), MathF.Max(1.2f * S, r * 0.11f), A(Palette.Ink, alpha));
        Raylib.DrawLineEx(new Vector2(c.X, slit), new Vector2(c.X, bottom - r * 0.08f), MathF.Max(1, r * 0.075f), A(Palette.Ink, alpha));

        // Penacho: una gota de tinta que brota de la cúpula, inclinada hacia atrás.
        float drop = r * 0.2f * (1 + 0.18f * pulse);
        Vector2 dc = new(c.X + r * 0.2f, top - r * 0.02f);
        Vector2 tipAt = dc + new Vector2(-r * 0.36f, -r * 0.36f) * (1 + 0.1f * pulse);
        Vector2 toTip = Vector2.Normalize(tipAt - dc), side = new(-toTip.Y, toTip.X);
        Color ac = A(accent, alpha);
        Tri(dc + side * drop * 0.95f, dc - side * drop * 0.95f, tipAt, ac);
        Raylib.DrawCircleV(dc, drop, ac);
        Raylib.DrawCircleV(dc + new Vector2(drop * 0.3f, -drop * 0.25f), drop * 0.3f, A(Palette.Parchment, 0.55f * alpha));
    }

    /// <summary>Viñeta dibujada a pluma para las secciones de la página de controles.</summary>
    public void Emblem(EmblemKind kind, Vector2 c, float size, float alpha)
    {
        if (alpha <= 0.01f) return;
        Color line = A(Palette.Parchment, 0.9f * alpha), wash = A(Palette.Parchment, 0.16f * alpha);
        switch (kind)
        {
            case EmblemKind.Boot:
            {
                // Bota de caballero mirando a la derecha, con su espuela.
                Span<Vector2> shaft = stackalloc Vector2[4];
                Span<Vector2> foot = stackalloc Vector2[6];
                Span<Vector2> all = stackalloc Vector2[9];
                shaft[0] = U(-0.2f, -0.46f); shaft[1] = U(0.1f, -0.46f); shaft[2] = U(0.1f, 0.14f); shaft[3] = U(-0.22f, 0.14f);
                foot[0] = U(-0.24f, 0.1f); foot[1] = U(0.1f, 0.1f); foot[2] = U(0.36f, 0.2f); foot[3] = U(0.48f, 0.33f); foot[4] = U(0.46f, 0.44f); foot[5] = U(-0.24f, 0.44f);
                all[0] = U(-0.2f, -0.46f); all[1] = U(0.1f, -0.46f); all[2] = U(0.1f, 0.1f); all[3] = U(0.36f, 0.2f); all[4] = U(0.48f, 0.33f);
                all[5] = U(0.46f, 0.44f); all[6] = U(-0.24f, 0.44f); all[7] = U(-0.24f, 0.26f); all[8] = U(-0.22f, 0.12f);
                Fill(shaft, wash);
                Fill(foot, wash);
                PenLoop(all, 1.8f * S, line, 501);
                Stroke(U(-0.23f, -0.3f), U(0.12f, -0.3f), 2.2f * S, line, 502, 0.2f, tipIn: 0.05f, tipOut: 0.05f);
                Stroke(U(-0.24f, 0.44f), U(0.46f, 0.44f), 3 * S, line, 503, 0.2f, tipIn: 0.05f, tipOut: 0.2f);
                for (int i = 0; i < 3; i++)
                    Stroke(U(0.02f, -0.2f + i * 0.12f), U(0.09f, -0.26f + i * 0.12f), 1 * S, line, 504 + i, 0, tipIn: 0.3f, tipOut: 0.3f);
                // Espuela: estrella pequeña con su brazo.
                Vector2 spur = U(-0.4f, 0.3f);
                Stroke(U(-0.24f, 0.32f), spur, 1.4f * S, line, 508, 0, tipIn: 0, tipOut: 0);
                for (int i = 0; i < 4; i++)
                {
                    float ang = i * MathF.PI / 4 + Jit(509) * 0.08f;
                    var dd = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * size * 0.09f;
                    Raylib.DrawLineEx(spur - dd, spur + dd, 1.2f * S, line);
                }
                break;
            }
            case EmblemKind.Sword:
            {
                // Espada inclinada hacia arriba: hoja, acanaladura, gavilanes, puño y pomo.
                float rot = -0.6f + Jit(520) * 0.015f;
                Span<Vector2> blade = stackalloc Vector2[5];
                blade[0] = R(-0.05f, -0.055f); blade[1] = R(0.44f, -0.055f); blade[2] = R(0.58f, 0); blade[3] = R(0.44f, 0.055f); blade[4] = R(-0.05f, 0.055f);
                Fill(blade, A(Palette.Parchment, 0.85f * alpha));
                PenLoop(blade, 1.2f * S, A(Palette.Ink, 0.8f * alpha), 521);
                Stroke(R(0.0f, 0), R(0.38f, 0), 1 * S, A(Palette.Ink, 0.7f * alpha), 522, 0, tipIn: 0.05f, tipOut: 0.4f);
                Stroke(R(-0.06f, -0.22f), R(-0.06f, 0.22f), 2.8f * S, line, 523, 0.2f, tipIn: 0.15f, tipOut: 0.15f);
                Raylib.DrawCircleV(R(-0.06f, -0.25f), 1.8f * S, line);
                Raylib.DrawCircleV(R(-0.06f, 0.25f), 1.8f * S, line);
                Stroke(R(-0.08f, 0), R(-0.3f, 0), 3.4f * S, line, 524, 0.1f, tipIn: 0, tipOut: 0);
                for (int i = 0; i < 3; i++)
                    Raylib.DrawLineEx(R(-0.12f - i * 0.06f, -0.05f), R(-0.14f - i * 0.06f, 0.05f), 1 * S, A(Palette.Ink, 0.7f * alpha));
                Raylib.DrawCircleV(R(-0.35f, 0), size * 0.06f, line);

                Vector2 R(float x, float y)
                {
                    float cs = MathF.Cos(rot), sn = MathF.Sin(rot);
                    return c + new Vector2(x * cs - y * sn, x * sn + y * cs) * size;
                }
                break;
            }
            case EmblemKind.Brazier:
            {
                // Un frasco de brasa del que brota su llama.
                Vector2 at = c + new Vector2(0, size * 0.12f);
                float fs = size * 0.82f;
                Flask(at, fs, 1, alpha, 0.5f);
                Flame(FlaskMouth(at, fs) - new Vector2(0, 1 * S), size * 0.34f, alpha);
                break;
            }
        }

        Vector2 U(float x, float y) => c + new Vector2(x, y) * size;
    }

    /// <summary>
    /// Chorreón de tinta que cuelga de un trazo: se alarga despacio, suelta una gota y vuelve a
    /// empezar. Avanza a saltos de 12 fps, como un dibujo hecho fotograma a fotograma.
    /// </summary>
    public void Drip(Vector2 from, float maxLen, float thick, Color c, int seed)
    {
        if (c.A == 0) return;
        float period = 5f + 3f * Hash(seed);
        float ph = (Frame / 12f + Hash(seed + 1) * period) % period / period;
        float grow = Smooth(ph / 0.8f);
        float len = maxLen * (0.3f + 0.7f * grow);
        Span<Vector2> p = stackalloc Vector2[5];
        Span<float> w = stackalloc float[5];
        for (int i = 0; i < 5; i++)
        {
            float k = i / 4f;
            p[i] = from + new Vector2((Hash(seed + i) - 0.5f) * 0.6f * S, len * k);
            w[i] = thick * (1 - 0.55f * k);
        }
        Pen(p, w, c);
        Raylib.DrawCircleV(p[4] + new Vector2(0, thick * 0.3f), thick * (0.45f + 0.25f * grow), c);
        if (ph > 0.8f)
        {
            float fall = (ph - 0.8f) / 0.2f;
            Raylib.DrawCircleV(p[4] + new Vector2(0, thick + fall * fall * 70 * S), thick * 0.4f, A(c, (1 - fall) * c.A / 255f));
        }
    }
}
