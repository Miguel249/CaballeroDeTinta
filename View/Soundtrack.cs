using System.Numerics;
using CaballeroDeTinta.Art;
using CaballeroDeTinta.Sim;
using Raylib_cs;

namespace CaballeroDeTinta.View;

/// <summary>
/// La banda sonora: reproduce los sonidos que pide la simulación (con volumen por distancia y
/// paneo según la cámara) y mezcla la música en un flujo propio.
///
/// La dramaturgia del jefe: el vals se apaga según te acercas a la niebla, al cruzarla se corta en
/// seco y solo queda el ruido del proyector sobre la pantalla negra; tras ese silencio cae el cartel
/// con un golpe de orquesta, y el galope empieza cuando el rey despierta.
/// </summary>
sealed unsafe class Soundtrack : IDisposable
{
    const int Chunk = 4096;
    const int Copies = 4;   // copias de cada sonido para que se puedan solapar

    /// <summary>Bucle de música o ambiente. Se sintetiza en segundo plano y suena en cuanto está listo.</summary>
    sealed class Loop(Func<float[]> make, float level)
    {
        readonly Task<float[]> _pending = Task.Run(make);
        public float[]? Samples => _pending.IsCompletedSuccessfully ? _pending.Result : null;
        public readonly float Level = level;
        public int Pos;
        public float Gain, Target;
        public float Speed = 1.5f; // unidades de ganancia por segundo
    }

    readonly Dictionary<Sfx, Sound[][]> _sfx = [];
    readonly Dictionary<Sfx, int> _turn = [];
    readonly Sound _stinger, _fanfare;
    readonly Loop _waltz, _gallop, _gallop2, _projector, _fire;
    readonly Loop[] _loops;
    readonly AudioStream _stream;
    readonly short[] _pcm = new short[Chunk];
    readonly Random _rng = new(5);
    Phase _lastPhase = Phase.Explore;
    bool _stung, _phase2, _kingDown;

    public Soundtrack()
    {
        // La música tarda en sintetizarse: arranca ya en otros hilos.
        _waltz = new Loop(Synth.Waltz, 0.55f);
        _gallop = new Loop(() => Synth.Gallop(false), 0.6f);
        _gallop2 = new Loop(() => Synth.Gallop(true), 0.6f);
        _projector = new Loop(Synth.Projector, 0.14f) { Target = 1, Gain = 1 };
        _fire = new Loop(Synth.Fire, 0.45f);

        // Los efectos se calculan en paralelo, pero raylib los carga en este hilo.
        var jobs = Enum.GetValues<Sfx>().SelectMany(s => Enumerable.Range(0, Synth.Variants(s)).Select(v => (s, v))).ToArray();
        var made = new float[jobs.Length][];
        Parallel.For(0, jobs.Length, i => made[i] = Synth.Make(jobs[i].s, jobs[i].v));
        foreach (var group in jobs.Select((j, i) => (j.s, Samples: made[i])).GroupBy(j => j.s))
        {
            _sfx[group.Key] = group.Select(j =>
            {
                Sound main = Load(j.Samples);
                return Enumerable.Range(0, Copies).Select(i => i == 0 ? main : Raylib.LoadSoundAlias(main)).ToArray();
            }).ToArray();
            _turn[group.Key] = 0;
        }
        _stinger = Load(Synth.Stinger());
        _fanfare = Load(Synth.Fanfare());
        _loops = [_waltz, _gallop, _gallop2, _projector, _fire];

        Raylib.SetAudioStreamBufferSizeDefault(Chunk);
        _stream = Raylib.LoadAudioStream(Synth.Rate, 16, 1);
        Pump();
        Raylib.PlayAudioStream(_stream);
    }

    static Sound Load(float[] samples)
    {
        var pcm = new short[samples.Length];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)(Math.Clamp(samples[i], -1, 1) * 32767);
        fixed (short* p = pcm)
        {
            var wave = new Wave { FrameCount = (uint)pcm.Length, SampleRate = Synth.Rate, SampleSize = 16, Channels = 1, Data = p };
            return Raylib.LoadSoundFromWave(wave); // copia los datos
        }
    }

    public void Update(Kingdom k, Camera3D cam)
    {
        Vector3 ear = k.Player.Body.Position;
        Vector3 look = cam.Target - cam.Position;
        Vector3 right = Vector3.Normalize(Vector3.Cross(look, Vector3.UnitY));
        foreach (Heard h in k.Sounds) Play(h, ear, cam.Position, right);
        Score(k);
        Pump();
    }

    // ================================================================= efectos

    static float Loudness(Sfx s) => s switch
    {
        Sfx.Step => 0.35f,
        Sfx.BoneStep => 0.5f,
        Sfx.Scrape => 0.5f,
        _ => 0.85f,
    };

    /// <summary>Distancia a la que un sonido baja a la mitad: los del rey se oyen en toda la sala.</summary>
    static float Reach(Sfx s) => s switch
    {
        Sfx.Slam or Sfx.Bell or Sfx.Roar or Sfx.KingStep or Sfx.KingFall or Sfx.Rise or Sfx.Bump or Sfx.Boing => 45f,
        Sfx.Death or Sfx.Heal or Sfx.Hurt => 1000f,
        _ => 16f,
    };

    void Play(Heard h, Vector3 ear, Vector3 camPos, Vector3 right)
    {
        Sound[][] variants = _sfx[h.Sfx];
        Sound[] copies = variants[_rng.Next(variants.Length)];
        Sound s = copies[_turn[h.Sfx]++ % Copies];

        float d = Vector3.Distance(h.Position, ear) / Reach(h.Sfx);
        float volume = Loudness(h.Sfx) * MathF.Min(1.3f, MathF.Sqrt(h.Size)) / (1 + d * d);
        Vector3 toSound = h.Position - camPos;
        float side = toSound.LengthSquared() > 1e-4f ? Vector3.Dot(Vector3.Normalize(toSound), right) : 0;
        bool musical = h.Sfx is Sfx.Death or Sfx.KingFall;
        float pitch = 1 / MathF.Sqrt(h.Size) * (musical ? 1 : 0.94f + 0.12f * (float)_rng.NextDouble());

        Raylib.SetSoundVolume(s, Math.Clamp(volume, 0, 1));
        Raylib.SetSoundPitch(s, pitch);
        Raylib.SetSoundPan(s, 0.5f - 0.35f * side); // en raylib 1 es la izquierda
        Raylib.PlaySound(s);
    }

    // ================================================================= música

    static float Smooth(float x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    static void Cut(Loop l) => l.Gain = 0;

    void Score(Kingdom k)
    {
        if (k.Phase != _lastPhase)
        {
            switch (k.Phase)
            {
                case Phase.Intro:
                    // Silencio: el vals se corta en seco y queda solo el proyector.
                    Cut(_waltz); Cut(_fire);
                    _stung = false;
                    break;
                case Phase.Boss:
                    _gallop.Pos = 0;
                    _gallop.Speed = 3f;
                    break;
                case Phase.Dead:
                    foreach (Loop l in new[] { _waltz, _gallop, _gallop2 }) Cut(l);
                    break;
                case Phase.Victory:
                    Raylib.PlaySound(_fanfare);
                    break;
                case Phase.Explore when _lastPhase == Phase.Dead:
                    _waltz.Pos = 0;
                    _waltz.Speed = 0.35f; // vuelve despacio, desde el principio
                    break;
            }
            _lastPhase = k.Phase;
        }

        if (k.Phase == Phase.Intro && !_stung && k.PhaseTime >= Cards.CardDelay)
        {
            Raylib.PlaySound(_stinger);
            _stung = true;
        }

        FallenKing king = k.King;
        if (king.Phase2 && !_phase2)
        {
            // El rugido interrumpe la música; la segunda parte empieza desde el principio.
            _phase2 = true;
            Cut(_gallop);
            _gallop2.Pos = 0;
            _gallop2.Speed = 3f;
        }
        if (!king.Phase2) _phase2 = false;
        if (king.Dead && !_kingDown && k.Phase is Phase.Boss or Phase.Victory) { _kingDown = true; Cut(_gallop); Cut(_gallop2); }
        if (!king.Dead) _kingDown = false;

        // Al acercarse a la niebla el vals se va apagando: los últimos metros ya son silencio.
        float gate = Vector2.Distance(new Vector2(k.Player.Feet.X, k.Player.Feet.Z), new Vector2(k.GateCenter.X, k.GateCenter.Z));
        if (_waltz.Gain >= _waltz.Target - 0.01f) _waltz.Speed = 1.5f;
        _waltz.Target = k.Phase != Phase.Explore ? 0 : k.BossDefeated ? 0.6f : Smooth((gate - 4) / 10);

        bool fighting = k.Phase == Phase.Boss && !king.Dead && king.State != KingState.Enrage;
        _gallop.Target = fighting && !king.Phase2 ? 1 : 0;
        _gallop2.Target = fighting && king.Phase2 ? 1 : 0;

        float fire = Vector3.Distance(k.Player.Feet, k.Bonfire) / 6;
        _fire.Target = k.Phase is Phase.Intro ? 0 : (k.BonfireLit ? 1f : 0.55f) / (1 + fire * fire);
    }

    void Pump()
    {
        while (Raylib.IsAudioStreamProcessed(_stream))
        {
            Mix();
            fixed (short* p = _pcm) Raylib.UpdateAudioStream(_stream, p, Chunk);
        }
    }

    void Mix()
    {
        Span<float> acc = stackalloc float[Chunk];
        acc.Clear();
        foreach (Loop l in _loops)
        {
            float step = l.Speed * Chunk / Synth.Rate;
            float end = l.Gain < l.Target ? MathF.Min(l.Target, l.Gain + step) : MathF.Max(l.Target, l.Gain - step);
            if (l.Gain < 1e-4f && end < 1e-4f) { l.Gain = end; continue; }
            if (l.Samples is not { } s) continue;
            for (int i = 0; i < Chunk; i++)
            {
                float g = l.Gain + (end - l.Gain) * i / Chunk;
                acc[i] += s[l.Pos] * g * l.Level;
                if (++l.Pos >= s.Length) l.Pos = 0;
            }
            l.Gain = end;
        }
        for (int i = 0; i < Chunk; i++) _pcm[i] = (short)(Math.Clamp(MathF.Tanh(acc[i]), -1, 1) * 32767);
    }

    public void Dispose()
    {
        Raylib.UnloadAudioStream(_stream);
        foreach (Sound[][] variants in _sfx.Values)
            foreach (Sound[] copies in variants)
            {
                for (int i = 1; i < copies.Length; i++) Raylib.UnloadSoundAlias(copies[i]);
                Raylib.UnloadSound(copies[0]);
            }
        Raylib.UnloadSound(_stinger);
        Raylib.UnloadSound(_fanfare);
    }
}
