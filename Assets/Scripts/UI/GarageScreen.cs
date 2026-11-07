using System;
using System.Collections.Generic;
using System.Linq;
using RandomCar.Car;
using RandomCar.Garage;
using RandomCar.Save;
using UnityEngine;
using UnityEngine.UIElements;
using static RandomCar.UI.UiKit;

namespace RandomCar.UI
{
    /// Гараж: слева разделы, в центре машина на подиуме, справа панель раздела, внизу — сравнение и покупка.
    /// Работа идёт с черновиком сборки: всё купленное и бесплатное (окраска, наклейки, настройка) сразу
    /// сохраняется в активную сборку машины; некупленная деталь — «примерка», деньги списываются только
    /// после подтверждения в диалоге.
    public sealed class GarageScreen
    {
        enum Category { Cars, Body, Aero, Wheels, Paint, Decals, Tuning, Builds }

        static readonly (Category cat, string title, string hint)[] Categories =
        {
            (Category.Cars, "Машины", "Выбор и покупка машин"),
            (Category.Body, "Кузов и обвесы", "Бамперы, пороги, арки, капот, крыша"),
            (Category.Aero, "Аэродинамика", "Спойлеры и антикрылья"),
            (Category.Wheels, "Колёса", "Диски, шины, цвета"),
            (Category.Paint, "Окраска", "Цвета, покрытие, схемы"),
            (Category.Decals, "Наклейки", "Винилы, номера, свои картинки"),
            (Category.Tuning, "Настройка", "Передачи, подвеска, тормоза"),
            (Category.Builds, "Сборки", "Несколько вариантов машины"),
        };

        static readonly (PartSlot slot, string name)[] BodySlots =
        {
            (PartSlot.FrontBumper, "Передний бампер"), (PartSlot.RearBumper, "Задний бампер"), (PartSlot.Skirts, "Пороги"),
            (PartSlot.Fenders, "Арки"), (PartSlot.Hood, "Капот"), (PartSlot.Grille, "Решётка"), (PartSlot.Mirrors, "Зеркала"),
            (PartSlot.Roof, "Крыша"), (PartSlot.Guard, "Защита"), (PartSlot.Lights, "Доп. свет"),
        };

        static readonly (PaintFinish finish, string name)[] Finishes =
        {
            (PaintFinish.Gloss, "Глянец"), (PaintFinish.Metallic, "Металлик"), (PaintFinish.Pearl, "Перламутр"),
            (PaintFinish.Satin, "Сатин"), (PaintFinish.Matte, "Матовый"), (PaintFinish.Chrome, "Хром"),
        };

        static readonly (PaintScheme scheme, string name)[] Schemes =
        {
            (PaintScheme.Solid, "Однотонная"), (PaintScheme.Roof, "Крыша"), (PaintScheme.Hood, "Капот"),
            (PaintScheme.RoofAndHood, "Крыша и капот"), (PaintScheme.Lower, "Низ кузова"), (PaintScheme.LowerAndRoof, "Низ и крыша"),
        };

        static readonly (DecalSide side, string name)[] Sides =
        {
            (DecalSide.Left, "Левый борт"), (DecalSide.Right, "Правый борт"), (DecalSide.Hood, "Капот"), (DecalSide.Roof, "Крыша"),
            (DecalSide.Trunk, "Багажник"), (DecalSide.Front, "Перед"), (DecalSide.Rear, "Зад"),
        };

        readonly CarCatalog catalog;
        readonly GarageStudio studio;
        readonly Action<CarEntry> confirm;
        readonly Action back;
        readonly Action<string> notice;

        readonly Dictionary<Category, Button> rail = new Dictionary<Category, Button>();
        readonly Label credits, carTitle, carClass, buildName, panelTitle, panelHint, compare, price;
        readonly ScrollView panel;
        readonly Button primary, secondary;
        readonly VisualElement dialog, dialogBox;

        CarEntry car;
        CarBuild installed;
        CarBuild draft;
        int buildIndex;
        PartOption tryOn;
        Category category = Category.Cars;
        PartSlot bodySlot = PartSlot.FrontBumper;
        string wheelTab = "rims";
        int selectedDecal = -1;
        bool decalLibrary;
        IVisualElementScheduledItem pendingSave;

        public VisualElement Root { get; }

        public GarageScreen(VisualElement parent, CarCatalog carCatalog, GarageStudio garageStudio, Action<CarEntry> onConfirm, Action onBack, Action<string> onNotice)
        {
            catalog = carCatalog;
            studio = garageStudio;
            confirm = onConfirm;
            back = onBack;
            notice = onNotice;
            Root = El("screen garage2", parent);
            Root.pickingMode = PickingMode.Ignore;

            // Верхняя полоса: название, машина, сборка, кредиты.
            var top = El("g-top", Root);
            Text("ГАРАЖ", "g-logo", top);
            var titleBox = El("g-title-box", top);
            carTitle = Text("", "g-car", titleBox);
            carClass = Text("", "g-class", titleBox);
            buildName = Text("", "g-build", top);
            var money = El("g-money", top);
            El("g-coin", money);
            credits = Text("", "g-credits", money);
            GarageStore.Changed += RefreshTop;

            // Разделы слева.
            var left = El("g-rail", Root);
            foreach (var (cat, title, _) in Categories)
            {
                var c = cat;
                var b = MenuButton(title, () => ShowCategory(c), left, "g-rail-button");
                b.RemoveFromClassList("menu-button");
                rail[cat] = b;
            }
            Text("Мышь: зажать и вести — повернуть, колесо — приблизить", "g-hint", left);

            // Панель раздела справа.
            var right = El("g-panel", Root);
            panelTitle = Text("", "g-panel-title", right);
            panelHint = Text("", "g-panel-hint", right);
            panel = new ScrollView(ScrollViewMode.Vertical);
            panel.AddToClassList("g-scroll");
            right.Add(panel);

            // Нижняя полоса: сравнение, цена, действия.
            var bottom = El("g-bottom", Root);
            var info = El("g-compare-box", bottom);
            compare = Text("", "g-compare", info);
            price = Text("", "g-price", info);
            secondary = MenuButton("", OnSecondary, bottom, "g-action secondary");
            primary = MenuButton("", OnPrimary, bottom, "g-action");
            new MusicWidget(Root, full: false).Root.AddToClassList("garage-music");

            dialog = El("g-dialog hidden", Root);
            dialogBox = El("g-dialog-box", dialog);
        }

        // ---------- открытие и выбор машины ----------

        public void Open(string carKey)
        {
            // Новые картинки из папки UserContent/Decals (Windows) — в библиотеку наклеек.
            string scan = RandomCar.UserContent.UserDecalFolder.Scan();
            if (scan != null) notice(scan);
            LoadCar(catalog.Get(carKey));
            ShowCategory(GarageStore.OwnsCar(car) ? Category.Paint : Category.Cars);
            primary.Focus();
        }

        void LoadCar(CarEntry entry)
        {
            if (car != null && car != entry) SaveNow();
            car = entry;
            tryOn = null;
            selectedDecal = -1;
            decalLibrary = false;
            if (GarageStore.OwnsCar(car))
            {
                buildIndex = GarageStore.ActiveIndex(car, catalog);
                installed = GarageStore.Active(car, catalog);
            }
            else
            {
                buildIndex = 0;
                installed = CarBuild.Default(car);
            }
            draft = installed.Clone();
            Preview();
            RefreshTop();
            RefreshBottom();
        }

        bool Owned => GarageStore.OwnsCar(car);

        void Preview() => studio.Refresh(car, draft, catalog);

        /// Бесплатное изменение: и в черновик, и в сохранённую сборку (с задержкой — ползунки не пишут файл на каждый шаг).
        void Edit(Action<CarBuild> change)
        {
            change(draft);
            change(installed);
            Preview();
            if (!Owned) return;
            pendingSave?.Pause();
            pendingSave = Root.schedule.Execute(SaveNow).StartingIn(500);
        }

        void SaveNow()
        {
            pendingSave?.Pause();
            pendingSave = null;
            if (car == null || installed == null || !GarageStore.OwnsCar(car)) return;
            GarageStore.SaveBuild(car, catalog, buildIndex, installed);
            GarageStore.SetActive(car, buildIndex);
        }

        void RefreshTop()
        {
            if (car == null) return;
            credits.text = $"{GarageStore.Credits:N0} кр.".Replace(',', ' ');
            carTitle.text = car.DisplayName.ToUpperInvariant();
            carClass.text = ClassName(car.Class);
            buildName.text = Owned ? $"Сборка: {installed.Name}" : "Машина не куплена";
            foreach (var pair in rail) pair.Value.SetEnabled(pair.Key == Category.Cars || Owned);
        }

        public static string ClassName(CarClass c) => c switch
        {
            CarClass.Compact => "Компактный",
            CarClass.Sport => "Спортивное купе",
            CarClass.Sedan => "Спортивный седан",
            CarClass.Muscle => "Маслкар",
            CarClass.Rally => "Раллийный",
            CarClass.Offroad => "Подготовленный внедорожник",
            CarClass.LuxurySuv => "Роскошный внедорожник",
            CarClass.Super => "Суперкар",
            CarClass.Hyper => "Гиперкар",
            _ => "Классика",
        };

        // ---------- разделы ----------

        void ShowCategory(Category c)
        {
            if (!Owned && c != Category.Cars) c = Category.Cars;
            HideDialog();
            if (tryOn != null && c != Category.Body && c != Category.Aero && c != Category.Wheels) CancelTryOn();
            category = c;
            foreach (var pair in rail) pair.Value.EnableInClassList("selected", pair.Key == c);
            var info = Array.Find(Categories, x => x.cat == c);
            panelTitle.text = info.title.ToUpperInvariant();
            panelHint.text = info.hint;
            studio.SetView(c switch
            {
                Category.Body => GarageView.Front,
                Category.Aero => GarageView.Rear,
                Category.Wheels => GarageView.Wheel,
                Category.Tuning => GarageView.Side,
                Category.Decals => DecalView(),
                _ => GarageView.Overview,
            });
            Rebuild();
        }

        GarageView DecalView()
        {
            if (selectedDecal < 0 || selectedDecal >= draft.Decals.Count) return GarageView.LeftSide;
            return draft.Decals[selectedDecal].Side switch
            {
                DecalSide.Right => GarageView.RightSide,
                DecalSide.Hood or DecalSide.Roof or DecalSide.Trunk => GarageView.Top,
                DecalSide.Front => GarageView.Front,
                DecalSide.Rear => GarageView.Rear,
                _ => GarageView.LeftSide,
            };
        }

        void Rebuild()
        {
            panel.Clear();
            var body = panel.contentContainer;
            switch (category)
            {
                case Category.Cars: BuildCars(body); break;
                case Category.Body: BuildBody(body); break;
                case Category.Aero: BuildSlot(body, PartSlot.Spoiler, "Спойлеры меняют только вид: прижимная сила машины не меняется."); break;
                case Category.Wheels: BuildWheels(body); break;
                case Category.Paint: BuildPaint(body); break;
                case Category.Decals: BuildDecals(body); break;
                case Category.Tuning: BuildTuning(body); break;
                case Category.Builds: BuildBuilds(body); break;
            }
            RefreshBottom();
        }

        // ---------- машины ----------

        void BuildCars(VisualElement body)
        {
            FillStats(body, car);
            Text("МАШИНЫ", "g-section", body);
            foreach (var entry in catalog.Cars)
            {
                var e = entry;
                bool owned = GarageStore.OwnsCar(e);
                bool unlocked = GarageStore.Unlocked(e, out string req);
                string status = owned ? (e.Key == RandomCar.Core.GameSettings.Car ? "Выбрана" : "В гараже") : unlocked ? $"{e.Price:N0} кр.".Replace(',', ' ') : "Закрыта";
                var card = Card(body, e.DisplayName, ClassName(e.Class) + (unlocked || owned ? "" : " · " + req), status, e == car, () =>
                {
                    LoadCar(e);
                    Rebuild();
                });
                card.EnableInClassList("locked", !owned && !unlocked);
                card.EnableInClassList("owned", owned);
            }
        }

        readonly Dictionary<string, (VisualElement fill, Label value)> bars = new Dictionary<string, (VisualElement, Label)>();

        void FillStats(VisualElement body, CarEntry e)
        {
            var spec = e.Spec;
            Text(spec.Description, "g-desc", body);
            var chips = El("g-chips", body);
            Text(spec.Drive switch { DriveType.FrontWheel => "Передний привод", DriveType.AllWheel => "Полный привод", _ => "Задний привод" }, "g-chip", chips);
            Text($"{spec.Mass:0} кг", "g-chip", chips);
            Text(spec.Turbo.MaxBoost > 0.01f ? $"Турбо {spec.Turbo.MaxBoost:0.0} бар" : "Атмосферный", "g-chip", chips);
            float kw = spec.PeakPowerKw();
            Bar(body, "МОЩНОСТЬ", kw / 520f, $"{kw * 1.36f:0} л.с.");
            Bar(body, "РАЗГОН", kw * 1000f / spec.Mass / 300f, $"{kw * 1000f / spec.Mass:0} Вт/кг");
            Bar(body, "МАКС. СКОРОСТЬ", (spec.TopSpeedKmh - 150f) / 200f, $"{spec.TopSpeedKmh:0} км/ч");
            float grip = (spec.Front.LateralGrip + spec.Rear.LateralGrip) * 0.5f;
            Bar(body, "СЦЕПЛЕНИЕ", (grip - 0.9f) / 0.6f, $"{grip:0.00}");
            Bar(body, "ГРУНТ", (spec.LooseSurfaceGrip - 0.8f) / 0.6f, $"×{spec.LooseSurfaceGrip:0.00}");
        }

        static void Bar(VisualElement parent, string name, float value, string text)
        {
            var row = El("stat-row", parent);
            Text(name, "stat-name", row);
            var bar = El("stat-bar", row);
            El("stat-fill", bar).style.width = Length.Percent(Mathf.Clamp(value, 0.05f, 1f) * 100f);
            Text(text, "stat-value", row);
        }

        // ---------- обвес ----------

        void BuildBody(VisualElement body)
        {
            var cosmetics = car.Prefab.GetComponent<CarCosmetics>();
            var available = BodySlots.Where(s => catalog.PartsFor(car, s.slot).Any(p => p.Variant != CarBuild.Stock)).ToList();
            if (available.Count == 0)
            {
                Text("Для этой машины пока нет обвеса.", "g-empty", body);
                return;
            }
            if (!available.Any(s => s.slot == bodySlot)) bodySlot = available[0].slot;
            var tabs = El("g-tabs", body);
            foreach (var (slot, name) in available)
            {
                var s = slot;
                var b = MenuButton(name, () =>
                {
                    if (tryOn != null && tryOn.Slot != s) CancelTryOn();
                    bodySlot = s;
                    Rebuild();
                }, tabs, "g-tab");
                b.RemoveFromClassList("menu-button");
                b.EnableInClassList("selected", s == bodySlot);
                if (draft.Get(s) != CarBuild.Stock) b.AddToClassList("changed");
            }
            BuildSlot(body, bodySlot, "Детали построены под эту машину и меняют только внешний вид.");
            _ = cosmetics;
        }

        void BuildSlot(VisualElement body, PartSlot slot, string note)
        {
            var parts = catalog.PartsFor(car, slot);
            if (parts.Count <= 1 && slot == PartSlot.Spoiler)
            {
                Text("Для этой машины спойлеры не предусмотрены.", "g-empty", body);
                return;
            }
            string current = installed.Get(slot);
            foreach (var part in parts)
            {
                var p = part;
                bool owned = GarageStore.OwnsPart(car, p);
                string status = p.Variant == current ? "Установлено" : owned ? "Куплено" : $"{p.Price:N0} кр.".Replace(',', ' ');
                var card = Card(body, p.Name, p.Description, status, draft.Get(slot) == p.Variant, () => ChoosePart(p));
                card.EnableInClassList("owned", owned);
            }
            Text(note, "g-note", body);
        }

        void ChoosePart(PartOption part)
        {
            if (GarageStore.OwnsPart(car, part))
            {
                tryOn = null;
                draft = installed.Clone();
                Edit(b => b.Set(part.Slot, part.Variant));
            }
            else
            {
                // Примерка: только черновик, деньги не списываются.
                tryOn = part;
                draft = installed.Clone();
                draft.Set(part.Slot, part.Variant);
                Preview();
            }
            Rebuild();
        }

        void CancelTryOn()
        {
            tryOn = null;
            draft = installed.Clone();
            Preview();
        }

        // ---------- колёса ----------

        void BuildWheels(VisualElement body)
        {
            var tabs = El("g-tabs", body);
            foreach (var (key, name) in new[] { ("rims", "Диски"), ("tires", "Шины"), ("rimcolor", "Цвет дисков"), ("calipers", "Суппорты") })
            {
                var k = key;
                var b = MenuButton(name, () =>
                {
                    if (tryOn != null) CancelTryOn();
                    wheelTab = k;
                    Rebuild();
                }, tabs, "g-tab");
                b.RemoveFromClassList("menu-button");
                b.EnableInClassList("selected", k == wheelTab);
            }
            switch (wheelTab)
            {
                case "tires":
                    BuildSlot(body, PartSlot.Tires, "Шины влияют на сцепление — цифры в описании. Это компромисс, а не улучшение.");
                    break;
                case "rimcolor":
                    new ColorPicker(body, "Цвет дисков", catalog.Palette, draft.Paint.Rims, c => Edit(b => b.Paint.Rims = c), compact: true);
                    FinishRow(body, "Покрытие дисков", draft.Paint.RimFinish, f => { Edit(b => b.Paint.RimFinish = f); Rebuild(); });
                    break;
                case "calipers":
                    new ColorPicker(body, "Цвет суппортов", catalog.Palette, draft.Paint.Calipers, c => Edit(b => b.Paint.Calipers = c), compact: true);
                    break;
                default:
                    BuildSlot(body, PartSlot.Rims, "Диски подгоняются под размер колёс машины.");
                    break;
            }
        }

        // ---------- окраска ----------

        void BuildPaint(VisualElement body)
        {
            Text("КУЗОВ", "g-section", body);
            new ColorPicker(body, "Основной цвет", catalog.Palette, draft.Paint.Body, c => Edit(b => b.Paint.Body = c));
            FinishRow(body, "Покрытие", draft.Paint.BodyFinish, f => { Edit(b => b.Paint.BodyFinish = f); Rebuild(); });

            Text("ДВУХЦВЕТНАЯ СХЕМА", "g-section", body);
            var schemes = El("g-options", body);
            foreach (var (scheme, name) in Schemes)
            {
                var s = scheme;
                Option(schemes, name, draft.Paint.Scheme == s, () => { Edit(b => b.Paint.Scheme = s); Rebuild(); });
            }
            Text("ОБВЕС", "g-section", body);
            var partsRow = El("g-options", body);
            foreach (var (mode, name) in new[] { (PartsColor.Body, "В цвет кузова"), (PartsColor.Black, "Чёрный"), (PartsColor.Accent, "Акцентный") })
            {
                var m = mode;
                Option(partsRow, name, draft.Paint.Parts == m, () => { Edit(b => b.Paint.Parts = m); Rebuild(); });
            }
            if (draft.Paint.Scheme != PaintScheme.Solid || draft.Paint.Parts == PartsColor.Accent)
            {
                Text("АКЦЕНТНЫЙ ЦВЕТ", "g-section", body);
                new ColorPicker(body, "Акцент", catalog.Palette, draft.Paint.Accent, c => Edit(b => b.Paint.Accent = c), compact: true);
                FinishRow(body, "Покрытие акцента", draft.Paint.AccentFinish, f => { Edit(b => b.Paint.AccentFinish = f); Rebuild(); });
            }
            Text("СТЁКЛА", "g-section", body);
            SliderRow(body, "Тонировка", draft.Paint.WindowTint, 0f, 1f, v => $"{Mathf.RoundToInt(v * 100)}%", v => Edit(b => b.Paint.WindowTint = v));
            Text("Окраска и наклейки бесплатны и сохраняются сразу.", "g-note", body);
        }

        void FinishRow(VisualElement body, string title, PaintFinish current, Action<PaintFinish> set)
        {
            Text(title, "g-sub", body);
            var row = El("g-options", body);
            foreach (var (finish, name) in Finishes)
            {
                var f = finish;
                Option(row, name, current == f, () => set(f));
            }
        }

        // ---------- наклейки ----------

        void BuildDecals(VisualElement body)
        {
            if (decalLibrary)
            {
                BuildDecalLibrary(body);
                return;
            }
            var head = El("g-row", body);
            Text($"Слои: {draft.Decals.Count} из {CarBuild.MaxDecals}", "g-sub", head);
            var add = MenuButton("+ ДОБАВИТЬ", () => { decalLibrary = true; Rebuild(); }, head, "g-small");
            add.SetEnabled(draft.Decals.Count < CarBuild.MaxDecals);
            if (draft.Decals.Count == 0) Text("Наклеек пока нет. Добавьте номер, полосы, узор или свою картинку.", "g-empty", body);
            for (int i = draft.Decals.Count - 1; i >= 0; i--)
            {
                int index = i;
                var d = draft.Decals[i];
                var row = El("g-layer", body);
                row.EnableInClassList("selected", index == selectedDecal);
                var thumb = new Image { image = DecalLibrary.Resolve(d), scaleMode = ScaleMode.ScaleToFit };
                thumb.AddToClassList("g-thumb");
                row.Add(thumb);
                var label = MenuButton($"{DecalLibrary.NameOf(d)}\n{Array.Find(Sides, s => s.side == d.Side).name}", () =>
                {
                    selectedDecal = index;
                    studio.SetView(DecalView());
                    Rebuild();
                }, row, "g-layer-name");
                label.RemoveFromClassList("menu-button");
                SmallButton(row, "▲", "Выше (рисуется поверх)", index < draft.Decals.Count - 1, () => MoveDecal(index, 1));
                SmallButton(row, "▼", "Ниже", index > 0, () => MoveDecal(index, -1));
                SmallButton(row, "×", "Удалить", true, () =>
                {
                    Edit(b => b.Decals.RemoveAt(index));
                    selectedDecal = -1;
                    Rebuild();
                });
            }
            if (selectedDecal >= 0 && selectedDecal < draft.Decals.Count) BuildDecalEditor(body, selectedDecal);
        }

        void MoveDecal(int index, int delta)
        {
            int target = index + delta;
            Edit(b =>
            {
                (b.Decals[index], b.Decals[target]) = (b.Decals[target], b.Decals[index]);
            });
            selectedDecal = target;
            Rebuild();
        }

        void BuildDecalEditor(VisualElement body, int index)
        {
            var d = draft.Decals[index];
            Text("НАСТРОЙКА НАКЛЕЙКИ", "g-section", body);
            var sides = El("g-options", body);
            foreach (var (side, name) in Sides)
            {
                var s = side;
                Option(sides, name, d.Side == s, () =>
                {
                    Edit(b => b.Decals[index].Side = s);
                    studio.SetView(DecalView());
                    Rebuild();
                });
            }
            if (d.Source.StartsWith("number:", StringComparison.Ordinal))
            {
                SliderRow(body, "Номер", d.Number, 0, 99, v => $"{Mathf.RoundToInt(v)}", v => Edit(b => b.Decals[index].Number = Mathf.RoundToInt(v)), whole: true);
            }
            bool top = d.Side == DecalSide.Hood || d.Side == DecalSide.Roof || d.Side == DecalSide.Trunk;
            SliderRow(body, top ? "Поперёк" : "Вдоль кузова", d.U, -0.05f, 1.05f, v => $"{Mathf.RoundToInt(v * 100)}%", v => Edit(b => b.Decals[index].U = v));
            SliderRow(body, top ? "Вдоль" : "По высоте", d.V, -0.05f, 1.05f, v => $"{Mathf.RoundToInt(v * 100)}%", v => Edit(b => b.Decals[index].V = v));
            SliderRow(body, "Размер", d.Size, 0.04f, 1.2f, v => $"{Mathf.RoundToInt(v * 100)}%", v => Edit(b => b.Decals[index].Size = v));
            SliderRow(body, "Поворот", d.Rotation, -180f, 180f, v => $"{Mathf.RoundToInt(v)}°", v => Edit(b => b.Decals[index].Rotation = v));
            SliderRow(body, "Растянуть", Mathf.Log(d.Aspect, 2f), -2f, 2f, v => $"×{Mathf.Pow(2f, v):0.00}", v => Edit(b => b.Decals[index].Aspect = Mathf.Pow(2f, v)));
            SliderRow(body, "Непрозрачность", d.Opacity, 0.1f, 1f, v => $"{Mathf.RoundToInt(v * 100)}%", v => Edit(b => b.Decals[index].Opacity = v));
            var toggles = El("g-options", body);
            Option(toggles, "Зеркально на другой борт", d.Mirror, () => { Edit(b => b.Decals[index].Mirror = !b.Decals[index].Mirror); Rebuild(); });
            Option(toggles, "Отразить", d.FlipX, () => { Edit(b => b.Decals[index].FlipX = !b.Decals[index].FlipX); Rebuild(); });
            new ColorPicker(body, "Цвет наклейки", catalog.Palette, d.Tint, c => Edit(b => b.Decals[index].Tint = c), compact: true);
        }

        void BuildDecalLibrary(VisualElement body)
        {
            var head = El("g-row", body);
            var backButton = MenuButton("← К СЛОЯМ", () => { decalLibrary = false; Rebuild(); }, head, "g-small");
            backButton.AddToClassList("secondary");
            Text("СВОИ КАРТИНКИ", "g-section", body);
            var importRow = El("g-row", body);
            var import = MenuButton("ДОБАВИТЬ НАКЛЕЙКУ", ImportImage, importRow, "g-small");
            import.SetEnabled(FileDialog.Supported || RandomCar.UserContent.AndroidFilePicker.Supported);
            if (RandomCar.UserContent.UserContentPaths.HasDropFolders)
            {
                var folder = MenuButton("ОТКРЫТЬ ПАПКУ", () => RandomCar.UserContent.UserContentPaths.Reveal(RandomCar.UserContent.UserContentPaths.DecalDropFolder), importRow, "g-small");
                folder.AddToClassList("secondary");
                Text($"PNG или JPEG до {CustomDecals.MaxFileBytes / (1024 * 1024)} МБ и {CustomDecals.MaxSourceSide}×{CustomDecals.MaxSourceSide}, не больше {CustomDecals.MaxCount} картинок. PNG сохраняет прозрачность. " +
                     "Можно просто положить файлы в папку игры UserContent/Decals — они появятся здесь при открытии гаража. Картинки хранятся только на этом компьютере.", "g-note", body);
            }
            else
                Text($"PNG или JPEG из памяти телефона, не больше {CustomDecals.MaxCount} картинок. Большие фото уменьшаются, PNG сохраняет прозрачность. Картинки хранятся только на этом телефоне.", "g-note", body);
            var customs = El("g-grid", body);
            foreach (var id in CustomDecals.List())
            {
                var key = id;
                var cell = El("g-cell", customs);
                var tex = CustomDecals.Load(key);
                var add = new Button(() => AddDecal("custom:" + key, DecalSide.Hood, 0.4f)) { tooltip = "Добавить на машину" };
                add.AddToClassList("g-cell-button");
                var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
                img.AddToClassList("g-cell-image");
                add.Add(img);
                cell.Add(add);
                var del = new Button(() => DeleteCustom(key)) { text = "×", tooltip = "Удалить картинку" };
                del.AddToClassList("g-cell-delete");
                cell.Add(del);
            }
            string group = null;
            VisualElement grid = null;
            foreach (var info in DecalArt.All)
            {
                if (info.Group != group)
                {
                    group = info.Group;
                    Text(group.ToUpperInvariant(), "g-section", body);
                    grid = El("g-grid", body);
                }
                var i = info;
                var cell = El("g-cell", grid);
                var add = new Button(() => AddDecal(i.Id.StartsWith("number:", StringComparison.Ordinal) ? i.Id : "builtin:" + i.Id, i.Side, i.Size)) { tooltip = i.Name };
                add.AddToClassList("g-cell-button");
                var img = new Image { image = DecalArt.Get(i.Id, 7), scaleMode = ScaleMode.ScaleToFit };
                img.AddToClassList("g-cell-image");
                add.Add(img);
                cell.Add(add);
                Text(i.Name, "g-cell-name", cell);
            }
        }

        void AddDecal(string source, DecalSide side, float size)
        {
            if (draft.Decals.Count >= CarBuild.MaxDecals)
            {
                notice($"Не больше {CarBuild.MaxDecals} наклеек на машину.");
                return;
            }
            var layer = new DecalLayer { Source = source, Side = side, Size = size, U = 0.5f, V = side == DecalSide.Left || side == DecalSide.Right ? 0.3f : 0.5f, Number = 7 };
            Edit(b => b.Decals.Add(JsonUtility.FromJson<DecalLayer>(JsonUtility.ToJson(layer))));
            selectedDecal = draft.Decals.Count - 1;
            decalLibrary = false;
            studio.SetView(DecalView());
            Rebuild();
        }

        void ImportImage()
        {
            if (RandomCar.UserContent.AndroidFilePicker.Supported)
            {
                // Android: системный выбор картинки; большие фото уменьшаются до 2048 точек ещё до загрузки в игру.
                RandomCar.UserContent.AndroidFilePicker.PickImage(2048, (files, error) =>
                {
                    if (files.Count == 0)
                    {
                        if (error != null) notice(error);
                        return;
                    }
                    ImportFrom(files[0].Path);
                    try
                    {
                        System.IO.File.Delete(files[0].Path);
                    }
                    catch (System.IO.IOException e)
                    {
                        Debug.LogWarning($"[RandomCar] Временная копия картинки не удалена: {e.Message}");
                    }
                });
                return;
            }
            string path = FileDialog.OpenImage("Картинка для наклейки (PNG или JPEG)");
            if (string.IsNullOrEmpty(path)) return;
            ImportFrom(path);
        }

        void ImportFrom(string path)
        {
            var result = CustomDecals.Import(path);
            if (!result.Ok)
            {
                notice(result.Error);
                return;
            }
            notice("Картинка добавлена.");
            AddDecal("custom:" + result.Id, DecalSide.Hood, 0.4f);
        }

        void DeleteCustom(string id)
        {
            Confirm("Удалить картинку?", "Наклейки с ней пропадут со всех машин.", "Удалить", () =>
            {
                try
                {
                    CustomDecals.Delete(id);
                    RandomCar.UserContent.UserDecalFolder.Forget(id);
                }
                catch (System.IO.IOException error)
                {
                    notice($"Не удалось удалить: {error.Message}");
                }
                Edit(b => b.Decals.RemoveAll(x => x.Source == "custom:" + id));
                Rebuild();
            });
        }

        // ---------- настройка ----------

        void BuildTuning(VisualElement body)
        {
            Text("Настройка меняет поведение, а не делает машину строго быстрее: у каждого изменения есть плата.", "g-desc", body);
            TuningRow(body, "ПЕРЕДАТОЧНОЕ ЧИСЛО", "Короче", "Длиннее", "gearing", draft.Tuning.Gearing, v => Edit(b => b.Tuning.Gearing = v));
            TuningRow(body, "ПОДВЕСКА", "Мягче", "Жёстче", "suspension", draft.Tuning.Suspension, v => Edit(b => b.Tuning.Suspension = v));
            TuningRow(body, "БАЛАНС ТОРМОЗОВ", "Назад", "Вперёд", "brakes", draft.Tuning.BrakeBias, v => Edit(b => b.Tuning.BrakeBias = v));
            var tires = CarTuning.Tires(draft.Get(PartSlot.Tires));
            Text($"Шины: асфальт ×{tires.Tarmac:0.00}, грунт ×{tires.Loose:0.00} (меняются в разделе «Колёса»).", "g-note", body);
            var reset = MenuButton("СБРОСИТЬ НАСТРОЙКУ", () => { Edit(b => b.Tuning = new TuningSetup()); Rebuild(); }, body, "g-small secondary");
            reset.SetEnabled(!draft.Tuning.IsStock);
        }

        void TuningRow(VisualElement body, string title, string minLabel, string maxLabel, string what, float value, Action<float> set)
        {
            Text(title, "g-section", body);
            var desc = Text(CarTuning.Describe(what, value), "g-desc", body);
            var row = El("g-tune", body);
            Text(minLabel, "g-tune-end", row);
            var slider = new Slider(-1f, 1f) { value = value };
            slider.AddToClassList("volume-slider");
            slider.AddToClassList("g-tune-slider");
            slider.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Round(e.newValue * 10f) / 10f;
                desc.text = CarTuning.Describe(what, v);
                set(v);
            });
            row.Add(slider);
            Text(maxLabel, "g-tune-end", row);
        }

        // ---------- сборки ----------

        void BuildBuilds(VisualElement body)
        {
            var builds = GarageStore.Builds(car, catalog);
            Text($"Сборки этой машины: {builds.Count} из {GarageStore.MaxBuilds}. Активная едет в гонке.", "g-desc", body);
            for (int i = 0; i < builds.Count; i++)
            {
                int index = i;
                var b = builds[i];
                var row = El("g-build-row", body);
                row.EnableInClassList("selected", index == buildIndex);
                var field = new TextField { value = b.Name, maxLength = CarBuild.MaxNameLength };
                field.AddToClassList("g-build-name");
                field.RegisterCallback<FocusOutEvent>(_ => Rename(index, field.value));
                row.Add(field);
                Text(Summary(b), "g-build-info", row);
                var use = MenuButton(index == buildIndex ? "АКТИВНАЯ" : "ВЫБРАТЬ", () => SwitchBuild(index), row, "g-small");
                use.SetEnabled(index != buildIndex);
                var del = MenuButton("×", () => Confirm("Удалить сборку?", $"«{b.Name}» будет удалена. Купленные детали останутся.", "Удалить", () =>
                {
                    GarageStore.DeleteBuild(car, index);
                    LoadCar(car);
                    Rebuild();
                }), row, "g-small secondary");
                del.SetEnabled(builds.Count > 1);
            }
            var actions = El("g-row", body);
            var copy = MenuButton("+ КОПИЯ ТЕКУЩЕЙ", () => NewBuild(installed.Clone(), "Копия"), actions, "g-small");
            copy.SetEnabled(builds.Count < GarageStore.MaxBuilds);
            var fresh = MenuButton("+ ЗАВОДСКАЯ", () => NewBuild(CarBuild.Default(car), "Новая"), actions, "g-small secondary");
            fresh.SetEnabled(builds.Count < GarageStore.MaxBuilds);
            Text("Например: повседневная, гоночная и экстремальная — переключаются одной кнопкой.", "g-note", body);
        }

        string Summary(CarBuild b)
        {
            int parts = b.Parts.Count(p => p.Variant != CarBuild.Stock);
            return $"деталей: {parts} · наклеек: {b.Decals.Count}" + (b.Tuning.IsStock ? "" : " · настроена");
        }

        void Rename(int index, string name)
        {
            var builds = GarageStore.Builds(car, catalog);
            if (index >= builds.Count || string.IsNullOrWhiteSpace(name) || builds[index].Name == name.Trim()) return;
            builds[index].Name = name.Trim();
            GarageStore.SaveBuild(car, catalog, index, builds[index]);
            if (index == buildIndex) installed.Name = name.Trim();
            RefreshTop();
        }

        void SwitchBuild(int index)
        {
            SaveNow();
            GarageStore.SetActive(car, index);
            LoadCar(car);
            Rebuild();
        }

        void NewBuild(CarBuild build, string name)
        {
            SaveNow();
            build.Name = name;
            int index = GarageStore.SaveBuild(car, catalog, -1, build);
            if (index < 0)
            {
                notice($"Не больше {GarageStore.MaxBuilds} сборок на машину.");
                return;
            }
            GarageStore.SetActive(car, index);
            LoadCar(car);
            Rebuild();
        }

        // ---------- нижняя полоса и покупки ----------

        void RefreshBottom()
        {
            if (car == null) return;
            secondary.style.display = DisplayStyle.Flex;
            primary.SetEnabled(true);
            if (!Owned)
            {
                bool unlocked = GarageStore.Unlocked(car, out string req);
                compare.text = unlocked ? $"«{car.DisplayName}» — {ClassName(car.Class).ToLowerInvariant()}" : $"Закрыта: {req}";
                price.text = unlocked ? $"Цена: {car.Price:N0} кр.".Replace(',', ' ') : "";
                primary.text = "КУПИТЬ МАШИНУ";
                primary.SetEnabled(unlocked && GarageStore.Credits >= car.Price);
                secondary.text = "В МЕНЮ";
            }
            else if (tryOn != null)
            {
                var installedPart = catalog.Part(tryOn.Slot, installed.Get(tryOn.Slot));
                compare.text = $"Установлено: {(installedPart != null ? installedPart.Name : "заводское")}  →  Примерка: {tryOn.Name}";
                int left = GarageStore.Credits - tryOn.Price;
                price.text = $"Цена: {tryOn.Price:N0} кр. · останется {Math.Max(0, left):N0} кр.".Replace(',', ' ');
                primary.text = "КУПИТЬ И УСТАНОВИТЬ";
                primary.SetEnabled(left >= 0);
                secondary.text = "СНЯТЬ ПРИМЕРКУ";
            }
            else
            {
                compare.text = $"Сборка «{installed.Name}» сохранена";
                price.text = car.Key == RandomCar.Core.GameSettings.Car ? "Эта машина выбрана для гонки" : "";
                primary.text = "ГОТОВО";
                secondary.text = "В МЕНЮ";
            }
        }

        void OnPrimary()
        {
            if (!Owned)
            {
                Confirm("Купить машину?", $"«{car.DisplayName}» за {car.Price:N0} кр. Останется {GarageStore.Credits - car.Price:N0} кр.".Replace(',', ' '), "Купить", () =>
                {
                    if (!GarageStore.BuyCar(car))
                    {
                        notice("Покупка не удалась: не хватает кредитов.");
                        return;
                    }
                    notice($"«{car.DisplayName}» теперь в вашем гараже!");
                    LoadCar(car);
                    Rebuild();
                });
                return;
            }
            if (tryOn != null)
            {
                var part = tryOn;
                Confirm("Купить деталь?", $"«{part.Name}» за {part.Price:N0} кр. Останется {GarageStore.Credits - part.Price:N0} кр.".Replace(',', ' '), "Купить", () =>
                {
                    if (!GarageStore.BuyPart(car, part))
                    {
                        notice("Покупка не удалась: не хватает кредитов.");
                        return;
                    }
                    tryOn = null;
                    draft = installed.Clone();
                    Edit(b => b.Set(part.Slot, part.Variant));
                    SaveNow();
                    Rebuild();
                });
                return;
            }
            SaveNow();
            confirm(car);
        }

        void OnSecondary()
        {
            if (Owned && tryOn != null)
            {
                CancelTryOn();
                Rebuild();
                return;
            }
            Close();
            back();
        }

        /// Выход из гаража: сохранить сборку, снять примерку.
        public void Close()
        {
            if (tryOn != null) CancelTryOn();
            SaveNow();
            HideDialog();
        }

        // ---------- диалог ----------

        void Confirm(string title, string text, string yes, Action onYes)
        {
            dialogBox.Clear();
            Text(title, "g-dialog-title", dialogBox);
            Text(text, "g-dialog-text", dialogBox);
            var row = El("g-row", dialogBox);
            var ok = MenuButton(yes.ToUpperInvariant(), () =>
            {
                HideDialog();
                onYes();
            }, row, "g-small");
            MenuButton("ОТМЕНА", HideDialog, row, "g-small secondary");
            dialog.RemoveFromClassList("hidden");
            ok.schedule.Execute(() => ok.Focus()).StartingIn(30);
        }

        void HideDialog() => dialog.AddToClassList("hidden");

        public bool DialogOpen => !dialog.ClassListContains("hidden");

        /// Esc в гараже: закрыть диалог или примерку. false — можно выходить.
        public bool Back()
        {
            if (DialogOpen)
            {
                HideDialog();
                return true;
            }
            if (decalLibrary)
            {
                decalLibrary = false;
                Rebuild();
                return true;
            }
            return false;
        }

        // ---------- элементы ----------

        static VisualElement Card(VisualElement parent, string title, string subtitle, string status, bool selected, Action click)
        {
            var b = MenuButton("", click, parent, "g-card");
            b.RemoveFromClassList("menu-button");
            b.EnableInClassList("selected", selected);
            var texts = El("g-card-texts", b);
            Text(title, "g-card-title", texts);
            if (!string.IsNullOrEmpty(subtitle)) Text(subtitle, "g-card-sub", texts);
            Text(status, "g-card-status", b);
            return b;
        }

        static void Option(VisualElement parent, string text, bool selected, Action click)
        {
            var b = MenuButton(text, click, parent, "g-option");
            b.RemoveFromClassList("menu-button");
            b.EnableInClassList("selected", selected);
        }

        static void SmallButton(VisualElement parent, string text, string tooltip, bool enabled, Action click)
        {
            var b = MenuButton(text, click, parent, "g-icon-button");
            b.RemoveFromClassList("menu-button");
            b.tooltip = tooltip;
            b.SetEnabled(enabled);
        }

        static void SliderRow(VisualElement parent, string label, float value, float min, float max, Func<float, string> format, Action<float> set, bool whole = false)
        {
            var row = El("setting volume", parent);
            var head = El("volume-head", row);
            Text(label, "g-sub", head);
            var valueLabel = Text(format(value), "volume-value", head);
            var slider = new Slider(min, max) { value = value };
            slider.AddToClassList("volume-slider");
            slider.RegisterValueChangedCallback(e =>
            {
                float v = whole ? Mathf.Round(e.newValue) : e.newValue;
                valueLabel.text = format(v);
                set(v);
            });
            row.Add(slider);
        }
    }
}
