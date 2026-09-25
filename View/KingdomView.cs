using System.Numerics;
using CaballeroDeTinta.Art;
using CaballeroDeTinta.Sim;
using Raylib_cs;

namespace CaballeroDeTinta.View;

/// <summary>Compone el fotograma: escena pintada, efectos de tinta, película y carteles.</summary>
sealed class KingdomView : IDisposable
{
    readonly Ink _ink = new();
    readonly Cards _cards = new();
    readonly Rigs _rigs;
    Camera3D _camera;
    float _hpLag = 1, _bossLag = 1;
    public Camera3D? Override;
    public Camera3D Camera => _camera;

    public KingdomView() => _rigs = new Rigs(_ink);

    public void Draw(Kingdom k)
    {
        float time = (float)Raylib.GetTime();
        _ink.Tick(time);
        _rigs.SetTime(time);
        _ink.Flash = k.ImpactFlash > 0 ? 1 : 0;
        _ink.Rough = k.ImpactRough;

        var (eye, target) = k.CameraRig();
        if (k.Shake > 0)
        {
            float s = k.Shake * k.Shake * 0.35f;
            var shake = new Vector3(MathF.Sin(time * 71) * s, MathF.Sin(time * 53 + 1) * s, MathF.Sin(time * 61 + 2) * s);
            eye += shake; target += shake * 0.6f;
        }
        _camera = Override ?? new Camera3D(eye, target, Vector3.UnitY, 55f, CameraProjection.Perspective);

        var lights = new List<Vector4>(k.Lights);
        float flicker = 0.85f + 0.3f * Hash(_ink.Seed);
        lights[0] = lights[0] with { W = lights[0].W * flicker * (k.BonfireLit ? 1.2f : 0.9f) };
        _ink.SetLights(ClosestLights(lights, _camera.Position));

        _ink.BeginScene(_camera);
        DrawWorld(k, time);
        _rigs.DrawKnight(k.Player);
        for (int i = 0; i < k.Skeletons.Count; i++) _rigs.DrawSkeleton(k.Skeletons[i], i);
        _rigs.DrawKing(k.King);
        DrawEffects3D(k);
        _ink.EndScene(() => { DrawEffects2D(k); Multiplane(k); });

        DrawHud(k);
        DrawCards(k);
    }

    static float Hash(float x) => MathF.Abs(MathF.Sin(x * 12.9898f) * 43758.5453f) % 1f;

    static Vector4[] ClosestLights(List<Vector4> all, Vector3 from) =>
        all.OrderBy(l => Vector3.DistanceSquared(new Vector3(l.X, l.Y, l.Z), from)).Take(6).ToArray();

    // ================================================================== mundo

    void DrawWorld(Kingdom k, float time)
    {
        foreach (Piece p in k.Statics) _ink.Draw(p.Shape, p.Position, p.Rotation, p.Size, p.Color, p.Size.Y > 30 ? 0 : 0.04f, p.Emissive);
        foreach (Piece p in k.Decor) _ink.Draw(p.Shape, p.Position, p.Rotation, p.Size, p.Color, p.Size.Y > 30 ? 0.02f : 0.035f, p.Emissive);

        foreach (Prop prop in k.Props)
        {
            if (!prop.Body.IsValid || !prop.Body.IsEnabled) continue;
            Vector3 pos = prop.Body.Position;
            Quaternion rot = prop.Body.Rotation;
            switch (prop.Kind)
            {
                case PropKind.Crate:
                    _ink.Draw(Shape3.Cube, pos, rot, prop.Size, prop.Color);
                    if (prop.Size.Y > 0.7f)
                        _ink.Draw(Shape3.Cube, pos, rot, prop.Size * new Vector3(1.02f, 0.22f, 1.02f), Palette.Mix(prop.Color, Palette.Ink, 0.35f), 0.015f);
                    break;
                case PropKind.Barrel:
                    _ink.Draw(Shape3.Cylinder, pos, rot, prop.Size, prop.Color);
                    _ink.Draw(Shape3.Cylinder, pos + Vector3.Transform(new Vector3(0, 0.3f, 0), rot), rot, new Vector3(0.84f, 0.07f, 0.84f), Palette.Charcoal, 0.01f);
                    _ink.Draw(Shape3.Cylinder, pos - Vector3.Transform(new Vector3(0, 0.3f, 0), rot), rot, new Vector3(0.84f, 0.07f, 0.84f), Palette.Charcoal, 0.01f);
                    break;
                case PropKind.Drum:
                    _ink.Draw(Shape3.Cylinder, pos, rot, prop.Size, prop.Color, 0.04f);
                    break;
                case PropKind.Rock:
                    _ink.Draw(Shape3.Sphere, pos, rot, new Vector3(0.95f, 0.75f, 0.85f), prop.Color);
                    break;
                case PropKind.Bone:
                    _ink.Draw(Shape3.Cylinder, pos, rot, prop.Size, prop.Color, 0.02f);
                    _ink.Draw(Shape3.Sphere, pos + Vector3.Transform(new Vector3(0, 0.28f, 0), rot), rot, new Vector3(0.16f), prop.Color, 0.02f);
                    _ink.Draw(Shape3.Sphere, pos - Vector3.Transform(new Vector3(0, 0.28f, 0), rot), rot, new Vector3(0.16f), prop.Color, 0.02f);
                    break;
                case PropKind.Skull:
                    _ink.Draw(Shape3.Sphere, pos, rot, prop.Size, prop.Color, 0.025f);
                    _ink.Draw(Shape3.Sphere, pos + Vector3.Transform(new Vector3(-0.08f, 0.03f, 0.17f), rot), rot, new Vector3(0.1f, 0.12f, 0.06f), Palette.Ink, 0);
                    _ink.Draw(Shape3.Sphere, pos + Vector3.Transform(new Vector3(0.08f, 0.03f, 0.17f), rot), rot, new Vector3(0.1f, 0.12f, 0.06f), Palette.Ink, 0);
                    break;
                case PropKind.Bell:
                    // El hull es un cono truncado con la base en el origen del cuerpo.
                    _ink.Draw(Shape3.Cylinder, pos + Vector3.Transform(new Vector3(0, 1.5f, 0), rot), rot, new Vector3(3.4f, 2.9f, 3.4f), prop.Color, 0.05f);
                    _ink.Draw(Shape3.Sphere, pos + Vector3.Transform(new Vector3(0, 2.9f, 0), rot), rot, new Vector3(3.4f, 1.4f, 3.4f), prop.Color, 0.05f);
                    _ink.Draw(Shape3.Cylinder, pos + Vector3.Transform(new Vector3(0, 0.12f, 0), rot), rot, new Vector3(4.7f, 0.3f, 4.7f), Palette.Mix(prop.Color, Palette.Ink, 0.3f), 0.04f);
                    _ink.Draw(Shape3.Sphere, pos + Vector3.Transform(new Vector3(0, 0.3f, 0), rot), rot, new Vector3(0.8f), Palette.Charcoal, 0.03f);
                    _ink.Segment(pos + Vector3.Transform(new Vector3(0, 3.4f, 0), rot), k.BellPivot, 0.18f, Palette.Charcoal);
                    break;
                case PropKind.Link:
                    break;
            }
        }

        foreach (var (top, links) in k.Chains)
        {
            Vector3 prev = top;
            foreach (var link in links)
            {
                _ink.Segment(prev, link.Position, 0.06f, Palette.Steel, 0.02f);
                prev = link.Position;
            }
        }

        DrawBonfire(k, time);
        if (!k.BossDefeated && k.Phase is Phase.Explore) DrawFogGate(k, time);
    }

    void DrawBonfire(Kingdom k, float time)
    {
        Vector3 b = k.Bonfire;
        _ink.Draw(Shape3.Cylinder, b + new Vector3(0, 0.12f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.35f) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f), new Vector3(0.22f, 1.3f, 0.22f), Palette.DarkUmber);
        _ink.Draw(Shape3.Cylinder, b + new Vector3(0, 0.12f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.35f) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, 2.0f), new Vector3(0.22f, 1.3f, 0.22f), Palette.DarkUmber);
        // Espada clavada en las brasas.
        _ink.Draw(Shape3.Cube, b + new Vector3(0, 0.75f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.15f), new Vector3(0.08f, 1.4f, 0.03f), Palette.Mix(Palette.Steel, Palette.Ember, 0.25f), 0.02f);
        // Llamas que bailan: conos saturados que cambian a doses.
        float seed = _ink.Seed;
        float power = k.BonfireLit ? 1.25f : 0.8f;
        for (int i = 0; i < 4; i++)
        {
            float h = (0.9f + 0.5f * Hash(seed + i)) * power;
            float sway = (Hash(seed * 1.7f + i) - 0.5f) * 0.5f;
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, sway) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, (Hash(seed + i * 3) - 0.5f) * 0.4f);
            Vector3 at = b + new Vector3(MathF.Sin(i * 1.7f) * 0.18f, h * 0.5f + 0.1f, MathF.Cos(i * 1.7f) * 0.18f);
            _ink.Draw(Shape3.Cone, at, q, new Vector3(0.45f, h, 0.45f), Palette.Ember, 0.025f, emissive: 1f);
        }
        _ink.Draw(Shape3.Cone, b + new Vector3(0, 0.45f, 0), Quaternion.Identity, new Vector3(0.28f, 0.7f * power, 0.28f), new Color(255, 220, 120, 255), 0, emissive: 1f);
        // Chispas que suben en espiral.
        for (int i = 0; i < 6; i++)
        {
            float ph = (time * 0.5f + i / 6f) % 1f;
            float ang = time * 2 + i;
            _ink.Draw(Shape3.Cube, b + new Vector3(MathF.Sin(ang) * 0.3f * ph, 0.8f + ph * 2.2f, MathF.Cos(ang) * 0.3f * ph), Quaternion.Identity, new Vector3(0.06f), Palette.Ember, 0, emissive: 1f);
        }
    }

    void DrawFogGate(Kingdom k, float time)
    {
        // Cortina de tinta cian fantasmal que ondula.
        Vector3 c = k.GateCenter + new Vector3(0, 0, -0.5f);
        for (int i = 0; i < 12; i++)
        {
            float x = -4.6f + i * 0.84f;
            float sway = MathF.Sin(time * 1.3f + i * 0.8f) * 0.25f;
            float h = 7.6f + MathF.Sin(time * 0.9f + i) * 0.4f;
            _ink.Draw(Shape3.Cube, c + new Vector3(x, h / 2, sway), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, sway * 0.05f), new Vector3(0.9f, h, 0.08f), Palette.Alpha(Palette.GhostCyan, 0.34f), 0, emissive: 0.85f);
        }
    }

    void DrawEffects3D(Kingdom k)
    {
        foreach (Fx f in k.Effects)
        {
            float a = f.Age / f.Life;
            switch (f.Kind)
            {
                case FxKind.Dust:
                case FxKind.Slam:
                    // Humo que se enrosca en espirales imposibles.
                    for (int i = 0; i < 9; i++)
                    {
                        float ang = i / 9f * MathF.Tau + a * 3f;
                        float r = f.Size * (0.3f + a * 0.9f);
                        Vector3 p = f.Position + new Vector3(MathF.Cos(ang) * r, 0.3f + a * 1.2f + MathF.Sin(ang * 2) * 0.2f, MathF.Sin(ang) * r);
                        float s = f.Size * 0.35f * (1 - a * 0.6f);
                        _ink.Draw(Shape3.Sphere, p, Quaternion.Identity, new Vector3(s), Palette.Alpha(Palette.Mix(Palette.DirtyCream, Palette.DarkStone, 0.4f), 1 - a), 0.025f * (1 - a));
                    }
                    break;
                case FxKind.Ring:
                {
                    float r = f.Age * 13f;
                    if (r > f.Size) break;
                    for (int i = 0; i < 28; i++)
                    {
                        float ang = i / 28f * MathF.Tau;
                        Vector3 p = f.Position + new Vector3(MathF.Cos(ang) * r, 0.25f, MathF.Sin(ang) * r);
                        _ink.Draw(Shape3.Sphere, p, Quaternion.Identity, new Vector3(0.55f, 0.35f, 0.55f), Palette.Mix(Palette.DirtyCream, Palette.Ink, 0.3f), 0.03f);
                    }
                    break;
                }
                case FxKind.Rest:
                    for (int i = 0; i < 8; i++)
                    {
                        float ang = i * 0.8f + a * 4;
                        Vector3 p = f.Position + new Vector3(MathF.Cos(ang) * 0.5f, a * 2f + i * 0.1f, MathF.Sin(ang) * 0.5f);
                        _ink.Draw(Shape3.Cube, p, Quaternion.Identity, new Vector3(0.07f), Palette.Ember, 0, emissive: 1);
                    }
                    break;
                case FxKind.Blood:
                    for (int i = 0; i < 10; i++)
                    {
                        float ang = i * 2.39f;
                        Vector3 p = f.Position + new Vector3(MathF.Cos(ang), 0, MathF.Sin(ang)) * f.Size * a + new Vector3(0, 1.2f * a - 3f * a * a, 0);
                        _ink.Draw(Shape3.Sphere, p, Quaternion.Identity, new Vector3(0.2f * f.Size * (1 - a) + 0.05f), Palette.Crimson, 0.015f, 0.5f);
                    }
                    break;
            }
        }
    }

    // ================================================================== 2D sobre la película

    Vector2 Screen(Vector3 p) => Raylib.GetWorldToScreen(p, _camera);

    bool InFront(Vector3 p) => Vector3.Dot(p - _camera.Position, Vector3.Normalize(_camera.Target - _camera.Position)) > 0.5f;

    void DrawEffects2D(Kingdom k)
    {
        foreach (Fx f in k.Effects)
        {
            if (!InFront(f.Position)) continue;
            float a = f.Age / f.Life;
            Vector2 s = Screen(f.Position);
            float dist = MathF.Max(2, Vector3.Distance(f.Position, _camera.Position));
            float scale = 14f / dist;
            var rng = new Random(f.Seed);
            switch (f.Kind)
            {
                case FxKind.Spark:
                    // Chispas como estrellitas de dibujo animado.
                    for (int i = 0; i < 5; i++)
                    {
                        float ang = (float)(rng.NextDouble() * MathF.Tau);
                        Vector2 dir = new(MathF.Cos(ang), MathF.Sin(ang) - 0.5f);
                        Cards.Star(s + dir * (20 + 90 * a) * scale * f.Size, (9 + 7 * (float)rng.NextDouble()) * (1 - a) * scale * 1.6f + 2, a * 6 + i, new Color(250, 230, 150, 255));
                    }
                    break;
                case FxKind.Hit:
                case FxKind.HeavyHit:
                {
                    bool heavy = f.Kind == FxKind.HeavyHit;
                    if (a < 0.35f) Cards.InkBurst(s, (heavy ? 150 : 90) * f.Size * scale * (0.6f + a), _ink.Seed + f.Seed % 100, Palette.Ink, Palette.Parchment);
                    if (heavy && a < 0.25f) Cards.SpeedLines(s, 1 - a * 3, _ink.Seed);
                    for (int i = 0; i < 4; i++)
                    {
                        float ang = (float)(rng.NextDouble() * MathF.Tau);
                        Cards.Star(s + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * (40 + 120 * a) * scale * f.Size, 10 * (1 - a) * scale * 1.5f + 2, a * 8, Palette.Parchment);
                    }
                    break;
                }
                case FxKind.Parry:
                    if (a < 0.4f)
                    {
                        Cards.InkBurst(s, 130 * scale * (0.8f + a), _ink.Seed + 3, Palette.GhostCyan, Palette.Parchment);
                        Cards.SpeedLines(s, 1 - a * 2.5f, _ink.Seed + 9);
                    }
                    Raylib.DrawRing(s, 40 + a * 260 * scale, 46 + a * 262 * scale, 0, 360, 48, Palette.Alpha(Palette.GhostCyan, 1 - a));
                    break;
                case FxKind.Slam:
                    if (a < 0.3f) Cards.InkBurst(s, 220 * scale * f.Size * 0.4f, _ink.Seed, Palette.Ink, Palette.DirtyCream);
                    Cards.Crack(s, 260 * scale * f.Size * 0.4f, f.Seed, Palette.Alpha(Palette.Ink, 1 - a));
                    break;
                case FxKind.Blood:
                    if (a < 0.5f) Cards.InkBurst(s, 80 * scale * f.Size * (0.5f + a), f.Seed % 50, Palette.Crimson, Palette.Mix(Palette.Crimson, Palette.Parchment, 0.3f));
                    break;
                case FxKind.Bell:
                    // El sonido de la campana, dibujado.
                    for (int i = 0; i < 3; i++)
                    {
                        float r = (a + i * 0.18f) * 420 * scale;
                        Raylib.DrawRing(s, r, r + 5, 0, 360, 64, Palette.Alpha(Palette.OldGold, 1 - a));
                    }
                    break;
            }
        }

        // Pajaritos/estrellas girando sobre quien está aturdido.
        foreach (Actor act in k.Skeletons.Where(s => s.State == FoeState.Stagger).Cast<Actor>().Append(k.King.State == KingState.Stagger ? k.King : null!).Where(a => a != null))
        {
            Vector3 head = act.Body.Position + new Vector3(0, act.HalfHeight + 0.5f, 0);
            if (!InFront(head)) continue;
            Vector2 c = Screen(head);
            float t = (float)Raylib.GetTime() * 5;
            float r = 900f / MathF.Max(3, Vector3.Distance(head, _camera.Position));
            for (int i = 0; i < 3; i++)
                Cards.Star(c + new Vector2(MathF.Cos(t + i * 2.1f) * r, MathF.Sin(t + i * 2.1f) * r * 0.3f), r * 0.28f, t, Palette.OldGold);
        }

        if (k.LockTarget is { } lt)
        {
            Vector3 p = lt.Body.Position + new Vector3(0, lt.HalfHeight * 0.2f, 0);
            if (InFront(p))
            {
                Vector2 c = Screen(p);
                Raylib.DrawRing(c, 9, 12, 0, 360, 24, Palette.Parchment);
                Raylib.DrawCircleV(c, 4, Palette.Ink);
            }
        }
    }

    /// <summary>Planos de primer plano oscuros y desenfocados, como en la cámara multiplano.</summary>
    void Multiplane(Kingdom k)
    {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        float shift = k.CamYaw * 240f;
        Color dark = Palette.Alpha(new Color(18, 14, 12, 255), 0.82f);
        Color soft = Palette.Alpha(new Color(18, 14, 12, 255), 0.35f);
        // Cadenas que cuelgan desde arriba.
        for (int i = 0; i < 3; i++)
        {
            float x = ((i * 520 + shift) % (w + 400) + w + 400) % (w + 400) - 200;
            float len = 90 + i * 60;
            for (int j = 0; j < len / 22; j++)
            {
                Raylib.DrawEllipse((int)x, j * 22, 11, 14, soft);
                Raylib.DrawEllipse((int)x, j * 22, 8, 11, dark);
            }
        }
        // Zarzas en las esquinas inferiores.
        for (int side = 0; side < 2; side++)
        {
            float bx = side == 0 ? 0 : w;
            float dir = side == 0 ? 1 : -1;
            var rng = new Random(side * 17 + 3);
            for (int i = 0; i < 9; i++)
            {
                float len = 60 + (float)rng.NextDouble() * 170;
                float ang = -MathF.PI / 2 + dir * (0.2f + (float)rng.NextDouble() * 0.9f);
                Vector2 a0 = new(bx + dir * (float)rng.NextDouble() * 90, h + 8);
                Vector2 a1 = a0 + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * len;
                Raylib.DrawLineEx(a0, a1, 10, soft);
                Raylib.DrawLineEx(a0, a1, 6, dark);
                for (int t = 1; t < 4; t++)
                {
                    Vector2 m = Vector2.Lerp(a0, a1, t / 4f);
                    Raylib.DrawTriangle(m, m + new Vector2(dir * 14, -6), m + new Vector2(dir * 2, -16), dark);
                }
            }
        }
    }

    // ================================================================== HUD y carteles

    void DrawHud(Kingdom k)
    {
        if (k.Phase is Phase.Intro) return;
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        Knight p = k.Player;
        float seed = _ink.Seed;
        _hpLag += (p.Health / p.MaxHealth - _hpLag) * 0.03f;
        if (_hpLag < p.Health / p.MaxHealth) _hpLag = p.Health / p.MaxHealth;
        _cards.Bar(new Vector2(34, 30), 300, 14, p.Health / p.MaxHealth, _hpLag, Palette.Burgundy, seed);
        _cards.Bar(new Vector2(34, 54), 240, 9, p.Stamina / p.MaxStamina, p.Stamina / p.MaxStamina, Palette.ForestGreen, seed + 1);
        for (int i = 0; i < p.MaxFlasks; i++)
        {
            var c = new Vector2(44 + i * 26, 88);
            Raylib.DrawCircleV(c, 10, Palette.Ink);
            Raylib.DrawCircleV(c, 7, i < p.Flasks ? Palette.Ember : new Color(60, 50, 44, 255));
        }
        if (p.Riposte > 0) _cards.TextLeft("¡Contraataque!", new Vector2(130, 76), 22, Palette.GhostCyan, bold: true);

        if (k.Phase == Phase.Boss && !k.King.Dead)
        {
            FallenKing king = k.King;
            _bossLag += (king.Health / king.MaxHealth - _bossLag) * 0.02f;
            float bw = MathF.Min(760, w * 0.6f);
            _cards.Text("Baldomero III, el Rey que se Desploma", new Vector2(w / 2f, h - 88), 26, Palette.Parchment, bold: true);
            _cards.Bar(new Vector2((w - bw) / 2, h - 66), bw, 12, king.Health / king.MaxHealth, _bossLag, king.Phase2 ? Palette.Crimson : Palette.Burgundy, seed + 2);
        }

        if (k.ToastTime > 0 && k.Phase is Phase.Explore or Phase.Boss)
            _cards.Text(k.Toast, new Vector2(w / 2f, 110), 30, Palette.Alpha(Palette.Parchment, Math.Clamp(k.ToastTime, 0, 1)), bold: true);

        if (k.Phase is Phase.Explore && Vector3.Distance(p.Feet, k.Bonfire) < 2.4f && p.State != KnightState.Rest)
            _cards.Text("E · descansar en la hoguera", new Vector2(w / 2f, h / 2f + 90), 24, Palette.Ember, bold: true);

        string help = "WASD mover · ratón cámara · clic izq. ataque · clic der. ataque fuerte · Espacio esquivar · F desviar · Q curar · Tab fijar · E hoguera";
        _cards.Text(help, new Vector2(w / 2f, h - 18), 17, Palette.Alpha(Palette.Parchment, 0.75f));
    }

    void DrawCards(Kingdom k)
    {
        switch (k.Phase)
        {
            case Phase.Intro:
                _cards.TitleCard(k.PhaseTime, "— Capítulo I · La Sala de las Campanas —", "BALDOMERO III", "el Rey que se Desploma", _ink.Seed);
                break;
            case Phase.Dead:
                if (k.PhaseTime > 0.8f)
                    _cards.EndCard(k.PhaseTime - 0.8f, "Y así cayó el pequeño caballero", k.PhaseTime > 3f ? "— pulsa cualquier botón para volver a la hoguera —" : "", Palette.Crimson, _ink.Seed);
                break;
            case Phase.Victory:
                _cards.EndCard(k.PhaseTime, "Fin del primer rollo", "Baldomero III descansa. La campana guarda silencio.", Palette.OldGold, _ink.Seed);
                break;
        }
    }

    public void Dispose()
    {
        _ink.Dispose();
        _cards.Dispose();
    }
}
