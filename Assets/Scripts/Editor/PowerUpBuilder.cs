using System;
using System.Collections.Generic;
using RandomCar.PowerUps;
using RandomCar.Race;
using UnityEditor;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Бонусы в сцене Game: настройки (ассет), пул ящиков «?», ракет, пулемётов, трассеров, мин, пузырей щита и молний.
    /// Всё создаётся заранее и в игре только включается (PowerUpDirector).
    public static class PowerUpBuilder
    {
        public const string SettingsPath = "Assets/Settings/PowerUpSettings.asset";
        const string Models = "Assets/Art/Models/Events";
        public const int Boxes = 160;
        public const int Rockets = 10;
        public const int Guns = 8;
        public const int Tracers = 72;
        public const int Mines = 16;
        public const int Bubbles = 8;
        public const int Bolts = 8;

        public static PowerUpSettings EnsureSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PowerUpSettings>(SettingsPath);
            if (settings != null) return settings;
            ArtLibrary.EnsureFolder("Assets/Settings");
            settings = ScriptableObject.CreateInstance<PowerUpSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        public static PowerUpDirector Build(Transform parent, RaceManager race)
        {
            FxFactory.EnsureAssets();
            var settings = EnsureSettings();
            var root = new GameObject("PowerUps").transform;
            root.SetParent(parent, false);

            var boxes = new List<Transform>();
            var boxGroup = Group("Boxes", root);
            for (int i = 0; i < Boxes; i++)
            {
                var box = Model("ItemBox", boxGroup, "Box_" + i);
                box.SetActive(false);
                foreach (var r in box.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                boxes.Add(box.transform);
            }

            var rockets = new List<RocketRig>();
            var rocketGroup = Group("Rockets", root);
            for (int i = 0; i < Rockets; i++)
            {
                var rig = new GameObject("Rocket_" + i);
                rig.transform.SetParent(rocketGroup, false);
                var homing = Model("HomingRocket", rig.transform, "Homing");
                var leader = Model("LeaderRocket", rig.transform, "Leader");
                var trail = FxFactory.Create(FxFactory.Kind.Trail, rig.transform, "Trail");
                trail.transform.localPosition = new Vector3(0f, 0f, -1.1f);
                var audio = rig.AddComponent<AudioSource>();
                Spatial(audio, 120f);
                rig.SetActive(false);
                rockets.Add(new RocketRig { Root = rig.transform, Homing = homing, Leader = leader, Trail = trail, Engine = audio });
            }

            var guns = new List<GunRig>();
            var gunGroup = Group("Guns", root);
            for (int i = 0; i < Guns; i++)
            {
                var gun = Model("MachineGun", gunGroup, "Gun_" + i);
                gun.transform.localScale = Vector3.one * 1.35f;
                gun.SetActive(false);
                gun.AddComponent<GunMarker>();
                var barrels = gun.transform.Find("Barrels");
                Transform Muzzle(string name, float x)
                {
                    var m = new GameObject(name).transform;
                    m.SetParent(barrels != null ? barrels : gun.transform, false);
                    m.position = gun.transform.TransformPoint(new Vector3(x, 0.34f, 1.3f));
                    return m;
                }
                var flash = FxFactory.Create(FxFactory.Kind.Muzzle, gun.transform, "Flash");
                flash.transform.localScale = Vector3.one * 0.35f;
                var audio = gun.AddComponent<AudioSource>();
                Spatial(audio, 160f);
                guns.Add(new GunRig { Root = gun.transform, Barrels = barrels, MuzzleLeft = Muzzle("MuzzleL", -0.12f), MuzzleRight = Muzzle("MuzzleR", 0.12f), Flash = flash, Audio = audio });
            }

            var tracers = new List<LineRenderer>();
            var tracerGroup = Group("Tracers", root);
            var tracerMaterial = ArtLibrary.Get("M_Tracer");
            for (int i = 0; i < Tracers; i++)
            {
                var go = new GameObject("Tracer_" + i);
                go.transform.SetParent(tracerGroup, false);
                var line = Line(go, tracerMaterial, 2, 0.22f, 0.08f, new Color(1f, 0.85f, 0.4f), new Color(1f, 0.55f, 0.2f, 0.25f));
                go.SetActive(false);
                tracers.Add(line);
            }

            var mines = new List<Transform>();
            var mineGroup = Group("DropMines", root);
            for (int i = 0; i < Mines; i++)
            {
                var mine = Model("Mine", mineGroup, "DropMine_" + i);
                mine.transform.localScale = Vector3.one * 1.25f;
                mine.SetActive(false);
                mines.Add(mine.transform);
            }

            var bubbles = new List<BubbleRig>();
            var bubbleGroup = Group("Bubbles", root);
            for (int i = 0; i < Bubbles; i++)
            {
                var bubble = Model("ShieldBubble", bubbleGroup, "Bubble_" + i);
                bubble.SetActive(false);
                var shell = bubble.GetComponentInChildren<Renderer>(true);
                shell.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bubbles.Add(new BubbleRig { Root = bubble.transform, Shell = shell });
            }

            var bolts = new List<LineRenderer>();
            var boltGroup = Group("Bolts", root);
            var boltMaterial = ArtLibrary.Get("M_Bolt");
            for (int i = 0; i < Bolts; i++)
            {
                var go = new GameObject("Bolt_" + i);
                go.transform.SetParent(boltGroup, false);
                var line = Line(go, boltMaterial, 10, 0.22f, 0.1f, new Color(0.7f, 0.95f, 1f), new Color(0.4f, 0.8f, 1f));
                go.SetActive(false);
                bolts.Add(line);
            }

            var director = root.gameObject.AddComponent<PowerUpDirector>();
            director.Configure(race, settings, boxes, rockets, guns, tracers, mines, bubbles, bolts);
            return director;
        }

        static Transform Group(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static LineRenderer Line(GameObject go, Material material, int points, float start, float end, Color a, Color b)
        {
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.positionCount = points;
            line.useWorldSpace = true;
            line.startWidth = start;
            line.endWidth = end;
            line.startColor = a;
            line.endColor = b;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        static void Spatial(AudioSource audio, float range)
        {
            audio.playOnAwake = false;
            audio.spatialBlend = 0.85f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.minDistance = 8f;
            audio.maxDistance = range;
            audio.dopplerLevel = 0f;
        }

        static GameObject Model(string model, Transform parent, string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Models}/{model}.fbx");
            if (asset == null) throw new InvalidOperationException($"нет модели {Models}/{model}.fbx");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.name = name;
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
