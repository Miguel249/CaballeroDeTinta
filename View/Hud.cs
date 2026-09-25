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
/// La interfaz durante el juego. Lee el reino y lo presenta: nunca cambia la simulación.
/// Guarda solo estado de presentación (rastros de daño, fundidos, qué zona ya se anunció...).
/// </summary>
sealed class Hud(Ui ui, Settings settings)
{
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

    // Indicaciones, avisos, zonas, objetos.
    float _prompt, _toastAge, _lastToastTime;
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
            if (_bossReveal == 0) { _bossFill = _bossLag = boss; }
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

        // Indicación de la hoguera.
        bool canRest = k.Phase is Phase.Explore && !p.Dead && p.State != KnightState.Rest && Vector3.Distance(p.Feet, k.Bonfire) < 2.4f;
        _prompt = Ui.Approach(_prompt, canRest ? 1 : 0, dt / (canRest ? 0.16f : 0.1f));

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
        float spin = ui.Time * 0.6f;
        float wobble = (Ui.Hash(ui.Frame * 17) - 0.5f) * 0.8f * s;
        const float gap = 0.9f;               // radianes sin tinta: el trazo no se cierra
        float sweep = (MathF.Tau - gap) * drawn;
        int pieces = broken > 0 ? 3 : 1;
        for (int piece = 0; piece < pieces; piece++)
        {
            float from = spin + piece * sweep / pieces, to = spin + (piece + 1) * sweep / pieces - (broken > 0 ? 0.25f : 0);
            float mid = (from + to) / 2;
            Vector2 push = new Vector2(MathF.Cos(mid), MathF.Sin(mid)) * broken * 14 * s;
            Arc(c + push, r + wobble, from, to, 4.2f * s, Ui.A(Palette.Parchment, 0.85f * a));
            Arc(c + push, r + wobble, from, to, 2.2f * s, Ui.A(Palette.Ink, a));
        }
        if (broken > 0) return;
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
            DrawPrompt(k, cam, a);
            DrawArea(a);
            DrawToast(a);
            DrawItem(a);
            _tutorial.Draw(a);
        }
    }

    void DrawVitals(Knight p, float a)
    {
        float s = ui.S, m = ui.Margin;
        var crest = new Vector2(m + 16 * s, m + 16 * s);
        float x = crest.X + 26 * s;
        float lowHp = p.Dead ? 0 : Math.Clamp((0.3f - _hp) / 0.3f, 0, 1);
        float hurt = MathF.Max(0, 1 - _hurtT / 0.22f);

        ui.Crest(crest, 17 * s, Ui.Blood, a, glow: MathF.Max(0, 1 - _healT / 0.7f));
        var hpRect = new Rectangle(x, m + 8 * s, 290 * s * p.MaxHealth / 100f, 15 * s);
        ui.Vital(hpRect, new VitalLook(_hpFill, _hpLag, _hp - _hpFill, Ui.Blood, 11)
        {
            Tremble = MathF.Max(hurt, lowHp * 0.35f),
            Pulse = lowHp * (0.5f + 0.5f * MathF.Sin(_clock * MathF.Tau / 2.4f)),
            Alpha = a,
        });
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
        // Remate de la barra: rombo de tinta, como en la barra del jefe.
        var finial = new Vector2(hpRect.X + hpRect.Width + 9 * s, hpRect.Y + hpRect.Height / 2);
        Raylib.DrawPoly(finial, 4, 6 * s, 45, Ui.A(Palette.Ink, a));
        Raylib.DrawPoly(finial, 4, 3.8f * s, 45, Ui.A(Palette.Parchment, 0.9f * a));

        var stRect = new Rectangle(x, m + 31 * s, 220 * s * p.MaxStamina / 100f, 7 * s);
        ui.Vital(stRect, new VitalLook(_stamina, _stamina, 0, Ui.Moss, 23)
        {
            Tremble = exhaust,
            Hatch = _recovering ? 1 : 0,
            Alpha = a * _staminaAlpha,
        });

        // Frascos de brasa bajo las barras.
        float fy = m + 58 * s;
        for (int i = 0; i < p.MaxFlasks; i++)
        {
            float fx = x + 8 * s + i * 24 * s;
            float fill = i < p.Flasks ? Ui.Smooth(_filled[i] / 0.35f) : 1 - Ui.Smooth(_drained[i] / 0.4f);
            float pop = i < p.Flasks ? MathF.Max(0, 1 - _filled[i] / 0.3f) : 0;
            ui.Flask(new Vector2(fx, fy - pop * 3 * s), 22 * s * (1 + pop * 0.12f), fill, a);
            // Al beber, una voluta de brasa sube del frasco.
            float puff = _drained[i] / 0.5f;
            if (i >= p.Flasks && puff < 1)
                for (int j = 0; j < 3; j++)
                    Raylib.DrawCircleV(new Vector2(fx + (j - 1) * 4 * s, fy - 12 * s - puff * 16 * s - j * 3 * s), (2.2f - j * 0.5f) * s * (1 - puff), Ui.A(Palette.Ember, a * (1 - puff)));
        }

        // Estado: contraataque listo tras un desvío.
        if (_riposte > 0.01f)
        {
            float rx = x + 8 * s + p.MaxFlasks * 24 * s + 14 * s;
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
        float reveal = Ui.Smooth(_bossReveal / 0.6f);
        float ba = a * _bossAlpha;
        if (ba <= 0.01f) return;
        float s = ui.S;
        float full = MathF.Min(760 * s, ui.W * 0.62f), bw = full * reveal;
        float y = ui.H - ui.Margin - 20 * s;
        var r = new Rectangle(ui.W / 2 - bw / 2, y, bw, 11 * s);
        BossBill bill = BossBill.Baldomero;

        // El nombre en versalitas; en la segunda fase tiembla, como si el rótulo se redibujara.
        Vector2 nameAt = new(ui.W / 2, y - 20 * s);
        if (king.Phase2) nameAt += new Vector2(Ui.Hash(ui.Frame) - 0.5f, Ui.Hash(ui.Frame + 7) - 0.5f) * 1.6f * s;
        float na = ba * Ui.Smooth((_bossReveal - 0.25f) / 0.4f);
        ui.Text(bill.Name.ToUpperInvariant(), nameAt, 21 * s, Ui.A(Palette.Parchment, na), Face.Display, 4, shadow: true);
        ui.Text(bill.Epithet, nameAt + new Vector2(0, -19 * s), 14 * s, Ui.A(Palette.Parchment, na * 0.7f), Face.Body, 2, shadow: true);

        if (bw < 4 * s) return;
        ui.Vital(r, new VitalLook(_bossFill * reveal + (1 - reveal), _bossLag, 0, king.Phase2 ? Palette.Mix(Palette.Crimson, Ui.Blood, 0.4f) : Ui.Blood, 41) { Alpha = ba });
        // Remates ornamentales en los extremos.
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 end = new(ui.W / 2 + side * (bw / 2 + 10 * s), y + 5.5f * s);
            Raylib.DrawPoly(end, 4, 7 * s, 45, Ui.A(Palette.Ink, ba));
            Raylib.DrawPoly(end, 4, 4.5f * s, 45, Ui.A(Palette.Parchment, ba));
            Ui.Taper(end + new Vector2(side * 8 * s, 0), end + new Vector2(side * 30 * s, 0), 2 * s, Ui.A(Palette.Parchment, 0.8f * ba), 3);
        }
    }

    void DrawPrompt(Kingdom k, Camera3D cam, float a)
    {
        if (_prompt <= 0.01f) return;
        Vector3 anchor = k.Bonfire + new Vector3(0, 1.9f, 0);
        Vector2 at = InFront(anchor, cam) ? Raylib.GetWorldToScreen(anchor, cam) : new Vector2(ui.W / 2, ui.H * 0.62f);
        // La cámara pone al caballero en el centro: la indicación se aparta a un lado para no taparlo.
        float s = ui.S, side = at.X - ui.W / 2;
        if (MathF.Abs(side) < 120 * s) at.X = ui.W / 2 + (side < 0 ? -120 : 120) * s;
        float m = ui.Margin + 40 * s;
        at = Vector2.Clamp(at, new Vector2(m, m), new Vector2(ui.W - m, ui.H - m - 170 * s));
        ui.Prompt(at, "E", "Descansar", _prompt * a);
    }

    void DrawArea(float a)
    {
        if (_areaTitle == null || _areaT > 5) return;
        float t = _areaT;
        float fa = Ui.Smooth(t / 0.9f) * (1 - Ui.Smooth((t - 3.6f) / 1.2f)) * a;
        if (fa <= 0.01f) return;
        float s = ui.S;
        var c = new Vector2(ui.W / 2, ui.H * 0.24f);
        ui.Wash(c + new Vector2(0, 14 * s), new Vector2(330, 90) * s, fa);
        ui.Text(_areaTitle.ToUpperInvariant(), c - new Vector2(0, (1 - fa) * 4 * s), 40 * s, Ui.A(Palette.Parchment, fa), Face.Display, 5, shadow: true);
        ui.Divider(c + new Vector2(0, 30 * s), 140 * s * Ui.Smooth(t / 1.4f), Ui.A(Palette.Parchment, fa * 0.9f));
        ui.Text(_areaSubtitle ?? "", c + new Vector2(0, 56 * s), 18 * s, Ui.A(Palette.Parchment, fa * 0.85f), Face.Body, 2, shadow: true);
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

    /// <summary>Tarjetita de objeto al rellenar los frascos: icono, nombre y cantidad.</summary>
    void DrawItem(float a)
    {
        if (_itemT > 3.2f) return;
        float ia = Ui.Smooth(_itemT / 0.25f) * (1 - Ui.Smooth((_itemT - 2.6f) / 0.5f)) * a;
        if (ia <= 0.01f) return;
        float s = ui.S;
        var r = new Rectangle(ui.W - ui.Margin - 230 * s + (1 - ia) * 18 * s, ui.H * 0.36f, 230 * s, 96 * s);
        ui.Panel(r, ia, 5);
        var icon = new Vector2(r.X + 50 * s, r.Y + r.Height / 2 + 2 * s);
        ui.Flask(icon, 38 * s, 1, ia);
        ui.Text("FRASCOS DE BRASA", new Vector2(r.X + 84 * s, r.Y + 38 * s), 15 * s, Ui.A(Palette.Parchment, ia), Face.Display, 2.5f, Align.Left);
        ui.Text($"× {_itemCount}", new Vector2(r.X + 84 * s, r.Y + 62 * s), 20 * s, Ui.A(Ui.Brass, ia), Face.Display, 1, Align.Left);
    }
}
