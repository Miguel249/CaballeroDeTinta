using System.Numerics;
using Box3D;

namespace CaballeroDeTinta.Sim;

enum FoeState { Idle, Chase, Windup, Recover, Flinch, Stagger, Dead }

/// <summary>Soldado esqueleto: lento, rítmico y peligroso si le das la espalda.</summary>
sealed class Skeleton : Actor
{
    public FoeState State;
    public Vector3 Home;
    public bool Struck;
    public float WalkPhase;
    public override bool Parryable => State == FoeState.Windup;

    const float Windup = 0.7f, Recover = 0.85f;

    public Skeleton(PhysicsWorld world, Vector3 feet, ulong id, float yaw)
    {
        MaxHealth = Health = 95;
        Home = feet;
        Yaw = yaw;
        CreateBody(world, feet, 0.32f, 1.85f, 60f, sensorEvents: false, id);
    }

    void Enter(FoeState s) { State = s; StateTime = 0; Struck = false; }

    public void Think(Kingdom k, float dt)
    {
        StateTime += dt;
        HurtFlash = MathF.Max(0, HurtFlash - dt);
        if (State == FoeState.Dead) return;
        Knight p = k.Player;
        Vector3 to = p.Body.Position - Body.Position;
        to.Y = 0;
        float d = to.Length();
        Vector3 dir = d > 1e-3f ? to / d : Forward;
        float lastPhase = WalkPhase;
        WalkPhase += new Vector2(Body.LinearVelocity.X, Body.LinearVelocity.Z).Length() * dt * 2.6f;
        if ((int)WalkPhase != (int)lastPhase) k.Play(Sfx.BoneStep, Feet);

        switch (State)
        {
            case FoeState.Idle:
                Steer((Home - Feet) with { Y = 0 } * 0.8f, 0.2f);
                if (!p.Dead && d < 11f) Enter(FoeState.Chase);
                break;
            case FoeState.Chase:
                if (p.Dead || d > 16f) { Enter(FoeState.Idle); break; }
                TurnTowards(YawOf(dir), dt * 5);
                Steer(Forward * 2.7f, 0.2f);
                if (d < 2.0f) { Enter(FoeState.Windup); k.Play(Sfx.Rattle, Body.Position); }
                break;
            case FoeState.Windup:
                Steer(Vector3.Zero, 0.3f);
                if (StateTime < Windup * 0.7f) TurnTowards(YawOf(dir), dt * 3);
                if (StateTime >= Windup)
                {
                    Enter(FoeState.Recover);
                    k.Play(Sfx.Swing, Body.Position + Forward, 1.15f);
                    k.EnemyStrike(this, Body.Position + Forward * 1.2f, 1.35f, 22, parryable: true);
                }
                break;
            case FoeState.Recover:
                Steer(StateTime < 0.15f ? Forward * 3f : Vector3.Zero, 0.3f);
                if (StateTime > Recover) Enter(FoeState.Chase);
                break;
            case FoeState.Flinch:
                Steer(Vector3.Zero, 0.1f);
                if (StateTime > 0.35f) Enter(FoeState.Chase);
                break;
            case FoeState.Stagger:
                Steer(Vector3.Zero, 0.2f);
                if (StateTime > 1.7f) Enter(FoeState.Chase);
                break;
        }
    }

    public void OnParried() => Enter(FoeState.Stagger);

    public void TakeHit(Kingdom k, float damage, Vector3 from, bool heavy)
    {
        if (State == FoeState.Dead) return;
        Health -= damage;
        HurtFlash = 0.2f;
        Vector3 push = Body.Position - from;
        push.Y = 0;
        if (push.LengthSquared() > 1e-4f) Body.LinearVelocity = Vector3.Normalize(push) * (heavy ? 7f : 3f);
        if (Health <= 0)
        {
            State = FoeState.Dead;
            Dead = true;
            k.Shatter(this, push);
        }
        else if (State != FoeState.Stagger && (heavy || State != FoeState.Windup || StateTime < Windup * 0.5f))
            Enter(FoeState.Flinch);
    }
}

enum KingState { Asleep, Stalk, Sweep, Slam, Leap, Blind, Stagger, Enrage, Dead }

/// <summary>
/// BALDOMERO III, el rey que se desploma. Enorme, lento y teatral. Arrastra una espada imposible
/// y su corona se le escurre sobre los ojos. En la segunda fase gotea pintura carmesí y salta.
/// </summary>
sealed class FallenKing : Actor
{
    public const ulong Id = 2UL << 40;

    public KingState State = KingState.Asleep;
    public bool Phase2;
    public bool Struck, RingHit;
    public float Cooldown = 1.5f;
    public int AttacksSinceBlind;
    public float Crown;          // 0 = en su sitio, 1 = tapando los ojos
    public float Poise = 140;
    public Vector3 ImpactPoint;
    public float RingStart = -1;
    public float WalkPhase;
    public Vector3 WanderDir;
    public float LastSpark;
    readonly Random _rng = new(1929);

    public override bool Parryable => State == KingState.Sweep;

    public const float SweepWindup = 1.05f, SweepActive = 0.3f, SweepRecover = 0.9f;
    public const float SlamWindup = 1.2f, SlamRecover = 1.25f;
    public const float LeapCrouch = 0.6f, LeapFlight = 1.0f;

    public FallenKing(PhysicsWorld world, Vector3 feet)
    {
        MaxHealth = Health = 1100;
        Yaw = 0; // sentado en el trono mirando a la entrada (+Z)
        CreateBody(world, feet, 1.25f, 5.6f, 180f, sensorEvents: false, Id);
    }

    void Enter(KingState s) { State = s; StateTime = 0; Struck = false; }

    public void Wake() { if (State == KingState.Asleep) Enter(KingState.Stalk); }

    /// <summary>Fuerza un estado (pruebas y capturas).</summary>
    public void Force(KingState s) => Enter(s);

    public float Speed => Phase2 ? 3.1f : 2.2f;

    public void Think(Kingdom k, float dt)
    {
        StateTime += dt;
        HurtFlash = MathF.Max(0, HurtFlash - dt);
        Poise = MathF.Min(140, Poise + 8 * dt);
        if (State is KingState.Asleep or KingState.Dead) { Steer(Vector3.Zero, 0.2f); return; }

        Knight p = k.Player;
        Vector3 to = p.Body.Position - Body.Position;
        to.Y = 0;
        float d = to.Length();
        Vector3 dir = d > 1e-3f ? to / d : Forward;
        float lastPhase = WalkPhase;
        WalkPhase += new Vector2(Body.LinearVelocity.X, Body.LinearVelocity.Z).Length() * dt * 1.1f;
        if ((int)WalkPhase != (int)lastPhase) k.Play(Sfx.KingStep, Feet);
        Crown = State == KingState.Blind ? MathF.Min(1, Crown + dt * 4) : MathF.Max(0, Crown - dt * 1.5f);

        switch (State)
        {
            case KingState.Stalk:
                TurnTowards(YawOf(dir), dt * (Phase2 ? 2.2f : 1.5f));
                Steer(d > 4.5f ? Forward * Speed : Vector3.Zero, 0.15f);
                Cooldown -= dt;
                if (Cooldown <= 0 && !p.Dead)
                {
                    if (AttacksSinceBlind >= 3 && _rng.NextDouble() < 0.45) { Enter(KingState.Blind); WanderDir = Forward; AttacksSinceBlind = 0; }
                    else if (Phase2 && d > 7f && _rng.NextDouble() < 0.7) Enter(KingState.Leap);
                    else if (d < 7.5f) Enter(_rng.NextDouble() < 0.55 ? KingState.Sweep : KingState.Slam);
                    // Cada ataque se anuncia con su sonido: se oye venir antes de verlo.
                    if (State == KingState.Blind) k.Play(Sfx.Boing, Body.Position + new Vector3(0, HalfHeight, 0));
                    if (State == KingState.Slam) k.Play(Sfx.Rise, Body.Position, 1.6f);
                }
                break;

            case KingState.Sweep:
                // Arrastra la espada por detrás (chispas) y barre un arco enorme por delante.
                Steer(StateTime < SweepWindup ? Forward * 0.8f : Vector3.Zero, 0.2f);
                if (StateTime < SweepWindup * 0.8f) TurnTowards(YawOf(dir), dt * 1.8f);
                if (StateTime < SweepWindup && StateTime - LastSpark > 0.12f)
                {
                    LastSpark = StateTime;
                    k.Play(Sfx.Scrape, Feet + Vector3.Transform(new Vector3(2.2f, 0.1f, -2.5f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, Yaw)));
                    k.AddFx(FxKind.Spark, Feet + Vector3.Transform(new Vector3(2.2f, 0.1f, -2.5f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, Yaw)), Vector3.UnitY, 0.5f);
                }
                if (!Struck && StateTime >= SweepWindup + 0.1f)
                {
                    Struck = true;
                    k.EnemyArc(this, 7.0f, 105f, Phase2 ? 44 : 36, parryable: true);
                }
                if (StateTime > SweepWindup + SweepActive + SweepRecover) EndAttack();
                break;

            case KingState.Slam:
                Steer(Vector3.Zero, 0.3f);
                if (StateTime < SlamWindup * 0.75f) TurnTowards(YawOf(dir), dt * 1.6f);
                if (!Struck && StateTime >= SlamWindup)
                {
                    Struck = true;
                    ImpactPoint = Feet + Forward * 4.6f;
                    k.Slam(this, ImpactPoint, 2.7f, Phase2 ? 55 : 48);
                    RingStart = StateTime;
                    RingHit = false;
                }
                if (RingStart >= 0 && !RingHit)
                {
                    // Onda expansiva: un anillo que hay que esquivar rodando a través.
                    float r = (StateTime - RingStart) * 13f;
                    if (r > 9f) RingStart = -1;
                    else if (k.RingTouches(ImpactPoint, r)) RingHit = true;
                }
                if (StateTime > SlamWindup + SlamRecover) { RingStart = -1; EndAttack(); }
                break;

            case KingState.Leap:
                if (StateTime < LeapCrouch) { Steer(Vector3.Zero, 0.4f); TurnTowards(YawOf(dir), dt * 4); }
                else if (!Struck)
                {
                    Struck = true;
                    // Tiro balístico hasta donde está el caballero.
                    Vector3 delta = p.Feet - Feet;
                    const float T = LeapFlight;
                    k.Play(Sfx.HeavySwing, Body.Position, 2.6f);
                    Body.LinearVelocity = new Vector3(delta.X / T, (delta.Y + 0.5f * 9.81f * T * T) / T, delta.Z / T);
                    RingStart = -1;
                }
                else if (RingStart < 0 && StateTime > LeapCrouch + 0.35f && k.IsGrounded(this))
                {
                    ImpactPoint = Feet;
                    k.Slam(this, ImpactPoint, 3.3f, 46);
                    RingStart = StateTime;
                    Body.LinearVelocity = Vector3.Zero;
                }
                if (RingStart >= 0 && StateTime - RingStart > 1.0f) { RingStart = -1; EndAttack(); }
                if (StateTime > 4f) { RingStart = -1; EndAttack(); }
                break;

            case KingState.Blind:
                // Con la corona sobre los ojos camina a ciegas y se estrella con lo que haya.
                if ((int)(StateTime * 1.4f) != (int)((StateTime - dt) * 1.4f))
                    WanderDir = Vector3.Transform(WanderDir, Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(_rng.NextDouble() - 0.5) * 2.4f));
                TurnTowards(YawOf(WanderDir), dt * 2.5f);
                Steer(Forward * 2.4f, 0.1f);
                if (k.BlindBump(this)) WanderDir = -WanderDir; // ¡bonk! y se da la vuelta
                if (StateTime > 3.2f) { Enter(KingState.Stalk); Cooldown = 0.8f; }
                break;

            case KingState.Stagger:
                Steer(Vector3.Zero, 0.2f);
                if (StateTime > 2.1f) { Enter(KingState.Stalk); Cooldown = 0.6f; }
                break;

            case KingState.Enrage:
                Steer(Vector3.Zero, 0.3f);
                if (StateTime > 1.8f) { Enter(KingState.Stalk); Cooldown = 0.3f; }
                break;
        }
    }

    void EndAttack()
    {
        AttacksSinceBlind++;
        Cooldown = Phase2 ? 0.5f + (float)_rng.NextDouble() * 0.6f : 1.0f + (float)_rng.NextDouble() * 1.0f;
        Enter(KingState.Stalk);
    }

    public void OnParried() { Enter(KingState.Stagger); Poise = 140; }

    public void TakeHit(Kingdom k, float damage, bool heavy)
    {
        if (State is KingState.Dead or KingState.Asleep) return;
        float mult = State == KingState.Blind ? 1.25f : 1f;
        Health -= damage * mult;
        HurtFlash = 0.18f;
        Poise -= heavy ? 38 : 11;
        if (Health <= 0)
        {
            Health = 0;
            Dead = true;
            Enter(KingState.Dead);
            k.AddFx(FxKind.Blood, Body.Position, Vector3.UnitY, 3f);
            k.Play(Sfx.KingFall, Body.Position);
            return;
        }
        if (!Phase2 && Health < MaxHealth * 0.5f)
        {
            Phase2 = true;
            Enter(KingState.Enrage);
            k.Play(Sfx.Roar, Body.Position + new Vector3(0, HalfHeight, 0));
            k.AddFx(FxKind.Blood, Body.Position + new Vector3(0, 1.5f, 0), Vector3.UnitY, 2.5f);
            k.Shake = MathF.Max(k.Shake, 0.8f);
            return;
        }
        if (Poise <= 0 && State != KingState.Stagger)
        {
            Enter(KingState.Stagger);
            Poise = 140;
        }
    }
}
