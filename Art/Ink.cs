using System.Numerics;
using Raylib_cs;

namespace CaballeroDeTinta.Art;

enum Shape3 { Cube, Sphere, Cylinder, Cone }

/// <summary>
/// Pintura y tinta en tiempo real:
///   - relleno con tres tonos planos, lavado de pigmento, sombreado a plumilla y niebla de acuarela;
///   - contorno de tinta por "casco invertido" cuyo grosor hierve a 12 fotogramas por segundo;
///   - temblor de trazo: en cada dibujo las formas se vuelven a trazar un poco desplazadas, como los
///     fotogramas de una animación a mano, que nunca calcan exactamente el anterior;
///   - posproducción de película: papel, grano, polvo, rayas, vaivén, parpadeo de exposición y viñeta.
/// Casi todo son primitivas; los modelos glTF (<see cref="Puppet"/>) pasan por la misma tinta.
/// </summary>
sealed unsafe class Ink : IDisposable
{
    /// <summary>
    /// Temblor de trazo, común al relleno y al contorno para que no se separen: un desplazamiento suave
    /// en el espacio (los vértices vecinos se mueven juntos) que cambia en cada dibujo. Crece con la
    /// distancia para que en pantalla tiemble lo mismo lo cercano y lo lejano.
    /// </summary>
    const string Boil = """
        uniform float seed;
        uniform vec3 viewPos;
        float bh(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
        float bn(vec3 p)
        {
            vec3 i = floor(p), f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(mix(bh(i), bh(i + vec3(1,0,0)), f.x), mix(bh(i + vec3(0,1,0)), bh(i + vec3(1,1,0)), f.x), f.y),
                       mix(mix(bh(i + vec3(0,0,1)), bh(i + vec3(1,0,1)), f.x), mix(bh(i + vec3(0,1,1)), bh(i + vec3(1,1,1)), f.x), f.y), f.z);
        }
        vec3 boil(vec3 w)
        {
            float amp = 0.008 + 0.0018 * length(viewPos - w);
            vec3 q = w * 1.4 + vec3(seed * 3.7, seed * 1.9, seed * 2.3);
            return (vec3(bn(q), bn(q + 19.1), bn(q + 47.7)) - 0.5) * 2.0 * amp;
        }
        """;

    const string FillVs = """
        #version 330
        in vec3 vertexPosition;
        in vec3 vertexNormal;
        in vec2 vertexTexCoord;
        uniform mat4 matModel;
        uniform mat4 matNormal;
        uniform mat4 matView;
        uniform mat4 matProjection;
        out vec3 fragPos;
        out vec3 fragNormal;
        out vec2 fragTexCoord;
        """ + Boil + """
        void main()
        {
            vec3 world = vec3(matModel * vec4(vertexPosition, 1.0));
            fragPos = world;
            fragNormal = normalize(vec3(matNormal * vec4(vertexNormal, 0.0)));
            fragTexCoord = vertexTexCoord;
            gl_Position = matProjection * matView * vec4(world + boil(world), 1.0);
        }
        """;

    const string FillFs = """
        #version 330
        in vec3 fragPos;
        in vec3 fragNormal;
        in vec2 fragTexCoord;
        uniform sampler2D texture0;   // blanco en las primitivas; la paleta pintada en los modelos
        uniform vec4 colDiffuse;
        uniform vec3 viewPos;
        uniform vec3 fogColor;
        uniform float emissive;
        uniform float seed;
        uniform vec4 lights[6];     // xyz posición, w intensidad (luces cálidas)
        out vec4 finalColor;

        float hash(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
        float noise(vec3 p)
        {
            vec3 i = floor(p), f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(mix(hash(i), hash(i + vec3(1,0,0)), f.x), mix(hash(i + vec3(0,1,0)), hash(i + vec3(1,1,0)), f.x), f.y),
                       mix(mix(hash(i + vec3(0,0,1)), hash(i + vec3(1,0,1)), f.x), mix(hash(i + vec3(0,1,1)), hash(i + vec3(1,1,1)), f.x), f.y), f.z);
        }

        void main()
        {
            vec3 base = colDiffuse.rgb * texture(texture0, fragTexCoord).rgb;
            vec3 n = normalize(fragNormal);
            vec3 L = normalize(vec3(-0.35, 0.8, 0.45));
            float d = max(dot(n, L), 0.0);
            // Tres tonos planos, como un fondo pintado a mano: la sombra es profunda.
            float band = d > 0.55 ? 1.0 : (d > 0.18 ? 0.64 : 0.4);
            // Lavado y pinceladas horizontales: el pigmento nunca es uniforme.
            float streak = noise(vec3(fragPos.x * 0.7, fragPos.y * 7.0, fragPos.z * 0.7));
            float wash = 0.8 + 0.2 * noise(fragPos * 0.33) + 0.1 * (streak - 0.5) + 0.05 * noise(fragPos * 2.3);
            float mx = max(base.r, max(base.g, base.b)), mn = min(base.r, min(base.g, base.b));
            float grey = 1.0 - smoothstep(0.08, 0.2, (mx - mn) / (mx + 1e-3));
            // Musgo y humedad en la parte baja de la piedra.
            float moss = smoothstep(0.55, 0.8, noise(fragPos * 0.45)) * (1.0 - smoothstep(0.3, 3.5, fragPos.y)) * (1.0 - abs(n.y)) * grey * (1.0 - emissive);
            base = mix(base, vec3(0.3, 0.36, 0.25), moss * 0.7);
            if (n.y > 0.7) base *= 1.06;
            vec3 col = base * band * wash;

            // Plumilla en la sombra; el trazo se redibuja en cada fotograma de animación.
            vec2 sc = gl_FragCoord.xy + vec2(seed * 13.0, seed * 7.0);
            float hatchA = step(0.62, fract((sc.x + sc.y) / 5.0));
            float hatchB = step(0.62, fract((sc.x - sc.y) / 5.0));
            if (band < 0.6) col *= 1.0 - 0.28 * hatchA;
            if (band < 0.6 && d < 0.05) col *= 1.0 - 0.22 * hatchB;

            // Charcos de luz cálida de antorchas y hogueras.
            vec3 warm = vec3(0.0);
            for (int i = 0; i < 6; i++)
            {
                vec3 toL = lights[i].xyz - fragPos;
                float dist2 = dot(toL, toL);
                float facing = max(dot(n, normalize(toL)), 0.0) * 0.7 + 0.3;
                warm += vec3(1.0, 0.55, 0.18) * lights[i].w * facing / (1.0 + dist2 * 0.09);
            }
            col += base * warm;

            col = mix(col, base, emissive);

            float dist = length(viewPos - fragPos);
            float fog = clamp((dist - 22.0) / 110.0, 0.0, 0.88) * (1.0 - emissive * 0.7);
            col = mix(col, fogColor, fog);
            finalColor = vec4(col, colDiffuse.a);
        }
        """;

    const string OutlineVs = """
        #version 330
        in vec3 vertexPosition;
        in vec3 vertexNormal;
        uniform mat4 matModel;
        uniform mat4 matNormal;
        uniform mat4 matView;
        uniform mat4 matProjection;
        uniform float width;
        float hash(vec3 p) { return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719))) * 43758.5453); }
        """ + Boil + """
        void main()
        {
            vec3 world = vec3(matModel * vec4(vertexPosition, 1.0));
            vec3 n = normalize(vec3(matNormal * vec4(vertexNormal, 0.0)));
            // Grosor casi constante en pantalla, y "hirviendo": cambia en cada fotograma dibujado.
            float boil = 0.55 + 0.9 * hash(floor(world * 4.0) + seed);
            float w = width * (0.5 + 0.03 * length(viewPos - world)) * boil;
            gl_Position = matProjection * matView * vec4(world + boil(world) + n * w, 1.0);
        }
        """;

    const string OutlineFs = """
        #version 330
        uniform vec4 colDiffuse;
        out vec4 finalColor;
        void main() { finalColor = vec4(colDiffuse.rgb, 1.0); }
        """;

    const string PostFs = """
        #version 330
        in vec2 fragTexCoord;
        uniform sampler2D texture0;
        uniform vec2 resolution;
        uniform float seed;
        uniform vec2 weave;
        uniform float exposure;
        uniform float flash;
        uniform float rough;
        uniform float desat;      // el mundo pierde color (pausa, muerte, menú)
        uniform float vignette;   // la tinta se acerca desde los bordes
        out vec4 finalColor;

        float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
        float noise(vec2 p)
        {
            vec2 i = floor(p), f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
        }
        float fbm(vec2 p) { float s = 0.0, a = 0.5; for (int i = 0; i < 4; i++) { s += a * noise(p); p *= 2.07; a *= 0.5; } return s; }

        void main()
        {
            vec2 uv = fragTexCoord + weave;
            // Impactos brutales: la imagen se emborrona como si se hubiera redibujado a toda prisa.
            if (rough > 0.0)
                uv += (vec2(noise(uv * 40.0 + seed), noise(uv * 40.0 - seed)) - 0.5) * 0.02 * rough;
            vec3 c = texture(texture0, uv).rgb;

            // Gradación de época: todo tiende al sepia salvo lo que brilla con color propio.
            float lum = dot(c, vec3(0.299, 0.587, 0.114));
            float mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b));
            float sat = (mx - mn) / (mx + 1e-4);
            float keep = smoothstep(0.55, 0.85, sat) * smoothstep(0.25, 0.5, mx);
            vec3 sepia = vec3(lum * 1.08, lum * 0.96, lum * 0.78);
            c = mix(mix(c, sepia, 0.42), c, keep);
            c = (c - 0.45) * 1.16 + 0.45;                   // contraste de copia de proyección
            c = c / (1.0 + max(c - 0.82, 0.0) * 1.4);     // luces suaves

            // Papel: fibras y manchas fijas.
            vec2 px = uv * resolution;
            float paper = 0.92 + 0.08 * fbm(px / 160.0) + 0.03 * (noise(vec2(px.x / 2.0, px.y / 90.0)) - 0.5);
            c *= paper;

            // Grano de película, distinto en cada fotograma.
            c += (hash(px + seed * 91.7) - 0.5) * 0.075;

            // Polvo: motas negras dispersas.
            vec2 grid = vec2(56.0, 32.0);
            vec2 cell = floor(uv * grid);
            float r = hash(cell + seed * 3.1);
            if (r > 0.9965)
            {
                vec2 f = fract(uv * grid) - vec2(hash(cell + 1.7), hash(cell + 9.2));
                float size = 0.04 + 0.1 * hash(cell + seed);
                c *= mix(1.0, 0.2, smoothstep(size, size * 0.4, length(f * vec2(1.0, 0.6))));
            }
            // Rayas verticales ocasionales.
            if (hash(vec2(seed, 7.1)) > 0.7)
            {
                float sx = hash(vec2(seed, 1.3));
                c = mix(c, vec3(0.92, 0.88, 0.78), smoothstep(0.0014, 0.0, abs(uv.x - sx)) * 0.4);
            }

            vec2 q = fragTexCoord - 0.5;
            c *= 1.0 - dot(q, q) * 1.25;

            // Pigmento que se va: primero la saturación, después la luz de los bordes.
            float grey = dot(c, vec3(0.299, 0.587, 0.114));
            c = mix(c, vec3(grey * 1.02, grey * 0.97, grey * 0.88), desat);
            c *= 1.0 - vignette * smoothstep(0.18, 0.75, length(q * vec2(1.0, 0.8)) * 1.35);
            c *= exposure;

            // Fotograma de impacto: negativo de tinta en alto contraste.
            if (flash > 0.0)
            {
                float t = step(0.42, lum);
                vec3 neg = mix(vec3(0.93, 0.88, 0.78), vec3(0.06, 0.05, 0.05), t);
                c = mix(c, neg, flash);
            }
            finalColor = vec4(c, 1.0);
        }
        """;

    readonly Shader _fill, _outline, _post;
    readonly int _fillView, _fillEmissive, _fillSeed, _fillLights, _fillFog;
    readonly int _outView, _outWidth, _outSeed;
    readonly int _postRes, _postSeed, _postWeave, _postExposure, _postFlash, _postRough, _postDesat, _postVignette;
    readonly Mesh[] _meshes = new Mesh[4];
    Material _fillMat, _outlineMat;
    Texture2D _white;
    RenderTexture2D _target;
    readonly float[] _lights = new float[24];

    public float Seed { get; private set; }
    public float Flash;
    public float Rough;
    /// <summary>Gradación de la interfaz sobre el mundo: nunca afecta a lo que se dibuja después de la película.</summary>
    public float Desaturate, Vignette;
    readonly Random _rng = new(3);
    Vector2 _weave;
    float _exposure = 1f;

    public Ink()
    {
        _fill = Raylib.LoadShaderFromMemory(FillVs, FillFs);
        _outline = Raylib.LoadShaderFromMemory(OutlineVs, OutlineFs);
        _post = Raylib.LoadShaderFromMemory(null, PostFs);

        _fillView = Raylib.GetShaderLocation(_fill, "viewPos");
        _fillEmissive = Raylib.GetShaderLocation(_fill, "emissive");
        _fillSeed = Raylib.GetShaderLocation(_fill, "seed");
        _fillLights = Raylib.GetShaderLocation(_fill, "lights");
        _fillFog = Raylib.GetShaderLocation(_fill, "fogColor");
        _outView = Raylib.GetShaderLocation(_outline, "viewPos");
        _outWidth = Raylib.GetShaderLocation(_outline, "width");
        _outSeed = Raylib.GetShaderLocation(_outline, "seed");
        // Raylib solo localiza por defecto algunas matrices: se piden las demás.
        _outline.Locs[(int)ShaderLocationIndex.MatrixView] = Raylib.GetShaderLocation(_outline, "matView");
        _outline.Locs[(int)ShaderLocationIndex.MatrixProjection] = Raylib.GetShaderLocation(_outline, "matProjection");
        _fill.Locs[(int)ShaderLocationIndex.MatrixView] = Raylib.GetShaderLocation(_fill, "matView");
        _fill.Locs[(int)ShaderLocationIndex.MatrixProjection] = Raylib.GetShaderLocation(_fill, "matProjection");
        _postRes = Raylib.GetShaderLocation(_post, "resolution");
        _postSeed = Raylib.GetShaderLocation(_post, "seed");
        _postWeave = Raylib.GetShaderLocation(_post, "weave");
        _postExposure = Raylib.GetShaderLocation(_post, "exposure");
        _postFlash = Raylib.GetShaderLocation(_post, "flash");
        _postRough = Raylib.GetShaderLocation(_post, "rough");
        _postDesat = Raylib.GetShaderLocation(_post, "desat");
        _postVignette = Raylib.GetShaderLocation(_post, "vignette");

        Raylib.SetShaderValue(_fill, _fillFog, ToVec3(Palette.Fog), ShaderUniformDataType.Vec3);

        _meshes[(int)Shape3.Cube] = Raylib.GenMeshCube(1, 1, 1);
        _meshes[(int)Shape3.Sphere] = Raylib.GenMeshSphere(0.5f, 14, 18);
        _meshes[(int)Shape3.Cylinder] = Raylib.GenMeshCylinder(0.5f, 1, 16);
        _meshes[(int)Shape3.Cone] = Raylib.GenMeshCone(0.5f, 1, 16);

        _fillMat = Raylib.LoadMaterialDefault();
        _fillMat.Shader = _fill;
        _outlineMat = Raylib.LoadMaterialDefault();
        _outlineMat.Shader = _outline;
        _outlineMat.Maps[0].Color = Palette.Ink;
        _white = _fillMat.Maps[0].Texture;

        Resize();
    }

    static Vector3 ToVec3(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f);

    void Resize()
    {
        if (_target.Id != 0) Raylib.UnloadRenderTexture(_target);
        _target = Raylib.LoadRenderTexture(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        Raylib.SetTextureFilter(_target.Texture, TextureFilter.Bilinear);
    }

    // --------------------------------------------------------------- fotograma

    /// <summary>Avanza el "reloj de la película": la tinta y el grano cambian a 12 fps, como animación a doses.</summary>
    public void Tick(double time)
    {
        float frame = MathF.Floor((float)time * 12f);
        if (frame != Seed)
        {
            Seed = frame;
            _weave = new Vector2((float)(_rng.NextDouble() - 0.5) * 0.0016f, (float)(_rng.NextDouble() - 0.5) * 0.0022f);
            _exposure = 0.97f + (float)_rng.NextDouble() * 0.06f;
        }
    }

    public void SetLights(ReadOnlySpan<Vector4> lights)
    {
        Array.Clear(_lights);
        for (int i = 0; i < Math.Min(6, lights.Length); i++)
        {
            _lights[i * 4] = lights[i].X; _lights[i * 4 + 1] = lights[i].Y;
            _lights[i * 4 + 2] = lights[i].Z; _lights[i * 4 + 3] = lights[i].W;
        }
    }

    public void BeginScene(Camera3D camera)
    {
        if (_target.Texture.Width != Raylib.GetScreenWidth() || _target.Texture.Height != Raylib.GetScreenHeight()) Resize();
        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(Palette.SkyHorizon);
        PaintSky();
        Raylib.BeginMode3D(camera);
        Raylib.SetShaderValue(_fill, _fillView, camera.Position, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_fill, _fillSeed, Seed % 97f, ShaderUniformDataType.Float);
        Raylib.SetShaderValueV(_fill, _fillLights, _lights, ShaderUniformDataType.Vec4, 6);
        Raylib.SetShaderValue(_outline, _outView, camera.Position, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_outline, _outSeed, Seed % 97f, ShaderUniformDataType.Float);
    }

    void PaintSky()
    {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        Raylib.DrawRectangleGradientV(0, 0, w, h, Palette.SkyTop, Palette.SkyHorizon);
        // Nubes pintadas: manchas grandes y suaves que se mueven muy despacio.
        var cloud = new Random(11);
        float t = (float)Raylib.GetTime() * 4f;
        for (int i = 0; i < 26; i++)
        {
            float x = (float)((cloud.NextDouble() * (w + 400) + t * (0.3 + cloud.NextDouble())) % (w + 400)) - 200;
            float y = (float)(cloud.NextDouble() * h * 0.42);
            float r = 50 + (float)cloud.NextDouble() * 120;
            Raylib.DrawEllipse((int)x, (int)y, r * 1.8f, r * 0.45f, Palette.Alpha(Palette.Parchment, 0.12f + (float)cloud.NextDouble() * 0.12f));
        }
    }

    /// <param name="inFilm">Dibujo 2D que forma parte de la película (recibe grano, sepia y polvo).</param>
    public void EndScene(Action? inFilm = null)
    {
        Raylib.EndMode3D();
        inFilm?.Invoke();
        Raylib.EndTextureMode();

        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        Raylib.SetShaderValue(_post, _postRes, new Vector2(w, h), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_post, _postSeed, Seed % 113f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_post, _postWeave, _weave, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_post, _postExposure, _exposure, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_post, _postFlash, Math.Clamp(Flash, 0, 1), ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_post, _postRough, Math.Clamp(Rough, 0, 1), ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_post, _postDesat, Math.Clamp(Desaturate, 0, 1), ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_post, _postVignette, Math.Clamp(Vignette, 0, 1), ShaderUniformDataType.Float);
        Raylib.BeginShaderMode(_post);
        // Las render textures salen invertidas en Y.
        Raylib.DrawTextureRec(_target.Texture, new Rectangle(0, 0, w, -h), Vector2.Zero, Color.White);
        Raylib.EndShaderMode();
    }

    // --------------------------------------------------------------- piezas

    /// <summary>Matriz de raylib a partir de escala, rotación y posición (convención de System.Numerics traspuesta).</summary>
    public static Matrix4x4 Trs(Vector3 position, Quaternion rotation, Vector3 scale) =>
        Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);

    /// <summary>Dibuja una primitiva: primero el casco de tinta y después el relleno pintado.</summary>
    /// <param name="world">Transformación en convención de System.Numerics (se traspone para raylib).</param>
    public void Draw(Shape3 shape, Matrix4x4 world, Color color, float outline = 0.035f, float emissive = 0f)
    {
        Mesh mesh = _meshes[(int)shape];
        // Los cilindros y conos de raylib apoyan la base en y = 0: se centran.
        if (shape is Shape3.Cylinder or Shape3.Cone)
            world = Matrix4x4.CreateTranslation(0, -0.5f, 0) * world;
        Matrix4x4 m = Matrix4x4.Transpose(world);

        if (outline > 0)
        {
            Raylib.SetShaderValue(_outline, _outWidth, outline, ShaderUniformDataType.Float);
            Rlgl.SetCullFace(0); // cara delantera: queda solo el reverso engordado
            Raylib.DrawMesh(mesh, _outlineMat, m);
            Rlgl.SetCullFace(1);
        }

        _fillMat.Maps[0].Color = color;
        Raylib.SetShaderValue(_fill, _fillEmissive, emissive, ShaderUniformDataType.Float);
        Raylib.DrawMesh(mesh, _fillMat, m);
    }

    public void Draw(Shape3 shape, Vector3 position, Quaternion rotation, Vector3 scale, Color color, float outline = 0.035f, float emissive = 0f) =>
        Draw(shape, Trs(position, rotation, scale), color, outline, emissive);

    /// <summary>
    /// Dibuja un modelo (ya posado) con casco de tinta y relleno pintado, usando la textura de cada material.
    /// <paramref name="emissive"/> decide el brillo propio por material (ojos, runas...).
    /// </summary>
    /// <param name="hidden">Mallas que no se dibujan (bit i = malla i): accesorios que el modelo trae de más.</param>
    public void DrawModel(Model model, Matrix4x4 world, Color tint, float outline = 0.03f, Func<int, float>? emissive = null, ulong hidden = 0)
    {
        Matrix4x4 m = Matrix4x4.Transpose(world);
        for (int i = 0; i < model.MeshCount; i++)
        {
            if (i < 64 && (hidden >> i & 1) != 0) continue;
            Mesh mesh = model.Meshes[i];
            int mat = model.MeshMaterial[i];
            if (outline > 0)
            {
                Raylib.SetShaderValue(_outline, _outWidth, outline, ShaderUniformDataType.Float);
                Rlgl.SetCullFace(0);
                Raylib.DrawMesh(mesh, _outlineMat, m);
                Rlgl.SetCullFace(1);
            }
            Material source = model.Materials[mat];
            Color c = source.Maps[0].Color;
            _fillMat.Maps[0].Texture = source.Maps[0].Texture.Id != 0 ? source.Maps[0].Texture : _white;
            _fillMat.Maps[0].Color = new Color(c.R * tint.R / 255, c.G * tint.G / 255, c.B * tint.B / 255, c.A * tint.A / 255);
            Raylib.SetShaderValue(_fill, _fillEmissive, emissive?.Invoke(mat) ?? 0f, ShaderUniformDataType.Float);
            Raylib.DrawMesh(mesh, _fillMat, m);
        }
        _fillMat.Maps[0].Texture = _white;
    }

    /// <summary>Tubo de "manguera de goma" entre dos puntos, curvado con un punto de control.</summary>
    public void Hose(Vector3 a, Vector3 control, Vector3 b, float radius, Color color, int segments = 6)
    {
        Vector3 prev = a;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector3 p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * control + t * t * b;
            Segment(prev, p, radius, color);
            prev = p;
        }
    }

    public void Segment(Vector3 a, Vector3 b, float radius, Color color, float outline = 0.03f)
    {
        Vector3 d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return;
        Quaternion q = FromTo(Vector3.UnitY, d / len);
        Draw(Shape3.Cylinder, (a + b) / 2, q, new Vector3(radius * 2, len + radius, radius * 2), color, outline);
    }

    public static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        float dot = Vector3.Dot(from, to);
        if (dot > 0.9999f) return Quaternion.Identity;
        if (dot < -0.9999f) return Quaternion.CreateFromAxisAngle(MathF.Abs(from.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ, MathF.PI);
        Vector3 axis = Vector3.Normalize(Vector3.Cross(from, to));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(dot));
    }

    /// <summary>Sombra de tinta: una mancha negra aplastada contra el suelo.</summary>
    public void BlobShadow(Vector3 at, float radius, float alpha = 0.55f)
    {
        Draw(Shape3.Cylinder, at + new Vector3(0, 0.02f, 0), Quaternion.Identity, new Vector3(radius * 2, 0.02f, radius * 1.6f), Palette.Alpha(Palette.Ink, alpha), outline: 0, emissive: 1f);
    }

    public void Dispose()
    {
        foreach (Mesh m in _meshes) Raylib.UnloadMesh(m);
        Raylib.UnloadRenderTexture(_target);
        Raylib.UnloadShader(_fill);
        Raylib.UnloadShader(_outline);
        Raylib.UnloadShader(_post);
    }
}
