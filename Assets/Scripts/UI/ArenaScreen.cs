using System;
using System.Collections.Generic;
using System.Linq;
using RandomCar.Arena;
using RandomCar.Audio;
using RandomCar.Car;
using RandomCar.Events;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static RandomCar.UI.UiKit;

namespace RandomCar.UI
{
    /// Интерфейс полигона испытаний поверх HUD: верхняя панель (меню, повтор, стоп, сброс, камера, неуязвимость, выход),
    /// строка состояния испытания и меню слева — все события игры по разделам с описанием и кнопками «Запустить»,
    /// «Повторить», «Стоп», выбор машины, мишени, режим. Пока меню открыто, машина стоит; закрыто — едет.
    /// Управление не только мышью: всё нажимается пальцем, в меню работают стрелки/крестовина и Enter/A, есть клавиши
    /// и кнопки геймпада (Input System). Размеры — в долях экрана, панель масштабируется под разрешение.
    public sealed class ArenaScreen
    {
        public VisualElement Root { get; }

        readonly TestArena arena;
        readonly Func<IReadOnlyList<EventDefinition>> definitions;
        readonly CarCatalog cars;
        readonly Action exit;

        readonly VisualElement panel, eventsPage, carsPage, list, details, carGrid;
        readonly Label statusTitle, statusStage, statusHint, detailTitle, detailTag, detailText, detailHint, carNote, modeNote;
        readonly Button menuButton, repeatButton, stopButton, cameraButton, modeButton, launchButton, detailRepeat, detailStop, actionButton,
                        eventsTab, carsTab, targetsButton, invulnerableButton, vulnerableButton;
        readonly List<(Button button, ArenaCategory category)> filters = new List<(Button, ArenaCategory)>();
        readonly List<(Button row, EventDefinition def)> rows = new List<(Button, EventDefinition)>();
        readonly List<(Button button, string key)> carButtons = new List<(Button, string)>();
        ArenaCategory filter = ArenaCategory.All;
        EventDefinition selected;
        bool showCars;
        bool built;
        InputAction toggleMenu, launch, stop, reset, camera;
        string lastStatus = "";
        int seenLaunches;

        public ArenaScreen(VisualElement parent, TestArena testArena, Func<IReadOnlyList<EventDefinition>> eventDefinitions, CarCatalog catalog, Action onExit)
        {
            arena = testArena;
            definitions = eventDefinitions;
            cars = catalog;
            exit = onExit;
            Root = El("screen arena", parent);
            Root.pickingMode = PickingMode.Ignore;

            // Верхняя панель: кнопки без фокуса (пробел и Enter остаются машине).
            var bar = El("arena-bar", Root);
            menuButton = BarButton("МЕНЮ", ToggleMenu, bar, "arena-accent");
            repeatButton = BarButton("ПОВТОРИТЬ", () => Run(true), bar);
            stopButton = BarButton("СТОП", () => arena.StopDemo(), bar);
            BarButton("СБРОС", () => arena.ResetAll(), bar);
            cameraButton = BarButton("", () => arena.SetObserver(!arena.Observing), bar);
            modeButton = BarButton("", () => arena.SetInvulnerable(!arena.Invulnerable), bar);
            El("arena-spacer", bar).pickingMode = PickingMode.Ignore;
            BarButton("ВЫХОД", () => exit(), bar, "arena-exit");

            // Состояние испытания.
            var status = El("arena-status", Root);
            status.pickingMode = PickingMode.Ignore;
            statusTitle = Text("Выбери событие в меню", "arena-status-title", status);
            statusStage = Text("", "arena-status-stage", status);
            statusHint = Text("", "arena-status-hint", status);

            // Меню слева.
            panel = El("arena-panel", Root);
            var head = El("arena-panel-head", panel);
            Text("ПОЛИГОН ИСПЫТАНИЙ", "arena-heading", head);
            PanelButton("ЕХАТЬ", () => SetMenu(false), head, "arena-drive");
            var tabs = El("arena-tabs", panel);
            eventsTab = PanelButton("СОБЫТИЯ", () => ShowCars(false), tabs, "arena-tab");
            carsTab = PanelButton("МАШИНА И РЕЖИМ", () => ShowCars(true), tabs, "arena-tab");

            eventsPage = El("arena-page", panel);
            var chips = El("arena-filters", eventsPage);
            foreach (ArenaCategory c in Enum.GetValues(typeof(ArenaCategory)))
            {
                var category = c;
                filters.Add((PanelButton(ArenaCatalog.CategoryName(c), () => SetFilter(category), chips, "arena-chip"), c));
            }
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("arena-list");
            eventsPage.Add(scroll);
            list = scroll.contentContainer;
            details = El("arena-details", eventsPage);
            detailTitle = Text("", "arena-detail-title", details);
            detailTag = Text("", "arena-detail-tag", details);
            detailText = Text("", "arena-detail-text", details);
            detailHint = Text("", "arena-detail-hint", details);
            var actions = El("arena-actions", details);
            launchButton = PanelButton("ЗАПУСТИТЬ", () => Run(false), actions, "arena-go");
            detailRepeat = PanelButton("ПОВТОРИТЬ", () => Run(true), actions);
            detailStop = PanelButton("СТОП", () => arena.StopDemo(), actions);
            actionButton = PanelButton("", ExtraAction, actions, "arena-extra");

            carsPage = El("arena-page", panel);
            Text("МАШИНА", "arena-subheading", carsPage);
            var carScroll = new ScrollView(ScrollViewMode.Vertical);
            carScroll.AddToClassList("arena-cars");
            carsPage.Add(carScroll);
            carGrid = carScroll.contentContainer;
            carGrid.AddToClassList("arena-car-grid");
            carNote = Text("", "arena-note", carsPage);
            Text("РЕЖИМ МАШИНЫ", "arena-subheading", carsPage);
            var modes = El("arena-row", carsPage);
            invulnerableButton = PanelButton("НЕУЯЗВИМОСТЬ", () => arena.SetInvulnerable(true), modes, "arena-toggle");
            vulnerableButton = PanelButton("УЯЗВИМОСТЬ", () => arena.SetInvulnerable(false), modes, "arena-toggle");
            modeNote = Text("", "arena-note", carsPage);
            Text("МАШИНЫ-МИШЕНИ", "arena-subheading", carsPage);
            targetsButton = PanelButton("", () => arena.SetTargets(!arena.Targets), carsPage, "arena-toggle wide");
            Text("Три машины без водителя стоят в зоне испытаний: в них врезается автобус, их подбрасывают мины и волны.", "arena-note", carsPage);

            Text("Tab / Select — меню     F / X(геймпад) — запустить или повторить     X / L3 — стоп     Backspace — сброс     V / R3 — камера     Esc — пауза",
                 "arena-keys", Root).pickingMode = PickingMode.Ignore;

            arena.Changed += Refresh;
            CreateInput();
        }

        // ---------- кнопки ----------

        static Button Make(string text, Action action, VisualElement parent, string cls)
        {
            var b = new Button(() =>
            {
                AudioDirector.Instance?.PlayUiClick();
                action();
            }) { text = text };
            foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries)) b.AddToClassList(c);
            parent.Add(b);
            return b;
        }

        static Button BarButton(string text, Action action, VisualElement parent, string extra = "")
        {
            var b = Make(text, action, parent, "arena-button " + extra);
            b.focusable = false;
            return b;
        }

        static Button PanelButton(string text, Action action, VisualElement parent, string extra = "") =>
            Make(text, action, parent, "arena-button " + extra);

        // ---------- открытие ----------

        /// Экран показан (вход на полигон или возврат с паузы).
        public void Open()
        {
            if (!built) Build();
            EnableInput(true);
            SetMenu(arena.MenuOpen);
            Refresh();
        }

        public void Close()
        {
            EnableInput(false);
        }

        void Build()
        {
            built = true;
            list.Clear();
            rows.Clear();
            foreach (var d in definitions().OrderBy(d => ArenaCatalog.Category(d)).ThenBy(d => d.Title))
            {
                var def = d;
                var row = PanelButton("", () => Select(def), list, "arena-row-button");
                var top = El("arena-row-top", row);
                Text(def.Title, "arena-row-title", top);
                Text(ArenaCatalog.Tag(def), "arena-row-tag tag-" + ArenaCatalog.Category(def).ToString().ToLowerInvariant(), top);
                Text(ArenaCatalog.Description(def.Id), "arena-row-text", row);
                rows.Add((row, def));
            }
            carGrid.Clear();
            carButtons.Clear();
            foreach (var c in cars.Cars)
            {
                var entry = c;
                var b = PanelButton("", () => arena.SelectCar(entry.Key), carGrid, "arena-car");
                Text(entry.DisplayName, "arena-car-name", b);
                Text(GarageScreen.ClassName(entry.Class), "arena-car-class", b);
                carButtons.Add((b, entry.Key));
            }
            selected = rows.Count > 0 ? rows[0].def : null;
            SetFilter(ArenaCategory.All);
        }

        void SetFilter(ArenaCategory c)
        {
            filter = c;
            foreach (var (b, cat) in filters) b.EnableInClassList("selected", cat == c);
            foreach (var (row, def) in rows)
                row.style.display = c == ArenaCategory.All || ArenaCatalog.Category(def) == c ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void Select(EventDefinition d)
        {
            selected = d;
            Refresh();
        }

        void ShowCars(bool on)
        {
            showCars = on;
            Refresh();
        }

        void ToggleMenu() => SetMenu(!arena.MenuOpen);

        void SetMenu(bool open)
        {
            arena.SetMenuOpen(open);
            panel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            menuButton.text = open ? "СКРЫТЬ МЕНЮ" : "МЕНЮ";
            // Закрытое меню не держит фокус: пробел — ручник, а не кнопка.
            var focused = Root.focusController?.focusedElement as VisualElement;
            if (!open) focused?.Blur();
            else Root.schedule.Execute(() => (rows.FirstOrDefault(r => r.def == selected).row ?? launchButton)?.Focus()).StartingIn(30);
        }

        /// Запустить выбранное (repeat — повторить с исходных мест; без выбранного — последнее запущенное).
        void Run(bool repeat)
        {
            string id = selected != null ? selected.Id : arena.SelectedId;
            if (string.IsNullOrEmpty(id)) return;
            if (repeat && arena.SelectedId == id) arena.Repeat();
            else if (repeat)
            {
                arena.ResetAll();
                arena.Launch(id);
            }
            else arena.Launch(id);
        }

        void ExtraAction()
        {
            if (arena.CanTouchNow) arena.TouchNow();
            else if (arena.CanTriggerNow) arena.TriggerNow();
        }

        // ---------- обновление ----------

        public void Refresh()
        {
            if (!built) return;
            // Запуск клавишей, кнопкой геймпада или повтором — карточка показывает то, что запущено.
            if (arena.Launches != seenLaunches)
            {
                seenLaunches = arena.Launches;
                var launched = rows.FirstOrDefault(r => r.def.Id == arena.SelectedId).def;
                if (launched != null) selected = launched;
            }
            cameraButton.text = arena.Observing ? "КАМЕРА: ОБЗОР" : "КАМЕРА: МАШИНА";
            modeButton.text = arena.Invulnerable ? "НЕУЯЗВИМОСТЬ" : "УЯЗВИМОСТЬ";
            modeButton.EnableInClassList("arena-on", arena.Invulnerable);
            eventsTab.EnableInClassList("selected", !showCars);
            carsTab.EnableInClassList("selected", showCars);
            eventsPage.style.display = showCars ? DisplayStyle.None : DisplayStyle.Flex;
            carsPage.style.display = showCars ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var (row, def) in rows)
            {
                row.EnableInClassList("selected", def == selected);
                row.EnableInClassList("running", arena.State == DemoState.Running && arena.Demo.Definition == def);
            }
            foreach (var (b, key) in carButtons) b.EnableInClassList("selected", key == arena.CarKey);
            invulnerableButton.EnableInClassList("selected", arena.Invulnerable);
            vulnerableButton.EnableInClassList("selected", !arena.Invulnerable);
            targetsButton.text = arena.Targets ? "МИШЕНИ: ЕСТЬ" : "МИШЕНИ: НЕТ";
            targetsButton.EnableInClassList("selected", arena.Targets);
            var car = cars.Get(arena.CarKey);
            carNote.text = car != null ? $"Сейчас: {car.DisplayName} ({GarageScreen.ClassName(car.Class)}) — своя сборка из гаража, если машина куплена." : "";
            modeNote.text = arena.Invulnerable
                ? "Неуязвимость: толчки, подбросы, кувырки и удары — как в гонке, но без последствий (замедление, нелепый руль, сплющивание) и без возврата на трассу. Системы повреждений в игре нет."
                : "Уязвимость: всё как в гонке — последствия событий и возврат перевернувшейся или улетевшей машины на трассу.";
            if (selected != null)
            {
                detailTitle.text = selected.Title;
                detailTag.text = $"{ArenaCatalog.Tag(selected).ToUpperInvariant()} · предупреждение {selected.WarningSeconds:0.#} с · действие {selected.ActiveSeconds:0.#} с";
                detailText.text = ArenaCatalog.Description(selected.Id);
                detailHint.text = ArenaCatalog.Hint(selected);
            }
            bool running = arena.State == DemoState.Running;
            stopButton.SetEnabled(running);
            detailStop.SetEnabled(running);
            repeatButton.SetEnabled(selected != null || !string.IsNullOrEmpty(arena.SelectedId));
            UpdateStatus();
        }

        /// Каждый кадр: стадия испытания, кнопка особого действия.
        public void Update()
        {
            if (!built || Root.resolvedStyle.display == DisplayStyle.None) return;
            UpdateStatus();
            ReadInput();
        }

        void UpdateStatus()
        {
            string title, stage, hint = "";
            switch (arena.State)
            {
                case DemoState.Running:
                    var d = arena.Demo;
                    title = d.Definition.Title;
                    float t = Time.time - arena.DemoStartedAt;
                    stage = d.Stage switch
                    {
                        EventStage.Telegraph => d.WaitingForApproach ? "ждёт машину" : "предупреждение",
                        EventStage.Active => "действует",
                        EventStage.Ending => "завершается",
                        _ => "",
                    } + $" · {t:0.0} с";
                    if (arena.CanTouchNow) hint = "Въедь в картинку или нажми «Коснуться картинки»";
                    else if (arena.CanTriggerNow) hint = "Подъедь к зоне или нажми «Сработать сейчас»";
                    break;
                case DemoState.Finished:
                    title = arena.Demo.Definition.Title;
                    stage = "закончилось — «Повторить» запустит заново";
                    break;
                case DemoState.Failed:
                    title = selected != null ? selected.Title : "";
                    stage = arena.LastError;
                    break;
                default:
                    title = "Выбери событие в меню";
                    stage = arena.MenuOpen ? "«Запустить» — событие перед машиной" : "Tab — меню полигона";
                    break;
            }
            var player = arena.Race.Player;
            if (arena.Invulnerable && player != null && player.Controller != null && player.Controller.IgnoredConsequences > 0)
                hint = (hint.Length > 0 ? hint + "   ·   " : "") + $"неуязвимость: без последствий ×{player.Controller.IgnoredConsequences} ({player.Controller.LastIgnored})";
            string key = title + stage + hint;
            if (key != lastStatus)
            {
                lastStatus = key;
                statusTitle.text = title;
                statusStage.text = stage;
                statusHint.text = hint;
            }
            string extra = arena.CanTouchNow ? "КОСНУТЬСЯ КАРТИНКИ" : arena.CanTriggerNow ? "СРАБОТАТЬ СЕЙЧАС" : "";
            if (actionButton.text != extra) actionButton.text = extra;
            actionButton.style.display = extra.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            bool running = arena.State == DemoState.Running;
            if (stopButton.enabledSelf != running)
            {
                stopButton.SetEnabled(running);
                detailStop.SetEnabled(running);
            }
        }

        // ---------- клавиши и геймпад ----------

        void CreateInput()
        {
            toggleMenu = new InputAction("ArenaMenu", InputActionType.Button);
            toggleMenu.AddBinding("<Keyboard>/tab");
            toggleMenu.AddBinding("<Gamepad>/select");
            launch = new InputAction("ArenaLaunch", InputActionType.Button);
            launch.AddBinding("<Keyboard>/f");
            launch.AddBinding("<Gamepad>/buttonWest");
            stop = new InputAction("ArenaStop", InputActionType.Button);
            stop.AddBinding("<Keyboard>/x");
            stop.AddBinding("<Gamepad>/leftStickPress");
            reset = new InputAction("ArenaReset", InputActionType.Button, "<Keyboard>/backspace");
            camera = new InputAction("ArenaCamera", InputActionType.Button);
            camera.AddBinding("<Keyboard>/v");
            camera.AddBinding("<Gamepad>/rightStickPress");
        }

        void EnableInput(bool on)
        {
            foreach (var a in new[] { toggleMenu, launch, stop, reset, camera })
            {
                if (on) a.Enable();
                else a.Disable();
            }
        }

        void ReadInput()
        {
            if (toggleMenu.WasPressedThisFrame()) ToggleMenu();
            if (launch.WasPressedThisFrame()) Run(arena.State != DemoState.Running && !string.IsNullOrEmpty(arena.SelectedId));
            if (stop.WasPressedThisFrame()) arena.StopDemo();
            if (reset.WasPressedThisFrame()) arena.ResetAll();
            if (camera.WasPressedThisFrame()) arena.SetObserver(!arena.Observing);
        }
    }
}
