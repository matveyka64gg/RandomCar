using System.Collections.Generic;
using System.Linq;
using RandomCar.Car;
using RandomCar.Maps;
using RandomCar.Race;
using RandomCar.Track;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static RandomCar.EditorTools.BuilderKit;
using MeshBuilder = RandomCar.EditorTools.TrackMeshBuilder.MeshBuilder;

namespace RandomCar.EditorTools
{
    /// Карта «Холмы Рандома» (бета 0.1): солнечный автодром в предгорьях, 2.9 км, 2 круга. Сцена Assets/Scenes/Map_Hills.unity.
    /// Ось x — на восток, z — на север, движение по часовой стрелке:
    ///  1. Стартовая прямая в долине (у = 0): трибуны, пит-здание, башня.
    ///  2. Быстрая правая дуга и подъём вдоль восточного склона.
    ///  3. Серпантин: шпилька R 20 м на подъёме, затем широкий правый поворот на хребет (40 м над стартом).
    ///  4. Хребет: эски с видом на долину и центральный холм, гребень.
    ///  5. Скоростной спуск (~9%) к западной долине, связка поворотов и подъём на бугор перед финишем.
    /// Рельеф — сетка вокруг трассы: у дороги ровная обочина на высоте полотна, дальше склоны, холмы и предгорья по краям.
    /// Мир собирается здесь один раз и сохраняется; машины и игровые системы живут в сцене Game.
    public static class HillsMapBuilder
    {
        public const string Key = "Hills";
        public const string ScenePath = "Assets/Scenes/Map_Hills.unity";
        public const int Laps = 2;
        const int Seed = 7;
        const string Folder = "Hills";
        /// Половина ширины дороги: 14 м — место для обгона в повороте.
        public const float RoadHalf = 7f;
        /// Обочина на высоте полотна: дальше самого дальнего отбойника (гравий + 13.5 м).
        const float Shoulder = 27f;
        const float BlendTo = 85f;
        const float TerrainStep = 8f;

        static readonly Vector2[] Corners =
        {
            new Vector2(-300f, 0f), new Vector2(300f, 0f), new Vector2(470f, -110f), new Vector2(480f, -300f),
            new Vector2(300f, -370f), new Vector2(520f, -440f), new Vector2(330f, -620f), new Vector2(170f, -590f),
            new Vector2(40f, -660f), new Vector2(-150f, -640f), new Vector2(-300f, -560f), new Vector2(-420f, -440f),
            new Vector2(-430f, -260f), new Vector2(-330f, -170f), new Vector2(-380f, -80f),
        };
        static readonly float[] Radii = { 50f, 90f, 70f, 40f, 20f, 40f, 60f, 45f, 45f, 120f, 60f, 50f, 60f, 40f, 30f };
        static readonly float[] Heights = { 0f, 0f, 4f, 16f, 24f, 32f, 40f, 42f, 40f, 36f, 20f, 8f, 4f, 12f, 2f };
        static readonly Vector2 StartPoint = new Vector2(-150f, 0f);

        /// Холмы рельефа: центр (x, z), высота, радиус. Центральный холм внутри петли виден почти отовсюду.
        static readonly (Vector2 c, float h, float r)[] Peaks =
        {
            (new Vector2(40f, -320f), 46f, 150f), (new Vector2(-180f, -330f), 22f, 90f), (new Vector2(260f, -200f), 18f, 70f),
            (new Vector2(-760f, -260f), 70f, 220f), (new Vector2(860f, -520f), 80f, 240f), (new Vector2(120f, 420f), 48f, 200f),
            (new Vector2(-420f, 360f), 55f, 220f), (new Vector2(620f, 300f), 60f, 210f), (new Vector2(-80f, -1060f), 75f, 260f),
            (new Vector2(700f, -1000f), 65f, 220f), (new Vector2(-700f, -880f), 60f, 220f),
        };

        static readonly MapLook Look = new MapLook
        {
            Sun = "#FFE7C2", SunIntensity = 1.8f, SunEuler = new Vector3(38f, -128f, 0f),
            SkyTint = "#9CC0E0", SkyGround = "#7D8B6A", SkyExposure = 1.2f, Atmosphere = 0.85f,
            AmbientSky = "#A9CFEA", AmbientEquator = "#D5DFCB", AmbientGround = "#5E6B4E",
            Fog = "#CFE5F2", FogStart = 320f, FogEnd = 2200f,
            PostExposure = 0.25f, Contrast = 6f, Saturation = 4f, Temperature = 6f,
            BloomIntensity = 0.55f, BloomThreshold = 1.05f, Vignette = 0.17f,
        };

        static float[] GridDistances() => Enumerable.Range(0, 4).Select(MapDefinition.GridDistance).ToArray();
        static float[] GridLaterals() => Enumerable.Range(0, 4).Select(MapDefinition.GridLateral).ToArray();

        public static TrackDefinition EnsureTrack()
        {
            var def = TrackAssets.Ensure(Key);
            var points = TrackShapes.RoundedPolygon(Corners, Radii, 6f, Heights);
            def.ControlPoints = points;
            def.SamplesPerSegment = 3;
            def.StartSample = TrackShapes.Closest(points, StartPoint) * 3;
            def.HalfWidth = RoadHalf;
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            return def;
        }

        public static void Build()
        {
            var definition = EnsureTrack();
            var scene = MapSceneKit.NewScene();
            var random = new System.Random(Seed);
            var path = definition.Path;
            var track = new TrackMeshBuilder(path, RoadHalf);
            var surfaces = PhysicsAssets.EnsureSurfaces();
            MapSceneKit.SetupLook(Key, Look);
            var relief = new Relief(path);

            // Трасса
            var trackRoot = new GameObject("Track");
            var terrain = Place(SaveMesh(relief.Mesh("HillsTerrain", -900f, 1000f, -1150f, 520f, TerrainStep, 0f), Folder), "Terrain", trackRoot.transform, "M_Grass");
            terrain.AddComponent<MeshCollider>();
            terrain.AddComponent<Surface>().Type = surfaces[SurfaceKind.Grass];
            // Дальние предгорья: крупная сетка вокруг (без коллайдера), под ближней сеткой опущена и не видна.
            var far = Place(SaveMesh(relief.FarMesh("HillsTerrainFar", -900f, 1000f, -1150f, 520f), Folder), "TerrainFar", trackRoot.transform, "M_Grass");
            far.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            // Страховка: глубоко под рельефом — земля, чтобы ничто не падало бесконечно (сторож вернёт машину раньше).
            var safety = new GameObject("SafetyGround").AddComponent<BoxCollider>();
            safety.transform.SetParent(trackRoot.transform);
            safety.size = new Vector3(6000f, 1f, 6000f);
            safety.center = new Vector3(0f, -40f, 0f);

            var road = Place(SaveMesh(Named(track.Road(), "HillsRoad"), Folder), "Road", trackRoot.transform, "M_Asphalt");
            road.AddComponent<MeshCollider>();
            road.AddComponent<Surface>().Type = surfaces[SurfaceKind.Asphalt];
            var gravel = Place(SaveMesh(Named(track.Gravel(), "HillsGravel"), Folder), "Gravel", trackRoot.transform, "M_Gravel");
            gravel.AddComponent<MeshCollider>();
            gravel.AddComponent<Surface>().Type = surfaces[SurfaceKind.Gravel];
            var kerbs = Place(SaveMesh(Named(track.Kerbs(), "HillsKerbs"), Folder), "Kerbs", trackRoot.transform, "M_KerbRed", "M_KerbWhite");
            kerbs.AddComponent<MeshCollider>();
            kerbs.AddComponent<Surface>().Type = surfaces[SurfaceKind.Kerb];
            MapSceneKit.AddSkidMarks();
            Place(SaveMesh(Named(track.Markings(GridDistances(), GridLaterals()), "HillsMarkings"), Folder), "Markings", trackRoot.transform, "M_Line");
            Place(SaveMesh(Named(track.StartLine(), "HillsStartLine"), Folder), "StartLine", trackRoot.transform, "M_Decal_White", "M_Decal_Black");
            var walls = Place(SaveMesh(Named(track.Walls(), "HillsWalls"), Folder), "Walls", trackRoot.transform, "M_Concrete", "M_Brand");
            walls.AddComponent<MeshCollider>();
            var posts = new List<Pose>();
            foreach (int side in new[] { -1, 1 })
            {
                var rail = Place(SaveMesh(Named(track.Rails(side, posts), side > 0 ? "HillsRail_Right" : "HillsRail_Left"), Folder), side > 0 ? "Rail_Right" : "Rail_Left", trackRoot.transform, "M_Steel");
                rail.AddComponent<MeshCollider>();
            }
            var postRoot = Group("GuardrailPosts", trackRoot.transform);
            foreach (var pose in posts) Lod(Instance("GuardrailPost", postRoot, pose.position, pose.rotation), 0.015f);

            // Объекты у трассы
            var props = Group("TrackProps", null);
            Vector3 p0 = path.Point(0), t0 = path.TangentAt(0);
            var gantry = Instance("StartGantry", props, p0, Quaternion.LookRotation(t0));
            gantry.transform.localScale = new Vector3(RoadHalf / 6f * 1.05f, 1.08f, 1f);
            var lamps = Enumerable.Range(1, 5).Select(k => Find(gantry.transform, "StartLamp_" + k).GetComponent<Renderer>()).ToArray();
            var lights = gantry.AddComponent<StartLights>();
            lights.Configure(lamps);
            lights.SetLit(0);

            var exclusions = new List<(Vector3 center, float radius)>();
            void Exclude(Vector3 c, float r) => exclusions.Add((c, r));
            Exclude(p0, 18f);

            // Трибуны внутри петли у стартовой прямой, пит-здание и башня снаружи.
            for (int k = -1; k <= 1; k++)
            {
                float along = 10f + k * 17f;
                Vector3 c = AtDistance(path, along, 25f, out Vector3 rr);
                c.y = relief.Height(c);
                Instance("Grandstand", props, c, Quaternion.LookRotation(-rr));
                Exclude(c + rr * 3f, 14f);
            }
            {
                Vector3 c = AtDistance(path, 30f, -31f, out Vector3 rr);
                c.y = relief.Height(c);
                Instance("PitBuilding", props, c, Quaternion.LookRotation(rr));
                Exclude(c, 30f);
                Vector3 tower = AtDistance(path, 70f, -37f, out _);
                tower.y = relief.Height(tower);
                Instance("ControlTower", props, tower, Quaternion.LookRotation(rr));
                Exclude(tower, 9f);
            }
            for (float d = -60f; d <= 85f; d += 14f)
            {
                if (Mathf.Abs(d - 10f) < 30f) continue;   // за трибунами флаги не нужны
                Vector3 c = AtDistance(path, d, RoadHalf + 7.6f, out Vector3 rr);
                c.y = relief.Height(c);
                Instance(((int)(d / 14f)) % 2 == 0 ? "FlagPole_Orange" : "FlagPole_Blue", props, c, Quaternion.LookRotation(-rr));
            }
            for (float d = -55f; d <= 90f; d += 36f)
            {
                Vector3 c = AtDistance(path, d, -(RoadHalf + 7.6f), out Vector3 rr);
                c.y = relief.Height(c);
                Instance("LightPole", props, c, Quaternion.LookRotation(rr));
            }

            // Углы: шевроны перед крутыми поворотами, реклама и шинные стенки снаружи
            PlaceCornerProps(path, track, props, exclusions);

            // Природа: облака, горы на горизонте, лес и камни на склонах
            var scenery = Group("Scenery", null);
            var mountains = Instance("Mountains", scenery, new Vector3(50f, -6f, -300f), Quaternion.Euler(0f, 37f, 0f));
            mountains.transform.localScale = new Vector3(2.4f, 1.7f, 2.4f);
            for (int i = 0; i < 22; i++)
            {
                float a = (float)(random.NextDouble() * Mathf.PI * 2f);
                float dist = 600f + (float)random.NextDouble() * 900f;
                var cloud = Instance("Cloud_" + "ABC"[i % 3], scenery,
                    new Vector3(50f + Mathf.Cos(a) * dist, 190f + (float)random.NextDouble() * 140f, -300f + Mathf.Sin(a) * dist),
                    Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                cloud.transform.localScale = Vector3.one * (1.6f + (float)random.NextDouble() * 1.8f);
                cloud.GetComponentInChildren<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            ScatterNature(path, track, relief, scenery, exclusions, random);

            // Корень карты: трасса, огни, точки камер.
            var mapRoot = new GameObject("Map");
            var raceTrack = mapRoot.AddComponent<RaceTrack>();
            raceTrack.Configure(definition);
            float gridMid = path.Length - (MapDefinition.GridDistance(0) + MapDefinition.GridDistance(3)) / 2f;
            Vector3 gridCenter = path.PointAt(gridMid / path.Length);
            Vector3 gridDir = path.DirectionAt(gridMid / path.Length);
            float yaw = Mathf.Atan2(-gridDir.z, -gridDir.x) * Mathf.Rad2Deg + 40f;
            var map = mapRoot.AddComponent<MapDefinition>();
            map.Configure(Key, "Холмы Рандома", raceTrack, lights, gridCenter, yaw, IntroShots(path, relief));
            map.SetLaps(Laps);
            // Между дорогой и отбойником (≥ 13.5 м от оси) есть ровная обочина для объектов событий.
            map.SetRoadside(RoadHalf + 5.5f);

            SetStatic(trackRoot);
            SetStatic(props);
            SetStatic(scenery);
            float minY = Enumerable.Range(0, path.Count).Min(i => path.Point(i).y), maxY = Enumerable.Range(0, path.Count).Max(i => path.Point(i).y);
            Debug.Log($"[RandomCar] Холмы Рандома: трасса {path.Length:F0} м, высоты {minY:F1}…{maxY:F1} м, кругов {Laps}");
            MapSceneKit.Finish(scene, ScenePath, Key, p0 + Vector3.up * 8f, new Vector3(3000f, 900f, 3000f));
        }

        static Mesh Named(Mesh mesh, string name)
        {
            mesh.name = name;
            return mesh;
        }

        /// LOD: объект перестаёт рисоваться, когда занимает меньше cull высоты экрана.
        static void Lod(GameObject go, float cull)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            if (!go.TryGetComponent<LODGroup>(out var group)) group = go.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(cull, renderers) });
            group.RecalculateBounds();
        }

        /// Облёт камеры перед гонкой: долина с высоты, серпантин, стартовая прямая вблизи.
        static CameraShot[] IntroShots(TrackPath path, Relief relief)
        {
            Vector3 start = path.PointAt(0f);
            Vector3 dir = path.DirectionAt(0f);
            Vector3 right = path.SideAt(0f);
            return new[]
            {
                new CameraShot { From = new Vector3(-520f, 150f, 260f), To = new Vector3(-300f, 120f, 300f), LookFrom = new Vector3(60f, 20f, -320f), LookTo = new Vector3(200f, 20f, -360f), Seconds = 3.2f, Fov = 50f },
                new CameraShot { From = start + dir * 160f + right * 22f + Vector3.up * 14f, To = start + dir * 40f + right * 14f + Vector3.up * 8f, LookFrom = start + dir * 80f, LookTo = start, Seconds = 2.8f, Fov = 46f },
            };
        }

        /// Рельеф: высота в любой точке карты. У дороги — полотно минус 8 см (обочина), дальше — сглаженная «область»
        /// высот трассы, холмы, шум и подъём к краям карты.
        sealed class Relief
        {
            readonly TrackPath path;
            readonly Vector3[] coarse;

            public Relief(TrackPath trackPath)
            {
                path = trackPath;
                var list = new List<Vector3>();
                for (int i = 0; i < path.Count; i += 8) list.Add(path.Point(i));
                coarse = list.ToArray();
            }

            /// Высота трассы, размазанная по местности (обратные квадраты расстояний).
            float Regional(float x, float z)
            {
                float sum = 0f, weight = 0f;
                foreach (var p in coarse)
                {
                    float dx = p.x - x, dz = p.z - z;
                    float w = 1f / (dx * dx + dz * dz + 400f);
                    sum += p.y * w;
                    weight += w;
                }
                return sum / weight;
            }

            public float Natural(float x, float z)
            {
                float h = Regional(x, z);
                foreach (var (c, height, r) in Peaks)
                {
                    float dx = x - c.x, dz = z - c.y;
                    h += height * Mathf.Exp(-(dx * dx + dz * dz) / (2f * r * r));
                }
                h += 9f * (Mathf.PerlinNoise(x / 170f + 4.3f, z / 170f + 9.1f) - 0.45f) + 2.4f * (Mathf.PerlinNoise(x / 48f + 1.7f, z / 48f + 3.2f) - 0.5f);
                // Предгорья: к краям карты земля поднимается (горизонт не плоский).
                float edge = Vector2.Distance(new Vector2(x, z), new Vector2(50f, -300f));
                h += 0.11f * Mathf.Max(0f, edge - 720f);
                return h;
            }

            public float Height(Vector3 point) => Height(point.x, point.z);

            public float Height(float x, float z)
            {
                var point = new Vector3(x, 0f, z);
                float p = path.Nearest(point);
                Vector3 axis = path.PointAt(p);
                float d = Vector2.Distance(new Vector2(x, z), new Vector2(axis.x, axis.z));
                float near = axis.y - 0.08f;
                if (d <= Shoulder) return near;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Shoulder, BlendTo, d));
                return Mathf.Lerp(near, Natural(x, z), t);
            }

            /// Ближняя сетка рельефа (с коллайдером).
            public Mesh Mesh(string name, float x0, float x1, float z0, float z1, float step, float drop)
            {
                var b = new MeshBuilder(name, 1);
                int nx = Mathf.RoundToInt((x1 - x0) / step), nz = Mathf.RoundToInt((z1 - z0) / step);
                var index = new int[nx + 1, nz + 1];
                for (int iz = 0; iz <= nz; iz++)
                    for (int ix = 0; ix <= nx; ix++)
                    {
                        float x = x0 + ix * step, z = z0 + iz * step;
                        index[ix, iz] = b.Vertex(new Vector3(x, Height(x, z) - drop, z), new Vector2(x / 12f, z / 12f));
                    }
                for (int iz = 0; iz < nz; iz++)
                    for (int ix = 0; ix < nx; ix++)
                        b.QuadUp(0, index[ix, iz], index[ix + 1, iz], index[ix + 1, iz + 1], index[ix, iz + 1]);
                return b.Build(recalcNormals: true);
            }

            /// Дальняя сетка: 40 м, до 2.6 км от центра. Внутри ближней сетки (с запасом 40 м) не строится, у края — опущена на 0.4 м.
            public Mesh FarMesh(string name, float x0, float x1, float z0, float z1)
            {
                const float step = 40f, half = 2600f;
                var b = new MeshBuilder(name, 1);
                Vector2 center = new Vector2(50f, -300f);
                int n = Mathf.RoundToInt(half * 2f / step);
                var index = new int[n + 1, n + 1];
                bool Inside(float x, float z, float margin) => x > x0 + margin && x < x1 - margin && z > z0 + margin && z < z1 - margin;
                for (int iz = 0; iz <= n; iz++)
                    for (int ix = 0; ix <= n; ix++)
                    {
                        float x = center.x - half + ix * step, z = center.y - half + iz * step;
                        float h = Inside(x, z, -1f) ? Height(x, z) - 0.4f : Natural(x, z);
                        index[ix, iz] = b.Vertex(new Vector3(x, h, z), new Vector2(x / 12f, z / 12f));
                    }
                for (int iz = 0; iz < n; iz++)
                    for (int ix = 0; ix < n; ix++)
                    {
                        float cx = center.x - half + (ix + 0.5f) * step, cz = center.y - half + (iz + 0.5f) * step;
                        if (Inside(cx, cz, step * 1.5f)) continue;
                        b.QuadUp(0, index[ix, iz], index[ix + 1, iz], index[ix + 1, iz + 1], index[ix, iz + 1]);
                    }
                return b.Build(recalcNormals: true);
            }
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
                    Lod(Instance(adIndex++ % 2 == 0 ? "AdBoard_A" : "AdBoard_B", props, pos, Quaternion.LookRotation(-rr)), 0.01f);
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
                            Lod(Instance((stack++ / 4) % 2 == 0 ? "TireStack_Red" : "TireStack_White", props, pos, Quaternion.LookRotation(tangent)), 0.015f);
                        }
                    }
                }
            }
        }

        // ---------- лес и камни ----------

        static void ScatterNature(TrackPath path, TrackMeshBuilder track, Relief relief, GameObject root, List<(Vector3 c, float r)> exclusions, System.Random random)
        {
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
            for (int attempt = 0; attempt < 40000; attempt++)
            {
                var p = new Vector3((float)(random.NextDouble() * 1500.0 - 690.0), 0f, (float)(random.NextDouble() * 1150.0 - 960.0));
                float progress = path.Nearest(p);
                Vector3 axis = path.PointAt(progress);
                float distance = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(axis.x, 0f, axis.z));
                int nearest = Mathf.RoundToInt(progress * path.Count) % path.Count;
                int side = Vector3.Dot(p - axis, path.SideAt(progress)) > 0f ? 1 : -1;
                float clearance = track.RailOffset(nearest, side) + 3.5f;
                if (distance < clearance) continue;
                if (exclusions.Any(e => (new Vector2(e.c.x - p.x, e.c.z - p.z)).sqrMagnitude < e.r * e.r)) continue;

                // Лесные массивы по низкочастотному шуму, у самой трассы реже.
                float forest = Mathf.PerlinNoise(p.x / 90f + 13.1f, p.z / 90f + 7.7f);
                float near = Mathf.InverseLerp(clearance, clearance + 50f, distance);
                float density = Mathf.Lerp(0.3f, 1f, near) * (forest > 0.47f ? 1f : 0.22f);
                if (random.NextDouble() > density) continue;

                string model;
                float spacing;
                double roll = random.NextDouble();
                if (distance > clearance + 35f && forest > 0.5f)
                {
                    model = roll < 0.75 ? "Pine_" + "ABC"[random.Next(3)] : roll < 0.9 ? "Tree_" + "AB"[random.Next(2)] : "Rock_" + "ABC"[random.Next(3)];
                    spacing = 6f;
                }
                else
                {
                    model = roll < 0.35 ? "Tree_" + "AB"[random.Next(2)] : roll < 0.65 ? "Bush_" + "AB"[random.Next(2)] : roll < 0.82 ? "Pine_" + "ABC"[random.Next(3)] : "Rock_" + "ABC"[random.Next(3)];
                    spacing = model.StartsWith("Bush") ? 3f : 7f;
                }
                bool isTree = model.StartsWith("Pine") || model.StartsWith("Tree");
                bool isBush = model.StartsWith("Bush");
                bool isRock = model.StartsWith("Rock");
                if (isTree && trees >= 1900 || isBush && bushes >= 420 || isRock && rocks >= 240) continue;
                if (!Free(p, spacing)) continue;

                p.y = relief.Height(p) - 0.15f;
                float scale = 0.8f + (float)random.NextDouble() * 0.5f;
                var go = Instance(model, root, p, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                go.transform.localScale = Vector3.one * scale;
                // Дальние деревья и камни не рисуются, когда они меньше ~1% высоты экрана (лес в дымке остаётся силуэтом рельефа).
                Lod(go, isBush || isRock ? 0.012f : 0.006f);
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
                if (isTree) trees++;
                else if (isBush) bushes++;
                else rocks++;
            }
            Debug.Log($"[RandomCar] Холмы: деревьев {trees}, кустов {bushes}, камней {rocks}");
        }
    }
}
