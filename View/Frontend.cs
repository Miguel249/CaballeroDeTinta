using System.Numerics;
using CaballeroDeTinta.Art;
using Raylib_cs;

namespace CaballeroDeTinta.View;

enum FrontCmd { None, Start, Resume, ToMenu, Quit }

/// <summary>Entrada de los menús: teclado, ratón y, si hay uno conectado, mando.</summary>
readonly record struct UiInput(bool Up, bool Down, bool Left, bool Right, bool Confirm, bool Back, Vector2 Mouse, bool MouseMoved, bool Click, bool MouseHeld)
{
    public static UiInput Read()
    {
        bool pad = Raylib.IsGamepadAvailable(0);
        static bool K(KeyboardKey k) => Raylib.IsKeyPressed(k) || Raylib.IsKeyPressedRepeat(k);
        bool P(GamepadButton b) => pad && Raylib.IsGamepadButtonPressed(0, b);
        return new UiInput(
            Up: K(KeyboardKey.Up) || K(KeyboardKey.W) || P(GamepadButton.LeftFaceUp),
            Down: K(KeyboardKey.Down) || K(KeyboardKey.S) || P(GamepadButton.LeftFaceDown),
            Left: K(KeyboardKey.Left) || K(KeyboardKey.A) || P(GamepadButton.LeftFaceLeft),
            Right: K(KeyboardKey.Right) || K(KeyboardKey.D) || P(GamepadButton.LeftFaceRight),
            Confirm: Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || Raylib.IsKeyPressed(KeyboardKey.Space) || P(GamepadButton.RightFaceDown),
            Back: Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.Backspace) || P(GamepadButton.RightFaceRight),
            Mouse: Raylib.GetMousePosition(),
            MouseMoved: Raylib.GetMouseDelta().LengthSquared() > 0,
            Click: Raylib.IsMouseButtonPressed(MouseButton.Left),
            MouseHeld: Raylib.IsMouseButtonDown(MouseButton.Left));
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
        public Rectangle Hit;
        public float Hover, Press;
    }

    readonly Item[] _main = [new("Iniciar partida"), new("Controles"), new("Opciones"), new("Salir")];
    readonly Item[] _pause = [new("Continuar"), new("Controles"), new("Opciones"), new("Volver al menú")];
    readonly Item[] _options =
    [
        new("Música", Kind.Slider), new("Efectos", Kind.Slider), new("Sensibilidad del ratón", Kind.Slider),
        new("Mostrar consejos", Kind.Toggle), new("Restablecer consejos"),
        new("Esqueletos con modelo 3D", Kind.Toggle),
    ];
    readonly Item[] _confirm = [new("Sí"), new("No")];
    readonly Item _back = new("Volver");

    readonly Stack<Page> _pages = new();
    readonly Dictionary<Page, int> _selected = [];
    string _confirmTitle = "", _confirmBody = "";
    Func<FrontCmd> _confirmYes = () => FrontCmd.None;
    float _pageT, _time;
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
        _time += dt;
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

        foreach (Item it in items) { it.Hover = Ui.Approach(it.Hover, it == ItemAt(items, hovered) ? 1 : 0, dt / 0.12f); it.Press = MathF.Max(0, it.Press - dt / 0.15f); }
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
                if (i == 5) { settings.ModelSkeletons = !settings.ModelSkeletons; settings.Save(); }
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
            Page.Pause => new Vector2(430, 400),
            Page.Controls => new Vector2(1120, 610),
            Page.Options => new Vector2(680, 620),
            Page.Confirm => new Vector2(540, 250),
            _ => Vector2.Zero,
        } * s;
        size = Vector2.Min(size, new Vector2(ui.W, ui.H) - new Vector2(ui.Margin * 2));
        return new Rectangle(ui.W / 2 - size.X / 2, ui.H / 2 - size.Y / 2, size.X, size.Y);
    }

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
                Rectangle r = PanelOf(page);
                for (int i = 0; i < _pause.Length; i++)
                    _pause[i].Hit = Centered(new Vector2(r.X + r.Width / 2, r.Y + 132 * s + i * 52 * s), new Vector2(300, 44) * s);
                _back.Hit = ui.HintRect(new Vector2(r.X + r.Width / 2, r.Y + r.Height - 40 * s), "ESC", "Volver");
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
                _back.Hit = ui.HintRect(new Vector2(r.X + r.Width / 2, r.Y + r.Height - 40 * s), "ESC", "Volver");
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

    void DrawItem(Item it, bool selected, float size, float a, Align align = Align.Center)
    {
        float s = ui.S;
        Vector2 c = new(it.Hit.X + it.Hit.Width / 2, it.Hit.Y + it.Hit.Height / 2);
        if (align == Align.Left) c.X = it.Hit.X + 34 * s;
        Vector2 at = c + new Vector2(it.Hover * 2 * s, it.Press * 1.5f * s);
        float tw = ui.Measure(it.Label, size, Face.Display, 2).X;
        float left = align == Align.Left ? at.X : at.X - tw / 2;

        Color color = it.Disabled
            ? Ui.A(Palette.Parchment, 0.28f * a)
            : Ui.A(selected ? Ui.Ivory : Palette.Parchment, (selected ? 1f : 0.62f + 0.25f * it.Hover) * a);
        ui.Text(it.Label, at, size, color, Face.Display, 2, align, shadow: true);

        if (selected && !it.Disabled)
        {
            float bob = MathF.Sin(_time * 2.4f) * 1.3f * s;
            ui.Flame(new Vector2(left - 18 * s + bob * 0.4f, at.Y + size * 0.36f), size * 0.62f, a, it.Press);
        }
        if (it.Hover > 0.01f && !it.Disabled)
        {
            // Subrayado de pluma que crece desde el centro de la palabra.
            float mid = left + tw / 2, half = tw / 2 * Ui.Smooth(it.Hover);
            float y = at.Y + size * 0.5f;
            Ui.Taper(new Vector2(mid, y), new Vector2(mid - half, y), 1.8f * s, Ui.A(Palette.Parchment, 0.8f * a), 3);
            Ui.Taper(new Vector2(mid, y), new Vector2(mid + half, y), 1.8f * s, Ui.A(Palette.Parchment, 0.8f * a), 3);
        }
    }

    void BackHint(Rectangle panel, float a) =>
        ui.Hint(new Vector2(panel.X + panel.Width / 2, panel.Y + panel.Height - 40 * ui.S), "ESC", "Volver", a, _back.Hover);

    /// <summary>Columna izquierda del menú principal: el mundo queda libre a la derecha, como en un cartel.</summary>
    float MainLeft => ui.Margin + 64 * ui.S;

    void DrawMain(float a)
    {
        float s = ui.S, x = MainLeft;
        int sel = _selected.GetValueOrDefault(Page.Main);
        // Sombra de tinta desde el borde izquierdo para que la columna se lea sobre cualquier fondo.
        Raylib.DrawRectangleGradientH(0, 0, (int)(ui.W * 0.62f), (int)ui.H, Ui.A(Palette.Ink, 0.72f * a), Ui.A(Palette.Ink, 0));
        float y = ui.H * 0.2f + Slide(a);
        string over = "UN CORTOMETRAJE EN TINTA";
        ui.Text(over, new Vector2(x, y - 64 * s), 15 * s, Ui.A(Palette.Parchment, 0.8f * a), Face.Display, 3.3f, Align.Left, shadow: true);
        float ow = ui.Measure(over, 15 * s, Face.Display, 3.3f).X;
        Ui.Taper(new Vector2(x + ow + 14 * s, y - 64 * s), new Vector2(x + ow + 60 * s, y - 64 * s), 1.8f * s, Ui.A(Palette.Parchment, 0.8f * a));
        float size = MathF.Min(86 * s, ui.W / 12f);
        ui.Text("El Caballero", new Vector2(x - 4 * s, y), size, Ui.A(Ui.Ivory, a), Face.Display, 1.5f, Align.Left, shadow: true);
        ui.Text("de Tinta", new Vector2(x + 60 * s, y + size * 0.92f), size, Ui.A(Ui.Ivory, a), Face.Display, 1.5f, Align.Left, shadow: true);
        float ry = y + size * 0.92f + 58 * s;
        Raylib.DrawPoly(new Vector2(x + 4 * s, ry), 4, 5.5f * s, 45, Ui.A(Palette.Parchment, 0.9f * a));
        Ui.Taper(new Vector2(x + 16 * s, ry), new Vector2(x + 330 * s, ry), 2 * s, Ui.A(Palette.Parchment, 0.9f * a), 5);

        if (DemoCompleted)
        {
            ui.Text("DEMO COMPLETADA", new Vector2(x, ry + 38 * s), 18 * s, Ui.A(Ui.Brass, a), Face.Display, 4, Align.Left, shadow: true);
            ui.Text("Baldomero III descansa. Gracias por jugar.", new Vector2(x, ry + 64 * s), 18 * s, Ui.A(Palette.Parchment, 0.85f * a), align: Align.Left, shadow: true);
        }
        for (int i = 0; i < _main.Length; i++) DrawItem(_main[i], i == sel, 30 * s, a, Align.Left);
        Rectangle hint = ui.HintRect(Vector2.Zero, "ENTER", "Aceptar");
        ui.Hint(new Vector2(x + hint.Width / 2 - 6 * s, ui.H - ui.Margin - 14 * s), "ENTER", "Aceptar", 0.8f * a);
    }

    void DrawPause(float a)
    {
        float s = ui.S;
        Rectangle r = PanelOf(Page.Pause);
        r.Y += Slide(a);
        ui.Panel(r, a, 3);
        ui.Heading("Pausa", new Vector2(r.X + r.Width / 2, r.Y + 62 * s), 24 * s, Ui.A(Ui.Ivory, a));
        int sel = _selected.GetValueOrDefault(Page.Pause);
        for (int i = 0; i < _pause.Length; i++)
        {
            Item it = _pause[i];
            it.Hit.Y += Slide(a);
            DrawItem(it, i == sel, 27 * s, a);
        }
        BackHint(r, a);
    }

    void PanelTitle(Rectangle r, string title, float a)
    {
        float s = ui.S;
        ui.Text(title, new Vector2(r.X + r.Width / 2, r.Y + 60 * s), 44 * s, Ui.A(Ui.Ivory, a), Face.Display, 2);
        ui.Divider(new Vector2(r.X + r.Width / 2, r.Y + 96 * s), 150 * s, Ui.A(Palette.Parchment, 0.85f * a));
    }

    static readonly (string Section, Gesture[][] Rows)[] ControlSheet =
    [
        ("Movimiento", [[new("Moverse", Glyph.Wasd)], [new("Cámara", Glyph.M(MouseMark.Move))]]),
        ("Combate", [[new("Ataque", Glyph.M(MouseMark.Left)), new("Ataque fuerte", Glyph.M(MouseMark.Right))],
                     [new("Esquivar", Glyph.K("ESPACIO")), new("Desviar", Glyph.K("F"))]]),
        ("Aventura", [[new("Curar", Glyph.K("Q")), new("Interactuar", Glyph.K("E"))],
                      [new("Fijar objetivo", Glyph.K("TAB"), Glyph.Or, Glyph.M(MouseMark.Wheel))],
                      [new("Pausa", Glyph.K("ESC"))]]),
    ];

    void DrawControls(float a)
    {
        float s = ui.S;
        Rectangle r = PanelOf(Page.Controls);
        r.Y += Slide(a);
        ui.Panel(r, a, 9);
        PanelTitle(r, "Controles", a);

        int cols = ControlSheet.Length;
        float inner = r.Width - 60 * s, colW = inner / cols;
        // La fila más alta manda: todas las columnas comparten la misma rejilla.
        float rowH = MathF.Min(118 * s, (r.Height - 250 * s) / 3);
        for (int c = 0; c < cols; c++)
        {
            var (section, rows) = ControlSheet[c];
            float cx = r.X + 30 * s + colW * (c + 0.5f);
            ui.Heading(section, new Vector2(cx, r.Y + 146 * s), 16 * s, Ui.A(Palette.Parchment, 0.9f * a));
            if (c > 0)
                Ui.Taper(new Vector2(r.X + 30 * s + colW * c, r.Y + 170 * s), new Vector2(r.X + 30 * s + colW * c, r.Y + r.Height - 90 * s), 1.2f * s, Ui.A(Palette.Parchment, 0.18f * a), 3);
            for (int row = 0; row < rows.Length; row++)
            {
                Gesture[] cells = rows[row];
                float glyphY = r.Y + 212 * s + row * rowH;
                float cellW = colW / cells.Length;
                float tall = cells.Max(g => g.Glyphs.Max(x => ui.GlyphSize(x).Y));
                for (int i = 0; i < cells.Length; i++)
                {
                    float x = cx - colW / 2 + cellW * (i + 0.5f);
                    ui.GlyphRow(cells[i].Glyphs, new Vector2(x, glyphY), a);
                    ui.Text(cells[i].Label, new Vector2(x, glyphY + tall / 2 + 17 * s), 17 * s, Ui.A(Palette.Parchment, 0.88f * a));
                }
            }
        }
        BackHint(r, a);
    }

    void DrawOptions(float a)
    {
        float s = ui.S;
        Rectangle r = PanelOf(Page.Options);
        float slide = Slide(a);
        r.Y += slide;
        ui.Panel(r, a, 15);
        PanelTitle(r, "Opciones", a);
        string[] groups = ["Sonido", "Control", "Consejos", "Gráficos"];
        int[] firsts = [0, 2, 3, 5];
        for (int g = 0; g < groups.Length; g++)
            ui.Heading(groups[g], new Vector2(r.X + r.Width / 2, OptionY(r, firsts[g]) - 34 * s), 14 * s, Ui.A(Palette.Parchment, 0.7f * a));

        int sel = _selected.GetValueOrDefault(Page.Options);
        for (int i = 0; i < _options.Length; i++)
        {
            Item it = _options[i];
            it.Hit.Y += slide;
            DrawItem(it, i == sel, 21 * s, a, Align.Left);
            float cy = it.Hit.Y + it.Hit.Height / 2;
            switch (it.Kind)
            {
                case Kind.Slider:
                {
                    Rectangle track = SliderTrack(it);
                    track.Y = cy;
                    float v = SliderValue(i);
                    Vector2 a0 = new(track.X, cy), a1 = new(track.X + track.Width, cy), knob = new(track.X + track.Width * v, cy);
                    Raylib.DrawLineEx(a0, a1, 1.4f * s, Ui.A(Palette.Parchment, 0.35f * a));
                    for (int t = 0; t <= 4; t++)
                    {
                        float tx = track.X + track.Width * t / 4;
                        Raylib.DrawLineEx(new Vector2(tx, cy - 4 * s), new Vector2(tx, cy + 4 * s), 1.2f * s, Ui.A(Palette.Parchment, 0.3f * a));
                    }
                    Ui.Taper(a0, knob, 3.2f * s, Ui.A(Palette.Parchment, 0.9f * a), 3);
                    bool hot = i == sel;
                    Raylib.DrawPoly(knob, 4, 9 * s, 45, Ui.A(Palette.Ink, a));
                    Raylib.DrawPoly(knob, 4, 6.5f * s, 45, Ui.A(hot ? Palette.Ember : Ui.Ivory, a));
                    string label = i == 2 ? $"×{settings.MouseSensitivity:0.0}" : $"{v * 100:0} %";
                    ui.Text(label, new Vector2(track.X + track.Width + 50 * s, cy), 16 * s, Ui.A(Palette.Parchment, 0.85f * a), align: Align.Right);
                    break;
                }
                case Kind.Toggle:
                {
                    bool on = i == 3 ? settings.Tutorials : settings.ModelSkeletons;
                    var box = Centered(new Vector2(it.Hit.X + it.Hit.Width - 150 * s, cy), new Vector2(22, 22) * s);
                    ui.InkFrame(box, 2 * s, Ui.A(Palette.Parchment, 0.9f * a), 77, 0.8f);
                    if (on)
                    {
                        // Marca de tinta en brasa, trazada con dos golpes de pluma.
                        Vector2 b0 = new(box.X + 4 * s, box.Y + 12 * s), b1 = new(box.X + 10 * s, box.Y + 18 * s), b2 = new(box.X + 21 * s, box.Y + 1 * s);
                        Ui.Taper(b1, b0, 3.4f * s, Ui.A(Palette.Ember, a), 2);
                        Ui.Taper(b1, b2, 3.4f * s, Ui.A(Palette.Ember, a), 3);
                    }
                    ui.Text(on ? "Sí" : "No", new Vector2(box.X + 40 * s, cy), 17 * s, Ui.A(Palette.Parchment, 0.85f * a), align: Align.Left);
                    break;
                }
                default:
                    if (it.Disabled) ui.Text("nada que restablecer", new Vector2(it.Hit.X + it.Hit.Width, cy), 15 * s, Ui.A(Palette.Parchment, 0.35f * a), align: Align.Right);
                    break;
            }
        }
        BackHint(r, a);
    }

    void DrawConfirm(float a)
    {
        float s = ui.S;
        Rectangle r = PanelOf(Page.Confirm);
        float slide = Slide(a);
        r.Y += slide;
        ui.Panel(r, a, 21);
        ui.Heading(_confirmTitle, new Vector2(r.X + r.Width / 2, r.Y + 62 * s), 22 * s, Ui.A(Ui.Ivory, a));
        ui.Text(_confirmBody, new Vector2(r.X + r.Width / 2, r.Y + 104 * s), 18 * s, Ui.A(Palette.Parchment, 0.85f * a));
        int sel = _selected.GetValueOrDefault(Page.Confirm);
        for (int i = 0; i < _confirm.Length; i++)
        {
            _confirm[i].Hit.Y += slide;
            DrawItem(_confirm[i], i == sel, 27 * s, a);
        }
    }
}
