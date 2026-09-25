using System.Numerics;
using CaballeroDeTinta.Art;
using Raylib_cs;

namespace CaballeroDeTinta.View;

/// <summary>
/// Pose del rey en unidades de su dibujo original (pies en el origen, ~6 m, mira a +Z).
/// <paramref name="Clip"/> da la base del cuerpo (null: de pie, en reposo); con <paramref name="ArmIK"/> = 1
/// los brazos van a <paramref name="HandR"/>/<paramref name="HandL"/> como en el dibujo original.
/// </summary>
readonly record struct KingPose(
    Vector3 HandR, Vector3 HandL, float ArmIK, float HeadTilt, float HeadRoll, float Crown,
    string? Clip, float ClipTime, bool Loop, float Now);

/// <summary>
/// Baldomero III con la malla, el esqueleto (Mixamo) y la textura generados con Meshy, juntados con
/// <c>tools/merge_meshy.py</c>. El cuerpo toma la base de sus animaciones (andar, correr, desplomarse,
/// levantarse, morir) y encima se aplica la animación de código de siempre: los brazos se estiran como
/// mangueras hasta donde el dibujo original ponía las manos y la corona se escurre sobre los ojos.
/// La piel se deforma en CPU con los pesos del rig.
/// </summary>
sealed unsafe class KingPuppet : IDisposable
{
    /// <summary>Unidades del dibujo original por unidad del modelo (el modelo mide 1,75; el rey, 5,8 m).</summary>
    public const float Scale = 3.3f;

    readonly Puppet _p;
    public Model Model => _p.Model;

    readonly Vector3[] _rest, _restN;
    readonly Matrix4x4[] _bindInv, _base, _from, _pose, _skin;
    readonly Transform[] _bind;
    readonly int _bones;
    readonly int _head, _headTop, _headFront, _armR, _foreR, _handR, _fingerR, _armL, _foreL, _handL, _fingerL;
    const float CrownFrom = 1.6f;                        // altura (modelo) desde la que empieza la corona
    static readonly Vector3 CrownBase = new(0, 1.6f, 0.22f);

    string? _clip;
    float _changedAt = -99;

    KingPuppet(Puppet p)
    {
        _p = p;
        Mesh mesh = Model.Meshes[0];
        int n = mesh.VertexCount;
        _rest = new Vector3[n];
        _restN = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            _rest[i] = new Vector3(mesh.Vertices[i * 3], mesh.Vertices[i * 3 + 1], mesh.Vertices[i * 3 + 2]);
            _restN[i] = new Vector3(mesh.Normals[i * 3], mesh.Normals[i * 3 + 1], mesh.Normals[i * 3 + 2]);
        }
        _bones = Model.Skeleton.BoneCount;
        _bind = new Transform[_bones];
        _bindInv = new Matrix4x4[_bones];
        _base = new Matrix4x4[_bones];
        _from = new Matrix4x4[_bones];
        _pose = new Matrix4x4[_bones];
        _skin = new Matrix4x4[_bones];
        for (int i = 0; i < _bones; i++)
        {
            _bind[i] = Model.Skeleton.BindPose[i];
            Matrix4x4.Invert(Trs(_bind[i]), out _bindInv[i]);
            _pose[i] = _from[i] = Trs(_bind[i]);
        }
        int B(string name) => p.Bone("mixamorig:" + name) is var b and >= 0 ? b : p.Bone(name);
        _head = B("Head"); _headTop = B("HeadTop_End"); _headFront = B("headfront");
        // La mano "derecha" del dibujo original está en +X, que en el modelo (mira a +Z) es su izquierda.
        _armR = B("LeftArm"); _foreR = B("LeftForeArm"); _handR = B("LeftHand"); _fingerR = B("LeftHandMiddle4");
        _armL = B("RightArm"); _foreL = B("RightForeArm"); _handL = B("RightHand"); _fingerL = B("RightHandMiddle4");
    }

    public static KingPuppet? TryLoad(string file)
    {
        Puppet? p = Puppet.TryLoad(file);
        if (p == null) return null;
        Mesh m = p.Model.Meshes[0];
        if (p.Model.Skeleton.BoneCount == 0 || m.BoneIndices == null || m.AnimVertices == null || m.AnimNormals == null) { p.Dispose(); return null; }
        var k = new KingPuppet(p);
        if (k._handR < 0 || k._handL < 0 || k._head < 0 || k._armR < 0 || k._armL < 0) { k.Dispose(); return null; }
        return k;
    }

    public float Length(string clip) => _p.Length(clip);

    static Matrix4x4 Trs(Transform t) =>
        Matrix4x4.CreateScale(t.Scale) * Matrix4x4.CreateFromQuaternion(t.Rotation) * Matrix4x4.CreateTranslation(t.Translation);

    /// <summary>De las unidades del modelo a las del dibujo original (el modelo ya tiene los pies en y = 0).</summary>
    public static Matrix4x4 ToKing => Matrix4x4.CreateScale(Scale);

    /// <summary>Mano (en unidades del dibujo original) tras la última pose: donde va la espada.</summary>
    public Vector3 Hand(bool right) => _pose[right ? _handR : _handL].Translation * Scale;

    public void Pose(KingPose pose)
    {
        // 1. Base: el clip (o el reposo), fundida con la anterior durante 0,2 s al cambiar.
        if (pose.Clip != _clip)
        {
            Array.Copy(_pose, _from, _bones);
            _clip = pose.Clip;
            _changedAt = pose.Now;
        }
        float blend = Math.Clamp((pose.Now - _changedAt) / 0.2f, 0, 1);
        float frame = pose.Clip == null ? 0 : ClipTime(pose);
        for (int i = 0; i < _bones; i++)
        {
            Matrix4x4 m = Trs(pose.Clip == null ? _bind[i] : _p.BonePose(pose.Clip, i, frame));
            _base[i] = blend >= 1 ? m : Lerp(_from[i], m, blend);
        }
        Array.Copy(_base, _pose, _bones);

        // 2. Brazos de goma hacia las manos del dibujo original, y la cabeza.
        if (pose.ArmIK > 0)
        {
            Reach(_armR, _foreR, _handR, _fingerR, pose.HandR / Scale, pose.ArmIK);
            Reach(_armL, _foreL, _handL, _fingerL, pose.HandL / Scale, pose.ArmIK);
        }
        if (pose.HeadTilt != 0 || pose.HeadRoll != 0)
        {
            Vector3 pivot = _pose[_head].Translation;
            Matrix4x4 turn = Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateFromYawPitchRoll(0, pose.HeadTilt, pose.HeadRoll) * Matrix4x4.CreateTranslation(pivot);
            foreach (int b in new[] { _head, _headTop, _headFront })
                if (b >= 0) _pose[b] *= turn;
        }

        // 3. Piel: la corona se escurre antes de deformar (va pegada a la cabeza) y luego los pesos del rig.
        for (int i = 0; i < _bones; i++) _skin[i] = _bindInv[i] * _pose[i];
        float c = pose.Crown;
        Quaternion crownQ = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.12f + c * 0.25f) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, c * 0.3f);
        Matrix4x4 crown = Matrix4x4.CreateTranslation(-CrownBase) * Matrix4x4.CreateScale(1 + c * 0.55f, 1, 1 + c * 0.45f)
                        * Matrix4x4.CreateFromQuaternion(crownQ) * Matrix4x4.CreateTranslation(CrownBase - new Vector3(0, c * 0.17f, -c * 0.02f));

        Mesh mesh = Model.Meshes[0];
        for (int v = 0; v < _rest.Length; v++)
        {
            Vector3 p = _rest[v], nrm = _restN[v];
            if (c > 0 && p.Y > CrownFrom)
            {
                float w = Math.Clamp((p.Y - CrownFrom) / 0.04f, 0, 1);
                p = Vector3.Lerp(p, Vector3.Transform(p, crown), w);
                nrm = Vector3.Lerp(nrm, Vector3.Transform(nrm, crownQ), w);
            }
            Vector3 sp = Vector3.Zero, sn = Vector3.Zero;
            for (int j = 0; j < 4; j++)
            {
                float w = mesh.BoneWeights[v * 4 + j];
                if (w <= 0) continue;
                Matrix4x4 k = _skin[mesh.BoneIndices[v * 4 + j]];
                sp += Vector3.Transform(p, k) * w;
                sn += Vector3.TransformNormal(nrm, k) * w;
            }
            sn = sn.LengthSquared() > 1e-8f ? Vector3.Normalize(sn) : nrm;
            mesh.AnimVertices[v * 3] = sp.X; mesh.AnimVertices[v * 3 + 1] = sp.Y; mesh.AnimVertices[v * 3 + 2] = sp.Z;
            mesh.AnimNormals[v * 3] = sn.X; mesh.AnimNormals[v * 3 + 1] = sn.Y; mesh.AnimNormals[v * 3 + 2] = sn.Z;
        }
        Raylib.UpdateMeshBuffer(mesh, 0, mesh.AnimVertices, _rest.Length * 12, 0);
        Raylib.UpdateMeshBuffer(mesh, 2, mesh.AnimNormals, _rest.Length * 12, 0);
    }

    float ClipTime(KingPose pose)
    {
        float len = _p.Length(pose.Clip!);
        return pose.Loop && len > 0 ? (pose.ClipTime % len + len) % len : Math.Clamp(pose.ClipTime, 0, len);
    }

    /// <summary>
    /// Brazo de manguera: gira la cadena desde el hombro para que la mano llegue al objetivo y la estira
    /// (o encoge) lo que haga falta. La mano gira con el brazo pero no se estira.
    /// </summary>
    void Reach(int arm, int fore, int hand, int finger, Vector3 target, float amount)
    {
        Vector3 s = _pose[arm].Translation, h = _pose[hand].Translation;
        Vector3 d0 = h - s, d1 = target - s;
        if (d0.LengthSquared() < 1e-6f || d1.LengthSquared() < 1e-6f) return;
        Vector3 axis = Vector3.Normalize(d0);
        Quaternion turn = Quaternion.Slerp(Quaternion.Identity, Ink.FromTo(axis, Vector3.Normalize(d1)), amount);
        float k = 1 + (Math.Clamp(d1.Length() / d0.Length(), 0.55f, 2.4f) - 1) * amount;
        Matrix4x4 stretch = Matrix4x4.Identity + new Matrix4x4(
            axis.X * axis.X, axis.X * axis.Y, axis.X * axis.Z, 0,
            axis.Y * axis.X, axis.Y * axis.Y, axis.Y * axis.Z, 0,
            axis.Z * axis.X, axis.Z * axis.Y, axis.Z * axis.Z, 0,
            0, 0, 0, 0) * (k - 1);
        Matrix4x4 limb = Matrix4x4.CreateTranslation(-s) * stretch * Matrix4x4.CreateFromQuaternion(turn) * Matrix4x4.CreateTranslation(s);
        _pose[arm] *= limb;
        _pose[fore] *= limb;
        Vector3 newHand = Vector3.Transform(h, limb);
        Matrix4x4 rigid = Matrix4x4.CreateTranslation(-h) * Matrix4x4.CreateFromQuaternion(turn) * Matrix4x4.CreateTranslation(newHand);
        _pose[hand] *= rigid;
        if (finger >= 0) _pose[finger] *= rigid;
    }

    static Matrix4x4 Lerp(Matrix4x4 a, Matrix4x4 b, float t)
    {
        Matrix4x4.Decompose(a, out Vector3 sa, out Quaternion ra, out Vector3 ta);
        Matrix4x4.Decompose(b, out Vector3 sb, out Quaternion rb, out Vector3 tb);
        return Matrix4x4.CreateScale(Vector3.Lerp(sa, sb, t)) * Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(ra, rb, t)) * Matrix4x4.CreateTranslation(Vector3.Lerp(ta, tb, t));
    }

    public void Dispose() => _p.Dispose();
}
