using System.Numerics;
using Box3D;
using CaballeroDeTinta.Art;
using Raylib_cs;

namespace CaballeroDeTinta.Sim;

enum Phase { Explore, Intro, Boss, Dead, Victory }

/// <summary>
/// El reino moribundo: capilla con hoguera, patio en ruinas con esqueletos, puerta de niebla
/// y la sala del trono de Baldomero III. Toda la física es Box3D.NET.
/// </summary>
sealed class Kingdom : IDisposable
{
    public const float FixedStep = 1f / 60f;
    const ulong FoeBase = 3UL << 40, PropBase = 4UL << 40;

    public PhysicsWorld World = null!;
    public Knight Player = null!;
    public FallenKing King = null!;
    public readonly List<Skeleton> Skeletons = [];
    public readonly List<Prop> Props = [];
    public readonly List<Piece> Statics = [];   // con cuerpo físico
    public readonly List<Piece> Decor = [];     // solo se dibuja (horizonte, detalles)
    public readonly List<Fx> Effects = [];
    public readonly List<Heard> Sounds = [];        // se vacía al empezar cada Update
    public readonly List<Vector4> Lights = [];
    public readonly List<(Vector3 Top, Body[] Links)> Chains = [];

    public Phase Phase;
    public float PhaseTime;
    public float Hitstop, Shake, ImpactFlash, ImpactRough;
    public float CamYaw = MathF.PI, CamPitch = 0.28f;
    public Actor? LockTarget;
    public Vector3 Bonfire = new(0, 0, 7);
    public bool BonfireLit;
    public Body Bell;
    public Vector3 BellPivot;
    public string Toast = "";
    public float ToastTime;
    public bool BossDefeated;
    public bool SealUp;
    /// <summary>
    /// Los esqueletos muertos se rompen en huesos con física. La vista lo apaga cuando dibuja el modelo 3D,
    /// que se desploma con su propia animación.
    /// </summary>
    public bool ShatterIntoBones = true;

    Shape _fogSensor;
    float _accumulator;
    readonly List<Shape> _found = [];
    readonly Random _rng = new(1931);

    public Kingdom() => Build();

    /// <summary>Partida nueva: el reino entero desde cero, hoguera apagada y jefe vivo.</summary>
    public void NewGame()
    {
        BonfireLit = BossDefeated = false;
        Build();
    }

    // ================================================================= construcción

    public void Build()
    {
        World?.Dispose();
        World = new PhysicsWorld(WorldSettings.Default with
        {
            Gravity = new Vector3(0, -18f, 0), // gravedad de dibujo animado: caídas rápidas y con peso
            WorkerCount = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
        });
        Skeletons.Clear(); Props.Clear(); Statics.Clear(); Decor.Clear(); Effects.Clear(); Lights.Clear(); Chains.Clear();
        Phase = Phase.Explore;
        PhaseTime = 0;
        Hitstop = Shake = ImpactFlash = ImpactRough = 0;
        LockTarget = null;
        CamYaw = MathF.PI;
        SealUp = false;
        _accumulator = 0;

        // Suelo del reino.
        Solid(new Vector3(0, -0.5f, -40), new Vector3(140, 1, 190), Palette.Mix(Palette.DarkStone, Palette.Umber, 0.3f));

        BuildChapel();
        BuildCourtyard();
        BuildGate();
        BuildThroneRoom();
        BuildHorizon();

        Player = new Knight(World, Bonfire + new Vector3(0, 0, -2.2f));
        King = new FallenKing(World, new Vector3(0, 0, -76));
    }

    Body Solid(Vector3 pos, Vector3 size, Color color, Quaternion? rot = null, Shape3 shape = Shape3.Cube)
    {
        Body b = World.CreateStaticBody(pos, rot);
        b.AddBox(new Box(size * 0.5f), ShapeDefinition.Default with { Friction = 0.8f, Filter = new CollisionFilter(Cat.Static, ulong.MaxValue) });
        Statics.Add(new Piece { Shape = shape, Position = pos, Rotation = rot ?? Quaternion.Identity, Size = size, Color = color });
        return b;
    }

    void Paint(Shape3 shape, Vector3 pos, Vector3 size, Color color, Quaternion? rot = null, float emissive = 0) =>
        Decor.Add(new Piece { Shape = shape, Position = pos, Rotation = rot ?? Quaternion.Identity, Size = size, Color = color, Emissive = emissive });

    Prop AddProp(PropKind kind, Body body, Vector3 size, Color color)
    {
        var p = new Prop { Body = body, Kind = kind, Size = size, Color = color };
        Props.Add(p);
        body.UserData = PropBase + (ulong)(Props.Count - 1);
        return p;
    }

    Prop Crate(Vector3 pos, float s)
    {
        Body b = World.CreateDynamicBody(pos, Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)_rng.NextDouble()));
        b.AddBox(new Box(new Vector3(s / 2)), ShapeDefinition.Default with { Density = 40f, Friction = 0.7f, Filter = new CollisionFilter(Cat.Prop, ulong.MaxValue) });
        return AddProp(PropKind.Crate, b, new Vector3(s), Palette.Mix(Palette.Umber, Palette.DirtyCream, 0.15f));
    }

    static readonly ConvexHull BarrelHull = ConvexHull.Cylinder(1.0f, 0.4f, -0.5f, 12);
    static readonly ConvexHull DrumHull = ConvexHull.Cylinder(1.1f, 0.85f, -0.55f, 14);

    Prop Barrel(Vector3 pos)
    {
        Body b = World.CreateDynamicBody(pos);
        b.AddHull(BarrelHull, ShapeDefinition.Default with { Density = 50f, Friction = 0.6f, Filter = new CollisionFilter(Cat.Prop, ulong.MaxValue) });
        return AddProp(PropKind.Barrel, b, new Vector3(0.8f, 1.0f, 0.8f), Palette.Mix(Palette.DarkUmber, Palette.Umber, 0.5f));
    }

    /// <summary>Columna de tambores apilados: se derrumba de verdad si algo enorme choca con ella.</summary>
    void Column(Vector3 basePos, int drums, Color color)
    {
        for (int i = 0; i < drums; i++)
        {
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, i * 0.7f);
            Body b = World.CreateDynamicBody(basePos + new Vector3(0, 0.55f + i * 1.105f, 0), q);
            b.AddHull(DrumHull, ShapeDefinition.Default with { Density = 260f, Friction = 0.9f, Filter = new CollisionFilter(Cat.Prop, ulong.MaxValue) });
            AddProp(PropKind.Drum, b, new Vector3(1.7f, 1.1f, 1.7f), Palette.Mix(color, Palette.Parchment, i % 2 * 0.08f));
        }
        // Capitel.
        Body cap = World.CreateDynamicBody(basePos + new Vector3(0, drums * 1.105f + 0.3f, 0));
        cap.AddBox(new Box(new Vector3(1.15f, 0.3f, 1.15f)), ShapeDefinition.Default with { Density = 240f, Friction = 0.9f, Filter = new CollisionFilter(Cat.Prop, ulong.MaxValue) });
        AddProp(PropKind.Crate, cap, new Vector3(2.3f, 0.6f, 2.3f), color);
    }

    static readonly ConvexHull RockHull = ConvexHull.Rock(0.45f);

    void Rubble(Vector3 center, float spread, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 p = center + new Vector3((float)(_rng.NextDouble() - 0.5) * spread, 0.5f, (float)(_rng.NextDouble() - 0.5) * spread);
            Body b = World.CreateDynamicBody(p, Quaternion.CreateFromYawPitchRoll((float)_rng.NextDouble() * 6, (float)_rng.NextDouble() * 6, 0));
            b.AddHull(RockHull, ShapeDefinition.Default with { Density = 120f, Friction = 0.8f, Filter = new CollisionFilter(Cat.Prop, ulong.MaxValue) });
            AddProp(PropKind.Rock, b, new Vector3(0.9f), Palette.Mix(Palette.Stone, Palette.DarkStone, (float)_rng.NextDouble()));
        }
    }

    void Torch(Vector3 wallPoint, Vector3 outward)
    {
        Vector3 p = wallPoint + outward * 0.35f;
        Paint(Shape3.Cylinder, p, new Vector3(0.14f, 0.8f, 0.14f), Palette.DarkUmber, Quaternion.CreateFromAxisAngle(Vector3.Cross(Vector3.UnitY, outward), 0.35f));
        Lights.Add(new Vector4(p + new Vector3(0, 0.7f, 0) + outward * 0.15f, 1.3f));
    }

    /// <summary>Cadena colgante: eslabones esféricos con SphericalJoint desde un punto fijo.</summary>
    void HangingChain(Vector3 top, int links, float spacing)
    {
        Body anchor = World.CreateStaticBody(top);
        Body prev = anchor;
        var bodies = new Body[links];
        for (int i = 0; i < links; i++)
        {
            Vector3 p = top - new Vector3(0, spacing * (i + 1), 0);
            Body link = World.CreateDynamicBody(p);
            link.AddSphere(new Sphere(0.12f), ShapeDefinition.Default with { Density = 400f, Filter = new CollisionFilter(Cat.Prop, ulong.MaxValue) });
            World.CreateSphericalJoint(SphericalJointDefinition.BallAndSocket(prev, link, p + new Vector3(0, spacing, 0)));
            AddProp(PropKind.Link, link, new Vector3(0.24f), Palette.Steel);
            bodies[i] = link;
            prev = link;
        }
        Chains.Add((top, bodies));
    }

    void BuildChapel()
    {
        var wall = Palette.Mix(Palette.Stone, Palette.DirtyCream, 0.2f);
        Solid(new Vector3(0, 4, 13.5f), new Vector3(14, 8, 1), wall);
        Solid(new Vector3(-7, 3, 7), new Vector3(1, 6, 14), wall);
        Solid(new Vector3(7, 2.2f, 9), new Vector3(1, 4.4f, 10), wall);   // pared derrumbada a medias
        // Rosetón vacío y arcos rotos pintados sobre el muro del fondo.
        Paint(Shape3.Cylinder, new Vector3(0, 5.5f, 12.95f), new Vector3(3.2f, 0.1f, 3.2f), Palette.Charcoal, Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2));
        Paint(Shape3.Cylinder, new Vector3(0, 5.5f, 12.9f), new Vector3(2.2f, 0.1f, 2.2f), Palette.Ember, Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2), emissive: 0.35f);
        // Altar y bancos volcados.
        Solid(new Vector3(0, 0.6f, 11.4f), new Vector3(3, 1.2f, 1.2f), Palette.DarkStone);
        Crate(new Vector3(-4.5f, 0.5f, 9), 0.9f);
        Barrel(new Vector3(4.8f, 0.5f, 10.5f));
        Barrel(new Vector3(5.4f, 0.5f, 9.3f));
        Lights.Add(new Vector4(Bonfire + new Vector3(0, 0.8f, 0), 3.2f));
        Torch(new Vector3(-6.5f, 2.6f, 4), Vector3.UnitX);
    }

    void BuildCourtyard()
    {
        var wall = Palette.Mix(Palette.Stone, Palette.DarkStone, 0.35f);
        // Murallas altas a ambos lados, con contrafuertes.
        Solid(new Vector3(-13, 5, -18), new Vector3(1.5f, 10, 42), wall);
        Solid(new Vector3(13, 5, -18), new Vector3(1.5f, 10, 42), wall);
        for (int i = 0; i < 5; i++)
        {
            float z = -2 - i * 9;
            Solid(new Vector3(-11.8f, 3, z), new Vector3(1.2f, 6, 1.6f), Palette.DarkStone);
            Solid(new Vector3(11.8f, 3, z), new Vector3(1.2f, 6, 1.6f), Palette.DarkStone);
            // Ojos tallados en la muralla que nadie explica.
            Paint(Shape3.Sphere, new Vector3(-12.2f, 7.2f, z - 4.5f), new Vector3(0.1f, 0.7f, 1.4f), Palette.DirtyCream);
            Paint(Shape3.Sphere, new Vector3(-12.15f, 7.2f, z - 4.5f), new Vector3(0.1f, 0.5f, 0.5f), Palette.Ink);
            Paint(Shape3.Sphere, new Vector3(12.2f, 7.2f, z - 4.5f), new Vector3(0.1f, 0.7f, 1.4f), Palette.DirtyCream);
            Paint(Shape3.Sphere, new Vector3(12.15f, 7.2f, z - 4.5f), new Vector3(0.1f, 0.5f, 0.5f), Palette.Ink);
        }
        Torch(new Vector3(-12.2f, 3f, -11), Vector3.UnitX);
        Torch(new Vector3(12.2f, 3f, -24), -Vector3.UnitX);

        // Columnas en pie y rotas.
        Column(new Vector3(-7, 0, -8), 5, Palette.Stone);
        Column(new Vector3(7, 0, -8), 3, Palette.Stone);
        Column(new Vector3(-7, 0, -20), 2, Palette.Stone);
        Column(new Vector3(7, 0, -20), 5, Palette.Stone);
        Column(new Vector3(-7, 0, -32), 4, Palette.Stone);
        Column(new Vector3(7, 0, -32), 5, Palette.Stone);

        // Estatua de un caballero sin cabeza, con la espada clavada.
        Solid(new Vector3(0, 0.8f, -17), new Vector3(3, 1.6f, 3), Palette.DarkStone);
        Solid(new Vector3(0, 3.1f, -17), new Vector3(1.4f, 3f, 0.9f), Palette.Stone);
        Paint(Shape3.Sphere, new Vector3(-0.95f, 4.3f, -17), new Vector3(0.9f, 0.7f, 0.9f), Palette.Stone);
        Paint(Shape3.Sphere, new Vector3(0.95f, 4.3f, -17), new Vector3(0.9f, 0.7f, 0.9f), Palette.Stone);
        Paint(Shape3.Cube, new Vector3(0, 2.8f, -16.2f), new Vector3(0.18f, 3.6f, 0.08f), Palette.Steel, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.12f));
        Paint(Shape3.Cube, new Vector3(0, 4.3f, -16.2f), new Vector3(1.1f, 0.14f, 0.12f), Palette.OldGold);

        foreach (var (x, z) in new[] { (-10f, -4f), (-10.5f, -5.2f), (10f, -14f), (9.8f, -27f), (-10f, -28f) })
            Crate(new Vector3(x, 0.5f, z), 0.9f + (float)_rng.NextDouble() * 0.3f);
        foreach (var (x, z) in new[] { (10.5f, -3f), (-10.2f, -15f), (10.2f, -30f), (10.6f, -31.2f) })
            Barrel(new Vector3(x, 0.5f, z));
        Rubble(new Vector3(3, 0, -12), 4, 5);
        Rubble(new Vector3(-3, 0, -27), 4, 5);
        HangingChain(new Vector3(-9, 9.2f, -12), 9, 0.45f);
        HangingChain(new Vector3(9, 9.2f, -22), 11, 0.45f);

        Skeletons.Add(new Skeleton(World, new Vector3(-3, 0, -13), FoeBase + 0, 0));
        Skeletons.Add(new Skeleton(World, new Vector3(4, 0, -23), FoeBase + 1, 0.4f));
        Skeletons.Add(new Skeleton(World, new Vector3(-1, 0, -33), FoeBase + 2, 0));
    }

    public Vector3 GateCenter = new(0, 0, -41);

    void BuildGate()
    {
        var tower = Palette.Mix(Palette.DarkStone, Palette.Charcoal, 0.25f);
        Solid(new Vector3(-7.5f, 8, -41), new Vector3(5, 16, 5), tower);
        Solid(new Vector3(7.5f, 8, -41), new Vector3(5, 16, 5), tower);
        Paint(Shape3.Cone, new Vector3(-7.5f, 19, -41), new Vector3(6.2f, 6, 6.2f), Palette.Burgundy);
        Paint(Shape3.Cone, new Vector3(7.5f, 19, -41), new Vector3(6.2f, 6, 6.2f), Palette.Burgundy);
        Solid(new Vector3(0, 9.5f, -41), new Vector3(10, 3, 4), tower);           // dintel
        Solid(new Vector3(-12, 5, -41), new Vector3(4, 10, 2), tower);
        Solid(new Vector3(12, 5, -41), new Vector3(4, 10, 2), tower);
        // Cadenas enormes cruzando la fachada.
        Paint(Shape3.Cube, new Vector3(-3.5f, 12.5f, -38.9f), new Vector3(0.4f, 0.4f, 7f), Palette.Steel, Quaternion.CreateFromYawPitchRoll(MathF.PI / 2, 0, 0.5f));
        Paint(Shape3.Cube, new Vector3(3.5f, 12.5f, -38.9f), new Vector3(0.4f, 0.4f, 7f), Palette.Steel, Quaternion.CreateFromYawPitchRoll(MathF.PI / 2, 0, -0.5f));

        // Muro de niebla: un sensor. Al cruzarlo empieza la presentación del jefe.
        Body fog = World.CreateStaticBody(GateCenter + new Vector3(0, 4, -0.5f));
        _fogSensor = fog.AddBox(new Box(new Vector3(5, 4, 0.4f)), ShapeDefinition.Default with { IsSensor = true, EnableSensorEvents = true });
    }

    void BuildThroneRoom()
    {
        var wall = Palette.Mix(Palette.DarkStone, Palette.Charcoal, 0.15f);
        Solid(new Vector3(-21, 7, -63), new Vector3(2, 14, 44), wall);
        Solid(new Vector3(21, 7, -63), new Vector3(2, 14, 44), wall);
        Solid(new Vector3(0, 9, -86), new Vector3(44, 18, 2), wall);
        Solid(new Vector3(-15.5f, 7, -42.5f), new Vector3(11, 14, 1.5f), wall);
        Solid(new Vector3(15.5f, 7, -42.5f), new Vector3(11, 14, 1.5f), wall);

        // Trono gigante en ruinas.
        Solid(new Vector3(0, 0.5f, -80), new Vector3(10, 1, 6), Palette.DarkStone);
        Solid(new Vector3(0, 5, -83), new Vector3(7, 9, 1.5f), Palette.Burgundy);
        Paint(Shape3.Cone, new Vector3(-2.5f, 10.4f, -83), new Vector3(1.2f, 2, 1.2f), Palette.OldGold);
        Paint(Shape3.Cone, new Vector3(0, 11f, -83), new Vector3(1.4f, 3, 1.4f), Palette.OldGold);
        Paint(Shape3.Cone, new Vector3(2.5f, 10.4f, -83), new Vector3(1.2f, 2, 1.2f), Palette.OldGold);

        foreach (var (x, z) in new[] { (-12f, -52f), (12f, -52f), (-14f, -64f), (14f, -64f), (-12f, -76f), (12f, -76f) })
            Column(new Vector3(x, 0, z), 5, Palette.Mix(Palette.Stone, Palette.DustyBlue, 0.15f));
        Rubble(new Vector3(-6, 0, -58), 6, 7);
        Rubble(new Vector3(7, 0, -68), 6, 7);
        foreach (var (x, z) in new[] { (-18f, -46f), (18f, -47f), (-18.5f, -80f), (18f, -81f) })
            Barrel(new Vector3(x, 0.5f, z));
        Torch(new Vector3(-20f, 3.5f, -56), Vector3.UnitX);
        Torch(new Vector3(20f, 3.5f, -70), -Vector3.UnitX);
        Lights.Add(new Vector4(0, 6, -81, 1.8f));

        // La gran campana: un hull cónico colgado de una bisagra (RevoluteJoint). Suena a golpes y con las explosiones.
        BellPivot = new Vector3(0, 16.5f, -66);
        Solid(new Vector3(0, 17.2f, -66), new Vector3(30, 1, 1.2f), Palette.DarkUmber);
        using (var bellHull = ConvexHull.Cone(3.2f, 2.3f, 0.9f, 16))
        {
            Bell = World.CreateDynamicBody(BellPivot - new Vector3(0, 4.6f, 0));
            Bell.AddHull(bellHull, ShapeDefinition.Default with { Density = 60f, Filter = new CollisionFilter(Cat.Prop, ulong.MaxValue) });
        }
        AddProp(PropKind.Bell, Bell, new Vector3(4.6f, 3.2f, 4.6f), Palette.OldGold);
        Body pivot = World.CreateStaticBody(BellPivot);
        World.CreateRevoluteJoint(RevoluteJointDefinition.Hinge(pivot, Bell, BellPivot, Vector3.UnitX) with
        {
            LimitsEnabled = true,
            LowerAngle = -1.1f,
            UpperAngle = 1.1f,
        });
        HangingChain(new Vector3(-16, 13, -60), 12, 0.5f);
        HangingChain(new Vector3(16, 13, -72), 12, 0.5f);
    }

    void BuildHorizon()
    {
        // Silueta de la catedral y torres lejanas: solo pintura, la niebla hace el resto.
        var far = Palette.Mix(Palette.DarkStone, Palette.Fog, 0.3f);
        Paint(Shape3.Cube, new Vector3(0, 22, -104), new Vector3(46, 44, 6), far);
        Paint(Shape3.Cone, new Vector3(0, 52, -104), new Vector3(20, 18, 8), Palette.Mix(Palette.Burgundy, Palette.Fog, 0.3f));
        Paint(Shape3.Cube, new Vector3(-26, 30, -100), new Vector3(8, 60, 8), far);
        Paint(Shape3.Cube, new Vector3(26, 30, -100), new Vector3(8, 60, 8), far);
        Paint(Shape3.Cone, new Vector3(-26, 66, -100), new Vector3(10, 14, 10), far);
        Paint(Shape3.Cone, new Vector3(26, 66, -100), new Vector3(10, 14, 10), far);
        // Torre del reloj torcida.
        Paint(Shape3.Cube, new Vector3(34, 26, -60), new Vector3(7, 52, 7), far, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.06f));
        Paint(Shape3.Cylinder, new Vector3(33.2f, 44, -56.4f), new Vector3(5, 0.3f, 5), Palette.DirtyCream, Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2));
        for (int i = 0; i < 18; i++)
        {
            float ang = i / 18f * MathF.Tau;
            float r = 75 + (i * 37 % 20);
            var p = new Vector3(MathF.Cos(ang) * r, 0, -40 + MathF.Sin(ang) * r);
            float h = 18 + (i * 53 % 30);
            Paint(Shape3.Cube, p + new Vector3(0, h / 2, 0), new Vector3(6, h, 6), Palette.Mix(far, Palette.Fog, 0.4f));
            Paint(Shape3.Cone, p + new Vector3(0, h + 4, 0), new Vector3(7, 8, 7), Palette.Mix(far, Palette.Fog, 0.4f));
            if (i % 3 == 0) Paint(Shape3.Cube, p + new Vector3(0, h * 0.7f, 3.05f), new Vector3(0.8f, 1.4f, 0.1f), Palette.Ember, emissive: 0.8f);
        }
        // Árboles muertos junto a la capilla.
        for (int i = 0; i < 8; i++)
        {
            var p = new Vector3(-16 - i * 3.5f, 0, 10 - i * 5 % 13);
            Paint(Shape3.Cone, p + new Vector3(0, 3.5f, 0), new Vector3(0.7f, 7, 0.7f), Palette.Charcoal);
            Paint(Shape3.Cube, p + new Vector3(0.8f, 5, 0), new Vector3(2.2f, 0.18f, 0.18f), Palette.Charcoal, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.6f));
        }
    }

    // ================================================================= simulación

    public void Update(float dt, Controls c)
    {
        Sounds.Clear();
        ToastTime -= dt;
        PhaseTime += dt;
        Shake = MathF.Max(0, Shake - dt * 2.2f);
        ImpactFlash = MathF.Max(0, ImpactFlash - dt);
        ImpactRough = MathF.Max(0, ImpactRough - dt * 1.5f);
        for (int i = Effects.Count - 1; i >= 0; i--)
        {
            Fx f = Effects[i];
            f.Age += dt;
            if (f.Age > f.Life) Effects.RemoveAt(i); else Effects[i] = f;
        }

        UpdateCamera(dt, c);

        switch (Phase)
        {
            case Phase.Intro:
                // La película se detiene: cartel de presentación.
                if (PhaseTime > 4.2f) { Phase = Phase.Boss; PhaseTime = 0; King.Wake(); LockTarget = King; }
                return;
            case Phase.Dead:
                if (PhaseTime > 2.5f && c.Any) Respawn();
                break;
            case Phase.Victory:
                break;
        }

        // Las pulsaciones se retienen hasta que un paso de física las consuma: si en este
        // fotograma no toca ningún paso, no se pierden.
        _pending = _pending with
        {
            Move = c.Move,
            Light = _pending.Light || c.Light,
            Heavy = _pending.Heavy || c.Heavy,
            Dodge = _pending.Dodge || c.Dodge,
            Parry = _pending.Parry || c.Parry,
            Heal = _pending.Heal || c.Heal,
            Interact = _pending.Interact || c.Interact,
        };
        if (Hitstop > 0) { Hitstop -= dt; return; } // congelación del impacto; la entrada queda en espera
        _accumulator += MathF.Min(dt, 0.1f);
        while (_accumulator >= FixedStep)
        {
            _accumulator -= FixedStep;
            FixedUpdate(_pending);
            _pending = new Controls { Move = c.Move };
            if (Hitstop > 0) break;
        }
    }

    Controls _pending;

    void FixedUpdate(Controls c)
    {
        if (c.Interact && Vector3.Distance(Player.Feet, Bonfire) < 2.4f && !Player.Dead)
        {
            BonfireLit = true;
            Player.RestAtBonfire();
            AddFx(FxKind.Rest, Bonfire + new Vector3(0, 1, 0), Vector3.UnitY, 1.5f);
            Play(Sfx.Bonfire, Bonfire);
            Say("Hoguera encendida. El reino vuelve a respirar.", 3f);
        }

        Player.Think(this, c, FixedStep);
        foreach (Skeleton s in Skeletons) s.Think(this, FixedStep);
        King.Think(this, FixedStep);

        World.Step(FixedStep, subStepCount: 4);

        foreach (SensorBeginEvent e in World.Events.SensorBegins)
            if (e.Sensor == _fogSensor && e.Visitor.Body == Player.Body && Phase == Phase.Explore && !BossDefeated)
                StartBoss();

        if (Player.Dead && Phase is Phase.Explore or Phase.Boss) { Phase = Phase.Dead; PhaseTime = 0; LockTarget = null; }
        if (King.Dead && Phase == Phase.Boss && King.StateTime > 2.5f) { Phase = Phase.Victory; PhaseTime = 0; BossDefeated = true; LockTarget = null; }
        if (LockTarget is { Dead: true }) LockTarget = null;
    }

    void StartBoss()
    {
        Phase = Phase.Intro;
        PhaseTime = 0;
        // Se sella la entrada detrás del caballero.
        Body seal = World.CreateStaticBody(GateCenter + new Vector3(0, 4, 1.2f));
        seal.AddBox(new Box(new Vector3(5, 4, 0.3f)), ShapeDefinition.Default with { Filter = new CollisionFilter(Cat.Static, ulong.MaxValue) });
        SealUp = true;
        Player.Body.SetTransform(Player.Body.Position + new Vector3(0, 0, -1.5f));
        Player.Body.IsAwake = true; // SetTransform no despierta el cuerpo
        CamYaw = MathF.PI;
    }

    void Respawn()
    {
        bool lit = BonfireLit, defeated = BossDefeated;
        Build();
        BonfireLit = lit;
        BossDefeated = defeated;
        if (defeated) { King.Dead = true; King.State = KingState.Dead; King.Body.Disable(); }
        Player.RestAtBonfire();
    }

    void UpdateCamera(float dt, Controls c)
    {
        if (c.LockOn)
        {
            if (LockTarget != null) LockTarget = null;
            else LockTarget = NearestFoe(18f);
        }
        if (LockTarget != null)
        {
            Vector3 to = LockTarget.Body.Position - Player.Body.Position;
            float want = Actor.YawOf(to);
            CamYaw += MathF.IEEERemainder(want - CamYaw, MathF.Tau) * (1 - MathF.Exp(-dt * 6));
            CamPitch += (0.3f - CamPitch) * (1 - MathF.Exp(-dt * 4));
        }
        else
        {
            CamYaw -= c.Look.X * 0.0032f;
            CamPitch = Math.Clamp(CamPitch + c.Look.Y * 0.0026f, -0.15f, 1.1f);
        }
    }

    Actor? NearestFoe(float range)
    {
        Actor? best = null;
        float bestD = range;
        IEnumerable<Actor> foes = Skeletons.Where(s => !s.Dead).Cast<Actor>();
        if (Phase == Phase.Boss && !King.Dead) foes = foes.Append(King);
        foreach (Actor a in foes)
        {
            float d = Vector3.Distance(a.Body.Position, Player.Body.Position);
            if (d < bestD) { bestD = d; best = a; }
        }
        return best;
    }

    /// <summary>Cámara en tercera persona que no atraviesa muros (raycast contra lo estático).</summary>
    public (Vector3 Eye, Vector3 Target) CameraRig()
    {
        Vector3 target = Player.Body.Position + new Vector3(0, 0.9f, 0);
        if (LockTarget != null) target = Vector3.Lerp(target, LockTarget.Body.Position + new Vector3(0, LockTarget.HalfHeight * 0.3f, 0), 0.25f);
        float dist = Phase is Phase.Boss && LockTarget == King ? 8.5f : 6.2f;
        Vector3 back = new Vector3(-MathF.Sin(CamYaw) * MathF.Cos(CamPitch), MathF.Sin(CamPitch), -MathF.Cos(CamYaw) * MathF.Cos(CamPitch));
        RaycastHit hit = World.RaycastClosest(target, back * dist, Cat.OnlyStatic);
        float d = hit.Hit ? MathF.Max(1.2f, dist * hit.Fraction - 0.3f) : dist;
        return (target + back * d, target);
    }

    // ================================================================= combate

    public void AddFx(FxKind kind, Vector3 at, Vector3 dir, float size, float life = 0.45f) =>
        Effects.Add(new Fx { Kind = kind, Position = at, Direction = dir, Size = size, Life = life, Seed = _rng.Next() });

    public void Say(string text, float seconds) { Toast = text; ToastTime = seconds; }

    public void Play(Sfx sfx, Vector3 at, float size = 1f) => Sounds.Add(new Heard(sfx, at, size));

    Actor? ActorOf(ulong id)
    {
        if (id == Knight.Id) return Player;
        if (id == FallenKing.Id) return King;
        if (id >= FoeBase && id < FoeBase + (ulong)Skeletons.Count) return Skeletons[(int)(id - FoeBase)];
        return null;
    }

    Prop? PropOf(ulong id) => id >= PropBase && id < PropBase + (ulong)Props.Count ? Props[(int)(id - PropBase)] : null;

    /// <summary>
    /// Golpe del caballero. <c>OverlapBox</c> de Box3D.NET da los candidatos (es de fase amplia),
    /// y aquí se afina por distancia.
    /// </summary>
    public void Strike(Knight k, Vector3 center, float radius, float height, float damage, bool heavy)
    {
        _found.Clear();
        var cb = new CollectShapes { Found = _found };
        World.OverlapBox(center, new Vector3(radius, height / 2, radius), ref cb);

        bool hitSomething = false;
        var seen = new HashSet<ulong>();
        foreach (Shape s in _found)
        {
            ulong id = s.Body.UserData;
            if (!seen.Add(id) || id == Knight.Id) continue;
            Vector3 closest = s.ClosestPointTo(center);
            if (Vector3.Distance(closest, center) > radius + 0.2f) continue;

            if (ActorOf(id) is { } foe)
            {
                float mult = foe is FallenKing { State: KingState.Stagger } ? 1.8f : 1f;
                if (foe is Skeleton sk) sk.TakeHit(this, damage * mult, k.Body.Position, heavy);
                else if (foe is FallenKing king) king.TakeHit(this, damage * mult, heavy);
                AddFx(heavy ? FxKind.HeavyHit : FxKind.Hit, closest, k.Forward, heavy ? 1.4f : 0.9f);
                Play(heavy ? Sfx.HeavyHit : Sfx.Hit, closest, foe is FallenKing ? 1.4f : 1f);
                if (foe is FallenKing { Phase2: true }) AddFx(FxKind.Blood, closest, k.Forward, 0.8f, 0.8f);
                Hitstop = MathF.Max(Hitstop, heavy ? 0.13f : 0.06f);
                Shake = MathF.Max(Shake, heavy ? 0.45f : 0.2f);
                if (heavy) { ImpactFlash = 0.05f; }
                hitSomething = true;
            }
            else if (PropOf(id) is { } prop)
            {
                Vector3 push = k.Forward * (heavy ? 9f : 4f) + new Vector3(0, heavy ? 3f : 1.5f, 0);
                prop.Body.ApplyImpulse(push * prop.Body.Mass * 0.35f, closest);
                AddFx(prop.Kind == PropKind.Bell ? FxKind.Bell : FxKind.Spark, closest, -k.Forward, prop.Kind == PropKind.Bell ? 3f : 0.7f, prop.Kind == PropKind.Bell ? 1.4f : 0.4f);
                Play(prop.Kind switch { PropKind.Bell => Sfx.Bell, PropKind.Link => Sfx.Clang, _ => Sfx.Knock }, closest, heavy ? 1.3f : 1f);
                hitSomething = true;
            }
        }

        if (!hitSomething)
        {
            // Si la espada da contra un muro, saltan estrellitas.
            RaycastHit wall = World.RaycastClosest(k.Body.Position, k.Forward * (radius + 1.0f), Cat.OnlyStatic);
            if (wall.Hit) { AddFx(FxKind.Spark, wall.Point, wall.Normal, 0.8f); Play(Sfx.Clang, wall.Point); Hitstop = MathF.Max(Hitstop, 0.04f); }
        }
    }

    /// <summary>Golpe de un enemigo en un volumen frente a él.</summary>
    public void EnemyStrike(Actor attacker, Vector3 center, float radius, float damage, bool parryable)
    {
        Vector3 d = Player.Body.Position - center;
        if (new Vector2(d.X, d.Z).Length() > radius + Player.Radius || MathF.Abs(d.Y) > 1.8f) return;
        HitPlayer(attacker, damage, center, parryable);
    }

    /// <summary>Barrido en arco del rey: alcanza al caballero y lanza por los aires lo que encuentre.</summary>
    public void EnemyArc(FallenKing king, float radius, float halfAngleDeg, float damage, bool parryable)
    {
        Vector3 origin = king.Feet;
        Vector3 d = Player.Feet - origin;
        d.Y = 0;
        float cos = MathF.Cos(halfAngleDeg * MathF.PI / 180);
        if (d.Length() < radius && d.Length() > 0.01f && Vector3.Dot(Vector3.Normalize(d), king.Forward) > cos && Player.Feet.Y < origin.Y + 2.5f)
            HitPlayer(king, damage, king.Body.Position, parryable);

        _found.Clear();
        var cb = new CollectShapes { Found = _found };
        World.OverlapBox(origin + king.Forward * radius * 0.5f + new Vector3(0, 1, 0), new Vector3(radius, 1.2f, radius), ref cb, new QueryFilter(ulong.MaxValue, Cat.Prop));
        foreach (Shape s in _found)
        {
            if (PropOf(s.Body.UserData) is not { } prop || prop.Kind is PropKind.Bell or PropKind.Link) continue;
            Vector3 to = prop.Body.Position - origin;
            to.Y = 0;
            if (to.Length() > radius || Vector3.Dot(Vector3.Normalize(to), king.Forward) < cos) continue;
            Vector3 tangent = Vector3.Cross(Vector3.UnitY, Vector3.Normalize(to));
            prop.Body.ApplyImpulseToCenter((tangent * 8f + Vector3.Normalize(to) * 5f + Vector3.UnitY * 4f) * prop.Body.Mass);
        }
        AddFx(FxKind.Dust, origin + king.Forward * 4f, king.Forward, 2.5f, 0.6f);
        Play(Sfx.HeavySwing, origin + king.Forward * 3f, 2.2f);
        Shake = MathF.Max(Shake, 0.35f);
    }

    /// <summary>Golpe contra el suelo: daño directo, explosión física (Explode) y temblor.</summary>
    public void Slam(FallenKing king, Vector3 at, float radius, float damage)
    {
        Vector3 d = Player.Feet - at;
        if (new Vector2(d.X, d.Z).Length() < radius && d.Y < 2f) HitPlayer(king, damage, at, parryable: false);
        // Solo los escombros: las cápsulas de los personajes también responderían a la explosión.
        World.Explode(at + new Vector3(0, 0.3f, 0), radius + 1.5f, 380f, falloff: 4f, filter: Cat.Prop);
        AddFx(FxKind.Slam, at, Vector3.UnitY, radius, 0.7f);
        AddFx(FxKind.Ring, at, Vector3.UnitY, 9f, 0.7f);
        Play(Sfx.Slam, at, radius / 2.7f);
        // La onda hace sonar la campana si le pilla cerca.
        if (Vector3.Distance(at with { Y = 0 }, BellPivot with { Y = 0 }) < 14f) Play(Sfx.Bell, Bell.Position, 1.2f);
        Shake = MathF.Max(Shake, 1.0f);
        ImpactFlash = 0.08f;
        ImpactRough = 0.8f;
        Hitstop = MathF.Max(Hitstop, 0.09f);
    }

    float _lastBump = -10;

    /// <summary>
    /// El rey ciego se estrella contra una columna: un raycast hacia delante la encuentra y
    /// la columna entera recibe el empujón (los tambores de encima, más fuerte, para que vuelque).
    /// </summary>
    public bool BlindBump(FallenKing king)
    {
        if (king.StateTime - _lastBump < 0.8f && _lastBump <= king.StateTime) return false;
        RaycastHit hit = World.RaycastClosest(king.Body.Position - new Vector3(0, 1.2f, 0), king.Forward * (king.Radius + 1.0f), new QueryFilter(ulong.MaxValue, Cat.Prop | Cat.Static));
        if (!hit.Hit) return false;
        _lastBump = king.StateTime;
        Vector3 axis = hit.Shape.Body.Position;
        foreach (Prop p in Props)
        {
            if (p.Kind is not (PropKind.Drum or PropKind.Crate) || !p.Body.IsValid) continue;
            Vector3 d = p.Body.Position - axis;
            if (new Vector2(d.X, d.Z).Length() > 1.3f) continue;
            float height = MathF.Max(0, p.Body.Position.Y);
            p.Body.ApplyImpulseToCenter(king.Forward * p.Body.Mass * (0.8f + height * 0.45f));
        }
        AddFx(FxKind.HeavyHit, hit.Point + new Vector3(0, 1.5f, 0), -king.Forward, 1.6f);
        AddFx(FxKind.Dust, hit.Point with { Y = 0 }, Vector3.UnitY, 2f, 0.8f);
        Play(Sfx.Bump, hit.Point);
        Shake = MathF.Max(Shake, 0.6f);
        Hitstop = MathF.Max(Hitstop, 0.08f);
        return true;
    }

    public bool RingTouches(Vector3 center, float r)
    {
        Vector3 d = Player.Feet - center;
        float dist = new Vector2(d.X, d.Z).Length();
        if (MathF.Abs(dist - r) > 0.55f || !IsGrounded(Player)) return false;
        return HitPlayer(King, King.Phase2 ? 24 : 18, center, parryable: false);
    }

    public bool IsGrounded(Actor a)
    {
        RaycastHit hit = World.RaycastClosest(a.Body.Position, new Vector3(0, -(a.HalfHeight + 0.25f), 0), Cat.NotActors);
        return hit.Hit;
    }

    bool HitPlayer(Actor attacker, float damage, Vector3 from, bool parryable)
    {
        if (Player.Dead || Player.Invulnerable > 0) return false;
        bool parried = Player.TakeHit(this, attacker, damage, from, parryable);
        Vector3 mid = (Player.Body.Position + attacker.Body.Position) * 0.5f;
        if (parried)
        {
            if (attacker is Skeleton s) s.OnParried();
            if (attacker is FallenKing k) k.OnParried();
            AddFx(FxKind.Parry, Player.Body.Position + Player.Forward * 0.6f + new Vector3(0, 0.5f, 0), Player.Forward, 1.6f, 0.5f);
            Play(Sfx.Parry, Player.Body.Position + Player.Forward * 0.6f, attacker is FallenKing ? 1.3f : 1f);
            Hitstop = 0.18f;
            ImpactFlash = 0.1f;
            Shake = MathF.Max(Shake, 0.5f);
            Say("¡Desvío! Contraataca", 1.2f);
        }
        else
        {
            AddFx(FxKind.Hit, Player.Body.Position + new Vector3(0, 0.3f, 0), Vector3.Normalize(Player.Body.Position - from + new Vector3(0, 0.01f, 0)), 1.1f);
            if (!Player.Dead) Play(Sfx.Hurt, Player.Body.Position);
            Hitstop = MathF.Max(Hitstop, 0.08f);
            Shake = MathF.Max(Shake, 0.5f);
        }
        return true;
    }

    /// <summary>El esqueleto se deshace en huesos con física.</summary>
    public void Shatter(Skeleton s, Vector3 push)
    {
        Vector3 at = s.Body.Position;
        s.Body.Disable();
        if (push.LengthSquared() > 1e-4f) push = Vector3.Normalize(push);
        for (int i = 0; i < (ShatterIntoBones ? 7 : 0); i++)
        {
            Vector3 p = at + new Vector3((float)(_rng.NextDouble() - 0.5) * 0.6f, (float)(_rng.NextDouble() - 0.3) * 1.4f, (float)(_rng.NextDouble() - 0.5) * 0.6f);
            Body b = World.CreateDynamicBody(p, Quaternion.CreateFromYawPitchRoll((float)_rng.NextDouble() * 6, (float)_rng.NextDouble() * 6, 0));
            bool skull = i == 0;
            if (skull) b.AddSphere(new Sphere(0.22f), ShapeDefinition.Default with { Density = 80f, Filter = new CollisionFilter(Cat.Prop, ulong.MaxValue) });
            else b.AddCapsule(new Capsule(new Vector3(0, -0.22f, 0), new Vector3(0, 0.22f, 0), 0.06f), ShapeDefinition.Default with { Density = 120f, Filter = new CollisionFilter(Cat.Prop, ulong.MaxValue) });
            b.LinearVelocity = push * 5f + new Vector3((float)(_rng.NextDouble() - 0.5) * 5, 4 + (float)_rng.NextDouble() * 4, (float)(_rng.NextDouble() - 0.5) * 5);
            b.AngularVelocity = new Vector3((float)_rng.NextDouble() * 12, (float)_rng.NextDouble() * 12, 0);
            AddProp(skull ? PropKind.Skull : PropKind.Bone, b, skull ? new Vector3(0.44f) : new Vector3(0.12f, 0.56f, 0.12f), Palette.Bone);
        }
        AddFx(FxKind.HeavyHit, at, push, 1.3f, 0.5f);
        AddFx(FxKind.Dust, s.Feet, Vector3.UnitY, 1.5f, 0.8f);
        Play(Sfx.Shatter, at);
    }

    public void Dispose() => World.Dispose();
}
