using System.Numerics;
using CaballeroDeTinta;
using CaballeroDeTinta.Art;
using CaballeroDeTinta.Sim;
using CaballeroDeTinta.View;
using Raylib_cs;

// Uso:
//   dotnet run                          -> jugar
//   dotnet run -- --test                -> pruebas de la simulación, sin ventana
//   dotnet run -- --shot out.png <0-21> [AnchoxAlto] -> escena automática y captura (8-10: smears y múltiplos, 11-19: interfaz, 20-21: esqueleto, 22-23: desvío y curación)
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
        if (i % 10 == 0) Console.WriteLine($"f={i} fase={tk.Phase} hitstop={tk.Hitstop:F2} caballero={tk.Player.State}/{tk.Player.StateTime:F2} pies={tk.Player.Feet} esqueleto={tk.Skeletons[0].State} {tk.Skeletons[0].Feet} fijado={tk.LockTarget?.GetType().Name ?? "-"} vida={tk.Player.Health:F0}");
    }
    return 0;
}

string? shotPath = args.Length >= 2 && args[0] == "--shot" ? args[1] : null;
int scene = args.Length >= 3 && int.TryParse(args[2], out int s) ? s : 0;
// Tamaño opcional de la captura ("1920x1080") para revisar la interfaz en otras resoluciones.
int[] size = args.Length >= 4 && args[3].Split('x') is [var sw, var sh] && int.TryParse(sw, out int w) && int.TryParse(sh, out int h) ? [w, h] : [1280, 720];

Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
Raylib.InitWindow(size[0], size[1], "El Caballero de Tinta - Box3D.NET");
Raylib.SetTargetFPS(144);
// Esc abre la pausa en vez de cerrar la ventana.
if (shotPath == null) Raylib.SetExitKey(KeyboardKey.Null);

// Sin sonido en las capturas automáticas, ni si no hay dispositivo de audio.
if (shotPath == null) Raylib.InitAudioDevice();
Soundtrack? soundtrack = Raylib.IsAudioDeviceReady() ? new Soundtrack() : null;
// Las capturas usan ajustes de fábrica y no escriben nada en disco.
Settings settings = shotPath == null ? Settings.Load() : new Settings();

using (var ui = new Ui())
using (var kingdom = new Kingdom())
using (var view = new KingdomView(ui, settings))
{
    var shot = shotPath == null ? null : new ShotScript(kingdom, scene);
    var front = new Frontend(ui, settings);
    bool inMenu = shot == null || ShotScript.StartsInMenu(scene), quit = false;
    if (inMenu) front.OpenMain(completed: scene == 12);
    else Raylib.DisableCursor();

    // Pausa: el juego se congela al instante; el iris y el menú entran en ~0,2 s.
    const float PauseTime = 0.2f;
    bool paused = false;
    float pause = 0;
    int skipLook = 0;

    // Transiciones entre pantallas: el iris se cierra del todo, se hace el cambio y se vuelve a abrir.
    float iris = shot == null ? 1 : 0;
    Action? onClosed = null;
    void IrisTo(Action change) => onClosed ??= change;
    void ToMenu(bool completed)
    {
        kingdom.NewGame();
        view.NewGame();
        front.OpenMain(completed);
        inMenu = true;
        paused = false;
        pause = 0;
        Raylib.EnableCursor();
    }
    void Pause()
    {
        paused = true;
        front.OpenPause();
        front.Sounds.Add(UiSfx.Open);
        Raylib.EnableCursor();
    }
    void Resume()
    {
        paused = false;
        front.Close();
        Raylib.DisableCursor();
        skipLook = 3; // el primer delta del ratón tras capturarlo salta
    }

    while (!quit && !Raylib.WindowShouldClose())
    {
        float dt = shot != null ? Kingdom.FixedStep : MathF.Min(Raylib.GetFrameTime(), 0.05f);
        ui.BeginFrame(dt);
        Controls scripted = shot?.Next() ?? default;
        if (shot != null) ScriptFrontend(shot.Frame);
        bool busy = onClosed != null;
        kingdom.ShatterIntoBones = !view.SkeletonModels;
        UiInput uiInput = shot != null || busy ? default : UiInput.Read();

        if (inMenu)
        {
            switch (front.Update(uiInput, dt))
            {
                case FrontCmd.Start:
                    IrisTo(() =>
                    {
                        kingdom.NewGame();
                        view.NewGame();
                        front.Close();
                        inMenu = false;
                        Raylib.DisableCursor();
                        skipLook = 3;
                    });
                    break;
                case FrontCmd.Quit:
                    quit = true;
                    break;
            }
            // La cámara gira despacio alrededor de la hoguera.
            kingdom.Update(dt, new Controls { Look = new Vector2(-dt * 30, 0) });
        }
        else if (paused)
        {
            switch (front.Update(uiInput, dt))
            {
                case FrontCmd.Resume: Resume(); break;
                case FrontCmd.ToMenu: IrisTo(() => ToMenu(false)); break;
            }
        }
        else if (pause <= 0)
        {
            Controls input = shot != null ? scripted : busy ? default : ReadControls(settings.MouseSensitivity, ref skipLook);
            if (shot == null && !busy)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.Escape)) Pause();
                // Tras el cartel de victoria, de vuelta al menú con la demo completada.
                else if (kingdom.Phase == Phase.Victory && kingdom.PhaseTime > 6.5f) IrisTo(() => ToMenu(true));
            }
            if (!paused) kingdom.Update(dt, input);
        }
        pause = Ui.Approach(pause, paused ? 1 : 0, dt / PauseTime);

        if (onClosed != null)
        {
            iris = MathF.Min(1, iris + dt / (kingdom.Phase == Phase.Victory ? 1.4f : 0.55f));
            if (iris >= 1) { onClosed(); onClosed = null; }
        }
        else iris = MathF.Max(0, iris - dt / 0.7f);

        Raylib.BeginDrawing();
        view.Override = shot?.Camera;
        view.Backdrop = inMenu;
        view.Pause = inMenu ? 0 : Ui.Smooth(pause);
        // En pausa el HUD no avanza: las animaciones quedan donde estaban.
        view.Draw(kingdom, paused || pause > 0 ? 0 : dt);
        if (!inMenu && pause > 0)
        {
            // Iris de pausa: se cierra un poco, sin llegar a tapar, y el menú aparece dentro.
            float p = Ui.Smooth(pause);
            ui.Iris(ui.Center, ui.IrisOpen + (MathF.Min(ui.W, ui.H) * 0.62f - ui.IrisOpen) * p, Ui.A(new Color(8, 6, 6, 255), 0.92f), 0.018f);
            Raylib.DrawRectangle(0, 0, (int)ui.W, (int)ui.H, Ui.A(Palette.Ink, 0.3f * p));
        }
        front.Draw(inMenu ? 1 : Ui.Smooth((pause - 0.35f) / 0.65f));
        if (iris > 0)
        {
            float closed = Ui.Smooth(iris);
            ui.Iris(ui.Center, ui.IrisOpen * (1 - closed), new Color(6, 5, 5, 255), 0.02f);
            if (closed > 0.995f) Raylib.DrawRectangle(0, 0, (int)ui.W, (int)ui.H, new Color(6, 5, 5, 255));
        }
        Raylib.EndDrawing();

        if (soundtrack != null)
        {
            soundtrack.MusicVolume = settings.Music;
            soundtrack.EffectsVolume = settings.Effects;
            soundtrack.Paused = !inMenu && (paused || pause > 0);
            foreach (UiSfx u in front.Sounds) soundtrack.PlayUi(u);
            soundtrack.Update(kingdom, view.Camera);
        }
        front.Sounds.Clear();

        if (shot is { Finished: true })
        {
            Raylib.TakeScreenshot(shotPath!);
            break;
        }
    }

    // Capturas de la interfaz: abre las pantallas que cada escena necesita en el fotograma justo.
    void ScriptFrontend(int f)
    {
        switch (scene)
        {
            case 13 or 16 when f == 30: Pause(); break;
            case 14 when f == 1: front.ShowControls(); break;
            case 15 when f == 1: front.ShowOptions(); break;
        }
        if (scene == 16 && f == 45) front.AskToMenu();
    }
}

soundtrack?.Dispose();
settings.Save();
if (Raylib.IsAudioDeviceReady()) Raylib.CloseAudioDevice();
Raylib.CloseWindow();
return 0;

static Controls ReadControls(float sensitivity, ref int skipLook)
{
    Vector2 move = Vector2.Zero;
    if (Raylib.IsKeyDown(KeyboardKey.W)) move.Y += 1;
    if (Raylib.IsKeyDown(KeyboardKey.S)) move.Y -= 1;
    if (Raylib.IsKeyDown(KeyboardKey.D)) move.X += 1;
    if (Raylib.IsKeyDown(KeyboardKey.A)) move.X -= 1;
    Vector2 look = skipLook > 0 ? Vector2.Zero : Raylib.GetMouseDelta() * sensitivity;
    if (skipLook > 0) skipLook--;
    return new Controls
    {
        Move = move,
        Look = look,
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
        public int Frame => _frame - 1;
        public Camera3D? Camera { get; private set; }
        public bool Finished => scene switch
        {
            // Justo en mitad del tajo del esqueleto: descenso y smear a la vez.
            20 => _frame > 5 && k.Skeletons[0] is { State: FoeState.Windup, StateTime: >= 0.66f } || _frame > 400,
            _ => _frame >= Length,
        };

        int Length => scene switch { 1 => 150, 2 => 170, 3 => 394, 7 => 425, 4 => 36, 5 => 330, 6 => 48, 8 => 25, 9 => 18, 10 => 73, 13 => 60, 16 => 70, 17 => 82, 18 => 130, 19 => 330, 21 => 170, 22 => 17, 23 => 42, _ => 90 };

        /// <summary>Escenas que empiezan en el menú principal (11, 12, 14, 15).</summary>
        public static bool StartsInMenu(int scene) => scene is 11 or 12 or 14 or 15;

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

                case 11: // menú principal
                case 12: // y al completar la demo
                case 13: // pausa
                case 14: // controles
                case 15: // opciones
                case 16: // confirmación de volver al menú
                    return default;

                case 17: // HUD en combate: poca vida, aguante agotado, un frasco gastado y objetivo fijado
                    if (f == 0) { Teleport(new Vector3(-2.5f, 0, -9.5f)); k.Player.Yaw = MathF.PI; k.Player.Health = 22; k.Player.Flasks = 2; }
                    if (f == 70) k.Player.Stamina = 0;
                    Camera = Look(new Vector3(-0.5f, 2.6f, -5.5f), new Vector3(-2.8f, 1.2f, -11.5f));
                    return new Controls { LockOn = f == 60 };

                case 18: // al empezar: título de la zona e indicación de la hoguera
                    k.CamYaw = MathF.PI * 0.8f;
                    return default;

                case 19: // primer consejo: movimiento
                    return default;

                case 22: // desvío con la ventana abierta
                case 23: // bebiendo un frasco de brasa
                    Camera = Look(new Vector3(1.1f, 1.5f, 2.6f), new Vector3(0, 0.8f, 4.8f), 45);
                    return new Controls { Parry = scene == 22 && f == 10, Heal = scene == 23 && f == 10 };

                case 20: // el esqueleto descarga el tajo
                case 21: // y se desploma al morir
                    if (f == 0) { Teleport(new Vector3(-2.5f, 0, -9.5f)); k.Player.Yaw = MathF.PI; }
                    if (scene == 21 && f == 10) k.Skeletons[0].TakeHit(k, 999, k.Player.Body.Position, heavy: true);
                    // De perfil: el arco de la hoja se ve entero.
                    Camera = Look(new Vector3(2.6f, 1.7f, -10.9f), new Vector3(-2.6f, 1.2f, -10.6f), 50);
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
