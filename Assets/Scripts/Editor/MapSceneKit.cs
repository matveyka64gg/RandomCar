using RandomCar.Car;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RandomCar.EditorTools
{
    /// Свет и атмосфера карты: солнце, процедурное небо, окружающий свет, туман, постобработка.
    public struct MapLook
    {
        public string Sun;
        public float SunIntensity;
        public Vector3 SunEuler;
        public string SkyTint;
        public string SkyGround;
        public float SkyExposure;
        public float Atmosphere;
        public string AmbientSky;
        public string AmbientEquator;
        public string AmbientGround;
        public string Fog;
        public float FogStart;
        public float FogEnd;
        public float PostExposure;
        public float Contrast;
        public float Saturation;
        public float Temperature;
        public float BloomIntensity;
        public float BloomThreshold;
        public float Vignette;
    }

    /// Общая обвязка сцены карты: новая сцена, свет и небо, постобработка, следы шин, зонд отражений, сохранение.
    public static class MapSceneKit
    {
        public static Scene NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        public static Light SetupLook(string key, MapLook look)
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = ArtLibrary.Hex(look.Sun);
            sun.intensity = look.SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.transform.rotation = Quaternion.Euler(look.SunEuler);

            RenderSettings.skybox = SkyMaterial(key, look);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ArtLibrary.Hex(look.AmbientSky);
            RenderSettings.ambientEquatorColor = ArtLibrary.Hex(look.AmbientEquator);
            RenderSettings.ambientGroundColor = ArtLibrary.Hex(look.AmbientGround);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = ArtLibrary.Hex(look.Fog);
            RenderSettings.fogStartDistance = look.FogStart;
            RenderSettings.fogEndDistance = look.FogEnd;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = VolumeProfile(key, look);
            return sun;
        }

        static Material SkyMaterial(string key, MapLook look)
        {
            string path = $"{ArtLibrary.MaterialsDir}/M_Sky_{key}.mat";
            var shader = Shader.Find("Skybox/Procedural");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetFloat("_SunSize", 0.035f);
            m.SetFloat("_SunSizeConvergence", 6f);
            m.SetFloat("_AtmosphereThickness", look.Atmosphere);
            m.SetColor("_SkyTint", ArtLibrary.Hex(look.SkyTint));
            m.SetColor("_GroundColor", ArtLibrary.Hex(look.SkyGround));
            m.SetFloat("_Exposure", look.SkyExposure);
            EditorUtility.SetDirty(m);
            return m;
        }

        static VolumeProfile VolumeProfile(string key, MapLook look)
        {
            string dir = $"{BuilderKit.GeneratedDir}/{key}";
            ArtLibrary.EnsureFolder(dir);
            string path = $"{dir}/Volume_{key}.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(look.BloomThreshold);
            bloom.intensity.Override(look.BloomIntensity);
            bloom.scatter.Override(0.62f);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(look.PostExposure);
            color.contrast.Override(look.Contrast);
            color.saturation.Override(look.Saturation);
            var white = profile.Add<WhiteBalance>(true);
            white.temperature.Override(look.Temperature);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(look.Vignette);
            vignette.smoothness.Override(0.45f);

            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// Следы шин карты: один динамический меш, обновляется в игре.
        public static void AddSkidMarks()
        {
            var skids = new GameObject("SkidMarks");
            skids.AddComponent<MeshFilter>();
            var renderer = skids.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = FxFactory.SkidMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            skids.AddComponent<SkidMarks>();
        }

        /// Сохранить сцену, запечь зонд отражений и сохранить ещё раз.
        public static void Finish(Scene scene, string scenePath, string key, Vector3 probeCenter, Vector3 probeSize)
        {
            var probeGo = new GameObject("ReflectionProbe");
            probeGo.transform.position = probeCenter;
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = probeSize;
            probe.resolution = 256;
            probe.boxProjection = false;
            probe.importance = 1;

            EditorSceneManager.SaveScene(scene, scenePath);
            string probePath = $"{BuilderKit.GeneratedDir}/{key}/ReflectionProbe_{key}.exr";
            if (Lightmapping.BakeReflectionProbe(probe, probePath))
            {
                probe.bakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(probePath);
                RenderSettings.customReflectionTexture = probe.bakedTexture;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"[RandomCar] Карта собрана: {scenePath}, объектов {Object.FindObjectsByType<Transform>().Length}");
        }
    }
}
