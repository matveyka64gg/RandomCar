using System;
using System.Collections.Generic;
using System.Linq;
using RandomCar.Audio;
using RandomCar.Car;
using RandomCar.Core;
using RandomCar.Events;
using RandomCar.Race;
using UnityEngine;
using UnityEngine.UIElements;

namespace RandomCar.UI
{
    /// Весь интерфейс игры на UI Toolkit: заставка, меню, лучшие времена, настройки, HUD, пауза, результаты.
    /// Экраны собираются кодом, оформление — в Assets/UI/RandomCar.uss.
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameUI : MonoBehaviour
    {
        /// Версия игры (публичная бета). Входит в версию сетевого протокола: разные версии в одно лобби не пускаются.
        public const string Version = "0.1-beta";
        /// Как версия показывается игроку.
        public const string VersionTitle = "Beta 0.1";
        public const string BetaBanner = "RANDOMCAR — BETA VERSION";

        [SerializeField] StyleSheet style;
        [SerializeField] RaceManager race;
        [SerializeField] EventDirector director;
        [SerializeField] GameFlow flow;

        VisualElement root;
        VisualElement splash, menu, best, settings, hud, pause, results, loading, author;
        GarageScreen garage;
        MapSelectScreen mapSelect;
        MusicLibraryScreen musicLibrary;
        TouchControls touch;
        Label settingsSource, settingsTouch;
        /// Телефон (или проверка с ключом -rc-mobile-ui): крупнее интерфейс, меню в две колонки, экранные кнопки.
        public static bool MobileUi => Application.isMobilePlatform || System.Environment.GetCommandLineArgs().Contains("-rc-mobile-ui");
        ArenaScreen arenaScreen;
        Label menuMapValue, menuCarValue, menuTagline;
        VisualElement splashLogo;
        VisualElement current;
        readonly Stack<VisualElement> overlays = new Stack<VisualElement>();
        Action splashDone;
        float splashUntil;

        // HUD
        Label lapValue, timeValue, lastLapValue, positionValue, speedValue, gearValue, turboValue, rushChip, countdown, fps;
        VisualElement tachoFill, turboFill, banner, bannerFill, standingsBox, lapToast, heatBox, feedBox;
        readonly List<VisualElement> heatBars = new List<VisualElement>();
        Label heatName, waveToastText;
        VisualElement waveToast;
        float waveUntil;
        // Хаос v4: плашка комбинации и мультяшные надписи над местом события.
        VisualElement comboToast;
        Label comboToastText;
        float comboUntil;
        sealed class ComicPop
        {
            public Label Label;
            public Vector3 World;
            public float Born = -10f;
            public float Size;
        }
        readonly List<ComicPop> pops = new List<ComicPop>();
        int nextPop;
        bool chaosHooked;
        readonly List<(Label label, float until)> feed = new List<(Label, float)>();
        readonly Dictionary<RaceEvent, int> passing = new Dictionary<RaceEvent, int>();
        int lastEffects;
        Label bannerIcon, bannerText, bannerTarget, lapToastText;
        readonly List<(VisualElement row, Label pos, VisualElement swatch, Label name)> standings = new List<(VisualElement, Label, VisualElement, Label)>();
        int lastCountdown = -1;
        float goUntil;
        int lastLaps;
        float toastUntil;
        float fpsSmoothed = 60f;

        // Меню и настройки
        Button chaosButton;
        Label chaosValue, chaosNote;
        Label settingsChaos, settingsQuality, settingsFps, settingsAi, settingsAssists, settingsSteer;
        Slider volume, musicVolume, sfxVolume;
        Label settingsRepeat, settingsShuffle, settingsRockets, settingsSiren, settingsEffects, settingsMouse, settingsView, settingsViewKey, settingsItemKey;
        PowerUpHud powerUpHud;
        TextField nicknameField;

        /// В сетевой игре имя в таблице приходит от хоста (лобби): меняется в следующей гонке.
        bool flowIsNetwork => race != null && race.Racers.Any(r => r.Kind == ParticipantKind.Remote);
        UnityEngine.InputSystem.InputActionRebindingExtensions.RebindingOperation rebinding;
        VisualElement musicToast;
        Label musicToastTitle, musicToastArtist;
        float musicToastUntil;
        MusicTrack toastTrack;
        VisualElement bestRows;
        VisualElement resultsBody;

        public bool IsSplash => current == splash || current == author;

        /// Первый экран при запуске: только эта надпись, белым по чёрному (решение пользователя, ничего не добавлять).
        public const string AuthorLine = "Игра сделана специально для \"Вакханалии\" и Олега А.";
        /// Сколько секунд экран автора держится сам (пропускается любой клавишей, кнопкой, щелчком, касанием).
        public const float AuthorSeconds = 4f;

        /// Корень интерфейса: сюда добавляют свои экраны другие сборки (сетевое лобби).
        public VisualElement Root => root;

        readonly List<VisualElement> extraScreens = new List<VisualElement>();
        Button resultsAgain, pauseRestart;

        /// Экран другой сборки участвует в переключении экранов наравне со своими.
        public void AddScreen(VisualElement screen)
        {
            if (!extraScreens.Contains(screen)) extraScreens.Add(screen);
            Hide(screen);
        }

        public void ShowScreen(VisualElement screen) => Switch(screen);

        /// Игрок вводит текст (имя сборки и т. п.): горячие клавиши не перехватываются.
        public bool IsTyping => root?.focusController?.focusedElement is VisualElement f && (f is TextField || f.GetFirstAncestorOfType<TextField>() != null);

        public void Configure(StyleSheet styleSheet, RaceManager raceManager, EventDirector eventDirector, GameFlow gameFlow)
        {
            style = styleSheet;
            race = raceManager;
            director = eventDirector;
            flow = gameFlow;
        }

        void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            if (style != null) root.styleSheets.Add(style);
            root.AddToClassList("root");
            if (MobileUi) UseMobileProfile();
            BuildAuthor();
            BuildSplash();
            BuildMenu();
            BuildBest();
            BuildSettings();
            BuildHud();
            BuildPause();
            BuildResults();
            garage = new GarageScreen(root, flow.Cars, flow.Studio, flow.ConfirmCar, flow.ExitGarage, m => ShowNotice(m));
            // Вращение машины в гараже не начинается, если курсор над панелями интерфейса.
            flow.Studio.PointerOverUi = PointerOverUi;
            mapSelect = new MapSelectScreen(root, flow.Maps, key => { Back(); flow.SelectMap(key); }, () => Back());
            musicLibrary = new MusicLibraryScreen(root, () => Back(), m => ShowNotice(m));
            if (flow.Arena != null)
            {
                arenaScreen = new ArenaScreen(root, flow.Arena, () => director.Definitions, flow.Cars, () => flow.ShowMenu());
                if (flow.Arena.Observer != null) flow.Arena.Observer.PointerOverUi = PointerOverUi;
            }
            loading = Screen("loading");
            Text("ЗАГРУЗКА ТРАССЫ…", "loading-text", loading);
            foreach (var s in AllScreens()) Hide(s);
            Hide(hud);
        }

        /// Телефон: интерфейс крупнее (кнопки не меньше ~8 мм под палец, по DPI экрана), меню в две колонки.
        /// Масштаб меняется только в собранной игре (в редакторе ассет PanelSettings сохранился бы с изменением).
        void UseMobileProfile()
        {
            root.AddToClassList("mobile");
            var document = GetComponent<UIDocument>();
            if (document.panelSettings == null || Application.isEditor) return;
            var panel = document.panelSettings;
            float baseScale = Mathf.Sqrt(UnityEngine.Screen.width / 1920f * (UnityEngine.Screen.height / 1080f));
            float dpi = UnityEngine.Screen.dpi > 0f ? UnityEngine.Screen.dpi : 400f;
            float wanted = 8f / 25.4f * dpi;                      // 8 мм в точках экрана
            float button = 66f * Mathf.Max(0.1f, baseScale);       // высота кнопки меню сейчас
            panel.scale = Mathf.Clamp(wanted / button, 1f, 1.3f);
            // Проверка на ПК: -rc-ui-scale 1.3 — масштаб как на телефоне.
            var args = System.Environment.GetCommandLineArgs();
            int at = System.Array.IndexOf(args, "-rc-ui-scale");
            if (at >= 0 && at + 1 < args.Length && float.TryParse(args[at + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float forced))
                panel.scale = Mathf.Clamp(forced, 0.5f, 2f);
            Debug.Log($"[RandomCar] Интерфейс для телефона: экран {UnityEngine.Screen.width}×{UnityEngine.Screen.height}, {dpi:F0} dpi, масштаб ×{panel.scale:F2}");
        }

        // ---------- переключение экранов ----------

        void Show(VisualElement screen)
        {
            screen.RemoveFromClassList("hidden");
            screen.AddToClassList("fade-out");
            screen.schedule.Execute(() => screen.RemoveFromClassList("fade-out")).StartingIn(20);
            var panel = screen.Q(className: "panel");
            if (panel != null)
            {
                panel.RemoveFromClassList("shown");
                panel.schedule.Execute(() => panel.AddToClassList("shown")).StartingIn(30);
            }
        }

        static void Hide(VisualElement screen) => screen.AddToClassList("hidden");

        IEnumerable<VisualElement> AllScreens()
        {
            var list = new List<VisualElement> { author, splash, menu, best, settings, pause, results, loading, garage.Root, mapSelect.Root, musicLibrary.Root };
            if (arenaScreen != null) list.Add(arenaScreen.Root);
            return list.Concat(extraScreens);
        }

        void Switch(VisualElement screen, bool keepHud = false)
        {
            if (arenaScreen != null && screen != arenaScreen.Root) arenaScreen.Close();
            foreach (var s in AllScreens())
                if (s != screen) Hide(s);
            if (keepHud) Show(hud);
            else if (screen != hud) Hide(hud);
            // Экранные кнопки — только во время езды (не в паузе, результатах и меню полигона).
            if (touch != null) touch.Visible = screen == hud && GameSettings.TouchControls;
            overlays.Clear();
            current = screen;
            Show(screen);
            FocusFirst(screen);
        }

        void PushOverlay(VisualElement screen)
        {
            overlays.Push(current);
            Hide(current == hud ? pause : current);
            current = screen;
            Show(screen);
            FocusFirst(screen);
        }

        /// Esc: из вложенного экрана назад. Возвращает false, если назад некуда.
        public bool Back()
        {
            if (overlays.Count == 0) return false;
            Hide(current);
            current = overlays.Pop();
            Show(current);
            FocusFirst(current);
            AudioDirector.Instance?.PlayUiClick();
            return true;
        }

        void FocusFirst(VisualElement screen)
        {
            screen.schedule.Execute(() => screen.Q<Button>()?.Focus()).StartingIn(40);
        }

        public void ShowSplash(Action done)
        {
            splashDone = done;
            splashUntil = Time.unscaledTime + 2.6f;
            Switch(splash);
            splashLogo.RemoveFromClassList("shown");
            splashLogo.schedule.Execute(() => splashLogo.AddToClassList("shown")).StartingIn(80);
        }

        /// Экран автора при запуске игры; done — когда он закончился (сам через AuthorSeconds или пропуском).
        public void ShowAuthor(Action done)
        {
            splashDone = done;
            splashUntil = Time.unscaledTime + AuthorSeconds;
            Switch(author);
        }

        public void SkipSplash()
        {
            if ((current != splash && current != author) || splashDone == null) return;
            var done = splashDone;
            splashDone = null;
            done();
        }

        public void ShowMenu()
        {
            RefreshChaos();
            RefreshMenu();
            Switch(menu);
        }

        public void ShowLoading() => Switch(loading);

        Label notice;

        /// Короткое сообщение поверх любого экрана (ошибки сети, недоступные функции).
        public void ShowNotice(string message, float seconds = 3.5f)
        {
            if (notice == null)
            {
                notice = Text("", "notice", root);
                notice.pickingMode = PickingMode.Ignore;
            }
            notice.text = message;
            notice.AddToClassList("shown");
            notice.schedule.Execute(() => notice.RemoveFromClassList("shown")).StartingIn((long)(seconds * 1000));
        }

        /// Курсор (экранные координаты Input System, начало внизу слева) над видимым элементом интерфейса.
        public bool PointerOverUi(Vector2 screen)
        {
            if (root?.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, UnityEngine.Screen.height - screen.y));
            var picked = root.panel.Pick(p);
            return picked != null && picked != root;
        }

        /// Esc в гараже: сначала закрыть диалог/библиотеку. false — можно выходить из гаража.
        public bool GarageBack() => garage.Back();

        /// Перед выходом из гаража: сохранить сборку и снять примерку.
        public void CloseGarage() => garage.Close();

        public void ShowGarage(string carKey)
        {
            Switch(garage.Root);
            garage.Open(carKey);
        }

        /// Экран «Моя музыка» поверх текущего (меню, настройки, пауза).
        public void ShowMusicLibrary()
        {
            PushOverlay(musicLibrary.Root);
            musicLibrary.Open();
        }

        public void ShowMapSelect(string mapKey)
        {
            PushOverlay(mapSelect.Root);
            mapSelect.Open(mapKey);
        }

        void RefreshMenu()
        {
            if (menuMapValue == null) return;
            var map = flow.Maps.Get(GameSettings.Map);
            menuMapValue.text = map != null ? map.DisplayName.ToUpperInvariant() : "—";
            menuCarValue.text = flow.Cars.Get(GameSettings.Car).DisplayName.ToUpperInvariant();
            menuTagline.text = map != null ? map.Description : "";
        }

        /// Полигон испытаний: HUD без круга, времени, места, накала и бонусов (спидометр и надписи событий остаются)
        /// и меню полигона поверх.
        public void ShowArena()
        {
            if (arenaScreen == null) return;
            hud.AddToClassList("arena-hud");
            if (powerUpHud != null) powerUpHud.Hidden = true;
            Switch(arenaScreen.Root, keepHud: true);
            arenaScreen.Open();
        }

        public ArenaScreen Arena => arenaScreen;

        public void ShowHud()
        {
            hud.RemoveFromClassList("arena-hud");
            if (powerUpHud != null) powerUpHud.Hidden = false;
            Switch(hud, keepHud: true);
            lastLaps = 0;
            toastUntil = 0f;
        }

        public void ShowPause()
        {
            pauseRestart.style.display = flow.NetworkSession ? DisplayStyle.None : DisplayStyle.Flex;
            Switch(pause, keepHud: true);
        }

        public void ShowResults()
        {
            resultsAgain.text = flow.NetworkSession ? "В ЛОББИ" : "ЕЩЁ РАЗ";
            FillResults();
            Switch(results, keepHud: true);
            countdown.style.opacity = 0f;
        }

        public void ShowBest(int highlight = -1)
        {
            FillBest(highlight);
            PushOverlay(best);
        }

        public void ShowSettings()
        {
            RefreshSettings();
            PushOverlay(settings);
        }

        // ---------- построение ----------

        static VisualElement El(string cls, VisualElement parent = null) => UiKit.El(cls, parent);

        static Label Text(string text, string cls, VisualElement parent) => UiKit.Text(text, cls, parent);

        static Button MenuButton(string text, Action action, VisualElement parent, string extra = "") => UiKit.MenuButton(text, action, parent, extra);

        static Button ChoiceButton(string text, Action action, VisualElement parent, out Label value) => UiKit.ChoiceButton(text, action, parent, out value);

        VisualElement Screen(string cls)
        {
            var s = El("screen " + cls, root);
            return s;
        }

        void Logo(VisualElement parent, float size = 104f)
        {
            var row = El("logo-row", parent);
            var a = Text("RANDOM", "title title-random", row);
            var b = Text("CAR", "title title-car", row);
            a.style.fontSize = size;
            b.style.fontSize = size;
        }

        /// Чёрный экран с одной белой надписью по центру (без логотипов и подсказок); строка переносится на узких экранах.
        void BuildAuthor()
        {
            author = Screen("author");
            Text(AuthorLine, "author-text", author).pickingMode = PickingMode.Ignore;
        }

        void BuildSplash()
        {
            splash = Screen("splash");
            splashLogo = El("splash-logo", splash);
            splashLogo.style.flexDirection = FlexDirection.Column;
            splashLogo.style.alignItems = Align.Center;
            Logo(splashLogo, 128f);
            var stripes = El("speed-stripes", splashLogo);
            El("stripe stripe-a", stripes);
            El("stripe stripe-b", stripes);
            El("stripe stripe-c", stripes);
            Text("Гонка, в которой может случиться всё", "tagline", splashLogo);
            Text("Любая клавиша — пропустить", "skip-hint", splash);
        }

        void BuildMenu()
        {
            menu = Screen("menu");
            El("menu-shade", menu);
            var column = El("menu-column", menu);
            Logo(column);
            menuTagline = Text("", "tagline", column);
            var buttons = El("menu-buttons", column);
            MenuButton("ГОНКА", () => flow.StartRace(), buttons);
            ChoiceButton("ТРАССА", () => ShowMapSelect(GameSettings.Map), buttons, out menuMapValue);
            ChoiceButton("ГАРАЖ", () => flow.EnterGarage(), buttons, out menuCarValue);
            MenuButton("СЕТЕВАЯ ИГРА", () => flow.ShowNetwork(), buttons, "secondary");
            MenuButton("ПОЛИГОН ИСПЫТАНИЙ", () => flow.EnterArena(), buttons, "secondary");
            chaosButton = ChoiceButton("ХАОС", CycleChaos, buttons, out chaosValue);
            chaosNote = Text("", "choice-note", buttons);
            MenuButton("ЛУЧШИЕ ВРЕМЕНА", () => ShowBest(), buttons);
            MenuButton("МОЯ МУЗЫКА", ShowMusicLibrary, buttons, "secondary");
            MenuButton("НАСТРОЙКИ", ShowSettings, buttons);
            MenuButton("ВЫХОД", () => flow.Quit(), buttons);
            Text(MobileUi ? "Касание — выбор     «Назад» — назад" : "↑ ↓ — выбор     Enter — подтвердить     Esc — назад", "controls-hint", menu);
            // Пометка беты: заметно, в свободном углу, не перекрывает кнопки.
            var beta = El("beta-badge", menu);
            beta.pickingMode = PickingMode.Ignore;
            Text(BetaBanner, "beta-title", beta).pickingMode = PickingMode.Ignore;
            Text($"{VersionTitle} · ошибки возможны", "beta-version", beta).pickingMode = PickingMode.Ignore;
            Text($"RandomCar · {VersionTitle}", "version", menu);
            new MusicWidget(menu, full: true).Root.AddToClassList("menu-music");
        }

        void BuildBest()
        {
            best = Screen("center dim");
            var panel = El("panel", best);
            Text("ЛУЧШИЕ ВРЕМЕНА", "heading", panel);
            bestRows = El("", panel);
            MenuButton("НАЗАД", () => Back(), panel);
        }

        void BuildSettings()
        {
            settings = Screen("center dim");
            var panel = El("panel settings-panel", settings);
            Text("НАСТРОЙКИ", "heading", panel);
            // Настроек больше, чем помещается на экран: колонки листаются колесом мыши и полосой прокрутки.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("settings-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.mouseWheelScrollSize = 60f;
            panel.Add(scroll);
            var columns = El("settings-columns", scroll);

            // Левая колонка — игра, правая — звук и музыка.
            var game = El("settings-column", columns);
            Text("ИГРОК", "settings-section", game);
            // Никнейм: виден в таблице мест в гонке и у всех игроков в сетевой игре.
            var nickRow = El("setting nickname-row", game);
            Text("НИКНЕЙМ", "setting-label", nickRow);
            nicknameField = new TextField { value = GameSettings.PlayerName, maxLength = 16 };
            nicknameField.AddToClassList("nickname-field");
            nicknameField.RegisterCallback<FocusOutEvent>(_ => SaveNickname());
            nicknameField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) SaveNickname();
            });
            nickRow.Add(nicknameField);
            Text("ИГРА", "settings-section", game);
            ChoiceButton("ХАОС", () => { CycleChaos(); RefreshSettings(); }, game, out settingsChaos);
            ChoiceButton("СОПЕРНИКИ", () => { GameSettings.AiDifficulty = (GameSettings.AiDifficulty + 1) % 3; RefreshSettings(); }, game, out settingsAi);
            ChoiceButton("ПОМОЩЬ ВОДИТЕЛЮ", () => { GameSettings.Assists = (AssistLevel)(((int)GameSettings.Assists + 1) % 3); RefreshSettings(); }, game, out settingsAssists);
            ChoiceButton("РУЛЬ С КЛАВИАТУРЫ", () => { GameSettings.SteerSensitivity = NextSensitivity(GameSettings.SteerSensitivity); RefreshSettings(); }, game, out settingsSteer);
            ChoiceButton("ГРАФИКА", () => { GameSettings.Quality = GameSettings.Quality == 1 ? 0 : 1; flow.ApplyQuality(); RefreshSettings(); }, game, out settingsQuality);
            ChoiceButton("СЧЁТЧИК FPS", () => { GameSettings.ShowFps = !GameSettings.ShowFps; RefreshSettings(); }, game, out settingsFps);
            ChoiceButton("ЭКРАННЫЕ КНОПКИ", () => { GameSettings.TouchControls = !GameSettings.TouchControls; RefreshSettings(); }, game, out settingsTouch);
            Text("КАМЕРА", "settings-section", game);
            ChoiceButton("ВИД В ГОНКЕ", () => { GameSettings.CameraMode = 1 - GameSettings.CameraMode; RefreshSettings(); }, game, out settingsView);
            ChoiceButton("МЫШЬ (ОБЗОР ПКМ)", () => { GameSettings.MouseSensitivity = NextMouse(GameSettings.MouseSensitivity); RefreshSettings(); }, game, out settingsMouse);
            ChoiceButton("КЛАВИША ВИДА", StartViewRebind, game, out settingsViewKey);
            ChoiceButton("КЛАВИША БОНУСА", StartItemRebind, game, out settingsItemKey);
            Text("КАРТА «ТЕЛЬ-АВИВ»", "settings-section", game);
            ChoiceButton("РАКЕТНЫЙ ДОЖДЬ", () => { GameSettings.RocketShow = (GameSettings.RocketShow + 1) % 5; RefreshSettings(); }, game, out settingsRockets);
            ChoiceButton("СИРЕНА", () => { GameSettings.AirSiren = !GameSettings.AirSiren; RefreshSettings(); }, game, out settingsSiren);
            ChoiceButton("ЭФФЕКТЫ В НЕБЕ", () => { GameSettings.EffectsQuality = 1 - GameSettings.EffectsQuality; RefreshSettings(); }, game, out settingsEffects);

            var sound = El("settings-column", columns);
            Text("ЗВУК И МУЗЫКА", "settings-section", sound);
            volume = VolumeSlider(sound, "Общая громкость", GameSettings.Volume, v => GameSettings.Volume = v);
            musicVolume = VolumeSlider(sound, "Музыка", GameSettings.MusicVolume, v => GameSettings.MusicVolume = v);
            sfxVolume = VolumeSlider(sound, "Звуковые эффекты", GameSettings.SfxVolume, v => GameSettings.SfxVolume = v);
            ChoiceButton("ПОВТОР", () => { MusicPlayer.Instance?.CycleRepeat(); RefreshSettings(); }, sound, out settingsRepeat);
            ChoiceButton("ПЕРЕМЕШАТЬ", () => { MusicPlayer.Instance?.SetShuffle(!GameSettings.MusicShuffle); RefreshSettings(); }, sound, out settingsShuffle);
            ChoiceButton("ИСТОЧНИК МУЗЫКИ", () => { GameSettings.MusicSource = (MusicSource)(((int)GameSettings.MusicSource + 1) % 3); RefreshSettings(); }, sound, out settingsSource);
            MenuButton("МОЯ МУЗЫКА…", ShowMusicLibrary, sound, "secondary");
            Text("Музыка: B — назад, M — пауза, N — вперёд (в гонке ещё крестовина ← →).", "settings-note", sound);

            MenuButton("НАЗАД", () => Back(), panel, "settings-back");
        }

        /// Ползунок громкости с подписью в процентах.
        static Slider VolumeSlider(VisualElement panel, string label, float value, Action<float> set)
        {
            var row = El("setting volume", panel);
            var head = El("volume-head", row);
            Text(label, "setting-label", head);
            var percent = Text($"{Mathf.RoundToInt(value * 100f)}%", "volume-value", head);
            var slider = new Slider(0f, 100f) { value = value * 100f };
            slider.AddToClassList("volume-slider");
            slider.RegisterValueChangedCallback(e =>
            {
                percent.text = $"{Mathf.RoundToInt(e.newValue)}%";
                set(e.newValue / 100f);
            });
            row.Add(slider);
            return slider;
        }

        void BuildHud()
        {
            hud = Screen("hud");
            hud.pickingMode = PickingMode.Ignore;
            touch = new TouchControls(hud);
            touch.Visible = false;

            var left = El("hud-top-left", hud);
            var lapCard = El("hud-card", left);
            Text("КРУГ", "hud-label", lapCard);
            lapValue = Text("1 / 3", "hud-big", lapCard);
            var timeCard = El("hud-card", left);
            Text("ВРЕМЯ", "hud-label", timeCard);
            timeValue = Text("0:00,00", "hud-big", timeCard);
            lastLapValue = Text("", "muted", timeCard);

            var right = El("hud-top-right", hud);
            var posCard = El("hud-card", right);
            posCard.style.alignItems = Align.FlexEnd;
            Text("МЕСТО", "hud-label", posCard);
            positionValue = Text("1/4", "position-big", posCard);
            standingsBox = El("standings", right);
            for (int i = 0; i < RandomCar.Maps.MapDefinition.MaxGrid; i++)
            {
                var row = El("standing", standingsBox);
                var pos = Text($"{i + 1}", "standing-pos", row);
                var swatch = El("standing-swatch", row);
                var name = Text("", "standing-name", row);
                standings.Add((row, pos, swatch, name));
            }

            banner = El("banner", hud);
            bannerIcon = Text("!", "banner-icon", banner);
            var bannerTexts = El("", banner);
            bannerText = Text("", "banner-text", bannerTexts);
            bannerTarget = Text("", "banner-target", bannerTexts);
            var progress = El("banner-progress", banner);
            bannerFill = El("banner-progress-fill", progress);

            countdown = Text("", "countdown", hud);
            countdown.pickingMode = PickingMode.Ignore;

            var speedo = El("speedo", hud);
            rushChip = Text("РЫВОК", "rush-chip", speedo);
            var speedRow = El("speedo-row", speedo);
            var gearBox = El("gear-box", speedRow);
            Text("ПЕРЕДАЧА", "hud-label", gearBox);
            gearValue = Text("1", "gear-value", gearBox);
            var speedBlock = El("speed-block", speedRow);
            speedValue = Text("0", "speed-value", speedBlock);
            Text("КМ/Ч", "speed-unit", speedBlock);
            var tacho = El("tacho-bar", speedo);
            El("tacho-red", tacho);
            tachoFill = El("tacho-fill", tacho);
            var turboRow = El("hud-row", speedo);
            Text("ТУРБО", "turbo-label", turboRow);
            var turboBar = El("turbo-bar", turboRow);
            turboFill = El("turbo-fill", turboBar);
            turboValue = Text("0.0", "turbo-value", turboRow);

            lapToast = El("lap-toast", hud);
            lapToastText = Text("", "", lapToast);

            // Накал гонки: четыре деления и название ступени; растёт от круга к кругу.
            heatBox = El("heat", left);
            Text("НАКАЛ", "hud-label", heatBox);
            var bars = El("heat-bars", heatBox);
            for (int i = 0; i < 4; i++) heatBars.Add(El("heat-bar", bars));
            heatName = Text("", "heat-name", heatBox);

            waveToast = El("wave-toast", hud);
            waveToastText = Text("", "", waveToast);
            comboToast = El("combo-toast", hud);
            comboToastText = Text("", "", comboToast);
            comboToast.pickingMode = PickingMode.Ignore;
            pops.Clear();
            for (int i = 0; i < 10; i++)
            {
                var label = Text("", "comic-pop", hud);
                label.pickingMode = PickingMode.Ignore;
                label.style.display = DisplayStyle.None;
                pops.Add(new ComicPop { Label = label });
            }
            feedBox = El("event-feed", hud);
            feedBox.pickingMode = PickingMode.Ignore;

            fps = Text("", "fps", hud);

            // Смена трека в гонке: короткая плашка с названием (управление — клавиши B / M / N).
            musicToast = El("music-toast", hud);
            musicToast.Add(new IconElement(Icon.Note));
            var musicTexts = El("music-toast-texts", musicToast);
            musicToastTitle = Text("", "music-toast-title", musicTexts);
            musicToastArtist = Text("", "music-toast-artist", musicTexts);

            // Бонусы: значок, название, клавиша, полоска; защита после возврата; рамка цели и предупреждения.
            powerUpHud = new PowerUpHud(hud, race);
        }

        void BuildPause()
        {
            pause = Screen("center dim");
            var panel = El("panel", pause);
            Text("ПАУЗА", "heading", panel);
            new MusicWidget(pause, full: true).Root.AddToClassList("pause-music");
            MenuButton("ПРОДОЛЖИТЬ", () => flow.Resume(), panel);
            pauseRestart = MenuButton("ЗАНОВО", () => flow.RestartRace(), panel);
            MenuButton("НАСТРОЙКИ", ShowSettings, panel);
            MenuButton("В ГЛАВНОЕ МЕНЮ", () => flow.ShowMenu(), panel, "secondary");
        }

        void BuildResults()
        {
            results = Screen("center dim");
            var panel = El("panel", results);
            resultsBody = El("", panel);
            var row = El("buttons-row", panel);
            resultsAgain = MenuButton("ЕЩЁ РАЗ", () => flow.RestartRace(), row);
            MenuButton("В МЕНЮ", () => flow.ShowMenu(), row, "secondary");
        }

        // ---------- содержимое ----------

        void CycleChaos()
        {
            GameSettings.Chaos = (ChaosLevel)(((int)GameSettings.Chaos + 1) % 5);
            RefreshChaos();
        }

        void RefreshChaos()
        {
            if (chaosValue == null) return;
            chaosValue.text = ChaosName(GameSettings.Chaos);
            chaosNote.text = GameSettings.Chaos switch
            {
                ChaosLevel.Off => "События выключены: чистая гонка.",
                ChaosLevel.Many => "Все события и трюки, происшествие примерно раз в секунду.",
                ChaosLevel.Mayhem => "БЕЗУМИЕ (стандарт): все события и трюки, происшествие каждые ~0.5 с.",
                ChaosLevel.Absurd => "АБСУРД: все события и трюки, происшествие каждые ~0.2 с.",
                _ => "Все события и трюки, но редко: раз в несколько секунд.",
            };
        }

        static string ChaosName(ChaosLevel level) => level switch
        {
            ChaosLevel.Off => "ВЫКЛЮЧЕН",
            ChaosLevel.Many => "МНОГО",
            ChaosLevel.Mayhem => "БЕЗУМИЕ",
            ChaosLevel.Absurd => "АБСУРД",
            _ => "УМЕРЕННО",
        };

        static readonly float[] Sensitivities = { 0.7f, 1f, 1.3f };

        static float NextSensitivity(float current)
        {
            for (int i = 0; i < Sensitivities.Length; i++)
                if (current < Sensitivities[i] - 0.01f) return Sensitivities[i];
            return Sensitivities[0];
        }

        static readonly float[] MouseSteps = { 0.5f, 0.75f, 1f, 1.5f, 2f };

        static float NextMouse(float current)
        {
            foreach (float s in MouseSteps)
                if (current < s - 0.01f) return s;
            return MouseSteps[0];
        }

        static string KeyName(string path)
        {
            if (string.IsNullOrEmpty(path)) path = FollowCamera.DefaultKey;
            return UnityEngine.InputSystem.InputControlPath.ToHumanReadableString(path, UnityEngine.InputSystem.InputControlPath.HumanReadableStringOptions.OmitDevice).ToUpperInvariant();
        }

        /// Переназначение клавиши вида: следующая нажатая клавиша клавиатуры (Esc — отмена).
        void StartViewRebind() => StartRebind(settingsViewKey, path => GameSettings.CameraKey = path);

        /// Переназначение клавиши бонуса (по умолчанию E).
        void StartItemRebind() => StartRebind(settingsItemKey, path => GameSettings.ItemKey = path);

        void StartRebind(Label label, Action<string> set)
        {
            if (rebinding != null) return;
            label.text = "НАЖМИ КЛАВИШУ…";
            var action = new UnityEngine.InputSystem.InputAction("Rebind", UnityEngine.InputSystem.InputActionType.Button, "<Keyboard>/anyKey");
            rebinding = UnityEngine.InputSystem.InputActionRebindingExtensions.PerformInteractiveRebinding(action, 0)
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithControlsExcluding("<Mouse>")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(op =>
                {
                    set(op.selectedControl != null ? op.selectedControl.path : "");
                    FinishRebind(action);
                })
                .OnCancel(op => FinishRebind(action))
                .Start();
        }

        void FinishRebind(UnityEngine.InputSystem.InputAction action)
        {
            rebinding?.Dispose();
            rebinding = null;
            action.Dispose();
            RefreshSettings();
        }

        /// Никнейм: без управляющих символов, не длиннее 16, пустой — «Игрок».
        void SaveNickname()
        {
            if (nicknameField == null) return;
            var chars = new List<char>();
            foreach (char c in (nicknameField.value ?? "").Trim())
                if (!char.IsControl(c) && chars.Count < 16) chars.Add(c);
            string name = chars.Count > 0 ? new string(chars.ToArray()) : "Игрок";
            GameSettings.PlayerName = name;
            nicknameField.SetValueWithoutNotify(name);
            // Машина игрока уже на решётке — сразу и в таблице мест.
            if (race != null && race.Player != null && race.Player.Kind == ParticipantKind.LocalPlayer && !flowIsNetwork) race.Player.Name = name;
        }

        void RefreshSettings()
        {
            if (nicknameField != null && nicknameField.focusController?.focusedElement != nicknameField) nicknameField.SetValueWithoutNotify(GameSettings.PlayerName);
            settingsAi.text = GameSettings.AiDifficulty switch { 0 => "ЛЁГКИЕ", 2 => "СЛОЖНЫЕ", _ => "СРЕДНИЕ" };
            settingsAssists.text = GameSettings.Assists switch { AssistLevel.Off => "ВЫКЛ", AssistLevel.Strong => "СИЛЬНАЯ", _ => "СТАНДАРТ" };
            settingsSteer.text = GameSettings.SteerSensitivity < 0.85f ? "ПЛАВНЫЙ" : GameSettings.SteerSensitivity > 1.15f ? "ОСТРЫЙ" : "ОБЫЧНЫЙ";
            settingsQuality.text = GameSettings.Quality == 1 ? "ВЫСОКАЯ" : "НИЗКАЯ";
            settingsFps.text = GameSettings.ShowFps ? "ВКЛ" : "ВЫКЛ";
            settingsView.text = GameSettings.CameraMode == 1 ? "ИЗ САЛОНА" : "СЗАДИ";
            settingsMouse.text = $"{GameSettings.MouseSensitivity:0.0}×";
            if (rebinding == null)
            {
                settingsViewKey.text = KeyName(GameSettings.CameraKey);
                settingsItemKey.text = KeyName(string.IsNullOrEmpty(GameSettings.ItemKey) ? RandomCar.Car.PlayerDriver.DefaultItemKey : GameSettings.ItemKey);
            }
            settingsChaos.text = ChaosName(GameSettings.Chaos);
            settingsRepeat.text = GameSettings.MusicRepeat switch { MusicRepeat.One => "ОДИН ТРЕК", MusicRepeat.Off => "ВЫКЛ", _ => "ПЛЕЙЛИСТ" };
            settingsShuffle.text = GameSettings.MusicShuffle ? "ВКЛ" : "ВЫКЛ";
            settingsSource.text = GameSettings.MusicSource switch { MusicSource.Game => "ВСТРОЕННАЯ", MusicSource.Mine => "МОЯ", _ => "ВСЯ" };
            settingsTouch.text = GameSettings.TouchControls ? "ВКЛ" : "ВЫКЛ";
            settingsRockets.text = GameSettings.RocketShow switch { 0 => "ВЫКЛ", 1 => "РЕДКО", 2 => "СРЕДНЕ", 3 => "ЧАСТО", _ => "МАКСИМУМ" };
            settingsSiren.text = GameSettings.AirSiren ? "ВКЛ" : "ВЫКЛ";
            settingsEffects.text = GameSettings.EffectsQuality == 1 ? "ПОЛНЫЕ" : "ЛЁГКИЕ";
            volume.SetValueWithoutNotify(GameSettings.Volume * 100f);
            musicVolume.SetValueWithoutNotify(GameSettings.MusicVolume * 100f);
            sfxVolume.SetValueWithoutNotify(GameSettings.SfxVolume * 100f);
            RefreshChaos();
        }

        void FillBest(int highlight)
        {
            bestRows.Clear();
            var times = race.BestTimes;
            if (times.Count == 0)
            {
                Text("Пока пусто. Проедь первый заезд!", "times-empty", bestRows);
                return;
            }
            for (int i = 0; i < times.Count; i++)
            {
                var row = El("times-row", bestRows);
                if (i == 0) row.AddToClassList("first");
                if (i == highlight) row.AddToClassList("highlight");
                Text($"{i + 1}.", "times-rank", row);
                Text(FormatTime(times[i]), "times-value", row);
            }
        }

        void FillResults()
        {
            resultsBody.Clear();
            var player = race.Player;
            int place = race.PlayerPosition;
            Text("ФИНИШ", "hud-label", resultsBody);
            Text($"{place}-е место", "result-place", resultsBody);

            var grid = El("result-grid", resultsBody);
            Cell(grid, "ВРЕМЯ", FormatTime(player.FinishTime));
            float bestLap = player.LapTimes.Count > 0 ? player.LapTimes.Min() : 0f;
            Cell(grid, "ЛУЧШИЙ КРУГ", FormatTime(bestLap));
            Cell(grid, "РЕКОРД", race.BestTimes.Count > 0 ? FormatTime(race.BestTimes[0]) : "—");

            for (int i = 0; i < player.LapTimes.Count; i++)
                Text($"Круг {i + 1}:  {FormatTime(player.LapTimes[i])}", "result-lap", resultsBody);

            // Итоговая таблица: кто финишировал — по времени, остальные — по пройденной дистанции.
            var table = El("result-table", resultsBody);
            var order = race.Standings();
            for (int i = 0; i < order.Count; i++)
            {
                var r = order[i];
                var row = El("result-row", table);
                if (r == player) row.AddToClassList("me");
                Text($"{i + 1}", "result-row-pos", row);
                var swatch = El("standing-swatch", row);
                swatch.style.backgroundColor = r.Color;
                Text(r.Name, "result-row-name", row);
                string value = r.Retired ? "сошёл" : r.Finished ? FormatTime(r.FinishTime) : $"{Mathf.Max(0, r.Laps.Laps)}/{race.TotalLaps} круга";
                Text(value, "result-row-time", row);
            }

            var badgeHost = El("", resultsBody);
            badgeHost.style.marginTop = 16;
            if (race.BestRank == 0) Text("Новый рекорд трассы!", "badge", badgeHost);
            else if (race.BestRank > 0) Text($"Место в таблице лучших: {race.BestRank + 1}", "badge", badgeHost);
        }

        static void Cell(VisualElement parent, string label, string value)
        {
            var cell = El("result-cell", parent);
            Text(label, "hud-label", cell);
            Text(value, "hud-mid", cell);
        }

        public static string FormatTime(float seconds)
        {
            if (seconds <= 0f) return "—";
            int minutes = (int)(seconds / 60f);
            float rest = seconds - minutes * 60f;
            return $"{minutes}:{rest:00.00}";
        }

        // ---------- обновление HUD ----------

        void Update()
        {
            if ((current == splash || current == author) && splashDone != null && Time.unscaledTime > splashUntil) SkipSplash();
            arenaScreen?.Update();
            if (hud == null || hud.ClassListContains("hidden") || race == null || race.Player == null) return;
            UpdateHud();
        }

        void UpdateHud()
        {
            var player = race.Player;
            int lap = Mathf.Min(player.Laps.Laps + 1, race.TotalLaps);
            lapValue.text = $"{lap} / {race.TotalLaps}";
            timeValue.text = FormatTime(Mathf.Max(0.001f, race.RaceTime)).Replace("—", "0:00,00");
            lastLapValue.text = player.LapTimes.Count > 0 ? $"последний: {FormatTime(player.LapTimes[^1])}" : "";
            positionValue.text = $"{race.PlayerPosition}/{race.Racers.Count}";

            var order = race.Standings();
            for (int i = 0; i < standings.Count; i++)
            {
                var (row, pos, swatch, name) = standings[i];
                bool used = i < order.Count;
                row.style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used) continue;
                name.text = order[i].Retired ? $"{order[i].Name} — сошёл" : order[i].Name;
                swatch.style.backgroundColor = order[i].Color;
                row.EnableInClassList("me", order[i] == player);
            }

            UpdateGauges(player.Controller);

            UpdateCountdown();
            UpdateBanner(player);
            UpdateLapToast(player);
            UpdateHeat();
            UpdateFeed(player);
            UpdateChaos();
            powerUpHud?.Update(player);
            UpdateMusicToast();

            if (GameSettings.ShowFps && Time.unscaledDeltaTime > 0f)
            {
                fpsSmoothed = Mathf.Lerp(fpsSmoothed, 1f / Time.unscaledDeltaTime, 0.05f);
                fps.text = $"{Mathf.RoundToInt(fpsSmoothed)} FPS";
            }
            else fps.text = "";
        }

        float blowOffFlashUntil;
        Turbo watchedTurbo;

        /// Приборы по реальному состоянию машины: скорость тела, обороты, передача, давление турбины.
        void UpdateGauges(CarController car)
        {
            if (watchedTurbo != car.Turbo)
            {
                if (watchedTurbo != null) watchedTurbo.BlowOff -= OnBlowOff;
                watchedTurbo = car.Turbo;
                watchedTurbo.BlowOff += OnBlowOff;
            }
            var drivetrain = car.Drivetrain;
            speedValue.text = Mathf.RoundToInt(car.SpeedKmh).ToString();
            gearValue.text = drivetrain.Gear < 0 ? "R" : drivetrain.Gear.ToString();
            gearValue.EnableInClassList("reverse", drivetrain.Gear < 0);
            float rpm01 = drivetrain.Rpm / car.Spec.Engine.MaxRpm;
            tachoFill.style.width = Length.Percent(Mathf.Clamp01(rpm01) * 100f);
            tachoFill.EnableInClassList("high", rpm01 > 0.82f && !drivetrain.OnLimiter);
            tachoFill.EnableInClassList("limit", drivetrain.OnLimiter || rpm01 > 0.97f);
            var turbo = car.Turbo;
            turboFill.style.width = Length.Percent(Mathf.Clamp01(turbo.Boost01) * 100f);
            turboFill.EnableInClassList("full", turbo.Boost01 > 0.85f);
            turboFill.EnableInClassList("blowoff", Time.time < blowOffFlashUntil);
            turboValue.text = turbo.Present ? $"{turbo.Boost:0.0} бар" : "атмо";
            turboFill.parent.EnableInClassList("absent", !turbo.Present);
            rushChip.EnableInClassList("shown", car.IsBoosted);
        }

        void OnBlowOff(float strength) => blowOffFlashUntil = Time.time + 0.18f;

        void UpdateCountdown()
        {
            if (race.Phase == RacePhase.Countdown)
            {
                int n = Mathf.CeilToInt(race.Countdown);
                if (n != lastCountdown && n > 0)
                {
                    countdown.text = n.ToString();
                    countdown.RemoveFromClassList("go");
                    countdown.AddToClassList("pop");
                    countdown.schedule.Execute(() => countdown.RemoveFromClassList("pop")).StartingIn(120);
                }
                lastCountdown = n;
                countdown.style.opacity = 1f;
                goUntil = 0f;
            }
            else if (race.Phase == RacePhase.Running && lastCountdown > 0)
            {
                countdown.text = "СТАРТ!";
                countdown.AddToClassList("go");
                countdown.AddToClassList("pop");
                countdown.schedule.Execute(() => countdown.RemoveFromClassList("pop")).StartingIn(140);
                goUntil = Time.time + 0.9f;
                lastCountdown = 0;
            }
            else if (race.Phase == RacePhase.Finished)
            {
                // Надпись «ФИНИШ» до появления панели результатов, потом убираем.
                countdown.text = "ФИНИШ";
                countdown.AddToClassList("go");
                countdown.style.opacity = current == results ? 0f : 1f;
                return;
            }
            if (race.Phase != RacePhase.Countdown)
                countdown.style.opacity = Time.time < goUntil ? 1f : 0f;
        }

        /// Плашка ближайшего события впереди: что это и сколько до зоны. Видна всем одинаково — цели нет.
        void UpdateBanner(Racer player)
        {
            RaceEvent ev = null;
            float distance = float.MaxValue;
            if (director != null && race.Phase == RacePhase.Running) ev = director.NearestAhead(player, out distance);
            bool visible = ev != null && distance < 450f && current == hud;
            banner.EnableInClassList("shown", visible);
            if (!visible) return;
            bool good = ev.Definition.IsGood;
            banner.EnableInClassList("good", good);
            bannerIcon.text = good ? "+" : "!";
            bannerText.text = ev.Definition.Warning;
            if (distance <= 1f) bannerTarget.text = good ? "Ты в зоне — лови!" : "Ты в зоне!";
            else bannerTarget.text = $"Впереди {Mathf.RoundToInt(distance / 10f) * 10} м" + (good ? " · заезжай" : " · объезжай");
            // Предупреждение — полоса убывает до срабатывания; действие — полоса показывает, сколько осталось.
            float left = 1f - ev.StageProgress;
            bannerFill.style.width = Length.Percent(Mathf.Clamp01(left) * 100f);
            bannerFill.style.opacity = ev.Stage == EventStage.Telegraph ? 1f : 0.55f;
        }

        static readonly string[] HeatNames = { "знакомство", "разгон", "жарко", "штурм" };

        // ---------- хаос v4: комбинации и мультяшные надписи ----------

        void HookChaos()
        {
            if (chaosHooked) return;
            chaosHooked = true;
            if (director != null)
            {
                director.ComboStarted += OnCombo;
                director.FinaleStarted += OnFinale;
            }
            RandomCar.Events.ChaosFx.Popped += OnPop;
        }

        void OnDestroy()
        {
            powerUpHud?.Unhook();
            if (!chaosHooked) return;
            if (director != null)
            {
                director.ComboStarted -= OnCombo;
                director.FinaleStarted -= OnFinale;
            }
            RandomCar.Events.ChaosFx.Popped -= OnPop;
        }

        void OnCombo(string title, int count)
        {
            comboToastText.text = count >= 3 || count == 0 ? $"СЦЕНА: {title.ToUpperInvariant()}" : $"КОМБО: {title}!";
            comboToast.EnableInClassList("scene", count >= 3);
            comboUntil = Time.time + 2.8f;
        }

        void OnFinale()
        {
            comboToastText.text = director != null && director.Intensity.Wild ? "ФИНАЛ: ВСЁ И СРАЗУ!" : "ФИНАЛЬНЫЙ КРУГ — ДЕРЖИСЬ!";
            comboToast.EnableInClassList("scene", true);
            comboUntil = Time.time + 3.2f;
        }

        void OnPop(Vector3 world, string text, Color color, float size)
        {
            if (pops.Count == 0) return;
            var pop = pops[nextPop];
            nextPop = (nextPop + 1) % pops.Count;
            pop.World = world;
            pop.Born = Time.time;
            pop.Size = size;
            pop.Label.text = text;
            pop.Label.style.color = color;
            pop.Label.style.display = DisplayStyle.Flex;
        }

        /// Надписи: «выскакивают» с перелётом, всплывают и гаснут; следуют за точкой в мире.
        void UpdateChaos()
        {
            HookChaos();
            comboToast.EnableInClassList("shown", Time.time < comboUntil);
            var cam = Camera.main;
            foreach (var pop in pops)
            {
                float age = Time.time - pop.Born;
                if (age > 1.1f || cam == null || root.panel == null)
                {
                    if (pop.Label.style.display != DisplayStyle.None) pop.Label.style.display = DisplayStyle.None;
                    continue;
                }
                Vector3 screen = cam.WorldToScreenPoint(pop.World + Vector3.up * age * 1.6f);
                if (screen.z <= 0.5f)
                {
                    pop.Label.style.display = DisplayStyle.None;
                    continue;
                }
                pop.Label.style.display = DisplayStyle.Flex;
                var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, UnityEngine.Screen.height - screen.y));
                float pulse = age < 0.18f ? Mathf.Lerp(0.3f, 1.3f, age / 0.18f) : Mathf.Lerp(1.3f, 1f, Mathf.Clamp01((age - 0.18f) / 0.15f));
                float distanceScale = Mathf.Clamp(28f / Mathf.Max(screen.z, 1f), 0.45f, 1.4f);
                pop.Label.style.left = p.x;
                pop.Label.style.top = p.y;
                pop.Label.style.scale = new Scale(Vector3.one * pulse * pop.Size * distanceScale);
                pop.Label.style.opacity = Mathf.Clamp01((1.1f - age) / 0.3f);
                pop.Label.style.rotate = new Rotate(Mathf.Sin(pop.Born * 7f) * 8f);
            }
        }

        void UpdateHeat()
        {
            bool on = director != null && director.Intensity.Chaos != ChaosLevel.Off && race.Phase != RacePhase.Idle;
            heatBox.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            int stage = director.Intensity.Stage;
            for (int i = 0; i < heatBars.Count; i++) heatBars[i].EnableInClassList("on", i < stage);
            string prefix = director.Intensity.Chaos switch { ChaosLevel.Absurd => "АБСУРД · ", ChaosLevel.Mayhem => "безумие · ", _ => "" };
            heatName.text = prefix + HeatNames[Mathf.Clamp(stage - 1, 0, HeatNames.Length - 1)];
            heatBox.EnableInClassList("mayhem", director.Intensity.Wild);
            waveToast.EnableInClassList("shown", Time.time < waveUntil);
        }

        void OnWave(int lap, int stage)
        {
            waveToastText.text = lap >= race.TotalLaps
                ? "ФИНАЛЬНЫЙ КРУГ · ШТУРМ!"
                : $"КРУГ {lap} · {HeatNames[Mathf.Clamp(stage - 1, 0, HeatNames.Length - 1)].ToUpperInvariant()}!";
            waveUntil = Time.time + 2.6f;
        }

        /// Лента «что произошло»: событие подействовало на машину игрока — что именно; проехал опасность
        /// невредимым — «увернулся». Строки живут 3 с, не больше четырёх.
        void UpdateFeed(Racer player)
        {
            if (director != null && race.Course != null)
            {
                var car = player.Controller;
                if (car.EffectsReceived != lastEffects)
                {
                    lastEffects = car.EffectsReceived;
                    var source = NearestEvent(player, 30f);
                    string title = source != null ? source.Definition.Title : "Событие";
                    bool good = car.LastEffect.StartsWith("рывок") || car.LastEffect == "батут" || car.LastEffect == "невесомость" || car.LastEffect == "мини-машинка";
                    AddFeed($"{title}: {car.LastEffect}", good ? "good" : "bad");
                }
                float length = race.Course.Length;
                foreach (var ev in director.ActiveEvents)
                {
                    if (ev.Definition == null || ev.Definition.IsGood || ev.Stage == EventStage.Ending) continue;
                    float d = RandomCar.Track.TrackPath.Wrap(ev.Progress - player.Progress) * length;
                    if (d > length * 0.5f) d -= length;
                    float half = ev.ZoneLength * 0.5f;
                    if (Mathf.Abs(d) <= half && !passing.ContainsKey(ev)) passing[ev] = car.EffectsReceived;
                    else if (d < -half && passing.TryGetValue(ev, out int before))
                    {
                        if (car.EffectsReceived == before && ev.Stage == EventStage.Active) AddFeed($"Увернулся: {ev.Definition.Title}!", "dodge");
                        passing[ev] = int.MaxValue;
                    }
                }
                if (race.Phase != RacePhase.Running) passing.Clear();
            }
            for (int i = feed.Count - 1; i >= 0; i--)
            {
                if (Time.time < feed[i].until) continue;
                feed[i].label.RemoveFromHierarchy();
                feed.RemoveAt(i);
            }
        }

        RaceEvent NearestEvent(Racer player, float radius)
        {
            // Сначала — событие, в участке которого игрок сейчас (длинные участки: обстрел, перекрытие).
            float length = race.Course.Length;
            RaceEvent inside = null;
            float insideD = float.MaxValue;
            foreach (var ev in director.ActiveEvents)
            {
                float d = RandomCar.Track.TrackPath.Wrap(ev.Progress - player.Progress) * length;
                if (d > length * 0.5f) d -= length;
                float margin = Mathf.Abs(d) - ev.ZoneLength * 0.5f;
                if (margin < 6f && Mathf.Abs(d) < insideD)
                {
                    insideD = Mathf.Abs(d);
                    inside = ev;
                }
            }
            if (inside != null) return inside;
            RaceEvent best = null;
            float bestD = radius * radius;
            foreach (var ev in director.ActiveEvents)
            {
                float d = (ev.Center - player.Car.position).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = ev;
                }
            }
            return best;
        }

        void AddFeed(string text, string kind)
        {
            // Одно и то же (лужа действует, пока в ней) — не новая строка, а продление старой.
            for (int i = 0; i < feed.Count; i++)
            {
                if (feed[i].label.text != text) continue;
                feed[i] = (feed[i].label, Time.time + 3f);
                return;
            }
            if (feed.Count >= 4)
            {
                feed[0].label.RemoveFromHierarchy();
                feed.RemoveAt(0);
            }
            var label = Text(text, "feed-line " + kind, feedBox);
            feed.Add((label, Time.time + 3f));
        }

        /// Плашка нового трека: появляется при смене композиции во время гонки и через 4 с исчезает.
        void UpdateMusicToast()
        {
            var music = MusicPlayer.Instance;
            var track = music != null && music.IsPlaying ? music.Current : null;
            if (track != null && track != toastTrack)
            {
                musicToastTitle.text = track.Title;
                musicToastArtist.text = track.Artist;
                musicToastArtist.style.display = string.IsNullOrEmpty(track.Artist) ? DisplayStyle.None : DisplayStyle.Flex;
                musicToastUntil = Time.unscaledTime + 4f;
            }
            toastTrack = track;
            musicToast.EnableInClassList("shown", Time.unscaledTime < musicToastUntil);
        }

        void UpdateLapToast(Racer player)
        {
            if (player.Laps.Laps != lastLaps)
            {
                lastLaps = player.Laps.Laps;
                if (player.LapTimes.Count > 0 && lastLaps <= race.TotalLaps)
                {
                    lapToastText.text = $"Круг {lastLaps}  ·  {FormatTime(player.LapTimes[^1])}";
                    toastUntil = Time.time + 2.5f;
                    // Новый круг — накал выше (волна событий): плашка по центру, у каждого игрока по его кругу.
                    if (lastLaps < race.TotalLaps && director != null && director.Intensity.Chaos != ChaosLevel.Off)
                        OnWave(lastLaps + 1, director.Intensity.Stage);
                }
            }
            lapToast.EnableInClassList("shown", Time.time < toastUntil);
        }
    }
}
