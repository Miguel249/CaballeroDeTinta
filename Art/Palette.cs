using Raylib_cs;

namespace CaballeroDeTinta.Art;

/// <summary>Paleta restringida de cine de animación antiguo. Los colores saturados se reservan a lo sobrenatural.</summary>
static class Palette
{
    public static readonly Color Parchment = new(226, 210, 176, 255);
    public static readonly Color DirtyCream = new(205, 190, 158, 255);
    public static readonly Color Charcoal = new(28, 24, 22, 255);
    public static readonly Color Ink = new(14, 11, 10, 255);
    public static readonly Color Umber = new(112, 86, 62, 255);
    public static readonly Color DarkUmber = new(72, 56, 44, 255);
    public static readonly Color Stone = new(150, 138, 118, 255);
    public static readonly Color DarkStone = new(98, 90, 80, 255);
    public static readonly Color ForestGreen = new(78, 92, 66, 255);
    public static readonly Color Burgundy = new(112, 38, 40, 255);
    public static readonly Color DustyBlue = new(96, 112, 128, 255);
    public static readonly Color OldGold = new(176, 140, 64, 255);
    public static readonly Color Bone = new(222, 212, 186, 255);
    public static readonly Color Steel = new(150, 150, 146, 255);

    // Lo sobrenatural: estos sí van saturados.
    public static readonly Color Ember = new(255, 124, 20, 255);
    public static readonly Color GhostCyan = new(90, 230, 230, 255);
    public static readonly Color Crimson = new(170, 10, 24, 255);

    public static readonly Color SkyTop = new(120, 116, 112, 255);
    public static readonly Color SkyHorizon = new(214, 196, 160, 255);
    public static readonly Color Fog = new(190, 174, 146, 255);

    public static Color Mix(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t),
        (byte)(a.A + (b.A - a.A) * t));

    public static Color Alpha(Color c, float a) => new(c.R, c.G, c.B, (byte)(Math.Clamp(a, 0, 1) * 255));
}
