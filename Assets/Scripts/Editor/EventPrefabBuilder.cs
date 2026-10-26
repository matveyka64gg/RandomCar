using System;
using System.Collections.Generic;
using RandomCar.Events;
using UnityEditor;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Префабы событий: модели из Blender, метки предупреждения, частицы, коллайдеры.
    /// В сцене по одному экземпляру на тип (пул), в игре они только включаются и выключаются.
    public static class EventPrefabBuilder
    {
        const string PrefabDir = "Assets/Prefabs/Events";
        const string EventModels = "Assets/Art/Models/Events";
        const string EnvModels = "Assets/Art/Models/Environment";
        public const string SettingsPath = "Assets/Settings/EventSettings.asset";

        public static EventSettings EnsureSettings()
        {
            ArtLibrary.EnsureFolder("Assets/Settings");
            var settings = AssetDatabase.LoadAssetAtPath<EventSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<EventSettings>();
                settings.Events = EventSettings.DefaultEvents();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            else
            {
                // Новые события из кода добавляются, а уже настроенные в инспекторе не трогаются.
                foreach (var def in EventSettings.DefaultEvents())
                    if (!settings.Events.Exists(e => e.Id == def.Id)) settings.Events.Add(def);
                EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssets();
            return settings;
        }

        public static List<GameObject> BuildPrefabs()
        {
            ArtLibrary.EnsureFolder(PrefabDir);
            FxFactory.EnsureAssets();
            DecalFactory.EnsureAssets();
            var result = new List<GameObject>
            {
                Build<WormsEvent>("worms", root =>
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Model(EventModels, "Worm", root, "Worm_" + i);
                        Model(EventModels, "DirtMound", root, "Mound_" + i);
                    }
                    FxFactory.Create(FxFactory.Kind.Dust, root, "FxDust");
                    FxFactory.Create(FxFactory.Kind.Debris, root, "FxDebris");
                }),
                Build<RocketEvent>("rocket", root =>
                {
                    var rocket = Model(EventModels, "Rocket", root, "Rocket");
                    var trail = FxFactory.Create(FxFactory.Kind.Trail, rocket.transform, "FxTrail");
                    trail.transform.localPosition = new Vector3(0f, 3.9f, 0f);
                    FxFactory.Create(FxFactory.Kind.Explosion, root, "FxExplosion");
                    FxFactory.Create(FxFactory.Kind.Smoke, root, "FxSmoke");
                    FxFactory.Create(FxFactory.Kind.Sparks, root, "FxSparks");
                }),
                Build<TankEvent>("tank", root =>
                {
                    Model(EventModels, "Tank", root, "Tank");
                    var crater = new GameObject("Crater");
                    crater.transform.SetParent(root, false);
                    DecalFactory.Flat("CraterDecal", crater.transform, DecalFactory.Disc, DecalFactory.Crater, Vector3.up * 0.01f, Vector3.one);
                    for (int k = 0; k < 7; k++)
                    {
                        float a = k / 7f * Mathf.PI * 2f;
                        var rock = Model(EnvModels, "Rock_A", crater.transform, "Rubble");
                        rock.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.95f;
                        rock.transform.localScale = Vector3.one * (0.18f + 0.06f * (k % 3));
                        rock.transform.localRotation = Quaternion.Euler(0f, k * 47f, 0f);
                    }
                    var laserGo = new GameObject("Laser");
                    laserGo.transform.SetParent(root, false);
                    var laser = laserGo.AddComponent<LineRenderer>();
                    laser.positionCount = 2;
                    laser.useWorldSpace = true;
                    laser.widthMultiplier = 0.16f;
                    laser.sharedMaterial = DecalFactory.Laser;
                    laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    laser.enabled = false;
                    FxFactory.Create(FxFactory.Kind.Muzzle, root, "FxMuzzle");
                    FxFactory.Create(FxFactory.Kind.Explosion, root, "FxExplosion");
                    FxFactory.Create(FxFactory.Kind.Smoke, root, "FxSmoke");
                }),
                Build<SheepEvent>("sheep", root =>
                {
                    for (int i = 0; i < 6; i++) Model(EventModels, i % 2 == 0 ? "Sheep_A" : "Sheep_B", root, "Sheep_" + i);
                    FxFactory.Create(FxFactory.Kind.Dust, root, "FxDust");
                }),
                Build<BallEvent>("ball", root =>
                {
                    Model(EventModels, "BeachBall", root, "Ball");
                    FxFactory.Create(FxFactory.Kind.Dust, root, "FxDust");
                }),
                Build<BananaEvent>("banana", root =>
                {
                    Model(EventModels, "BananaPeel", root, "Banana");
                    DecalFactory.Flat("Puddle", root, DecalFactory.Disc, DecalFactory.Puddle, Vector3.zero, Vector3.one);
                }),
                Build<PillarsEvent>("pillars", root =>
                {
                    var columns = Model(EventModels, "BasaltColumns", root, "Columns");
                    var box = columns.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 2f, 0f);
                    box.size = new Vector3(5.2f, 4f, 2.4f);
                    box.enabled = false;
                    DecalFactory.Flat("Cracks", root, DecalFactory.Quad, DecalFactory.Cracks, Vector3.zero, Vector3.one);
                    FxFactory.Create(FxFactory.Kind.Debris, root, "FxDebris");
                    FxFactory.Create(FxFactory.Kind.Dust, root, "FxDust");
                }),
                Build<WindEvent>("wind", root =>
                {
                    Model(EventModels, "Windsock", root, "Windsock");
                    FxFactory.Create(FxFactory.Kind.Wind, root, "FxWind");
                }),
                Build<RampEvent>("ramp", root => Model(EventModels, "Ramp", root, "Ramp")),
                Build<CrateEvent>("crate", root =>
                {
                    for (int i = 0; i < 3; i++) Model(EventModels, "BonusCrate", root, "Crate_" + i);
                    FxFactory.Create(FxFactory.Kind.Confetti, root, "FxConfetti");
                }),
                Build<BoostPadEvent>("boostpad", root => Model(EventModels, "BoostPad", root, "Pad")),
                Build<TailwindEvent>("tailwind", root => FxFactory.Create(FxFactory.Kind.WindLime, root, "FxWind")),
            };
            AssetDatabase.SaveAssets();
            return result;
        }

        static GameObject Build<T>(string id, Action<Transform> fill) where T : RaceEvent
        {
            var root = new GameObject("Event_" + id);
            var ev = root.AddComponent<T>();
            ev.Configure(id);
            fill(root.transform);
            CreateMarker(root.transform);
            var markerRoot = root.transform.Find("Marker");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                bool flat = r is ParticleSystemRenderer || r is LineRenderer || r.transform.IsChildOf(markerRoot)
                            || r.name == "CraterDecal" || r.name == "Puddle" || r.name == "Cracks";
                r.shadowCastingMode = flat ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            string path = $"{PrefabDir}/Event_{id}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        static void CreateMarker(Transform parent)
        {
            var go = new GameObject("Marker");
            go.transform.SetParent(parent, false);
            var marker = go.AddComponent<TelegraphMarker>();
            var ring = DecalFactory.Flat("Ring", go.transform, DecalFactory.Ring, DecalFactory.Marker, Vector3.up * 0.01f, Vector3.one);
            var fill = DecalFactory.Flat("Fill", go.transform, DecalFactory.Disc, DecalFactory.Marker, Vector3.zero, Vector3.one);
            var arrow = DecalFactory.Flat("Arrow", go.transform, DecalFactory.Arrow, DecalFactory.Marker, Vector3.up * 0.02f, new Vector3(0.7f, 1f, 0.7f));
            marker.Configure(ring.GetComponent<Renderer>(), fill.GetComponent<Renderer>(), arrow.GetComponent<Renderer>());
            go.SetActive(false);
        }

        static GameObject Model(string folder, string model, Transform parent, string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{model}.fbx");
            if (asset == null) throw new InvalidOperationException($"нет модели {folder}/{model}.fbx");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.name = name;
            go.transform.SetParent(parent, false);
            // Подвижные части событий выключены до розыгрыша, камни кратера всегда на месте внутри кратера.
            go.SetActive(name.StartsWith("Rubble"));
            return go;
        }
    }
}
