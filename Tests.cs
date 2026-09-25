using System.Numerics;
using CaballeroDeTinta.Art;
using CaballeroDeTinta.Sim;

namespace CaballeroDeTinta;

/// <summary>Pruebas de la simulación sin ventana: <c>dotnet run -- --test</c>.</summary>
static class Tests
{
    static int _failures;

    public static int Run()
    {
        Scenario("Reposo: el reino se sostiene solo", k =>
        {
            float[] drumY = k.Props.Where(p => p.Kind == PropKind.Drum).Select(p => p.Body.Position.Y).ToArray();
            Simulate(k, 4f, default);
            float[] after = k.Props.Where(p => p.Kind == PropKind.Drum).Select(p => p.Body.Position.Y).ToArray();
            float worst = drumY.Zip(after, (a, b) => MathF.Abs(a - b)).Max();
            Report("máx. desplazamiento de tambores", worst);
            Report("caballero (pies)", k.Player.Feet);
            Expect(worst < 0.05f, "las columnas no se caen solas");
            Expect(k.IsGrounded(k.Player), "el caballero está en el suelo");
            Expect(k.Skeletons.All(s => s.State == FoeState.Idle), "los esqueletos esperan");
        });

        Scenario("Hoguera", k =>
        {
            k.Player.Health = 40;
            k.Player.Flasks = 0;
            Simulate(k, 0.2f, new Controls { Interact = true });
            Expect(k.BonfireLit && k.Player.Health == 100 && k.Player.Flasks == 3, "descansar cura y recarga los frascos");
        });

        Scenario("Movimiento relativo a la cámara", k =>
        {
            Vector3 start = k.Player.Feet;
            Simulate(k, 1f, new Controls { Move = new Vector2(0, 1) });
            Vector3 d = k.Player.Feet - start;
            Report("desplazamiento con W", d);
            Expect(d.Z < -3f, "W avanza hacia donde mira la cámara (-Z)");
            start = k.Player.Feet;
            Simulate(k, 0.6f, new Controls { Move = new Vector2(1, 0) });
            d = k.Player.Feet - start;
            Report("desplazamiento con D", d);
            Expect(d.X > 1.5f, "D va a la derecha de la pantalla (+X mirando a -Z)");
        });

        Scenario("Esqueleto: persigue y golpea", k =>
        {
            Teleport(k, new Vector3(-3, 0, -9));
            Simulate(k, 4f, default);
            Report("vida del caballero", k.Player.Health);
            Expect(k.Player.Health < 100, "el esqueleto se acerca y acierta");
        });

        Scenario("Parry a un esqueleto", k =>
        {
            Teleport(k, new Vector3(-3, 0, -11.2f));
            Skeleton s = k.Skeletons[0];
            bool parried = false;
            for (int i = 0; i < 400 && !parried; i++)
            {
                // Mirar al esqueleto y desviar justo antes de que suelte el golpe.
                k.Player.Yaw = Actor.YawOf(s.Body.Position - k.Player.Body.Position);
                bool press = s.State == FoeState.Windup && s.StateTime > 0.58f && k.Player.State == KnightState.Free;
                k.Update(Kingdom.FixedStep, new Controls { Parry = press });
                parried = s.State == FoeState.Stagger;
            }
            Report("estado del esqueleto", s.State);
            Report("vida del caballero", k.Player.Health);
            Expect(parried, "el desvío aturde al esqueleto");
            Expect(k.Player.Riposte > 0, "abre la ventana de contraataque");
        });

        Scenario("Espadazos: el esqueleto se deshace en huesos", k =>
        {
            Teleport(k, new Vector3(-3, 0, -11.4f));
            Skeleton s = k.Skeletons[0];
            int bonesBefore = k.Props.Count(p => p.Kind is PropKind.Bone or PropKind.Skull);
            for (int i = 0; i < 600 && !s.Dead; i++)
            {
                k.Player.Yaw = Actor.YawOf(s.Body.Position - k.Player.Body.Position);
                k.Player.Health = 100;
                k.Update(Kingdom.FixedStep, new Controls { Light = i % 20 == 0 });
            }
            Simulate(k, 1f, default);
            int bones = k.Props.Count(p => p.Kind is PropKind.Bone or PropKind.Skull) - bonesBefore;
            Report("huesos sueltos", bones);
            Expect(s.Dead, "tres o cuatro tajos lo derriban");
            Expect(bones == 7, "deja 7 huesos con física");
            Expect(k.Props.Where(p => p.Kind == PropKind.Skull).All(p => p.Body.Position.Y > -0.5f), "los huesos caen al suelo y no lo atraviesan");
        });

        Scenario("Esquiva con invulnerabilidad", k =>
        {
            Teleport(k, new Vector3(-3, 0, -11.2f));
            Skeleton s = k.Skeletons[0];
            bool dodged = false;
            for (int i = 0; i < 400; i++)
            {
                bool press = s.State == FoeState.Windup && s.StateTime > 0.6f && k.Player.State == KnightState.Free;
                k.Update(Kingdom.FixedStep, new Controls { Dodge = press });
                if (press) dodged = true;
                if (dodged && s.State == FoeState.Recover) break;
            }
            Report("vida tras esquivar", k.Player.Health);
            Expect(dodged && k.Player.Health == 100, "rodar a tiempo evita el golpe");
        });

        Scenario("Niebla, cartel y despertar del rey", k =>
        {
            Teleport(k, new Vector3(0, 0, -38.8f));
            for (int i = 0; i < 120 && k.Phase == Phase.Explore; i++) k.Update(Kingdom.FixedStep, new Controls { Move = new Vector2(0, 1) });
            Expect(k.Phase == Phase.Intro, "cruzar la niebla (sensor) abre el cartel");
            Simulate(k, 4.5f, default);
            Expect(k.Phase == Phase.Boss && k.King.State != KingState.Asleep, "tras el cartel el rey despierta");
            Expect(k.LockTarget == k.King, "la cámara fija al rey");
            Vector3 before = k.Player.Feet;
            Simulate(k, 1.5f, new Controls { Move = new Vector2(0, -1) });
            Report("z tras intentar huir", k.Player.Feet.Z);
            Expect(k.Player.Feet.Z < -40.3f, "la entrada queda sellada");
        });

        Scenario("Golpe al suelo del rey: Explode mueve escombros", k =>
        {
            GoToBoss(k);
            Teleport(k, new Vector3(-6, 0, -64));
            FallenKing king = k.King;
            king.Body.SetTransform(new Vector3(-6, king.HalfHeight, -58.5f));
            king.Yaw = MathF.PI;
            var rocks = k.Props.Where(p => p.Kind == PropKind.Rock && Vector3.Distance(p.Body.Position, new Vector3(-6, 0, -58)) < 6).ToList();
            var before = rocks.Select(r => r.Body.Position).ToList();
            king.Force(KingState.Slam);
            float hp = k.Player.Health;
            Simulate(k, 2.2f, default);
            float moved = rocks.Select((r, i) => Vector3.Distance(r.Body.Position, before[i])).DefaultIfEmpty(0).Max();
            Report("rocas cerca", rocks.Count);
            Report("máx. desplazamiento de roca", moved);
            Report("vida del caballero", k.Player.Health);
            Expect(moved > 1f, "la explosión lanza los escombros");
            Expect(k.Player.Health < hp, "el golpe o la onda alcanzan al caballero");
        });

        Scenario("El rey ciego derriba columnas", k =>
        {
            GoToBoss(k);
            FallenKing king = k.King;
            var drums = k.Props.Where(p => p.Kind == PropKind.Drum && Vector3.Distance(p.Body.Position with { Y = 0 }, new Vector3(-12, 0, -52)) < 1.5f).ToList();
            float top = drums.Max(d => d.Body.Position.Y);
            king.Body.SetTransform(new Vector3(-12, king.HalfHeight, -58));
            king.Yaw = 0; // mirando a +Z, hacia la columna
            king.Force(KingState.Blind);
            king.WanderDir = Vector3.UnitZ;
            Teleport(k, new Vector3(10, 0, -75));
            Simulate(k, 3.5f, default);
            float after = drums.Max(d => d.Body.Position.Y);
            Report("tambor más alto antes / después", $"{top:F2} / {after:F2}");
            Expect(after < top - 1f, "la columna se derrumba al chocar");
        });

        Scenario("Parry al barrido del rey y segunda fase", k =>
        {
            GoToBoss(k);
            FallenKing king = k.King;
            Teleport(k, new Vector3(0, 0, -69));
            king.Body.SetTransform(new Vector3(0, king.HalfHeight, -74));
            king.Yaw = 0;
            king.Force(KingState.Sweep);
            for (int i = 0; i < 200; i++)
            {
                k.Player.Yaw = Actor.YawOf(king.Body.Position - k.Player.Body.Position);
                bool press = king.State == KingState.Sweep && king.StateTime > FallenKing.SweepWindup - 0.05f && k.Player.State == KnightState.Free;
                k.Update(Kingdom.FixedStep, new Controls { Parry = press });
                if (king.State == KingState.Stagger) break;
            }
            Expect(king.State == KingState.Stagger, "el barrido se puede desviar");
            king.TakeHit(k, king.MaxHealth * 0.55f, heavy: true);
            Expect(king.Phase2 && king.State == KingState.Enrage, "a mitad de vida entra en la segunda fase");
            king.TakeHit(k, king.MaxHealth, heavy: true);
            Simulate(k, 3f, default);
            Expect(k.Phase == Phase.Victory && k.BossDefeated, "derrotarlo da la victoria");
        });

        Scenario("Muerte y vuelta a la hoguera", k =>
        {
            k.Player.TakeHit(k, k.Skeletons[0], 999, k.Player.Body.Position + Vector3.UnitZ, false);
            Simulate(k, 3f, default);
            Expect(k.Phase == Phase.Dead, "morir muestra el cartel");
            Simulate(k, 0.1f, new Controls { Light = true });
            Expect(k.Phase == Phase.Explore && k.Player.Health == 100, "cualquier botón devuelve a la hoguera");
        });

        Scenario("Sonido: la simulación pide cada efecto", k =>
        {
            var heard = new HashSet<Sfx>();
            for (int i = 0; i < 60; i++) { k.Update(Kingdom.FixedStep, new Controls { Move = new Vector2(0, 1) }); foreach (Heard h in k.Sounds) heard.Add(h.Sfx); }
            Teleport(k, new Vector3(-3, 0, -11.4f));
            Skeleton s = k.Skeletons[0];
            for (int i = 0; i < 600 && !s.Dead; i++)
            {
                k.Player.Yaw = Actor.YawOf(s.Body.Position - k.Player.Body.Position);
                k.Player.Health = 100;
                k.Update(Kingdom.FixedStep, new Controls { Light = i % 20 == 0 });
                foreach (Heard h in k.Sounds) heard.Add(h.Sfx);
            }
            Report("sonidos oídos", string.Join(", ", heard));
            Expect(heard.IsSupersetOf([Sfx.Swing, Sfx.Hit, Sfx.Shatter, Sfx.Step]), "tajo, golpe, pasos y huesos que se desmontan");
            Expect(heard.Contains(Sfx.BoneStep) || heard.Contains(Sfx.Rattle), "el esqueleto se oye venir");
        });

        Scenario("Sonido: el golpe del rey retumba y hace sonar la campana", k =>
        {
            GoToBoss(k);
            FallenKing king = k.King;
            king.Body.SetTransform(new Vector3(0, king.HalfHeight, -70));
            king.Force(KingState.Slam);
            var heard = new HashSet<Sfx>();
            for (int i = 0; i < 150; i++) { k.Update(Kingdom.FixedStep, default); foreach (Heard h in k.Sounds) heard.Add(h.Sfx); }
            Expect(heard.Contains(Sfx.Slam) && heard.Contains(Sfx.Bell), "¡bum! y la campana");
        });

        Scenario("Sintetizador: todo suena, nada satura", k =>
        {
            int silent = 0, broken = 0;
            foreach (Sfx sfx in Enum.GetValues<Sfx>())
                for (int v = 0; v < Synth.Variants(sfx); v++)
                {
                    float[] w = Synth.Make(sfx, v);
                    if (w.Any(x => !float.IsFinite(x) || MathF.Abs(x) > 1)) broken++;
                    if (w.Max(MathF.Abs) < 0.1f) silent++;
                }
            float[] waltz = Synth.Waltz();
            Report("duración del vals (s)", waltz.Length / (float)Synth.Rate);
            Expect(broken == 0 && silent == 0, "cada efecto tiene señal, finita y sin recortar");
            Expect(MathF.Abs(waltz[0] - waltz[^1]) < 0.05f, "el vals empalma su final con el principio");
        });

        Console.WriteLine(_failures == 0 ? "\nTODO OK" : $"\n{_failures} comprobaciones fallidas");
        return _failures == 0 ? 0 : 1;
    }

    static void GoToBoss(Kingdom k)
    {
        Teleport(k, new Vector3(0, 0, -38.8f));
        for (int i = 0; i < 120 && k.Phase == Phase.Explore; i++) k.Update(Kingdom.FixedStep, new Controls { Move = new Vector2(0, 1) });
        Simulate(k, 4.5f, default);
    }

    static void Teleport(Kingdom k, Vector3 feet)
    {
        k.Player.Body.SetTransform(feet + new Vector3(0, k.Player.HalfHeight + 0.05f, 0));
        k.Player.Body.IsAwake = true;
    }

    static void Scenario(string name, Action<Kingdom> body)
    {
        Console.WriteLine($"\n== {name}");
        using var k = new Kingdom();
        try { body(k); }
        catch (Exception e) { Console.WriteLine($"   [FALLO] excepción: {e.GetType().Name}: {e.Message}"); _failures++; }
    }

    static void Simulate(Kingdom k, float seconds, Controls c)
    {
        for (int i = 0; i < (int)(seconds / Kingdom.FixedStep); i++) k.Update(Kingdom.FixedStep, c);
    }

    static void Report(string what, object value) =>
        Console.WriteLine($"   {what,-34}: {(value is float f ? f.ToString("F2") : value)}");

    static void Expect(bool ok, string what)
    {
        Console.WriteLine($"   [{(ok ? "OK" : "FALLO")}] {what}");
        if (!ok) _failures++;
    }
}
