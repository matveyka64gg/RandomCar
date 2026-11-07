using System.Collections.Generic;
using RandomCar.Car;
using RandomCar.Core;
using RandomCar.Events;
using RandomCar.Garage;
using RandomCar.Maps;
using RandomCar.Race;
using RandomCar.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using static RandomCar.EditorTools.BuilderKit;

namespace RandomCar.EditorTools
{
    /// Постоянная сцена Game: гонка, события (пул), звук, сценарий, загрузчик карт, камера, интерфейс и студия гаража.
    /// Карты грузятся в неё аддитивно.
    public static class GameSceneBuilder
    {
        /// Студия гаража стоит далеко от любой карты: её не видно с трассы, а с подиума не видно трассу.
        public static readonly Vector3 GaragePosition = new Vector3(4000f, 0f, 4000f);

        public static void Build(CarCatalog cars, MapCatalog maps, EventSettings eventSettings, List<GameObject> eventPrefabs)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var game = new GameObject("Game");
            var race = game.AddComponent<RaceManager>();
            race.Configure(cars);
            var bootstrap = game.AddComponent<GameBootstrap>();
            var loader = game.AddComponent<MapLoader>();
            loader.Configure(maps);

            // Камера: за машиной в гонке, облёт решётки в меню, подиум в гараже.
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
            follow.Configure(null, race);
            var menuCamera = camGo.AddComponent<MenuCamera>();
            menuCamera.enabled = false;
            camGo.transform.position = new Vector3(0f, 20f, -40f);

            // События: заранее подготовленные экземпляры (EventDefinition.Copies: 4 на событие, трюкам v5 — до 6),
            // выключены до розыгрыша. В «безумии» события и трюки идут каждые доли секунды и держатся, пока с ними возятся.
            var eventsRoot = Group("Events", null);
            var eventObjects = new List<RaceEvent>();
            foreach (var prefab in eventPrefabs)
            {
                string eventId = prefab.GetComponent<RaceEvent>().Id;
                var definition = eventSettings.Events.Find(d => d.Id == eventId);
                int copies = definition != null ? Mathf.Max(1, definition.Copies) : 3;
                for (int copy = 0; copy < copies; copy++)
                {
                    var ev = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    ev.name = $"{prefab.name}_{copy}";
                    ev.transform.SetParent(eventsRoot.transform, false);
                    ev.SetActive(false);
                    eventObjects.Add(ev.GetComponent<RaceEvent>());
                }
            }
            var director = game.AddComponent<EventDirector>();
            director.Configure(race, eventSettings, follow, eventObjects);
            // Хаос v4: пул взрывов, фейерверков, вспышек и звуков в мире.
            ChaosFxBuilder.Build(game.transform);
            // Бонусы: ящики «?» на трассе, ракеты, пулемёты, мины, щиты (пул).
            PowerUpBuilder.Build(game.transform, race);

            var garage = BuildGarage(cam);

            game.AddComponent<RandomCar.Audio.AudioDirector>().Configure(race, director);
            // Музыка — один проигрыватель на всю игру в постоянной сцене.
            var music = new GameObject("Music").AddComponent<RandomCar.Audio.MusicPlayer>();
            music.Configure(MusicImport.Refresh());
            var flow = game.AddComponent<GameFlow>();
            // Полигон испытаний: режим на той же гонке и событиях, камера обзора — на камере игры (выключена до обзора).
            var arenaCamera = follow.gameObject.AddComponent<RandomCar.Arena.ArenaCamera>();
            arenaCamera.enabled = false;
            var testArena = game.AddComponent<RandomCar.Arena.TestArena>();
            testArena.Configure(race, director, follow, arenaCamera);
            flow.SetArena(testArena);
            bootstrap.Configure(flow);
            var uiGo = new GameObject("UI");
            var document = uiGo.AddComponent<UnityEngine.UIElements.UIDocument>();
            document.panelSettings = UiAssets.PanelSettings();
            var gameUi = uiGo.AddComponent<GameUI>();
            gameUi.Configure(UiAssets.Style(), race, director, flow);
            flow.Configure(race, director, gameUi, follow, menuCamera, loader, garage);
            // ГИПЕРГАМИЯ: картинка на весь экран поверх интерфейса и музыка пользователя (одна на игру).
            var hyper = new GameObject("HypergamyScreen");
            hyper.AddComponent<UnityEngine.UIElements.UIDocument>().panelSettings = UiAssets.OverlayPanelSettings();
            var hyperAudio = hyper.AddComponent<AudioSource>();
            hyperAudio.playOnAwake = false;
            hyperAudio.loop = false;
            hyperAudio.spatialBlend = 0f;
            hyperAudio.priority = 0;
            hyperAudio.dopplerLevel = 0f;
            hyperAudio.ignoreListenerVolume = true;
            hyper.AddComponent<HypergamyScreen>().Configure(HypergamyAssets.Picture(), HypergamyAssets.Music(), HypergamyAssets.EdgeColor(), flow, race);
            // Сетевая игра — отдельная сборка поверх ядра; NetworkManager создаётся только при входе в сеть.
            new GameObject("Network").AddComponent<RandomCar.Net.NetSession>().Configure(flow);
            game.AddComponent<DevCommandLine>().Configure(flow);
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            EditorSceneManager.SaveScene(scene, Catalogs.GameScenePath);
            if (garageProbe != null)
            {
                string probePath = $"{GeneratedDir}/GarageReflections.exr";
                if (Lightmapping.BakeReflectionProbe(garageProbe, probePath))
                    garageProbe.bakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(probePath);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, Catalogs.GameScenePath);
            }
            Debug.Log($"[RandomCar] Сцена Game собрана: {Catalogs.GameScenePath}");
        }

        static ReflectionProbe garageProbe;

        static GarageStudio BuildGarage(Camera cam)
        {
            var root = Group("Garage", null);
            root.transform.position = GaragePosition;
            var studio = Instance("GarageStudio", root, GaragePosition, Quaternion.identity);
            var anchor = Find(studio.transform, "TurntableAnchor");
            var table = new GameObject("Turntable").transform;
            table.SetParent(root.transform, false);
            table.position = anchor != null ? anchor.position : GaragePosition + Vector3.up * 0.125f;

            // Студийный свет: ключевой, заполняющий и контровой — подчёркивают блики краски.
            Spot(root, "KeyLight", new Vector3(6f, 5.5f, 5f), new Color(1f, 0.95f, 0.88f), 9f);
            Spot(root, "FillLight", new Vector3(-6.5f, 4f, 3f), new Color(0.8f, 0.9f, 1f), 5f);
            Spot(root, "RimLight", new Vector3(0f, 5f, -7f), new Color(1f, 0.62f, 0.36f), 7f);

            var garage = root.AddComponent<GarageStudio>();
            garage.Configure(table, cam);
            SetStatic(studio);

            // Свой зонд отражений: краска и стёкла отражают студию, а не траву и небо карты.
            var probeGo = new GameObject("GarageReflections");
            probeGo.transform.SetParent(root.transform, false);
            probeGo.transform.position = GaragePosition + Vector3.up * 2f;
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
            probe.size = new Vector3(30f, 12f, 30f);
            probe.resolution = 256;
            probe.boxProjection = true;
            probe.importance = 10;
            garageProbe = probe;
            return garage;
        }

        static void Spot(GameObject root, string name, Vector3 offset, Color color, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.position = GaragePosition + offset;
            go.transform.LookAt(GaragePosition + Vector3.up * 0.6f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = 22f;
            light.spotAngle = 62f;
            light.innerSpotAngle = 30f;
            light.shadows = name == "KeyLight" ? LightShadows.Soft : LightShadows.None;
        }
    }
}
