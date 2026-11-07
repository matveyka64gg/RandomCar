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
    /// Карта «Город» (бета 0.1): вечерний город, петля 3.1 км по кварталам, 2 круга. Сцена Assets/Scenes/Map_City.unity.
    /// Ось x — на восток, z — на север, движение по часовой стрелке:
    ///  1. Главный проспект (760 м) от площади со стартом на восток.
    ///  2. Деловой квартал на востоке: офисные башни, связка «вправо-вправо-влево-влево» по кварталам.
    ///  3. Южный бульвар (520 м) с деревьями, затем две диагональные дуги.
    ///  4. Старый город на западе: брусчатка и тесная связка поворотов, выезд обратно на проспект.
    /// Дорога 16 м (4 полосы). Боковые улицы и продолжения перекрёстков закрыты барьерами за тротуаром (без тупиков на маршруте);
    /// невидимая стена — по линии фасадов.
    public static class CityMapBuilder
    {
        public const string Key = "City";
        public const string ScenePath = "Assets/Scenes/Map_City.unity";
        public const int Laps = 2;
        const string Folder = "City";
        const int Seed = 17;
        public const float RoadHalf = 8f;
        const float SidewalkWidth = 4.5f;
        const float StreetHalf = 6f;

        static readonly Vector2[] Corners =
        {
            new Vector2(-380f, 0f), new Vector2(380f, 0f), new Vector2(380f, -200f), new Vector2(220f, -200f),
            new Vector2(220f, -380f), new Vector2(420f, -380f), new Vector2(420f, -560f), new Vector2(-100f, -560f),
            new Vector2(-220f, -440f), new Vector2(-420f, -440f), new Vector2(-420f, -260f), new Vector2(-260f, -260f),
            new Vector2(-260f, -130f), new Vector2(-380f, -130f),
        };
        static readonly float[] Radii = { 30f, 45f, 26f, 24f, 24f, 30f, 50f, 60f, 60f, 28f, 24f, 22f, 22f, 26f };
        static readonly Vector2 StartPoint = new Vector2(-200f, 0f);

        static readonly MapLook Look = new MapLook
        {
            Sun = "#FFC48A", SunIntensity = 1.7f, SunEuler = new Vector3(22f, -62f, 0f),
            SkyTint = "#8FA8D8", SkyGround = "#7A6E78", SkyExposure = 1.35f, Atmosphere = 1.15f,
            AmbientSky = "#9FB2E0", AmbientEquator = "#F0BC98", AmbientGround = "#5C5660",
            Fog = "#F2CDB2", FogStart = 260f, FogEnd = 1600f,
            PostExposure = 0.2f, Contrast = 8f, Saturation = 14f, Temperature = 8f,
            BloomIntensity = 0.8f, BloomThreshold = 0.95f, Vignette = 0.18f,
        };

        // Здания: модель, ширина фасада, глубина, высота (для коллайдера и расстановки).
        static readonly (string model, float w, float d, float h)[] Mixed =
        {
            ("Bld_Shop_A", 12f, 10f, 8.5f), ("Bld_Shop_B", 10f, 10f, 8.5f), ("Bld_Shop_C", 14f, 10f, 8.5f),
            ("Bld_Apt_A", 14f, 12f, 17f), ("Bld_Apt_B", 16f, 12f, 20f), ("Bld_Apt_C", 12f, 12f, 14f),
        };
        static readonly (string model, float w, float d, float h)[] OldTown =
        {
            ("Bld_Old_A", 12f, 11f, 13f), ("Bld_Old_B", 10f, 10f, 12f), ("Bld_Old_A", 12f, 11f, 13f), ("Bld_Shop_B", 10f, 10f, 8.5f),
        };
        static readonly (string model, float w, float d, float h)[] Business =
        {
            ("Bld_Office_A", 18f, 18f, 33f), ("Bld_Office_B", 14f, 14f, 43f), ("Bld_Apt_B", 16f, 12f, 20f), ("Bld_Shop_C", 14f, 10f, 8.5f),
        };
        static readonly (string model, float w, float d, float h)[] Towers =
        {
            ("Bld_Office_A", 18f, 18f, 33f), ("Bld_Office_B", 14f, 14f, 43f), ("Bld_Apt_B", 16f, 12f, 20f), ("Bld_Apt_A", 14f, 12f, 17f), ("Bld_Apt_C", 12f, 12f, 14f),
        };

        public static TrackDefinition EnsureTrack()
        {
            var def = TrackAssets.Ensure(Key);
            var points = TrackShapes.RoundedPolygon(Corners, Radii, 6f);
            def.ControlPoints = points;
            def.SamplesPerSegment = 3;
            def.StartSample = TrackShapes.Closest(points, StartPoint) * 3;
            def.HalfWidth = RoadHalf;
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            return def;
        }

        /// Ширина тротуара или площади с каждой стороны: на площади у старта и на бульваре шире.
        sealed class Layout
        {
            public TrackPath Path;
            public float Length;
            public float[] CornerDistance;
            public float PlazaFrom, PlazaTo, BoulevardFrom, BoulevardTo, CobbleFrom, CobbleTo;

            public float Outer(float d, int side)
            {
                float extra = 0f;
                // Площадь у старта — слева (снаружи петли), справа у решётки — тротуар шире (камера меню облетает решётку на 15 м).
                if (side < 0) extra = Mathf.Max(extra, 18f * Ramp(d, PlazaFrom, PlazaTo, 24f));
                else extra = Mathf.Max(extra, 9f * Ramp(d, Length - 75f, 25f, 18f));
                // Бульвар — с обеих сторон: деревья на широком тротуаре.
                extra = Mathf.Max(extra, 5f * Ramp(d, BoulevardFrom, BoulevardTo, 18f));
                return RoadHalf + SidewalkWidth + extra;
            }

            /// 1 внутри [from, to], плавный спад длиной fade снаружи (с учётом перехода через старт).
            float Ramp(float d, float from, float to, float fade)
            {
                float span = Mathf.Repeat(to - from, Length);
                float x = Mathf.Repeat(d - from, Length);
                if (x <= span) return 1f;
                float outside = Mathf.Min(x - span, Length - x);
                return outside >= fade ? 0f : Mathf.SmoothStep(0f, 1f, 1f - outside / fade);
            }

            public bool Cobble(float d) => RoadMeshes.InRange(d, CobbleFrom, CobbleTo, Length);
            public bool Boulevard(float d) => RoadMeshes.InRange(d, BoulevardFrom, BoulevardTo, Length);
            public bool Plaza(float d) => RoadMeshes.InRange(d, PlazaFrom, PlazaTo, Length);
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
            float length = path.Length;

            var layout = new Layout { Path = path, Length = length, CornerDistance = Corners.Select(c => path.Nearest(new Vector3(c.x, 0f, c.y)) * length).ToArray() };
            layout.PlazaFrom = length - 90f;
            layout.PlazaTo = 130f;
            layout.BoulevardFrom = layout.CornerDistance[6] + 60f;
            layout.BoulevardTo = layout.CornerDistance[7] - 40f;
            layout.CobbleFrom = layout.CornerDistance[10] - 45f;
            layout.CobbleTo = layout.CornerDistance[13] + 10f;

            var trackRoot = new GameObject("Track");
            var props = Group("Props", null);
            var buildings = Group("Buildings", null);

            // Земля: городская площадь под всем, сверху — дороги и тротуары.
            Place(SaveMesh(Named(TrackMeshBuilder.Ground(5000f, -0.03f), "CityGround"), Folder), "Ground", trackRoot.transform, "M_CityGround");
            var groundCollider = new GameObject("GroundCollider").AddComponent<BoxCollider>();
            groundCollider.transform.SetParent(trackRoot.transform);
            groundCollider.size = new Vector3(5000f, 1f, 5000f);
            groundCollider.center = new Vector3(0f, -0.53f, 0f);
            groundCollider.gameObject.AddComponent<Surface>().Type = surfaces[SurfaceKind.Asphalt];

            RoadMeshes.AddRoad(path, trackRoot, "CityRoadAsphalt", "M_Asphalt", surfaces[SurfaceKind.Asphalt], d => !layout.Cobble(d), 8f, Folder, RoadHalf);
            RoadMeshes.AddRoad(path, trackRoot, "CityRoadCobble", "M_Cobble", surfaces[SurfaceKind.Cobble], layout.Cobble, 3f, Folder, RoadHalf);

            Place(SaveMesh(Named(track.Markings(GridDistances(), GridLaterals()), "CityMarkings"), Folder), "Markings", trackRoot.transform, "M_Line");
            Place(SaveMesh(LaneLines(path, d => !layout.Cobble(d) && d > 40f && d < length - 60f), Folder), "LaneLines", trackRoot.transform, "M_Line");
            Place(SaveMesh(Named(track.StartLine(), "CityStartLine"), Folder), "StartLine", trackRoot.transform, "M_Decal_White", "M_Decal_Black");

            // Боковые улицы: продолжения улиц за углами и поперечные улицы на прямых. Обрезаются по линии тротуара.
            var streets = StreetNetwork(path, layout);
            var occupancy = new HashSet<(int, int)>();
            var junctions = new List<(Vector3 point, Vector3 dir, float d, int side)>();
            var streetMesh = new MeshBuilder("CityStreets", 1);
            foreach (var (a, b) in streets) AddStreet(streetMesh, path, layout, a, b, occupancy, junctions);
            Place(SaveMesh(streetMesh.Build(), Folder), "SideStreets", trackRoot.transform, "M_Asphalt");

            // Тротуары с бордюром (сплошные: машины не попадают в щели) и невидимая стена по линии фасадов.
            var sidewalks = Place(SaveMesh(Sidewalks(path, layout), Folder), "Sidewalks", trackRoot.transform, "M_Sidewalk", "M_Curb");
            sidewalks.AddComponent<MeshCollider>();
            sidewalks.AddComponent<Surface>().Type = surfaces[SurfaceKind.Asphalt];
            var walls = new GameObject("CityWalls");
            walls.transform.SetParent(trackRoot.transform, false);
            walls.AddComponent<MeshCollider>().sharedMesh = SaveMesh(Walls(path, layout), Folder);

            MapSceneKit.AddSkidMarks();

            // Старт: арка со стартовыми огнями (по ширине дороги).
            Vector3 p0 = path.Point(0), t0 = path.TangentAt(0);
            var gantry = Instance("StartGantry", props, p0, Quaternion.LookRotation(t0));
            gantry.transform.localScale = new Vector3(RoadHalf / 6f * 1.05f, 1.1f, 1f);
            var lamps = Enumerable.Range(1, 5).Select(k => Find(gantry.transform, "StartLamp_" + k).GetComponent<Renderer>()).ToArray();
            var lights = gantry.AddComponent<StartLights>();
            lights.Configure(lamps);
            lights.SetLit(0);

            // Примыкания улиц: барьеры поперёк улицы, зебры на маршруте, светофоры.
            var zebra = new MeshBuilder("CityZebras", 1);
            var zebraAt = new List<float>();
            foreach (var j in junctions) BuildJunction(path, layout, j, props, zebra, zebraAt);
            Place(SaveMesh(zebra.Build(), Folder), "Zebras", trackRoot.transform, "M_Line");

            // Площадь у старта: флаги, кадки, скамейки, рекламный щит.
            PlacePlaza(path, layout, props, occupancy, random);

            // Застройка вдоль улиц и фоновые кварталы.
            int placed = PlaceStreetBuildings(path, layout, buildings, occupancy, random);
            int towers = PlaceTowers(path, layout, buildings, occupancy, random);

            // Фонари, скамейки, остановки, деревья бульвара.
            PlaceStreetProps(path, layout, props, random, junctions);

            // Корень карты.
            var mapRoot = new GameObject("Map");
            var raceTrack = mapRoot.AddComponent<RaceTrack>();
            raceTrack.Configure(definition);
            float gridMid = length - (MapDefinition.GridDistance(0) + MapDefinition.GridDistance(3)) / 2f;
            Vector3 gridCenter = path.PointAt(gridMid / length);
            Vector3 gridDir = path.DirectionAt(gridMid / length);
            float yaw = Mathf.Atan2(-gridDir.z, -gridDir.x) * Mathf.Rad2Deg + 40f;
            var cityMap = mapRoot.AddComponent<MapDefinition>();
            cityMap.Configure(Key, "Город", raceTrack, lights, gridCenter, yaw, IntroShots(path));
            cityMap.SetLaps(Laps);
            // Объекты событий встают на тротуар (до фасадов 12.5 м от оси).
            cityMap.SetRoadside(RoadHalf + SidewalkWidth - 0.6f);

            SetStatic(trackRoot);
            SetStatic(props);
            SetStatic(buildings);
            Debug.Log($"[RandomCar] Город: трасса {length:F0} м, кругов {Laps}, зданий у улиц {placed}, фоновых {towers}, примыканий улиц {junctions.Count}");
            MapSceneKit.Finish(scene, ScenePath, Key, p0 + Vector3.up * 8f, new Vector3(1800f, 400f, 1400f));
        }

        static Mesh Named(Mesh mesh, string name)
        {
            mesh.name = name;
            return mesh;
        }

        static float[] GridDistances() => Enumerable.Range(0, 4).Select(MapDefinition.GridDistance).ToArray();
        static float[] GridLaterals() => Enumerable.Range(0, 4).Select(MapDefinition.GridLateral).ToArray();

        static Vector3 At(TrackPath path, float distance, float lateral, float y = 0f) => RoadMeshes.At(path, distance, lateral, y);

        /// Внешняя сторона поворота (с запасом 30 м до и после дуги): сюда выносит машины, препятствий здесь не ставим.
        static bool OutsideOfTurn(TrackPath path, float d, int side)
        {
            float length = path.Length;
            for (float k = -30f; k <= 30f; k += 6f)
            {
                float p = TrackPath.Wrap((d + k) / length);
                int i = Mathf.Clamp(Mathf.RoundToInt(p * path.Count), 0, path.Count - 1);
                float c = path.CurvatureAt(i, 4);
                // Кривизна > 0 — поворот налево: внешняя сторона справа (side > 0).
                if (Mathf.Abs(c) > 1f / 90f && (c > 0f ? 1 : -1) == side) return true;
            }
            return false;
        }

        /// Точка внутри коридора маршрута (дорога, тротуар или площадь плюс запас)?
        static bool InCorridor(TrackPath path, Layout layout, Vector3 point, float margin)
        {
            float p = path.Nearest(point);
            Vector3 axis = path.PointAt(p);
            int side = Vector3.Dot(point - axis, path.SideAt(p)) > 0f ? 1 : -1;
            return DistanceToAxis(path, point) < layout.Outer(p * path.Length, side) + margin;
        }

        static float DistanceToAxis(TrackPath path, Vector3 point)
        {
            Vector3 axis = path.PointAt(path.Nearest(point));
            axis.y = point.y;
            return Vector3.Distance(axis, point);
        }

        /// Прерывистые линии между полосами (±4 м): четыре полосы на проспектах.
        static Mesh LaneLines(TrackPath path, System.Func<float, bool> include)
        {
            var b = new MeshBuilder("CityLaneLines", 1);
            float length = path.Length;
            for (float d = 0f; d < length; d += 2f)
            {
                if (!include(d) || Mathf.Repeat(d, 9f) >= 3f) continue;
                foreach (float lat in new[] { -RoadHalf * 0.5f, RoadHalf * 0.5f })
                    b.QuadUpPoints(0, At(path, d, lat - 0.08f, 0.012f), At(path, d, lat + 0.08f, 0.012f), At(path, d + 2f, lat + 0.08f, 0.012f), At(path, d + 2f, lat - 0.08f, 0.012f));
            }
            return b.Build();
        }

        /// Тротуары: плитка сверху (подсетка 0) и вертикальная грань бордюра (подсетка 1).
        static Mesh Sidewalks(TrackPath path, Layout layout)
        {
            var b = new MeshBuilder("CitySidewalks", 2);
            const float h = 0.15f, step = 2f;
            float length = path.Length;
            int count = Mathf.CeilToInt(length / step);
            foreach (int side in new[] { -1, 1 })
                for (int k = 0; k < count; k++)
                {
                    float d0 = k * step, d1 = Mathf.Min(length, (k + 1) * step);
                    float o0 = layout.Outer(d0, side), o1 = layout.Outer(d1, side);
                    Vector3 in0 = At(path, d0, side * RoadHalf, h), out0 = At(path, d0, side * o0, h);
                    Vector3 in1 = At(path, d1, side * RoadHalf, h), out1 = At(path, d1, side * o1, h);
                    b.QuadUpPoints(0, in0, out0, out1, in1, worldUv: 2f);
                    Vector3 face = At(path, d0, 0f, 0f) - At(path, d0, side, 0f);
                    b.QuadFacing(1, in0 - Vector3.up * h, in1 - Vector3.up * h, in1, in0, face);
                }
            return b.Build();
        }

        /// Невидимая стена по линии фасадов: машина не уезжает с маршрута в боковые улицы и щели между домами.
        static Mesh Walls(TrackPath path, Layout layout)
        {
            var b = new MeshBuilder("CityWalls", 1);
            const float step = 3f, h = 14f;
            float length = path.Length;
            int count = Mathf.CeilToInt(length / step);
            foreach (int side in new[] { -1, 1 })
                for (int k = 0; k < count; k++)
                {
                    float d0 = k * step, d1 = Mathf.Min(length, (k + 1) * step);
                    Vector3 a = At(path, d0, side * (layout.Outer(d0, side) + 0.3f)), c = At(path, d1, side * (layout.Outer(d1, side) + 0.3f));
                    Vector3 face = At(path, d0, 0f) - a;
                    face.y = 0f;
                    b.QuadFacing(0, a, c, c + Vector3.up * h, a + Vector3.up * h, face);
                }
            return b.Build();
        }

        // ---------- улицы ----------

        /// Отрезки боковых улиц (x, z): за каждым прямым углом — продолжения обеих улиц, на длинных прямых — поперечные улицы.
        static List<(Vector2 a, Vector2 b)> StreetNetwork(TrackPath path, Layout layout)
        {
            var result = new List<(Vector2, Vector2)>();
            int n = Corners.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = Corners[(i - 1 + n) % n], p = Corners[i], next = Corners[(i + 1) % n];
                Vector2 d1 = (p - prev).normalized, d2 = (next - p).normalized;
                if (Mathf.Abs(Vector2.SignedAngle(d1, d2)) < 80f) continue;
                result.Add((p, p + d1 * 90f));
                result.Add((p, p - d2 * 90f));
            }
            for (int i = 0; i < n; i++)
            {
                Vector2 a = Corners[i], b = Corners[(i + 1) % n];
                float len = Vector2.Distance(a, b);
                if (len < 170f) continue;
                Vector2 dir = (b - a) / len, normal = new Vector2(-dir.y, dir.x);
                int count = Mathf.FloorToInt((len - 120f) / 130f) + 1;
                for (int k = 0; k < count; k++)
                {
                    float t = 60f + (len - 120f) * (count == 1 ? 0.5f : k / (float)(count - 1));
                    Vector2 c = a + dir * t;
                    // Не у самой стартовой линии и не на решётке.
                    float d = path.Nearest(new Vector3(c.x, 0f, c.y)) * layout.Length;
                    if (d < 70f || d > layout.Length - 70f) continue;
                    result.Add((c - normal * 95f, c + normal * 95f));
                }
            }
            return result;
        }

        /// Полотно улицы кусками по 2 м: только за тротуаром маршрута и не ближе 2 м к чужому коридору.
        /// Точки, где улица выходит к маршруту, — примыкания (барьер, зебра, светофор).
        static void AddStreet(MeshBuilder b, TrackPath path, Layout layout, Vector2 a, Vector2 c, HashSet<(int, int)> occupancy, List<(Vector3, Vector3, float, int)> junctions)
        {
            float len = Vector2.Distance(a, c);
            if (len < 1f) return;
            Vector2 dir2 = (c - a) / len;
            Vector3 dir = new Vector3(dir2.x, 0f, dir2.y), side = Vector3.Cross(Vector3.up, dir);
            const float step = 2f;
            bool wasInside = true;
            for (float s = 0f; s < len; s += step)
            {
                Vector3 m = new Vector3(a.x, 0f, a.y) + dir * (s + step * 0.5f);
                float p = path.Nearest(m);
                float d = p * layout.Length;
                Vector3 axis = path.PointAt(p);
                int routeSide = Vector3.Dot(m - axis, path.SideAt(p)) > 0f ? 1 : -1;
                Vector3 q0 = new Vector3(a.x, 0.004f, a.y) + dir * s, q1 = q0 + dir * step;
                // Кусок улицы целиком за тротуаром: проверяются все четыре угла (у поворотов улица идёт наискосок к дуге).
                bool inside = false;
                foreach (var q in new[] { m, q0 - side * StreetHalf, q0 + side * StreetHalf, q1 - side * StreetHalf, q1 + side * StreetHalf })
                {
                    float pq = path.Nearest(q);
                    Vector3 aq = path.PointAt(pq);
                    int sq = Vector3.Dot(q - aq, path.SideAt(pq)) > 0f ? 1 : -1;
                    if (DistanceToAxis(path, q) < layout.Outer(pq * layout.Length, sq) + 0.35f) inside = true;
                }
                if (!inside)
                {
                    b.QuadUpPoints(0, q0 - side * StreetHalf, q0 + side * StreetHalf, q1 + side * StreetHalf, q1 - side * StreetHalf, worldUv: 8f);
                    for (float w = -StreetHalf - 2f; w <= StreetHalf + 2f; w += 1f)
                    {
                        Vector3 cell = m + side * w;
                        occupancy.Add((Mathf.FloorToInt(cell.x / Cell), Mathf.FloorToInt(cell.z / Cell)));
                    }
                    if (wasInside && s > 0.5f)
                        junctions.Add((new Vector3(a.x, 0f, a.y) + dir * s, dir, d, routeSide));
                }
                else if (!wasInside)
                    junctions.Add((new Vector3(a.x, 0f, a.y) + dir * s, -dir, d, routeSide));
                wasInside = inside;
            }
        }

        static void BuildJunction(TrackPath path, Layout layout, (Vector3 point, Vector3 dir, float d, int side) j, GameObject props, MeshBuilder zebra, List<float> zebraAt)
        {
            float length = path.Length;
            Vector3 across = Vector3.Cross(Vector3.up, j.dir);
            // Барьеры поперёк улицы чуть дальше линии фасадов.
            for (float w = -StreetHalf + 1.5f; w <= StreetHalf - 1.4f; w += 3.1f)
            {
                Vector3 pos = j.point + j.dir * 0.9f + across * w;
                var block = Instance("CityBarrier", props, pos, Quaternion.LookRotation(j.dir), "City");
                var col = block.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 0.45f, 0f);
                col.size = new Vector3(3f, 0.9f, 0.6f);
            }
            // Светофоры по углам примыкания, на краю тротуара, смотрят на маршрут.
            foreach (float off in new[] { -StreetHalf - 1.2f, StreetHalf + 1.2f })
            {
                float dd = j.d + off;
                if (OutsideOfTurn(path, dd, j.side)) continue;
                float p = TrackPath.Wrap(dd / length);
                Vector3 tl = At(path, dd, j.side * (RoadHalf + 0.9f), 0.15f);
                var light = Instance("TrafficLight", props, tl, Quaternion.LookRotation(-path.SideAt(p) * j.side), "City");
                Lod(light, 0.02f);
                light.AddComponent<CapsuleCollider>().radius = 0.15f;
            }
            // Зебра на маршруте перед примыканием (одна на место, если улицы примыкают с обеих сторон).
            if (zebraAt.Any(z => Mathf.Abs(Mathf.DeltaAngle(z / length * 360f, j.d / length * 360f)) / 360f * length < 16f)) return;
            if (j.d < 60f || j.d > length - 60f) return;
            zebraAt.Add(j.d);
            float center = j.d - StreetHalf - 2.2f;
            for (float l = -RoadHalf + 0.7f; l < RoadHalf - 0.5f; l += 1.3f)
            {
                Vector3 c0 = At(path, center - 1.6f, l, 0.016f), c1 = At(path, center - 1.6f, l + 0.65f, 0.016f);
                Vector3 c2 = At(path, center + 1.6f, l + 0.65f, 0.016f), c3 = At(path, center + 1.6f, l, 0.016f);
                zebra.QuadUpPoints(0, c0, c1, c2, c3);
            }
        }

        // ---------- застройка ----------

        static (string model, float w, float d, float h)[] Pool(Vector3 p)
        {
            if (p.x < -180f && p.z < -90f && p.z > -480f) return OldTown;
            if (p.x > 150f && p.z < -60f) return Business;
            return Mixed;
        }

        /// Дома вдоль улиц фасадом к дороге. Угол каждого дома не ближе линии фасадов, дома не налезают друг на друга и на улицы.
        static int PlaceStreetBuildings(TrackPath path, Layout layout, GameObject root, HashSet<(int, int)> occupancy, System.Random random)
        {
            int placed = 0;
            float length = path.Length;
            foreach (int side in new[] { -1, 1 })
            {
                float d = 0f;
                while (d < length)
                {
                    var pool = Pool(path.PointAt(TrackPath.Wrap(d / length)));
                    var type = pool[random.Next(pool.Length)];
                    float center = d + type.w * 0.5f;
                    float p = TrackPath.Wrap(center / length);
                    // Здания у старта не мешают площади (на ней — флаги и кадки).
                    if (side < 0 && layout.Plaza(center))
                    {
                        d += 4f;
                        continue;
                    }
                    Vector3 right = path.SideAt(p) * side;
                    Vector3 along = path.DirectionAt(p);
                    float front = Mathf.Max(layout.Outer(d, side), layout.Outer(d + type.w, side), layout.Outer(center, side)) + 0.4f;
                    Vector3 frontPos = path.PointAt(p) + right * front;
                    var corners = new[]
                    {
                        frontPos - along * type.w * 0.5f, frontPos + along * type.w * 0.5f,
                        frontPos + right * type.d - along * type.w * 0.5f, frontPos + right * type.d + along * type.w * 0.5f,
                    };
                    bool clear = corners.All(c => DistanceToAxis(path, c) >= front - 0.3f) && Free(occupancy, frontPos, right, along, type.w, type.d);
                    if (!clear)
                    {
                        d += 3f;
                        continue;
                    }
                    var go = Instance(type.model, root, frontPos, Quaternion.LookRotation(-right), "City");
                    AddBox(go, type.w, type.d, type.h);
                    Lod(go, 0.006f);
                    Mark(occupancy, frontPos, right, along, type.w, type.d);
                    placed++;
                    d += type.w + (float)random.NextDouble() * 1.2f + 0.2f;
                }
            }
            return placed;
        }

        /// Фоновые кварталы: у маршрута (до 100 м) — модели домов по сетке кварталов (с LOD), дальше — «ковёр» простых коробок
        /// в нескольких общих мешах до горизонта (тысячи домов без тысяч объектов).
        static int PlaceTowers(TrackPath path, Layout layout, GameObject root, HashSet<(int, int)> occupancy, System.Random random)
        {
            int placed = 0, carpet = 0;
            const float pitch = 21f;
            string[] carpetMaterials = { "M_Plaster", "M_Brick", "M_PlasterTeal", "M_PlasterMustard", "M_PlasterLilac" };
            var b = new MeshBuilder("CityCarpet", carpetMaterials.Length);
            // Сначала — мелкий шаг у самого маршрута: углы кварталов снаружи поворотов и щели за первым рядом домов.
            for (float x = -560f; x <= 560f; x += 9f)
                for (float z = -720f; z <= 120f; z += 9f)
                {
                    var pos = new Vector3(x, 0f, z);
                    float axis = DistanceToAxis(path, pos);
                    if (axis < 16f || axis > 48f) continue;
                    bool business = pos.x > 150f && pos.z < -60f;
                    bool old = pos.x < -180f && pos.z < -90f && pos.z > -480f;
                    var type = business ? Business[random.Next(Business.Length)] : old ? OldTown[random.Next(OldTown.Length)] : Mixed[random.Next(Mixed.Length)];
                    var rot = FaceRoute(path, pos);
                    Vector3 right = rot * Vector3.back, along = rot * Vector3.right;
                    if (!Free(occupancy, pos, right, along, type.w + 1f, type.d + 1f)) continue;
                    var probe = new[]
                    {
                        pos - along * type.w * 0.5f, pos + along * type.w * 0.5f, pos + right * type.d - along * type.w * 0.5f, pos + right * type.d + along * type.w * 0.5f,
                        pos, pos + right * type.d, pos + right * type.d * 0.5f - along * type.w * 0.5f, pos + right * type.d * 0.5f + along * type.w * 0.5f,
                    };
                    if (probe.Any(c => InCorridor(path, layout, c, 1.2f))) continue;
                    var go = Instance(type.model, root, pos, rot, "City");
                    AddBox(go, type.w, type.d, type.h);
                    Lod(go, 0.01f);
                    Mark(occupancy, pos, right, along, type.w, type.d);
                    placed++;
                }
            for (float x = -980f; x <= 980f; x += pitch)
                for (float z = -1100f; z <= 520f; z += pitch)
                {
                    var pos = new Vector3(x + (float)(random.NextDouble() * 4.0 - 2.0), 0f, z + (float)(random.NextDouble() * 4.0 - 2.0));
                    float axis = DistanceToAxis(path, pos);
                    if (axis < 30f) continue;
                    bool business = pos.x > 150f && pos.z < -60f;
                    bool old = pos.x < -180f && pos.z < -90f && pos.z > -480f;
                    if (axis > 100f)
                    {
                        if (random.NextDouble() > (axis < 400f ? 0.75 : 0.45)) continue;
                        float w = 12f + (float)random.NextDouble() * 8f, d = 12f + (float)random.NextDouble() * 6f;
                        float h = business ? 22f + (float)random.NextDouble() * 34f : old ? 9f + (float)random.NextDouble() * 6f : 10f + (float)random.NextDouble() * 18f;
                        if (random.NextDouble() < 0.06) h += 25f;
                        var rotC = Quaternion.Euler(0f, random.Next(4) * 90f, 0f);
                        if (!Free(occupancy, pos - rotC * Vector3.back * (d * 0.5f), rotC * Vector3.back, rotC * Vector3.right, w + 2f, d + 2f)) continue;
                        CarpetBox(b, random.Next(carpetMaterials.Length), pos, w, d, h, rotC);
                        carpet++;
                        continue;
                    }
                    if (random.NextDouble() > 0.5) continue;
                    var type = business ? Towers[random.Next(2)] : old ? OldTown[random.Next(OldTown.Length)] : Towers[2 + random.Next(Towers.Length - 2)];
                    var rot = FaceRoute(path, pos);
                    Vector3 right = rot * Vector3.back, along = rot * Vector3.right;
                    if (!Free(occupancy, pos, right, along, type.w + 2f, type.d + 2f)) continue;
                    var corners = new[] { pos - along * type.w * 0.5f, pos + along * type.w * 0.5f, pos + right * type.d - along * type.w * 0.5f, pos + right * type.d + along * type.w * 0.5f };
                    if (corners.Any(c => DistanceToAxis(path, c) < 24f || InCorridor(path, layout, c, 2f))) continue;
                    var go = Instance(type.model, root, pos, rot, "City");
                    AddBox(go, type.w, type.d, type.h);
                    Lod(go, 0.012f);
                    Mark(occupancy, pos, right, along, type.w, type.d);
                    placed++;
                }
            var carpetGo = Place(SaveMesh(b.Build(), Folder), "CityCarpet", root.transform, carpetMaterials);
            carpetGo.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Debug.Log($"[RandomCar] Город: моделей в кварталах {placed}, домов-коробок до горизонта {carpet}");
            return placed + carpet;
        }

        /// Поворот дома фасадом к ближайшей точке маршрута (кратно 90°: кварталы по сетке улиц).
        static Quaternion FaceRoute(TrackPath path, Vector3 pos)
        {
            Vector3 to = path.PointAt(path.Nearest(pos)) - pos;
            to.y = 0f;
            float yaw = Mathf.Round(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg / 90f) * 90f;
            return Quaternion.Euler(0f, yaw, 0f);
        }

        /// Коробка дома с плоской крышей (центр — середина основания).
        static void CarpetBox(MeshBuilder b, int sub, Vector3 c, float w, float d, float h, Quaternion q)
        {
            Vector3 P(float x, float y, float z) => c + q * new Vector3(x, y, z);
            var corners = new[] { P(-w / 2, 0, -d / 2), P(w / 2, 0, -d / 2), P(w / 2, 0, d / 2), P(-w / 2, 0, d / 2) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = corners[i], e = corners[(i + 1) % 4];
                Vector3 n = -Vector3.Cross(e - a, Vector3.up).normalized;
                b.QuadFacing(sub, a, e, e + Vector3.up * h, a + Vector3.up * h, n);
            }
            b.QuadFacing(sub, corners[0] + Vector3.up * h, corners[1] + Vector3.up * h, corners[2] + Vector3.up * h, corners[3] + Vector3.up * h, Vector3.up);
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

        static void PlacePlaza(TrackPath path, Layout layout, GameObject root, HashSet<(int, int)> occupancy, System.Random random)
        {
            float length = path.Length;
            for (float d = -80f; d <= 120f; d += 14f)
            {
                float p = TrackPath.Wrap(d / length);
                Vector3 right = -path.SideAt(p);
                float outer = layout.Outer(Mathf.Repeat(d, length), -1);
                if (outer < RoadHalf + 12f) continue;
                Vector3 axis = path.PointAt(p);
                Instance(((int)Mathf.Repeat(d / 14f, 2f)) == 0 ? "FlagPole_Orange" : "FlagPole_Blue", root, axis + right * (RoadHalf + 3.2f) + Vector3.up * 0.15f, Quaternion.LookRotation(-right));
                // Ряд кадок с деревьями в глубине площади.
                var planter = Instance("Planter", root, axis + right * (outer - 3.5f) + Vector3.up * 0.15f, Quaternion.LookRotation(-right), "City");
                var col = planter.AddComponent<BoxCollider>();
                col.center = Vector3.up * 0.35f;
                col.size = new Vector3(1.4f, 0.7f, 1.4f);
                if (random.NextDouble() < 0.5)
                    Instance("Bench", root, axis + right * (outer - 7.5f) + Vector3.up * 0.15f, Quaternion.LookRotation(right), "City");
            }
            // Рекламный щит над площадью.
            {
                float d = 40f;
                float p = TrackPath.Wrap(d / length);
                Vector3 right = -path.SideAt(p);
                float outer = layout.Outer(d, -1);
                Instance("CityBillboard_9", root, path.PointAt(p) + right * (outer + 6f), Quaternion.LookRotation(-right), "City");
            }
        }

        static void PlaceStreetProps(TrackPath path, Layout layout, GameObject root, System.Random random, List<(Vector3 point, Vector3 dir, float d, int side)> junctions)
        {
            float length = path.Length;
            bool NearJunction(float d, int side, float margin) => junctions.Any(j => j.side == side && Mathf.Abs(Mathf.DeltaAngle(j.d / length * 360f, d / length * 360f)) / 360f * length < StreetHalf + margin);
            bool NearStart(float d) => Mathf.Abs(Mathf.DeltaAngle(d / length * 360f, 0f)) / 360f * length < 14f;
            foreach (int side in new[] { -1, 1 })
            {
                for (float d = side > 0 ? 10f : 24f; d < length; d += 28f)
                {
                    if (NearStart(d) || NearJunction(d, side, 3f) || OutsideOfTurn(path, d, side)) continue;
                    float p = TrackPath.Wrap(d / length);
                    Vector3 right = path.SideAt(p) * side;
                    var lamp = Instance("StreetLamp", root, path.PointAt(p) + right * (RoadHalf + 1.0f) + Vector3.up * 0.15f, Quaternion.LookRotation(-right), "City");
                    Lod(lamp, 0.02f);
                    var cap = lamp.AddComponent<CapsuleCollider>();
                    cap.radius = 0.15f;
                    cap.height = 6f;
                    cap.center = Vector3.up * 3f;

                    // Между фонарями: на бульваре — дерево в кадке, иначе скамейка, кадка или остановка.
                    float dm = d + 14f;
                    if (NearJunction(dm, side, 3f) || NearStart(dm) || OutsideOfTurn(path, dm, side)) continue;
                    if (side < 0 && layout.Plaza(dm)) continue;
                    float pm = TrackPath.Wrap(dm / length);
                    Vector3 rm = path.SideAt(pm) * side;
                    float outer = layout.Outer(dm, side);
                    if (layout.Boulevard(dm))
                    {
                        Vector3 spotTree = path.PointAt(pm) + rm * (RoadHalf + 2.6f) + Vector3.up * 0.15f;
                        var planter = Instance("Planter", root, spotTree, Quaternion.LookRotation(-rm), "City");
                        var pc = planter.AddComponent<BoxCollider>();
                        pc.center = Vector3.up * 0.35f;
                        pc.size = new Vector3(1.4f, 0.7f, 1.4f);
                        var tree = Instance(random.Next(2) == 0 ? "Tree_A" : "Tree_B", root, spotTree + Vector3.up * 0.55f, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                        tree.transform.localScale = Vector3.one * 0.7f;
                        if (random.NextDouble() < 0.4) Instance("Bench", root, path.PointAt(pm) + rm * (outer - 1.2f) + Vector3.up * 0.15f, Quaternion.LookRotation(-rm), "City");
                        continue;
                    }
                    Vector3 spot = path.PointAt(pm) + rm * (outer - 1.0f) + Vector3.up * 0.15f;
                    double roll = random.NextDouble();
                    if (roll < 0.35)
                    {
                        var planter = Instance("Planter", root, spot, Quaternion.LookRotation(-rm), "City");
                        var col = planter.AddComponent<BoxCollider>();
                        col.center = Vector3.up * 0.35f;
                        col.size = new Vector3(1.4f, 0.7f, 1.4f);
                    }
                    else if (roll < 0.6) Instance("Bench", root, spot, Quaternion.LookRotation(-rm), "City");
                    else if (roll < 0.7) Instance("BusStop", root, spot + rm * 0.4f, Quaternion.LookRotation(-rm), "City");
                }
            }
        }

        static void AddBox(GameObject go, float w, float d, float h)
        {
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, h * 0.5f, -d * 0.5f);
            col.size = new Vector3(w, h, d);
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

        static CameraShot[] IntroShots(TrackPath path)
        {
            Vector3 start = path.PointAt(0f);
            Vector3 dir = path.DirectionAt(0f);
            Vector3 right = path.SideAt(0f);
            return new[]
            {
                new CameraShot { From = new Vector3(-560f, 190f, 220f), To = new Vector3(-380f, 150f, 260f), LookFrom = new Vector3(0f, 0f, -280f), LookTo = new Vector3(-60f, 10f, -200f), Seconds = 3.2f, Fov = 50f },
                new CameraShot { From = start + dir * 150f + right * 5f + Vector3.up * 3f, To = start + dir * 40f + right * 4f + Vector3.up * 2.5f, LookFrom = start + dir * 60f + Vector3.up * 2f, LookTo = start + Vector3.up * 2f, Seconds = 2.8f, Fov = 50f },
            };
        }
    }
}
