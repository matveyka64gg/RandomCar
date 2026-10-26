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
        public const string Version = "0.3";

        [SerializeField] StyleSheet style;
        [SerializeField] RaceManager race;
        [SerializeField] EventDirector director;
        [SerializeField] GameFlow flow;

        VisualElement root;
        VisualElement splash, menu, best, settings, hud, pause, results;
        VisualElement splashLogo;
        VisualElement current;
        readonly Stack<VisualElement> overlays = new Stack<VisualElement>();
        Action splashDone;
        float splashUntil;

        // HUD
        Label lapValue, timeValue, lastLapValue, positionValue, speedValue, countdown, fps;
        VisualElement speedFill, banner, bannerFill, standingsBox, lapToast;
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
        Label settingsChaos, settingsQuality, settingsFps;
        Slider volume;
        VisualElement bestRows;
        VisualElement resultsBody;

        public bool IsSplash => current == splash;

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
            BuildSplash();
            BuildMenu();
            BuildBest();
            BuildSettings();
            BuildHud();
            BuildPause();
            BuildResults();
            foreach (var s in new[] { splash, menu, best, settings, hud, pause, results }) Hide(s);
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

        void Switch(VisualElement screen, bool keepHud = false)
        {
            foreach (var s in new[] { splash, menu, best, settings, pause, results })
                if (s != screen) Hide(s);
            if (keepHud) Show(hud);
            else if (screen != hud) Hide(hud);
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

        public void SkipSplash()
        {
            if (current != splash || splashDone == null) return;
            var done = splashDone;
            splashDone = null;
            done();
        }

        public void ShowMenu()
        {
            RefreshChaos();
            Switch(menu);
        }

        public void ShowHud()
        {
            Switch(hud, keepHud: true);
            lastLaps = 0;
            toastUntil = 0f;
        }

        public void ShowPause()
        {
            Switch(pause, keepHud: true);
        }

        public void ShowResults()
        {
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

        static VisualElement El(string cls, VisualElement parent = null)
        {
            var e = new VisualElement();
            foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries)) e.AddToClassList(c);
            parent?.Add(e);
            return e;
        }

        static Label Text(string text, string cls, VisualElement parent)
        {
            var l = new Label(text);
            foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries)) l.AddToClassList(c);
            parent.Add(l);
            return l;
        }

        Button MenuButton(string text, Action action, VisualElement parent, string extra = "")
        {
            var b = new Button(() =>
            {
                AudioDirector.Instance?.PlayUiClick();
                action();
            }) { text = text };
            b.AddToClassList("menu-button");
            if (extra.Length > 0) b.AddToClassList(extra);
            b.RegisterCallback<FocusInEvent>(_ => AudioDirector.Instance?.PlayUiMove());
            b.RegisterCallback<PointerEnterEvent>(_ => b.Focus());
            parent.Add(b);
            return b;
        }

        Button ChoiceButton(string text, Action action, VisualElement parent, out Label value)
        {
            var b = MenuButton("", action, parent, "secondary");
            b.AddToClassList("choice");
            Text(text, "", b);
            value = Text("", "choice-value", b);
            return b;
        }

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
            Text("Солнечный автодром «Холмы Рандома»", "tagline", column);
            var buttons = El("menu-buttons", column);
            MenuButton("ИГРАТЬ", () => flow.StartRace(), buttons);
            chaosButton = ChoiceButton("ХАОС", CycleChaos, buttons, out chaosValue);
            chaosNote = Text("", "choice-note", buttons);
            MenuButton("ЛУЧШИЕ ВРЕМЕНА", () => ShowBest(), buttons);
            MenuButton("НАСТРОЙКИ", ShowSettings, buttons);
            MenuButton("ВЫХОД", () => flow.Quit(), buttons);
            Text("↑ ↓ — выбор     Enter — подтвердить     Esc — назад", "controls-hint", menu);
            Text($"RandomCar · версия {Version}", "version", menu);
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
            var panel = El("panel", settings);
            Text("НАСТРОЙКИ", "heading", panel);

            var volumeRow = El("setting", panel);
            Text("Громкость", "setting-label", volumeRow);
            volume = new Slider(0f, 100f) { value = GameSettings.Volume * 100f };
            volume.RegisterValueChangedCallback(e => GameSettings.Volume = e.newValue / 100f);
            volumeRow.Add(volume);

            ChoiceButton("ГРАФИКА", () => { GameSettings.Quality = GameSettings.Quality == 1 ? 0 : 1; flow.ApplyQuality(); RefreshSettings(); }, panel, out settingsQuality);
            ChoiceButton("СЧЁТЧИК FPS", () => { GameSettings.ShowFps = !GameSettings.ShowFps; RefreshSettings(); }, panel, out settingsFps);
            ChoiceButton("ХАОС", () => { CycleChaos(); RefreshSettings(); }, panel, out settingsChaos);
            MenuButton("НАЗАД", () => Back(), panel);
        }

        void BuildHud()
        {
            hud = Screen("hud");
            hud.pickingMode = PickingMode.Ignore;

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
            for (int i = 0; i < 4; i++)
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
            speedValue = Text("0", "speed-value", speedo);
            Text("км/ч", "speed-unit", speedo);
            var bar = El("speed-bar", speedo);
            speedFill = El("speed-fill", bar);

            lapToast = El("lap-toast", hud);
            lapToastText = Text("", "", lapToast);

            fps = Text("", "fps", hud);
        }

        void BuildPause()
        {
            pause = Screen("center dim");
            var panel = El("panel", pause);
            Text("ПАУЗА", "heading", panel);
            MenuButton("ПРОДОЛЖИТЬ", () => flow.Resume(), panel);
            MenuButton("ЗАНОВО", () => flow.RestartRace(), panel);
            MenuButton("НАСТРОЙКИ", ShowSettings, panel);
            MenuButton("В ГЛАВНОЕ МЕНЮ", () => flow.ShowMenu(), panel, "secondary");
        }

        void BuildResults()
        {
            results = Screen("center dim");
            var panel = El("panel", results);
            resultsBody = El("", panel);
            var row = El("buttons-row", panel);
            MenuButton("ЕЩЁ РАЗ", () => flow.RestartRace(), row);
            MenuButton("В МЕНЮ", () => flow.ShowMenu(), row, "secondary");
        }

        // ---------- содержимое ----------

        void CycleChaos()
        {
            GameSettings.Chaos = (ChaosLevel)(((int)GameSettings.Chaos + 1) % 3);
            RefreshChaos();
        }

        void RefreshChaos()
        {
            if (chaosValue == null) return;
            chaosValue.text = ChaosName(GameSettings.Chaos);
            chaosNote.text = GameSettings.Chaos switch
            {
                ChaosLevel.Off => "События выключены: чистая гонка.",
                ChaosLevel.Many => "Событие примерно каждые 8–12 секунд.",
                _ => "Событие примерно каждые 20–30 секунд.",
            };
        }

        static string ChaosName(ChaosLevel level) => level switch
        {
            ChaosLevel.Off => "ВЫКЛЮЧЕН",
            ChaosLevel.Many => "МНОГО",
            _ => "УМЕРЕННО",
        };

        void RefreshSettings()
        {
            settingsQuality.text = GameSettings.Quality == 1 ? "ВЫСОКАЯ" : "НИЗКАЯ";
            settingsFps.text = GameSettings.ShowFps ? "ВКЛ" : "ВЫКЛ";
            settingsChaos.text = ChaosName(GameSettings.Chaos);
            volume.SetValueWithoutNotify(GameSettings.Volume * 100f);
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

        static readonly Dictionary<string, Color> CarColors = new Dictionary<string, Color>
        {
            { "Искра", new Color(1f, 0.29f, 0.17f) },
            { "Комета", new Color(0.18f, 0.42f, 1f) },
            { "Пчёлка", new Color(1f, 0.76f, 0.1f) },
            { "Мята", new Color(0.18f, 0.83f, 0.63f) },
        };

        void Update()
        {
            if (current == splash && splashDone != null && Time.unscaledTime > splashUntil) SkipSplash();
            if (hud == null || hud.ClassListContains("hidden") || race == null || race.Player == null) return;
            UpdateHud();
        }

        void UpdateHud()
        {
            var player = race.Player;
            int lap = Mathf.Min(player.Laps.Laps + 1, RaceManager.TotalLaps);
            lapValue.text = $"{lap} / {RaceManager.TotalLaps}";
            timeValue.text = FormatTime(Mathf.Max(0.001f, race.RaceTime)).Replace("—", "0:00,00");
            lastLapValue.text = player.LapTimes.Count > 0 ? $"последний: {FormatTime(player.LapTimes[^1])}" : "";
            positionValue.text = $"{race.PlayerPosition}/{race.Racers.Count}";

            var order = race.Racers.OrderByDescending(r => r.Score).ToList();
            for (int i = 0; i < standings.Count && i < order.Count; i++)
            {
                var (row, pos, swatch, name) = standings[i];
                name.text = order[i].Name;
                swatch.style.backgroundColor = CarColors.TryGetValue(order[i].Name, out var c) ? c : Color.white;
                row.EnableInClassList("me", order[i] == player);
            }

            float kmh = Mathf.Abs(player.Controller.Speed) * 3.6f;
            speedValue.text = Mathf.RoundToInt(kmh).ToString();
            speedFill.style.width = Length.Percent(Mathf.Clamp01(kmh / 130f) * 100f);
            speedFill.EnableInClassList("boost", player.Controller.IsBoosted);

            UpdateCountdown();
            UpdateBanner(player);
            UpdateLapToast(player);

            if (GameSettings.ShowFps && Time.unscaledDeltaTime > 0f)
            {
                fpsSmoothed = Mathf.Lerp(fpsSmoothed, 1f / Time.unscaledDeltaTime, 0.05f);
                fps.text = $"{Mathf.RoundToInt(fpsSmoothed)} FPS";
            }
            else fps.text = "";
        }

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

        void UpdateBanner(Racer player)
        {
            var ev = director != null ? director.Active : null;
            bool visible = ev != null && ev.Stage != EventStage.Ending && race.Phase == RacePhase.Running;
            banner.EnableInClassList("shown", visible);
            if (!visible) return;
            bool good = ev.Definition.IsGood;
            banner.EnableInClassList("good", good);
            bannerIcon.text = good ? "+" : "!";
            bannerText.text = ev.Definition.Warning;
            bannerTarget.text = ev.Target == player ? (good ? "Это для тебя!" : "Цель — ты! Уходи с полосы") : $"Цель — {ev.Target.Name}";
            float left = ev.Stage == EventStage.Telegraph ? 1f - ev.StageProgress : 0f;
            bannerFill.style.width = Length.Percent(left * 100f);
        }

        void UpdateLapToast(Racer player)
        {
            if (player.Laps.Laps != lastLaps)
            {
                lastLaps = player.Laps.Laps;
                if (player.LapTimes.Count > 0 && lastLaps <= RaceManager.TotalLaps)
                {
                    lapToastText.text = $"Круг {lastLaps}  ·  {FormatTime(player.LapTimes[^1])}";
                    toastUntil = Time.time + 2.5f;
                }
            }
            lapToast.EnableInClassList("shown", Time.time < toastUntil);
        }
    }
}
