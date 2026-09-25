using System.Text.Json;

namespace CaballeroDeTinta;

/// <summary>
/// Preferencias del jugador y consejos ya vistos. Se guardan en
/// <c>%APPDATA%\CaballeroDeTinta\ajustes.json</c>; si el archivo falta o está roto, valores por defecto.
/// </summary>
sealed class Settings
{
    public float Music { get; set; } = 0.8f;
    public float Effects { get; set; } = 0.9f;
    public float MouseSensitivity { get; set; } = 1f;
    public bool Tutorials { get; set; } = true;
    /// <summary>Esqueletos con el modelo 3D (glTF) en vez de primitivas.</summary>
    public bool ModelSkeletons { get; set; } = true;
    public HashSet<string> SeenTutorials { get; set; } = [];

    public const float MinSensitivity = 0.3f, MaxSensitivity = 2.5f;

    string? _path; // null: no se escribe nada (pruebas y capturas automáticas)

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CaballeroDeTinta", "ajustes.json");
        Settings s;
        try { s = File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { s = new(); }
        s._path = path;
        s.Music = Math.Clamp(s.Music, 0, 1);
        s.Effects = Math.Clamp(s.Effects, 0, 1);
        s.MouseSensitivity = Math.Clamp(s.MouseSensitivity, MinSensitivity, MaxSensitivity);
        s.SeenTutorials ??= [];
        return s;
    }

    public void Save()
    {
        if (_path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
