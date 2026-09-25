using System.Numerics;
using CaballeroDeTinta.Art;
using CaballeroDeTinta.Sim;
using Raylib_cs;

namespace CaballeroDeTinta.View;

/// <summary>Un gesto que se enseña: los iconos y el verbo.</summary>
readonly record struct Gesture(string Label, params Glyph[] Glyphs);

/// <summary>
/// Consejos que aparecen cuando son útiles, de uno en uno, y no vuelven una vez aprendidos.
/// Solo observa el reino: no decide nada del juego.
/// </summary>
sealed class Tutorial(Ui ui, Settings settings)
{
    sealed record Lesson(string Id, string Title, Gesture[] Gestures, Func<Kingdom, Tutorial, bool> Ready, Func<Kingdom, Tutorial, bool> Done);

    static readonly Lesson[] Lessons =
    [
        new("mover", "Movimiento", [new("Moverse", Glyph.Wasd)],
            (k, t) => true,
            (k, t) => Vector3.Distance(k.Player.Feet, t._startFeet) > 2.5f),
        new("camara", "Cámara", [new("Mirar alrededor", Glyph.M(MouseMark.Move))],
            (k, t) => t.Seen("mover"),
            (k, t) => MathF.Abs(k.CamYaw - t._startYaw) > 0.6f || MathF.Abs(k.CamPitch - t._startPitch) > 0.2f),
        new("curar", "Curación", [new("Beber un frasco de brasa", Glyph.K("Q"))],
            (k, t) => k.Player.Health < k.Player.MaxHealth * 0.55f && k.Player.Flasks > 0 && k.Player.State != KnightState.Heal,
            (k, t) => k.Player.State == KnightState.Heal),
        new("atacar", "Combate", [new("Ataque", Glyph.M(MouseMark.Left)), new("Ataque fuerte", Glyph.M(MouseMark.Right))],
            (k, t) => k.Skeletons.Any(s => !s.Dead && Vector3.Distance(s.Feet, k.Player.Feet) < 13),
            (k, t) => k.Player.State is KnightState.Light or KnightState.Heavy),
        new("defender", "Defensa", [new("Esquivar", Glyph.K("ESPACIO")), new("Desviar el golpe", Glyph.K("F"))],
            (k, t) => t.Seen("atacar") && (k.Player.HurtFlash > 0 || k.Skeletons.Any(s => s.State == FoeState.Windup && Vector3.Distance(s.Feet, k.Player.Feet) < 6)),
            (k, t) => k.Player.State is KnightState.Dodge or KnightState.Parry),
        new("fijar", "Fijar objetivo", [new("Fijar o soltar", Glyph.K("TAB"), Glyph.Or, Glyph.M(MouseMark.Wheel))],
            (k, t) => t.Seen("atacar") && k.LockTarget == null && k.Skeletons.Any(s => !s.Dead && Vector3.Distance(s.Feet, k.Player.Feet) < 10),
            (k, t) => k.LockTarget != null),
    ];

    const float FirstDelay = 4.2f, Gap = 1.4f, MaxTime = 9f, FadeTime = 0.2f;

    Lesson? _current;
    float _t, _alpha, _clock, _gap, _doneT;
    bool _done, _leaving;
    Vector3 _startFeet;
    float _startYaw, _startPitch;

    bool Seen(string id) => settings.SeenTutorials.Contains(id);

    public void Reset()
    {
        _current = null;
        _alpha = _clock = _t = 0;
        _gap = 0;
        _leaving = _done = false;
    }

    public void Update(Kingdom k, float dt)
    {
        _clock += dt;
        bool allowed = settings.Tutorials && k.Phase is Phase.Explore or Phase.Boss && !k.Player.Dead;

        if (_current == null)
        {
            _gap -= dt;
            if (allowed && _clock > FirstDelay && _gap <= 0 && k.Player.State != KnightState.Rest)
            {
                _current = Lessons.FirstOrDefault(l => !Seen(l.Id) && l.Ready(k, this));
                if (_current != null)
                {
                    _t = _doneT = 0;
                    _done = _leaving = false;
                    _startFeet = k.Player.Feet;
                    _startYaw = k.CamYaw;
                    _startPitch = k.CamPitch;
                }
            }
            return;
        }

        _t += dt;
        if (!_done && _t > 0.5f && _current.Done(k, this)) _done = true;
        if (_done) _doneT += dt;
        if (!_leaving && ((_done && _doneT > 0.7f) || _t > MaxTime || !allowed))
        {
            _leaving = true;
            // Si se interrumpe (muerte, cartel del jefe) sin haberlo aprendido, volverá más tarde.
            if (_done || _t > MaxTime)
            {
                settings.SeenTutorials.Add(_current.Id);
                settings.Save();
            }
        }
        _alpha = Ui.Approach(_alpha, _leaving ? 0 : 1, dt / FadeTime);
        if (_leaving && _alpha <= 0)
        {
            _current = null;
            _gap = Gap;
        }
    }

    /// <summary>Tarjeta abajo al centro: título en versalitas, los iconos y el verbo debajo.</summary>
    public void Draw(float alpha)
    {
        if (_current is not { } lesson) return;
        float a = Ui.Smooth(_alpha) * alpha;
        if (a <= 0.01f) return;
        float s = ui.S;
        float press = _done ? Ui.Smooth(_doneT / 0.12f) * (1 - Ui.Smooth((_doneT - 0.25f) / 0.2f)) : 0;

        float colGap = 48 * s;
        float[] widths = lesson.Gestures.Select(g => MathF.Max(ui.GlyphRowWidth(g.Glyphs), ui.Measure(g.Label, 18 * s).X)).ToArray();
        float glyphH = lesson.Gestures.Max(g => g.Glyphs.Max(x => ui.GlyphSize(x).Y));
        float total = widths.Sum() + colGap * (widths.Length - 1);

        // Pegada al margen inferior: el caballero, en el centro de la imagen, queda libre.
        float labelY = ui.H - ui.Margin - 10 * s + (1 - a) * 10 * s;
        float glyphY = labelY - 20 * s - glyphH / 2;
        float top = glyphY - glyphH / 2 - 30 * s;
        ui.Wash(new Vector2(ui.W / 2, (top + labelY) / 2), new Vector2(total / 2 + 90 * s, (labelY - top) / 2 + 46 * s), a);
        ui.Heading(lesson.Title, new Vector2(ui.W / 2, top), 17 * s, Ui.A(Palette.Parchment, a), shadow: true);

        float x = ui.W / 2 - total / 2;
        for (int i = 0; i < lesson.Gestures.Length; i++)
        {
            Gesture g = lesson.Gestures[i];
            float cx = x + widths[i] / 2;
            ui.GlyphRow(g.Glyphs, new Vector2(cx, glyphY), a, press);
            Color label = Palette.Mix(Palette.Parchment, Ui.Brass, _done ? Ui.Smooth(_doneT / 0.2f) : 0);
            ui.Text(g.Label, new Vector2(cx, labelY), 18 * s, Ui.A(label, a), shadow: true);
            x += widths[i] + colGap;
        }
    }
}
