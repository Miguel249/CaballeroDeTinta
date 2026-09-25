using System.Numerics;
using System.Runtime.CompilerServices;
using CaballeroDeTinta.Art;
using CaballeroDeTinta.Sim;
using Raylib_cs;

namespace CaballeroDeTinta.View;

/// <summary>
/// Personajes hechos de primitivas y animados a mano con código: brazos y piernas de manguera de goma,
/// squash & stretch, anticipación y poses que solo cambian a 12 fps (animación "a doses").
/// Los golpes rápidos llevan smear frames de verdad (el dibujo intermedio que cubre el arco de la hoja,
/// a pincel seco) y los movimientos bruscos dejan múltiplos: copias del dibujo que se quedan atrás.
/// </summary>
sealed class Rigs(Ink ink) : IDisposable
{
    Matrix4x4 _root;
    float _time;   // tiempo de animación, cuantizado a doses

    // Esqueleto con modelo glTF (KayKit, CC0). Si falta el archivo se dibuja con primitivas.
    readonly Puppet? _skeleton = Puppet.TryLoad("Skeleton_Warrior.glb");
    readonly Model? _blade = Puppet.TryLoadProp("Skeleton_Blade.gltf");
    readonly Model? _shield = Puppet.TryLoadProp("Skeleton_Shield_Small_A.gltf");
    readonly ConditionalWeakTable<Skeleton, ClipMemory> _clips = new();

    /// <summary>Dibujar los esqueletos con el modelo 3D (si está disponible) o con primitivas.</summary>
    public bool UseModels = true;
    public bool HasSkeletonModel => _skeleton != null;

    public void SetTime(float time) => _time = MathF.Floor(time * 12f) / 12f;

    void Root(Vector3 feet, float yaw, Vector3 squash, float lean = 0, float roll = 0) =>
        _root = Matrix4x4.CreateScale(squash)
              * Matrix4x4.CreateFromYawPitchRoll(0, lean, roll)
              * Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, yaw)
              * Matrix4x4.CreateTranslation(feet);

    Vector3 W(Vector3 local) => Vector3.Transform(local, _root);

    void P(Shape3 s, Vector3 pos, Vector3 scale, Color c, Quaternion? rot = null, float outline = 0.03f, float emissive = 0) =>
        ink.Draw(s, Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rot ?? Quaternion.Identity) * Matrix4x4.CreateTranslation(pos) * _root, c, outline, emissive);

    static Vector3 Squash(float y) => new(1 / MathF.Sqrt(y), y, 1 / MathF.Sqrt(y));

    static float Ease(float t) => t * t * (3 - 2 * t);

    // ================================================================== smears y múltiplos

    /// <summary>Lo que dura un dibujo a doses: el smear cubre el arco recorrido en ese tiempo.</summary>
    const float DrawingTime = 1f / 12f;

    static float Hash(int a, int b, int c, int d)
    {
        uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791) ^ (uint)d * 2654435761u;
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xffff) / 65535f;
    }

    static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        // Las dos caras: el smear se ve por delante y por detrás.
        Raylib.DrawTriangle3D(a, b, c, color); Raylib.DrawTriangle3D(a, c, d, color);
        Raylib.DrawTriangle3D(a, c, b, color); Raylib.DrawTriangle3D(a, d, c, color);
    }

    /// <summary>
    /// Smear frame: la forma intermedia que un animador pinta entre dos poses de un golpe demasiado rápido
    /// para verse. Rellena el arco que ha barrido la hoja durante el último dibujo, con pincel seco: vetas
    /// largas en la punta, cortas junto a la empuñadura, que se rompen hacia el final de la estela.
    /// El trazo se redibuja distinto en cada dibujo, como si fuera a mano.
    /// </summary>
    /// <param name="pose">Mano y dirección de la hoja (en el espacio del personaje) en un instante del estado.</param>
    /// <param name="from">Inicio del tramo rápido del golpe.</param>
    /// <param name="to">Fin del tramo rápido.</param>
    void Smear(Func<float, (Vector3 Hand, Vector3 Dir)> pose, float p, float from, float to, float inner, float outer, Color paint, int seed)
    {
        float p0 = MathF.Max(from, p - DrawingTime), p1 = MathF.Min(p, to);
        if (p1 - p0 < 0.004f) return;
        const int steps = 14, strands = 9;
        var hands = new Vector3[steps + 1];
        var dirs = new Vector3[steps + 1];
        for (int i = 0; i <= steps; i++) (hands[i], dirs[i]) = pose(p0 + (p1 - p0) * i / steps);
        Vector3 At(int i, float u) => W(hands[i] + dirs[i] * (inner + (outer - inner) * u));

        int drawing = (int)(_time * 12);
        for (int j = 0; j < strands; j++)
        {
            // Cada veta es una pasada del pincel: más larga cuanto más cerca de la punta, con su propio
            // largo en cada dibujo, separada de la vecina por un hilo de papel y afilada en la cola.
            float r = (j + 0.5f) / strands;
            float length = (0.25f + 0.75f * r * r) * (0.7f + 0.3f * Hash(seed, drawing, j, 1));
            if (j > 0 && j < strands - 1 && Hash(seed, drawing, j, 2) < 0.18f) continue; // una pasada que falla
            float u0 = j / (float)strands, u1 = (j + 0.82f) / strands;
            if (j == strands - 1) u1 = 1;
            int first = steps - Math.Max(1, (int)MathF.Round(steps * length));
            Color c = j == strands - 1 ? Palette.Ink : j == 0 ? Palette.Mix(paint, Palette.Ink, 0.45f) : paint;
            for (int i = first; i < steps; i++)
            {
                float taperA = Math.Clamp((i - first) / (steps * length * 0.4f), 0, 1);
                float taperB = Math.Clamp((i + 1 - first) / (steps * length * 0.4f), 0, 1);
                float midU = (u0 + u1) / 2, half = (u1 - u0) / 2;
                Quad(At(i, midU - half * taperA), At(i + 1, midU - half * taperB), At(i + 1, midU + half * taperB), At(i, midU + half * taperA), c);
            }
        }
        // Filo de tinta a lo largo de la punta, afilado hacia la pose antigua.
        for (int i = 0; i < steps; i++)
        {
            float wa = 0.12f * i / steps, wb = 0.12f * (i + 1) / steps;
            Quad(At(i, 1), At(i + 1, 1), At(i + 1, 1 + wb), At(i, 1 + wa), Palette.Ink);
        }
    }

    /// <summary>
    /// Múltiplos: en un movimiento muy rápido el animador dibuja el personaje varias veces a lo largo
    /// del recorrido. Aquí son siluetas planas y translúcidas que se quedan atrás.
    /// </summary>
    void Multiples(Vector3 feet, Vector3 velocity, float yaw, int count, float gap, Action<float> silhouette)
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        for (int e = count; e >= 1; e--)
        {
            Root(feet - velocity * gap * e, yaw, Vector3.One);
            silhouette(1 - e / (count + 1f));
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
    }

    static Color Ghost(Color c, float alpha) => Palette.Alpha(Palette.Mix(c, Palette.Ink, 0.2f), alpha);

    // Poses de las hojas en los golpes rápidos: las usan tanto la pose como su smear.

    static (Vector3 Hand, Vector3 Dir) LightBlade(Knight k, float p)
    {
        float side = k.Combo % 2 == 0 ? 1 : -1;
        float a = Math.Clamp((p - Sim.Knight.LightWindup) / (Sim.Knight.LightActive + 0.06f), 0, 1);
        float ang = side * (1.4f - a * 2.9f);
        return (new Vector3(MathF.Sin(ang) * 0.6f, 1.0f, MathF.Cos(ang) * 0.55f + 0.1f), Vector3.Normalize(new Vector3(MathF.Sin(ang), -0.05f, MathF.Cos(ang))));
    }

    static (Vector3 Hand, Vector3 Dir) HeavyBlade(float p)
    {
        float a = Math.Clamp((p - Sim.Knight.HeavyWindup) / 0.16f, 0, 1);
        return (Vector3.Lerp(new Vector3(0.15f, 1.9f, 0.1f), new Vector3(0.1f, 0.55f, 0.95f), a),
                Vector3.Normalize(Vector3.Lerp(new Vector3(0, 1, 0.3f), new Vector3(0, -0.55f, 1f), a)));
    }

    const float BoneChop = 0.1f;

    static (Vector3 Hand, Vector3 Dir) SkeletonBlade(float p)
    {
        // Tajo por encima de la cabeza: de apuntar atrás a clavarse delante.
        float a = Ease(Math.Clamp(p / BoneChop, 0, 1));
        float ang = -1.28f + a * 3.39f;
        return (Vector3.Lerp(new Vector3(0.3f, 2.1f, -0.4f), new Vector3(0.1f, 0.5f, 1.0f), a), new Vector3(0, MathF.Cos(ang), MathF.Sin(ang)));
    }

    static (Vector3 Hand, Vector3 Dir) SweepBlade(float p)
    {
        float a = Math.Clamp((p - FallenKing.SweepWindup) / FallenKing.SweepActive, 0, 1);
        float ang = 1.9f - a * 3.8f;
        return (new Vector3(MathF.Sin(ang) * 2.2f, 2.0f, MathF.Cos(ang) * 1.8f + 0.4f), Vector3.Normalize(new Vector3(MathF.Sin(ang), -0.12f, MathF.Cos(ang))));
    }

    static (Vector3 Hand, Vector3 Dir) SlamBlade(float p)
    {
        float a = Math.Clamp((p - FallenKing.SlamWindup) / 0.1f, 0, 1);
        return (Vector3.Lerp(new Vector3(0.3f, 6, 1), new Vector3(0.3f, 1.4f, 2.4f), a),
                Vector3.Normalize(Vector3.Lerp(new Vector3(0, 1, 0.3f), new Vector3(0, -0.28f, 1), a)));
    }

    // ================================================================== caballero

    public void DrawKnight(Knight k)
    {
        if (k.Body.IsEnabled) ink.BlobShadow(k.Feet + ShadowDrift(1), 0.55f);

        float t = _time;
        float speed = Math.Clamp(k.Speed / 4.4f, 0, 1);
        float s = MathF.Sin(k.WalkPhase * MathF.PI);
        float lowHp = k.Health < k.MaxHealth * 0.3f ? 1 : 0;

        float bob = MathF.Abs(s) * 0.12f * speed + MathF.Sin(t * 2.2f) * 0.015f;
        float squashY = 1 - 0.07f * speed * (1 - MathF.Abs(s)) + MathF.Sin(t * 2.2f) * 0.012f;
        float lean = 0.12f * speed + lowHp * 0.18f;
        float stretchZ = 1f;
        float helmetTilt = lowHp * 0.25f, helmetSquash = 1f;
        Vector3 footL = new(-0.17f, MathF.Max(0, s) * 0.25f * speed + 0.12f, s * 0.34f * speed);
        Vector3 footR = new(0.17f, MathF.Max(0, -s) * 0.25f * speed + 0.12f, -s * 0.34f * speed);
        Vector3 handR = new(0.42f, 0.78f - s * 0.05f, 0.2f - s * 0.25f * speed);
        Vector3 handL = new(-0.42f, 0.8f + s * 0.05f, s * 0.25f * speed);
        Vector3 swordDir = Vector3.Normalize(new Vector3(0.15f, -0.25f, 1f));
        float roll = 0, dropY = 0;
        bool flask = false;

        float p = k.StateTime;
        switch (k.State)
        {
            case KnightState.Light:
            {
                float side = k.Combo % 2 == 0 ? 1 : -1;
                if (p < Sim.Knight.LightWindup)
                {
                    float a = Ease(p / Sim.Knight.LightWindup);
                    handR = Vector3.Lerp(handR, new Vector3(0.65f * side, 1.2f, -0.2f), a);
                    swordDir = Vector3.Normalize(Vector3.Lerp(swordDir, new Vector3(side, 0.35f, -0.4f), a));
                    squashY = 1 - 0.1f * a; lean = -0.12f * a;
                }
                else
                {
                    float a = Math.Clamp((p - Sim.Knight.LightWindup) / (Sim.Knight.LightActive + 0.06f), 0, 1);
                    (handR, swordDir) = LightBlade(k, p);
                    squashY = 1.06f - 0.06f * a; lean = 0.25f;
                    stretchZ = 1 + 0.25f * (1 - a);
                }
                break;
            }
            case KnightState.Heavy:
                if (p < Sim.Knight.HeavyWindup)
                {
                    // Anticipación exagerada: se encoge y levanta la espada muy por detrás.
                    float a = Ease(p / Sim.Knight.HeavyWindup);
                    handR = Vector3.Lerp(handR, new Vector3(0.15f, 1.9f, -0.35f), a);
                    handL = Vector3.Lerp(handL, new Vector3(-0.05f, 1.85f, -0.3f), a);
                    swordDir = Vector3.Normalize(Vector3.Lerp(swordDir, new Vector3(0, 0.6f, -1f), a));
                    squashY = 1 - 0.24f * a; lean = -0.22f * a;
                    helmetSquash = 1 - 0.12f * a;
                    // Tiembla justo antes de soltarlo.
                    if (a > 0.8f) roll = MathF.Sin(t * 70) * 0.04f;
                }
                else
                {
                    float a = Math.Clamp((p - Sim.Knight.HeavyWindup) / 0.16f, 0, 1);
                    (handR, swordDir) = HeavyBlade(p);
                    handL = handR + new Vector3(-0.18f, 0.02f, -0.05f);
                    squashY = a < 1 ? 1.25f : 0.8f + 0.2f * Math.Clamp((p - Sim.Knight.HeavyWindup - 0.16f) / 0.3f, 0, 1);
                    lean = 0.4f; stretchZ = a < 1 ? 1.3f : 1f;
                }
                break;
            case KnightState.Dodge:
            {
                float a = p / Sim.Knight.DodgeTime;
                // Estirado durante la primera mitad (fotogramas de smear), aplastado al aterrizar.
                stretchZ = a < 0.45f ? 1.7f : 1f;
                squashY = a < 0.45f ? 0.62f : 0.82f + 0.18f * Ease((a - 0.45f) / 0.55f);
                lean = a < 0.45f ? 0.55f : 0.2f;
                footL = new Vector3(-0.15f, 0.25f, -0.3f); footR = new Vector3(0.15f, 0.2f, 0.25f);
                handR = new Vector3(0.35f, 0.7f, -0.35f); handL = new Vector3(-0.35f, 0.8f, 0.2f);
                swordDir = Vector3.Normalize(new Vector3(0.2f, 0.1f, -1));
                break;
            }
            case KnightState.Parry:
            {
                float a = MathF.Min(1, p / 0.08f);
                handL = Vector3.Lerp(handL, new Vector3(-0.1f, 1.25f, 0.6f), a);
                handR = Vector3.Lerp(handR, new Vector3(0.25f, 1.05f, 0.45f), a);
                swordDir = Vector3.Normalize(Vector3.Lerp(swordDir, new Vector3(-0.3f, 1f, 0.2f), a));
                squashY = 1 - 0.08f * a; lean = -0.05f;
                break;
            }
            case KnightState.Heal:
                handL = new Vector3(-0.12f, 1.35f, 0.35f);
                flask = true;
                helmetTilt = -0.25f;
                squashY = 1 + MathF.Sin(p * 12) * 0.03f;
                break;
            case KnightState.Hurt:
                lean = -0.35f; squashY = 0.85f; helmetSquash = 0.78f; helmetTilt = -0.2f;
                handR = new Vector3(0.6f, 1.3f, -0.1f); handL = new Vector3(-0.6f, 1.35f, 0.0f);
                break;
            case KnightState.Rest:
                squashY = 0.68f; lean = 0.25f; helmetTilt = 0.35f;
                handR = new Vector3(0.3f, 0.5f, 0.35f); handL = new Vector3(-0.3f, 0.5f, 0.35f);
                swordDir = Vector3.Normalize(new Vector3(0, -1, 0.35f));
                footL = new Vector3(-0.25f, 0.12f, 0.4f); footR = new Vector3(0.25f, 0.12f, 0.4f);
                break;
            case KnightState.Dead:
                dropY = -0.1f;
                break;
        }

        // Tras un desvío, ambos personajes se deforman un instante.
        if (k.Riposte > 1.4f) { squashY *= 0.8f; stretchZ *= 1.2f; helmetSquash *= 0.85f; }

        Vector3 feet = k.Feet + new Vector3(0, bob + dropY, 0);
        float deadRoll = k.State == KnightState.Dead ? MathF.Min(1, p * 2.5f) * -1.45f : 0;
        var squash = new Vector3(1 / MathF.Sqrt(squashY), squashY, stretchZ / MathF.Sqrt(squashY));
        Root(feet, k.Yaw, squash, lean + deadRoll, roll);

        Color steel = k.HurtFlash > 0 ? Palette.Parchment : Palette.Steel;
        Color darkSteel = Palette.Mix(steel, Palette.Charcoal, 0.35f);

        // Capa: una tira de lienzo que va con retraso respecto al cuerpo. Cada tramo conserva
        // el eje X local como ancho, así la tela no se retuerce entre tramos.
        Vector3 lag = Vector3.Transform(-k.CapeLag * 0.08f, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -k.Yaw));
        Vector3 prev = new(0, 1.2f, -0.2f);
        for (int i = 0; i < 4; i++)
        {
            float wave = MathF.Sin(t * 4 - i * 1.1f) * 0.035f * (i + 1);
            Vector3 next = prev + new Vector3(wave * 0.4f + lag.X * 0.25f, -0.26f + MathF.Max(lag.Y, 0), -0.04f - 0.035f * i + MathF.Min(lag.Z, 0) * 0.3f - MathF.Abs(wave) * 0.5f);
            Vector3 up = Vector3.Normalize(prev - next);
            Vector3 x = Vector3.Normalize(Vector3.UnitX - Vector3.Dot(Vector3.UnitX, up) * up);
            Vector3 z = Vector3.Cross(x, up);
            var basis = new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
            Quaternion q = Quaternion.CreateFromRotationMatrix(basis);
            P(Shape3.Cube, (prev + next) / 2, new Vector3(0.56f + i * 0.07f, Vector3.Distance(prev, next) + 0.04f, 0.045f), Palette.Burgundy, q, 0.022f);
            prev = next;
        }

        // Piernas de goma y botas grandotas.
        Vector3 hipL = new(-0.13f, 0.62f, 0), hipR = new(0.13f, 0.62f, 0);
        ink.Hose(W(hipL), W((hipL + footL) / 2 + new Vector3(-0.06f, 0, 0.1f)), W(footL + new Vector3(0, 0.1f, 0)), 0.075f, darkSteel, 4);
        ink.Hose(W(hipR), W((hipR + footR) / 2 + new Vector3(0.06f, 0, 0.1f)), W(footR + new Vector3(0, 0.1f, 0)), 0.075f, darkSteel, 4);
        P(Shape3.Sphere, footL + new Vector3(0, -0.02f, 0.06f), new Vector3(0.3f, 0.24f, 0.46f), Palette.DarkUmber);
        P(Shape3.Sphere, footR + new Vector3(0, -0.02f, 0.06f), new Vector3(0.3f, 0.24f, 0.46f), Palette.DarkUmber);

        // Torso estrecho, cinturón y hombreras redondas.
        P(Shape3.Sphere, new Vector3(0, 0.9f, 0), new Vector3(0.46f, 0.72f, 0.38f), steel);
        P(Shape3.Cylinder, new Vector3(0, 0.64f, 0), new Vector3(0.44f, 0.1f, 0.36f), Palette.DarkUmber, outline: 0.02f);
        Vector3 shL = new(-0.3f, 1.14f, 0), shR = new(0.3f, 1.14f, 0);
        P(Shape3.Sphere, shL, new Vector3(0.34f, 0.28f, 0.34f), steel);
        P(Shape3.Sphere, shR, new Vector3(0.34f, 0.28f, 0.34f), steel);

        // Brazos de manguera de goma con guanteletes enormes.
        ink.Hose(W(shL), W((shL + handL) / 2 + new Vector3(-0.18f, -0.05f, 0)), W(handL), 0.065f, darkSteel, 5);
        ink.Hose(W(shR), W((shR + handR) / 2 + new Vector3(0.18f, -0.05f, 0)), W(handR), 0.065f, darkSteel, 5);
        P(Shape3.Sphere, handL, new Vector3(0.27f), steel);
        P(Shape3.Sphere, handR, new Vector3(0.27f), steel);
        if (flask)
            P(Shape3.Sphere, handL + new Vector3(0, 0.16f, 0.05f), new Vector3(0.16f, 0.2f, 0.16f), Palette.Ember, emissive: 0.9f, outline: 0.02f);

        // Espada, y su smear cuando el tajo va demasiado rápido para verse.
        Quaternion sq = Ink.FromTo(Vector3.UnitY, swordDir);
        P(Shape3.Cube, handR + swordDir * 0.62f, new Vector3(0.075f, 1.02f, 0.025f), Palette.Parchment, sq, 0.022f);
        P(Shape3.Cube, handR + swordDir * 0.1f, new Vector3(0.3f, 0.05f, 0.06f), Palette.OldGold, sq, 0.02f);
        Color slash = k.Riposte > 0 ? Palette.Mix(Palette.Parchment, Palette.GhostCyan, 0.35f) : Palette.Parchment;
        if (k.State == KnightState.Light)
            Smear(q => LightBlade(k, q), p, Sim.Knight.LightWindup, Sim.Knight.LightWindup + Sim.Knight.LightActive + 0.06f, 0.14f, 1.16f, slash, k.Combo + 1);
        if (k.State == KnightState.Heavy)
            Smear(HeavyBlade, p, Sim.Knight.HeavyWindup, Sim.Knight.HeavyWindup + 0.16f, 0.14f, 1.2f, slash, 7);

        // Yelmo expresivo: se inclina, se aplasta y tiene una ranura por mirada.
        Quaternion hq = Quaternion.CreateFromAxisAngle(Vector3.UnitX, helmetTilt);
        Vector3 hs = new Vector3(0.6f, 0.64f * helmetSquash, 0.6f) * (helmetSquash < 1 ? 1.08f : 1f);
        P(Shape3.Sphere, new Vector3(0, 1.48f, 0.02f), hs, steel, hq);
        P(Shape3.Cube, new Vector3(0, 1.48f, 0.3f), new Vector3(0.36f, 0.06f * helmetSquash, 0.05f), Palette.Ink, hq, outline: 0);
        P(Shape3.Cube, new Vector3(0, 1.36f, 0.29f), new Vector3(0.04f, 0.16f, 0.05f), Palette.Ink, hq, outline: 0);
        P(Shape3.Cone, new Vector3(0, 1.86f, -0.05f), new Vector3(0.14f, 0.34f, 0.22f), Palette.Burgundy, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.5f + MathF.Sin(t * 6) * 0.1f), 0.02f);

        // Múltiplos al rodar y en la embestida del ataque fuerte.
        bool dash = k.State == KnightState.Dodge && p < Sim.Knight.DodgeTime * 0.45f
                 || k.State == KnightState.Heavy && p > Sim.Knight.HeavyWindup && p < Sim.Knight.HeavyWindup + 0.2f;
        Vector3 v = k.Body.LinearVelocity with { Y = 0 };
        if (dash && v.LengthSquared() > 4)
            Multiples(feet, v, k.Yaw, 3, 0.03f, a =>
            {
                P(Shape3.Sphere, new Vector3(0, 0.9f, 0), new Vector3(0.5f, 0.8f, 0.42f), Ghost(Palette.Steel, a * 0.7f), outline: 0, emissive: 1);
                P(Shape3.Sphere, new Vector3(0, 1.48f, 0), new Vector3(0.6f, 0.62f, 0.6f), Ghost(Palette.Steel, a * 0.7f), outline: 0, emissive: 1);
                P(Shape3.Cube, new Vector3(0, 0.85f, -0.3f), new Vector3(0.6f, 0.8f, 0.05f), Ghost(Palette.Burgundy, a * 0.5f), outline: 0, emissive: 1);
                P(Shape3.Cube, new Vector3(0, 1.48f, 0.3f), new Vector3(0.36f, 0.06f, 0.05f), Palette.Alpha(Palette.Ink, a * 0.6f), outline: 0, emissive: 1);
            });
    }

    Vector3 ShadowDrift(int seed)
    {
        // Las sombras a veces van a su aire.
        float t = _time * 0.6f + seed * 11;
        float wander = MathF.Max(0, MathF.Sin(t * 0.37f)) * 0.35f;
        return new Vector3(MathF.Sin(t) * wander, 0, MathF.Cos(t * 1.3f) * wander);
    }

    // ================================================================== esqueleto

    public void DrawSkeleton(Skeleton s, int index)
    {
        // Con modelo, el esqueleto muerto se desploma con su propia animación en vez de romperse en huesos.
        if (UseModels && _skeleton != null) { DrawSkeletonModel(s, index, _skeleton); return; }
        if (s.Dead) return;
        ink.BlobShadow(s.Feet + ShadowDrift(index + 5), 0.5f, 0.45f);

        float t = _time;
        float sp = Math.Clamp(new Vector2(s.Body.LinearVelocity.X, s.Body.LinearVelocity.Z).Length() / 2.7f, 0, 1);
        float w = MathF.Sin(s.WalkPhase * MathF.PI);
        // Andar rítmico, de marcha de dibujo animado.
        float bob = MathF.Abs(w) * 0.2f * sp + MathF.Sin(t * 3 + index) * 0.03f;
        float squashY = 1 + MathF.Abs(w) * 0.08f * sp;
        float lean = 0.08f, roll = w * 0.12f * sp;
        Vector3 handR = new(0.4f, 1.0f, 0.25f), handL = new(-0.4f, 0.95f - w * 0.1f, w * 0.25f);
        Vector3 swordDir = Vector3.Normalize(new Vector3(0.2f, 0.6f, 1f));
        float jaw = sp > 0.2f ? MathF.Abs(MathF.Sin(t * 18)) * 0.08f : 0;
        float skullTilt = 0;

        switch (s.State)
        {
            case FoeState.Windup:
            {
                float a = Ease(Math.Clamp(s.StateTime / 0.7f, 0, 1));
                handR = Vector3.Lerp(handR, new Vector3(0.3f, 2.1f, -0.4f), a);
                swordDir = Vector3.Normalize(Vector3.Lerp(swordDir, new Vector3(0, 0.3f, -1), a));
                squashY = 1 - 0.18f * a; lean = -0.3f * a;
                jaw = 0.14f * a;
                break;
            }
            case FoeState.Recover when s.StateTime < 0.3f:
                (handR, swordDir) = SkeletonBlade(s.StateTime);
                squashY = 0.85f; lean = 0.45f;
                break;
            case FoeState.Flinch:
                lean = -0.35f; skullTilt = -0.4f; squashY = 1.1f;
                break;
            case FoeState.Stagger:
                lean = 0.5f; skullTilt = 0.6f; squashY = 0.8f; roll = MathF.Sin(t * 4) * 0.15f;
                handR = new Vector3(0.45f, 0.4f, 0.2f); handL = new Vector3(-0.45f, 0.45f, 0.1f);
                swordDir = Vector3.Normalize(new Vector3(0.3f, -1, 0.3f));
                jaw = 0.15f;
                break;
        }

        Root(s.Feet + new Vector3(0, bob, 0), s.Yaw, Squash(squashY), lean, roll);
        Color bone = s.HurtFlash > 0 ? Palette.Crimson : Palette.Bone;
        Color rust = Palette.Mix(Palette.Umber, Palette.Burgundy, 0.3f);

        float f = w * 0.35f * sp;
        ink.Hose(W(new(-0.12f, 0.8f, 0)), W(new(-0.18f, 0.45f, f * 0.5f + 0.1f)), W(new(-0.15f, 0.08f, f)), 0.045f, bone, 4);
        ink.Hose(W(new(0.12f, 0.8f, 0)), W(new(0.18f, 0.45f, -f * 0.5f + 0.1f)), W(new(0.15f, 0.08f, -f)), 0.045f, bone, 4);
        P(Shape3.Sphere, new(-0.15f, 0.05f, f + 0.05f), new Vector3(0.16f, 0.1f, 0.28f), bone);
        P(Shape3.Sphere, new(0.15f, 0.05f, -f + 0.05f), new Vector3(0.16f, 0.1f, 0.28f), bone);
        P(Shape3.Sphere, new(0, 0.82f, 0), new Vector3(0.36f, 0.16f, 0.24f), bone); // pelvis
        ink.Segment(W(new(0, 0.85f, 0)), W(new(0, 1.45f, -0.03f)), 0.05f, bone);
        for (int i = 0; i < 3; i++)
            P(Shape3.Cylinder, new(0, 1.18f + i * 0.13f, 0), new Vector3(0.5f - i * 0.05f, 0.05f, 0.32f), bone, outline: 0.02f);
        Vector3 shL = new(-0.28f, 1.52f, 0), shR = new(0.28f, 1.52f, 0);
        ink.Segment(W(shL), W(shR), 0.045f, bone);
        ink.Hose(W(shL), W((shL + handL) / 2 + new Vector3(-0.15f, 0, 0)), W(handL), 0.04f, bone, 4);
        ink.Hose(W(shR), W((shR + handR) / 2 + new Vector3(0.15f, 0, 0)), W(handR), 0.04f, bone, 4);
        P(Shape3.Sphere, handL, new Vector3(0.14f), bone);
        P(Shape3.Sphere, handR, new Vector3(0.14f), bone);

        // Espada oxidada y escudo roto.
        Quaternion sq = Ink.FromTo(Vector3.UnitY, swordDir);
        P(Shape3.Cube, handR + swordDir * 0.55f, new Vector3(0.08f, 0.95f, 0.03f), rust, sq, 0.02f);
        if (s.State == FoeState.Recover) Smear(SkeletonBlade, s.StateTime, 0, BoneChop, 0.1f, 1.05f, Palette.Bone, 20 + index);
        P(Shape3.Cylinder, handL + new Vector3(-0.08f, 0, 0.12f), new Vector3(0.55f, 0.06f, 0.55f), Palette.Mix(Palette.DarkUmber, Palette.ForestGreen, 0.3f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.2f), 0.02f);

        // Calavera con mandíbula que castañetea y cuencas vacías.
        Quaternion hq = Quaternion.CreateFromAxisAngle(Vector3.UnitX, skullTilt);
        P(Shape3.Sphere, new(0, 1.78f, 0.02f), new Vector3(0.4f, 0.4f, 0.42f), bone, hq);
        P(Shape3.Cube, new(0, 1.56f - jaw, 0.1f), new Vector3(0.26f, 0.1f, 0.24f), bone, hq, 0.02f);
        P(Shape3.Sphere, new(-0.09f, 1.8f, 0.19f), new Vector3(0.12f, 0.14f, 0.06f), Palette.Ink, hq, 0);
        P(Shape3.Sphere, new(0.09f, 1.8f, 0.19f), new Vector3(0.12f, 0.14f, 0.06f), Palette.Ink, hq, 0);
        // Un yelmo abollado que le baila en la cabeza.
        P(Shape3.Sphere, new(0.02f, 1.95f + MathF.Abs(w) * 0.04f, -0.02f), new Vector3(0.44f, 0.24f, 0.46f), Palette.Mix(Palette.Steel, Palette.Umber, 0.4f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.25f + w * 0.1f));
    }

    // ================================================================== esqueleto con modelo

    /// <summary>Último clip de cada esqueleto: al cambiar de estado se funde desde él.</summary>
    sealed class ClipMemory
    {
        public string Clip = "", FromClip = "";
        public float Time, FromTime, ChangedAt = -99, DeadAt = -1;
        public bool Loop, FromLoop;
    }

    const float SkeletonScale = 0.88f;       // el modelo mide 2,6 m con yelmo; el esqueleto del juego, 1,85 m de cápsula

    // El clip de tajo, medido: la hoja sube hasta 0,45 s, baja entre 0,5 y 0,633 s y luego vuelve.
    const string Chop = "1H_Melee_Attack_Chop";
    const float ChopTop = 0.45f, ChopFall = 0.5f, ChopImpact = 0.633f;

    /// <summary>
    /// Instante del clip de tajo a lo largo de todo el ataque (carga + recuperación, 0 = empieza la carga).
    /// La hoja sube, aguanta arriba y baja acelerando en la última décima de la carga, de modo que toca
    /// justo cuando la simulación aplica el daño; después vuelve despacio a la guardia.
    /// </summary>
    static float ChopClip(float t)
    {
        const float windup = 0.7f, swing = 0.1f, hold = windup - swing;
        if (t < 0.5f) return t / 0.5f * ChopTop;
        if (t < hold) return ChopTop + (ChopFall - ChopTop) * (t - 0.5f) / (hold - 0.5f);
        if (t < windup) { float u = (t - hold) / swing; return ChopFall + (ChopImpact - ChopFall) * u * u; }
        return ChopImpact + (t - windup) * 0.6f;
    }

    /// <summary>Tiempo del ataque del esqueleto: la carga y la recuperación en una sola línea.</summary>
    static float AttackTime(Skeleton s) => s.State == FoeState.Windup ? s.StateTime : 0.7f + s.StateTime;

    static float Doses(float t) => MathF.Floor(t * 12f) / 12f;

    /// <summary>
    /// Qué clip enseña el esqueleto según su estado. El tiempo del clip sale del estado de la simulación,
    /// así el golpe del modelo cae justo cuando la simulación lo lanza, y se cuantiza a doses como el resto.
    /// </summary>
    (string Clip, float Time, bool Loop) SkeletonClip(Skeleton s, int index, Puppet p, ClipMemory m)
    {
        float st = Doses(s.StateTime);
        switch (s.State)
        {
            case FoeState.Dead: return ("Death_C_Skeletons", Doses(_time - m.DeadAt), false);
            case FoeState.Chase:
            {
                // El paso sigue a la distancia recorrida (sin patinar): un ciclo del clip son dos pasos.
                float len = p.Length("Walking_D_Skeletons");
                return ("Walking_D_Skeletons", Doses(s.WalkPhase / 2 * len % len), true);
            }
            // El descenso (la última décima de la carga) no se cuantiza: tiene que verse entero, con su smear.
            case FoeState.Windup: return (Chop, ChopClip(s.StateTime > 0.6f ? s.StateTime : st), false);
            case FoeState.Recover: return (Chop, ChopClip(0.7f + st), false);
            case FoeState.Flinch: return ("Hit_A", st * 1.8f, false);
            case FoeState.Stagger: return ("Hit_B", st * 0.55f, false);
            default: return ("Idle", Doses(_time + index * 0.37f), true);
        }
    }

    void DrawSkeletonModel(Skeleton s, int index, Puppet p)
    {
        ink.BlobShadow(s.Feet + ShadowDrift(index + 5), 0.5f, 0.45f);

        ClipMemory m = _clips.GetValue(s, _ => new ClipMemory());
        if (s.Dead && m.DeadAt < 0) m.DeadAt = _time;
        var (clip, time, loop) = SkeletonClip(s, index, p, m);
        if (m.Clip != clip || (m.Loop == false && time < m.Time - 0.05f))
        {
            // Cambio de estado: se funde desde la última pose durante dos dibujos.
            m.FromClip = m.Clip; m.FromTime = m.Time; m.FromLoop = m.Loop;
            m.ChangedAt = _time;
        }
        m.Clip = clip; m.Time = time; m.Loop = loop;
        float blend = Math.Clamp((_time - m.ChangedAt) / 0.17f, 0, 1);
        p.Pose(clip, time, loop, m.FromClip, m.FromTime, m.FromLoop, blend);

        // Squash & stretch de dibujo animado por encima del modelo: se encoge al cargar y se estira al soltar.
        float squashY = s.State switch
        {
            FoeState.Windup when s.StateTime > 0.6f => 1.06f,  // se estira al descargar
            FoeState.Windup => 1 - 0.1f * Ease(Math.Clamp(s.StateTime / 0.6f, 0, 1)),
            FoeState.Recover when s.StateTime < 0.12f => 1.08f,
            FoeState.Flinch => 1.06f,
            _ => 1,
        };
        Matrix4x4 world = Matrix4x4.CreateScale(SkeletonScale) * Matrix4x4.CreateScale(Squash(squashY))
                        * Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, s.Yaw) * Matrix4x4.CreateTranslation(s.Feet);
        Color tint = s.HurtFlash > 0 ? new Color(255, 120, 110, 255) : Color.White;
        // Material 2 del modelo: el brillo de las cuencas.
        ink.DrawModel(p.Model, world, tint, 0.03f, mat => mat == 2 ? 1f : 0f);

        int handR = p.Bone("handslot.r"), handL = p.Bone("handslot.l");
        if (_blade is { } blade && handR >= 0) ink.DrawModel(blade, p.BoneWorld(handR) * world, tint, 0.025f);
        if (_shield is { } shield && handL >= 0) ink.DrawModel(shield, p.BoneWorld(handL) * world, tint, 0.025f);

        // Smear del tajo: el mismo pincel seco que los personajes de primitivas, siguiendo la hoja real.
        if (s.State is FoeState.Windup or FoeState.Recover && handR >= 0)
        {
            _root = world;
            Smear(t =>
            {
                Matrix4x4 b = p.BoneAt(Chop, handR, ChopClip(t));
                return (b.Translation, Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, b)));
            }, AttackTime(s), 0.6f, 0.7f, 0.15f, 1.15f, Palette.Bone, 20 + index);
        }
    }

    public void Dispose()
    {
        _skeleton?.Dispose();
        if (_blade is { } b) Raylib.UnloadModel(b);
        if (_shield is { } sh) Raylib.UnloadModel(sh);
    }

    // ================================================================== el rey

    public void DrawKing(FallenKing k)
    {
        if (!k.Body.IsEnabled) return;
        float t = _time;
        float p = k.StateTime;
        ink.BlobShadow(k.Feet + ShadowDrift(3) * 2, 2.6f, 0.6f);

        float step = MathF.Sin(k.WalkPhase * MathF.PI);
        float squashY = 1 - MathF.Abs(MathF.Cos(k.WalkPhase * MathF.PI)) * 0.05f + MathF.Sin(t * 1.3f) * 0.015f;
        float lean = 0.05f, roll = step * 0.05f;
        Vector3 handR = new(1.9f, 2.2f, 0.5f), handL = new(-1.8f, 2.6f, 0.4f);
        // Por defecto arrastra la espada por detrás, con la punta en el suelo.
        Vector3 swordDir = Vector3.Normalize(new Vector3(0.35f, -0.22f, -1f));
        Vector3 look = new(0, 0, 1);
        float crown = k.Crown;
        float mouth = 0.05f;

        switch (k.State)
        {
            case KingState.Asleep:
                squashY = 0.8f + MathF.Sin(t * 0.8f) * 0.02f; lean = 0.25f;
                crown = 0.6f;
                handR = new(1.8f, 1.6f, 1.2f); handL = new(-1.8f, 1.6f, 1.2f);
                swordDir = Vector3.Normalize(new Vector3(0, -0.3f, 1));
                break;
            case KingState.Sweep:
                if (p < FallenKing.SweepWindup)
                {
                    float a = Ease(p / FallenKing.SweepWindup);
                    handR = Vector3.Lerp(handR, new Vector3(2.4f, 2.0f, -1.2f), a);
                    swordDir = Vector3.Normalize(Vector3.Lerp(swordDir, new Vector3(0.6f, -0.1f, -1f), a));
                    squashY = 1 - 0.1f * a; lean = -0.1f * a; roll = 0.15f * a;
                    mouth = 0.18f * a;
                }
                else
                {
                    float a = Math.Clamp((p - FallenKing.SweepWindup) / FallenKing.SweepActive, 0, 1);
                    (handR, swordDir) = SweepBlade(p);
                    roll = -0.2f * a; lean = 0.15f; mouth = 0.25f;
                }
                break;
            case KingState.Slam:
                if (p < FallenKing.SlamWindup)
                {
                    float a = Ease(p / FallenKing.SlamWindup);
                    handR = Vector3.Lerp(handR, new Vector3(0.4f, 7.2f, -0.6f), a);
                    handL = Vector3.Lerp(handL, new Vector3(-0.4f, 7.0f, -0.5f), a);
                    swordDir = Vector3.Normalize(Vector3.Lerp(swordDir, new Vector3(0, 0.5f, -1), a));
                    squashY = 1 + 0.12f * a; lean = -0.25f * a; mouth = 0.3f * a;
                }
                else
                {
                    float a = Math.Clamp((p - FallenKing.SlamWindup) / 0.1f, 0, 1);
                    (handR, swordDir) = SlamBlade(p);
                    handL = handR + new Vector3(-0.6f, 0.1f, -0.1f);
                    squashY = a < 1 ? 1.1f : 0.8f + 0.2f * Math.Clamp((p - FallenKing.SlamWindup - 0.1f) / 0.6f, 0, 1);
                    lean = 0.45f;
                }
                break;
            case KingState.Leap:
                if (p < FallenKing.LeapCrouch) { squashY = 1 - 0.3f * Ease(p / FallenKing.LeapCrouch); lean = 0.2f; }
                else if (k.RingStart < 0) { squashY = 1.25f; handR = new(2.4f, 5.5f, 0); handL = new(-2.4f, 5.5f, 0); swordDir = Vector3.Normalize(new Vector3(0.3f, 1, -0.4f)); }
                else squashY = 0.72f + 0.28f * Math.Clamp((p - k.RingStart) / 0.5f, 0, 1);
                break;
            case KingState.Blind:
                // Palpa el aire con los brazos por delante.
                handR = new(1.0f, 3.4f + MathF.Sin(t * 5) * 0.2f, 2.4f); handL = new(-1.0f, 3.4f + MathF.Cos(t * 5) * 0.2f, 2.4f);
                roll = MathF.Sin(t * 2.5f) * 0.1f;
                mouth = 0.12f;
                break;
            case KingState.Stagger:
                squashY = 0.72f; lean = 0.35f; roll = 0.1f;
                handR = new(2.0f, 0.8f, 1.2f); handL = new(-1.6f, 0.9f, 1.4f);
                swordDir = Vector3.Normalize(new Vector3(0.4f, -1, 0.5f));
                crown = 0.5f;
                mouth = 0.2f;
                break;
            case KingState.Enrage:
                squashY = 1.1f + MathF.Sin(t * 30) * 0.04f; lean = -0.2f;
                handR = new(2.2f, 5.6f, 0.2f); handL = new(-2.2f, 5.6f, 0.2f);
                swordDir = Vector3.Normalize(new Vector3(0.2f, 1, 0));
                mouth = 0.4f;
                break;
            case KingState.Dead:
                lean = MathF.Min(1, p * 0.8f) * -1.35f; squashY = 0.95f; mouth = 0.3f;
                handR = new(2.6f, 3f, 0); handL = new(-2.6f, 3f, 0);
                crown = 1f;
                break;
        }

        Root(k.Feet, k.Yaw, Squash(squashY), lean, roll);
        Color robe = k.HurtFlash > 0 ? Palette.Parchment : (k.Phase2 ? Palette.Mix(Palette.Burgundy, Palette.Crimson, 0.35f) : Palette.Burgundy);
        Color skin = Palette.Mix(Palette.DirtyCream, Palette.Stone, 0.2f);
        Color steel = Palette.Mix(Palette.Steel, Palette.Umber, 0.2f);

        // Túnica enorme, cuello de armiño con motas y barriga real.
        P(Shape3.Cone, new(0, 1.9f, 0), new Vector3(5.4f, 3.9f, 4.8f), robe, outline: 0.05f);
        P(Shape3.Sphere, new(0, 3.2f, 0.1f), new Vector3(3.0f, 2.6f, 2.7f), Palette.Mix(robe, Palette.Charcoal, 0.2f), outline: 0.05f);
        P(Shape3.Sphere, new(0, 3.95f, 0), new Vector3(3.4f, 0.9f, 3.1f), Palette.Parchment, outline: 0.05f);
        for (int i = 0; i < 9; i++)
        {
            float a = i / 9f * MathF.Tau;
            P(Shape3.Sphere, new(MathF.Sin(a) * 1.55f, 4.0f, MathF.Cos(a) * 1.4f), new Vector3(0.16f, 0.24f, 0.16f), Palette.Ink, outline: 0);
        }
        // Pies asomando.
        P(Shape3.Sphere, new(-0.8f, 0.2f, 1.6f + step * 0.3f), new Vector3(1.0f, 0.5f, 1.3f), Palette.DarkUmber, outline: 0.04f);
        P(Shape3.Sphere, new(0.8f, 0.2f, 1.6f - step * 0.3f), new Vector3(1.0f, 0.5f, 1.3f), Palette.DarkUmber, outline: 0.04f);

        // Brazos de goma gruesos.
        Vector3 shL = new(-1.5f, 4.0f, 0), shR = new(1.5f, 4.0f, 0);
        ink.Hose(W(shL), W((shL + handL) / 2 + new Vector3(-0.8f, 0.2f, 0)), W(handL), 0.35f, robe, 6);
        ink.Hose(W(shR), W((shR + handR) / 2 + new Vector3(0.8f, 0.2f, 0)), W(handR), 0.35f, robe, 6);
        P(Shape3.Sphere, handL, new Vector3(0.8f), steel, outline: 0.04f);
        P(Shape3.Sphere, handR, new Vector3(0.8f), steel, outline: 0.04f);

        // La espada imposible.
        Quaternion sq = Ink.FromTo(Vector3.UnitY, swordDir);
        P(Shape3.Cube, handR + swordDir * 3.4f, new Vector3(0.42f, 6.2f, 0.12f), Palette.Mix(Palette.Steel, Palette.DirtyCream, 0.3f), sq, 0.045f);
        P(Shape3.Cube, handR + swordDir * 0.25f, new Vector3(1.5f, 0.22f, 0.3f), Palette.OldGold, sq, 0.04f);
        Color blade = Palette.Mix(Palette.Steel, Palette.Parchment, 0.5f);
        if (k.State == KingState.Sweep) Smear(SweepBlade, p, FallenKing.SweepWindup, FallenKing.SweepWindup + FallenKing.SweepActive, 0.5f, 6.5f, blade, 30);
        if (k.State == KingState.Slam) Smear(SlamBlade, p, FallenKing.SlamWindup, FallenKing.SlamWindup + 0.1f, 0.5f, 6.5f, blade, 31);

        // Cabeza, barba, nariz y ojos saltones que siguen al caballero.
        P(Shape3.Cone, new(0, 4.1f, 0.55f), new Vector3(1.5f, 1.9f, 0.9f), Palette.DirtyCream, Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI), 0.04f);
        P(Shape3.Sphere, new(0, 5.0f, 0.1f), new Vector3(1.55f, 1.65f, 1.5f), skin, outline: 0.045f);
        P(Shape3.Sphere, new(0, 4.85f, 0.85f), new Vector3(0.5f, 0.42f, 0.5f), Palette.Mix(skin, Palette.Burgundy, 0.35f), outline: 0.03f);
        P(Shape3.Cube, new(0, 4.52f - mouth, 0.72f), new Vector3(0.5f, 0.08f + mouth, 0.1f), Palette.Ink, outline: 0);
        if (crown < 0.7f)
        {
            float pupil = k.State == KingState.Stagger ? MathF.Sin(t * 8) * 0.08f : 0;
            foreach (float x in new[] { -0.33f, 0.33f })
            {
                P(Shape3.Sphere, new(x, 5.22f, 0.66f), new Vector3(0.46f, 0.52f, 0.3f), Palette.Parchment, outline: 0.03f);
                P(Shape3.Sphere, new(x + pupil, 5.18f, 0.8f), new Vector3(0.16f, 0.2f, 0.08f), Palette.Ink, outline: 0);
            }
        }

        // La corona que se escurre sobre los ojos.
        float cy = 5.95f - crown * 0.82f;
        Quaternion cq = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.18f + crown * 0.25f) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, crown * 0.2f);
        P(Shape3.Cylinder, new(0.1f, cy, 0.05f), new Vector3(1.45f, 0.5f, 1.4f), Palette.OldGold, cq, 0.035f);
        for (int i = 0; i < 5; i++)
        {
            float a = i / 5f * MathF.Tau;
            Vector3 local = Vector3.Transform(new Vector3(MathF.Sin(a) * 0.62f, 0.45f, MathF.Cos(a) * 0.6f), cq);
            P(Shape3.Cone, new Vector3(0.1f, cy, 0.05f) + local, new Vector3(0.26f, 0.5f, 0.26f), Palette.OldGold, cq, 0.025f);
        }

        // Segunda fase: gotea pintura carmesí.
        if (k.Phase2)
        {
            for (int i = 0; i < 7; i++)
            {
                float phase = (t * 0.9f + i * 0.37f) % 1f;
                float a = i * 0.9f;
                Vector3 start = new(MathF.Sin(a) * 1.8f, 3.2f, MathF.Cos(a) * 1.5f);
                P(Shape3.Sphere, start - new Vector3(0, phase * 3.1f, 0), new Vector3(0.18f, 0.3f + phase * 0.2f, 0.18f), Palette.Crimson, outline: 0.015f, emissive: 0.4f);
            }
        }

        // En pleno salto deja múltiplos: el rey entero, varias veces, a lo largo del vuelo.
        if (k.State == KingState.Leap && k.Struck && k.RingStart < 0 && k.Body.LinearVelocity.LengthSquared() > 9)
            Multiples(k.Feet, k.Body.LinearVelocity, k.Yaw, 3, 0.045f, a =>
            {
                P(Shape3.Cone, new(0, 1.9f, 0), new Vector3(5.4f, 3.9f, 4.8f), Ghost(robe, a * 0.45f), outline: 0, emissive: 1);
                P(Shape3.Sphere, new(0, 3.4f, 0.1f), new Vector3(3.2f, 2.4f, 2.9f), Ghost(robe, a * 0.45f), outline: 0, emissive: 1);
                P(Shape3.Sphere, new(0, 5.0f, 0.1f), new Vector3(1.55f, 1.65f, 1.5f), Ghost(skin, a * 0.45f), outline: 0, emissive: 1);
                P(Shape3.Cylinder, new(0.1f, 5.9f, 0.05f), new Vector3(1.45f, 0.5f, 1.4f), Ghost(Palette.OldGold, a * 0.5f), outline: 0, emissive: 1);
            });
    }
}
