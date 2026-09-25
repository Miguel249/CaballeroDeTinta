using System.Numerics;
using CaballeroDeTinta;
using CaballeroDeTinta.Art;
using CaballeroDeTinta.Sim;
using CaballeroDeTinta.View;
using Raylib_cs;

// Uso:
//   dotnet run                          -> jugar
//   dotnet run -- --test                -> pruebas de la simulación, sin ventana
//   dotnet run -- --shot out.png <0-10> -> escena automática y captura (8-10: smears y múltiplos)
//   dotnet run -- --trace <0-10>        -> la misma escena sin ventana, imprimiendo el estado
//   dotnet run -- --wav <carpeta>       -> exporta todos los sonidos y la música sintetizados
if (args.Contains("--test"))
    return Tests.Run();

if (args.Length >= 2 && args[0] == "--wav")
{
    Directory.CreateDirectory(args[1]);
    foreach (Sfx sfx in Enum.GetValues<Sfx>())
        for (int v = 0; v < Synth.Variants(sfx); v++)
            Synth.SaveWav(Path.Combine(args[1], $"{sfx}{(v > 0 ? v : "")}.wav"), Synth.Make(sfx, v));
    Synth.SaveWav(Path.Combine(args[1], "_Vals.wav"), Synth.Waltz());
    Synth.SaveWav(Path.Combine(args[1], "_Galope.wav"), Synth.Gallop(false));
    Synth.SaveWav(Path.Combine(args[1], "_Galope2.wav"), Synth.Gallop(true));
    Synth.SaveWav(Path.Combine(args[1], "_Cartel.wav"), Synth.Stinger());
    Synth.SaveWav(Path.Combine(args[1], "_Victoria.wav"), Synth.Fanfare());
    Synth.SaveWav(Path.Combine(args[1], "_Proyector.wav"), Synth.Projector());
    Synth.SaveWav(Path.Combine(args[1], "_Hoguera.wav"), Synth.Fire());
    Console.WriteLine($"Sonidos en {Path.GetFullPath(args[1])}");
    return 0;
}

if (args.Length >= 2 && args[0] == "--trace")
{
    // Reproduce una escena grabada sin ventana e imprime el estado.
    using var tk = new Kingdom();
    var script = new ShotScript(tk, int.Parse(args[1]));
    for (int i = 0; !script.Finished; i++)
    {
        tk.Update(Kingdom.FixedStep, script.Next());
        if (i % 10 == 0) Console.WriteLine($"f={i} fase={tk.Phase} hitstop={tk.Hitstop:F2} caballero={tk.Player.State}/{tk.Player.StateTime:F2} pies={tk.Player.Feet} esqueleto={tk.Skeletons[0].State} {tk.Skeletons[0].Feet}");
    }
    return 0;
}

string? shotPath = args.Length >= 2 && args[0] == "--shot" ? args[1] : null;
int scene = args.Length >= 3 && int.TryParse(args[2], out int s) ? s : 0;

Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
Raylib.InitWindow(1280, 720, "El Caballero de Tinta - Box3D.NET");
Raylib.SetTargetFPS(144);
if (shotPath == null) Raylib.DisableCursor();

// Sin sonido en las capturas automáticas, ni si no hay dispositivo de audio.
if (shotPath == null) Raylib.InitAudioDevice();
Soundtrack? soundtrack = Raylib.IsAudioDeviceReady() ? new Soundtrack() : null;

using (var kingdom = new Kingdom())
using (var view = new KingdomView())
{
    var shot = shotPath == null ? null : new ShotScript(kingdom, scene);
    while (!Raylib.WindowShouldClose())
    {
        Controls input = shot?.Next() ?? ReadControls();
        float dt = shot != null ? Kingdom.FixedStep : MathF.Min(Raylib.GetFrameTime(), 0.05f);
        kingdom.Update(dt, input);

        Raylib.BeginDrawing();
        view.Override = shot?.Camera;
        view.Draw(kingdom);
        Raylib.EndDrawing();
        soundtrack?.Update(kingdom, view.Camera);

        if (shot is { Finished: true })
        {
            Raylib.TakeScreenshot(shotPath!);
            break;
        }
    }
}

soundtrack?.Dispose();
if (Raylib.IsAudioDeviceReady()) Raylib.CloseAudioDevice();
Raylib.CloseWindow();
return 0;

static Controls ReadControls()
{
    Vector2 move = Vector2.Zero;
    if (Raylib.IsKeyDown(KeyboardKey.W)) move.Y += 1;
    if (Raylib.IsKeyDown(KeyboardKey.S)) move.Y -= 1;
    if (Raylib.IsKeyDown(KeyboardKey.D)) move.X += 1;
    if (Raylib.IsKeyDown(KeyboardKey.A)) move.X -= 1;
    return new Controls
    {
        Move = move,
        Look = Raylib.GetMouseDelta(),
        Light = Raylib.IsMouseButtonPressed(MouseButton.Left),
        Heavy = Raylib.IsMouseButtonPressed(MouseButton.Right),
        Dodge = Raylib.IsKeyPressed(KeyboardKey.Space),
        Parry = Raylib.IsKeyPressed(KeyboardKey.F),
        Heal = Raylib.IsKeyPressed(KeyboardKey.Q),
        Interact = Raylib.IsKeyPressed(KeyboardKey.E),
        LockOn = Raylib.IsKeyPressed(KeyboardKey.Tab) || Raylib.IsMouseButtonPressed(MouseButton.Middle),
    };
}

namespace CaballeroDeTinta
{
    /// <summary>Escenas grabadas para sacar capturas sin intervención.</summary>
    sealed class ShotScript(Kingdom k, int scene)
    {
        int _frame;
        public Camera3D? Camera { get; private set; }
        public bool Finished => _frame >= Length;

        int Length => scene switch { 1 => 150, 2 => 170, 3 => 394, 7 => 425, 4 => 36, 5 => 330, 6 => 48, 8 => 25, 9 => 18, 10 => 73, _ => 90 };

        static Camera3D Look(Vector3 from, Vector3 to, float fov = 55) => new(from, to, Vector3.UnitY, fov, CameraProjection.Perspective);

        public Controls Next()
        {
            int f = _frame++;
            switch (scene)
            {
                case 0: // la hoguera, al empezar
                    k.CamYaw = MathF.PI * 0.8f;
                    return new Controls { Interact = f == 20 };

                case 1: // patio: el caballero contra un esqueleto
                    if (f == 0) { Teleport(new Vector3(-2.5f, 0, -9.5f)); k.Player.Yaw = MathF.PI; }
                    Camera = Look(new Vector3(6, 3.2f, -4), new Vector3(-2.5f, 1.2f, -12));
                    return new Controls { Light = f is 95 or 110, LockOn = f == 60 };

                case 2: // cartel de presentación del jefe
                    if (f == 0) Teleport(new Vector3(0, 0, -38.8f));
                    return new Controls { Move = new Vector2(0, 1) };

                case 3: // combate: Baldomero levanta la espada
                case 7: // y después del golpe
                    if (f == 0) Teleport(new Vector3(0, 0, -39.2f));
                    if (f == 330) { Teleport(new Vector3(2.5f, 0, -65.5f)); k.King.Body.SetTransform(new Vector3(0, k.King.HalfHeight, -72)); k.King.Yaw = 0; k.King.Force(KingState.Slam); }
                    if (f > 330) Camera = Look(new Vector3(12, 5.5f, -57), new Vector3(0, 3.0f, -72), 60);
                    return new Controls { Move = f < 45 ? new Vector2(0, 1) : default };

                case 4: // anticipación del ataque fuerte
                case 6: // y el impacto
                    if (f == 0) { Teleport(new Vector3(-2.8f, 0, -10.8f)); k.Player.Yaw = MathF.PI; }
                    Camera = Look(new Vector3(1.5f, 2.2f, -9.5f), new Vector3(-2.8f, 1.1f, -12.3f), 50);
                    return new Controls { Heavy = f == 12 };

                case 8: // smear del tajo ligero, contra el aire
                    if (f == 0) { Teleport(new Vector3(0, 0, -3)); k.Player.Yaw = MathF.PI; }
                    Camera = Look(new Vector3(1.6f, 2.1f, -6.2f), new Vector3(0, 1.0f, -3.4f), 50);
                    return new Controls { Light = f == 12 };

                case 9: // múltiplos al rodar
                    if (f == 0) { Teleport(new Vector3(-1.5f, 0, -3)); k.Player.Yaw = MathF.PI / 2; }
                    Camera = Look(new Vector3(0, 1.9f, 1.8f), new Vector3(0, 0.9f, -3), 50);
                    return new Controls { Dodge = f == 12, Move = f >= 11 ? new Vector2(1, 0) : default };

                case 10: // smear del barrido del rey
                    if (f == 0) { Teleport(new Vector3(0, 0, -64.5f)); k.King.Body.SetTransform(new Vector3(0, k.King.HalfHeight, -71)); k.King.Yaw = 0; k.King.Force(KingState.Sweep); }
                    Camera = Look(new Vector3(10, 6.5f, -60), new Vector3(0, 2.5f, -70.5f), 60);
                    return default;

                default: // cartel de derrota
                    if (f == 10) k.Player.TakeHit(k, k.Skeletons[0], 999, k.Player.Body.Position + Vector3.UnitZ, false);
                    return default;
            }
        }

        void Teleport(Vector3 feet)
        {
            k.Player.Body.SetTransform(feet + new Vector3(0, k.Player.HalfHeight + 0.05f, 0));
            k.Player.Body.IsAwake = true;
        }
    }
}
