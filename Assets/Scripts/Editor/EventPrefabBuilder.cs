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
        /// Крик «EL DRIFTO!» — оригинальная запись пользователя (не заменять).
        public const string ElDriftoClipPath = "Assets/Art/Audio/Sfx/Events/el-drifto.mp3";

        public static EventSettings EnsureSettings()
        {
            ArtLibrary.EnsureFolder("Assets/Settings");
            var settings = AssetDatabase.LoadAssetAtPath<EventSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<EventSettings>();
                settings.Events = EventSettings.DefaultEvents();
                settings.Version = EventSettings.CurrentVersion;
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            else if (settings.Version < 2)
            {
                // Переход на правила v2: интервалы, лимит одновременных событий, длины зон. Веса не меняются.
                settings.ModerateInterval = new Vector2(8f, 12f);
                settings.ManyInterval = new Vector2(4f, 7f);
                settings.ModerateMaxActive = 2;
                settings.ManyMaxActive = 3;
                settings.StartGrace = 4f;
                foreach (var def in EventSettings.DefaultEvents())
                {
                    var e = settings.Events.Find(x => x.Id == def.Id);
                    if (e == null) continue;
                    e.ZoneLength = def.ZoneLength;
                    e.NeedsStraight = def.NeedsStraight;
                    e.ActiveSeconds = def.ActiveSeconds;
                }
                settings.Version = 2;
                EditorUtility.SetDirty(settings);
                Debug.Log("[RandomCar] EventSettings переведены на правила v2 (зоны, частота 8–12 / 4–7 с)");
            }
            if (settings.Version < 3)
            {
                // Правила v3: события впереди игроков с ожиданием подъезда и удержанием зоны, интервалы под накал.
                // Описания событий берутся из кода целиком (веса не менялись и в инспекторе не настраивались).
                settings.Events = EventSettings.DefaultEvents();
                var fresh = ScriptableObject.CreateInstance<EventSettings>();
                settings.ModerateInterval = fresh.ModerateInterval;
                settings.ManyInterval = fresh.ManyInterval;
                settings.StartGrace = fresh.StartGrace;
                UnityEngine.Object.DestroyImmediate(fresh);
                settings.Version = 3;
                EditorUtility.SetDirty(settings);
                Debug.Log("[RandomCar] EventSettings переведены на правила v3 (события впереди игроков, накал по кругам)");
            }
            if (settings.Version < 4)
            {
                // Хаос v4: «Безумие», категории и совместимость событий, бюджет физики, новые события и интервалы.
                // Описания событий берутся из кода целиком (веса старых событий не менялись).
                settings.Events = EventSettings.DefaultEvents();
                var fresh = ScriptableObject.CreateInstance<EventSettings>();
                settings.ModerateInterval = fresh.ModerateInterval;
                settings.ManyInterval = fresh.ManyInterval;
                settings.MayhemInterval = fresh.MayhemInterval;
                settings.ModerateMaxActive = fresh.ModerateMaxActive;
                settings.ManyMaxActive = fresh.ManyMaxActive;
                settings.MayhemMaxActive = fresh.MayhemMaxActive;
                settings.ModerateMaxBodies = fresh.ModerateMaxBodies;
                settings.ManyMaxBodies = fresh.ManyMaxBodies;
                settings.MayhemMaxBodies = fresh.MayhemMaxBodies;
                settings.StartGrace = fresh.StartGrace;
                UnityEngine.Object.DestroyImmediate(fresh);
                settings.Version = EventSettings.CurrentVersion;
                EditorUtility.SetDirty(settings);
                Debug.Log("[RandomCar] EventSettings переведены на правила v4 (хаос: «Безумие», комбинации, физические объекты)");
            }
            if (settings.Version < 6)
            {
                // Хаос v5 «MAXIMUM CHAOS»: «безумие» — стандарт, «абсурд», поток трюков, сила ударов (ChaosPower);
                // v6 (5.1) — «безумие» спокойнее (играбельно), прежний темп перенесён в «абсурд».
                // Описания событий берутся из кода целиком, интервалы и пределы — новые.
                settings.Events = EventSettings.DefaultEvents();
                var fresh = ScriptableObject.CreateInstance<EventSettings>();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(fresh), settings);
                settings.Events = EventSettings.DefaultEvents();
                UnityEngine.Object.DestroyImmediate(fresh);
                settings.Version = EventSettings.CurrentVersion;
                EditorUtility.SetDirty(settings);
                Debug.Log("[RandomCar] EventSettings переведены на правила v5 (MAXIMUM CHAOS: трюки, «абсурд», интервалы)");
            }
            else if (settings.Version < 7)
            {
                // v7: шоу-события на всех уровнях Хаоса. Описания шоу и настройки их потока — из кода целиком
                // (в ассете v6 лежали «только безумие и абсурд»), остальные события и настройки не трогаются.
                settings.Events.RemoveAll(e => e.Spectacle);
                settings.Events.AddRange(EventSettings.DefaultEvents().FindAll(d => d.Spectacle));
                var fresh = ScriptableObject.CreateInstance<EventSettings>();
                settings.ModerateSpectacleInterval = fresh.ModerateSpectacleInterval;
                settings.ManySpectacleInterval = fresh.ManySpectacleInterval;
                settings.MayhemSpectacleInterval = fresh.MayhemSpectacleInterval;
                settings.AbsurdSpectacleInterval = fresh.AbsurdSpectacleInterval;
                settings.ModerateSpectacleFirst = fresh.ModerateSpectacleFirst;
                settings.ManySpectacleFirst = fresh.ManySpectacleFirst;
                settings.SpectacleFirst = fresh.SpectacleFirst;
                settings.AbsurdSpectacleFirst = fresh.AbsurdSpectacleFirst;
                UnityEngine.Object.DestroyImmediate(fresh);
                settings.Version = 7;
                Debug.Log("[RandomCar] EventSettings переведены на правила v7 (шоу-события на всех уровнях Хаоса, ГИПЕРГАМИЯ)");
            }
            // Новые события из кода добавляются, а уже настроенные в инспекторе не трогаются.
            foreach (var def in EventSettings.DefaultEvents())
                if (!settings.Events.Exists(e => e.Id == def.Id)) settings.Events.Add(def);
            EditorUtility.SetDirty(settings);
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
                Build<DebrisEvent>("debris", root =>
                {
                    var fragment = Model(EventModels, "Rocket", root, "Fragment");
                    fragment.transform.localScale = Vector3.one * 0.55f;
                    var trail = FxFactory.Create(FxFactory.Kind.Trail, fragment.transform, "FxTrail");
                    trail.transform.localPosition = new Vector3(0f, 3.9f, 0f);
                    FxFactory.Create(FxFactory.Kind.Explosion, root, "FxExplosion");
                    FxFactory.Create(FxFactory.Kind.Smoke, root, "FxSmoke");
                    FxFactory.Create(FxFactory.Kind.Sparks, root, "FxSparks");
                    // Куски на дороге: настоящие препятствия (коллайдеры), лежат до конца события.
                    string[] rocks = { "Rock_A", "Rock_B", "Rock_A", "Rock_B", "Rock_A" };
                    for (int i = 0; i < rocks.Length; i++)
                    {
                        var piece = new GameObject("Piece_" + i);
                        piece.transform.SetParent(root, false);
                        var model = Model(EnvModels, rocks[i], piece.transform, "Chunk");
                        model.SetActive(true);
                        model.transform.localScale = new Vector3(0.45f, 0.3f, 0.55f) * (0.8f + 0.1f * i);
                        var col = piece.AddComponent<BoxCollider>();
                        col.center = new Vector3(0f, 0.25f, 0f);
                        col.size = new Vector3(0.9f, 0.5f, 1.1f) * (0.8f + 0.1f * i);
                        var fire = FxFactory.Create(FxFactory.Kind.Smoke, piece.transform, "FxFire");
                        fire.transform.localPosition = Vector3.up * 0.3f;
                        piece.SetActive(false);
                    }
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
                Build<BoostChainEvent>("boostchain", root =>
                {
                    for (int i = 0; i < BoostChainEvent.Pads; i++) Model(EventModels, "BoostPad", root, "Pad_" + i);
                }),
                Build<RoadblockEvent>("roadblock", root =>
                {
                    for (int s = 0; s < RoadblockEvent.Sections; s++)
                    {
                        var section = new GameObject("Section_" + s);
                        section.transform.SetParent(root, false);
                        var col = section.AddComponent<BoxCollider>();
                        col.enabled = false;
                        Model(EventModels, "ArrowBoard", section.transform, "Board").SetActive(true);
                        for (int i = 0; i < RoadblockEvent.BarriersPerSection; i++) Model(EventModels, "JerseyBarrier", section.transform, "Barrier_" + i).SetActive(true);
                        for (int i = 0; i < RoadblockEvent.ConesPerSection; i++) Model(EventModels, "TrafficCone", section.transform, "Cone_" + i).SetActive(true);
                        section.SetActive(false);
                        if (s > 0) CreateMarker(root, "Marker_" + s);
                    }
                    FxFactory.Create(FxFactory.Kind.Dust, root, "FxDust");
                }),
                Build<BarrageEvent>("barrage", root => FillBarrage(root, "Meteor", 1f)),
                // ---------- хаос v4 ----------
                Build<MegaBallsEvent>("megaballs", root =>
                {
                    for (int i = 0; i < MegaBallsEvent.Beach; i++) BodyChild(root, "Beach_" + i, "BeachBall", BodyKind.Beach, 1.5f, new Vector3(0f, -1.5f, 0f));
                    BodyChild(root, "Metal_0", "Ball_Metal", BodyKind.Metal, 1.2f, Vector3.zero);
                    for (int i = 0; i < MegaBallsEvent.Rubber; i++) BodyChild(root, "Rubber_" + i, "Ball_Rubber", BodyKind.Rubber, 1.3f, Vector3.zero);
                    for (int i = 0; i < MegaBallsEvent.Mini; i++)
                    {
                        var mini = BodyChild(root, "Mini_" + i, "Ball_Mini", BodyKind.Mini, 0.9f, Vector3.zero);
                        Recolor(mini.transform, "M_BallMini_Red", MiniColors[i % MiniColors.Length]);
                    }
                    Model(EventModels, "BarrelTower", root, "Tower");
                }),
                Build<MinefieldEvent>("mines", root =>
                {
                    for (int i = 0; i < MinefieldEvent.MaxMines; i++) Model(EventModels, "Mine", root, "Mine_" + i);
                }),
                Build<HammersEvent>("hammers", root =>
                {
                    for (int r = 0; r < HammersEvent.Rows; r++)
                    {
                        var row = new GameObject("Row_" + r).transform;
                        row.SetParent(root, false);
                        Model(EventModels, "GantryPillar", row, "PillarL").SetActive(true);
                        Model(EventModels, "GantryPillar", row, "PillarR").SetActive(true);
                        Model(EventModels, "GantryBeam", row, "Beam").SetActive(true);
                        for (int k = 0; k < HammersEvent.HammersPerRow; k++) Model(EventModels, "Hammer", row, "Hammer_" + k).SetActive(true);
                        row.gameObject.SetActive(false);
                    }
                }),
                Build<SpinnerEvent>("spinner", root =>
                {
                    var b = Model(EventModels, "SpinnerBase", root, "Base");
                    var post = b.AddComponent<CapsuleCollider>();
                    post.center = new Vector3(0f, 1f, 0f);
                    post.radius = 0.7f;
                    post.height = 2f;
                    post.enabled = false;
                    Model(EventModels, "SpinnerArms", root, "Arms");
                }),
                Build<RollersEvent>("rollers", root =>
                {
                    Model(EventModels, "BarrelTower", root, "Tower");
                    for (int i = 0; i < RollersEvent.Barrels; i++) BodyChild(root, "Barrel_" + i, "Barrel", BodyKind.Barrel, 0.56f, Vector3.zero, Shape.Capsule);
                    for (int i = 0; i < RollersEvent.Tnt; i++) BodyChild(root, "Tnt_" + i, "BarrelTNT", BodyKind.Barrel, 0.56f, Vector3.zero, Shape.Capsule);
                    for (int i = 0; i < RollersEvent.Dice; i++) BodyChild(root, "Dice_" + i, "Dice", BodyKind.Dice, 0.7f, Vector3.zero, Shape.Box);
                }),
                Build<MovingWallsEvent>("walls", root =>
                {
                    for (int r = 0; r < MovingWallsEvent.Rows; r++)
                    {
                        var row = new GameObject("Row_" + r).transform;
                        row.SetParent(root, false);
                        foreach (string side in new[] { "WallL", "WallR" })
                        {
                            var wall = Model(EventModels, "SlideWall", row, side);
                            wall.SetActive(true);
                            var box = wall.AddComponent<BoxCollider>();
                            box.center = new Vector3(0f, 0.65f, 0f);
                            box.size = new Vector3(4f, 1.3f, 0.8f);
                            box.enabled = false;
                            var rb = wall.AddComponent<Rigidbody>();
                            rb.isKinematic = true;
                            rb.interpolation = RigidbodyInterpolation.Interpolate;
                        }
                        row.gameObject.SetActive(false);
                    }
                }),
                Build<KickersEvent>("kickers", root =>
                {
                    for (int i = 0; i < KickersEvent.Count; i++)
                    {
                        Model(EventModels, "KickerBox", root, "Box_" + i);
                        Model(EventModels, "KickerSpring", root, "Spring_" + i);
                        Model(EventModels, "KickerGlove", root, "Glove_" + i);
                    }
                }),
                Build<BouncersEvent>("bouncers", root =>
                {
                    for (int i = 0; i < BouncersEvent.Count; i++) Model(EventModels, "Trampoline", root, "Pad_" + i);
                }),
                Build<IceEvent>("ice", root =>
                {
                    for (int i = 0; i < IceEvent.Tiles; i++) Model(EventModels, "IceTile", root, "Ice_" + i);
                    Model(EventModels, "Penguin", root, "Penguin_0");
                    Model(EventModels, "Penguin", root, "Penguin_1");
                }),
                Build<JellyEvent>("jelly", root =>
                {
                    for (int i = 0; i < JellyEvent.Blocks; i++) Model(EventModels, "JellyBlock", root, "Jelly_" + i);
                }),
                Build<ForkEvent>("fork", root =>
                {
                    for (int i = 0; i < ForkEvent.Barriers; i++)
                    {
                        var barrier = Model(EventModels, "JerseyBarrier", root, "Barrier_" + i);
                        var box = barrier.AddComponent<BoxCollider>();
                        box.center = new Vector3(0f, 0.45f, 0f);
                        box.size = new Vector3(0.84f, 0.9f, 3f);
                        box.enabled = false;
                    }
                    for (int i = 0; i < 2; i++) Model(EventModels, "ArrowBoard", root, "Board_" + i);
                    var block = new GameObject("BoardBlock");
                    block.transform.SetParent(root, false);
                    var blockCollider = block.AddComponent<BoxCollider>();
                    blockCollider.size = new Vector3(3.6f, 1.8f, 0.7f);
                    blockCollider.enabled = false;
                    for (int i = 0; i < ForkEvent.Mines; i++) Model(EventModels, "Mine", root, "Mine_" + i);
                    for (int i = 0; i < ForkEvent.Pads; i++) Model(EventModels, "BoostPad", root, "Pad_" + i);
                    Model(EventModels, "IceTile", root, "Ice_0");
                }),
                Build<BigBallEvent>("bigball", root => BodyChild(root, "Giant_0", "Ball_Giant", BodyKind.Giant, 1f, Vector3.zero)),
                Build<LowGravityEvent>("lowgrav", root =>
                {
                    for (int i = 0; i < 4; i++) Model(EventModels, "ArchPost", root, "Post_" + i);
                    for (int i = 0; i < 2; i++) Model(EventModels, "ArchBeam", root, "Beam_" + i);
                    FxFactory.Create(FxFactory.Kind.Bubbles, root, "FxBubbles");
                }),
                Build<ShrinkEvent>("shrink", root =>
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var post = Model(EventModels, "ArchPost", root, "Post_" + i);
                        if (i != 1) continue;
                        var col = post.AddComponent<CapsuleCollider>();
                        col.center = new Vector3(0f, 3.5f, 0f);
                        col.radius = 0.45f;
                        col.height = 7f;
                        col.enabled = false;
                    }
                    for (int i = 0; i < 2; i++) Model(EventModels, "ArchBeam", root, "Beam_" + i);
                    FxFactory.Create(FxFactory.Kind.Rays, root, "FxRays");
                }),
                Build<RingsEvent>("rings", root =>
                {
                    for (int i = 0; i < RingsEvent.Count; i++) Model(EventModels, "BoostRing", root, "Ring_" + i);
                }),
                // ---------- хаос v5: трюки ----------
                Build<SpringGag>("spring", root => Model(EventModels, "GagSpring", root, "Spring")),
                Build<PopMineGag>("popmine", root => Model(EventModels, "Mine", root, "Mine")),
                Build<GumGag>("gum", root => Model(EventModels, "GumPuddle", root, "Gum")),
                Build<OilGag>("oil", root => Model(EventModels, "OilSlick", root, "Oil")),
                Build<CrazyGag>("crazy", root => FxFactory.Create(FxFactory.Kind.Stars, root, "FxStars")),
                Build<HandGag>("hand", root => Model(EventModels, "GiantHand", root, "Hand")),
                Build<MalletGag>("mallet", root => Model(EventModels, "GiantMallet", root, "Mallet")),
                Build<AnvilGag>("anvil", root =>
                {
                    var anvil = Model(EventModels, "Anvil", root, "Anvil");
                    var box = anvil.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.8f, 0.2f);
                    box.size = new Vector3(1.3f, 1.6f, 3.2f);
                    box.enabled = false;
                }),
                Build<FanGag>("fan", root =>
                {
                    Model(EventModels, "GiantFan", root, "Fan");
                    FxFactory.Create(FxFactory.Kind.Wind, root, "FxWind");
                }),
                Build<MagnetGag>("magnet", root => Model(EventModels, "Magnet", root, "Magnet")),
                Build<GravFlipGag>("gravflip", root => FxFactory.Create(FxFactory.Kind.Bubbles, root, "FxBubbles")),
                Build<DucksGag>("ducks", root =>
                {
                    for (int i = 0; i < DucksGag.Count; i++) BodyChild(root, "Duck_" + i, "RubberDuck", BodyKind.Duck, 1.3f, Vector3.zero);
                }),
                Build<CannonGag>("cannon", root =>
                {
                    Model(EventModels, "Cannon", root, "Cannon");
                    BodyChild(root, "Ball_0", "Cannonball", BodyKind.Cannonball, 0.9f, Vector3.zero);
                }),
                Build<UfoGag>("ufo", root =>
                {
                    Model(EventModels, "Ufo", root, "Ufo");
                    Model(EventModels, "UfoBeam", root, "Beam");
                }),
                Build<TornadoGag>("tornado", root =>
                {
                    Model(EventModels, "TornadoFunnel", root, "Funnel");
                    FxFactory.Create(FxFactory.Kind.Dust, root, "FxDust");
                    FxFactory.Create(FxFactory.Kind.Debris, root, "FxDebris");
                }),
                // Шоу-события v6.
                Build<VerityEvent>("verity", FillVerity),
                Build<ElDriftoEvent>("eldrifto", FillElDrifto),
                Build<HypergamyEvent>("hypergamy", FillHypergamy),
                // «Тель-Авив»: тот же обстрел ракетами (подмена barrage=rocketbarrage в карте).
                Build<BarrageEvent>("rocketbarrage", root =>
                {
                    FillBarrage(root, "Rocket", 0.7f);
                    root.GetComponent<BarrageEvent>().UseSkyRockets(true);
                }),
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
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                bool marker = r.GetComponentInParent<TelegraphMarker>(true) != null;
                bool flat = r is ParticleSystemRenderer || r is LineRenderer || marker
                            || r.name == "CraterDecal" || r.name == "Puddle" || r.name == "Cracks"
                            || r.transform.parent != null && (r.transform.parent.name.StartsWith("Ice_") || r.transform.parent.name.StartsWith("Jelly_")
                                                              || r.transform.parent.name == "Oil" || r.transform.parent.name == "Beam" || r.transform.parent.name == "Funnel");
                r.shadowCastingMode = flat ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            string path = $"{PrefabDir}/Event_{id}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        /// Обстрел: снаряды с дымным следом, воронки, метки на каждый удар и несколько наборов взрывов.
        static void FillBarrage(Transform root, string projectileModel, float scale)
        {
            for (int i = 0; i < BarrageEvent.Strikes; i++)
            {
                var projectile = Model(EventModels, projectileModel, root, "Projectile_" + i);
                projectile.transform.localScale = Vector3.one * scale;
                var trail = FxFactory.Create(FxFactory.Kind.Trail, projectile.transform, "FxTrail");
                trail.transform.localPosition = new Vector3(0f, projectileModel == "Rocket" ? 3.9f : 1.8f, 0f);
                var crater = new GameObject("Crater_" + i);
                crater.transform.SetParent(root, false);
                DecalFactory.Flat("CraterDecal", crater.transform, DecalFactory.Disc, DecalFactory.Crater, Vector3.up * 0.01f, Vector3.one);
                crater.SetActive(false);
                if (i > 0) CreateMarker(root, "Marker_" + i);
            }
            for (int i = 0; i < BarrageEvent.Blasts; i++)
            {
                FxFactory.Create(FxFactory.Kind.Explosion, root, "FxExplosion_" + i);
                FxFactory.Create(FxFactory.Kind.Smoke, root, "FxSmoke_" + i);
                FxFactory.Create(FxFactory.Kind.Sparks, root, "FxSparks_" + i);
            }
        }

        /// Верити: кинематическое тело со сферой (машины о неё бьются), внутри «Pivot» (поворот к камере, сальто,
        /// сплющивание) и модель с подвижными глазами и ртом.
        static void FillVerity(Transform root)
        {
            var go = new GameObject("Verity");
            go.transform.SetParent(root, false);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            go.AddComponent<SphereCollider>().radius = VerityEvent.BallRadius * 0.97f;
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(go.transform, false);
            Model(EventModels, "Verity", pivot, "Model").SetActive(true);
            go.SetActive(false);
        }

        /// EL DRIFTO BUS: голос (запись пользователя), тело автобуса (Rigidbody 12 т, коллайдер кузова, ChaosBody),
        /// внутри — «Visual» (сплющивание при ударах) → «Lean» (крен в дрифте) → модель с колёсами; дым, искры,
        /// пыль и визг шин.
        /// ГИПЕРГАМИЯ: «Card» (место на дороге) → триггер касания (капсула по размаху карточки, кинематическое тело,
        /// слой Ignore Raycast — лучи колёс, камеры и ИИ его не видят) и «Pivot» (поворот к камере, покачивание,
        /// вырастание) → рама из Blender и картинка двумя плоскостями (лицом и спиной), блёстки.
        static void FillHypergamy(Transform root)
        {
            var ev = root.GetComponent<HypergamyEvent>();
            var card = new GameObject("Card").transform;
            card.SetParent(root, false);
            var trigger = new GameObject("Trigger");
            trigger.transform.SetParent(card, false);
            trigger.layer = 2;
            var body = trigger.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var capsule = trigger.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.direction = 1;
            capsule.radius = 1.95f;
            capsule.height = 6.2f;
            capsule.center = new Vector3(0f, 3.0f, 0f);
            trigger.AddComponent<HypergamyContact>().Configure(ev);
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(card, false);
            Model(EventModels, "HypergamyCard", pivot, "Model").SetActive(true);
            var material = HypergamyAssets.PictureMaterial();
            // Окно под картинку в раме: 3.4 × 4.02 м, низ на 0.22 м, подложка ±0.08 м. Квад Unity смотрит в −Z:
            // спина — как есть, лицо — повёрнуто на 180°; так картинка читается без зеркала с обеих сторон.
            foreach (var (name, z, yaw) in new[] { ("PictureFront", 0.085f, 180f), ("PictureBack", -0.085f, 0f) })
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.name = name;
                quad.transform.SetParent(pivot, false);
                quad.transform.localPosition = new Vector3(0f, 0.22f + 4.02f / 2f, z);
                quad.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                quad.transform.localScale = new Vector3(3.4f, 4.02f, 1f);
                var renderer = quad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var sparkle = FxFactory.Create(FxFactory.Kind.Glitter, pivot, "FxSparkle");
            sparkle.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            card.gameObject.SetActive(false);
        }

        static void FillElDrifto(Transform root)
        {
            var ev = root.GetComponent<ElDriftoEvent>();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ElDriftoClipPath);
            if (clip == null) throw new InvalidOperationException($"нет записи крика {ElDriftoClipPath}");
            ev.SetScream(clip);
            var voice = new GameObject("Voice").AddComponent<AudioSource>();
            voice.transform.SetParent(root, false);
            voice.playOnAwake = false;
            voice.loop = false;
            // Наполовину объёмный: слышно всем рядом с событием, но понятно, откуда кричат.
            voice.spatialBlend = 0.45f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 45f;
            voice.maxDistance = 320f;
            voice.dopplerLevel = 0f;
            voice.priority = 0;

            var bus = new GameObject("Bus");
            bus.transform.SetParent(root, false);
            var rb = bus.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var box = bus.AddComponent<BoxCollider>();
            // Низ коллайдера — на уровне низа колёс (колёса не «тонут» в дороге), верх — по крыше.
            box.center = new Vector3(0f, 1.66f, 0f);
            box.size = new Vector3(2.7f, 3.32f, 11.1f);
            var visual = new GameObject("Visual").transform;
            visual.SetParent(bus.transform, false);
            var lean = new GameObject("Lean").transform;
            lean.SetParent(visual, false);
            Model(EventModels, "ElDriftoBus", lean, "Model").SetActive(true);
            var body = bus.AddComponent<ChaosBody>();
            body.Configure(BodyKind.Bus, 5.5f, visual);
            bus.AddComponent<DriftBusContact>().Configure(ev);
            var smokeL = FxFactory.Create(FxFactory.Kind.TireSmoke, bus.transform, "FxSmokeL");
            smokeL.transform.localPosition = new Vector3(-1.1f, 0.25f, -2.95f);
            var smokeR = FxFactory.Create(FxFactory.Kind.TireSmoke, bus.transform, "FxSmokeR");
            smokeR.transform.localPosition = new Vector3(1.1f, 0.25f, -2.95f);
            FxFactory.Create(FxFactory.Kind.CarSparks, bus.transform, "FxSparks");
            var dust = FxFactory.Create(FxFactory.Kind.CarDust, bus.transform, "FxDust");
            dust.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            var screech = new GameObject("Screech").AddComponent<AudioSource>();
            screech.transform.SetParent(bus.transform, false);
            screech.playOnAwake = false;
            screech.loop = true;
            screech.spatialBlend = 1f;
            screech.minDistance = 14f;
            screech.maxDistance = 180f;
            screech.volume = 0f;
            bus.SetActive(false);
        }

        enum Shape { Sphere, Capsule, Box }

        static readonly string[] MiniColors = { "M_BallMini_Red", "M_BallMini_Yellow", "M_BallMini_Teal", "M_BallMini_Lime", "M_BallMini_Purple", "M_BallMini_Blue" };

        /// Физический объект события: Rigidbody + коллайдер в корне, модель внутри «Visual» (для сплющивания).
        static ChaosBody BodyChild(Transform parent, string name, string model, BodyKind kind, float radius, Vector3 modelOffset, Shape shape = Shape.Sphere)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rb = go.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            switch (shape)
            {
                case Shape.Capsule:
                    var capsule = go.AddComponent<CapsuleCollider>();
                    capsule.radius = radius;
                    capsule.height = 1.2f;
                    capsule.direction = 1;
                    break;
                case Shape.Box:
                    go.AddComponent<BoxCollider>().size = Vector3.one * radius * 2f;
                    break;
                default:
                    go.AddComponent<SphereCollider>().radius = radius;
                    break;
            }
            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            var m = Model(EventModels, model, visual, "Model");
            m.SetActive(true);
            m.transform.localPosition = modelOffset;
            var body = go.AddComponent<ChaosBody>();
            body.Configure(kind, radius, visual);
            go.SetActive(false);
            return body;
        }

        /// Заменить материал библиотеки на другой (разноцветные мячики роя).
        static void Recolor(Transform root, string from, string to)
        {
            var material = ArtLibrary.Get(to);
            if (material == null) throw new InvalidOperationException($"нет материала {to}");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name == from) mats[i] = material;
                r.sharedMaterials = mats;
            }
        }

        static void CreateMarker(Transform parent, string name = "Marker")
        {
            var go = new GameObject(name);
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
