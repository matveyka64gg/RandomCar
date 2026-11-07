using System.Collections.Generic;
using System.Linq;
using RandomCar.Car;
using RandomCar.Maps;
using RandomCar.Race;
using RandomCar.Track;
using UnityEditor;
using UnityEngine;
using static RandomCar.EditorTools.BuilderKit;
using MeshBuilder = RandomCar.EditorTools.TrackMeshBuilder.MeshBuilder;

namespace RandomCar.EditorTools
{
    /// Карта «Пустыня»: полдень, длинная стартовая прямая, перепады высот, гребень, каньон со скальными стенами,
    /// грунтовый участок и песчаные зоны вылета. Сцена Assets/Scenes/Map_Desert.unity.
    public static class DesertMapBuilder
    {
        public const string Key = "Desert";
        public const string ScenePath = "Assets/Scenes/Map_Desert.unity";
        const int Seed = 27;
        const float TerrainHalf = 1500f;
        const float TerrainStep = 8f;
        const float WallLateral = 40f;

        static readonly Vector2[] Corners =
        {
            new Vector2(250f, 0f), new Vector2(330f, -120f), new Vector2(220f, -260f), new Vector2(40f, -230f),
            new Vector2(-60f, -320f), new Vector2(-220f, -300f), new Vector2(-330f, -150f), new Vector2(-250f, 0f),
        };
        static readonly float[] Radii = { 60f, 70f, 45f, 90f, 35f, 40f, 80f, 50f };
        static readonly float[] Heights = { 0f, 4f, 7f, 3f, -1f, -4f, -1f, 0f };

        static readonly MapLook Look = new MapLook
        {
            Sun = "#FFF1D6", SunIntensity = 2.0f, SunEuler = new Vector3(52f, -35f, 0f),
            SkyTint = "#7FB2E8", SkyGround = "#B49A7A", SkyExposure = 1.3f, Atmosphere = 0.75f,
            AmbientSky = "#9CC6EE", AmbientEquator = "#F1D8B4", AmbientGround = "#9C7A58",
            Fog = "#EED7B5", FogStart = 300f, FogEnd = 1700f,
            PostExposure = 0f, Contrast = 9f, Saturation = 10f, Temperature = 9f,
            BloomIntensity = 0.5f, BloomThreshold = 1.1f, Vignette = 0.16f,
        };

        public static TrackDefinition EnsureTrack()
        {
            var def = TrackAssets.Ensure(Key);
            var points = TrackShapes.RoundedPolygon(Corners, Radii, 6f, Heights);
            def.ControlPoints = points;
            def.SamplesPerSegment = 3;
            def.StartSample = TrackShapes.Closest(points, new Vector2(-150f, 0f)) * 3;
            def.HalfWidth = 6f;
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
            var track = new TrackMeshBuilder(path);
            var surfaces = PhysicsAssets.EnsureSurfaces();
            MapSceneKit.SetupLook(Key, Look);
            float length = path.Length;

            float Dist(Vector2 corner) => path.Nearest(new Vector3(corner.x, 0f, corner.y)) * length;
            // Каньон — от угла 4 до угла 5 и чуть дальше; грунтовка — от угла 5 до угла 6.
            float canyonFrom = Dist(Corners[4]) - 40f, canyonTo = Dist(Corners[5]) + 30f;
            float dirtFrom = Dist(Corners[5]) + 40f, dirtTo = Dist(Corners[6]) + 20f;
            bool Canyon(float d) => RoadMeshes.InRange(d, canyonFrom, canyonTo, length);
            bool Dirt(float d) => RoadMeshes.InRange(d, dirtFrom, dirtTo, length);

            var trackRoot = new GameObject("Track");
            var props = Group("Props", null);
            var scenery = Group("Scenery", null);

            // Рельеф: барханы, у дороги — ровная обочина на высоте полотна, в каньоне — подъём к стенам.
            var terrain = Place(SaveMesh(Terrain(path, Canyon), "Desert"), "Terrain", trackRoot.transform, "M_Sand");
            terrain.AddComponent<MeshCollider>();
            terrain.AddComponent<Surface>().Type = surfaces[SurfaceKind.Sand];

            RoadMeshes.AddRoad(path, trackRoot, "DesertRoadAsphalt", "M_Asphalt", surfaces[SurfaceKind.Asphalt], d => !Dirt(d), 8f, "Desert");
            RoadMeshes.AddRoad(path, trackRoot, "DesertRoadDirt", "M_Gravel", surfaces[SurfaceKind.Dirt], Dirt, 6f, "Desert");
            Place(SaveMesh(RoadMeshes.Markings(path, "DesertMarkings", d => !Dirt(d) && d > 12f && d < length - 50f), "Desert"), "Markings", trackRoot.transform, "M_Line");
            var startLine = track.StartLine();
            startLine.name = "DesertStartLine";
            Place(SaveMesh(startLine, "Desert"), "StartLine", trackRoot.transform, "M_Decal_White", "M_Decal_Black");
            var kerbMesh = track.Kerbs();
            kerbMesh.name = "DesertKerbs";
            var kerbs = Place(SaveMesh(kerbMesh, "Desert"), "Kerbs", trackRoot.transform, "M_KerbRed", "M_KerbWhite");
            kerbs.AddComponent<MeshCollider>();
            kerbs.AddComponent<Surface>().Type = surfaces[SurfaceKind.Kerb];

            // Стены: в каньоне вплотную за скалами, в остальном — далеко в песке, чтобы не заблудиться.
            var walls = new GameObject("DesertWalls");
            walls.transform.SetParent(trackRoot.transform, false);
            walls.AddComponent<MeshCollider>().sharedMesh = SaveMesh(RoadMeshes.Walls(path, "DesertWalls", WallLateral, 8f, d => !Canyon(d)), "Desert");
            var canyonWalls = new GameObject("CanyonWalls");
            canyonWalls.transform.SetParent(trackRoot.transform, false);
            canyonWalls.AddComponent<MeshCollider>().sharedMesh = SaveMesh(RoadMeshes.Walls(path, "CanyonWallsCollider", 14.5f, 20f, Canyon), "Desert");

            MapSceneKit.AddSkidMarks();

            Vector3 p0 = path.Point(0), t0 = path.TangentAt(0);
            var gantry = Instance("StartGantry", props, p0, Quaternion.LookRotation(t0));
            var lamps = Enumerable.Range(1, 5).Select(k => Find(gantry.transform, "StartLamp_" + k).GetComponent<Renderer>()).ToArray();
            var lights = gantry.AddComponent<StartLights>();
            lights.Configure(lamps);
            lights.SetLit(0);

            // Ориентиры у старта.
            {
                Vector3 c = RoadMeshes.At(path, 60f, -34f);
                c.y = Height(path, c, Canyon);
                Instance("Diner", props, c, Quaternion.LookRotation(path.SideAt(60f / length)), "Desert");
                Vector3 w = RoadMeshes.At(path, 110f, -52f);
                w.y = Height(path, w, Canyon);
                Instance("WaterTower", props, w, Quaternion.identity, "Desert");
                for (int k = -3; k <= 3; k++)
                {
                    Vector3 f = RoadMeshes.At(path, 20f + k * 14f, 13f);
                    f.y = Height(path, f, Canyon);
                    Instance(k % 2 == 0 ? "FlagPole_Orange" : "FlagPole_Blue", props, f, Quaternion.LookRotation(-path.SideAt(Mathf.Repeat(20f + k * 14f, length) / length)));
                }
            }

            PlaceCanyon(path, props, random, canyonFrom, canyonTo);
            PlaceTrackside(path, props, random, Canyon, Dirt, canyonFrom, dirtFrom);
            PlaceScenery(path, scenery, random, Canyon);

            var mapRoot = new GameObject("Map");
            var raceTrack = mapRoot.AddComponent<RaceTrack>();
            raceTrack.Configure(definition);
            float gridMid = length - (MapDefinition.GridDistance(0) + MapDefinition.GridDistance(3)) / 2f;
            Vector3 gridCenter = path.PointAt(gridMid / length);
            Vector3 gridDir = path.DirectionAt(gridMid / length);
            float yaw = Mathf.Atan2(-gridDir.z, -gridDir.x) * Mathf.Rad2Deg + 40f;
            var desertMap = mapRoot.AddComponent<MapDefinition>();
            desertMap.Configure(Key, "Пустыня", raceTrack, lights, gridCenter, yaw, IntroShots(path));
            desertMap.SetRoadside(22f);

            SetStatic(trackRoot);
            SetStatic(props);
            SetStatic(scenery);
            Debug.Log($"[RandomCar] Пустыня: трасса {length:F0} м, каньон {canyonTo - canyonFrom:F0} м, грунтовка {dirtTo - dirtFrom:F0} м");
            MapSceneKit.Finish(scene, ScenePath, Key, p0 + Vector3.up * 8f, new Vector3(2000f, 600f, 2000f));
        }

        /// Высота рельефа в точке: у дороги — чуть ниже полотна, дальше — барханы, в каньоне — подъём к скалам.
        static float Height(TrackPath path, Vector3 point, System.Func<float, bool> canyon)
        {
            float p = path.Nearest(point);
            Vector3 axis = path.PointAt(p);
            float d = Vector2.Distance(new Vector2(point.x, point.z), new Vector2(axis.x, axis.z));
            float natural = -1.5f + 4.5f * Mathf.PerlinNoise(point.x / 70f + 3.1f, point.z / 70f + 7.3f)
                            + 1.6f * Mathf.PerlinNoise(point.x / 21f + 11f, point.z / 21f + 5f)
                            + 0.06f * Mathf.Max(0f, Vector2.Distance(Vector2.zero, new Vector2(point.x, point.z)) - 420f);
            float near = axis.y - 0.07f;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(9f, 48f, d));
            float h = Mathf.Lerp(near, natural, t);
            // Каньон: скальные откосы у дороги, дальше 70 м снова пустыня (без бесконечного плато).
            if (canyon(p * path.Length))
                h += 20f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(13f, 26f, d)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(70f, 130f, d)));
            return h;
        }

        static Mesh Terrain(TrackPath path, System.Func<float, bool> canyon)
        {
            var b = new MeshBuilder("DesertTerrain", 1);
            int n = Mathf.RoundToInt(TerrainHalf * 2f / TerrainStep);
            var index = new int[n + 1, n + 1];
            for (int iz = 0; iz <= n; iz++)
                for (int ix = 0; ix <= n; ix++)
                {
                    float x = -TerrainHalf + ix * TerrainStep, z = -TerrainHalf + iz * TerrainStep - 150f;
                    var p = new Vector3(x, 0f, z);
                    p.y = Height(path, p, canyon);
                    index[ix, iz] = b.Vertex(p, new Vector2(x / 12f, z / 12f));
                }
            for (int iz = 0; iz < n; iz++)
                for (int ix = 0; ix < n; ix++)
                    b.QuadUp(0, index[ix, iz], index[ix + 1, iz], index[ix + 1, iz + 1], index[ix, iz + 1]);
            return b.Build(recalcNormals: true);
        }

        static void PlaceCanyon(TrackPath path, GameObject root, System.Random random, float from, float to)
        {
            float length = path.Length;
            float span = Mathf.Repeat(to - from, length);
            foreach (int side in new[] { -1, 1 })
                for (float s = 0f; s < span; s += 15f)
                {
                    float d = from + s;
                    float p = TrackPath.Wrap(d / length);
                    Vector3 right = path.SideAt(p) * side;
                    Vector3 pos = path.PointAt(p) + right * (15f + (float)random.NextDouble() * 1.5f);
                    pos.y -= 1.5f;
                    var wall = Instance(random.Next(2) == 0 ? "CanyonWall_A" : "CanyonWall_B", root, pos, Quaternion.LookRotation(-right), "Desert");
                    wall.transform.localScale = new Vector3(0.75f, 0.8f + (float)random.NextDouble() * 0.5f, 1f);
                }
            // Указатель на въезде в каньон.
            float pe = TrackPath.Wrap((from - 30f) / length);
            Vector3 sign = path.PointAt(pe) + path.SideAt(pe) * 9.5f;
            Instance("RoadSign_A", root, sign, Quaternion.LookRotation(-path.DirectionAt(pe)), "Desert");
        }

        static void PlaceTrackside(TrackPath path, GameObject root, System.Random random, System.Func<float, bool> canyon,
                                   System.Func<float, bool> dirt, float canyonFrom, float dirtFrom)
        {
            float length = path.Length;
            float pd = TrackPath.Wrap((dirtFrom - 25f) / length);
            Instance("RoadSign_B", root, path.PointAt(pd) + path.SideAt(pd) * -9.5f, Quaternion.LookRotation(-path.DirectionAt(pd)), "Desert");

            // Шинные стенки снаружи быстрых поворотов, кактусы и камни в зоне вылета.
            for (int i = 0; i < path.Count; i += 4)
            {
                float d = path.DistanceAt(i);
                if (canyon(d) || d < 60f || d > length - 60f) continue;
                float k = path.CurvatureAt(i, 4);
                float pp = d / length;
                if (Mathf.Abs(k) > 1f / 110f && i % 8 == 0)
                {
                    int outside = k > 0f ? 1 : -1;
                    Vector3 pos = path.Point(i) + path.RightAt(i) * outside * 17f;
                    var barrier = Instance("TireBarrier", root, pos, Quaternion.LookRotation(-path.RightAt(i) * outside), "Desert");
                    var col = barrier.AddComponent<BoxCollider>();
                    col.center = new Vector3(0f, 0.3f, 0f);
                    col.size = new Vector3(4f, 0.6f, 0.8f);
                }
                if (random.NextDouble() < 0.18)
                {
                    int side = random.Next(2) == 0 ? -1 : 1;
                    float lat = 18f + (float)random.NextDouble() * 18f;
                    Vector3 pos = path.Point(i) + path.RightAt(i) * side * lat;
                    pos.y = path.Point(i).y;
                    string model = random.NextDouble() < 0.55 ? (random.Next(2) == 0 ? "Saguaro_A" : "Saguaro_B") : random.NextDouble() < 0.5 ? "Boulder_A" : "DeadTree";
                    var go = Instance(model, root, pos, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), "Desert");
                    if (model.StartsWith("Saguaro")) { var c = go.AddComponent<CapsuleCollider>(); c.radius = 0.35f; c.height = 6f; c.center = Vector3.up * 3f; }
                    else if (model.StartsWith("Boulder")) { var c = go.AddComponent<SphereCollider>(); c.radius = 1.3f; c.center = Vector3.up * 0.6f; }
                }
            }
        }

        static void PlaceScenery(TrackPath path, GameObject root, System.Random random, System.Func<float, bool> canyon)
        {
            // Столовые горы и шпили на горизонте, валуны и кактусы между ними.
            for (int i = 0; i < 26; i++)
            {
                float a = (float)(random.NextDouble() * Mathf.PI * 2f);
                float r = 560f + (float)random.NextDouble() * 420f;
                var pos = new Vector3(Mathf.Cos(a) * r, -2f, Mathf.Sin(a) * r - 150f);
                var go = Instance(random.Next(2) == 0 ? "Mesa_A" : "Mesa_B", root, pos, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), "Desert");
                go.transform.localScale = new Vector3(1.6f + (float)random.NextDouble() * 1.6f, 1.2f + (float)random.NextDouble() * 1.4f, 1.6f + (float)random.NextDouble() * 1.6f);
            }
            int placed = 0;
            for (int attempt = 0; attempt < 3000 && placed < 260; attempt++)
            {
                var pos = new Vector3((float)(random.NextDouble() * 1100.0 - 550.0), 0f, (float)(random.NextDouble() * 900.0 - 600.0));
                float p = path.Nearest(pos);
                Vector3 axis = path.PointAt(p);
                float dist = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(axis.x, axis.z));
                if (dist < 46f) continue;
                pos.y = Height(path, pos, canyon) - 0.2f;
                double roll = random.NextDouble();
                string model = roll < 0.1 ? (random.Next(2) == 0 ? "Spire_A" : "Spire_B") : roll < 0.45 ? (random.Next(2) == 0 ? "Boulder_B" : "Boulder_C")
                    : roll < 0.85 ? (random.Next(2) == 0 ? "Saguaro_A" : "Saguaro_B") : "DeadTree";
                var go = Instance(model, root, pos, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), "Desert");
                go.transform.localScale = Vector3.one * (0.8f + (float)random.NextDouble() * 0.6f);
                placed++;
            }
        }

        static CameraShot[] IntroShots(TrackPath path)
        {
            Vector3 start = path.PointAt(0f);
            Vector3 dir = path.DirectionAt(0f);
            Vector3 right = path.SideAt(0f);
            return new[]
            {
                new CameraShot { From = new Vector3(-420f, 120f, 160f), To = new Vector3(-300f, 90f, 220f), LookFrom = new Vector3(0f, 0f, -150f), LookTo = new Vector3(-80f, 0f, -60f), Seconds = 3.2f, Fov = 50f },
                new CameraShot { From = start + dir * 170f + right * 12f + Vector3.up * 5f, To = start + dir * 45f + right * 9f + Vector3.up * 4f, LookFrom = start + dir * 70f, LookTo = start, Seconds = 2.8f, Fov = 48f },
            };
        }
    }
}
