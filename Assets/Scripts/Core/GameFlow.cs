using System;
using System.Collections;
using RandomCar.Arena;
using RandomCar.Audio;
using RandomCar.Car;
using RandomCar.Events;
using RandomCar.Garage;
using RandomCar.Maps;
using RandomCar.Race;
using RandomCar.Save;
using RandomCar.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace RandomCar.Core
{
    public enum FlowState { Splash, Loading, Menu, Garage, Racing, Paused, Results, Lobby }

    /// Сценарий игры: заставка → меню (трасса, гараж, настройки) → гонка (пауза) → результаты.
    /// Карта грузится аддитивно к сцене Game; мир карты не пересоздаётся, гонка только сбрасывается на решётку.
    public sealed class GameFlow : MonoBehaviour
    {
        [SerializeField] RaceManager race;
        [SerializeField] EventDirector director;
        [SerializeField] GameUI ui;
        [SerializeField] FollowCamera followCamera;
        [SerializeField] MenuCamera menuCamera;
        [SerializeField] MapLoader maps;
        [SerializeField] GarageStudio garage;
        [SerializeField] TestArena arena;

        public FlowState State { get; private set; } = FlowState.Splash;

        public CarCatalog Cars => race.Catalog;
        public MapCatalog Maps => maps.Catalog;
        public RaceManager Race => race;
        public EventDirector Director => director;
        public GameUI Ui => ui;

        /// Полигон испытаний (отдельный режим из главного меню).
        public TestArena Arena => arena;
        public bool InArena => arena != null && arena.Active;

        public void SetArena(TestArena testArena) => arena = testArena;

        /// Сетевой режим подменяет участников и старт (этап F); одиночная игра работает без него.
        public Func<bool> NetworkStartOverride;
        public Action NetworkMenu;

        /// Идёт сетевая сессия (лобби или гонка): пауза не останавливает время, «ещё раз» ведёт в лобби,
        /// выход в меню завершает сессию (NetworkLeave).
        public bool NetworkSession { get; set; }
        public Action NetworkLeave;
        public Action NetworkEscape;

        float splashStarted;
        bool splashDone;
        string spawnedCar;
        string spawnedBuild;

        public void Configure(RaceManager raceManager, EventDirector eventDirector, GameUI gameUi, FollowCamera follow, MenuCamera menu, MapLoader mapLoader, GarageStudio studio)
        {
            race = raceManager;
            director = eventDirector;
            ui = gameUi;
            followCamera = follow;
            menuCamera = menu;
            maps = mapLoader;
            garage = studio;
        }

        void Awake() => race.RaceFinished += OnRaceFinished;

        void OnDestroy()
        {
            if (race != null) race.RaceFinished -= OnRaceFinished;
        }

        /// Вызывается GameBootstrap при запуске сцены: заставка идёт, пока грузится карта.
        /// Свой контент игрока: папки UserContent рядом с игрой (Windows), новые треки из папки, временные копии Android.
        async void PrepareUserContent()
        {
            RandomCar.UserContent.UserContentPaths.EnsureDropFolders();
            RandomCar.UserContent.AndroidFilePicker.CleanTemp();
            string found = await RandomCar.UserContent.UserMusicLibrary.ScanDropFolderAsync();
            if (found != null) Debug.Log($"[RandomCar] Своя музыка: {found}");
        }

        public void Begin()
        {
            PrepareUserContent();
            ApplyQuality();
            State = FlowState.Splash;
            splashStarted = Time.unscaledTime;
            splashDone = false;
            UseMenuCamera(true);
            UseMapMusic(GameSettings.Map);
            // Сначала экран автора (один раз за запуск: Begin вызывается только при старте игры), затем сразу меню;
            // если карта ещё грузится — логотип RandomCar как экран загрузки.
            ui.ShowAuthor(() =>
            {
                splashDone = true;
                if (maps.Current != null) ShowMenu();
                else ui.ShowSplash(null);
            });
            LoadMap(GameSettings.Map, () =>
            {
                if (splashDone) ShowMenu();
            });
        }

        /// Загрузить карту, поставить участников одиночной игры на решётку.
        void LoadMap(string key, Action done)
        {
            maps.Load(key, map =>
            {
                if (map == null) return;
                race.Bind(map);
                SpawnSinglePlayer(force: true);
                race.PrepareGrid();
                menuCamera.Configure(map.MenuOrbitCenter, map.MenuOrbitYaw);
                done?.Invoke();
            });
        }

        /// Сеть: загрузить карту (если другая) и вызвать done, когда она готова. Участников ставит сама сеть.
        public void LoadMapForNetwork(string key, Action done)
        {
            StopAllCoroutines();
            SetPaused(false);
            if (director != null) director.CancelAll();
            garage.Exit();
            UseMapMusic(key);
            if (maps.CurrentKey == key && race.Map != null && !maps.Loading)
            {
                done?.Invoke();
                return;
            }
            State = FlowState.Loading;
            ui.ShowLoading();
            maps.Load(key, map =>
            {
                if (map == null) return;
                race.Bind(map);
                menuCamera.Configure(map.MenuOrbitCenter, map.MenuOrbitYaw);
                done?.Invoke();
            });
        }

        /// Сеть: экран лобби поверх карты (камера меню, машины стоят на решётке).
        public void EnterLobby()
        {
            StopAllCoroutines();
            SetPaused(false);
            if (director != null) director.CancelAll();
            garage.Exit();
            if (race.Map != null) race.PrepareGrid();
            UseMenuCamera(true);
            State = FlowState.Lobby;
            MusicPlayer.Instance?.SetContext(MusicContext.Menu);
        }

        /// После сетевой сессии: снова одиночные участники на текущей карте.
        public void RestoreSinglePlayer()
        {
            if (race.Map == null) return;
            SpawnSinglePlayer(force: true);
            race.PrepareGrid();
        }

        void SpawnSinglePlayer(bool force)
        {
            var entry = Cars.Get(GameSettings.Car);
            if (!GarageStore.OwnsCar(entry)) entry = Cars.Cars.Find(GarageStore.OwnsCar) ?? entry;
            var build = GarageStore.Active(entry, Cars);
            string json = build.ToJson();
            if (!force && spawnedCar == entry.Key && spawnedBuild == json && race.Racers.Count > 0) return;
            race.Spawn(RaceManager.SinglePlayer(Cars, entry.Key, build, seed: Environment.TickCount));
            spawnedCar = entry.Key;
            spawnedBuild = json;
        }

        public void ShowMenu()
        {
            if (NetworkSession && NetworkLeave != null)
            {
                // Сначала сеть закрывает сессию и возвращает одиночных участников, затем сама зовёт ShowMenu.
                NetworkLeave();
                return;
            }
            if (InArena)
            {
                ExitArena();
                return;
            }
            StopAllCoroutines();
            SetPaused(false);
            if (director != null) director.CancelAll();
            garage.Exit();
            if (race.Map != null) race.PrepareGrid();
            UseMenuCamera(true);
            State = FlowState.Menu;
            MusicPlayer.Instance?.SetContext(MusicContext.Menu);
            ui.ShowMenu();
        }

        /// Плейлист выбранной карты (у «Тель-Авива» свой), переход между плейлистами плавный.
        void UseMapMusic(string mapKey)
        {
            var entry = Maps.Get(mapKey);
            MusicPlayer.Instance?.UsePlaylist(entry != null ? entry.Playlist : null);
        }

        public void SelectMap(string key)
        {
            GameSettings.Map = Maps.Get(key).Key;
            if (maps.CurrentKey == GameSettings.Map) return;
            UseMapMusic(GameSettings.Map);
            State = FlowState.Loading;
            ui.ShowLoading();
            LoadMap(GameSettings.Map, ShowMenu);
        }

        // ---------- полигон испытаний ----------

        /// Отдельный режим: своя карта, своя машина, события по кнопке. Гонка, её таймер, результаты, рекорды и
        /// кредиты не участвуют (TestArena). Выход — «В главное меню» или «Выход»: возвращается трасса из настроек.
        public void EnterArena()
        {
            if (maps.Loading || arena == null || NetworkSession) return;
            string key = Maps.ArenaKey;
            if (string.IsNullOrEmpty(key))
            {
                ui.ShowNotice("Полигон испытаний не собран (RandomCar/Build All).");
                return;
            }
            StopAllCoroutines();
            SetPaused(false);
            if (director != null) director.CancelAll();
            garage.Exit();
            State = FlowState.Loading;
            ui.ShowLoading();
            UseMapMusic(key);
            maps.Load(key, map =>
            {
                if (map == null)
                {
                    LoadMap(GameSettings.Map, ShowMenu);
                    return;
                }
                race.Bind(map);
                arena.Begin();
                menuCamera.Configure(map.MenuOrbitCenter, map.MenuOrbitYaw);
                UseMenuCamera(false);
                State = FlowState.Racing;
                MusicPlayer.Instance?.SetContext(MusicContext.Race);
                race.Restart();
                arena.AfterRestart();
                ui.ShowArena();
            });
        }

        void ExitArena()
        {
            // Сначала остановить гонку: пока карта полигона выгружается, трассы нет — ни сторож, ни поток событий
            // (он снова включён) не должны работать по пустой трассе.
            race.Stop();
            arena.End();
            StopAllCoroutines();
            SetPaused(false);
            State = FlowState.Loading;
            ui.ShowLoading();
            UseMapMusic(GameSettings.Map);
            LoadMap(GameSettings.Map, ShowMenu);
        }

        // ---------- гараж ----------

        public void EnterGarage()
        {
            State = FlowState.Garage;
            menuCamera.enabled = false;
            followCamera.enabled = false;
            garage.Enter();
            MusicPlayer.Instance?.SetContext(MusicContext.Garage);
            ui.ShowGarage(GameSettings.Car);
        }

        public void PreviewCar(CarEntry car, CarBuild build) => garage.Refresh(car, build, Cars);

        public GarageStudio Studio => garage;

        /// Выбрать машину (активная сборка уже сохранена гаражом) и вернуться в меню.
        public void ConfirmCar(CarEntry car)
        {
            GameSettings.Car = car.Key;
            SpawnSinglePlayer(force: false);
            ExitGarage();
        }

        public void ExitGarage()
        {
            garage.Exit();
            ShowMenu();
        }

        public void ShowNetwork()
        {
            if (NetworkMenu != null) NetworkMenu();
            else ui.ShowNotice("Сетевая игра появится после установки сетевого пакета.");
        }

        // ---------- гонка ----------

        public void StartRace()
        {
            if (maps.Loading || race.Map == null) return;
            if (NetworkStartOverride != null && NetworkStartOverride()) return;
            StopAllCoroutines();
            SetPaused(false);
            if (maps.CurrentKey != GameSettings.Map)
            {
                LoadMap(GameSettings.Map, StartRace);
                return;
            }
            SpawnSinglePlayer(force: false);
            BeginRacing();
        }

        /// Гонка на уже поставленных участниках (одиночная игра и сеть).
        public void BeginRacing()
        {
            garage.Exit();
            UseMenuCamera(false);
            State = FlowState.Racing;
            MusicPlayer.Instance?.SetContext(MusicContext.Race);
            ui.ShowHud();
            race.Restart();
        }

        public void RestartRace()
        {
            if (InArena)
            {
                arena.ResetAll();
                return;
            }
            if (NetworkSession)
            {
                NetworkMenu?.Invoke();
                return;
            }
            StartRace();
        }

        public void Pause()
        {
            if (State != FlowState.Racing) return;
            TouchInput.ReleaseAll();
            State = FlowState.Paused;
            // В сети время не останавливается: остальные продолжают гонку.
            if (!NetworkSession) SetPaused(true);
            ui.ShowPause();
        }

        public void Resume()
        {
            if (State != FlowState.Paused) return;
            State = FlowState.Racing;
            SetPaused(false);
            if (InArena) ui.ShowArena();
            else ui.ShowHud();
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// Качество: «высокая» — PC-профиль URP с постобработкой, «низкая» — мобильный профиль без неё.
        public void ApplyQuality()
        {
            bool high = GameSettings.Quality == 1;
            int level = Mathf.Clamp(high ? QualitySettings.names.Length - 1 : 0, 0, QualitySettings.names.Length - 1);
            QualitySettings.SetQualityLevel(level, true);
            var cam = Camera.main;
            // На телефоне постобработка (тон, цвет, свечение) остаётся и при экономной графике: без неё карты теряют свой вид.
            if (cam != null && cam.TryGetComponent<UniversalAdditionalCameraData>(out var data)) data.renderPostProcessing = high || Application.isMobilePlatform;
        }

        void SetPaused(bool paused)
        {
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
        }

        void UseMenuCamera(bool menu)
        {
            if (menuCamera != null) menuCamera.enabled = menu;
            if (followCamera != null) followCamera.enabled = !menu;
        }

        void OnRaceFinished()
        {
            if (State != FlowState.Racing) return;
            StartCoroutine(ShowResultsSoon());
        }

        IEnumerator ShowResultsSoon()
        {
            yield return new WaitForSeconds(1.6f);
            State = FlowState.Results;
            MusicPlayer.Instance?.SetContext(MusicContext.Menu);
            ui.ShowResults();
        }

        /// Музыка с клавиатуры в любом экране: B — назад, M — пауза, N — вперёд; в гонке ещё крестовина ← →.
        static void MusicHotkeys(Keyboard keyboard, bool racing)
        {
            var music = MusicPlayer.Instance;
            if (music == null) return;
            // Крестовина в меню листает интерфейс, поэтому музыкой управляет только в гонке.
            var pad = racing ? Gamepad.current : null;
            if ((keyboard != null && keyboard.nKey.wasPressedThisFrame) || (pad != null && pad.dpad.right.wasPressedThisFrame)) music.Next();
            else if ((keyboard != null && keyboard.bKey.wasPressedThisFrame) || (pad != null && pad.dpad.left.wasPressedThisFrame)) music.Previous();
            else if (keyboard != null && keyboard.mKey.wasPressedThisFrame) music.TogglePause();
        }

        void Update()
        {
            if (followCamera != null) followCamera.AllowInput = State == FlowState.Racing && !ui.IsTyping;
            var keyboard = Keyboard.current;
            // Пропуск заставки: клавиша, щелчок, любая кнопка геймпада или касание экрана (будущая версия для телефонов).
            bool anyPressed = (keyboard != null && keyboard.anyKey.wasPressedThisFrame) ||
                              (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                              (Gamepad.current != null && (Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.startButton.wasPressedThisFrame)) ||
                              (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame);
            bool escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            var gamepad = Gamepad.current;
            bool padStart = gamepad != null && gamepad.startButton.wasPressedThisFrame;
            // B на геймпаде — «назад» в меню (в гонке B — бонус, поэтому только вне гонки).
            bool padBack = gamepad != null && gamepad.buttonEast.wasPressedThisFrame && State != FlowState.Racing;
            if (padBack && (State == FlowState.Menu || State == FlowState.Garage || State == FlowState.Lobby)) escape = true;
            if (State != FlowState.Splash && !ui.IsTyping) MusicHotkeys(keyboard, State == FlowState.Racing);
            switch (State)
            {
                case FlowState.Splash:
                    // Первые полсекунды игнорируем: при старте Input System присылает «нажатия» от синхронизации устройств.
                    if (anyPressed && Time.unscaledTime - splashStarted > 0.5f) ui.SkipSplash();
                    break;
                case FlowState.Menu:
                    if (escape) ui.Back();
                    break;
                case FlowState.Garage:
                    if (escape && !ui.IsTyping && !ui.GarageBack())
                    {
                        ui.CloseGarage();
                        ExitGarage();
                    }
                    break;
                case FlowState.Racing:
                    // Пауза: Esc/P, Start на геймпаде, кнопка «II» на экране (телефон; «Назад» Android приходит как Esc).
                    if (TouchInput.ConsumePause() || padStart)
                    {
                        Pause();
                        break;
                    }
                    if (keyboard == null) break;
                    if (escape || keyboard.pKey.wasPressedThisFrame) Pause();
                    else if (keyboard.rKey.wasPressedThisFrame && !NetworkSession) RestartRace();
                    break;
                case FlowState.Paused:
                    if (padStart) Resume();
                    else if ((escape || padBack) && !ui.Back()) Resume();
                    break;
                case FlowState.Lobby:
                    if (escape && !ui.Back()) NetworkEscape?.Invoke();
                    break;
            }
        }
    }
}
