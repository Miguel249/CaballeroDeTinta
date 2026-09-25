using System.Numerics;
using CaballeroDeTinta.Art;
using CaballeroDeTinta.Sim;
using Raylib_cs;

namespace CaballeroDeTinta.View;

/// <summary>Cómo se anuncia un jefe: cartel de presentación y barra de vida.</summary>
readonly record struct BossBill(string Overline, string Name, string Epithet)
{
    public static readonly BossBill Baldomero = new("Capítulo I · La Sala de las Campanas", "Baldomero III", "el Rey que se Desploma");
}

/// <summary>
/// Algo que el caballero puede hacer donde está: dónde anclar la indicación, la tecla y el verbo.
/// Hoy la simulación solo tiene "Descansar"; Hablar, Abrir o Recoger usarán este mismo dibujo.
/// </summary>
readonly record struct Cue(Vector3 Anchor, string Key, string Verb);

/// <summary>
/// La interfaz durante el juego. Lee el reino y lo presenta: nunca cambia la simulación.
/// Guarda solo estado de presentación (rastros de daño, fundidos, qué zona ya se anunció...).
/// </summary>
sealed class Hud(Ui ui, Settings settings)
{
    /// <summary>Rótulo breve del jefe antes de que entre su barra, y cuándo empieza a subir la barra.</summary>
    const float BillLength = 1.75f, BarDelay = 1.3f;

    readonly Tutorial _tutorial = new(ui, settings);

    // Vida y aguante.
    Knight? _knight;
    float _hp, _hpFill, _hpLag, _hpHold, _hurtT = 9, _healT = 9, _hpSeen;
    float _stamina, _staminaAlpha = 1, _staminaFull, _exhaustT = 9;
    bool _recovering;

    // Frascos: tiempo desde que se vació o se llenó cada uno.
    readonly float[] _drained = Enumerable.Repeat(9f, 8).ToArray();
    readonly float[] _filled = Enumerable.Repeat(9f, 8).ToArray();
    int _flasks;

    // Jefe.
    float _bossFill = 1, _bossLag = 1, _bossHold, _bossReveal, _bossAlpha;
    // El rótulo se ve la primera vez en cada partida; al volver tras morir, la barra entra directamente.
    bool _bossBilled, _billing;

    // Indicaciones, avisos, zonas, objetos.
    float _prompt, _toastAge, _lastToastTime;
    Cue _cue;
    string _toast = "";
    string? _areaTitle, _areaSubtitle;
    float _areaT = 99;
    readonly HashSet<string> _areas = [];
    float _itemT = 99;
    int _itemCount;
    float _riposte;

    // Fijación de objetivo.
    Actor? _lock;
    Vector3 _lockAt, _lockTop, _lockBottom;
    float _lockDraw, _lostT = 9, _lockRadius;
    Vector2 _lostScreen;

    float _clock;

    /// <summary>Tinte que el HUD pide a la película: el mundo pierde color al morir.</summary>
    public float Desaturate { get; private set; }
    /// <summary>Viñeta de tinta: muy poca vida, muerte.</summary>
    public float Vignette { get; private set; }

    public void Reset()
    {
        _knight = null;
        _areas.Clear();
        _areaT = _itemT = 99;
        _lock = null;
        _lostT = 9;
        _clock = 0;
        _bossReveal = _bossAlpha = 0;
        _bossBilled = _billing = false;
        _tutorial.Reset();
    }

    // ================================================================== estado

    public void Update(Kingdom k, float dt)
    {
        _clock += dt;
        Knight p = k.Player;
        float hp = p.Health / p.MaxHealth, st = p.Stamina / p.MaxStamina;

        if (!ReferenceEquals(p, _knight))
        {
            // Caballero nuevo (partida nueva o vuelta a la hoguera): sin animaciones de arrastre.
            bool sameGame = _knight != null;
            _knight = p;
            _hp = _hpFill = _hpLag = _hpSeen = hp;
            _stamina = st;
            _flasks = p.Flasks;
            _recovering = false;
            if (!sameGame) _clock = 0;
        }

        // Vida: el daño se ve al instante; el rastro claro espera y luego retrocede.
        if (hp < _hpSeen - 1e-4f) { _hpHold = 0.5f; _hurtT = 0; _hpFill = hp; }
        else if (hp > _hpSeen + 1e-4f) _healT = 0;
        _hpSeen = hp;
        _hp = hp;
        _hpFill = hp < _hpFill ? hp : Ui.Approach(_hpFill, hp, dt * 0.9f); // la curación devuelve el pigmento poco a poco
        if (_hpLag > hp)
        {
            if (_hpHold > 0) _hpHold -= dt;
            else _hpLag = MathF.Max(hp, _hpLag - MathF.Max(0.25f, (_hpLag - hp) * 3) * dt);
        }
        else _hpLag = hp;
        _hurtT += dt;
        _healT += dt;

        // Aguante: sigue al valor real con suavidad; agotado queda rayado hasta recuperarse.
        _stamina += (st - _stamina) * (1 - MathF.Exp(-dt * 14));
        if (st <= 0.005f && !_recovering) { _recovering = true; _exhaustT = 0; }
        if (st > 0.3f) _recovering = false;
        _exhaustT += dt;
        _staminaFull = st >= 0.999f && !_recovering ? _staminaFull + dt : 0;
        _staminaAlpha = Ui.Approach(_staminaAlpha, _staminaFull > 1.2f ? 0.35f : 1f, dt * 3);

        // Frascos.
        for (int i = p.Flasks; i < _flasks && i < _drained.Length; i++) _drained[i] = 0;
        if (p.Flasks > _flasks)
        {
            for (int i = _flasks; i < p.Flasks && i < _filled.Length; i++) _filled[i] = 0;
            _itemT = 0;
            _itemCount = p.Flasks;
        }
        _flasks = p.Flasks;
        for (int i = 0; i < _drained.Length; i++) { _drained[i] += dt; _filled[i] += dt; }
        _itemT += dt;
        _riposte = Ui.Approach(_riposte, p.Riposte > 0 ? 1 : 0, dt / 0.15f);

        // Jefe.
        FallenKing king = k.King;
        float boss = king.Health / king.MaxHealth;
        if (k.Phase == Phase.Boss)
        {
            if (_bossReveal == 0)
            {
                _bossFill = _bossLag = boss;
                _billing = !_bossBilled;
                _bossBilled = true;
            }
            _bossReveal += dt;
        }
        else if (k.Phase != Phase.Victory) _bossReveal = 0;
        if (boss < _bossFill - 1e-4f) { _bossHold = 0.35f; }
        _bossFill = boss;
        if (_bossLag > boss)
        {
            if (_bossHold > 0) _bossHold -= dt;
            else _bossLag = MathF.Max(boss, _bossLag - MathF.Max(0.12f, (_bossLag - boss) * 2) * dt);
        }
        else _bossLag = boss;
        _bossAlpha = Ui.Approach(_bossAlpha, k.Phase == Phase.Boss && !king.Dead ? 1 : 0, dt / (king.Dead ? 0.8f : 0.3f));

        // Indicación contextual: al desvanecerse conserva el último verbo.
        Cue? cue = CueOf(k);
        if (cue is { } c) _cue = c;
        _prompt = Ui.Approach(_prompt, cue != null ? 1 : 0, dt / (cue != null ? 0.16f : 0.1f));

        // Avisos de la simulación: se reinicia el fundido si el texto cambia o se vuelve a decir.
        if (k.ToastTime > 0 && (k.Toast != _toast || k.ToastTime > _lastToastTime + 0.01f)) { _toast = k.Toast; _toastAge = 0; }
        _lastToastTime = k.ToastTime;
        _toastAge += dt;

        // Zonas: cada una se anuncia una vez por partida.
        if (k.Phase == Phase.Explore && _clock > 0.6f)
        {
            float z = p.Feet.Z;
            if (z > 1.5f) Announce("capilla", "La Capilla en Ruinas", "donde arde la última hoguera");
            else if (z < -1.5f && z > -39f) Announce("patio", "El Patio de los Huesos", "Reino de las Campanas");
        }
        _areaT += dt;

        // Fijación.
        if (!ReferenceEquals(k.LockTarget, _lock))
        {
            if (_lock != null) { _lostT = 0; _lostScreen = Vector2.Zero; }
            _lock = k.LockTarget;
            _lockDraw = 0;
        }
        if (_lock != null)
        {
            _lockAt = _lock.Body.Position + new Vector3(0, _lock.HalfHeight * 0.2f, 0);
            _lockTop = _lock.Body.Position + new Vector3(0, _lock.HalfHeight, 0);
            _lockBottom = _lock.Body.Position - new Vector3(0, _lock.HalfHeight, 0);
            _lockDraw = MathF.Min(1, _lockDraw + dt / 0.13f);
        }
        _lostT += dt;

        // Tinte de la película.
        float low = p.Dead ? 0 : Math.Clamp((0.3f - hp) / 0.3f, 0, 1);
        float pulse = 0.5f + 0.5f * MathF.Sin(_clock * MathF.Tau / 2.4f);
        float death = k.Phase == Phase.Dead ? Ui.Smooth(k.PhaseTime / 1.4f) : 0;
        Desaturate = MathF.Max(death, low * 0.25f);
        Vignette = MathF.Max(death * 0.8f, low * (0.18f + 0.12f * pulse));

        _tutorial.Update(k, dt);
    }

    /// <summary>Lo que se puede hacer ahora mismo. Nuevas interacciones de la simulación se añaden aquí.</summary>
    static Cue? CueOf(Kingdom k)
    {
        Knight p = k.Player;
        if (k.Phase is Phase.Explore && !p.Dead && p.State != KnightState.Rest && Vector3.Distance(p.Feet, k.Bonfire) < 2.4f)
            return new Cue(k.Bonfire + new Vector3(0, 1.9f, 0), "E", "Descansar");
        return null;
    }

    void Announce(string id, string title, string subtitle)
    {
        if (!_areas.Add(id)) return;
        _areaTitle = title;
        _areaSubtitle = subtitle;
        _areaT = 0;
    }

    // ================================================================== dibujo

    /// <summary>Lo que pertenece a la película (recibe grano y sepia): la marca de fijación.</summary>
    public void DrawInFilm(Camera3D cam)
    {
        float s = ui.S;
        if (_lock != null && InFront(_lockAt, cam))
        {
            Vector2 c = Raylib.GetWorldToScreen(_lockAt, cam);
            // El círculo abraza al objetivo: su tamaño sale de la altura que ocupa en pantalla.
            float tall = Vector2.Distance(Raylib.GetWorldToScreen(_lockTop, cam), Raylib.GetWorldToScreen(_lockBottom, cam));
            _lockRadius = Math.Clamp(tall * 0.36f, 16 * s, 110 * s);
            _lostScreen = c;
            LockMark(c, _lockRadius, Ui.Smooth(_lockDraw), 0);
        }
        else if (_lostT < 0.22f && _lostScreen != Vector2.Zero)
            LockMark(_lostScreen, _lockRadius, 1, _lostT / 0.22f);
    }

    /// <summary>Círculo de tinta abierto con un rombo-ojo en el centro. <paramref name="broken"/> lo rompe y lo dispersa.</summary>
    void LockMark(Vector2 c, float r, float drawn, float broken)
    {
        float s = ui.S;
        float a = 1 - broken;
        // Giro lento y a saltos de fotograma: tinta que respira, no una mira que gira.
        float spin = ui.Frame / 12f * 0.16f;
        float wobble = ui.Jit(17) * 0.6f * s;
        // El trazo engorda un poco con objetivos grandes, sin volverse tosco en los pequeños.
        float k = Math.Clamp(r / (60 * s), 0.8f, 1.25f);
        const float gap = 0.9f;               // radianes sin tinta: el trazo no se cierra
        float sweep = (MathF.Tau - gap) * drawn;
        int pieces = broken > 0 ? 3 : 1;
        for (int piece = 0; piece < pieces; piece++)
        {
            float from = spin + piece * sweep / pieces, to = spin + (piece + 1) * sweep / pieces - (broken > 0 ? 0.25f : 0);
            float mid = (from + to) / 2;
            Vector2 push = new Vector2(MathF.Cos(mid), MathF.Sin(mid)) * broken * 14 * s;
            Arc(c + push, r + wobble, from, to, 4.4f * s * k, Ui.A(Palette.Parchment, 0.85f * a));
            Arc(c + push, r + wobble, from, to, 2.3f * s * k, Ui.A(Palette.Ink, a));
        }
        if (broken > 0) return;
        // La pluma se levanta dejando una gota al final del trazo.
        if (drawn > 0.98f)
        {
            float end = spin + sweep;
            Vector2 tip = c + new Vector2(MathF.Cos(end), MathF.Sin(end)) * (r + wobble);
            Raylib.DrawCircleV(tip, 2.6f * s * k, Ui.A(Palette.Parchment, 0.85f));
            Raylib.DrawCircleV(tip, 1.7f * s * k, Palette.Ink);
        }
        float d = 5 * s * drawn;
        Raylib.DrawPoly(c, 4, d + 2 * s, 45, Ui.A(Palette.Parchment, 0.9f));
        Raylib.DrawPoly(c, 4, d, 45, Palette.Ink);
        Raylib.DrawCircleV(c, 1.4f * s, Palette.Parchment);
    }

    static void Arc(Vector2 c, float r, float from, float to, float thick, Color color)
    {
        int n = Math.Max(2, (int)((to - from) * 10));
        Vector2 prev = c + new Vector2(MathF.Cos(from), MathF.Sin(from)) * r;
        for (int i = 1; i <= n; i++)
        {
            float ang = from + (to - from) * i / n;
            // Trazo de pluma: más grueso en medio, fino en las puntas.
            float taper = MathF.Sin(MathF.PI * i / n) * 0.6f + 0.4f;
            Vector2 p = c + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r;
            Raylib.DrawLineEx(prev, p, thick * taper, color);
            prev = p;
        }
    }

    static bool InFront(Vector3 p, Camera3D cam) => Vector3.Dot(p - cam.Position, Vector3.Normalize(cam.Target - cam.Position)) > 0.5f;

    /// <summary>La interfaz encima de la película. <paramref name="alpha"/> baja con la pausa.</summary>
    public void Draw(Kingdom k, Camera3D cam, float alpha)
    {
        if (k.Phase is Phase.Intro || alpha <= 0.01f) return;
        float endFade = k.Phase switch
        {
            Phase.Dead => 1 - Ui.Smooth(k.PhaseTime / 0.6f),
            Phase.Victory => 1 - Ui.Smooth(k.PhaseTime / 0.8f),
            _ => 1,
        };
        float a = alpha * endFade;
        if (a > 0.01f)
        {
            DrawVitals(k.Player, a);
            DrawBoss(k.King, a);
            DrawPrompt(cam, a);
            DrawArea(a);
            DrawToast(a);
            DrawItem(a);
            // Con la barra del jefe abajo, el consejo sube para no taparla.
            float bar = _bossAlpha * Ui.Smooth((_bossReveal - (_billing ? BarDelay : 0)) / 0.45f);
            _tutorial.Draw(a, 78 * ui.S * bar);
        }
    }

    void DrawVitals(Knight p, float a)
    {
        float s = ui.S, m = ui.Margin;
        var crest = new Vector2(m + 18 * s, m + 18 * s);
        float x = crest.X + 30 * s;
        float lowHp = p.Dead ? 0 : Math.Clamp((0.3f - _hp) / 0.3f, 0, 1);
        float hurt = MathF.Max(0, 1 - _hurtT / 0.22f);
        float beat = lowHp * (0.5f + 0.5f * MathF.Sin(_clock * MathF.Tau / 2.4f));

        var hpRect = new Rectangle(x, m + 9 * s, 290 * s * p.MaxHealth / 100f, 15 * s);
        ui.Vital(hpRect, new VitalLook(_hpFill, _hpLag, _hp - _hpFill, Ui.Blood, 11)
        {
            Tremble = MathF.Max(hurt, lowHp * 0.35f),
            Pulse = beat,
            Alpha = a,
        });
        // El emblema va encima del corchete de la barra: la vida nace del sello.
        ui.Crest(crest, 19 * s, Ui.Blood, a, glow: MathF.Max(0, 1 - _healT / 0.7f), pulse: beat);
        // Salpicadura mínima en la punta al recibir daño.
        if (hurt > 0)
        {
            Vector2 tip = new(hpRect.X + hpRect.Width * _hp, hpRect.Y + hpRect.Height / 2);
            for (int i = 0; i < 4; i++)
            {
                float ang = -1.2f + i * 0.8f + Ui.Hash(i + 3) * 0.4f;
                float d = (6 + 12 * (1 - hurt)) * s * (0.6f + Ui.Hash(i) * 0.6f);
                Raylib.DrawCircleV(tip + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * d, (1.2f + 1.6f * Ui.Hash(i + 9)) * s * hurt, Ui.A(Palette.Ink, a * hurt));
            }
        }

        float exhaust = MathF.Max(0, 1 - _exhaustT / 0.4f);
        var stRect = new Rectangle(x + 2 * s, m + 33 * s, 220 * s * p.MaxStamina / 100f, 6 * s);
        ui.Vital(stRect, new VitalLook(_stamina, _stamina, 0, Ui.Moss, 23)
        {
            Tremble = exhaust,
            Hatch = _recovering ? 1 : 0,
            Alpha = a * _staminaAlpha,
            Style = VitalStyle.Stamina,
        });

        // Frascos de brasa bajo las barras.
        const float flaskSize = 27, flaskStep = 21;
        float fy = m + 62 * s;
        for (int i = 0; i < p.MaxFlasks; i++)
        {
            float fx = x + 10 * s + i * flaskStep * s;
            float fill = i < p.Flasks ? Ui.Smooth(_filled[i] / 0.35f) : 1 - Ui.Smooth(_drained[i] / 0.4f);
            float pop = i < p.Flasks ? MathF.Max(0, 1 - _filled[i] / 0.3f) : 0;
            float size = flaskSize * s * (1 + pop * 0.12f);
            var at = new Vector2(fx, fy - pop * 3 * s);
            ui.Flask(at, size, fill, a);
            // Al beber, una llamita escapa por el cuello y se deshace en una voluta de brasa.
            // Avanza a saltos de 12 fps, como el resto de lo dibujado.
            float puff = MathF.Floor(_drained[i] * 12) / 12 / 0.5f;
            if (i < p.Flasks || puff >= 1) continue;
            Vector2 mouth = Ui.FlaskMouth(at, size);
            if (puff < 0.35f) ui.Flame(mouth, 9 * s * (1 - puff / 0.35f), a);
            for (int j = 0; j < 4; j++)
            {
                float k = puff + j * 0.08f;
                var wisp = mouth + new Vector2(MathF.Sin(k * 7 + j) * 3.5f * s, -4 * s - k * 22 * s - j * 2.5f * s);
                Raylib.DrawCircleV(wisp, (2.2f - j * 0.4f) * s * (1 - puff), Ui.A(Palette.Mix(Palette.Ember, Ui.Brass, j * 0.2f), a * (1 - puff)));
            }
        }

        // Estado: contraataque listo tras un desvío.
        if (_riposte > 0.01f)
        {
            float rx = x + 10 * s + p.MaxFlasks * flaskStep * s + 12 * s;
            var at = new Vector2(rx, fy);
            float ra = a * _riposte;
            Raylib.DrawLineEx(at + new Vector2(-6, 7) * s, at + new Vector2(7, -8) * s, 3.4f * s, Ui.A(Palette.Ink, ra));
            Raylib.DrawLineEx(at + new Vector2(-6, 7) * s, at + new Vector2(7, -8) * s, 1.8f * s, Ui.A(Palette.GhostCyan, ra));
            Raylib.DrawLineEx(at + new Vector2(-6, -2) * s, at + new Vector2(1, 5) * s, 2.4f * s, Ui.A(Palette.Ink, ra));
            ui.Text("Contraataque", at + new Vector2(14 * s, 0), 15 * s, Ui.A(Palette.GhostCyan, ra), Face.Bold, 1, Align.Left, shadow: true);
        }
    }

    void DrawBoss(FallenKing king, float a)
    {
        if (_billing) DrawBill(a);
        // Sin rótulo (se vuelve a la pelea tras morir) la barra entra en cuanto empieza el combate.
        float t = _bossReveal - (_billing ? BarDelay : 0);
        float ba = a * _bossAlpha * Ui.Smooth(t / 0.3f);
        if (ba <= 0.01f) return;
        float s = ui.S;
        float reveal = Ui.Smooth(t / 0.7f);
        float full = MathF.Min(760 * s, ui.W * 0.62f), bw = full * (0.35f + 0.65f * reveal);
        // Entra desde abajo, como un rótulo que sube a escena.
        float y = ui.H - ui.Margin - 20 * s + (1 - Ui.Smooth(t / 0.45f)) * 44 * s;
        var r = new Rectangle(ui.W / 2 - bw / 2, y, bw, 11 * s);
        BossBill bill = BossBill.Baldomero;

        // El nombre en versalitas; en la segunda fase tiembla, como si el rótulo se redibujara.
        Vector2 nameAt = new(ui.W / 2, y - 21 * s);
        if (king.Phase2) nameAt += new Vector2(ui.Jit(3), ui.Jit(10)) * 0.9f * s;
        float na = ba * Ui.Smooth((t - 0.2f) / 0.4f);
        Color name = Ui.A(king.Phase2 ? Palette.Mix(Palette.Parchment, Ui.Ivory, 0.5f) : Palette.Parchment, na);
        ui.Text(ui.Upper(bill.Name), nameAt, 21 * s, name, Face.Display, 4, shadow: true);
        ui.Text(bill.Epithet, nameAt + new Vector2(0, -19 * s), 14 * s, Ui.A(Palette.Parchment, na * 0.7f), Face.Body, 2, shadow: true);

        ui.Vital(r, new VitalLook(_bossFill * reveal + (1 - reveal), _bossLag, 0, king.Phase2 ? Palette.Mix(Palette.Crimson, Ui.Blood, 0.4f) : Ui.Blood, 41)
        {
            Alpha = ba,
            Style = VitalStyle.Boss,
            Fury = king.Phase2 ? 1 : 0,
        });
    }

    /// <summary>
    /// Presentación breve del jefe al empezar el combate por primera vez: capítulo, nombre y epíteto sobre
    /// una pincelada que se pinta sola; después todo se desvanece y la barra sube desde abajo.
    /// </summary>
    void DrawBill(float a)
    {
        float t = _bossReveal;
        if (t > BillLength) return;
        float s = ui.S, leave = 1 - Ui.Smooth((t - 1.25f) / 0.45f);
        BossBill bill = BossBill.Baldomero;
        var c = new Vector2(ui.W / 2, ui.H * 0.27f);
        float oa = Ui.Smooth(t / 0.25f) * leave * a;
        float na = Ui.Smooth((t - 0.12f) / 0.3f) * leave * a;
        float ea = Ui.Smooth((t - 0.38f) / 0.3f) * leave * a;
        ui.Swath(c + new Vector2(-320 * s, 26 * s), c + new Vector2(330 * s, 8 * s), 170 * s, Ui.A(Palette.Ink, 0.78f * leave * a), 97, Ui.Smooth(t / 0.35f));
        ui.Heading(bill.Overline, c - new Vector2(0, 50 * s), 14 * s, Ui.A(Palette.Parchment, 0.9f * oa), shadow: true);
        // El nombre cae como un sello: entra un poco grande y se asienta.
        float settle = 1 + 0.06f * (1 - Ui.Smooth((t - 0.12f) / 0.3f));
        ui.Text(ui.Upper(bill.Name), c + new Vector2(0, 2 * s), 50 * s * settle, Ui.A(Ui.Ivory, na), Face.Display, 6, shadow: true);
        ui.Flourish(c + new Vector2(0, 38 * s), 170 * s, Ui.A(Palette.Parchment, 0.85f * na), Ui.Smooth((t - 0.2f) / 0.55f), 41);
        ui.Text(bill.Epithet, c + new Vector2(0, 64 * s + (1 - ea) * 5 * s), 20 * s, Ui.A(Palette.Parchment, 0.9f * ea), Face.Body, 2, shadow: true);
    }

    void DrawPrompt(Camera3D cam, float a)
    {
        if (_prompt <= 0.01f) return;
        Vector2 at = InFront(_cue.Anchor, cam) ? Raylib.GetWorldToScreen(_cue.Anchor, cam) : new Vector2(ui.W / 2, ui.H * 0.62f);
        // La cámara pone al caballero en el centro: la indicación se aparta a un lado para no taparlo.
        float s = ui.S, side = at.X - ui.W / 2;
        Vector2 size = ui.PromptSize(_cue.Key, _cue.Verb);
        float clear = 90 * s + size.X / 2;
        if (MathF.Abs(side) < clear) at.X = ui.W / 2 + (side < 0 ? -clear : clear);
        // Nunca por encima del bloque de vida y frascos, ni sobre los consejos de abajo.
        float mx = ui.Margin + size.X / 2 + 30 * s, top = ui.Margin + 120 * s, bottom = ui.H - ui.Margin - 200 * s;
        at = Vector2.Clamp(at, new Vector2(mx, top), new Vector2(ui.W - mx, MathF.Max(top, bottom)));
        ui.Prompt(at, _cue.Key, _cue.Verb, _prompt * a);
    }

    /// <summary>Tarjeta de capítulo: casi solo tipografía, un velo de tinta muy tenue y un filete con volutas.</summary>
    void DrawArea(float a)
    {
        if (_areaTitle == null || _areaT > 5) return;
        float t = _areaT;
        float fa = Ui.Smooth(t / 0.9f) * (1 - Ui.Smooth((t - 3.6f) / 1.2f)) * a;
        if (fa <= 0.01f) return;
        float s = ui.S;
        var c = new Vector2(ui.W / 2, ui.H * 0.24f);
        ui.Wash(c + new Vector2(0, 12 * s), new Vector2(380, 80) * s, fa * 0.75f);
        // Las letras se juntan mientras aparecen, como un rótulo que se enfoca.
        float tracking = 5 + 2.2f * (1 - Ui.Smooth(t / 1.2f));
        Color paper = Ui.A(Palette.Parchment, fa);
        Vector2 dot = c - new Vector2(0, 42 * s);
        Raylib.DrawPoly(dot, 4, 3.4f * s, 45, Ui.A(Palette.Parchment, 0.8f * fa));
        ui.Stroke(dot - new Vector2(9 * s, 0), dot - new Vector2(34 * s, 0), 1.3f * s, Ui.A(Palette.Parchment, 0.7f * fa), 71, 0.2f, tipIn: 0.1f, tipOut: 0.8f);
        ui.Stroke(dot + new Vector2(9 * s, 0), dot + new Vector2(34 * s, 0), 1.3f * s, Ui.A(Palette.Parchment, 0.7f * fa), 72, 0.2f, tipIn: 0.1f, tipOut: 0.8f);
        ui.Text(ui.Upper(_areaTitle), c - new Vector2(0, (1 - fa) * 4 * s), 40 * s, paper, Face.Display, tracking, shadow: true);
        ui.Flourish(c + new Vector2(0, 31 * s), 150 * s, Ui.A(Palette.Parchment, fa * 0.9f), Ui.Smooth((t - 0.15f) / 1.2f), Ui.Seed(_areaTitle));
        ui.Text(_areaSubtitle ?? "", c + new Vector2(0, 58 * s), 18 * s, Ui.A(Palette.Parchment, fa * 0.85f), Face.Body, 2.4f, shadow: true);
    }

    /// <summary>Avisos cortos de la simulación: una frase en versalitas y, si la hay, una segunda más discreta.</summary>
    void DrawToast(float a)
    {
        if (_toast.Length == 0 || _lastToastTime <= 0) return;
        float ta = Ui.Smooth(_toastAge / 0.2f) * Ui.Smooth(_lastToastTime / 0.4f) * a;
        if (ta <= 0.01f) return;
        float s = ui.S;
        int cut = _toast.IndexOfAny(['.', '!', '?'], 1);
        string head = cut > 0 ? _toast[..(cut + 1)].TrimEnd('.') : _toast, rest = cut > 0 ? _toast[(cut + 1)..].Trim() : "";
        var c = new Vector2(ui.W / 2, ui.H * (_areaT < 5 ? 0.4f : 0.2f));
        ui.Wash(c + new Vector2(0, 10 * s), new Vector2(240, 50) * s, ta);
        ui.Heading(head, c + new Vector2(0, (1 - ta) * 5 * s), 20 * s, Ui.A(Palette.Parchment, ta), shadow: true);
        if (rest.Length > 0) ui.Text(rest, c + new Vector2(0, 26 * s), 17 * s, Ui.A(Palette.Parchment, 0.85f * ta), shadow: true);
    }

    /// <summary>
    /// Etiqueta de inventario al rellenar los frascos: un trozo de papel con el borde derecho arrancado,
    /// ojal de latón y cordel. Icono, nombre y cantidad; no bloquea nada.
    /// </summary>
    void DrawItem(float a)
    {
        if (_itemT > 3.2f) return;
        float ia = Ui.Smooth(_itemT / 0.25f) * (1 - Ui.Smooth((_itemT - 2.6f) / 0.5f)) * a;
        if (ia <= 0.01f) return;
        float s = ui.S;
        var r = new Rectangle(ui.W - ui.Margin - 236 * s + (1 - ia) * 18 * s, ui.H * 0.36f, 236 * s, 84 * s);
        float cut = 18 * s, mid = r.Y + r.Height / 2;

        // Silueta: esquinas izquierdas cortadas en chaflán y borde derecho rasgado.
        const int torn = 12;
        Span<Vector2> tag = stackalloc Vector2[torn + 6];
        int n = 0;
        tag[n++] = new Vector2(r.X + cut, r.Y);
        tag[n++] = new Vector2(r.X + r.Width * 0.55f, r.Y + (Ui.Hash(801) - 0.5f) * 1.5f * s);
        for (int i = 0; i < torn; i++)
        {
            float k = i / (float)(torn - 1);
            float bite = (i % 2 == 0 ? 0 : 5 * s) + Ui.Hash(810 + i) * 4 * s;
            tag[n++] = new Vector2(r.X + r.Width - bite, r.Y + r.Height * k);
        }
        tag[n++] = new Vector2(r.X + r.Width * 0.5f, r.Y + r.Height + (Ui.Hash(802) - 0.5f) * 1.5f * s);
        tag[n++] = new Vector2(r.X + cut, r.Y + r.Height);
        tag[n++] = new Vector2(r.X, r.Y + r.Height - cut);
        tag[n++] = new Vector2(r.X, r.Y + cut);
        Span<Vector2> shade = stackalloc Vector2[n];
        for (int i = 0; i < n; i++) shade[i] = tag[i] + new Vector2(5, 7) * s;

        // Cordel que sale del ojal hacia fuera de la etiqueta.
        var hole = new Vector2(r.X + 17 * s, mid);
        Span<Vector2> cord = stackalloc Vector2[5];
        Span<float> cw = stackalloc float[5];
        for (int i = 0; i < 5; i++)
        {
            float k = i / 4f;
            cord[i] = hole + new Vector2(-k * 44 * s, -MathF.Sin(k * 2.6f) * 16 * s + ui.Jit(830 + i, true) * 0.5f * s);
            cw[i] = 2 * s * (1 - 0.6f * k);
        }
        Ui.Fill(shade, Ui.A(Palette.Ink, 0.45f * ia), new Vector2(r.X + r.Width / 2, mid) + new Vector2(5, 7) * s);
        Ui.Pen(cord, cw, Ui.A(Palette.Umber, ia));
        Ui.Fill(tag[..n], Ui.A(Palette.Parchment, 0.97f * ia), new Vector2(r.X + r.Width / 2, mid));
        Ui.PenLoop(tag[..n], 1.6f * s, Ui.A(Palette.Ink, ia), 840);
        Raylib.DrawCircleV(hole, 5.2f * s, Ui.A(Ui.Brass, ia));
        Raylib.DrawCircleV(hole, 3.2f * s, Ui.A(Palette.Ink, ia));

        ui.Flask(new Vector2(r.X + 56 * s, mid + 2 * s), 42 * s, 1, ia, 0.6f);
        float tx = r.X + 88 * s;
        ui.Text("FRASCOS DE BRASA", new Vector2(tx, mid - 14 * s), 15 * s, Ui.A(Palette.Ink, ia), Face.Display, 2.5f, Align.Left);
        ui.Stroke(new Vector2(tx, mid + 1 * s), new Vector2(tx + 120 * s, mid + 1 * s), 1.2f * s, Ui.A(Palette.Ink, 0.5f * ia), 850, 0.4f, tipIn: 0.05f, tipOut: 0.6f);
        if (_itemCount != _itemLabelCount) { _itemLabelCount = _itemCount; _itemLabel = $"× {_itemCount}"; }
        ui.Text(_itemLabel, new Vector2(tx, mid + 18 * s), 21 * s, Ui.A(Ui.Blood, ia), Face.Display, 1, Align.Left);
    }

    string _itemLabel = "";
    int _itemLabelCount = -1;
}
