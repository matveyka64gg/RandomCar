using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RandomCar.EditorTools
{
    /// Частицы для событий и машин. Создаются в редакторе как дочерние объекты префабов, в игре только запускаются.
    public static class FxFactory
    {
        public enum Kind { Dust, Debris, Explosion, Smoke, Sparks, Muzzle, Trail, Confetti, Wind, WindLime, Boost, CarDust }

        static Material soft;
        static Material additive;

        public static void EnsureAssets()
        {
            var dot = SoftDot();
            soft = ParticleMaterial("M_FxSoft", dot, false);
            additive = ParticleMaterial("M_FxAdd", dot, true);
        }

        public static ParticleSystem Create(Kind kind, Transform parent, string name)
        {
            if (soft == null) EnsureAssets();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            var shape = ps.shape;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = soft;

            switch (kind)
            {
                case Kind.Dust:
                    Setup(main, false, 2f, (1.0f, 1.8f), (0.4f, 1.8f), (1.4f, 2.6f), C(0.86f, 0.74f, 0.55f, 0.75f), C(0.72f, 0.58f, 0.40f, 0.6f), -0.05f, 120);
                    main.loop = true;
                    emission.rateOverTime = 22f;
                    Shape(shape, ParticleSystemShapeType.Cone, 1.6f, 25f);
                    Fade(ps, 0.9f, 1.8f);
                    break;
                case Kind.CarDust:
                    Setup(main, false, 1f, (0.6f, 1.1f), (0.5f, 2.2f), (0.8f, 1.6f), C(0.86f, 0.76f, 0.58f, 0.7f), C(0.75f, 0.66f, 0.5f, 0.5f), -0.02f, 80);
                    emission.rateOverTime = 0f;
                    Shape(shape, ParticleSystemShapeType.Box, 0f, 0f, new Vector3(1.8f, 0.2f, 3.6f));
                    Fade(ps, 0.8f, 2f);
                    break;
                case Kind.Debris:
                    Setup(main, false, 1f, (0.9f, 1.5f), (4f, 9f), (0.14f, 0.34f), C(0.48f, 0.32f, 0.2f, 1f), C(0.35f, 0.32f, 0.3f, 1f), 1.6f, 80);
                    Burst(emission, 36);
                    Shape(shape, ParticleSystemShapeType.Hemisphere, 1.2f, 0f);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    Fade(ps, 1f, 1f);
                    break;
                case Kind.Explosion:
                    renderer.sharedMaterial = additive;
                    Setup(main, false, 1f, (0.35f, 0.8f), (2.5f, 8f), (1.6f, 3.6f), C(1f, 0.85f, 0.35f, 1f), C(1f, 0.45f, 0.12f, 1f), -0.2f, 80);
                    Burst(emission, 34);
                    Shape(shape, ParticleSystemShapeType.Sphere, 0.8f, 0f);
                    var col = ps.colorOverLifetime;
                    col.enabled = true;
                    col.color = Gradient((0f, new Color(1f, 0.95f, 0.6f)), (0.4f, new Color(1f, 0.5f, 0.15f)), (1f, new Color(0.3f, 0.12f, 0.06f)), 1f, 0f);
                    Grow(ps, 0.6f, 1.6f);
                    break;
                case Kind.Smoke:
                    Setup(main, false, 1f, (2.2f, 3.4f), (0.8f, 3.2f), (2.4f, 4.4f), C(0.42f, 0.42f, 0.44f, 0.75f), C(0.62f, 0.6f, 0.58f, 0.6f), -0.12f, 60);
                    Burst(emission, 22);
                    Shape(shape, ParticleSystemShapeType.Hemisphere, 1.6f, 0f);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    Fade(ps, 0.6f, 2.2f);
                    break;
                case Kind.Sparks:
                    renderer.sharedMaterial = additive;
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.velocityScale = 0.06f;
                    renderer.lengthScale = 1.5f;
                    Setup(main, false, 1f, (0.3f, 0.7f), (8f, 17f), (0.07f, 0.14f), C(1f, 0.92f, 0.5f, 1f), C(1f, 0.6f, 0.2f, 1f), 1.4f, 120);
                    Burst(emission, 48);
                    Shape(shape, ParticleSystemShapeType.Hemisphere, 0.4f, 0f);
                    Fade(ps, 1f, 0.6f);
                    break;
                case Kind.Muzzle:
                    renderer.sharedMaterial = additive;
                    Setup(main, false, 0.5f, (0.08f, 0.2f), (1f, 6f), (0.8f, 2.4f), C(1f, 0.9f, 0.5f, 1f), C(1f, 0.55f, 0.2f, 1f), 0f, 30);
                    Burst(emission, 12);
                    Shape(shape, ParticleSystemShapeType.Cone, 0.15f, 18f);
                    Fade(ps, 1f, 1.4f);
                    break;
                case Kind.Trail:
                    Setup(main, true, 1f, (0.6f, 1.0f), (0.1f, 0.6f), (0.5f, 1.1f), C(0.95f, 0.95f, 0.95f, 0.8f), C(0.8f, 0.8f, 0.82f, 0.6f), -0.05f, 200);
                    emission.rateOverTime = 70f;
                    Shape(shape, ParticleSystemShapeType.Sphere, 0.15f, 0f);
                    Fade(ps, 0.8f, 2.2f);
                    break;
                case Kind.Confetti:
                    Setup(main, false, 1f, (1.2f, 1.9f), (4f, 9f), (0.14f, 0.26f), C(1f, 0.3f, 0.2f, 1f), C(0.2f, 0.9f, 0.8f, 1f), 0.8f, 120);
                    main.startColor = new ParticleSystem.MinMaxGradient(Gradient((0f, ArtLibrary.Hex("#FF4B2B")), (0.33f, ArtLibrary.Hex("#FFC21A")), (0.66f, ArtLibrary.Hex("#24E0C8")), (1f, ArtLibrary.Hex("#A6F23A")), 1f, 1f))
                    {
                        mode = ParticleSystemGradientMode.RandomColor,
                    };
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    Burst(emission, 60);
                    Shape(shape, ParticleSystemShapeType.Sphere, 0.6f, 0f);
                    Fade(ps, 1f, 1f);
                    break;
                case Kind.Wind:
                case Kind.WindLime:
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.velocityScale = 0.12f;
                    renderer.lengthScale = 2f;
                    var windColor = kind == Kind.Wind ? C(0.95f, 0.97f, 1f, 0.55f) : C(0.65f, 0.95f, 0.23f, 0.75f);
                    Setup(main, true, 1f, (0.6f, 1.0f), (16f, 24f), (0.05f, 0.09f), windColor, windColor, 0f, 300);
                    emission.rateOverTime = 30f;
                    Shape(shape, ParticleSystemShapeType.Box, 0f, 0f, new Vector3(kind == Kind.Wind ? 10f : 3.4f, 2.2f, 1f));
                    Fade(ps, 1f, 1f);
                    break;
                case Kind.Boost:
                    renderer.sharedMaterial = additive;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    Setup(main, true, 1f, (0.12f, 0.22f), (3f, 5f), (0.28f, 0.5f), C(0.3f, 1f, 0.95f, 1f), C(0.15f, 0.6f, 1f, 1f), 0f, 80);
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    emission.rateOverTime = 70f;
                    emission.enabled = false;
                    Shape(shape, ParticleSystemShapeType.Cone, 0.12f, 12f, default, new Vector3(0f, 180f, 0f));
                    Fade(ps, 1f, 0.2f);
                    ps.Play();
                    main.playOnAwake = true;
                    break;
            }
            return ps;
        }

        // ---------- настройка ----------

        static void Setup(ParticleSystem.MainModule main, bool loop, float duration, (float, float) life, (float, float) speed,
                          (float, float) size, Color a, Color b, float gravity, int max)
        {
            main.loop = loop;
            main.duration = duration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.Item1, life.Item2);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.Item1, speed.Item2);
            main.startSize = new ParticleSystem.MinMaxCurve(size.Item1, size.Item2);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.gravityModifier = gravity;
            main.maxParticles = max;
        }

        static void Burst(ParticleSystem.EmissionModule emission, int count)
        {
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        static void Shape(ParticleSystem.ShapeModule shape, ParticleSystemShapeType type, float radius, float angle,
                          Vector3 scale = default, Vector3 rotation = default)
        {
            shape.enabled = true;
            shape.shapeType = type;
            shape.radius = Mathf.Max(0.01f, radius);
            shape.angle = angle;
            if (scale != default) shape.scale = scale;
            if (rotation != default) shape.rotation = rotation;
        }

        static void Fade(ParticleSystem ps, float startScale, float endScale)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Gradient((0f, Color.white), (1f, Color.white), 1f, 0f);
            Grow(ps, startScale, endScale);
        }

        static void Grow(ParticleSystem ps, float from, float to)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from, 1f, to));
        }

        static Gradient Gradient((float, Color) k0, (float, Color) k1, float alphaStart, float alphaEnd)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(k0.Item2, k0.Item1), new GradientColorKey(k1.Item2, k1.Item1) },
                      new[] { new GradientAlphaKey(alphaStart, 0f), new GradientAlphaKey(alphaStart, 0.6f), new GradientAlphaKey(alphaEnd, 1f) });
            return g;
        }

        static Gradient Gradient((float, Color) k0, (float, Color) k1, (float, Color) k2, float alphaStart, float alphaEnd)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(k0.Item2, k0.Item1), new GradientColorKey(k1.Item2, k1.Item1), new GradientColorKey(k2.Item2, k2.Item1) },
                      new[] { new GradientAlphaKey(alphaStart, 0f), new GradientAlphaKey(alphaStart * 0.9f, 0.5f), new GradientAlphaKey(alphaEnd, 1f) });
            return g;
        }

        static Gradient Gradient((float, Color) k0, (float, Color) k1, (float, Color) k2, (float, Color) k3, float alphaStart, float alphaEnd)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(k0.Item2, k0.Item1), new GradientColorKey(k1.Item2, k1.Item1), new GradientColorKey(k2.Item2, k2.Item1), new GradientColorKey(k3.Item2, k3.Item1) },
                      new[] { new GradientAlphaKey(alphaStart, 0f), new GradientAlphaKey(alphaEnd, 1f) });
            return g;
        }

        static Color C(float r, float g, float b, float a) => new Color(r, g, b, a);

        // ---------- ассеты ----------

        static Texture2D SoftDot()
        {
            string path = $"{ArtLibrary.TexturesDir}/T_SoftDot.png";
            if (!File.Exists(path))
            {
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
                    }
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material ParticleMaterial(string name, Texture2D texture, bool additiveBlend)
        {
            string path = $"{ArtLibrary.MaterialsDir}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", Color.white);
            Transparent(m, additiveBlend);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// Прозрачный материал URP без инспектора: тип поверхности, режим смешивания, без записи глубины.
        public static void Transparent(Material m, bool additiveBlend)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additiveBlend ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additiveBlend ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", additiveBlend ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
        }
    }
}
