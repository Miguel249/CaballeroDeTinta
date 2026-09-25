using Raylib_cs;

namespace CaballeroDeTinta.Art;

/// <summary>Piezas de la interfaz que pueden sustituirse por una ilustración dibujada a mano.</summary>
enum UiArt { PauseInk, Divider, Crest, Flask, OrnamentCorner }

/// <summary>
/// Ilustraciones opcionales en <c>Assets/UI</c>. Si un archivo existe, el kit lo usa en lugar del dibujo
/// procedural; si no, <see cref="Draw"/> devuelve <c>false</c> y se dibuja con geometría como siempre.
/// Así se puede ir sustituyendo pieza a pieza sin tocar <c>Frontend</c> ni <c>Hud</c>.
/// <list type="bullet">
/// <item><c>pause_ink.png</c>: pincelada horizontal (izquierda → derecha) detrás de la pausa.</item>
/// <item><c>divider_01.png</c>: filete horizontal centrado.</item>
/// <item><c>crest.png</c>: emblema del caballero, cuadrado.</item>
/// <item><c>flask.png</c>: frasco lleno; vacío se tiñe de oscuro.</item>
/// <item><c>ornament_corner.png</c>: florón de la esquina superior izquierda (se gira para las demás).</item>
/// </list>
/// Todas se tiñen con el color pedido: conviene dibujarlas en blanco sobre transparente.
/// </summary>
sealed class UiSkin : IDisposable
{
    readonly Dictionary<UiArt, Texture2D> _art = [];

    public UiSkin(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (UiArt a in Enum.GetValues<UiArt>())
        {
            string path = Path.Combine(folder, FileOf(a));
            if (!File.Exists(path)) continue;
            Texture2D t = Raylib.LoadTexture(path);
            if (t.Id == 0) continue;
            Raylib.GenTextureMipmaps(ref t);
            Raylib.SetTextureFilter(t, TextureFilter.Trilinear);
            _art[a] = t;
        }
    }

    static string FileOf(UiArt a) => a switch
    {
        UiArt.PauseInk => "pause_ink.png",
        UiArt.Divider => "divider_01.png",
        UiArt.Crest => "crest.png",
        UiArt.Flask => "flask.png",
        _ => "ornament_corner.png",
    };

    public bool Has(UiArt a) => _art.ContainsKey(a);

    /// <summary>Dibuja la ilustración centrada en <paramref name="dest"/> (X, Y son el centro), girada en grados.</summary>
    public bool Draw(UiArt a, Rectangle dest, Color tint, float rotation = 0)
    {
        if (!_art.TryGetValue(a, out Texture2D t)) return false;
        var src = new Rectangle(0, 0, t.Width, t.Height);
        Raylib.DrawTexturePro(t, src, dest, new System.Numerics.Vector2(dest.Width / 2, dest.Height / 2), rotation, tint);
        return true;
    }

    public void Dispose()
    {
        foreach (Texture2D t in _art.Values) Raylib.UnloadTexture(t);
        _art.Clear();
    }
}
