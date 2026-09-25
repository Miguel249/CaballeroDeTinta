using System.Numerics;
using Box3D;

namespace CaballeroDeTinta.Sim;

enum KnightState { Free, Light, Heavy, Dodge, Parry, Heal, Hurt, Dead, Rest }

/// <summary>
/// El pequeño caballero. Tiempos de combate deliberados, como en un soulslike:
/// cada acción tiene anticipación, instante activo y recuperación, y todo cuesta aguante.
/// </summary>
sealed class Knight : Actor
{
    public const ulong Id = 1UL << 40;

    public KnightState State;
    public float Stamina = 100, MaxStamina = 100;
    public int Flasks = 3, MaxFlasks = 3;
    public int Combo;
    public bool Struck;           // el golpe del ataque actual ya se lanzó
    public bool QueuedLight;
    public float ParryWindow;     // > 0: un golpe ahora se desvía
    public float Riposte;         // > 0: el siguiente golpe es crítico
    public Vector3 DodgeDir;
    public float Speed;           // para la animación de caminar
    public float WalkPhase;
    public Vector3 CapeLag;       // la capa responde al movimiento con retraso
    public float StaminaDelay;

    public override bool Parryable => false;

    // Tiempos (segundos).
    public const float LightWindup = 0.14f, LightActive = 0.1f, LightRecover = 0.3f;
    public const float HeavyWindup = 0.5f, HeavyActive = 0.12f, HeavyRecover = 0.45f;
    public const float DodgeTime = 0.5f, DodgeIFrom = 0.06f, DodgeITo = 0.38f;
    public const float ParryTime = 0.5f, ParryOpen = 0.2f;
    public const float HealTime = 1.1f;

    public Knight(PhysicsWorld world, Vector3 feet)
    {
        MaxHealth = Health = 100;
        CreateBody(world, feet, 0.34f, 1.6f, 90f, sensorEvents: true, Id);
        Yaw = MathF.PI; // mirando hacia -Z, hacia el castillo
    }

    void Enter(KnightState s)
    {
        State = s;
        StateTime = 0;
        Struck = false;
    }

    bool Spend(float cost)
    {
        if (Stamina <= 0) return false;
        Stamina -= cost;
        StaminaDelay = 0.7f;
        return true;
    }

    public void Think(Kingdom k, Controls c, float dt)
    {
        StateTime += dt;
        Invulnerable = MathF.Max(0, Invulnerable - dt);
        HurtFlash = MathF.Max(0, HurtFlash - dt);
        Riposte = MathF.Max(0, Riposte - dt);
        StaminaDelay -= dt;
        if (StaminaDelay <= 0 && State is not (KnightState.Dodge or KnightState.Light or KnightState.Heavy))
            Stamina = MathF.Min(MaxStamina, Stamina + 42f * dt);

        // Dirección deseada relativa a la cámara.
        Vector3 camF = new(MathF.Sin(k.CamYaw), 0, MathF.Cos(k.CamYaw));
        Vector3 camR = new(-camF.Z, 0, camF.X);
        Vector3 wish = camF * c.Move.Y + camR * c.Move.X;
        if (wish.LengthSquared() > 1) wish = Vector3.Normalize(wish);

        Actor? target = k.LockTarget;
        Vector3 v = Body.LinearVelocity;
        Speed = new Vector2(v.X, v.Z).Length();
        float lastPhase = WalkPhase;
        WalkPhase += Speed * dt * 2.2f;
        if (State == KnightState.Free && Speed > 0.8f && (int)WalkPhase != (int)lastPhase) k.Play(Sfx.Step, Feet);
        CapeLag = Vector3.Lerp(CapeLag, new Vector3(v.X, 0, v.Z), 1 - MathF.Exp(-dt * 5));

        switch (State)
        {
            case KnightState.Free:
                float speed = target != null ? 3.4f : 4.4f;
                Steer(wish * speed, 0.25f);
                if (target != null) TurnTowards(YawOf(target.Body.Position - Body.Position), dt * 12);
                else if (wish.LengthSquared() > 0.01f) TurnTowards(YawOf(wish), dt * 12);

                if (c.Dodge && Spend(22)) { DodgeDir = wish.LengthSquared() > 0.01f ? Vector3.Normalize(wish) : -Forward; Enter(KnightState.Dodge); k.Play(Sfx.Dodge, Body.Position); }
                else if (c.Light && Spend(14)) { Combo = 0; Enter(KnightState.Light); }
                else if (c.Heavy && Spend(30)) Enter(KnightState.Heavy);
                else if (c.Parry && Spend(10)) Enter(KnightState.Parry);
                else if (c.Heal && Flasks > 0) { Flasks--; Enter(KnightState.Heal); }
                break;

            case KnightState.Light:
                Steer(StateTime < LightWindup ? Forward * 1.8f : Vector3.Zero, 0.3f);
                if (target != null && StateTime < LightWindup) TurnTowards(YawOf(target.Body.Position - Body.Position), dt * 14);
                if (!Struck && StateTime >= LightWindup)
                {
                    Struck = true;
                    float dmg = 34 * (Riposte > 0 ? 3f : 1f);
                    k.Play(Sfx.Swing, Body.Position + Forward, Combo == 1 ? 0.85f : 1f);
                    k.Strike(this, Body.Position + Forward * 1.1f, 1.25f, 2.2f, dmg, heavy: false);
                    Riposte = 0;
                }
                if (c.Light) QueuedLight = true;
                if (StateTime > LightWindup + LightActive && QueuedLight && Combo < 2 && Stamina > 0)
                {
                    QueuedLight = false;
                    if (Spend(14)) { Combo++; Enter(KnightState.Light); }
                }
                else if (StateTime > LightWindup + LightActive + LightRecover) { QueuedLight = false; Enter(KnightState.Free); }
                break;

            case KnightState.Heavy:
                // Anticipación larga y luego un golpe brutal que avanza.
                bool lunging = StateTime >= HeavyWindup && StateTime < HeavyWindup + HeavyActive + 0.08f;
                Steer(lunging ? Forward * 7f : Vector3.Zero, 0.4f);
                if (target != null && StateTime < HeavyWindup) TurnTowards(YawOf(target.Body.Position - Body.Position), dt * 8);
                if (!Struck && StateTime >= HeavyWindup + 0.04f)
                {
                    Struck = true;
                    float dmg = 88 * (Riposte > 0 ? 2.5f : 1f);
                    k.Play(Sfx.HeavySwing, Body.Position + Forward);
                    k.Strike(this, Body.Position + Forward * 1.3f, 1.5f, 2.6f, dmg, heavy: true);
                    Riposte = 0;
                }
                if (StateTime > HeavyWindup + HeavyActive + HeavyRecover) Enter(KnightState.Free);
                break;

            case KnightState.Dodge:
                float t = StateTime / DodgeTime;
                Steer(DodgeDir * (t < 0.7f ? 9.5f : 3f), 0.6f);
                Invulnerable = StateTime > DodgeIFrom && StateTime < DodgeITo ? 0.02f : Invulnerable;
                if (target == null) TurnTowards(YawOf(DodgeDir), dt * 20);
                if (StateTime > DodgeTime) Enter(KnightState.Free);
                break;

            case KnightState.Parry:
                Steer(Vector3.Zero, 0.5f);
                ParryWindow = StateTime < ParryOpen ? 1 : 0;
                if (StateTime > ParryTime) { ParryWindow = 0; Enter(KnightState.Free); }
                break;

            case KnightState.Heal:
                Steer(wish * 1.2f, 0.2f);
                if (!Struck && StateTime > HealTime * 0.6f)
                {
                    Struck = true;
                    Health = MathF.Min(MaxHealth, Health + 45);
                    k.Play(Sfx.Heal, Body.Position);
                    k.AddFx(FxKind.Rest, Body.Position + new Vector3(0, 0.6f, 0), Vector3.UnitY, 0.8f);
                }
                if (StateTime > HealTime) Enter(KnightState.Free);
                break;

            case KnightState.Hurt:
                Steer(Vector3.Zero, 0.1f);
                if (StateTime > 0.45f) Enter(KnightState.Free);
                break;

            case KnightState.Rest:
                Steer(Vector3.Zero, 0.5f);
                if (StateTime > 1.2f && (c.Move.LengthSquared() > 0.1f || c.Any)) Enter(KnightState.Free);
                break;

            case KnightState.Dead:
                Steer(Vector3.Zero, 0.2f);
                break;
        }
    }

    /// <summary>Resultado de recibir un golpe: <c>true</c> si se desvió con parry.</summary>
    public bool TakeHit(Kingdom k, Actor attacker, float damage, Vector3 from, bool parryable)
    {
        if (Dead || Invulnerable > 0) return false;
        Vector3 toAttacker = attacker.Body.Position - Body.Position;
        bool facing = Vector3.Dot(Vector3.Normalize(new Vector3(toAttacker.X, 0, toAttacker.Z)), Forward) > 0.2f;
        if (parryable && State == KnightState.Parry && ParryWindow > 0 && facing)
        {
            Riposte = 1.6f;
            Enter(KnightState.Free);
            return true;
        }

        Health -= damage;
        HurtFlash = 0.25f;
        Invulnerable = 0.5f;
        Vector3 push = Body.Position - from;
        push.Y = 0;
        if (push.LengthSquared() > 1e-4f) push = Vector3.Normalize(push);
        Body.LinearVelocity = push * 6f + new Vector3(0, 2.5f, 0);
        if (Health <= 0)
        {
            Health = 0;
            Dead = true;
            Enter(KnightState.Dead);
            k.Play(Sfx.Death, Body.Position);
        }
        else Enter(KnightState.Hurt);
        return false;
    }

    public void RestAtBonfire()
    {
        Health = MaxHealth;
        Flasks = MaxFlasks;
        Stamina = MaxStamina;
        Enter(KnightState.Rest);
    }

    /// <summary>Progreso normalizado del estado actual, útil para animar.</summary>
    public float Progress => State switch
    {
        KnightState.Light => StateTime / (LightWindup + LightActive + LightRecover),
        KnightState.Heavy => StateTime / (HeavyWindup + HeavyActive + HeavyRecover),
        KnightState.Dodge => StateTime / DodgeTime,
        KnightState.Parry => StateTime / ParryTime,
        KnightState.Heal => StateTime / HealTime,
        _ => 0,
    };
}
