using System.Numerics;
using CaballeroDeTinta.Art;
using Raylib_cs;

namespace CaballeroDeTinta.View;

enum FrontCmd { None, Start, Resume, ToMenu, Quit }

/// <summary>Entrada de los menús: teclado, ratón y, si hay uno conectado, mando.</summary>
readonly record struct UiInput(bool Up, bool Down, bool Left, bool Right, bool Confirm, bool Back, Vector2 Mouse, bool MouseMoved, bool Click, bool MouseHeld)
{
    /// <summary>Este fotograma se tocó el mando / el teclado o el ratón: decide qué iconos enseñan las pistas.</summary>
    public bool PadUsed { get; init; }
    public bool DeskUsed { get; init; }

    public static UiInput Read()
    {
        bool pad = Raylib.IsGamepadAvailable(0);
        static bool K(KeyboardKey k) => Raylib.IsKeyPressed(k) || Raylib.IsKeyPressedRepeat(k);
        bool P(GamepadButton b) => pad && Raylib.IsGamepadButtonPressed(0, b);
        bool pu = P(GamepadButton.LeftFaceUp), pd = P(GamepadButton.LeftFaceDown), pl = P(GamepadButton.LeftFaceLeft), pr = P(GamepadButton.LeftFaceRight);
        bool pc = P(GamepadButton.RightFaceDown), pb = P(GamepadButton.RightFaceRight);
        bool ku = K(KeyboardKey.Up) || K(KeyboardKey.W), kd = K(KeyboardKey.Down) || K(KeyboardKey.S);
        bool kl = K(KeyboardKey.Left) || K(KeyboardKey.A), kr = K(KeyboardKey.Right) || K(KeyboardKey.D);
        bool kc = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || Raylib.IsKeyPressed(KeyboardKey.Space);
        bool kb = Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.Backspace);
        bool moved = Raylib.GetMouseDelta().LengthSquared() > 0, click = Raylib.IsMouseButtonPressed(MouseButton.Left);
        return new UiInput(
            Up: ku || pu, Down: kd || pd, Left: kl || pl, Right: kr || pr, Confirm: kc || pc, Back: kb || pb,
            Mouse: Raylib.GetMousePosition(),
            MouseMoved: moved,
            Click: click,
            MouseHeld: Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            PadUsed = pu || pd || pl || pr || pc || pb,
            DeskUsed = ku || kd || kl || kr || kc || kb || moved || click,
        };
    }
}

/// <summary>
/// Menú principal, pausa, controles, opciones y confirmaciones. Todas las pantallas comparten el mismo
/// lenguaje: intertítulo de tinta, versalitas espaciadas, llamita de brasa como selección.
/// </summary>
sealed class Frontend(Ui ui, Settings settings)
{
    enum Page { Main, Pause, Controls, Options, Confirm }
    enum Kind { Button, Slider, Toggle }

    sealed class Item(string label, Kind kind = Kind.Button)
    {
        public readonly string Label = label;
        public readonly Kind Kind = kind;
        public bool Disabled;
        /// <summary>Zona sensible: sale solo de <see cref="Layout"/>, nunca de lo que se dibuja.</summary>
        public Rectangle Hit;
        public float Hover, Press;
        /// <summary>El ratón se fue: el subrayado se retira en vez de encogerse.</summary>
        public bool Leaving;
    }

    readonly Item[] _main = [new("Iniciar partida"), new("Controles"), new("Opciones"), new("Salir")];
    readonly Item[] _pause = [new("Continuar"), new("Controles"), new("Opciones"), new("Volver al menú")];
    readonly Item[] _options =
    [
        new("Música", Kind.Slider), new("Efectos", Kind.Slider), new("Sensibilidad del ratón", Kind.Slider),
        new("Mostrar consejos", Kind.Toggle), new("Restablecer consejos"),
        new("Personajes con modelo 3D", Kind.Toggle),
    ];
    readonly Item[] _confirm = [new("Sí"), new("No")];
    readonly Item _back = new("Volver");

    readonly Stack<Page> _pages = new();
    readonly Dictionary<Page, int> _selected = [];
    string _confirmTitle = "", _confirmBody = "";
    Func<FrontCmd> _confirmYes = () => FrontCmd.None;
    float _pageT;
    int _dragging = -1;

    /// <summary>Sonidos pedidos este fotograma; los reproduce la banda sonora.</summary>
    public readonly List<UiSfx> Sounds = [];
    public bool DemoCompleted;
    public bool IsOpen => _pages.Count > 0;

    Page Current => _pages.Peek();

    public void OpenMain(bool completed)
    {
        _pages.Clear();
        DemoCompleted = completed;
        Push(Page.Main);
        _selected[Page.Main] = 0;
    }

    public void OpenPause()
    {
        _pages.Clear();
        Push(Page.Pause);
        _selected[Page.Pause] = 0;
    }

    public void Close() => _pages.Clear();

    /// <summary>Para las capturas automáticas: abre una subpantalla directamente.</summary>
    public void ShowControls() => Push(Page.Controls);
    public void ShowOptions() => Push(Page.Options);
    public void AskToMenu() => Confirm("¿Volver al menú?", "El progreso no guardado se perderá.", () => FrontCmd.ToMenu);

    void Push(Page p)
    {
        _pages.Push(p);
        _pageT = 0;
        _dragging = -1;
        if (p is Page.Confirm) _selected[p] = 1; // por defecto, la opción segura
        else _selected.TryAdd(p, 0);
    }

    void Pop()
    {
        if (Current == Page.Options) settings.Save();
        _pages.Pop();
        _pageT = 0.12f; // al volver no se repite la entrada entera
        _dragging = -1;
    }

    void Confirm(string title, string body, Func<FrontCmd> yes)
    {
        _confirmTitle = title;
        _confirmBody = body;
        _confirmYes = yes;
        Push(Page.Confirm);
    }

    Item[] ItemsOf(Page p) => p switch
    {
        Page.Main => _main,
        Page.Pause => _pause,
        Page.Options => _options,
        Page.Confirm => _confirm,
        _ => [],
    };

    // ================================================================== entrada

    public FrontCmd Update(UiInput input, float dt)
    {
        if (!IsOpen) return FrontCmd.None;
        _pageT += dt;
        Page page = Current;
        Item[] items = ItemsOf(page);
        _options[4].Disabled = settings.SeenTutorials.Count == 0;
        Layout(page);

        int sel = _selected.GetValueOrDefault(page);
        int hovered = -1;
        for (int i = 0; i < items.Length; i++)
            if (!items[i].Disabled && Raylib.CheckCollisionPointRec(input.Mouse, items[i].Hit)) hovered = i;
        bool backHovered = page is not (Page.Main or Page.Confirm) && Raylib.CheckCollisionPointRec(input.Mouse, _back.Hit);

        if (hovered >= 0 && (input.MouseMoved || input.Click) && hovered != sel) Select(page, sel = hovered);
        bool horizontal = page == Page.Confirm;
        if (items.Length > 0)
        {
            if (input.Up || (horizontal && input.Left)) Select(page, sel = Step(items, sel, -1));
            if (input.Down || (horizontal && input.Right)) Select(page, sel = Step(items, sel, +1));
        }

        foreach (Item it in items)
        {
            bool on = it == ItemAt(items, hovered);
            it.Leaving = !on;
            it.Hover = Ui.Approach(it.Hover, on ? 1 : 0, dt / (on ? 0.14f : 0.18f));
            it.Press = MathF.Max(0, it.Press - dt / 0.18f);
        }
        _back.Hover = Ui.Approach(_back.Hover, backHovered ? 1 : 0, dt / 0.12f);

        // Deslizadores: flechas o arrastrar con el ratón.
        if (!input.MouseHeld) _dragging = -1;
        if (page == Page.Options)
        {
            if (input.Click && hovered >= 0 && items[hovered].Kind == Kind.Slider) _dragging = hovered;
            if (_dragging >= 0)
            {
                Rectangle track = SliderTrack(_options[_dragging]);
                SetSlider(_dragging, (input.Mouse.X - track.X) / track.Width);
            }
            else if (sel >= 0 && sel < items.Length && items[sel].Kind == Kind.Slider && (input.Left || input.Right))
                SetSlider(sel, SliderValue(sel) + (input.Right ? 0.05f : -0.05f));
        }

        if (input.Back) return Back();
        if (input.Click && backHovered) return Back();
        if (page == Page.Controls && input.Confirm) return Back();

        bool activate = (input.Confirm && sel >= 0 && sel < items.Length) || (input.Click && hovered >= 0 && hovered == sel && items[hovered].Kind != Kind.Slider);
        if (!activate || items[sel].Disabled) return FrontCmd.None;
        items[sel].Press = 1;
        return Activate(page, sel);
    }

    static Item? ItemAt(Item[] items, int i) => i >= 0 && i < items.Length ? items[i] : null;

    static int Step(Item[] items, int from, int dir)
    {
        for (int n = 1; n <= items.Length; n++)
        {
            int i = ((from + dir * n) % items.Length + items.Length) % items.Length;
            if (!items[i].Disabled) return i;
        }
        return from;
    }

    void Select(Page page, int i)
    {
        _selected[page] = i;
        Sounds.Add(UiSfx.Hover);
    }

    FrontCmd Back()
    {
        switch (Current)
        {
            case Page.Main:
                // En la raíz, Esc lleva el cursor a "Salir" en vez de cerrar de golpe.
                if (_selected.GetValueOrDefault(Page.Main) != _main.Length - 1) Select(Page.Main, _main.Length - 1);
                return FrontCmd.None;
            case Page.Pause:
                Sounds.Add(UiSfx.Close);
                return FrontCmd.Resume;
            default:
                Sounds.Add(UiSfx.Back);
                Pop();
                return FrontCmd.None;
        }
    }

    FrontCmd Activate(Page page, int i)
    {
        switch (page)
        {
            case Page.Main:
                Sounds.Add(UiSfx.Confirm);
                switch (i)
                {
                    case 0: return FrontCmd.Start;
                    case 1: Push(Page.Controls); break;
                    case 2: Push(Page.Options); break;
                    default: return FrontCmd.Quit;
                }
                break;
            case Page.Pause:
                switch (i)
                {
                    case 0: Sounds.Add(UiSfx.Close); return FrontCmd.Resume;
                    case 1: Sounds.Add(UiSfx.Confirm); Push(Page.Controls); break;
                    case 2: Sounds.Add(UiSfx.Confirm); Push(Page.Options); break;
                    default: Sounds.Add(UiSfx.Confirm); AskToMenu(); break;
                }
                break;
            case Page.Options:
                Sounds.Add(UiSfx.Confirm);
                if (i == 3) { settings.Tutorials = !settings.Tutorials; settings.Save(); }
                if (i == 5) { settings.ModelCharacters = !settings.ModelCharacters; settings.Save(); }
                if (i == 4) Confirm("¿Restablecer consejos?", "Los consejos de control volverán a aparecer desde el principio.", () =>
                {
                    settings.SeenTutorials.Clear();
                    settings.Save();
                    return FrontCmd.None;
                });
                break;
            case Page.Confirm:
                Sounds.Add(i == 0 ? UiSfx.Confirm : UiSfx.Back);
                Pop();
                return i == 0 ? _confirmYes() : FrontCmd.None;
        }
        return FrontCmd.None;
    }

    float SliderValue(int i) => i switch
    {
        0 => settings.Music,
        1 => settings.Effects,
        _ => (settings.MouseSensitivity - Settings.MinSensitivity) / (Settings.MaxSensitivity - Settings.MinSensitivity),
    };

    void SetSlider(int i, float v)
    {
        v = MathF.Round(Math.Clamp(v, 0, 1) * 20) / 20; // pasos del 5 %
        if (MathF.Abs(v - SliderValue(i)) < 1e-4f) return;
        switch (i)
        {
            case 0: settings.Music = v; break;
            case 1: settings.Effects = v; break;
            default: settings.MouseSensitivity = Settings.MinSensitivity + v * (Settings.MaxSensitivity - Settings.MinSensitivity); break;
        }
        Sounds.Add(UiSfx.Tick);
    }


    // ================================================================== composición

    Rectangle PanelOf(Page p)
    {
        float s = ui.S;
        Vector2 size = p switch
        {
            Page.Controls => new Vector2(1120, 610),
            Page.Options => new Vector2(680, 620),
            Page.Confirm => new Vector2(540, 250),
            _ => Vector2.Zero,
        } * s;
        size = Vector2.Min(size, new Vector2(ui.W, ui.H) - new Vector2(ui.Margin * 2));
        return new Rectangle(ui.W / 2 - size.X / 2, ui.H / 2 - size.Y / 2, size.X, size.Y);
    }

    /// <summary>
    /// Origen de la pausa, algo a la izquierda del centro. La pausa no es una ventana sino un intertítulo
    /// sobre la película: el título arriba a la izquierda y las opciones escalonadas sobre una pincelada.
    /// </summary>
    Vector2 PauseOrigin => ui.Center + new Vector2(-40, 12) * ui.S;
    Vector2 PauseHintAt => PauseOrigin + new Vector2(215, 196) * ui.S;

    /// <summary>Zonas sensibles de cada opción. Se calcula igual al leer la entrada y al dibujar.</summary>
    void Layout(Page page)
    {
        float s = ui.S;
        switch (page)
        {
            case Page.Main:
            {
                float y0 = ui.H * (DemoCompleted ? 0.62f : 0.58f);
                for (int i = 0; i < _main.Length; i++)
                    _main[i].Hit = new Rectangle(MainLeft - 34 * s, y0 + i * 52 * s - 22 * s, 340 * s, 44 * s);
                break;
            }
            case Page.Pause:
            {
                // Cada opción baja y se corre un poco a la derecha, siguiendo la pincelada.
                Vector2 o = PauseOrigin;
                for (int i = 0; i < _pause.Length; i++)
                    _pause[i].Hit = new Rectangle(o.X - 130 * s + i * 13 * s, o.Y - 34 * s + i * 50 * s - 22 * s, 320 * s, 44 * s);
                _back.Hit = ui.HintRect(PauseHintAt, "ESC", "Volver");
                break;
            }
            case Page.Options:
            {
                Rectangle r = PanelOf(page);
                for (int i = 0; i < _options.Length; i++)
                    _options[i].Hit = new Rectangle(r.X + 50 * s, OptionY(r, i) - 20 * s, r.Width - 100 * s, 40 * s);
                _back.Hit = ui.HintRect(new Vector2(r.X + r.Width / 2, r.Y + r.Height - 40 * s), "ESC", "Volver");
                break;
            }
            case Page.Controls:
            {
                Rectangle r = PanelOf(page);
                _back.Hit = ui.HintRect(new Vector2(r.X + r.Width / 2, r.Y + r.Height - 36 * s), "ESC", "Volver");
                break;
            }
            case Page.Confirm:
            {
                Rectangle r = PanelOf(page);
                for (int i = 0; i < 2; i++)
                    _confirm[i].Hit = Centered(new Vector2(r.X + r.Width / 2 + (i == 0 ? -90 : 90) * s, r.Y + r.Height - 62 * s), new Vector2(150, 44) * s);
                break;
            }
        }
    }

    static Rectangle Centered(Vector2 c, Vector2 size) => new(c.X - size.X / 2, c.Y - size.Y / 2, size.X, size.Y);

    float OptionY(Rectangle r, int i)
    {
        // Grupos: sonido (0-1), control (2), consejos (3-4) y gráficos (5); cada uno abre con su encabezado.
        float s = ui.S;
        int group = i < 2 ? 0 : i < 3 ? 1 : i < 5 ? 2 : 3;
        return r.Y + 160 * s + group * 44 * s + i * 42 * s;
    }

    Rectangle SliderTrack(Item it) => new(it.Hit.X + it.Hit.Width - 250 * ui.S, it.Hit.Y + it.Hit.Height / 2, 190 * ui.S, 0);

    // ================================================================== dibujo

    /// <param name="reveal">Entrada de la pantalla (0-1): la pausa la anima con el iris.</param>
    public void Draw(float reveal = 1)
    {
        if (!IsOpen) return;
        Page page = Current;
        Layout(page);
        float enter = Ui.Smooth(_pageT / 0.18f) * Ui.Smooth(reveal);
        if (page == Page.Confirm)
        {
            // La pantalla de debajo sigue ahí, apagada.
            Page below = _pages.ElementAt(1);
            Layout(below);
            DrawPage(below, 0.35f * Ui.Smooth(reveal));
            Layout(page);
            Raylib.DrawRectangle(0, 0, (int)ui.W, (int)ui.H, Ui.A(Palette.Ink, 0.35f * enter));
        }
        DrawPage(page, enter);
    }

    void DrawPage(Page page, float a)
    {
        switch (page)
        {
            case Page.Main: DrawMain(a); break;
            case Page.Pause: DrawPause(a); break;
            case Page.Controls: DrawControls(a); break;
            case Page.Options: DrawOptions(a); break;
            case Page.Confirm: DrawConfirm(a); break;
        }
    }

    float Slide(float a) => (1 - a) * 10 * ui.S;

    /// <summary>
    /// Una opción. <paramref name="offset"/> es solo visual (la entrada de la página): la zona sensible no se toca.
    /// Seleccionada, la llama respira; con el ratón encima, un subrayado de pluma se traza de izquierda a
    /// derecha y al irse la cola alcanza a la punta; al confirmar, la palabra da un pequeño golpe.
    /// </summary>
    void DrawItem(Item it, bool selected, float size, float a, Align align = Align.Center, Vector2 offset = default)
    {
        float s = ui.S;
        Vector2 c = new Vector2(it.Hit.X + it.Hit.Width / 2, it.Hit.Y + it.Hit.Height / 2) + offset;
        if (align == Align.Left) c.X = it.Hit.X + 34 * s + offset.X;
        Vector2 at = c + new Vector2(3 * s, 1.5f * s) * Ui.Smooth(it.Press);
        float tw = ui.Measure(it.Label, size, Face.Display, 2).X;
        float left = align == Align.Left ? at.X : at.X - tw / 2;

        Color color = it.Disabled
            ? Ui.A(Palette.Parchment, 0.28f * a)
            : Ui.A(selected ? Ui.Ivory : Palette.Parchment, (selected ? 1f : 0.62f + 0.25f * it.Hover) * a);
        ui.Text(it.Label, at, size, color, Face.Display, 2, align, shadow: true);

        if (selected && !it.Disabled)
            ui.Flame(new Vector2(left - 18 * s, at.Y + size * 0.36f), size * 0.62f, a, it.Press, breath: 1);
        if (it.Hover > 0.01f && !it.Disabled)
        {
            float h = Ui.Smooth(it.Hover), y = at.Y + size * 0.52f;
            float x0 = it.Leaving ? left + tw * (1 - h) : left, x1 = it.Leaving ? left + tw : left + tw * h;
            ui.Stroke(new Vector2(x0, y), new Vector2(x1, y), 1.9f * s, Ui.A(Palette.Parchment, 0.8f * a), Ui.Seed(it.Label), 0.5f, tipIn: 0.12f, tipOut: 0.35f);
        }
    }

    void BackHint(Rectangle panel, float a, float lift = 40) =>
        ui.Hint(new Vector2(panel.X + panel.Width / 2, panel.Y + panel.Height - lift * ui.S), "ESC", "Volver", a, _back.Hover);

    /// <summary>Columna izquierda del menú principal: el mundo queda libre a la derecha, como en un cartel.</summary>
    float MainLeft => ui.Margin + 64 * ui.S;

    void DrawMain(float a)
    {
        float s = ui.S, x = MainLeft;
        int sel = _selected.GetValueOrDefault(Page.Main);
        // Sombra de tinta desde el borde izquierdo para que la columna se lea sobre cualquier fondo.
        Raylib.DrawRectangleGradientH(0, 0, (int)(ui.W * 0.62f), (int)ui.H, Ui.A(Palette.Ink, 0.72f * a), Ui.A(Palette.Ink, 0));
        float y = ui.H * 0.2f + Slide(a);
        float size = MathF.Min(98 * s, ui.W / 11.5f), k = size / (98 * s);
        Color paper = Ui.A(Palette.Parchment, 0.9f * a);

        // Una mancha de tinta detrás del título: le da peso y lo separa del escenario.
        ui.Blot(new Vector2(x + 200 * s * k, y + size * 0.46f), new Vector2(340, 140) * s * k, Ui.A(Palette.Ink, 0.2f * a), 1207, 0.2f, boil: true);
        ui.Blot(new Vector2(x + 190 * s * k, y + size * 0.46f), new Vector2(290, 112) * s * k, Ui.A(Palette.Ink, 0.24f * a), 1211, 0.16f, boil: true);

        string over = "UN CORTOMETRAJE EN TINTA";
        float oy = y - size * 0.72f;
        ui.Text(over, new Vector2(x, oy), 15 * s, Ui.A(Palette.Parchment, 0.8f * a), Face.Display, 3.3f, Align.Left, shadow: true);
        float ow = ui.Measure(over, 15 * s, Face.Display, 3.3f).X;
        ui.Stroke(new Vector2(x + ow + 14 * s, oy), new Vector2(x + ow + 64 * s, oy), 1.8f * s, Ui.A(Palette.Parchment, 0.8f * a), 1203, 0.3f, tipIn: 0.1f, tipOut: 0.8f);
        Raylib.DrawPoly(new Vector2(x + ow + 70 * s, oy), 4, 2.6f * s, 45, Ui.A(Palette.Parchment, 0.8f * a));

        ui.Text("El Caballero", new Vector2(x - 4 * s, y), size, Ui.A(Ui.Ivory, a), Face.Display, 1.5f, Align.Left, shadow: true);
        ui.Text("de Tinta", new Vector2(x + 62 * s * k, y + size * 0.92f), size, Ui.A(Ui.Ivory, a), Face.Display, 1.5f, Align.Left, shadow: true);

        // Firma: el emblema del caballero abre el filete, y del filete cuelga tinta todavía fresca.
        float ry = y + size * 0.92f + 60 * s * k;
        ui.Stroke(new Vector2(x + 26 * s, ry), new Vector2(x + 360 * s, ry + 1.5f * s), 4 * s, paper, 1301, 0.8f, tipIn: 0.03f, tipOut: 0.65f);
        ui.Drip(new Vector2(x + 70 * s, ry + 1 * s), 26 * s, 4.2f * s, paper, 1);
        ui.Drip(new Vector2(x + 104 * s, ry + 1 * s), 12 * s, 3.2f * s, paper, 2);
        ui.Drip(new Vector2(x + 150 * s, ry + 1 * s), (DemoCompleted ? 24 : 36) * s, 3.6f * s, paper, 3);
        ui.Crest(new Vector2(x + 4 * s, ry), 18 * s, Ui.Blood, a);

        if (DemoCompleted)
        {
            ui.Text("DEMO COMPLETADA", new Vector2(x, ry + 44 * s), 18 * s, Ui.A(Ui.Brass, a), Face.Display, 4, Align.Left, shadow: true);
            ui.Text("Baldomero III descansa. Gracias por jugar.", new Vector2(x, ry + 70 * s), 18 * s, Ui.A(Palette.Parchment, 0.85f * a), align: Align.Left, shadow: true);
        }
        for (int i = 0; i < _main.Length; i++) DrawItem(_main[i], i == sel, 30 * s, a, Align.Left);
        Rectangle hint = ui.HintRect(Vector2.Zero, "ENTER", "Aceptar");
        ui.Hint(new Vector2(x + hint.Width / 2 - 6 * s, ui.H - ui.Margin - 14 * s), "ENTER", "Aceptar", 0.8f * a);
    }

    /// <summary>
    /// Pausa como intertítulo: "Pausa" arriba a la izquierda, fuera de la mancha; una pincelada de tinta
    /// que se pinta mientras entra y, sobre ella, las opciones escalonadas. El mundo sigue alrededor.
    /// </summary>
    void DrawPause(float a)
    {
        float s = ui.S;
        var slide = new Vector2(0, Slide(a));
        Vector2 o = PauseOrigin + slide;
        ui.Swath(o + new Vector2(-230, 0) * s, o + new Vector2(330, 115) * s, 262 * s, Ui.A(Palette.Ink, 0.9f * a), 17, Ui.Smooth(a * 1.15f), UiArt.PauseInk);

        Vector2 t = o + new Vector2(-252, -150) * s;
        ui.Text("INTERMEDIO", t + new Vector2(4 * s, -46 * s), 14 * s, Ui.A(Palette.Parchment, 0.75f * a), Face.Display, 4.2f, Align.Left, shadow: true);
        ui.Text("Pausa", t, 72 * s, Ui.A(Ui.Ivory, a), Face.Display, 2, Align.Left, shadow: true);
        float tw = ui.Measure("Pausa", 72 * s, Face.Display, 2).X;
        Vector2 rule = t + new Vector2(5 * s, 42 * s);
        Raylib.DrawPoly(rule, 4, 4.5f * s, 45, Ui.A(Palette.Parchment, 0.9f * a));
        ui.Stroke(rule + new Vector2(10 * s, 0), rule + new Vector2(tw + 46 * s, 1.5f * s), 2 * s, Ui.A(Palette.Parchment, 0.85f * a), 23, 0.5f, tipIn: 0.05f, tipOut: 0.8f);

        int sel = _selected.GetValueOrDefault(Page.Pause);
        for (int i = 0; i < _pause.Length; i++) DrawItem(_pause[i], i == sel, 27 * s, a, Align.Left, slide);
        ui.Hint(PauseHintAt + slide, "ESC", "Volver", a, _back.Hover);
    }

    void PanelTitle(Rectangle r, string title, float a)
    {
        float s = ui.S;
        ui.Text(title, new Vector2(r.X + r.Width / 2, r.Y + 60 * s), 44 * s, Ui.A(Ui.Ivory, a), Face.Display, 2, shadow: true);
        ui.Divider(new Vector2(r.X + r.Width / 2, r.Y + 96 * s), 150 * s, Ui.A(Palette.Parchment, 0.85f * a));
    }

    static readonly (string Section, EmblemKind Emblem, Gesture[][] Rows)[] ControlSheet =
    [
        ("Movimiento", EmblemKind.Boot, [[new("Moverse", Glyph.Wasd)], [new("Cámara", Glyph.M(MouseMark.Move))]]),
        ("Combate", EmblemKind.Sword, [[new("Ataque", Glyph.M(MouseMark.Left)), new("Ataque fuerte", Glyph.M(MouseMark.Right))],
                     [new("Esquivar", Glyph.K("ESPACIO")), new("Desviar", Glyph.K("F"))]]),
        ("Aventura", EmblemKind.Brazier, [[new("Curar", Glyph.K("Q")), new("Interactuar", Glyph.K("E")),
                      new("Fijar objetivo", Glyph.K("TAB"), Glyph.Or, Glyph.M(MouseMark.Wheel)), new("Pausa", Glyph.K("ESC"))]]),
    ];

    /// <summary>
    /// Sitio de cada sección en la lámina, en unidades de diseño desde el centro del panel: Movimiento y
    /// Combate enfrentados arriba, Aventura centrada abajo bajo un filete. Nada de columnas iguales.
    /// </summary>
    static readonly (Vector2 Heading, Vector2 FirstRow, float RowStep, float CellStep)[] ControlSlots =
    [
        (new(-270, -152), new(-270, -85), 105, 0),
        (new(250, -152), new(250, -85), 105, 210),
        (new(0, 128), new(0, 192), 0, 200),
    ];

    void DrawControls(float a)
    {
        float s = ui.S;
        Rectangle r = PanelOf(Page.Controls);
        r.Y += Slide(a);
        ui.Panel(r, a, 9);
        PanelTitle(r, "Controles", a);
        Vector2 pc = new(r.X + r.Width / 2, r.Y + r.Height / 2);
        Color label = Ui.A(Palette.Parchment, 0.88f * a);

        for (int c = 0; c < ControlSheet.Length; c++)
        {
            var (section, emblem, rows) = ControlSheet[c];
            var slot = ControlSlots[c];
            Vector2 head = pc + slot.Heading * s;
            ui.Heading(section, head, 16 * s, Ui.A(Palette.Parchment, 0.9f * a));
            float half = ui.Measure(ui.Upper(section), 16 * s, Face.Display, 16 * 0.22f).X / 2;
            ui.Emblem(emblem, head - new Vector2(half + 72 * s, 3 * s), 44 * s, a);
            for (int row = 0; row < rows.Length; row++)
            {
                Gesture[] cells = rows[row];
                float tall = 0;
                foreach (Gesture g in cells) tall = MathF.Max(tall, ui.GlyphRowHeight(g.Glyphs));
                Vector2 rowAt = pc + (slot.FirstRow + new Vector2(0, row * slot.RowStep)) * s;
                for (int i = 0; i < cells.Length; i++)
                {
                    Vector2 at = rowAt + new Vector2((i - (cells.Length - 1) / 2f) * slot.CellStep * s, 0);
                    ui.GlyphRow(cells[i].Glyphs, at, a);
                    ui.Text(cells[i].Label, at + new Vector2(0, tall / 2 + 17 * s), 17 * s, label);
                }
            }
        }

        // Un nudo de puntos entre las dos secciones de arriba y un filete tenue sobre la de abajo.
        Vector2 knot = pc + new Vector2(-10, -70) * s;
        Color faint = Ui.A(Palette.Parchment, 0.4f * a);
        Raylib.DrawPoly(knot, 4, 4 * s, 45, faint);
        for (int i = 1; i <= 2; i++)
        {
            Raylib.DrawCircleV(knot - new Vector2(0, i * 13 * s), (2 - i * 0.5f) * s, faint);
            Raylib.DrawCircleV(knot + new Vector2(0, i * 13 * s), (2 - i * 0.5f) * s, faint);
        }
        ui.Flourish(pc + new Vector2(0, 97) * s, 330 * s, Ui.A(Palette.Parchment, 0.35f * a), 1, 13);
        BackHint(r, a, 36);
    }

    static readonly (string Title, int First)[] OptionGroups = [("Sonido", 0), ("Control", 2), ("Consejos", 3), ("Gráficos", 5)];

    void DrawOptions(float a)
    {
        float s = ui.S;
        Rectangle r = PanelOf(Page.Options);
        var slide = new Vector2(0, Slide(a));
        r.Y += slide.Y;
        ui.Panel(r, a, 15);
        PanelTitle(r, "Opciones", a);
        for (int g = 0; g < OptionGroups.Length; g++)
            ui.Heading(OptionGroups[g].Title, new Vector2(r.X + r.Width / 2, OptionY(r, OptionGroups[g].First) - 34 * s), 14 * s, Ui.A(Palette.Parchment, 0.7f * a));

        int sel = _selected.GetValueOrDefault(Page.Options);
        for (int i = 0; i < _options.Length; i++)
        {
            Item it = _options[i];
            DrawItem(it, i == sel, 21 * s, a, Align.Left, slide);
            float cy = it.Hit.Y + it.Hit.Height / 2 + slide.Y;
            // Controles en una misma columna: deslizadores y casillas empiezan en el mismo sitio.
            Rectangle track = SliderTrack(it);
            switch (it.Kind)
            {
                case Kind.Slider:
                {
                    float v = SliderValue(i);
                    Vector2 a0 = new(track.X, cy), a1 = new(track.X + track.Width, cy), knob = new(track.X + track.Width * v, cy);
                    // Surco: un trazo fino y algo torcido, con marcas hechas a mano.
                    ui.Stroke(a0, a1, 1.5f * s, Ui.A(Palette.Parchment, 0.35f * a), 900 + i, 0.6f, tipIn: 0.04f, tipOut: 0.04f);
                    for (int t = 0; t <= 4; t++)
                    {
                        float tx = track.X + track.Width * t / 4, th = (3.5f + 1.5f * Ui.Hash(i * 7 + t)) * s;
                        ui.Stroke(new Vector2(tx, cy - th), new Vector2(tx + 0.6f * s, cy + th), 1.1f * s, Ui.A(Palette.Parchment, 0.3f * a), 950 + t, 0.2f, tipIn: 0.3f, tipOut: 0.3f);
                    }
                    // Tinta cargada hasta el pomo.
                    if (v > 0.01f) ui.Stroke(a0, knob, 3.4f * s, Ui.A(Palette.Parchment, 0.9f * a), 920 + i, 0.5f, tipIn: 0.08f, tipOut: 0.02f);
                    bool hot = i == sel;
                    Raylib.DrawPoly(knob, 4, 9 * s, 45, Ui.A(Palette.Ink, a));
                    Raylib.DrawPoly(knob, 4, 6.5f * s, 45, Ui.A(hot ? Palette.Ember : Ui.Ivory, a));
                    if (hot)
                        for (int side = -1; side <= 1; side += 2)
                            Raylib.DrawCircleV(knob + new Vector2(side * 14 * s, 0), 1.6f * s, Ui.A(Palette.Ember, a));
                    string text = i == 2 ? $"×{settings.MouseSensitivity:0.0}" : $"{v * 100:0} %";
                    ui.Text(text, new Vector2(track.X + track.Width + 50 * s, cy), 16 * s, Ui.A(Palette.Parchment, 0.85f * a), align: Align.Right);
                    break;
                }
                case Kind.Toggle:
                {
                    bool on = i == 3 ? settings.Tutorials : settings.ModelCharacters;
                    var box = new Rectangle(track.X, cy - 11 * s, 22 * s, 22 * s);
                    ui.InkFrame(box, 2 * s, Ui.A(Palette.Parchment, 0.9f * a), 77 + i, 0.8f);
                    ui.InkFrame(box, 0.8f * s, Ui.A(Palette.Parchment, 0.4f * a), 91 + i, 1.6f);
                    if (on)
                    {
                        // Marca de tinta en brasa, trazada con dos golpes de pluma.
                        Vector2 b0 = new(box.X + 4 * s, box.Y + 12 * s), b1 = new(box.X + 10 * s, box.Y + 18 * s), b2 = new(box.X + 21 * s, box.Y + 1 * s);
                        ui.Stroke(b1, b0, 3.4f * s, Ui.A(Palette.Ember, a), 60 + i, 0.2f, tipIn: 0.1f, tipOut: 0.5f);
                        ui.Stroke(b1, b2, 3.4f * s, Ui.A(Palette.Ember, a), 70 + i, 0.3f, tipIn: 0.1f, tipOut: 0.6f);
                    }
                    ui.Text(on ? "Sí" : "No", new Vector2(box.X + 36 * s, cy), 17 * s, Ui.A(Palette.Parchment, 0.85f * a), align: Align.Left);
                    break;
                }
                default:
                    if (it.Disabled) ui.Text("nada que restablecer", new Vector2(track.X + track.Width + 50 * s, cy), 15 * s, Ui.A(Palette.Parchment, 0.35f * a), align: Align.Right);
                    break;
            }
        }
        BackHint(r, a);
    }

    void DrawConfirm(float a)
    {
        float s = ui.S;
        Rectangle r = PanelOf(Page.Confirm);
        var slide = new Vector2(0, Slide(a));
        r.Y += slide.Y;
        ui.Panel(r, a, 21);
        ui.Heading(_confirmTitle, new Vector2(r.X + r.Width / 2, r.Y + 62 * s), 22 * s, Ui.A(Ui.Ivory, a));
        ui.Text(_confirmBody, new Vector2(r.X + r.Width / 2, r.Y + 104 * s), 18 * s, Ui.A(Palette.Parchment, 0.85f * a));
        int sel = _selected.GetValueOrDefault(Page.Confirm);
        for (int i = 0; i < _confirm.Length; i++) DrawItem(_confirm[i], i == sel, 27 * s, a, Align.Center, slide);
    }
}
