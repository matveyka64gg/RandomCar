using System.Collections.Generic;
using System.Linq;
using RandomCar.Car;
using RandomCar.Core;
using RandomCar.Race;
using RandomCar.Track;
using RandomCar.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RandomCar.EditorTools
{
    /// Однократная сборка мира в сцену Assets/Scenes/Main.unity: трасса, окружение, свет, машины и игровые системы.
    /// Результат — обычная сохранённая сцена. В игре мир не пересоздаётся.
    public static class WorldBuilder
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        const string GeneratedDir = "Assets/Art/Generated";
        const string PrefabsDir = "Assets/Prefabs/Cars";
        const string ModelsDir = "Assets/Art/Models";
        const int Seed = 7;

        static readonly string[] CarKeys = { "Iskra", "Kometa", "Pchelka", "Myata" };
        static readonly string[] CarNames = { "Искра", "Комета", "Пчёлка", "Мята" };

        // Решётка за линией старта: расстояние назад и смещение вбок.
        public static readonly float[] GridDistances = { 8f, 15f, 22f, 29f };
        public static readonly float[] GridLaterals = { -3f, 3f, -3f, 3f };

        [MenuItem("RandomCar/Build Main Scene")]
        public static void BuildAll()
        {
            ArtLibrary.CreateAll();
            ReimportModels();
            var carPrefabs = BuildCarPrefabs();
            var eventSettings = EventPrefabBuilder.EnsureSettings();
            var eventPrefabs = EventPrefabBuilder.BuildPrefabs();
            ConfigureRenderPipeline();
            BuildScene(carPrefabs, eventSettings, eventPrefabs);
        }

        [MenuItem("RandomCar/Reimport Models")]
        public static void ReimportModels()
        {
            int total = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ModelsDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                total += ModelImportRules.RemapMaterials(path);
            }
            Debug.Log($"[RandomCar] Материалов подменено на библиотечные: {total}");
        }

        // ---------- машины ----------

        public static GameObject[] BuildCarPrefabs()
        {
            ArtLibrary.EnsureFolder(PrefabsDir);
            FxFactory.EnsureAssets();
            var result = new GameObject[CarKeys.Length];
            for (int i = 0; i < CarKeys.Length; i++)
            {
                string key = CarKeys[i];
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsDir}/Cars/Car_{key}.fbx");
                var root = new GameObject($"Car_{key}");
                var cc = root.AddComponent<CharacterController>();
                cc.skinWidth = 0.05f;
                cc.radius = 0.85f;
                cc.height = 1.7f;
                cc.center = new Vector3(0f, 0.9f, 0f);
                cc.stepOffset = 0.3f;
                root.AddComponent<CarController>();

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = "Model";
                instance.transform.SetParent(root.transform, false);

                Transform fl = Find(instance.transform, "Wheel_FL"), fr = Find(instance.transform, "Wheel_FR");
                Transform rl = Find(instance.transform, "Wheel_RL"), rr = Find(instance.transform, "Wheel_RR");
                Transform body = Find(instance.transform, "Body");
                // Проверка ориентации: передние колёса должны быть впереди (+z), левые слева (-x).
                Vector3 flLocal = root.transform.InverseTransformPoint(fl.position);
                if (flLocal.z < 0f)
                {
                    instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * instance.transform.localRotation;
                    (fl, rr) = (rr, fl);
                    (fr, rl) = (rl, fr);
                    Debug.Log($"[RandomCar] {key}: модель развёрнута на 180°");
                }
                float radius = fl.GetComponent<Renderer>().bounds.extents.y;

                var visuals = root.AddComponent<CarVisuals>();
                visuals.Configure(body, new[] { fl, fr }, new[] { rl, rr }, radius);
                float rear = root.transform.InverseTransformPoint(body.GetComponent<Renderer>().bounds.min).z;
                var boost = FxFactory.Create(FxFactory.Kind.Boost, root.transform, "FxBoost");
                boost.transform.localPosition = new Vector3(0f, 0.45f, rear - 0.05f);
                var dust = FxFactory.Create(FxFactory.Kind.CarDust, root.transform, "FxDust");
                dust.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                visuals.ConfigureFx(boost, dust);

                string path = $"{PrefabsDir}/Car_{key}.prefab";
                result[i] = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                Debug.Log($"[RandomCar] {key}: колесо FL {flLocal}, радиус {radius:F2}");
            }
            return result;
        }

        // ---------- рендер ----------

        static void ConfigureRenderPipeline()
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset
                        ?? QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return;
            var so = new SerializedObject(asset);
            Set(so, "m_MSAA", 4);
            Set(so, "m_ShadowDistance", 220f);
            Set(so, "m_ShadowCascadeCount", 4);
            Set(so, "m_MainLightShadowmapResolution", 4096);
            Set(so, "m_SoftShadowsSupported", true);
            Set(so, "m_SupportsHDR", true);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        static void Set(SerializedObject so, string name, object value)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[RandomCar] нет свойства URP {name}"); return; }
            switch (value)
            {
                case int i: p.intValue = i; break;
                case float f: p.floatValue = f; break;
                case bool b: p.boolValue = b; break;
            }
        }

        static VolumeProfile BuildVolumeProfile()
        {
            string path = $"{GeneratedDir}/RaceVolume.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.62f);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.25f);
            color.contrast.Override(6f);
            color.saturation.Override(4f);
            var white = profile.Add<WhiteBalance>(true);
            white.temperature.Override(6f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.17f);
            vignette.smoothness.Override(0.45f);

            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ---------- сцена ----------

        static void BuildScene(GameObject[] carPrefabs, RandomCar.Events.EventSettings eventSettings, List<GameObject> eventPrefabs)
        {
            ArtLibrary.EnsureFolder(GeneratedDir);
            ArtLibrary.EnsureFolder("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var random = new System.Random(Seed);
            var path = TrackLayout.CreatePath();
            var track = new TrackMeshBuilder(path);

            // Свет, небо, туман
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = ArtLibrary.Hex("#FFE7C2");
            sun.intensity = 1.8f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.transform.rotation = Quaternion.Euler(38f, -128f, 0f);

            var sky = SkyMaterial();
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ArtLibrary.Hex("#A9CFEA");
            RenderSettings.ambientEquatorColor = ArtLibrary.Hex("#D5DFCB");
            RenderSettings.ambientGroundColor = ArtLibrary.Hex("#5E6B4E");
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = ArtLibrary.Hex("#CFE5F2");
            RenderSettings.fogStartDistance = 260f;
            RenderSettings.fogEndDistance = 1900f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = BuildVolumeProfile();

            // Трасса
            var trackRoot = new GameObject("Track");
            trackRoot.AddComponent<RaceTrack>();
            var ground = Place(SaveMesh(TrackMeshBuilder.Ground(3600f, -0.03f)), "Ground", trackRoot.transform, "M_Grass");
            var groundCollider = new GameObject("GroundCollider").AddComponent<BoxCollider>();
            groundCollider.transform.SetParent(trackRoot.transform);
            groundCollider.size = new Vector3(3600f, 1f, 3600f);
            groundCollider.center = new Vector3(0f, -0.5f, 0f);
            Place(SaveMesh(track.Road()), "Road", trackRoot.transform, "M_Asphalt");
            Place(SaveMesh(track.Gravel()), "Gravel", trackRoot.transform, "M_Gravel");
            Place(SaveMesh(track.Kerbs()), "Kerbs", trackRoot.transform, "M_KerbRed", "M_KerbWhite");
            Place(SaveMesh(track.Markings(GridDistances, GridLaterals)), "Markings", trackRoot.transform, "M_Line");
            Place(SaveMesh(track.StartLine()), "StartLine", trackRoot.transform, "M_Decal_White", "M_Decal_Black");
            var walls = Place(SaveMesh(track.Walls()), "Walls", trackRoot.transform, "M_Concrete", "M_Brand");
            walls.AddComponent<MeshCollider>();
            var posts = new List<Pose>();
            foreach (int side in new[] { -1, 1 })
            {
                var rail = Place(SaveMesh(track.Rails(side, posts)), side > 0 ? "Rail_Right" : "Rail_Left", trackRoot.transform, "M_Steel");
                rail.AddComponent<MeshCollider>();
            }
            var postRoot = Group("GuardrailPosts", trackRoot.transform);
            foreach (var pose in posts) Instance("GuardrailPost", postRoot, pose.position, pose.rotation);

            // Объекты у трассы
            var props = Group("TrackProps", null);
            Vector3 p0 = path.Point(0), t0 = path.TangentAt(0), r0 = path.RightAt(0);
            var gantry = Instance("StartGantry", props, p0, Quaternion.LookRotation(t0));
            var lamps = Enumerable.Range(1, 5).Select(k => Find(gantry.transform, "StartLamp_" + k).GetComponent<Renderer>()).ToArray();
            var lights = gantry.AddComponent<StartLights>();
            lights.Configure(lamps);
            lights.SetLit(0);

            var exclusions = new List<(Vector3 center, float radius)>();
            void Exclude(Vector3 c, float r) => exclusions.Add((c, r));
            Exclude(p0, 18f);

            // Трибуны снаружи стартовой прямой, пит-здание и башня внутри
            for (int k = -1; k <= 1; k++)
            {
                float along = 10f + k * 17f;
                Vector3 c = AtDistance(path, along, 24f, out Vector3 rr);
                Instance("Grandstand", props, c, Quaternion.LookRotation(-rr));
                Exclude(c + rr * 3f, 13f);
            }
            {
                Vector3 c = AtDistance(path, 30f, -30f, out Vector3 rr);
                Instance("PitBuilding", props, c, Quaternion.LookRotation(rr));
                Exclude(c, 30f);
                Vector3 tower = AtDistance(path, 70f, -36f, out _);
                Instance("ControlTower", props, tower, Quaternion.LookRotation(rr));
                Exclude(tower, 9f);
            }
            for (float d = -60f; d <= 85f; d += 14f)
            {
                Vector3 c = AtDistance(path, d, 13.6f, out Vector3 rr);
                if (Mathf.Abs(d - 10f) < 30f) continue;   // за трибунами флаги не нужны
                Instance(((int)(d / 14f)) % 2 == 0 ? "FlagPole_Orange" : "FlagPole_Blue", props, c, Quaternion.LookRotation(-rr));
            }
            for (float d = -55f; d <= 90f; d += 36f)
            {
                Vector3 c = AtDistance(path, d, -13.6f, out Vector3 rr);
                Instance("LightPole", props, c, Quaternion.LookRotation(rr));
            }

            // Углы: шевроны перед крутыми поворотами, реклама и шинные стенки снаружи
            PlaceCornerProps(path, track, props, exclusions);

            // Природа: холмы, горы, облака, лес
            var scenery = Group("Scenery", null);
            Instance("Hills", scenery, Vector3.zero, Quaternion.identity);
            Instance("Mountains", scenery, Vector3.zero, Quaternion.Euler(0f, 37f, 0f));
            for (int i = 0; i < 16; i++)
            {
                float a = (float)(random.NextDouble() * Mathf.PI * 2f);
                float dist = 450f + (float)random.NextDouble() * 650f;
                var cloud = Instance("Cloud_" + "ABC"[i % 3], scenery,
                    new Vector3(Mathf.Cos(a) * dist, 150f + (float)random.NextDouble() * 120f, Mathf.Sin(a) * dist),
                    Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                cloud.transform.localScale = Vector3.one * (1.4f + (float)random.NextDouble() * 1.6f);
                cloud.GetComponentInChildren<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            ScatterNature(path, track, scenery, exclusions, random);

            // Машины на решётке
            var carsRoot = Group("Cars", null);
            var entries = new List<RaceEntry>();
            Transform player = null;
            for (int i = 0; i < carPrefabs.Length; i++)
            {
                float d = path.Length - GridDistances[i];
                float pr = d / path.Length;
                Vector3 pos = path.PointAt(pr) + path.SideAt(pr) * GridLaterals[i];
                var car = (GameObject)PrefabUtility.InstantiatePrefab(carPrefabs[i], scene);
                car.transform.SetParent(carsRoot.transform);
                car.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(path.DirectionAt(pr)));
                bool isPlayer = i == 0;
                if (isPlayer)
                {
                    car.AddComponent<PlayerDriver>();
                    car.name = "Player_" + CarKeys[i];
                    player = car.transform;
                }
                else
                {
                    car.AddComponent<AiDriver>();
                    car.name = "AI_" + CarKeys[i];
                }
                entries.Add(new RaceEntry { Name = CarNames[i], Car = car.GetComponent<CarController>(), IsPlayer = isPlayer });
            }

            // Игровые системы
            var game = new GameObject("Game");
            var race = game.AddComponent<RaceManager>();
            race.Configure(trackRoot.GetComponent<RaceTrack>(), entries, lights);
            var bootstrap = game.AddComponent<GameBootstrap>();

            // Камера
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2600f;
            cam.fieldOfView = 60f;
            cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.Skybox;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            camGo.AddComponent<AudioListener>();
            var follow = camGo.AddComponent<FollowCamera>();
            follow.Configure(player, race);
            camGo.transform.position = player.position - player.forward * 7.2f + Vector3.up * 2.6f;
            camGo.transform.LookAt(player.position + Vector3.up * 1.1f);

            // События: по одному заранее подготовленному экземпляру на тип, выключены до розыгрыша.
            var eventsRoot = Group("Events", null);
            var eventObjects = new List<RandomCar.Events.RaceEvent>();
            foreach (var prefab in eventPrefabs)
            {
                var ev = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                ev.transform.SetParent(eventsRoot.transform, false);
                ev.SetActive(false);
                eventObjects.Add(ev.GetComponent<RandomCar.Events.RaceEvent>());
            }
            var director = game.AddComponent<RandomCar.Events.EventDirector>();
            director.Configure(race, eventSettings, follow, eventObjects);

            // Камера меню: облёт стартовой решётки.
            var menuCamera = camGo.AddComponent<MenuCamera>();
            float gridMid = path.Length - (GridDistances[0] + GridDistances[GridDistances.Length - 1]) / 2f;
            Vector3 gridCenter = path.PointAt(gridMid / path.Length);
            menuCamera.Configure(gridCenter, Mathf.Atan2(-path.DirectionAt(gridMid / path.Length).z, -path.DirectionAt(gridMid / path.Length).x) * Mathf.Rad2Deg + 40f);
            menuCamera.enabled = false;

            // Звук, сценарий и интерфейс.
            game.AddComponent<RandomCar.Audio.AudioDirector>().Configure(race, director);
            var flow = game.AddComponent<GameFlow>();
            bootstrap.Configure(flow);
            var uiGo = new GameObject("UI");
            var document = uiGo.AddComponent<UnityEngine.UIElements.UIDocument>();
            document.panelSettings = UiAssets.PanelSettings();
            var gameUi = uiGo.AddComponent<GameUI>();
            gameUi.Configure(UiAssets.Style(), race, director, flow);
            flow.Configure(race, director, gameUi, follow, menuCamera);
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            // Отражения: зонд запекается один раз в редакторе
            var probeGo = new GameObject("ReflectionProbe");
            probeGo.transform.position = p0 + Vector3.up * 6f;
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(3000f, 800f, 3000f);
            probe.resolution = 256;
            probe.boxProjection = false;
            probe.importance = 1;

            SetStatic(trackRoot);
            SetStatic(props);
            SetStatic(scenery);

            EditorSceneManager.SaveScene(scene, ScenePath);
            string probePath = $"{GeneratedDir}/ReflectionProbe.exr";
            if (Lightmapping.BakeReflectionProbe(probe, probePath))
            {
                probe.bakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(probePath);
                RenderSettings.customReflectionTexture = probe.bakedTexture;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"[RandomCar] Сцена собрана: {ScenePath}, объектов {Object.FindObjectsByType<Transform>().Length}");
        }

        static Material SkyMaterial()
        {
            string path = $"{ArtLibrary.MaterialsDir}/M_Sky.mat";
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
            m.SetFloat("_AtmosphereThickness", 0.85f);
            m.SetColor("_SkyTint", ArtLibrary.Hex("#9CC0E0"));
            m.SetColor("_GroundColor", ArtLibrary.Hex("#7D8B6A"));
            m.SetFloat("_Exposure", 1.2f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- углы ----------

        static void PlaceCornerProps(TrackPath path, TrackMeshBuilder track, GameObject props, List<(Vector3, float)> exclusions)
        {
            int n = path.Count;
            // Апексы: локальные максимумы |кривизны| с радиусом меньше 70 м.
            var apexes = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float k = Mathf.Abs(track.Curvature(i));
                if (k < 1f / 70f) continue;
                bool max = true;
                for (int d = -10; d <= 10 && max; d++)
                    if (d != 0 && Mathf.Abs(track.Curvature(i + d)) > k) max = false;
                if (max && !track.InWallZone(i)) apexes.Add(i);
            }

            var chevron = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsDir}/Environment/ChevronSign.fbx");
            int adIndex = 0;
            foreach (int apex in apexes)
            {
                float k = track.Curvature(apex);
                int outside = k > 0f ? 1 : -1;                    // налево — снаружи справа
                // Шевроны на внешней стороне, смотрят навстречу машинам.
                foreach (int back in new[] { 16, 4 })
                {
                    int i = (apex - back + n) % n;
                    Vector3 pos = path.Point(i) + path.RightAt(i) * (outside * (track.RailOffset(i, outside) + 1.4f));
                    var sign = Instance("ChevronSign", props, pos, Quaternion.LookRotation(-path.TangentAt(i)));
                    if (k < 0f) sign.transform.localScale = new Vector3(-1f, 1f, 1f);   // модель показывает «налево»
                }
                // Реклама за отбойником на выходе из поворота
                {
                    int i = (apex + 8) % n;
                    Vector3 rr = path.RightAt(i) * outside;
                    Vector3 pos = path.Point(i) + rr * (track.RailOffset(i, outside) + 3.5f);
                    Instance(adIndex++ % 2 == 0 ? "AdBoard_A" : "AdBoard_B", props, pos, Quaternion.LookRotation(-rr));
                    exclusions.Add((pos, 5f));
                }
                // Шинная стенка перед отбойником в самых крутых поворотах
                if (Mathf.Abs(k) > 1f / 45f)
                {
                    int stack = 0;
                    for (int s = -12; s <= 12; s++)
                    {
                        int i = (apex + s + n) % n;
                        Vector3 rr = path.RightAt(i) * outside;
                        Vector3 basePos = path.Point(i) + rr * (track.RailOffset(i, outside) - 0.7f);
                        Vector3 tangent = path.TangentAt(i);
                        for (int h = 0; h < 2; h++)
                        {
                            Vector3 pos = basePos + tangent * (h * 0.85f);
                            Instance((stack++ / 4) % 2 == 0 ? "TireStack_Red" : "TireStack_White", props, pos, Quaternion.LookRotation(tangent));
                        }
                    }
                }
            }
        }

        // ---------- лес и камни ----------

        static void ScatterNature(TrackPath path, TrackMeshBuilder track, GameObject root, List<(Vector3 c, float r)> exclusions, System.Random random)
        {
            var placed = new List<Vector3>();
            var grid = new Dictionary<(int, int), List<Vector3>>();
            const float cell = 8f;
            bool Free(Vector3 p, float spacing)
            {
                int cx = Mathf.FloorToInt(p.x / cell), cz = Mathf.FloorToInt(p.z / cell);
                for (int x = cx - 1; x <= cx + 1; x++)
                    for (int z = cz - 1; z <= cz + 1; z++)
                        if (grid.TryGetValue((x, z), out var list))
                            foreach (var q in list)
                                if ((q - p).sqrMagnitude < spacing * spacing) return false;
                return true;
            }
            void Mark(Vector3 p)
            {
                var key = (Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.z / cell));
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<Vector3>();
                list.Add(p);
            }

            int trees = 0, bushes = 0, rocks = 0;
            for (int attempt = 0; attempt < 16000; attempt++)
            {
                float a = (float)(random.NextDouble() * Mathf.PI * 2f);
                float rad = Mathf.Sqrt((float)random.NextDouble()) * 330f;
                var p = new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);

                float progress = path.Nearest(p);
                Vector3 axis = path.PointAt(progress);
                float distance = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(axis.x, 0f, axis.z));
                int nearest = Mathf.RoundToInt(progress * path.Count) % path.Count;
                int side = Vector3.Dot(p - axis, path.SideAt(progress)) > 0f ? 1 : -1;
                float clearance = track.RailOffset(nearest, side) + 3.5f;
                if (distance < clearance) continue;
                if (exclusions.Any(e => (e.c - p).sqrMagnitude < e.r * e.r)) continue;

                // Лесные массивы по низкочастотному шуму, у самой трассы реже.
                float forest = Mathf.PerlinNoise(p.x / 90f + 13.1f, p.z / 90f + 7.7f);
                float near = Mathf.InverseLerp(clearance, clearance + 50f, distance);
                float density = Mathf.Lerp(0.25f, 1f, near) * (forest > 0.5f ? 1f : 0.35f);
                if (random.NextDouble() > density) continue;

                string model;
                float spacing;
                double roll = random.NextDouble();
                if (distance > clearance + 35f && forest > 0.5f)
                {
                    model = roll < 0.75 ? "Pine_" + "ABC"[random.Next(3)] : roll < 0.9 ? "Tree_" + "AB"[random.Next(2)] : "Rock_" + "ABC"[random.Next(3)];
                    spacing = 5.5f;
                }
                else
                {
                    model = roll < 0.35 ? "Tree_" + "AB"[random.Next(2)] : roll < 0.65 ? "Bush_" + "AB"[random.Next(2)] : roll < 0.82 ? "Pine_" + "ABC"[random.Next(3)] : "Rock_" + "ABC"[random.Next(3)];
                    spacing = model.StartsWith("Bush") ? 3f : 6.5f;
                }
                bool isTree = model.StartsWith("Pine") || model.StartsWith("Tree");
                bool isBush = model.StartsWith("Bush");
                bool isRock = model.StartsWith("Rock");
                if (isTree && trees >= 1100 || isBush && bushes >= 320 || isRock && rocks >= 180) continue;
                if (!Free(p, spacing)) continue;

                float scale = 0.8f + (float)random.NextDouble() * 0.5f;
                var go = Instance(model, root, p, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                go.transform.localScale = Vector3.one * scale;
                if (distance < clearance + 40f && !isBush)
                {
                    if (isRock)
                    {
                        var sphere = go.AddComponent<SphereCollider>();
                        sphere.radius = model.EndsWith("C") ? 2f : model.EndsWith("B") ? 1.1f : 0.55f;
                        sphere.center = Vector3.up * sphere.radius * 0.6f;
                    }
                    else
                    {
                        var capsule = go.AddComponent<CapsuleCollider>();
                        capsule.radius = 0.45f;
                        capsule.height = 4f;
                        capsule.center = Vector3.up * 2f;
                    }
                }
                Mark(p);
                placed.Add(p);
                if (isTree) trees++;
                else if (isBush) bushes++;
                else rocks++;
            }
            Debug.Log($"[RandomCar] Природа: деревьев {trees}, кустов {bushes}, камней {rocks}");
        }

        // ---------- помощники ----------

        static Vector3 AtDistance(TrackPath path, float distance, float lateral, out Vector3 right)
        {
            float p = TrackPath.Wrap(distance / path.Length);
            right = path.SideAt(p);
            return path.PointAt(p) + right * lateral;
        }

        static GameObject Instance(string model, GameObject parent, Vector3 position, Quaternion rotation)
        {
            string folder = model.StartsWith("Car_") ? "Cars" : "Environment";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsDir}/{folder}/{model}.fbx");
            if (asset == null)
            {
                Debug.LogError($"[RandomCar] нет модели {model}");
                return new GameObject(model + "_MISSING");
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            if (parent != null) go.transform.SetParent(parent.transform, false);
            go.transform.SetPositionAndRotation(position, rotation);
            return go;
        }

        static GameObject Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        static GameObject Place(Mesh mesh, string name, Transform parent, params string[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.Select(ArtLibrary.Get).ToArray();
            return go;
        }

        static Mesh SaveMesh(Mesh mesh)
        {
            string dir = $"{GeneratedDir}/Track";
            ArtLibrary.EnsureFolder(dir);
            string path = $"{dir}/{mesh.name}.asset";
            // Ассет создаётся заново: копирование в существующий меш оставляло на GPU старый буфер индексов
            // (часть подсетки не рисовалась до перезагрузки). Сцена собирается целиком, ссылки всё равно новые.
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static void SetStatic(GameObject root)
        {
            const StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.OccludeeStatic;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name || root.name.StartsWith(name + ".") || root.name.StartsWith(name + "_")) return root;
            foreach (Transform child in root)
            {
                var found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
