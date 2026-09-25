using System.Numerics;
using Raylib_cs;

namespace CaballeroDeTinta.Art;

/// <summary>
/// Un modelo glTF con esqueleto y sus animaciones, para dibujarlo con la tinta de <see cref="Ink"/>.
/// raylib deforma los vértices en CPU al posar, así que varias instancias comparten el mismo modelo:
/// se posa, se dibuja, y la siguiente vuelve a posarlo.
/// </summary>
sealed unsafe class Puppet : IDisposable
{
    /// <summary>raylib hornea las animaciones glTF a 60 fotogramas por segundo.</summary>
    public const float Fps = 60f;

    public Model Model;
    readonly ModelAnimation* _anims;
    readonly int _animCount;
    readonly Dictionary<string, int> _clips = [];
    readonly Dictionary<int, Transform[][]> _tracks = [];   // clip → fotograma → hueso

    Puppet(string path)
    {
        Model = Raylib.LoadModel(path);
        int count = 0;
        _anims = Raylib.LoadModelAnimations(path, ref count);
        _animCount = count;
        for (int i = 0; i < count; i++)
            _clips[new string((sbyte*)_anims[i].Name)] = i;
    }

    /// <summary>Carpeta de modelos junto al ejecutable.</summary>
    public static string PathOf(string file) => Path.Combine(AppContext.BaseDirectory, "Assets", "Models", file);

    /// <summary>Carga el modelo si existe; si falta o está roto, <c>null</c> y el juego sigue con las primitivas.</summary>
    public static Puppet? TryLoad(string file)
    {
        string path = PathOf(file);
        if (!File.Exists(path)) return null;
        var p = new Puppet(path);
        if (p.Model.MeshCount > 0) return p;
        p.Dispose();
        return null;
    }

    /// <summary>Modelo estático (armas, accesorios) o <c>null</c>.</summary>
    public static Model? TryLoadProp(string file)
    {
        string path = PathOf(file);
        if (!File.Exists(path)) return null;
        Model m = Raylib.LoadModel(path);
        return m.MeshCount > 0 ? m : null;
    }

    public bool Has(string clip) => _clips.ContainsKey(clip);

    /// <summary>
    /// Desplazamiento horizontal que el clip mete en la cadera respecto a su inicio. Algunos clips
    /// (esquivas) se mueven solos; la física ya mueve al personaje, así que la vista lo resta.
    /// </summary>
    public Vector3 Drift(string clip, int hips, float time)
    {
        Vector3 d = BoneAt(clip, hips, time).Translation - BoneAt(clip, hips, 0).Translation;
        return d with { Y = 0 };
    }

    /// <summary>Duración del clip en segundos.</summary>
    public float Length(string clip) => _clips.TryGetValue(clip, out int i) ? (_anims[i].KeyFrameCount - 1) / Fps : 0;

    public int Bone(string name)
    {
        for (int i = 0; i < Model.Skeleton.BoneCount; i++)
            if (new string((sbyte*)Model.Skeleton.Bones[i].Name) == name) return i;
        return -1;
    }

    /// <summary>
    /// Posa el modelo en el instante <paramref name="time"/> del clip, mezclado con otro clip si
    /// <paramref name="blend"/> &lt; 1 (fundidos entre estados).
    /// </summary>
    public void Pose(string clip, float time, bool loop, string? from = null, float fromTime = 0, bool fromLoop = true, float blend = 1)
    {
        if (!_clips.TryGetValue(clip, out int a)) return;
        float fa = Frame(a, time, loop);
        if (from != null && blend < 1 && _clips.TryGetValue(from, out int b))
            Raylib.UpdateModelAnimationEx(Model, _anims[b], Frame(b, fromTime, fromLoop), _anims[a], fa, Math.Clamp(blend, 0, 1));
        else
            Raylib.UpdateModelAnimation(Model, _anims[a], fa);
    }

    float Frame(int clip, float time, bool loop)
    {
        // raylib deja el último fotograma horneado en la pose de reposo: los clips que no se repiten
        // se detienen en el penúltimo (si no, un muerto se levantaría al terminar de caer).
        float last = _anims[clip].KeyFrameCount - 1;
        float f = time * Fps;
        return loop ? (f % last + last) % last : Math.Clamp(f, 0, MathF.Max(0, last - 1));
    }

    /// <summary>Transformación de un hueso en el espacio del modelo, en la pose actual (convención de System.Numerics).</summary>
    public Matrix4x4 BoneWorld(int bone)
    {
        Transform t = Model.CurrentPose[bone];
        return Matrix4x4.CreateScale(t.Scale) * Matrix4x4.CreateFromQuaternion(t.Rotation) * Matrix4x4.CreateTranslation(t.Translation);
    }

    /// <summary>
    /// Pose de un hueso (espacio del modelo) en cualquier instante de un clip, sin tocar la pose actual
    /// del modelo. El recorrido de todos los huesos se calcula una vez por clip, de una pasada, y se guarda.
    /// </summary>
    public Transform BonePose(string clip, int bone, float time)
    {
        if (!_clips.TryGetValue(clip, out int a) || bone < 0) return Model.Skeleton.BindPose[Math.Max(0, bone)];
        if (!_tracks.TryGetValue(a, out Transform[][]? track))
        {
            int bones = Model.Skeleton.BoneCount;
            // El último fotograma horneado es la pose de reposo (ver Frame): no se guarda.
            track = new Transform[Math.Max(1, _anims[a].KeyFrameCount - 1)][];
            for (int f = 0; f < track.Length; f++)
            {
                Raylib.UpdateModelAnimation(Model, _anims[a], f);
                track[f] = new Transform[bones];
                for (int b = 0; b < bones; b++) track[f][b] = Model.CurrentPose[b];
            }
            _tracks[a] = track;
        }
        float fr = Math.Clamp(time * Fps, 0, track.Length - 1);
        int i0 = (int)fr, i1 = Math.Min(i0 + 1, track.Length - 1);
        float u = fr - i0;
        Transform t0 = track[i0][bone], t1 = track[i1][bone];
        return new Transform
        {
            Translation = Vector3.Lerp(t0.Translation, t1.Translation, u),
            Rotation = Quaternion.Slerp(t0.Rotation, t1.Rotation, u),
            Scale = Vector3.Lerp(t0.Scale, t1.Scale, u),
        };
    }

    public Matrix4x4 BoneAt(string clip, int bone, float time)
    {
        if (!_clips.ContainsKey(clip) || bone < 0) return Matrix4x4.Identity;
        Transform t = BonePose(clip, bone, time);
        return Matrix4x4.CreateScale(t.Scale) * Matrix4x4.CreateFromQuaternion(t.Rotation) * Matrix4x4.CreateTranslation(t.Translation);
    }

    public void Dispose()
    {
        if (_anims != null) Raylib.UnloadModelAnimations(_anims, _animCount);
        Raylib.UnloadModel(Model);
    }
}
