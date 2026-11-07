using System.Collections.Generic;
using System.IO;
using System.Linq;
using RandomCar.Car;
using RandomCar.Maps;
using RandomCar.Race;
using RandomCar.Track;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static RandomCar.EditorTools.BuilderKit;
using static RandomCar.EditorTools.TelAvivLayout;
using MeshBuilder = RandomCar.EditorTools.TrackMeshBuilder.MeshBuilder;

namespace RandomCar.EditorTools
{
    /// Карта «Тель-Авив» v2 (ночь), планировка — TelAvivLayout: приморская прямая с набережной и вантовым мостом,
    /// портовый проспект, скоростная эстакада вдоль небоскрёбов, тоннель под парком, бульвар с площадью,
    /// холмистый Яффо. Дорога 22 м (4 полосы). Город не стеной вдоль дороги: отступы, просветы, площади, парк у реки,
    /// ориентиры-небоскрёбы в зоне обзора эстакады и моста, огни горизонта. Над городом — «Ракетный дождь».
    /// Здания и пропы с LODGroup (дальние отбрасываются), без теней у горизонта. Сцена Assets/Scenes/Map_TelAviv.unity.
    public static class TelAvivMapBuilder
    {
        public const string Key = "TelAviv";
        public const string ScenePath = "Assets/Scenes/Map_TelAviv.unity";
        const string Folder = "TelAviv";
        const int Seed = 972;

        static readonly MapLook Look = new MapLook
        {
            Sun = "#9FB8FF", SunIntensity = 0.3f, SunEuler = new Vector3(38f, 120f, 0f),
            SkyTint = "#1C2E66", SkyGround = "#05070C", SkyExposure = 0.24f, Atmosphere = 0.9f,
            AmbientSky = "#33437E", AmbientEquator = "#3E3A60", AmbientGround = "#12121C",
            Fog = "#16203A", FogStart = 420f, FogEnd = 4200f,
            PostExposure = 0.6f, Contrast = 14f, Saturation = 20f, Temperature = -8f,
            BloomIntensity = 1.7f, BloomThreshold = 0.85f, Vignette = 0.22f,
        };

        /// Модель, ширина вдоль улицы, глубина, высота; Front — начало в середине фасада (здание уходит в −z),
        /// иначе — в центре основания.
        readonly struct Building
        {
            public readonly string Model;
            public readonly float W, D, H;
            public readonly bool Front;

            public Building(string model, float w, float d, float h, bool front = true)
            {
                Model = model;
                W = w;
                D = d;
                H = h;
                Front = front;
            }
        }

        static readonly Building[] Bauhaus =
        {
            new Building("TA_Bauhaus_A", 18f, 13f, 13f), new Building("TA_Bauhaus_B", 22f, 14f, 16f), new Building("TA_Bauhaus_C", 15f, 12f, 16f),
        };
        static readonly Building[] Sandstone =
        {
            new Building("TA_Sandstone_A", 12f, 10f, 7f), new Building("TA_Sandstone_B", 16f, 11f, 11f),
        };
        static readonly Building[] Hotels =
        {
            new Building("TA_Hotel_A", 36f, 16f, 52f), new Building("TA_Hotel_B", 28f, 14f, 38f),
        };
        static readonly Building[] Residential =
        {
            new Building("TA_Resi_A", 25f, 19f, 80f, false), new Building("TA_Resi_B", 21f, 21f, 96f, false),
        };
        static readonly Building[] Towers =
        {
            new Building("TA_Tower_A", 30f, 30f, 120f), new Building("TA_Tower_B", 34f, 34f, 150f),
            new Building("TA_Tower_C", 40f, 30f, 95f), new Building("TA_Tower_D", 24f, 24f, 70f),
        };

        public static TrackDefinition EnsureTrack()
        {
            var def = TrackAssets.Ensure(Key);
            var points = TrackShapes.RoundedPolygon(Corners, Radii, 6f);
            for (int i = 0; i < points.Length; i++) points[i].y = ProfileHeight(new Vector2(points[i].x, points[i].z));
            SmoothHeights(points, 6, 4);
            def.ControlPoints = points;
            def.SamplesPerSegment = 3;
            def.StartSample = TrackShapes.Closest(points, new Vector2(CoastX, -300f)) * 3;
            def.HalfWidth = RoadHalf;
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            return def;
        }

        public static void Build()
        {
            var definition = EnsureTrack();
            CreateMaterials();
            var scene = MapSceneKit.NewScene();
            var random = new System.Random(Seed);
            var path = definition.Path;
            var surfaces = PhysicsAssets.EnsureSurfaces();
            var moon = MapSceneKit.SetupLook(Key, Look);
            moon.shadowStrength = 0.6f;
            float length = path.Length;

            var trackRoot = new GameObject("Track");
            var structures = Group("Structures", null);
            var props = Group("Props", null);
            var buildings = Group("Buildings", null);
            var skyline = Group("Skyline", null);
            var nature = Group("Nature", null);
            var lightsRoot = Group("Lights", null);
            var occupancy = new HashSet<(int, int)>();

            // Земля с руслом реки, море, пляж и набережная.
            TelAvivStructures.GroundAndWater(trackRoot, surfaces);
            TelAvivStructures.Promenade(trackRoot, props, surfaces);
            var hill = Place(SaveMesh(TelAvivStructures.JaffaTerrain(path), Folder), "JaffaHill", trackRoot.transform, "M_CityGroundNight");
            hill.AddComponent<MeshCollider>();
            hill.AddComponent<Surface>().Type = surfaces[SurfaceKind.Asphalt];

            // Дорога 22 м: мокрый асфальт отражает огни; разметка на 4 полосы.
            RoadMeshes.AddRoad(path, trackRoot, "TA2_Road", "M_AsphaltWet", surfaces[SurfaceKind.Asphalt], d => true, 8f, Folder, RoadHalf);
            Place(SaveMesh(TelAvivStructures.Markings(path, GridDistances(), GridLaterals()), Folder), "Markings", trackRoot.transform, "M_Line");
            Place(SaveMesh(TelAvivStructures.StartLine(path), Folder), "StartLine", trackRoot.transform, "M_Decal_White", "M_Decal_Black");
            var sidewalks = Place(SaveMesh(TelAvivStructures.Sidewalks(path), Folder), "Sidewalks", trackRoot.transform, "M_Sidewalk", "M_Curb");
            sidewalks.AddComponent<MeshCollider>();
            sidewalks.AddComponent<Surface>().Type = surfaces[SurfaceKind.Asphalt];
            var walls = new GameObject("TA2_WallColliders");
            walls.transform.SetParent(trackRoot.transform, false);
            walls.AddComponent<MeshCollider>().sharedMesh = SaveMesh(TelAvivStructures.Walls(path), Folder);
            MapSceneKit.AddSkidMarks();

            // Эстакада и мост: парапеты, плита, опоры; вантовый мост над устьем реки.
            var piers = TelAvivStructures.ElevatedDeck(path, structures);
            foreach (var (pos, forward, h) in piers)
            {
                var pier = Instance("TA_HighwayPier", structures, pos, Quaternion.LookRotation(forward), Folder);
                pier.transform.localScale = new Vector3(1.05f, Mathf.Max(0.3f, (h - 1.4f) / 10f), 1f);
            }
            float pylonD = path.Nearest(new Vector3(CoastX, 0f, (RiverZ0 + RiverZ1) * 0.5f)) * length;
            Vector3 pylonBase = RoadMeshes.At(path, pylonD, 0f);
            pylonBase.y = RiverLevel - 0.4f;
            var pylonForward = path.DirectionAt(pylonD / length);
            Instance("TA_BridgePylon", structures, pylonBase, Quaternion.LookRotation(pylonForward), Folder);
            TelAvivStructures.BridgeCables(path, structures, pylonD, pylonBase + Vector3.up * 59.5f);
            Mark(occupancy, new Vector3(CoastX - 40f, 0f, RiverZ0 - 10f), Vector3.right, Vector3.forward, 140f, 80f);

            // Тоннель под парком: стены, потолок со световыми лентами, порталы, светильники; холм-парк сверху.
            var (enter, exit, tunnelLamps) = TelAvivStructures.Tunnel(path, structures);
            foreach (float d in new[] { enter, exit })
            {
                Vector3 p = RoadMeshes.At(path, d, 0f);
                Instance("TA_TunnelPortal", structures, new Vector3(p.x, 0f, p.z), Quaternion.LookRotation(path.DirectionAt(d / length)), Folder);
            }
            foreach (var lamp in tunnelLamps) AddLamp(lightsRoot, lamp, new Color(1f, 0.82f, 0.55f), 22f, 10f);
            Vector3 tunnelMid = RoadMeshes.At(path, (enter + exit) * 0.5f, 0f);
            float moundHalfZ = (exit - enter) * 0.5f + 24f;
            Place(SaveMesh(TelAvivStructures.ParkMound(new Vector3(tunnelMid.x, TunnelHeight + 0.4f, tunnelMid.z), 46f, moundHalfZ, 5f), Folder), "ParkMound", nature.transform, "M_Grass");
            Mark(occupancy, new Vector3(tunnelMid.x, 0f, tunnelMid.z - moundHalfZ), Vector3.forward, Vector3.right, 100f, moundHalfZ * 2f);
            for (int i = 0; i < 18; i++)
            {
                var p = new Vector3(tunnelMid.x + (float)(random.NextDouble() * 2 - 1) * 36f, TunnelHeight + 0.2f, tunnelMid.z + (float)(random.NextDouble() * 2 - 1) * (moundHalfZ - 20f));
                float lift = 5f * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.55f, 1f, Mathf.Max(Mathf.Abs((p.x - tunnelMid.x) / 46f), Mathf.Abs((p.z - tunnelMid.z) / moundHalfZ))));
                Ficus(nature, p + Vector3.up * lift, random, 1.2f);
            }

            // Старт: арка во всю ширину дороги.
            Vector3 p0 = path.Point(0), t0 = path.TangentAt(0);
            var gantry = Instance("StartGantry", props, p0, Quaternion.LookRotation(t0));
            gantry.transform.localScale = new Vector3(RoadHalf / 6f * 1.05f, 1.15f, 1f);
            var startLamps = Enumerable.Range(1, 5).Select(k => Find(gantry.transform, "StartLamp_" + k).GetComponent<Renderer>()).ToArray();
            var startLights = gantry.AddComponent<StartLights>();
            startLights.Configure(startLamps);
            startLights.SetLit(0);

            // Город по районам.
            int placed = 0;
            placed += PlaceSeafront(path, buildings, nature, occupancy, random);
            placed += PlacePort(path, buildings, props, nature, occupancy, random);
            placed += PlaceBusiness(path, buildings, occupancy, random);
            placed += PlaceBoulevard(path, buildings, props, nature, trackRoot, lightsRoot, occupancy, random);
            placed += PlaceJaffa(path, buildings, props, occupancy, random);
            placed += PlaceCityFill(path, buildings, occupancy, random);
            int far = PlaceSkyline(skyline, random);
            int carpet = PlaceCityCarpet(skyline, random);
            int lamps = PlaceStreetLights(path, props, nature, lightsRoot, random);

            // Небо: звёзды и облака (облака освещаются вспышками).
            Place(SaveMesh(StarDome(random), Folder), "Stars", props.transform, "M_Stars").GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            BuildClouds(props, random);

            // Ночное шоу над городом.
            BuildRocketShow();

            var mapRoot = new GameObject("Map");
            var raceTrack = mapRoot.AddComponent<RaceTrack>();
            raceTrack.Configure(definition);
            float gridMid = length - (MapDefinition.GridDistance(0) + MapDefinition.GridDistance(3)) / 2f;
            Vector3 gridCenter = path.PointAt(gridMid / length);
            Vector3 gridDir = path.DirectionAt(gridMid / length);
            float yaw = Mathf.Atan2(-gridDir.z, -gridDir.x) * Mathf.Rad2Deg + 40f;
            var map = mapRoot.AddComponent<MapDefinition>();
            map.Configure(Key, "Тель-Авив", raceTrack, startLights, gridCenter, yaw, IntroShots(path));
            map.SetRoadside(13f);
            map.SetEventSubstitutions("rocket=debris", "barrage=rocketbarrage");
            map.SetLaps(2);

            SetStatic(trackRoot);
            SetStatic(structures);
            SetStatic(buildings);
            SetStatic(skyline);
            SetStatic(nature);
            foreach (var t in props.GetComponentsInChildren<Transform>(true))
                if (t.GetComponentInParent<RocketShow>() == null) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
            Debug.Log($"[RandomCar] Тель-Авив v2: трасса {length:F0} м, зданий {placed}, горизонт {far}, дальний город {carpet} домов, опор {piers.Count}, фонарей {lamps}, светильников тоннеля {tunnelLamps.Count}");
            MapSceneKit.Finish(scene, ScenePath, Key, p0 + Vector3.up * 10f, new Vector3(1600f, 400f, 1600f));
        }

        static float[] GridDistances() => Enumerable.Range(0, MapDefinition.MaxGrid).Select(MapDefinition.GridDistance).ToArray();
        static float[] GridLaterals() => Enumerable.Range(0, MapDefinition.MaxGrid).Select(MapDefinition.GridLateral).ToArray();

        // ---------- районы ----------

        /// Ряд зданий вдоль трассы с отступом и просветами. filter(d, точка оси) — где ставить.
        static int PlaceRow(TrackPath path, GameObject root, HashSet<(int, int)> occupancy, System.Random random, int side,
                            System.Func<float, Vector3, bool> filter, float setback, System.Func<Building> pick, float gapChance, Vector2 gap, float lod)
        {
            int placed = 0;
            float length = path.Length;
            float d = 0f;
            while (d < length)
            {
                Vector3 axis = RoadMeshes.At(path, d, 0f);
                if (!filter(d, axis) || Mathf.Min(d, length - d) < 30f)
                {
                    d += 4f;
                    continue;
                }
                if (random.NextDouble() < gapChance)
                {
                    d += Mathf.Lerp(gap.x, gap.y, (float)random.NextDouble());
                    continue;
                }
                var type = pick();
                float center = d + type.W * 0.5f;
                float pc = TrackPath.Wrap(center / length);
                Vector3 right = path.SideAt(pc) * side;
                Vector3 along = path.DirectionAt(pc);
                float s = setback + (float)random.NextDouble() * 4f;
                Vector3 front = path.PointAt(pc) + right * s;
                front.y = 0f;
                if (!Fits(path, occupancy, front, right, along, type, s - 0.5f))
                {
                    d += 4f;
                    continue;
                }
                Spawn(root, type, front, right, along, lod);
                Mark(occupancy, front, right, along, type.W, type.D);
                placed++;
                d += type.W + 2f + (float)random.NextDouble() * 5f;
            }
            return placed;
        }

        static bool Fits(TrackPath path, HashSet<(int, int)> occupancy, Vector3 front, Vector3 right, Vector3 along, Building type, float minAxis)
        {
            var corners = new[] { front - along * type.W * 0.5f, front + along * type.W * 0.5f, front + right * type.D - along * type.W * 0.5f, front + right * type.D + along * type.W * 0.5f, front + right * type.D * 0.5f };
            if (corners.Any(c => DistanceToAxis(path, c) < minAxis || c.x < PromenadeOuter + 2f && c.z > -600f && c.z < RiverZ1)) return false;
            if (corners.Any(c => c.z > RiverZ0 - 6f && c.z < RiverZ1 + 6f)) return false;
            return Free(occupancy, front, right, along, type.W, type.D);
        }

        /// Здание «фасадом к улице»: front — середина фасада, right — от дороги вглубь.
        static GameObject Spawn(GameObject root, Building type, Vector3 front, Vector3 right, Vector3 along, float lod)
        {
            var rotation = Quaternion.LookRotation(-right);
            Vector3 position = type.Front ? front : front + right * type.D * 0.5f;
            position.y = GroundAt(position);
            var go = Instance(type.Model, root, position, rotation, Folder);
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, type.H * 0.5f, type.Front ? -type.D * 0.5f : 0f);
            col.size = new Vector3(type.W, type.H, type.D);
            Lod(go, lod);
            return go;
        }

        /// Отдельно стоящее здание по центру основания с поворотом.
        static bool SpawnFree(TrackPath path, GameObject root, HashSet<(int, int)> occupancy, Building type, Vector3 center, float yaw, float minAxis, float lod)
        {
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 right = rotation * Vector3.back, along = rotation * Vector3.right;
            Vector3 front = center - right * type.D * 0.5f;
            if (!Fits(path, occupancy, front, right, along, type, minAxis)) return false;
            Vector3 position = type.Front ? front : center;
            position.y = GroundAt(position);
            var go = Instance(type.Model, root, position, Quaternion.LookRotation(-right), Folder);
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, type.H * 0.5f, type.Front ? -type.D * 0.5f : 0f);
            col.size = new Vector3(type.W, type.H, type.D);
            Lod(go, lod);
            Mark(occupancy, front, right, along, type.W, type.D);
            return true;
        }

        static Building Pick(Building[] set, System.Random random) => set[random.Next(set.Length)];

        /// Высота земли под точкой: на холме Яффо — рельеф (дом чуть утоплен, чтобы на склоне не висел).
        static float GroundAt(Vector3 p)
        {
            float h = JaffaHill(new Vector2(p.x, p.z));
            return h > 0.05f ? h - 1.1f : 0f;
        }

        /// Приморская прямая: справа — гостиницы и жилые башни с широкими просветами (видно город), пальмы.
        static int PlaceSeafront(TrackPath path, GameObject root, GameObject nature, HashSet<(int, int)> occupancy, System.Random random)
        {
            bool Coast(float d, Vector3 p) => p.x < -440f && p.z > -440f && p.z < RiverZ0 - 30f && !Elevated(path, d);
            int placed = PlaceRow(path, root, occupancy, random, 1, Coast, 24f,
                () => random.NextDouble() < 0.55 ? Pick(Hotels, random) : random.NextDouble() < 0.5 ? Pick(Residential, random) : Pick(Bauhaus, random),
                0.38f, new Vector2(16f, 40f), 0.006f);
            // Пальмовая аллея по пляжной кромке набережной.
            for (float z = PromenadeZ0; z < PromenadeZ1; z += 18f)
                Palm(nature, new Vector3(PromenadeOuter + 1.2f, 0f, z + (float)random.NextDouble() * 4f), random);
            for (int i = 0; i < 6; i++)
            {
                float z = PromenadeZ0 + 40f + i * 115f + (float)random.NextDouble() * 20f;
                var kiosk = Instance($"TA_Kiosk_{i % 3}", root, new Vector3(PromenadeOuter + 0.5f, 0f, z), Quaternion.Euler(0f, 90f, 0f), Folder);
                var col = kiosk.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 1.5f, -2f);
                col.size = new Vector3(5f, 3f, 4f);
                Lod(kiosk, 0.02f);
            }
            for (int i = 0; i < 80; i++)
            {
                var pos = new Vector3(PromenadeOuter - 8f - (float)random.NextDouble() * 70f, -0.2f, -520f + (float)random.NextDouble() * 1040f);
                if (pos.z > RiverZ0 - 10f) continue;
                Lod(Instance($"TA_Umbrella_{i % 3}", root, pos, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), Folder), 0.015f);
            }
            for (int i = 0; i < 5; i++)
            {
                var guard = Instance("TA_Lifeguard", root, new Vector3(-600f, -0.5f, -400f + i * 190f), Quaternion.Euler(0f, -90f, 0f), Folder);
                Lod(guard, 0.01f);
            }
            return placed;
        }

        /// Портовый проспект: слева — склады, маяк и жилые башни за ними; справа — парк у реки (открытый вид на город).
        static int PlacePort(TrackPath path, GameObject root, GameObject props, GameObject nature, HashSet<(int, int)> occupancy, System.Random random)
        {
            bool Port(float d, Vector3 p) => p.z > 760f && p.x > -470f && p.x < 140f;
            int placed = PlaceRow(path, root, occupancy, random, -1, Port, 22f, () => Pick(Sandstone, random), 0.3f, new Vector2(10f, 26f), 0.01f);
            Lod(Instance("TA_Lighthouse", props, new Vector3(-600f, 0f, 760f), Quaternion.identity, Folder), 0.004f);
            for (int i = 0; i < 26; i++)
            {
                var type = Pick(Residential, random);
                var center = new Vector3(-420f + (float)random.NextDouble() * 640f, 0f, 900f + (float)random.NextDouble() * 260f);
                if (SpawnFree(path, root, occupancy, type, center, random.Next(4) * 90f, 40f, 0.004f)) placed++;
            }
            // Парк у реки: трава, фикусы, пальмы — между дорогой и берегом ничего не заслоняет вид.
            var park = new MeshBuilder("TA2_RiverPark", 1);
            park.QuadUpPoints(0, new Vector3(-470f, 0.01f, RiverZ1 + 4f), new Vector3(150f, 0.01f, RiverZ1 + 4f), new Vector3(150f, 0.01f, 820f - SidewalkOuter - 1f), new Vector3(-470f, 0.01f, 820f - SidewalkOuter - 1f), worldUv: 6f);
            Place(SaveMesh(park.Build(recalcNormals: true), Folder), "RiverPark", nature.transform, "M_Grass");
            for (int i = 0; i < 46; i++)
            {
                var p = new Vector3(-460f + (float)random.NextDouble() * 600f, 0f, RiverZ1 + 10f + (float)random.NextDouble() * 140f);
                if (DistanceToAxis(path, p) < 18f) continue;
                if (random.NextDouble() < 0.6) Ficus(nature, p, random, 1f);
                else Palm(nature, p, random);
            }
            return placed;
        }

        /// Деловой центр к востоку от эстакады: ориентиры — четыре новых небоскрёба в зоне обзора с эстакады,
        /// за ними — ряд стеклянных башен.
        static int PlaceBusiness(TrackPath path, GameObject root, HashSet<(int, int)> occupancy, System.Random random)
        {
            int placed = 0;
            var landmarks = new[]
            {
                (new Building("TA_Sky_Disc", 54f, 54f, 202f, false), new Vector3(350f, 0f, 520f), 0f),
                (new Building("TA_Sky_Twist", 40f, 40f, 186f, false), new Vector3(370f, 0f, 300f), 20f),
                (new Building("TA_Sky_Blade", 46f, 16f, 190f, false), new Vector3(430f, 0f, 660f), 70f),
                (new Building("TA_Sky_Stack", 40f, 34f, 150f, false), new Vector3(330f, 0f, 150f), 10f),
            };
            foreach (var (type, pos, yaw) in landmarks)
                if (SpawnFree(path, root, occupancy, type, pos, yaw, 60f, 0.0015f)) placed++;
            for (int attempt = 0; attempt < 2500 && placed < 40; attempt++)
            {
                var type = Pick(Towers, random);
                var center = new Vector3(300f + (float)random.NextDouble() * 380f, 0f, 60f + (float)random.NextDouble() * 720f);
                if (SpawnFree(path, root, occupancy, type, center, random.Next(4) * 90f + (float)random.NextDouble() * 15f, 70f, 0.002f)) placed++;
            }
            return placed;
        }

        /// Бульвар: справа (север) — аллея фикусов с фонарями и киосками, дома за ней; слева — дома и площадь с фонтаном.
        static int PlaceBoulevard(TrackPath path, GameObject root, GameObject props, GameObject nature, GameObject trackRoot, GameObject lightsRoot,
                                  HashSet<(int, int)> occupancy, System.Random random)
        {
            float length = path.Length;
            bool Avenue(float d, Vector3 p) => p.z < -300f && p.z > -360f && p.x > -170f && p.x < 150f;
            bool South(float d, Vector3 p) => Avenue(d, p) && (p.x < -80f || p.x > 80f);
            int placed = PlaceRow(path, root, occupancy, random, 1, Avenue, 40f, () => Pick(Bauhaus, random), 0.2f, new Vector2(8f, 18f), 0.008f);
            placed += PlaceRow(path, root, occupancy, random, -1, South, 20f, () => Pick(Bauhaus, random), 0.15f, new Vector2(8f, 16f), 0.008f);
            for (float d = 0f; d < length; d += 12f)
            {
                Vector3 axis = RoadMeshes.At(path, d, 0f);
                if (!Avenue(d, axis)) continue;
                Vector3 side = path.SideAt(d / length);
                Ficus(nature, axis + side * 19f, random, 1f);
                Ficus(nature, axis + side * 27f, random, 0.9f);
                if (Mathf.Repeat(d, 36f) < 12f)
                {
                    Lod(Instance("TA_PromenadeLamp", props, axis + side * 23f, Quaternion.LookRotation(path.DirectionAt(d / length)), Folder), 0.015f);
                    AddLamp(lightsRoot, axis + side * 23f + Vector3.up * 4.6f, new Color(1f, 0.84f, 0.6f), 18f, 8f);
                }
            }
            Mark(occupancy, new Vector3(-40f, 0f, -300f), Vector3.forward, Vector3.right, 400f, 40f);
            // Площадь: брусчатка, фонтан, пальмы по кругу, подсветка.
            var plaza = new MeshBuilder("TA2_Plaza", 1);
            plaza.QuadUpPoints(0, new Vector3(-75f, 0.02f, -330f - SidewalkOuter), new Vector3(75f, 0.02f, -330f - SidewalkOuter), new Vector3(75f, 0.02f, -460f), new Vector3(-75f, 0.02f, -460f), worldUv: 3f);
            Place(SaveMesh(plaza.Build(recalcNormals: true), Folder), "Plaza", trackRoot.transform, "M_Cobble");
            var fountain = Instance("TA_Fountain", props, new Vector3(0f, 0f, -400f), Quaternion.identity, Folder);
            fountain.transform.localScale = new Vector3(2.4f, 1.6f, 2.4f);
            Lod(fountain, 0.004f);
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                Palm(nature, new Vector3(Mathf.Cos(a) * 34f, 0f, -400f + Mathf.Sin(a) * 30f), random);
            }
            // Подсветка площади: фонари по периметру и подсветка фонтана (видно с бульвара и с эстакады).
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f + 0.2f;
                var pos = new Vector3(Mathf.Cos(a) * 56f, 0f, -400f + Mathf.Sin(a) * 44f);
                Lod(Instance("TA_PromenadeLamp", props, pos, Quaternion.identity, Folder), 0.01f);
                AddLamp(lightsRoot, pos + Vector3.up * 5f, new Color(1f, 0.8f, 0.55f), 26f, 12f);
            }
            AddLamp(lightsRoot, new Vector3(0f, 4f, -400f), new Color(0.35f, 0.9f, 1f), 40f, 18f);
            for (int i = 0; i < 4; i++)
            {
                float a = i / 4f * Mathf.PI * 2f;
                Lod(Instance($"TA_Kiosk_{i % 3}", props, new Vector3(Mathf.Cos(a) * 62f, 0f, -400f + Mathf.Sin(a) * 52f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f), Folder), 0.01f);
            }
            Mark(occupancy, new Vector3(0f, 0f, -330f - SidewalkOuter), Vector3.back, Vector3.right, 150f, 130f);
            return placed;
        }

        /// Старый Яффо: песчаник плотно у дороги, часовая башня на площади у поворота.
        static int PlaceJaffa(TrackPath path, GameObject root, GameObject props, HashSet<(int, int)> occupancy, System.Random random)
        {
            bool Jaffa(float d, Vector3 p) => p.z < -345f && p.x < -170f && p.x > -470f;
            Lod(Instance("TA_ClockTower", props, new Vector3(-196f, 0f, -462f), Quaternion.Euler(0f, 20f, 0f), Folder), 0.004f);
            Mark(occupancy, new Vector3(-196f, 0f, -478f), Vector3.forward, Vector3.right, 30f, 30f);
            int placed = PlaceRow(path, root, occupancy, random, 1, Jaffa, 15.5f, () => Pick(Sandstone, random), 0.12f, new Vector2(4f, 10f), 0.012f);
            placed += PlaceRow(path, root, occupancy, random, -1, Jaffa, 15.5f, () => Pick(Sandstone, random), 0.12f, new Vector2(4f, 10f), 0.012f);
            return placed;
        }

        /// Город в глубине кварталов: белые дома и жилые башни сеткой улиц между морем и эстакадой, к северу
        /// от порта и к югу от Яффо. Дальше 30 м от трассы — не заслоняют дорогу, но формируют силуэт.
        static int PlaceCityFill(TrackPath path, GameObject root, HashSet<(int, int)> occupancy, System.Random random)
        {
            int placed = 0;
            // Не сетка: случайные точки с минимальным шагом, повороты по 15°, сквер вместо дома в каждой пятой точке,
            // ближе к центру — выше (жилые башни), у моря — ниже (видно воду и шоу).
            void Region(float x0, float x1, float z0, float z1, int count, float towerShare, Building[] low)
            {
                for (int i = 0; i < count; i++)
                {
                    var center = new Vector3(Mathf.Lerp(x0, x1, (float)random.NextDouble()), 0f, Mathf.Lerp(z0, z1, (float)random.NextDouble()));
                    if (random.NextDouble() < 0.2) continue;
                    float inland = Mathf.InverseLerp(-480f, 100f, center.x);
                    bool tall = random.NextDouble() < towerShare * (0.4f + inland);
                    var type = tall ? Pick(Residential, random) : Pick(low, random);
                    float yaw = random.Next(24) * 15f;
                    if (SpawnFree(path, root, occupancy, type, center, yaw, 34f, 0.005f)) placed++;
                }
            }
            Region(-450f, 110f, -270f, RiverZ0 - 24f, 260, 0.3f, Bauhaus);
            Region(-460f, 140f, -740f, -480f, 90, 0.1f, Sandstone);
            Region(260f, 720f, -720f, 50f, 120, 0.45f, Bauhaus);
            Region(-420f, 300f, 880f, 1200f, 80, 0.5f, Bauhaus);
            return placed;
        }

        /// Дальний город «ковром»: сотни простых домов со светящимися окнами (текстура M_TowerGlass) до горизонта
        /// на суше — объединены в несколько мешей, без теней и коллайдеров: мегаполис за кадром почти бесплатно.
        static int PlaceCityCarpet(GameObject root, System.Random random)
        {
            int count = 0;
            var regions = new[]
            {
                (x0: 760f, x1: 2600f, z0: -1800f, z1: 2400f, n: 900),
                (x0: -600f, x1: 760f, z0: 1250f, z1: 2600f, n: 420),
                (x0: -600f, x1: 760f, z0: -2200f, z1: -800f, n: 420),
            };
            int part = 0;
            foreach (var r in regions)
            {
                var b = new MeshBuilder($"TA2_CityCarpet_{part}", 1);
                for (int i = 0; i < r.n; i++)
                {
                    float x = Mathf.Lerp(r.x0, r.x1, (float)random.NextDouble());
                    float z = Mathf.Lerp(r.z0, r.z1, (float)random.NextDouble());
                    float far = Mathf.InverseLerp(700f, 2600f, new Vector2(x, z - 200f).magnitude);
                    float w = 18f + (float)random.NextDouble() * 26f, d = 16f + (float)random.NextDouble() * 22f;
                    float h = random.NextDouble() < 0.12 ? 70f + (float)random.NextDouble() * 90f : 12f + (float)random.NextDouble() * 38f * (1f - far * 0.5f);
                    float yaw = (float)random.NextDouble() * 90f;
                    CarpetBox(b, new Vector3(x, 0f, z), w, d, h, yaw);
                    count++;
                }
                var go = Place(SaveMesh(b.Build(), Folder), $"CityCarpet_{part++}", root.transform, "M_TowerGlass");
                go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            return count;
        }

        /// Коробка дома: четыре стены и крыша; UV по метрам — сетка окон, как у стеклянных башен.
        static void CarpetBox(MeshBuilder b, Vector3 c, float w, float d, float h, float yaw)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => c + q * new Vector3(x, y, z);
            var corners = new[] { P(-w / 2, 0, -d / 2), P(w / 2, 0, -d / 2), P(w / 2, 0, d / 2), P(-w / 2, 0, d / 2) };
            float run = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = corners[i], e = corners[(i + 1) % 4];
                float len = Vector3.Distance(a, e);
                Vector3 n = Vector3.Cross(e - a, Vector3.up).normalized * -1f;
                int i0 = b.Vertex(a, new Vector2(run / 4f, 0f)), i1 = b.Vertex(e, new Vector2((run + len) / 4f, 0f));
                int i2 = b.Vertex(e + Vector3.up * h, new Vector2((run + len) / 4f, h / 3.6f)), i3 = b.Vertex(a + Vector3.up * h, new Vector2(run / 4f, h / 3.6f));
                b.QuadFacingIndices(0, i0, i1, i2, i3, n);
                run += len;
            }
            int t0 = b.Vertex(corners[0] + Vector3.up * h, Vector2.zero), t1 = b.Vertex(corners[1] + Vector3.up * h, Vector2.zero);
            int t2 = b.Vertex(corners[2] + Vector3.up * h, Vector2.zero), t3 = b.Vertex(corners[3] + Vector3.up * h, Vector2.zero);
            b.QuadFacingIndices(0, t0, t1, t2, t3, Vector3.up);
        }

        /// Огни горизонта: силуэты башен вокруг, далеко (без теней и коллайдеров, с ранним отсечением).
        static int PlaceSkyline(GameObject root, System.Random random)
        {
            int placed = 0;
            string[] models = { "TA_Tower_A", "TA_Tower_B", "TA_Tower_C", "TA_Tower_D", "TA_Sky_Blade", "TA_Sky_Stack", "TA_Resi_B" };
            for (int i = 0; i < 110; i++)
            {
                float a = (float)(random.NextDouble() * Mathf.PI * 1.4 - Mathf.PI * 0.7);
                float r = 1000f + (float)random.NextDouble() * 1200f;
                var pos = new Vector3(Mathf.Cos(a) * r + 150f, 0f, Mathf.Sin(a) * r + 150f);
                if (pos.x < SeaX + 200f) continue;
                var go = Instance(models[random.Next(models.Length)], root, pos, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), Folder);
                go.transform.localScale = new Vector3(1f, 0.7f + (float)random.NextDouble() * 0.9f, 1f);
                foreach (var rend in go.GetComponentsInChildren<Renderer>()) rend.shadowCastingMode = ShadowCastingMode.Off;
                Lod(go, 0.001f);
                placed++;
            }
            return placed;
        }

        /// Фонари: на земле — на тротуарах обеих сторон, на эстакаде и мосту — на парапетах (холодный свет).
        static int PlaceStreetLights(TrackPath path, GameObject props, GameObject nature, GameObject lightsRoot, System.Random random)
        {
            float length = path.Length;
            int count = 0;
            foreach (int side in new[] { -1, 1 })
            {
                for (float d = side > 0 ? 10f : 26f; d < length; d += 32f)
                {
                    if (Mathf.Min(d, length - d) < 16f) continue;
                    Vector3 axis = RoadMeshes.At(path, d, 0f);
                    if (InTunnel(axis) || PromenadeOpen(axis, side)) continue;
                    float p = TrackPath.Wrap(d / length);
                    Vector3 right = path.SideAt(p) * side;
                    bool high = Elevated(path, d);
                    Vector3 basePos = axis + right * (high ? ParapetInner + 0.5f : 12.6f) + Vector3.up * (high ? 1.1f : 0.15f);
                    var lampGo = Instance("StreetLamp", props, basePos, Quaternion.LookRotation(-right), "City");
                    lampGo.transform.localScale = Vector3.one * 1.25f;
                    var cap = lampGo.AddComponent<CapsuleCollider>();
                    cap.radius = 0.15f;
                    cap.height = 6f;
                    cap.center = Vector3.up * 3f;
                    Lod(lampGo, 0.012f);
                    if (count++ % 2 == 0)
                        AddLamp(lightsRoot, basePos + Vector3.up * 7f - right * 1.8f, high ? new Color(0.75f, 0.88f, 1f) : new Color(1f, 0.78f, 0.5f), 30f, 16f);
                    if (!high && random.NextDouble() < 0.55)
                    {
                        float dm = d + 16f;
                        Vector3 am = RoadMeshes.At(path, dm, 0f);
                        if (!InTunnel(am) && !Elevated(path, dm) && !PromenadeOpen(am, side))
                            Palm(nature, am + path.SideAt(TrackPath.Wrap(dm / length)) * side * 13.4f + Vector3.up * 0.15f, random);
                    }
                }
            }
            for (float z = PromenadeZ0; z < PromenadeZ1; z += 44f)
            {
                Lod(Instance("TA_PromenadeLamp", props, new Vector3(PromenadeOuter + 2.5f, 0f, z + 22f), Quaternion.identity, Folder), 0.015f);
                AddLamp(lightsRoot, new Vector3(PromenadeOuter + 2.5f, 5.6f, z + 22f), new Color(1f, 0.85f, 0.62f), 26f, 12f);
            }
            return count;
        }

        static void Palm(GameObject root, Vector3 pos, System.Random random)
        {
            string[] palms = { "TA_Palm_A", "TA_Palm_B", "TA_Palm_C" };
            var go = Instance(palms[random.Next(palms.Length)], root, pos, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), Folder);
            float s = 0.95f + (float)random.NextDouble() * 0.4f;
            go.transform.localScale = Vector3.one * s;
            var cap = go.AddComponent<CapsuleCollider>();
            cap.radius = 0.25f;
            cap.height = 4f;
            cap.center = Vector3.up * 2f;
            Lod(go, 0.012f);
        }

        static void Ficus(GameObject root, Vector3 pos, System.Random random, float scale)
        {
            var go = Instance(random.NextDouble() < 0.5 ? "TA_Ficus_0" : "TA_Ficus_1", root, pos, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), Folder);
            go.transform.localScale = Vector3.one * scale * (0.9f + (float)random.NextDouble() * 0.35f);
            var cap = go.AddComponent<CapsuleCollider>();
            cap.radius = 0.35f;
            cap.height = 3f;
            cap.center = Vector3.up * 1.5f;
            Lod(go, 0.012f);
        }

        /// LOD: объект перестаёт рисоваться, когда занимает меньше cull высоты экрана (дальние мелочи не тратят кадр).
        static void Lod(GameObject go, float cull)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            if (!go.TryGetComponent<LODGroup>(out var group)) group = go.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(cull, renderers) });
            group.RecalculateBounds();
        }

        static void AddLamp(GameObject root, Vector3 pos, Color color, float range, float intensity)
        {
            var go = new GameObject("LampLight");
            go.transform.SetParent(root.transform, false);
            go.transform.position = pos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
        }

        // ---------- материалы карты ----------

        public static void CreateMaterials()
        {
            ArtLibrary.Lit("M_AsphaltWet", "#3A3C44", 0.62f, 0.05f, null, 0f, ArtLibrary.Get("M_Asphalt").GetTexture("_BaseMap") as Texture2D);
            ArtLibrary.Lit("M_CityGroundNight", "#2A2C33", 0.3f);
            ArtLibrary.Lit("M_Sea", "#08162A", 0.96f, 0.15f);
            ArtLibrary.Lit("M_Bauhaus", "#EEE9DF", 0.25f);
            ArtLibrary.Lit("M_BauhausCream", "#E6D8BE", 0.25f);
            ArtLibrary.Lit("M_Sandstone", "#C9A878", 0.15f);
            ArtLibrary.Lit("M_SandstoneDark", "#A9875A", 0.15f);
            ArtLibrary.Lit("M_WindowLitCool", "#CFE6FF", 0.85f, 0f, "#BFDCFF", 1.4f);
            ArtLibrary.Lit("M_TowerFrame", "#2A3140", 0.6f, 0.6f);
            ArtLibrary.Lit("M_NeonTeal", "#24E0C8", 0.5f, 0f, "#24E0C8", 3.0f);
            ArtLibrary.Lit("M_NeonBlue", "#3F7BFF", 0.5f, 0f, "#3F7BFF", 3.0f);
            ArtLibrary.Lit("M_NeonYellow", "#FFD23F", 0.5f, 0f, "#FFC21A", 2.8f);
            ArtLibrary.Lit("M_PalmTrunk", "#7A6248", 0.15f);
            ArtLibrary.Lit("M_PalmFrond", "#3E7A3A", 0.3f);
            ArtLibrary.Lit("M_PalmFrond2", "#4F8F3E", 0.3f);
            ArtLibrary.Lit("M_FicusLeaf", "#3F8A45", 0.25f);
            ArtLibrary.Lit("M_FicusLeaf2", "#4FA052", 0.25f);
            ArtLibrary.Lit("M_ClockFace", "#FFF2C8", 0.5f, 0f, "#FFE7A0", 2.2f);
            ArtLibrary.Lit("M_FountainWater", "#7FE6FF", 0.9f, 0f, "#4FD8FF", 1.6f);
            ArtLibrary.Lit("M_RocketBody", "#B9BCC2", 0.5f, 0.5f);
            ArtLibrary.Lit("M_RocketDark", "#3A3D44", 0.4f, 0.4f);
            ArtLibrary.Lit("M_RocketFlame", "#FFB060", 0.5f, 0f, "#FFA040", 6f);
            ArtLibrary.Lit("M_InterceptorBody", "#E8ECEF", 0.5f, 0.3f);
            var tower = ArtLibrary.Lit("M_TowerGlass", "#FFFFFF", 0.88f, 0.5f, "#FFFFFF", 1.4f, TowerWindows("T_TowerWindows", false));
            tower.SetTexture("_EmissionMap", TowerWindows("T_TowerWindowsEmit", true));
            tower.SetTextureScale("_BaseMap", new Vector2(1f / 16f, 1f / 16f));
            EditorUtility.SetDirty(tower);
            // Повторяющиеся пропы рисуются инстансингом (SRP Batcher берёт остальное).
            foreach (var name in new[] { "M_PalmTrunk", "M_PalmFrond", "M_PalmFrond2", "M_FicusLeaf", "M_FicusLeaf2", "M_Concrete", "M_Bauhaus", "M_BauhausCream", "M_Sandstone" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{ArtLibrary.MaterialsDir}/{name}.mat");
                if (m == null) continue;
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }
            var stars = AssetDatabase.LoadAssetAtPath<Material>($"{ArtLibrary.MaterialsDir}/M_Stars.mat");
            if (stars == null)
            {
                stars = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(stars, $"{ArtLibrary.MaterialsDir}/M_Stars.mat");
            }
            stars.SetColor("_BaseColor", new Color(1.6f, 1.6f, 1.8f));
            EditorUtility.SetDirty(stars);
            var cloud = ArtLibrary.Lit("M_NightCloud", "#5A6680", 0.05f, 0f, "#0C1020", 1f, CloudTexture());
            FxFactory.Transparent(cloud, false);
            cloud.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(cloud);
            var glow = FxFactory.ParticleMaterial("M_TrailGlow", TrailTexture(), true);
            // Яркость выше 1 — следы светятся и дают ореол (bloom).
            glow.SetColor("_BaseColor", new Color(2.2f, 2.2f, 2.2f, 1f));
            // Вспышки — плоские билборды: видны с обеих сторон.
            ArtLibrary.Get("M_FxAdd").SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(glow);
            FxFactory.EnsureAssets();
            AssetDatabase.SaveAssets();
        }

        /// Окна башен: 16×16 ячеек; светится часть окон (тёплый и холодный свет), в стекле — отражение.
        static Texture2D TowerWindows(string name, bool emission)
        {
            string path = $"{ArtLibrary.TexturesDir}/{name}.png";
            int size = 512;
            var random = new System.Random(5);
            var pixels = new Color32[size * size];
            int cell = size / 16;
            for (int cy = 0; cy < 16; cy++)
                for (int cx = 0; cx < 16; cx++)
                {
                    double r = random.NextDouble();
                    bool lit = r < 0.42;
                    bool cool = random.NextDouble() < 0.4;
                    var glowColor = cool ? new Color(0.75f, 0.85f, 1f) : new Color(1f, 0.8f, 0.55f);
                    float dim = 0.55f + 0.45f * (float)random.NextDouble();
                    for (int y = 0; y < cell; y++)
                        for (int x = 0; x < cell; x++)
                        {
                            bool frame = x < 3 || y < 4 || x >= cell - 2;
                            Color c;
                            if (emission) c = frame || !lit ? Color.black : glowColor * dim;
                            else c = frame ? new Color(0.12f, 0.14f, 0.18f) : (lit ? Color.Lerp(new Color(0.2f, 0.25f, 0.32f), glowColor, 0.5f) : new Color(0.10f, 0.14f, 0.22f));
                            pixels[(cy * cell + y) * size + cx * cell + x] = c;
                        }
                }
            return SaveTexture(path, size, pixels, true);
        }

        static Texture2D CloudTexture()
        {
            string path = $"{ArtLibrary.TexturesDir}/T_CloudSoft.png";
            int size = 256;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                    float n = Mathf.PerlinNoise(u * 5f + 3f, v * 5f + 7f) * 0.6f + Mathf.PerlinNoise(u * 13f, v * 13f) * 0.4f;
                    float a = Mathf.Clamp01((1f - d * d) * (n * 1.6f - 0.35f));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            return SaveTexture(path, size, pixels, false);
        }

        static Texture2D TrailTexture()
        {
            string path = $"{ArtLibrary.TexturesDir}/T_TrailGlow.png";
            int size = 64;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float v = Mathf.Abs(y / (float)(size - 1) - 0.5f) * 2f;
                    float a = Mathf.Pow(1f - v, 2.2f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            return SaveTexture(path, size, pixels, false);
        }

        static Texture2D SaveTexture(string path, int size, Color32[] pixels, bool repeat)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.anisoLevel = 4;
            importer.alphaIsTransparency = !repeat;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------- небо ----------

        static Mesh StarDome(System.Random random)
        {
            var b = new MeshBuilder("TelAvivStars", 1);
            for (int i = 0; i < 1400; i++)
            {
                float az = (float)(random.NextDouble() * Mathf.PI * 2.0);
                float el = Mathf.Asin((float)random.NextDouble() * 0.95f + 0.05f);
                var dir = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az));
                Vector3 c = dir * 3000f;
                float s = 2.5f + (float)random.NextDouble() * (random.NextDouble() < 0.1 ? 7f : 3f);
                Vector3 u = Vector3.Cross(dir, Vector3.up).normalized;
                if (u.sqrMagnitude < 0.01f) u = Vector3.right;
                Vector3 v = Vector3.Cross(u, dir).normalized;
                b.QuadFacing(0, c - u * s - v * s, c + u * s - v * s, c + u * s + v * s, c - u * s + v * s, -dir);
            }
            return b.Build(recalcNormals: true);
        }

        static void BuildClouds(GameObject props, System.Random random)
        {
            var quad = SaveMesh(QuadMesh("TelAvivCloudQuad"), Folder);
            var root = Group("Clouds", props.transform);
            for (int i = 0; i < 52; i++)
            {
                var go = new GameObject("Cloud");
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(-1400f + (float)random.NextDouble() * 3600f, 380f + (float)random.NextDouble() * 220f, -1700f + (float)random.NextDouble() * 3600f);
                go.transform.rotation = Quaternion.Euler(90f, (float)random.NextDouble() * 360f, 0f);
                float s = 280f + (float)random.NextDouble() * 360f;
                go.transform.localScale = new Vector3(s, s * (0.5f + (float)random.NextDouble() * 0.4f), 1f);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = ArtLibrary.Get("M_NightCloud");
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        static Mesh QuadMesh(string name)
        {
            var m = new Mesh { name = name };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ---------- ракетный дождь ----------

        static void BuildRocketShow()
        {
            var root = new GameObject("RocketShow");
            var pool = Group("Pool", root.transform);
            var trailMat = ArtLibrary.Get("M_TrailGlow");
            var rockets = new List<Transform>();
            for (int i = 0; i < 48; i++)
            {
                var go = Instance("TA_Rocket", pool, Vector3.zero, Quaternion.identity, Folder);
                go.name = "Rocket_" + i;
                go.transform.localScale = Vector3.one * 3f;
                var trail = go.AddComponent<TrailRenderer>();
                trail.sharedMaterial = trailMat;
                trail.time = 5f;
                trail.minVertexDistance = 4f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 9f), new Keyframe(0.15f, 14f), new Keyframe(1f, 32f));
                trail.colorGradient = Gradient(new Color(1f, 0.55f, 0.18f, 1f), new Color(0.8f, 0.45f, 0.3f, 0.45f), new Color(0.35f, 0.33f, 0.35f, 0f));
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.emitting = false;
                rockets.Add(go.transform);
            }
            var interceptors = new List<Transform>();
            for (int i = 0; i < 40; i++)
            {
                var go = Instance("TA_Interceptor", pool, Vector3.zero, Quaternion.identity, Folder);
                go.name = "Interceptor_" + i;
                go.transform.localScale = Vector3.one * 3f;
                var trail = go.AddComponent<TrailRenderer>();
                trail.sharedMaterial = trailMat;
                trail.time = 3.5f;
                trail.minVertexDistance = 3f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 6f), new Keyframe(1f, 22f));
                trail.colorGradient = Gradient(new Color(0.85f, 0.92f, 1f, 1f), new Color(0.8f, 0.85f, 0.95f, 0.5f), new Color(0.6f, 0.65f, 0.7f, 0f));
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.emitting = false;
                interceptors.Add(go.transform);
            }
            var quad = SaveMesh(QuadMesh("TelAvivFlashQuad"), Folder);
            var flashes = new List<Renderer>();
            for (int i = 0; i < 24; i++)
            {
                var go = new GameObject("Flash_" + i);
                go.transform.SetParent(pool.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = ArtLibrary.Get("M_FxAdd");
                r.shadowCastingMode = ShadowCastingMode.Off;
                flashes.Add(r);
            }
            var lights = new List<Light>();
            for (int i = 0; i < 6; i++)
            {
                var go = new GameObject("FlashLight_" + i);
                go.transform.SetParent(pool.transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 950f;
                l.intensity = 0f;
                l.shadows = LightShadows.None;
                l.enabled = false;
                lights.Add(l);
            }
            var sparks = Particles(root.transform, "Sparks", "M_FxAdd", 700, 0.35f);
            var smoke = Particles(root.transform, "Smoke", "M_FxSoft", 500, -0.02f);

            var siren = new GameObject("Siren").AddComponent<AudioSource>();
            siren.transform.SetParent(root.transform, false);
            siren.clip = AssetDatabase.FindAssets("t:AudioClip", new[] { MusicImport.SfxRoot + "/TelAviv" })
                .Select(g => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            siren.playOnAwake = false;
            siren.spatialBlend = 0f;
            siren.loop = false;
            siren.volume = 0.5f;
            if (siren.clip == null) Debug.LogWarning("[RandomCar] Сирена не найдена в Assets/Art/Audio/Sfx/TelAviv — шоу пойдёт без неё.");
            var booms = new GameObject("Booms").AddComponent<AudioSource>();
            booms.transform.SetParent(root.transform, false);
            booms.playOnAwake = false;
            booms.spatialBlend = 0f;

            var show = root.AddComponent<RocketShow>();
            show.Configure(rockets.ToArray(), interceptors.ToArray(), flashes.ToArray(), lights.ToArray(), sparks, smoke, siren, booms, null,
                new Vector3(0f, 0f, 200f), new Vector3(1f, 0f, 0.15f),
                new[] { new Vector3(520f, 0f, -250f), new Vector3(600f, 0f, 640f), new Vector3(-220f, 0f, 1050f), new Vector3(-320f, 0f, -760f) });
        }

        static Gradient Gradient(Color a, Color b, Color c)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 0.35f), new GradientColorKey(c, 1f) },
                      new[] { new GradientAlphaKey(a.a, 0f), new GradientAlphaKey(b.a, 0.35f), new GradientAlphaKey(c.a, 1f) });
            return g;
        }

        /// Система частиц в мировых координатах: частицы только испускаются кодом (Emit), с лимитом.
        static ParticleSystem Particles(Transform parent, string name, string material, int max, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.playOnAwake = true;
            main.loop = true;
            main.startLifetime = 3f;
            main.gravityModifier = gravity;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, gravity < 0f ? new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.6f)) : new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(Gradient(Color.white, new Color(1f, 1f, 1f, 0.6f), new Color(1f, 1f, 1f, 0f)));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ArtLibrary.Get(material);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.maxParticleSize = 5f;
            return ps;
        }

        // ---------- общие ----------

        static float DistanceToAxis(TrackPath path, Vector3 point)
        {
            Vector3 axis = path.PointAt(path.Nearest(point));
            axis.y = point.y;
            return Vector3.Distance(axis, point);
        }

        const float Cell = 2f;

        static IEnumerable<(int, int)> Cells(Vector3 front, Vector3 right, Vector3 along, float w, float d)
        {
            for (float a = -w * 0.5f + 0.5f; a <= w * 0.5f - 0.5f; a += Cell * 0.5f)
                for (float b = 0.5f; b <= d - 0.5f; b += Cell * 0.5f)
                {
                    Vector3 q = front + along * a + right * b;
                    yield return (Mathf.FloorToInt(q.x / Cell), Mathf.FloorToInt(q.z / Cell));
                }
        }

        static bool Free(HashSet<(int, int)> occupancy, Vector3 front, Vector3 right, Vector3 along, float w, float d) =>
            Cells(front, right, along, w, d).All(c => !occupancy.Contains(c));

        static void Mark(HashSet<(int, int)> occupancy, Vector3 front, Vector3 right, Vector3 along, float w, float d)
        {
            foreach (var c in Cells(front, right, along, w, d)) occupancy.Add(c);
        }

        /// Заставка: над морем на мост и город, затем решётка старта.
        static CameraShot[] IntroShots(TrackPath path)
        {
            Vector3 start = path.PointAt(0f);
            Vector3 dir = path.DirectionAt(0f);
            Vector3 right = path.SideAt(0f);
            return new[]
            {
                new CameraShot { From = new Vector3(-820f, 70f, 330f), To = new Vector3(-760f, 48f, 640f), LookFrom = new Vector3(CoastX, 20f, 600f), LookTo = new Vector3(250f, 90f, 450f), Seconds = 3.6f, Fov = 50f },
                new CameraShot { From = start + dir * 150f - right * 16f + Vector3.up * 4f, To = start + dir * 40f - right * 10f + Vector3.up * 3f, LookFrom = start + dir * 60f + Vector3.up * 2f, LookTo = start + Vector3.up * 2f, Seconds = 2.8f, Fov = 50f },
            };
        }
    }
}
