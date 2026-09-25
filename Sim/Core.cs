using System.Numerics;
using Box3D;
using Raylib_cs;

namespace CaballeroDeTinta.Sim;

/// <summary>Entrada de un fotograma, independiente de Raylib para poder simularla en pruebas.</summary>
readonly record struct Controls
{
    public Vector2 Move { get; init; }
    public Vector2 Look { get; init; }
    public bool Light { get; init; }
    public bool Heavy { get; init; }
    public bool Dodge { get; init; }
    public bool Parry { get; init; }
    public bool Heal { get; init; }
    public bool Interact { get; init; }
    public bool LockOn { get; init; }
    public bool Any => Light || Heavy || Dodge || Parry || Heal || Interact;
}

static class Cat
{
    public const ulong Static = 1, Actor = 2, Prop = 4;
    public static readonly QueryFilter OnlyStatic = new(ulong.MaxValue, Static);
    public static readonly QueryFilter NotActors = new(ulong.MaxValue, ~Actor);
}

enum FxKind { Spark, Hit, HeavyHit, Parry, Slam, Blood, Ring, Dust, Bell, Rest }

/// <summary>Sonidos que la simulación pide; la banda sonora decide cómo suenan.</summary>
enum Sfx
{
    Swing, HeavySwing, Dodge, Step, BoneStep, KingStep, Hit, HeavyHit, Knock, Clang, Parry, Hurt, Death,
    Shatter, Rattle, Scrape, Rise, Slam, Bump, Boing, Bell, Heal, Bonfire, Roar, KingFall,
}

/// <summary>Un sonido pedido en este fotograma. <c>Size</c> &gt; 1 lo hace más grave y fuerte.</summary>
readonly record struct Heard(Sfx Sfx, Vector3 Position, float Size);

/// <summary>Un efecto que la simulación pide y la vista dibuja (estrellas, estallidos de tinta...).</summary>
struct Fx
{
    public FxKind Kind;
    public Vector3 Position;
    public Vector3 Direction;
    public float Age, Life, Size;
    public int Seed;
}

/// <summary>Pieza física suelta: escombros, barriles, tambores de columna, huesos.</summary>
sealed class Prop
{
    public required Body Body;
    public required PropKind Kind;
    public Vector3 Size;
    public Color Color;
}

enum PropKind { Crate, Barrel, Drum, Rock, Bone, Skull, Bell, Link }

/// <summary>Arquitectura: estática, con o sin cuerpo físico.</summary>
struct Piece
{
    public Art.Shape3 Shape;
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Size;
    public Color Color;
    public float Emissive;
}

/// <summary>Personaje con cuerpo de cápsula movido por velocidad (sin rotación física).</summary>
abstract class Actor
{
    public Body Body;
    public float Yaw;             // 0 = mira hacia +Z
    public float Health, MaxHealth;
    public float StateTime;
    public float Invulnerable;
    public float HurtFlash;
    public bool Dead;
    public float Radius, HalfHeight;
    public Vector3 Feet => Body.Position - new Vector3(0, HalfHeight, 0);
    public Vector3 Forward => new(MathF.Sin(Yaw), 0, MathF.Cos(Yaw));
    public abstract bool Parryable { get; }

    protected void CreateBody(PhysicsWorld world, Vector3 feet, float radius, float height, float density, bool sensorEvents, ulong id)
    {
        Radius = radius;
        HalfHeight = height / 2;
        Body = world.CreateBody(BodyDefinition.Dynamic(feet + new Vector3(0, HalfHeight, 0)) with
        {
            MotionLocks = MotionLocks.NoRotation,
            CanSleep = false,
        });
        Body.AddCapsule(Capsule.Upright(height, radius), ShapeDefinition.Default with
        {
            Density = density,
            Friction = 0f,
            Filter = new CollisionFilter(Cat.Actor, ulong.MaxValue),
            EnableSensorEvents = sensorEvents,
        });
        Body.UserData = id;
    }

    /// <summary>Velocidad horizontal deseada; la vertical la decide la gravedad.</summary>
    protected void Steer(Vector3 wish, float response = 1f)
    {
        Vector3 v = Body.LinearVelocity;
        v.X += (wish.X - v.X) * response;
        v.Z += (wish.Z - v.Z) * response;
        Body.LinearVelocity = v;
    }

    protected void TurnTowards(float targetYaw, float maxStep)
    {
        float d = MathF.IEEERemainder(targetYaw - Yaw, MathF.Tau);
        Yaw += Math.Clamp(d, -maxStep, maxStep);
    }

    public static float YawOf(Vector3 dir) => MathF.Atan2(dir.X, dir.Z);
}

/// <summary>Callback de solapamiento sin asignaciones: vuelca las formas en una lista reutilizada.</summary>
struct CollectShapes : IOverlapCallback
{
    public List<Shape> Found;
    public bool OnOverlap(Shape shape)
    {
        Found.Add(shape);
        return true;
    }
}
