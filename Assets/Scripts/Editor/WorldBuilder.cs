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
    /// Сборка всего мира игры в редакторе: материалы, модели, префабы машин и событий, каталоги, сцены карт
    /// (Map_Hills, Map_City, Map_Desert) и постоянная сцена Game. Результат — сохранённые ассеты и сцены;
    /// в игре мир не пересоздаётся.
    public static class WorldBuilder
    {
        const string PrefabsDir = "Assets/Prefabs/Cars";
        const string ModelsDir = BuilderKit.ModelsDir;

        public static string[] CarKeys => CarRoster.Keys;

        [MenuItem("RandomCar/Build All")]
        public static void BuildAll()
        {
            ArtLibrary.CreateAll();
            TelAvivMapBuilder.CreateMaterials();
            ReimportModels();
            PhysicsAssets.ConfigurePhysics();
            BuildCarPrefabs();
            var carCatalog = Catalogs.EnsureCarCatalog();
            var eventSettings = EventPrefabBuilder.EnsureSettings();
            var eventPrefabs = EventPrefabBuilder.BuildPrefabs();
            ConfigureRenderPipeline();
            HillsMapBuilder.Build();
            CityMapBuilder.Build();
            DesertMapBuilder.Build();
            TelAvivMapBuilder.Build();
            ArenaMapBuilder.Build();
            var mapCatalog = Catalogs.EnsureMapCatalog();
            GameSceneBuilder.Build(carCatalog, mapCatalog, eventSettings, eventPrefabs);
            Catalogs.SetBuildScenes();
        }

        [MenuItem("RandomCar/Build Game Scene Only")]
        public static void BuildGameOnly()
        {
            var carCatalog = Catalogs.EnsureCarCatalog();
            var eventSettings = EventPrefabBuilder.EnsureSettings();
            var eventPrefabs = EventPrefabBuilder.BuildPrefabs();
            GameSceneBuilder.Build(carCatalog, Catalogs.EnsureMapCatalog(), eventSettings, eventPrefabs);
            Catalogs.SetBuildScenes();
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
            Transform Find(Transform root, string name) => BuilderKit.Find(root, name);
            var specs = PhysicsAssets.EnsureCarSpecs();
            var bodyMaterial = PhysicsAssets.BodyMaterial();
            var result = new GameObject[CarKeys.Length];
            for (int i = 0; i < CarKeys.Length; i++)
            {
                string key = CarKeys[i];
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsDir}/Cars/Car_{key}.fbx");
                var root = new GameObject($"Car_{key}");
                var rb = root.AddComponent<Rigidbody>();
                rb.mass = specs[key].Mass;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                // Распаковка: внутри экземпляра модели нельзя менять иерархию (диски переносятся в колёса).
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = "Model";
                instance.transform.SetParent(root.transform, false);

                Transform fl = Find(instance.transform, "Wheel_FL"), fr = Find(instance.transform, "Wheel_FR");
                Transform rl = Find(instance.transform, "Wheel_RL"), rr = Find(instance.transform, "Wheel_RR");
                Transform body = Find(instance.transform, "Body");
                // Диски и тормозные диски лежат в модели рядом с колёсами: переносим внутрь колёс (вращаются с ними).
                // Суппорты — на ступицу: она поворачивается рулём, но не вращается.
                var hubs = new Dictionary<string, Transform>();
                foreach (string label in new[] { "FL", "FR", "RL", "RR" })
                {
                    var wheelT = Find(instance.transform, "Wheel_" + label);
                    if (wheelT == null) continue;
                    foreach (string part in new[] { "Rim_", "Disc_" })
                    {
                        var t = Find(instance.transform, part + label);
                        if (t != null) t.SetParent(wheelT, true);
                    }
                    var caliper = Find(instance.transform, "Caliper_" + label);
                    if (caliper == null) continue;
                    var hub = new GameObject("Hub_" + label).transform;
                    hub.SetParent(instance.transform, false);
                    hub.SetPositionAndRotation(wheelT.position, wheelT.rotation);
                    caliper.SetParent(hub, true);
                    hubs[label] = hub;
                }
                // Проверка ориентации: передние колёса должны быть впереди (+z), левые слева (-x).
                Vector3 flLocal = root.transform.InverseTransformPoint(fl.position);
                if (flLocal.z < 0f)
                {
                    instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * instance.transform.localRotation;
                    (fl, rr) = (rr, fl);
                    (fr, rl) = (rl, fr);
                    Debug.Log($"[RandomCar] {key}: модель развёрнута на 180°");
                }

                // Кузов: составной коллайдер — низ (днище выше дороги, до капота и багажника) и кабина (уже, до крыши).
                // Машина на крыше опирается на узкую кабину и заваливается на бок, удары по низкой машине проходят
                // под высоким кузовом, углы не «висят» в воздухе над капотом. Материал кузова задаёт CarPhysics по CarSpec.
                var bodyBounds = body.GetComponent<Renderer>().bounds;
                Vector3 min = root.transform.InverseTransformPoint(bodyBounds.min);
                Vector3 max = root.transform.InverseTransformPoint(bodyBounds.max);
                var shape = BodyShape(root.transform, instance.transform, body, min, max, Mathf.Abs(root.transform.InverseTransformPoint(fl.position).x));
                var chassis = root.AddComponent<BoxCollider>();
                chassis.center = shape.ChassisCenter;
                chassis.size = shape.ChassisSize;
                chassis.sharedMaterial = bodyMaterial;
                var cabin = root.AddComponent<BoxCollider>();
                cabin.center = shape.CabinCenter;
                cabin.size = shape.CabinSize;
                cabin.sharedMaterial = bodyMaterial;

                var wheels = new List<CarWheel>();
                foreach (var (t, front, left) in new[] { (fl, true, true), (fr, true, false), (rl, false, true), (rr, false, false) })
                {
                    hubs.TryGetValue(t.name.Substring(t.name.Length - 2), out var wheelHub);
                    var wheel = new CarWheel
                    {
                        Visual = t,
                        Hub = wheelHub,
                        LocalCenter = root.transform.InverseTransformPoint(t.position),
                        Radius = t.GetComponent<Renderer>().bounds.extents.y,
                        Front = front,
                        Left = left,
                    };
                    wheel.ArchRoom = ArchRoom(root.transform, body, wheel, t.GetComponent<Renderer>().localBounds.size.x);
                    wheels.Add(wheel);
                }
                var physics = root.AddComponent<CarPhysics>();
                physics.Configure(specs[key], wheels.ToArray());
                root.AddComponent<CarController>();
                root.AddComponent<CarVisuals>().Configure(Find(instance.transform, "SteeringWheel"));
                ConfigureCosmetics(root, instance.transform, body, new[] { fl, fr, rl, rr });

                float rear = Mathf.Min(min.z, max.z);
                var boost = FxFactory.Create(FxFactory.Kind.Boost, root.transform, "FxBoost");
                boost.transform.localPosition = new Vector3(0f, 0.45f, rear - 0.05f);
                var dust = FxFactory.Create(FxFactory.Kind.CarDust, root.transform, "FxDust");
                dust.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                var smoke = FxFactory.Create(FxFactory.Kind.TireSmoke, root.transform, "FxTireSmoke");
                var sparks = FxFactory.Create(FxFactory.Kind.CarSparks, root.transform, "FxSparks");
                var pop = FxFactory.Create(FxFactory.Kind.ExhaustPop, root.transform, "FxExhaustPop");
                pop.transform.localPosition = new Vector3(0.45f, 0.32f, rear + 0.02f);
                root.AddComponent<CarFx>().Configure(smoke, dust, sparks, pop, boost);
                root.AddComponent<RandomCar.Audio.CarAudio>();
                // Огни: стоп-сигналы, задний ход, аварийка, ночью — луч ближнего света (выключен до темноты).
                var beam = new GameObject("HeadlightBeam").AddComponent<Light>();
                beam.transform.SetParent(root.transform, false);
                beam.transform.localPosition = new Vector3(0f, Mathf.Lerp(min.y, max.y, 0.35f), max.z - 0.2f);
                // Широкий пологий луч: пятно света начинается в ~10 м и растекается по дороге, а не «фонарик» под носом.
                beam.transform.localRotation = Quaternion.Euler(2.5f, 0f, 0f);
                beam.type = LightType.Spot;
                beam.range = 46f;
                beam.spotAngle = 100f;
                beam.innerSpotAngle = 30f;
                beam.intensity = 16f;
                beam.color = ArtLibrary.Hex("#FFF1D8");
                beam.shadows = LightShadows.None;
                beam.renderMode = LightRenderMode.ForcePixel;
                beam.enabled = false;
                root.AddComponent<CarLights>().Configure(beam);
                OptimizeDetails(instance.transform);

                Debug.Log($"[RandomCar] {key}: колесо FL {flLocal}, радиус {wheels[0].Radius:F2}, низ {chassis.size}, кабина {cabin.size}, " +
                          $"запас в арке {wheels[0].ArchRoom:F3}/{wheels[2].ArchRoom:F3}");
                string path = $"{PrefabsDir}/Car_{key}.prefab";
                result[i] = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
            }
            return result;
        }

        struct Shape
        {
            public Vector3 ChassisCenter, ChassisSize, CabinCenter, CabinSize;
        }

        /// Составной кузов по меткам модели (капот, крыша, багажник) и вершинам кузова: низ — от днища до кромки
        /// капота/багажника по всей длине, кабина — от неё до крыши между серединами лобового и заднего стёкол,
        /// шириной по стёклам.
        static Shape BodyShape(Transform root, Transform model, Transform body, Vector3 min, Vector3 max, float wheelX)
        {
            Vector3 Mark(string name, Vector3 fallback)
            {
                var m = BuilderKit.Find(model, name);
                return m != null ? root.InverseTransformPoint(m.position) : fallback;
            }
            float length = max.z - min.z;
            var hood = Mark("Mark_HoodStart", new Vector3(0f, Mathf.Lerp(min.y, max.y, 0.6f), min.z + length * 0.7f));
            var deck = Mark("Mark_TrunkEnd", new Vector3(0f, Mathf.Lerp(min.y, max.y, 0.6f), min.z + length * 0.3f));
            var roofRear = Mark("Mark_RoofStart", new Vector3(0f, max.y, min.z + length * 0.4f));
            var roofFront = Mark("Mark_RoofEnd", new Vector3(0f, max.y, min.z + length * 0.55f));
            float bottom = Mathf.Max(min.y, 0.3f);
            // Крыша — по меткам (верх в начале и конце крыши + выпуклость): штыревая антенна в рамку не входит.
            float top = Mathf.Min(max.y, Mathf.Max(roofRear.y, roofFront.y) + 0.03f) - 0.04f;
            float waist = Mathf.Clamp(hood.y, bottom + 0.3f, top - 0.1f);
            // Ширина по колее, а не по зеркалам: зеркала не должны цеплять соседей и стены.
            float width = Mathf.Min(Mathf.Abs(max.x - min.x) - 0.04f, 2f * (wheelX + 0.17f));
            // Крышка багажника заметно выше капота — хэтчбек, универсал, внедорожник: кабина до кормы.
            bool longRoof = deck.y > hood.y + 0.1f;
            float z0 = longRoof ? min.z + 0.08f : Mathf.Min((deck.z + roofRear.z) * 0.5f, roofRear.z - 0.1f);
            float z1 = Mathf.Max((hood.z + roofFront.z) * 0.5f, roofFront.z + 0.1f);
            // Ширина кабины — по вершинам кузова над кромкой (стёкла и стойки).
            var mesh = body.GetComponent<MeshFilter>().sharedMesh;
            float cabinHalf = 0f;
            var toRoot = root.worldToLocalMatrix * body.localToWorldMatrix;
            foreach (var v in mesh.vertices)
            {
                var p = toRoot.MultiplyPoint3x4(v);
                if (p.y > waist + 0.06f && p.y < top && p.z > z0 && p.z < z1) cabinHalf = Mathf.Max(cabinHalf, Mathf.Abs(p.x));
            }
            if (cabinHalf < 0.3f) cabinHalf = width * 0.4f;
            float cabinBottom = waist - 0.06f;
            return new Shape
            {
                ChassisCenter = new Vector3(0f, (bottom + waist) * 0.5f, (min.z + max.z) * 0.5f),
                ChassisSize = new Vector3(width, waist - bottom, length - 0.04f),
                CabinCenter = new Vector3(0f, (cabinBottom + top) * 0.5f, (z0 + z1) * 0.5f),
                CabinSize = new Vector3(Mathf.Min(width, 2f * cabinHalf - 0.04f), top - cabinBottom, z1 - z0),
            };
        }

        /// Сколько колесо может подняться от положения покоя, не задев подкрылок и крыло: лучи вверх от верха
        /// шины (по ширине и по дуге) до сетки кузова (двусторонние треугольники, без физики редактора).
        /// CarVisuals не даёт колесу уйти глубже — кузов визуально приподнимается.
        static float ArchRoom(Transform root, Transform body, CarWheel wheel, float tireWidth)
        {
            var mesh = body.GetComponent<MeshFilter>().sharedMesh;
            var toRoot = root.worldToLocalMatrix * body.localToWorldMatrix;
            var source = mesh.vertices;
            var p = new Vector3[source.Length];
            for (int i = 0; i < p.Length; i++) p[i] = toRoot.MultiplyPoint3x4(source[i]);
            var tris = mesh.triangles;
            float r = wheel.Radius;
            Vector3 c = wheel.LocalCenter;
            float x0 = c.x - tireWidth * 0.5f - 0.02f, x1 = c.x + tireWidth * 0.5f + 0.02f;
            // Треугольники над колесом в пределах его ширины и дуги.
            var near = new List<int>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 a = p[tris[t]], b = p[tris[t + 1]], d = p[tris[t + 2]];
                if (Mathf.Max(a.x, b.x, d.x) < x0 || Mathf.Min(a.x, b.x, d.x) > x1) continue;
                if (Mathf.Max(a.z, b.z, d.z) < c.z - r || Mathf.Min(a.z, b.z, d.z) > c.z + r) continue;
                if (Mathf.Max(a.y, b.y, d.y) < c.y) continue;
                near.Add(t);
            }
            float room = 0.5f;
            for (int ix = 0; ix < 4; ix++)
            {
                float x = c.x + (ix / 3f - 0.5f) * tireWidth * 0.9f;
                for (int iz = 0; iz < 9; iz++)
                {
                    float dz = (iz / 8f - 0.5f) * 1.5f * r;
                    float h = Mathf.Sqrt(Mathf.Max(0f, r * r - dz * dz));
                    var origin = new Vector3(x, c.y + h - 0.01f, c.z + dz);
                    foreach (int t in near)
                        if (RayUp(origin, p[tris[t]], p[tris[t + 1]], p[tris[t + 2]], out float dist) && dist < room)
                            room = dist;
                }
            }
            return Mathf.Max(0.01f, room + 0.01f - 0.006f);
        }

        /// Луч вверх из точки o: пересечение с треугольником (обе стороны), расстояние.
        static bool RayUp(Vector3 o, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            Vector3 dir = Vector3.up;
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 h = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, h);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det;
            Vector3 s = o - a;
            float u = Vector3.Dot(s, h) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(dir, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(e2, q) * inv;
            return distance > 0f;
        }

        /// Мелочи салона и тормозов: без теней (они внутри кузова и колёс) и только вблизи — LODGroup прячет их,
        /// когда машина меньше ~7% высоты экрана. Обшивка кабины остаётся всегда (иначе вдали стёкла «просвечивают»).
        static void OptimizeDetails(Transform model)
        {
            var near = new List<Renderer>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name;
                bool inside = n.StartsWith("Interior") || n.StartsWith("Driver") || n.StartsWith("SteeringWheel");
                bool brakes = n.StartsWith("Disc_") || n.StartsWith("Caliper_");
                if (inside || brakes || n.StartsWith("CabinLiner")) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (inside || brakes) near.Add(r);
            }
            if (near.Count == 0) return;
            var group = model.gameObject.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0.07f, near.ToArray()) });
            group.fadeMode = LODFadeMode.None;
            group.RecalculateBounds();
        }

        /// Внешний вид: обвес (Kit/<Слот>/<Вариант>), варианты шин в колёсах, слоты краски по ролям, наклейки.
        static void ConfigureCosmetics(GameObject root, Transform model, Transform body, Transform[] wheelTransforms)
        {
            // Обвес: объекты Kit_<Слот>_<Вариант> из модели раскладываются по слотам; включён заводской вариант.
            var kits = new List<CarCosmetics.KitVariant>();
            var kitRoot = new GameObject("Kit").transform;
            kitRoot.SetParent(model, false);
            var kitObjects = new List<Transform>();
            foreach (Transform child in model) if (child.name.StartsWith("Kit_")) kitObjects.Add(child);
            foreach (var t in kitObjects)
            {
                var parts = t.name.Split('_');
                if (parts.Length < 3 || !System.Enum.TryParse(parts[1], out PartSlot slot))
                {
                    Debug.LogWarning($"[RandomCar] {root.name}: непонятная деталь обвеса {t.name}");
                    continue;
                }
                var slotRoot = kitRoot.Find(parts[1]);
                if (slotRoot == null)
                {
                    slotRoot = new GameObject(parts[1]).transform;
                    slotRoot.SetParent(kitRoot, false);
                }
                t.SetParent(slotRoot, true);
                t.name = parts[2];
                t.gameObject.SetActive(parts[2] == CarBuild.Stock);
                kits.Add(new CarCosmetics.KitVariant { Slot = slot, Variant = parts[2], Object = t.gameObject });
            }

            // Колёса: заводская шина — рендер самого колеса; варианты шин переносятся внутрь колеса (вращаются с ним).
            var looks = new List<CarCosmetics.WheelLook>();
            float rimRadius = 0.24f, tireWidth = 0.24f;
            foreach (var w in wheelTransforms)
            {
                string label = w.name.Substring(w.name.Length - 2);
                var look = new CarCosmetics.WheelLook { Wheel = w, StockTire = w.GetComponent<Renderer>() };
                var rim = w.Find("Rim_" + label);
                if (rim != null)
                {
                    look.StockRim = rim.GetComponent<Renderer>();
                    var rb = look.StockRim.localBounds;
                    rimRadius = Mathf.Max(rb.extents.y, rb.extents.z) / 1.0f;
                }
                tireWidth = w.GetComponent<Renderer>().localBounds.size.x;
                foreach (Transform child in model)
                    if (child.name.StartsWith("Tire_") && child.name.EndsWith("_" + label)) look.TireVariants.Add(child.GetComponent<Renderer>());
                foreach (var tv in look.TireVariants)
                {
                    tv.transform.SetParent(w, true);
                    tv.gameObject.SetActive(false);
                }
                looks.Add(look);
            }

            // Слоты краски: кузов (зоны по материалам), стёкла, суппорты, заводские диски, детали обвеса.
            var slots = new List<CarCosmetics.PaintSlot>();
            void Collect(Renderer r, bool kit)
            {
                var mats = r.sharedMaterials;
                for (int s = 0; s < mats.Length; s++)
                {
                    if (mats[s] == null) continue;
                    PaintRole? role = mats[s].name switch
                    {
                        "M_Paint" => PaintRole.Body,
                        "M_PaintRoof" => PaintRole.Roof,
                        "M_PaintHood" => PaintRole.Hood,
                        "M_PaintLower" => PaintRole.Lower,
                        "M_CarGlass" => PaintRole.Glass,
                        "M_Caliper" => PaintRole.Caliper,
                        "M_Rim" => PaintRole.Rim,
                        _ => mats[s].name.StartsWith("M_Paint") ? PaintRole.Body : (PaintRole?)null,
                    };
                    if (role.HasValue) slots.Add(new CarCosmetics.PaintSlot { Renderer = r, Slot = s, Role = role.Value, Kit = kit });
                }
            }
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                bool kit = r.transform.IsChildOf(kitRoot);
                bool tire = r.name.StartsWith("Wheel_") || r.name.StartsWith("Tire_");
                if (!tire) Collect(r, kit);
            }
            var cosmetics = root.AddComponent<CarCosmetics>();
            cosmetics.Configure(slots, kits, looks, rimRadius, tireWidth);

            // Наклейки: окрашенные части кузова; зоны капота, крыши и багажника — по меткам из Blender.
            var filter = body.GetComponent<MeshFilter>();
            var bodyMats = body.GetComponent<Renderer>().sharedMaterials;
            var paintSubmeshes = new List<int>();
            for (int s = 0; s < bodyMats.Length; s++)
                if (bodyMats[s] != null && bodyMats[s].name.StartsWith("M_Paint")) paintSubmeshes.Add(s);
            var b = filter.sharedMesh.bounds;
            float Mark(string name, float fallback)
            {
                var m = BuilderKit.Find(model, name);
                return m != null ? body.InverseTransformPoint(m.position).z : fallback;
            }
            var hood = new Vector2(Mark("Mark_HoodStart", b.center.z + b.extents.z * 0.35f), Mark("Mark_HoodEnd", b.max.z - 0.15f));
            var roof = new Vector2(Mark("Mark_RoofStart", b.center.z - b.extents.z * 0.3f), Mark("Mark_RoofEnd", b.center.z + b.extents.z * 0.05f));
            var trunk = new Vector2(Mark("Mark_TrunkStart", b.min.z + 0.1f), Mark("Mark_TrunkEnd", b.min.z + b.extents.z * 0.45f));
            root.AddComponent<CarVinyls>().Configure(filter, paintSubmeshes.ToArray(), ArtLibrary.Get("M_Vinyl"), hood, roof, trunk);
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

    }
}
