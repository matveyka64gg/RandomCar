using RandomCar.Car;
using UnityEngine.Rendering;
using RandomCar.Maps;
using RandomCar.Race;
using RandomCar.Track;
using UnityEditor;
using UnityEngine;
using static RandomCar.EditorTools.BuilderKit;
using MeshBuilder = RandomCar.EditorTools.TrackMeshBuilder.MeshBuilder;

namespace RandomCar.EditorTools
{
    /// «Полигон испытаний» (Crash Test Arena): закрытый испытательный центр — кольцо-полигон 18 м шириной с длинной
    /// главной прямой (на ней — зона испытаний), широкая бетонная площадка снаружи прямой (оттуда выезжает автобус,
    /// выскакивает Верити), ангар краш-тестов, вышка, трибуна, краш-стена с мишенью и шинами, гигантский манекен,
    /// круглая скидпад-площадка, мачты прожекторов, контейнеры, бетонная ограда по периметру. Сцена Assets/Scenes/Map_Arena.unity.
    /// В выборе трасс карты нет (MapEntry.Hidden): сюда попадают только через пункт меню «Полигон испытаний».
    /// Вокруг зоны испытаний (главная прямая x −125…0) на 35 м по обе стороны — ни одного коллайдера: события, которым
    /// нужна обочина (автобус, Верити, пушка, рука, вентилятор), ставятся там так же, как на обычной трассе.
    public static class ArenaMapBuilder
    {
        public const string Key = "Arena";
        public const string ScenePath = "Assets/Scenes/Map_Arena.unity";
        public const string Title = "Полигон испытаний";
        public const float HalfWidth = 9f;
        const string Folder = "Arena";

        static readonly Vector2[] Corners = { new Vector2(-200f, 0f), new Vector2(200f, 0f), new Vector2(200f, 170f), new Vector2(-200f, 170f) };
        static readonly float[] Radii = { 55f, 55f, 55f, 55f };

        static readonly MapLook Look = new MapLook
        {
            Sun = "#FFF4E0", SunIntensity = 1.9f, SunEuler = new Vector3(48f, -30f, 0f),
            SkyTint = "#79B4F0", SkyGround = "#9FB08F", SkyExposure = 1.25f, Atmosphere = 0.8f,
            AmbientSky = "#A9CDF2", AmbientEquator = "#E6E2D2", AmbientGround = "#7E8F6A",
            Fog = "#CFE0EE", FogStart = 350f, FogEnd = 1600f,
            PostExposure = 0f, Contrast = 8f, Saturation = 12f, Temperature = 2f,
            BloomIntensity = 0.5f, BloomThreshold = 1.1f, Vignette = 0.14f,
        };

        public static TrackDefinition EnsureTrack()
        {
            var def = TrackAssets.Ensure(Key);
            var points = TrackShapes.RoundedPolygon(Corners, Radii, 6f);
            def.ControlPoints = points;
            def.SamplesPerSegment = 3;
            // Старт — на главной прямой после выхода из поворота (прямая x −145…145): машина и зона испытаний на прямой.
            def.StartSample = TrackShapes.Closest(points, new Vector2(-112f, 0f)) * 3;
            def.HalfWidth = HalfWidth;
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            return def;
        }

        public static void Build()
        {
            var definition = EnsureTrack();
            var scene = MapSceneKit.NewScene();
            var path = definition.Path;
            var track = new TrackMeshBuilder(path);
            var surfaces = PhysicsAssets.EnsureSurfaces();
            MapSceneKit.SetupLook(Key, Look);
            float length = path.Length;

            var trackRoot = new GameObject("Track");
            var props = Group("Props", null);

            // Земля: трава, бетон площадки снаружи прямой и у ангара, скидпад и полоса краш-теста в центре.
            var grass = Place(SaveMesh(Slab("ArenaGrass", -330f, 330f, -180f, 330f, -0.05f, 20f), Folder), "Grass", trackRoot.transform, "M_Grass");
            grass.AddComponent<MeshCollider>();
            grass.AddComponent<Surface>().Type = surfaces[SurfaceKind.Grass];
            var concrete = new MeshBuilder("ArenaConcrete", 1);
            AddSlab(concrete, -250f, 250f, -82f, -10.5f, -0.02f);
            AddSlab(concrete, -140f, -40f, -130f, -82f, -0.02f);
            AddDisc(concrete, new Vector3(110f, -0.02f, 85f), 36f, 48);
            AddSlab(concrete, -40f, 70f, 54f, 70f, -0.02f);
            var apron = Place(SaveMesh(concrete.Build(), Folder), "Concrete", trackRoot.transform, "M_ArenaConcrete");
            apron.AddComponent<MeshCollider>();
            apron.AddComponent<Surface>().Type = surfaces[SurfaceKind.Asphalt];

            RoadMeshes.AddRoad(path, trackRoot, "ArenaRoad", "M_Asphalt", surfaces[SurfaceKind.Asphalt], d => true, 8f, Folder, HalfWidth);
            Place(SaveMesh(RoadMeshes.Markings(path, "ArenaMarkings", d => d > 12f && d < length - 50f, HalfWidth), Folder), "Markings", trackRoot.transform, "M_Line");
            var startLine = track.StartLine();
            startLine.name = "ArenaStartLine";
            Place(SaveMesh(startLine, Folder), "StartLine", trackRoot.transform, "M_Decal_White", "M_Decal_Black");
            var kerbMesh = track.Kerbs();
            kerbMesh.name = "ArenaKerbs";
            var kerbs = Place(SaveMesh(kerbMesh, Folder), "Kerbs", trackRoot.transform, "M_KerbRed", "M_KerbWhite");
            kerbs.AddComponent<MeshCollider>();
            kerbs.AddComponent<Surface>().Type = surfaces[SurfaceKind.Kerb];
            Place(SaveMesh(Paint(), Folder), "Paint", trackRoot.transform, "M_SignYellow", "M_Line", "M_Decal_Black");
            Place(SaveMesh(Joints(), Folder), "Joints", trackRoot.transform, "M_ConcreteDark");

            MapSceneKit.AddSkidMarks();

            // Ограда по периметру: сегменты 12 м с коллайдерами (машина, вылетевшая с полигона, не уезжает в пустоту).
            var walls = Group("PerimeterWall", trackRoot.transform);
            WallLine(walls, new Vector3(-288f, 0f, -168f), new Vector3(288f, 0f, -168f), Vector3.forward);
            WallLine(walls, new Vector3(288f, 0f, 318f), new Vector3(-288f, 0f, 318f), Vector3.back);
            WallLine(walls, new Vector3(-300f, 0f, 306f), new Vector3(-300f, 0f, -156f), Vector3.right);
            WallLine(walls, new Vector3(300f, 0f, -156f), new Vector3(300f, 0f, 306f), Vector3.left);

            // Ангар краш-тестов (пол вровень с площадкой), вышка, трибуна, контейнеры.
            var hangar = Instance("TestHangar", props, new Vector3(-90f, -0.28f, -104f), Quaternion.identity, Folder);
            foreach (var (c, s) in new[]
            {
                (new Vector3(-23.75f, 4f, 0f), new Vector3(0.5f, 8f, 36f)), (new Vector3(23.75f, 4f, 0f), new Vector3(0.5f, 8f, 36f)),
                (new Vector3(0f, 4f, -17.75f), new Vector3(48f, 8f, 0.5f)), (new Vector3(-21f, 5.5f, 17.75f), new Vector3(6f, 11f, 0.5f)),
                (new Vector3(21f, 5.5f, 17.75f), new Vector3(6f, 11f, 0.5f)), (new Vector3(0f, 13.5f, 17.75f), new Vector3(48f, 5f, 0.5f)),
                (new Vector3(0f, 0.15f, 0f), new Vector3(49f, 0.3f, 37f)), (new Vector3(0f, 13f, 0f), new Vector3(48f, 6f, 36f)),
            })
            {
                var box = hangar.AddComponent<BoxCollider>();
                box.center = c;
                box.size = s;
            }
            Solid(Instance("ControlTower", props, new Vector3(-10f, 0f, -72f), Quaternion.identity));
            Solid(Instance("Grandstand", props, new Vector3(80f, 0f, -70f), Quaternion.identity));
            Solid(Instance("PitBuilding", props, new Vector3(170f, 0f, -70f), Quaternion.identity));
            foreach (var (name, pos, yaw, y) in new[]
            {
                ("Container_Orange", new Vector3(-150f, 0f, -96f), 90f, 0f), ("Container_Blue", new Vector3(-150f, 0f, -104f), 90f, 0f),
                ("Container_Orange", new Vector3(-150f, 2.6f, -100f), 90f, 2.6f), ("Container_Blue", new Vector3(-36f, 0f, -118f), 0f, 0f),
                ("Container_Orange", new Vector3(-32f, 0f, -118f), 0f, 0f), ("Container_Blue", new Vector3(230f, 0f, 250f), 30f, 0f),
            })
                Solid(Instance(name, props, pos, Quaternion.Euler(0f, yaw, 0f), Folder));

            // Центр: краш-стена в конце полосы с шинами, гигантский манекен, скидпад с конусами.
            Solid(Instance("CrashWall", props, new Vector3(72f, 0f, 62f), Quaternion.LookRotation(Vector3.left), Folder));
            Solid(Instance("CrashDummy", props, new Vector3(-118f, 0f, 62f), Quaternion.LookRotation(Vector3.back), Folder));
            for (int k = 0; k < 14; k++)
            {
                float a = k / 14f * Mathf.PI * 2f;
                Instance("Cone", props, new Vector3(110f + Mathf.Cos(a) * 24f, 0f, 85f + Mathf.Sin(a) * 24f), Quaternion.identity);
            }
            for (int k = 0; k < 6; k++)
                Instance("Cone", props, new Vector3(-20f + k * 14f, 0f, 58f), Quaternion.identity);

            // Таблички «TEST ZONE» у начала и конца зоны испытаний — без коллайдеров (обочина зоны свободна для событий).
            foreach (var x in new[] { -98f, -10f })
                foreach (var z in new[] { -15f, 15f })
                    Instance("ZoneSign", props, new Vector3(x, 0f, z), Quaternion.LookRotation(Vector3.left), Folder);

            // Флаги у старта зоны, рекламные щиты вдоль дальней прямой, шины на внешней стороне поворотов.
            for (int k = 0; k < 4; k++)
                Instance(k % 2 == 0 ? "FlagPole_Orange" : "FlagPole_Blue", props, new Vector3(-142f + k * 9f, 0f, -16f), Quaternion.LookRotation(Vector3.forward));
            for (int k = 0; k < 6; k++)
                Instance(k % 2 == 0 ? "AdBoard_A" : "AdBoard_B", props, new Vector3(-120f + k * 48f, 0f, 188f), Quaternion.LookRotation(Vector3.back));
            foreach (var corner in new[] { new Vector2(-200f, 0f), new Vector2(200f, 0f), new Vector2(200f, 170f), new Vector2(-200f, 170f) })
            {
                Vector2 c = corner + new Vector2(Mathf.Sign(corner.x) * 30f, corner.y > 85f ? 30f : -30f);
                for (int k = -2; k <= 2; k++)
                {
                    Vector2 dir = new Vector2(-Mathf.Sign(corner.x), corner.y > 85f ? 1f : -1f).normalized;
                    Vector2 along = new Vector2(dir.y, -dir.x);
                    Vector2 p = c + along * k * 2.4f;
                    Solid(Instance(k % 2 == 0 ? "TireStack_Red" : "TireStack_White", props, new Vector3(p.x, 0f, p.y), Quaternion.identity));
                }
            }

            // Мачты прожекторов вокруг полигона — смотрят в центр.
            Vector3 center = new Vector3(0f, 0f, 85f);
            foreach (var p in new[]
            {
                new Vector3(-235f, 0f, -48f), new Vector3(-30f, 0f, -50f), new Vector3(150f, 0f, -48f), new Vector3(262f, 0f, 85f),
                new Vector3(150f, 0f, 220f), new Vector3(-30f, 0f, 222f), new Vector3(-235f, 0f, 220f), new Vector3(-268f, 0f, 85f),
            })
            {
                Vector3 to = center - p;
                to.y = 0f;
                var mast = Instance("Floodlight", props, p, Quaternion.LookRotation(to.normalized), Folder);
                var pole = mast.AddComponent<CapsuleCollider>();
                pole.radius = 0.6f;
                pole.height = 23f;
                pole.center = new Vector3(0f, 11.5f, 0f);
            }

            // За оградой: лес по кругу, горы на горизонте, облака (модели «Холмов»; без коллайдеров — туда не доехать).
            var scenery = Group("Scenery", null);
            var random = new System.Random(71);
            Instance("Mountains", scenery, new Vector3(0f, -2f, 75f), Quaternion.Euler(0f, 37f, 0f));
            for (int i = 0; i < 150; i++)
            {
                float a = (float)(random.NextDouble() * Mathf.PI * 2f);
                // Прямоугольный пояс 25–90 м за оградой (ограда x ±300, z −168…318).
                float out1 = 25f + (float)random.NextDouble() * 65f;
                Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float sx = Mathf.Abs(p.x) > 1e-3f ? (300f + out1) / Mathf.Abs(p.x) : float.MaxValue;
                float sz = Mathf.Abs(p.z) > 1e-3f ? ((p.z > 0f ? 318f : 168f) + out1) / Mathf.Abs(p.z) : float.MaxValue;
                p *= Mathf.Min(sx, sz);
                p.z += p.z > 0f ? 0f : 0f;
                double roll = random.NextDouble();
                string model = roll < 0.6 ? "Pine_" + "ABC"[random.Next(3)] : roll < 0.85 ? "Tree_" + "AB"[random.Next(2)] : "Bush_" + "AB"[random.Next(2)];
                var tree = Instance(model, scenery, p, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                tree.transform.localScale = Vector3.one * (0.9f + (float)random.NextDouble() * 0.6f);
            }
            for (int i = 0; i < 14; i++)
            {
                float a = (float)(random.NextDouble() * Mathf.PI * 2f);
                float dist = 450f + (float)random.NextDouble() * 600f;
                var cloud = Instance("Cloud_" + "ABC"[i % 3], scenery,
                    new Vector3(Mathf.Cos(a) * dist, 150f + (float)random.NextDouble() * 110f, 75f + Mathf.Sin(a) * dist),
                    Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                cloud.transform.localScale = Vector3.one * (1.4f + (float)random.NextDouble() * 1.4f);
                cloud.GetComponentInChildren<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            var mapRoot = new GameObject("Map");
            var raceTrack = mapRoot.AddComponent<RaceTrack>();
            raceTrack.Configure(definition);
            var arena = mapRoot.AddComponent<MapDefinition>();
            arena.Configure(Key, Title, raceTrack, null, new Vector3(-100f, 0f, -20f), 20f, new CameraShot[0]);
            arena.SetRoadside(30f);
            arena.SetLaps(1);

            SetStatic(trackRoot);
            SetStatic(props);
            SetStatic(scenery);
            Debug.Log($"[RandomCar] Полигон испытаний: кольцо {length:F0} м, ширина {HalfWidth * 2f:F0} м");
            MapSceneKit.Finish(scene, ScenePath, Key, new Vector3(-60f, 12f, 20f), new Vector3(700f, 200f, 560f));
        }

        // ---------- меши ----------

        static Mesh Slab(string name, float x0, float x1, float z0, float z1, float y, float step)
        {
            var b = new MeshBuilder(name, 1);
            int nx = Mathf.CeilToInt((x1 - x0) / step), nz = Mathf.CeilToInt((z1 - z0) / step);
            var index = new int[nx + 1, nz + 1];
            for (int iz = 0; iz <= nz; iz++)
                for (int ix = 0; ix <= nx; ix++)
                {
                    float x = Mathf.Lerp(x0, x1, ix / (float)nx), z = Mathf.Lerp(z0, z1, iz / (float)nz);
                    index[ix, iz] = b.Vertex(new Vector3(x, y, z), new Vector2(x / 12f, z / 12f));
                }
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                    b.QuadUp(0, index[ix, iz], index[ix + 1, iz], index[ix + 1, iz + 1], index[ix, iz + 1]);
            return b.Build(recalcNormals: true);
        }

        static void AddSlab(MeshBuilder b, float x0, float x1, float z0, float z1, float y, int sub = 0) =>
            b.QuadUpPoints(sub, new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), 8f);

        static void AddDisc(MeshBuilder b, Vector3 c, float radius, int segments, int sub = 0) => AddRing(b, c, 0f, radius, segments, sub);

        static void AddRing(MeshBuilder b, Vector3 c, float r0, float r1, int segments, int sub = 0)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                b.QuadUpPoints(sub, c + d0 * r0, c + d1 * r0, c + d1 * r1, c + d0 * r1, 8f);
            }
        }

        static void AddFrame(MeshBuilder b, float x0, float x1, float z0, float z1, float y, float w, int sub)
        {
            AddSlab(b, x0, x1, z0, z0 + w, y, sub);
            AddSlab(b, x0, x1, z1 - w, z1, y, sub);
            AddSlab(b, x0, x0 + w, z0, z1, y, sub);
            AddSlab(b, x1 - w, x1, z0, z1, y, sub);
        }

        /// Разметка на бетоне: мишени краш-теста, рамки испытательных боксов, кольца скидпада, края полосы краш-теста.
        static Mesh Paint()
        {
            var b = new MeshBuilder("ArenaPaint", 3);
            const float y = -0.012f;
            foreach (var c in new[] { new Vector3(-70f, y, -45f), new Vector3(30f, y, -45f), new Vector3(140f, y, -40f) })
            {
                AddRing(b, c, 6.2f, 7.4f, 40, 0);
                AddRing(b, c, 4.2f, 5.0f, 36, 2);
                AddRing(b, c, 2.0f, 2.8f, 28, 0);
                AddDisc(b, c, 0.9f, 20, 2);
            }
            for (int k = 0; k < 4; k++) AddFrame(b, -230f + k * 18f, -216f + k * 18f, -70f, -48f, y, 0.35f, 1);
            AddRing(b, new Vector3(110f, y, 85f), 30f, 30.6f, 64, 0);
            AddRing(b, new Vector3(110f, y, 85f), 18f, 18.4f, 48, 1);
            AddSlab(b, -40f, 70f, 54f, 54.4f, y, 0);
            AddSlab(b, -40f, 70f, 69.6f, 70f, y, 0);
            for (int k = 0; k < 6; k++) AddSlab(b, 52f + k * 3f, 53.5f + k * 3f, 55f, 69f, y, k % 2 == 0 ? 0 : 2);
            return b.Build();
        }

        /// Деформационные швы бетона: тёмные линии сеткой 10 м по площадке у прямой и у ангара.
        static Mesh Joints()
        {
            var b = new MeshBuilder("ArenaJoints", 1);
            const float y = -0.015f, w = 0.12f;
            for (float x = -250f; x <= 250f; x += 10f) AddSlab(b, x - w / 2, x + w / 2, -82f, -10.5f, y);
            for (float z = -80f; z <= -12f; z += 10f) AddSlab(b, -250f, 250f, z - w / 2, z + w / 2, y);
            for (float x = -140f; x <= -40f; x += 10f) AddSlab(b, x - w / 2, x + w / 2, -130f, -82f, y);
            for (float z = -130f; z < -82f; z += 10f) AddSlab(b, -140f, -40f, z - w / 2, z + w / 2, y);
            return b.Build();
        }

        static void WallLine(GameObject parent, Vector3 from, Vector3 to, Vector3 inward)
        {
            float length = Vector3.Distance(from, to);
            int count = Mathf.Max(1, Mathf.RoundToInt(length / 12f));
            Vector3 dir = (to - from).normalized;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = from + dir * (12f * (i + 0.5f));
                var seg = Instance("ArenaWall", parent, p, Quaternion.LookRotation(inward), Folder);
                var box = seg.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 1.7f, 0f);
                box.size = new Vector3(12f, 3.4f, 0.6f);
            }
        }

        /// Коллайдер по габаритам модели (ящик в осях объекта).
        static GameObject Solid(GameObject go)
        {
            bool any = false;
            var bounds = new Bounds();
            var inverse = go.transform.worldToLocalMatrix;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var m = inverse * filter.transform.localToWorldMatrix;
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            if (!any) return go;
            var box = go.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            return go;
        }
    }
}
