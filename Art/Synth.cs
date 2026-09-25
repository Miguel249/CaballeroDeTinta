using CaballeroDeTinta.Sim;

namespace CaballeroDeTinta.Art;

/// <summary>
/// Sonido sintetizado en código, como el resto del arte: no hay archivos de audio.
/// Foley de dibujo animado (latigazos, xilófono de huesos, trombón triste, "¡bonk!") y una partitura
/// de orquesta pequeña de los años treinta: vals melancólico para explorar, galope para el jefe.
/// Todo pasa por una "banda óptica": sin graves profundos ni agudos brillantes, algo saturado.
/// </summary>
static class Synth
{
    public const int Rate = 44100;

    // ================================================================= utilidades

    static float[] Buf(float seconds) => new float[(int)(seconds * Rate)];

    public static float Midi(float note) => 440f * MathF.Pow(2, (note - 69) / 12f);

    static float Exp(float t, float tau) => t < 0 ? 0 : MathF.Exp(-t / tau);

    /// <summary>Se mantiene a 1 hasta <paramref name="hold"/> y luego se apaga.</summary>
    static float Hold(float t, float hold, float tau) => t < hold ? 1 : MathF.Exp(-(t - hold) / tau);

    /// <summary>Filtro de estado variable (Simper): paso bajo, banda y alto a la vez.</summary>
    struct Svf
    {
        float _ic1, _ic2;
        public float Low, Band, High;

        public void Run(float x, float cutoff, float q)
        {
            float g = MathF.Tan(MathF.PI * Math.Clamp(cutoff, 20, Rate * 0.45f) / Rate);
            float k = 1 / q;
            float a1 = 1 / (1 + g * (g + k)), a2 = g * a1, a3 = g * a2;
            float v3 = x - _ic2;
            float v1 = a1 * _ic1 + a2 * v3;
            float v2 = _ic2 + a2 * _ic1 + a3 * v3;
            _ic1 = 2 * v1 - _ic1;
            _ic2 = 2 * v2 - _ic2;
            Low = v2; Band = v1; High = x - k * v1 - v2;
        }
    }

    /// <summary>Parciales senoidales con caída propia: campanas, metales, xilófonos.</summary>
    static void Partials(float[] b, float start, float f0, float gain, (float Ratio, float Amp, float Decay)[] partials, float beat = 0)
    {
        int s0 = (int)(start * Rate);
        foreach (var (ratio, amp, decay) in partials)
        {
            float f = f0 * ratio;
            if (f > Rate * 0.45f) continue;
            int n = Math.Min(b.Length - s0, (int)(decay * 6 * Rate));
            // Osciladores por rotación (sin llamar a Sin ni a Exp en cada muestra).
            double wa = Math.Tau * f / Rate, wb = Math.Tau * (f + beat) / Rate;
            double ca = Math.Cos(wa), sa = Math.Sin(wa), cb = Math.Cos(wb), sb = Math.Sin(wb);
            double xa = 1, ya = 0, xb = 1, yb = 0;
            double env = amp * gain * (beat > 0 ? 0.5 : 1), fall = Math.Exp(-1.0 / (decay * Rate));
            for (int i = 0; i < n; i++)
            {
                double s = ya + (beat > 0 ? yb : 0);
                b[s0 + i] += (float)(s * env * Math.Min(1, i / 22.0));
                (xa, ya) = (xa * ca - ya * sa, xa * sa + ya * ca);
                if (beat > 0) (xb, yb) = (xb * cb - yb * sb, xb * sb + yb * cb);
                env *= fall;
            }
        }
    }

    /// <summary>Oscilador con altura variable en el tiempo: glissandos, silbatos, "boing".</summary>
    static void Glide(float[] b, float start, float length, Func<float, float> freq, Func<float, float> env, float gain, float saw = 0)
    {
        int s0 = (int)(start * Rate);
        int n = Math.Min(b.Length - s0, (int)(length * Rate));
        float ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            ph += freq(t) / Rate;
            ph -= MathF.Floor(ph);
            float s = MathF.Sin(MathF.Tau * ph) * (1 - saw) + (2 * ph - 1) * saw;
            b[s0 + i] += s * env(t) * gain;
        }
    }

    /// <summary>Ruido filtrado en banda con corte y envolvente variables.</summary>
    static void Noise(float[] b, int seed, float start, float length, Func<float, float> cutoff, float q, Func<float, float> env, float gain, bool low = false)
    {
        var rng = new Random(seed);
        var f = new Svf();
        int s0 = (int)(start * Rate);
        int n = Math.Min(b.Length - s0, (int)(length * Rate));
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            f.Run((float)rng.NextDouble() * 2 - 1, cutoff(t), q);
            b[s0 + i] += (low ? f.Low : f.Band) * env(t) * gain;
        }
    }

    /// <summary>
    /// Banda sonora óptica de película antigua: recorta graves y agudos, satura un poco y
    /// normaliza al pico pedido.
    /// </summary>
    static float[] Optical(float[] b, float peak = 0.9f, float lowCut = 70, float highCut = 6500)
    {
        var hp = new Svf();
        var lp = new Svf();
        float max = 1e-6f;
        for (int i = 0; i < b.Length; i++)
        {
            hp.Run(b[i], lowCut, 0.7f);
            lp.Run(hp.High, highCut, 0.7f);
            b[i] = MathF.Tanh(lp.Low * 1.3f);
            max = MathF.Max(max, MathF.Abs(b[i]));
        }
        float g = peak / max;
        for (int i = 0; i < b.Length; i++) b[i] *= g;
        // Sin chasquidos al principio ni al final.
        int fade = Math.Min(b.Length / 4, 200);
        for (int i = 0; i < fade; i++) b[b.Length - 1 - i] *= i / (float)fade;
        return b;
    }

    // ================================================================= efectos

    /// <summary>Número de variantes de cada efecto (se alternan para que no suene a máquina).</summary>
    public static int Variants(Sfx s) => s is Sfx.Step or Sfx.BoneStep or Sfx.Hit or Sfx.Swing or Sfx.Knock or Sfx.Clang ? 3 : 1;

    public static float[] Make(Sfx sfx, int v = 0)
    {
        int seed = (int)sfx * 101 + v * 7919;
        var rng = new Random(seed);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        float[] b;
        switch (sfx)
        {
            case Sfx.Swing:
            {
                // Latigazo de aire: ruido en banda que sube y vuelve a bajar.
                float len = 0.24f, top = R(2300, 3000);
                b = Buf(len);
                Noise(b, seed, 0, len, t => 500 + (top - 500) * MathF.Sin(MathF.PI * MathF.Min(1, t / len * 1.3f)), 2.2f,
                      t => MathF.Pow(MathF.Sin(MathF.PI * t / len), 2), 1.4f);
                return Optical(b, 0.7f);
            }
            case Sfx.HeavySwing:
            {
                float len = 0.5f;
                b = Buf(len);
                Noise(b, seed, 0, len, t => 220 + 1300 * MathF.Sin(MathF.PI * MathF.Min(1, t / len * 1.2f)), 1.6f,
                      t => MathF.Pow(MathF.Sin(MathF.PI * t / len), 1.5f), 1.6f);
                Noise(b, seed + 1, 0, len, t => 160, 0.8f, t => MathF.Sin(MathF.PI * t / len), 0.8f, low: true);
                return Optical(b, 0.85f);
            }
            case Sfx.Dodge:
            {
                // Rodar: aire, tela que aletea y un golpecito al caer.
                float len = 0.4f;
                b = Buf(len);
                Noise(b, seed, 0, 0.3f, t => 400 + 1500 * t / 0.3f, 1.8f, t => MathF.Sin(MathF.PI * t / 0.3f) * (0.6f + 0.4f * MathF.Sin(t * MathF.Tau * 32)), 1.2f);
                Glide(b, 0.27f, 0.12f, t => 95 - t * 200, t => Exp(t, 0.03f), 0.5f);
                return Optical(b, 0.55f);
            }
            case Sfx.Step:
            {
                b = Buf(0.12f);
                Noise(b, seed, 0, 0.12f, t => R(500, 900), 0.8f, t => Exp(t, 0.016f), 1f, low: true);
                Glide(b, 0, 0.1f, t => 80 - t * 200, t => Exp(t, 0.025f), 0.5f);
                return Optical(b, 0.28f);
            }
            case Sfx.BoneStep:
            {
                // Hueso contra losa: dos chasquidos secos.
                b = Buf(0.1f);
                float f = R(1300, 1700);
                Partials(b, 0, f, 0.8f, [(1, 1, 0.01f), (2.3f, 0.4f, 0.006f)]);
                Partials(b, 0.03f, f * 1.1f, 0.5f, [(1, 1, 0.008f), (2.3f, 0.4f, 0.005f)]);
                return Optical(b, 0.25f);
            }
            case Sfx.KingStep:
            {
                b = Buf(0.6f);
                Glide(b, 0, 0.6f, t => 52 - t * 20, t => Exp(t, 0.14f), 1f);
                Noise(b, seed, 0, 0.3f, t => 260, 0.7f, t => Exp(t, 0.05f), 0.9f, low: true);
                // Cota de malla que tintinea.
                Partials(b, 0.02f, 2100, 0.08f, [(1, 1, 0.05f), (1.37f, 0.7f, 0.04f)]);
                return Optical(b, 0.7f, lowCut: 45);
            }
            case Sfx.Hit:
            {
                // ¡Zas!: chasquido y golpe sordo.
                b = Buf(0.32f);
                Noise(b, seed, 0, 0.1f, t => R(1600, 2200), 0.9f, t => Exp(t, 0.018f), 1.4f);
                Glide(b, 0, 0.3f, t => 170 * MathF.Exp(-t * 9) + 55, t => Exp(t, 0.07f), 1f);
                return Optical(b, 0.85f);
            }
            case Sfx.HeavyHit:
            {
                b = Buf(0.7f);
                Noise(b, seed, 0, 0.5f, t => 3200 * MathF.Exp(-t * 7) + 250, 0.8f, t => Exp(t, 0.08f), 1.6f);
                Glide(b, 0, 0.7f, t => 120 * MathF.Exp(-t * 6) + 34, t => Exp(t, 0.17f), 1.3f);
                Noise(b, seed + 3, 0, 0.05f, t => 5000, 0.7f, t => Exp(t, 0.006f), 1.2f);
                return Optical(b, 0.95f);
            }
            case Sfx.Knock:
            {
                // Madera y piedra: bloque de percusión de estudio de doblaje.
                b = Buf(0.3f);
                float f = R(180, 260);
                Partials(b, 0, f, 1, [(1, 1, 0.05f), (2.3f, 0.5f, 0.03f), (3.9f, 0.25f, 0.015f)]);
                Noise(b, seed, 0, 0.04f, t => 2500, 0.8f, t => Exp(t, 0.005f), 0.8f);
                return Optical(b, 0.6f);
            }
            case Sfx.Clang:
            {
                b = Buf(1.0f);
                float f = R(560, 700);
                Partials(b, 0, f, 1, [(1, 1, 0.35f), (1.51f, 0.35f, 0.3f), (2.76f, 0.6f, 0.2f), (5.4f, 0.4f, 0.12f), (8.93f, 0.25f, 0.08f)], beat: 3);
                Noise(b, seed, 0, 0.03f, t => 4000, 0.8f, t => Exp(t, 0.004f), 1f);
                return Optical(b, 0.6f);
            }
            case Sfx.Parry:
            {
                // ¡Tiiing!: acero contra acero, largo y brillante.
                b = Buf(1.4f);
                Partials(b, 0, 1180, 1, [(1, 1, 0.55f), (1.5f, 0.5f, 0.45f), (2.0f, 0.55f, 0.35f), (2.67f, 0.4f, 0.25f), (3.99f, 0.3f, 0.18f)], beat: 4);
                Noise(b, seed, 0, 0.35f, t => 3000 + 5000 * t / 0.35f, 3f, t => Exp(t, 0.1f), 0.9f);
                return Optical(b, 0.9f, highCut: 8000);
            }
            case Sfx.Hurt:
            {
                // Silbato que cae y un golpe: dolor de dibujo animado.
                b = Buf(0.55f);
                Glide(b, 0, 0.45f, t => 1150 * MathF.Pow(0.3f, t / 0.4f), t => MathF.Min(1, t * 60) * Exp(t, 0.18f), 0.8f);
                Noise(b, seed, 0, 0.45f, t => 1150 * MathF.Pow(0.3f, t / 0.4f), 6f, t => Exp(t, 0.15f), 0.4f);
                Glide(b, 0, 0.25f, t => 140 * MathF.Exp(-t * 8) + 50, t => Exp(t, 0.06f), 0.9f);
                return Optical(b, 0.8f);
            }
            case Sfx.Death:
            {
                // El trombón triste: "wah, wah, wah, waaaah".
                b = Buf(3.6f);
                float[] notes = [58, 57, 56, 55];
                for (int i = 0; i < 4; i++)
                {
                    float start = i * 0.55f, len = i < 3 ? 0.5f : 2.2f;
                    Brass(b, start, len, Midi(notes[i] - 12), 0.9f, wah: true, vibrato: i == 3 ? 0.03f : 0.006f);
                }
                return Optical(b, 0.9f);
            }
            case Sfx.Shatter:
            {
                // Esqueleto que se desmonta: xilófono desordenado.
                b = Buf(1.0f);
                float[] scale = [72, 74, 76, 79, 81, 84, 86, 88, 91, 93];
                for (int i = 0; i < 14; i++)
                {
                    float at = MathF.Pow((float)rng.NextDouble(), 1.6f) * 0.6f;
                    Partials(b, at, Midi(scale[rng.Next(scale.Length)]), R(0.4f, 1f), [(1, 1, 0.07f), (3.93f, 0.3f, 0.02f)]);
                }
                Noise(b, seed, 0, 0.15f, t => 1800, 0.7f, t => Exp(t, 0.03f), 0.8f);
                return Optical(b, 0.8f, highCut: 8000);
            }
            case Sfx.Rattle:
            {
                // Castañeteo de huesos: aviso del golpe del esqueleto.
                b = Buf(0.55f);
                float at = 0;
                for (int i = 0; i < 12; i++)
                {
                    Partials(b, at, R(1400, 1900), 0.6f + 0.4f * i / 12f, [(1, 1, 0.007f), (2.1f, 0.4f, 0.004f)]);
                    at += 0.055f - i * 0.0025f;
                }
                return Optical(b, 0.4f);
            }
            case Sfx.Scrape:
            {
                // Punta de la espada arrastrada por la piedra.
                b = Buf(0.2f);
                Noise(b, seed, 0, 0.2f, t => 3400, 5f, t => MathF.Sin(MathF.PI * t / 0.2f) * (0.5f + 0.5f * (float)rng.NextDouble()), 1.2f);
                Noise(b, seed + 1, 0, 0.2f, t => 5200, 7f, t => MathF.Sin(MathF.PI * t / 0.2f), 0.6f);
                return Optical(b, 0.35f, highCut: 7500);
            }
            case Sfx.Rise:
            {
                // Tensión de dibujo animado: carraca que se acelera y metales que suben.
                b = Buf(1.3f);
                Brass(b, 0, 1.2f, 55, 0.6f, rise: 0.5f);
                float at = 0;
                for (int i = 0; i < 16; i++)
                {
                    Noise(b, seed + i, at, 0.02f, t => 1500, 1.5f, t => Exp(t, 0.004f), 0.9f);
                    at += 0.1f * MathF.Pow(0.9f, i);
                }
                return Optical(b, 0.7f);
            }
            case Sfx.Slam:
            {
                // ¡Bum!: el suelo del castillo entero.
                b = Buf(2.0f);
                Glide(b, 0, 2f, t => 64 * MathF.Exp(-t * 1.5f) + 28, t => Exp(t, 0.45f), 1.4f);
                Noise(b, seed, 0, 1.6f, t => 900 * MathF.Exp(-t * 2.5f) + 110, 0.7f, t => Exp(t, 0.35f), 1.8f, low: true);
                Noise(b, seed + 1, 0, 0.06f, t => 3000, 0.6f, t => Exp(t, 0.012f), 1.6f);
                for (int i = 0; i < b.Length; i++) b[i] = MathF.Tanh(b[i] * 1.8f);
                return Optical(b, 1f, lowCut: 35);
            }
            case Sfx.Bump:
            {
                // ¡Bonk!: bloque de madera y el muelle que vibra.
                b = Buf(0.9f);
                Partials(b, 0, 520, 1, [(1, 1, 0.05f), (2.27f, 0.6f, 0.03f)]);
                Glide(b, 0.02f, 0.85f, t => 190 * (1 + 0.14f * MathF.Sin(MathF.Tau * 17 * t) * Exp(t, 0.35f)), t => MathF.Min(1, t * 200) * Exp(t, 0.28f), 0.7f, saw: 0.3f);
                return Optical(b, 0.8f);
            }
            case Sfx.Boing:
            {
                // La corona se le escurre: arpa de boca.
                b = Buf(0.7f);
                Glide(b, 0, 0.7f, t => 330 * (1 - 0.35f * t) * (1 + 0.22f * MathF.Sin(MathF.Tau * 14 * t) * Exp(t, 0.3f)), t => MathF.Min(1, t * 300) * Exp(t, 0.22f), 0.8f, saw: 0.4f);
                return Optical(b, 0.6f);
            }
            case Sfx.Bell:
            {
                // Campana de bronce (parciales de campana de iglesia) con batido.
                b = Buf(5.5f);
                Partials(b, 0, 147, 1, [(0.5f, 0.5f, 4f), (1, 1, 3f), (1.183f, 0.8f, 2.2f), (1.506f, 0.4f, 1.8f), (2.0f, 0.6f, 1.4f),
                                         (2.514f, 0.3f, 1f), (2.662f, 0.25f, 0.9f), (3.011f, 0.2f, 0.7f), (4.166f, 0.15f, 0.5f)], beat: 0.7f);
                Noise(b, seed, 0, 0.05f, t => 2000, 0.7f, t => Exp(t, 0.01f), 0.6f);
                return Optical(b, 0.8f, lowCut: 50);
            }
            case Sfx.Heal:
            {
                // Tres tragos y un destello.
                b = Buf(1.2f);
                for (int i = 0; i < 3; i++)
                    Glide(b, i * 0.16f, 0.08f, t => 280 + t * 3500, t => MathF.Sin(MathF.PI * t / 0.08f), 0.7f);
                Partials(b, 0.55f, Midi(84), 0.5f, [(1, 1, 0.5f), (1.5f, 0.6f, 0.4f), (2, 0.3f, 0.3f)]);
                Partials(b, 0.65f, Midi(88), 0.4f, [(1, 1, 0.5f), (1.5f, 0.5f, 0.4f)]);
                return Optical(b, 0.55f, highCut: 8000);
            }
            case Sfx.Bonfire:
            {
                // ¡Fuuum!: la hoguera prende.
                b = Buf(1.6f);
                Noise(b, seed, 0, 1.6f, t => 100 + 1700 * MathF.Sin(MathF.PI * MathF.Min(1, t / 0.9f)), 0.8f, t => MathF.Min(1, t / 0.15f) * Hold(t, 0.15f, 0.45f), 1.6f, low: true);
                Glide(b, 0, 1.2f, t => 75, t => MathF.Min(1, t / 0.1f) * Exp(t, 0.3f), 0.5f);
                Crackle(b, seed, 0.1f, 1.4f, 26, 0.5f);
                return Optical(b, 0.7f);
            }
            case Sfx.Roar:
            {
                // Rugido del rey: gruñido áspero con metales detrás.
                b = Buf(2.0f);
                var lp = new Svf();
                float ph = 0;
                for (int i = 0; i < b.Length; i++)
                {
                    float t = i / (float)Rate;
                    ph += (66 + 8 * MathF.Sin(t * 5) + (float)rng.NextDouble() * 12) / Rate;
                    ph -= MathF.Floor(ph);
                    float growl = (2 * ph - 1) * (0.6f + 0.4f * MathF.Sin(MathF.Tau * 31 * t)) + ((float)rng.NextDouble() - 0.5f) * 0.5f;
                    lp.Run(growl, 400 + 900 * MathF.Sin(MathF.PI * MathF.Min(1, t / 1.8f)), 1.2f);
                    b[i] = lp.Low * MathF.Min(1, t / 0.2f) * Hold(t, 1.2f, 0.3f);
                }
                Brass(b, 0.1f, 1.5f, Midi(38), 0.5f);
                Brass(b, 0.1f, 1.5f, Midi(39), 0.4f);
                for (int i = 0; i < b.Length; i++) b[i] = MathF.Tanh(b[i] * 2.5f);
                return Optical(b, 0.95f);
            }
            case Sfx.KingFall:
            {
                // Tuba que se desinfla y el rey que cae de bruces.
                b = Buf(3.4f);
                Brass(b, 0, 2.2f, 220, 0.9f, rise: -2.0f, wah: true);
                Glide(b, 2.2f, 1.2f, t => 60 * MathF.Exp(-t * 2) + 30, t => Exp(t, 0.35f), 1.4f);
                Noise(b, seed, 2.2f, 1f, t => 700 * MathF.Exp(-t * 3) + 100, 0.7f, t => Exp(t, 0.25f), 1.4f, low: true);
                return Optical(b, 0.95f, lowCut: 40);
            }
        }
        return new float[1];
    }

    static void Crackle(float[] b, int seed, float start, float length, int pops, float gain)
    {
        var rng = new Random(seed);
        for (int i = 0; i < pops; i++)
        {
            float at = start + (float)rng.NextDouble() * length;
            float f = 1500 + (float)rng.NextDouble() * 3500;
            Noise(b, seed + i * 13, at, 0.015f, t => f, 1.2f, t => Exp(t, 0.003f), gain * (0.3f + (float)rng.NextDouble()));
        }
    }

    // ================================================================= instrumentos

    /// <summary>Metal con sordina: diente de sierra con un filtro que se abre al soplar.</summary>
    static void Brass(float[] b, float start, float length, float freq, float gain, float vibrato = 0.006f, bool wah = false, float rise = 0)
    {
        var lp = new Svf();
        int s0 = (int)(start * Rate);
        int n = Math.Min(b.Length - s0, (int)((length + 0.12f) * Rate));
        float ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float f = freq * MathF.Pow(2, rise * t / length) * (1 + vibrato * MathF.Sin(MathF.Tau * 5.5f * t) * MathF.Min(1, t * 3));
            ph += f / Rate;
            ph -= MathF.Floor(ph);
            float env = MathF.Min(1, t / 0.03f) * Hold(t, length, 0.04f);
            float open = wah ? 0.25f + 0.75f * MathF.Sin(MathF.PI * MathF.Min(1, t / MathF.Min(length, 0.5f))) : 0.55f + 0.45f * env;
            lp.Run(2 * ph - 1, f * (1.5f + 7 * open), 1.1f);
            b[s0 + i] += lp.Low * env * gain;
        }
    }

    /// <summary>Piano vertical desafinado: armónicos que se apagan antes cuanto más agudos.</summary>
    static void Piano(float[] b, float start, float freq, float gain, float decay = 1.1f)
    {
        Partials(b, start, freq, gain * 0.5f, [(1, 1, decay), (2.002f, 0.45f, decay * 0.6f), (3.005f, 0.25f, decay * 0.4f), (4.01f, 0.12f, decay * 0.3f), (5.02f, 0.06f, decay * 0.2f)], beat: 0.8f);
    }

    /// <summary>Tuba: triángulo redondo con cuerpo en el segundo armónico.</summary>
    static void Tuba(float[] b, float start, float length, float freq, float gain)
    {
        int s0 = (int)(start * Rate);
        int n = Math.Min(b.Length - s0, (int)((length + 0.08f) * Rate));
        float ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            ph += freq / Rate;
            ph -= MathF.Floor(ph);
            float tri = 1 - 4 * MathF.Abs(ph - 0.5f);
            float env = MathF.Min(1, t / 0.025f) * (t < length ? 1 - 0.3f * t / length : Exp(t - length, 0.03f) * 0.7f);
            b[s0 + i] += (tri + 0.35f * MathF.Sin(MathF.Tau * 2 * ph)) * env * gain;
        }
    }

    /// <summary>Clarinete: armónicos impares, vibrato lento y un poco de aire.</summary>
    static void Clarinet(float[] b, float start, float length, float freq, float gain)
    {
        var lp = new Svf();
        int s0 = (int)(start * Rate);
        int n = Math.Min(b.Length - s0, (int)((length + 0.1f) * Rate));
        float ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            ph += freq * (1 + 0.005f * MathF.Sin(MathF.Tau * 5 * t) * MathF.Min(1, t * 2)) / Rate;
            ph -= MathF.Floor(ph);
            float sq = ph < 0.5f ? 1 : -1;
            lp.Run(sq, freq * 4.5f, 0.8f);
            float env = MathF.Min(1, t / 0.05f) * Hold(t, length, 0.05f);
            b[s0 + i] += lp.Low * env * gain;
        }
    }

    static void Snare(float[] b, int seed, float start, float gain) =>
        Noise(b, seed, start, 0.18f, t => 2600, 0.9f, t => Exp(t, 0.045f), gain);

    static void Timpani(float[] b, float start, float freq, float gain) =>
        Glide(b, start, 1.2f, t => freq * (1 + 0.1f * Exp(t, 0.03f)), t => MathF.Min(1, t * 500) * Exp(t, 0.35f), gain);

    // ================================================================= música

    /// <summary>
    /// Suma a un bucle las colas que se salen del final, para que el bucle empalme sin cortes.
    /// </summary>
    static float[] Fold(float[] b, int loop)
    {
        var o = new float[loop];
        for (int i = 0; i < b.Length; i++) o[i % loop] += b[i];
        return o;
    }

    static readonly int[][] WaltzChords =
    [
        [62, 65, 69], [62, 65, 69], [62, 67, 70], [61, 64, 67], [62, 65, 69], [62, 65, 70], [62, 67, 70], [61, 64, 67],
        [60, 65, 69], [60, 64, 70], [62, 65, 69], [61, 64, 69], [62, 67, 70], [62, 65, 69], [61, 64, 67], [62, 65, 69],
    ];
    static readonly int[] WaltzBass = [38, 38, 43, 45, 38, 46, 43, 45, 41, 36, 38, 45, 43, 45, 45, 38];
    static readonly (float At, float Len, int Note)[][] WaltzTune =
    [
        [(0, 2, 69), (2, 1, 74)], [(0, 1.5f, 77), (1.5f, 0.5f, 76), (2, 1, 74)], [(0, 2, 74), (2, 1, 70)], [(0, 1.5f, 73), (1.5f, 0.5f, 76), (2, 1, 79)],
        [(0, 3, 77)], [(0, 1, 74), (1, 1, 70), (2, 1, 74)], [(0, 1.5f, 79), (1.5f, 0.5f, 77), (2, 1, 74)], [(0, 2, 76), (2, 1, 73)],
        [(0, 1, 72), (1, 1, 77), (2, 1, 81)], [(0, 1.5f, 79), (1.5f, 0.5f, 77), (2, 1, 76)], [(0, 2, 77), (2, 1, 74)], [(0, 1, 76), (1, 1, 73), (2, 1, 69)],
        [(0, 1, 70), (1, 1, 74), (2, 1, 79)], [(0, 1.5f, 77), (1.5f, 0.5f, 76), (2, 1, 74)], [(0, 2, 73), (2, 1, 76)], [(0, 3, 74)],
    ];

    /// <summary>Vals del reino moribundo (re menor, 3/4): tuba, piano "um-pa-pa" y clarinete.</summary>
    public static float[] Waltz()
    {
        const float beat = 60f / 92;
        int bars = WaltzChords.Length;
        int loop = (int)(bars * 3 * beat * Rate);
        var b = new float[loop + Rate * 3];
        for (int bar = 0; bar < bars; bar++)
        {
            float t0 = bar * 3 * beat;
            Tuba(b, t0, beat * 0.9f, Midi(WaltzBass[bar]), 0.5f);
            foreach (int beatIndex in new[] { 1, 2 })
                foreach (int n in WaltzChords[bar])
                    Piano(b, t0 + beatIndex * beat + 0.01f * (n % 3), Midi(n), beatIndex == 1 ? 0.22f : 0.17f, 0.5f);
            foreach (var (at, len, note) in WaltzTune[bar])
                Clarinet(b, t0 + at * beat, len * beat * 0.95f, Midi(note), 0.28f);
        }
        return Optical(Fold(b, loop), 0.8f, lowCut: 55);
    }

    static readonly int[][] GallopChords =
    [
        [62, 65, 69], [62, 65, 69], [62, 65, 70], [61, 64, 67], [62, 65, 69], [62, 65, 69], [62, 67, 70], [61, 64, 67],
        [62, 65, 70], [62, 65, 70], [62, 67, 70], [61, 64, 67], [62, 65, 69], [62, 67, 70], [61, 64, 67], [61, 64, 67],
    ];
    static readonly int[] GallopBass = [38, 38, 46, 45, 38, 38, 43, 45, 46, 46, 43, 45, 38, 43, 45, 45];
    static readonly (float At, float Len, int Note)[][] GallopTune =
    [
        [(0, .5f, 69), (.5f, .5f, 74), (1, .5f, 76), (1.5f, .5f, 77)], [(0, .5f, 76), (.5f, .5f, 74), (1, .5f, 73), (1.5f, .5f, 74)],
        [(0, 1, 77), (1, .5f, 74), (1.5f, .5f, 70)], [(0, 1.5f, 73), (1.5f, .5f, 69)],
        [(0, .5f, 74), (.5f, .5f, 77), (1, .5f, 79), (1.5f, .5f, 81)], [(0, .5f, 79), (.5f, .5f, 77), (1, .5f, 76), (1.5f, .5f, 77)],
        [(0, 1, 79), (1, .5f, 82), (1.5f, .5f, 79)], [(0, 2, 81)],
        [(0, .5f, 82), (.5f, .5f, 81), (1, .5f, 79), (1.5f, .5f, 77)], [(0, .5f, 74), (.5f, .5f, 77), (1, 1, 82)],
        [(0, .5f, 79), (.5f, .5f, 77), (1, .5f, 74), (1.5f, .5f, 70)], [(0, 1, 73), (1, 1, 76)],
        [(0, .5f, 77), (.5f, .5f, 76), (1, .5f, 74), (1.5f, .5f, 69)], [(0, .5f, 70), (.5f, .5f, 74), (1, .5f, 79), (1.5f, .5f, 82)],
        [(0, .5f, 81), (.5f, .5f, 79), (1, .5f, 76), (1.5f, .5f, 73)], [(0, 1, 69)],
    ];

    /// <summary>
    /// Galope del jefe (2/4): bajo de "stride", acordes a contratiempo, metales y caja.
    /// La segunda fase sube medio tono y acelera, como en las persecuciones de los cortos.
    /// </summary>
    public static float[] Gallop(bool phase2)
    {
        float beat = 60f / (phase2 ? 172 : 150);
        int shift = phase2 ? 1 : 0;
        int bars = GallopChords.Length;
        int loop = (int)(bars * 2 * beat * Rate);
        var b = new float[loop + Rate * 3];
        for (int bar = 0; bar < bars; bar++)
        {
            float t0 = bar * 2 * beat;
            Tuba(b, t0, beat * 0.45f, Midi(GallopBass[bar] - 12 + shift), 0.55f);
            Tuba(b, t0 + beat, beat * 0.45f, Midi(GallopBass[bar] - 5 + shift), 0.45f);
            foreach (float off in new[] { 0.5f, 1.5f })
                foreach (int n in GallopChords[bar])
                    Piano(b, t0 + off * beat, Midi(n + shift), 0.2f, 0.18f);
            foreach (var (at, len, note) in GallopTune[bar])
                Brass(b, t0 + at * beat, len * beat * 0.85f, Midi(note - 12 + shift), 0.3f);
            for (int i = 0; i < (phase2 ? 4 : 2); i++)
                Snare(b, bar * 31 + i, t0 + (phase2 ? i * 0.5f : i + 0.5f) * beat, i % 2 == 1 ? 0.35f : 0.22f);
            if (bar % 4 == 0) Timpani(b, t0, Midi(38 - 12 + shift), 0.6f);
            if (phase2 && bar % 2 == 1) foreach (int n in GallopChords[bar]) Brass(b, t0 + 1.5f * beat, beat * 0.3f, Midi(n + shift), 0.12f);
        }
        return Optical(Fold(b, loop), 0.85f, lowCut: 50);
    }

    /// <summary>El golpe de orquesta que acompaña al cartel del jefe, tras el silencio.</summary>
    public static float[] Stinger()
    {
        var b = Buf(3.2f);
        foreach (int n in new[] { 38, 45, 50, 53, 57, 62, 65 })
            Brass(b, 0, 0.35f + (n < 50 ? 1.4f : 0.5f), Midi(n), n < 50 ? 0.45f : 0.3f, vibrato: 0.012f);
        Timpani(b, 0, Midi(26), 1.2f);
        Timpani(b, 0.9f, Midi(26), 0.5f);
        Noise(b, 5, 0, 2.5f, t => 6000, 0.6f, t => Exp(t, 0.7f), 0.5f);
        return Optical(b, 0.95f, lowCut: 40);
    }

    /// <summary>Fanfarria de victoria (re mayor).</summary>
    public static float[] Fanfare()
    {
        var b = Buf(3.6f);
        float[] up = [62, 66, 69, 74];
        for (int i = 0; i < up.Length; i++) Brass(b, i * 0.16f, 0.14f, Midi(up[i]), 0.5f);
        foreach (int n in new[] { 50, 62, 66, 69, 74 })
            Brass(b, 0.7f, 1.8f, Midi(n), 0.3f, vibrato: 0.01f);
        Timpani(b, 0.7f, Midi(38), 0.9f);
        Partials(b, 0.7f, 147, 0.4f, [(1, 1, 2.5f), (2, 0.5f, 1.5f), (2.51f, 0.3f, 1f)], beat: 0.7f);
        return Optical(b, 0.9f);
    }

    /// <summary>El proyector: zumbido del obturador a 24 imágenes por segundo, siseo y chasquidos.</summary>
    public static float[] Projector()
    {
        var b = Buf(6f);
        var rng = new Random(24);
        var f = new Svf();
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)Rate;
            f.Run((float)rng.NextDouble() * 2 - 1, 3500, 0.6f);
            float gate = 0.55f + 0.45f * MathF.Max(0, MathF.Sin(MathF.Tau * 24 * t));
            b[i] = f.Band * 0.25f * gate + 0.06f * MathF.Sin(MathF.Tau * 48 * t);
        }
        Crackle(b, 77, 0, 6, 40, 0.8f);
        return Optical(Fold(b, b.Length), 0.35f, highCut: 7000);
    }

    /// <summary>Chisporroteo de la hoguera, en bucle.</summary>
    public static float[] Fire()
    {
        var b = Buf(5f);
        Noise(b, 9, 0, 5, t => 180, 0.7f, t => 1, 1f, low: true);
        Crackle(b, 10, 0, 5, 70, 1.2f);
        return Optical(Fold(b, b.Length), 0.5f);
    }

    // ================================================================= exportar

    /// <summary>WAV mono de 16 bits, para escuchar un sonido fuera del juego.</summary>
    public static void SaveWav(string path, float[] s)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + s.Length * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(s.Length * 2);
        foreach (float x in s) w.Write((short)(Math.Clamp(x, -1, 1) * 32767));
    }
}
