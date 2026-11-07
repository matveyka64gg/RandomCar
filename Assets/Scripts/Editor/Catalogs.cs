using System.Collections.Generic;
using System.IO;
using System.Linq;
using RandomCar.Car;
using RandomCar.Maps;
using UnityEditor;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Каталоги машин и карт, список сцен сборки.
    public static class Catalogs
    {
        public const string CarCatalogPath = "Assets/Settings/CarCatalog.asset";
        public const string MapCatalogPath = "Assets/Settings/MapCatalog.asset";
        public const string GameScenePath = "Assets/Scenes/Game.unity";

        /// Палитра гаража: быстрый выбор цвета (любой другой — ползунками).
        static readonly (string name, string hex)[] Palette =
        {
            ("Огненный", "#E2401F"), ("Вишня", "#9E1030"), ("Коралл", "#FF6F61"), ("Оранжевый «Рандом»", "#FF6A2A"),
            ("Янтарь", "#FFB21A"), ("Подсолнух", "#FFD84A"), ("Лайм", "#A6E23A"), ("Изумруд", "#11845B"),
            ("Мята", "#2DD4A0"), ("Бирюза", "#1FC7C1"), ("Небесный", "#4AA3E8"), ("Кобальт", "#1F4FD8"),
            ("Ночная синь", "#14234D"), ("Фиалка", "#7A4FE0"), ("Пурпур", "#B0287F"), ("Розовый неон", "#FF4FA0"),
            ("Снежный", "#F2F0E8"), ("Серебро", "#B9BEC6"), ("Титан", "#7D838C"), ("Графит", "#3A3E45"),
            ("Чёрный", "#0E0F11"), ("Песок", "#CDB48A"), ("Хаки", "#5E6B3A"), ("Шоколад", "#4A2C1E"),
        };

        static readonly (PartSlot slot, string variant, string name, string desc, int price)[] Kits =
        {
            (PartSlot.FrontBumper, "Stock", "Заводской бампер", "", 0),
            (PartSlot.FrontBumper, "Sport", "Спортивный бампер", "Губа сплиттера и клыки по углам.", 2500),
            (PartSlot.FrontBumper, "Race", "Гоночный бампер", "Карбоновый сплиттер с торцевыми пластинами и канардами.", 4800),
            (PartSlot.FrontBumper, "Offroad", "Силовой бампер", "Стальной брус, лебёдка, защита и такелажные скобы.", 4200),
            (PartSlot.RearBumper, "Stock", "Заводской бампер", "", 0),
            (PartSlot.RearBumper, "Diffuser", "Диффузор", "Карбоновый диффузор с рёбрами и стоп-сигналом.", 3000),
            (PartSlot.RearBumper, "Offroad", "Силовой бампер", "Стальной брус с фаркопом.", 3500),
            (PartSlot.Skirts, "Stock", "Без порогов", "", 0),
            (PartSlot.Skirts, "Sport", "Спортивные пороги", "Чёрные лезвия вдоль порогов.", 1800),
            (PartSlot.Skirts, "Sliders", "Защитные трубы", "Стальные трубы вдоль порогов.", 2200),
            (PartSlot.Fenders, "Stock", "Заводские арки", "", 0),
            (PartSlot.Fenders, "Wide", "Расширители с заклёпками", "Накладки на арки в цвет кузова.", 3800),
            (PartSlot.Fenders, "Offroad", "Внедорожные расширители", "Широкие пластиковые накладки.", 3000),
            (PartSlot.Hood, "Stock", "Заводской капот", "", 0),
            (PartSlot.Hood, "Vents", "Капот с жабрами", "Две решётки вывода воздуха.", 2400),
            (PartSlot.Hood, "Scoop", "Капот с воздухозаборником", "Выпуклый заборник посередине.", 2800),
            (PartSlot.Hood, "Carbon", "Карбоновый капот", "Накладка из углепластика.", 4500),
            (PartSlot.Grille, "Stock", "Заводская решётка", "", 0),
            (PartSlot.Grille, "Mesh", "Сотовая решётка", "Металлическая сетка-соты.", 1200),
            (PartSlot.Grille, "Bars", "Хромированные планки", "Горизонтальные планки.", 1400),
            (PartSlot.Mirrors, "Stock", "Заводские зеркала", "", 0),
            (PartSlot.Mirrors, "Aero", "Гоночные зеркала", "Карбоновые корпуса на тонких ножках.", 900),
            (PartSlot.Roof, "Stock", "Без багажника", "", 0),
            (PartSlot.Roof, "Rails", "Рейлинги", "Продольные хромированные рейлинги.", 1200),
            (PartSlot.Roof, "Rack", "Поперечины", "Рейлинги с тремя поперечинами.", 1600),
            (PartSlot.Roof, "Expedition", "Экспедиционный багажник", "Корзина с канистрами, ящиком и запаской.", 5200),
            (PartSlot.Roof, "Lightbar", "Светодиодная люстра", "Длинная фара над лобовым стеклом.", 2400),
            (PartSlot.Roof, "Scoop", "Воздухозаборник на крыше", "Раллийный заборник для салона.", 1500),
            (PartSlot.Guard, "Stock", "Без защиты", "", 0),
            (PartSlot.Guard, "Bullbar", "Кенгурятник", "Трубчатая защита перед решёткой.", 2600),
            (PartSlot.Lights, "Stock", "Без дополнительного света", "", 0),
            (PartSlot.Lights, "Rally", "Раллийная люстра", "Четыре круглые фары на кронштейне.", 2600),
            (PartSlot.Lights, "Pods", "Противотуманные фары", "Две фары на бампере.", 1600),
            (PartSlot.Spoiler, "Stock", "Без спойлера", "", 0),
            (PartSlot.Spoiler, "Lip", "Губа на багажнике", "Тонкий спойлер по кромке багажника.", 1500),
            (PartSlot.Spoiler, "Ducktail", "Утиный хвост", "Приподнятая кромка багажника.", 2200),
            (PartSlot.Spoiler, "Wing", "Антикрыло GT", "Карбоновое крыло на стойках.", 4500),
            (PartSlot.Spoiler, "Swan", "Крыло на «лебединых шеях»", "Крупное крыло с креплением сверху.", 6500),
            (PartSlot.Spoiler, "Roof", "Козырёк на крыше", "Спойлер над задним стеклом.", 1800),
            (PartSlot.Spoiler, "Rally", "Раллийное крыло", "Большое крыло на задней кромке крыши.", 3600),
        };

        static readonly CarClass[] Road = { CarClass.Compact, CarClass.Sport, CarClass.Sedan, CarClass.Muscle, CarClass.Rally, CarClass.Super, CarClass.Hyper, CarClass.Classic, CarClass.LuxurySuv };
        static readonly CarClass[] Rough = { CarClass.Rally, CarClass.Offroad, CarClass.LuxurySuv, CarClass.Compact, CarClass.Classic };

        static readonly (string variant, string name, string desc, int price, CarClass[] classes)[] RimParts =
        {
            ("Five", "Пять спиц", "Классика спортивных машин.", 2000, Road),
            ("Ten", "Десять спиц", "Тонкие спицы, лёгкий вид.", 2400, Road),
            ("Six", "Шесть спиц", "Крепкие широкие спицы.", 2000, null),
            ("Split", "Сдвоенные спицы", "Пять пар тонких спиц.", 2800, Road),
            ("Multi", "Многоспицевые", "Двадцать спиц с закруткой.", 3200, Road),
            ("Turbofan", "Турбина", "Сплошной диск с лопатками.", 3000, Road),
            ("Dish", "Ретро-колпак", "Плоский диск с отверстиями.", 1800, null),
            ("Mesh", "Сетка", "Перекрёстные спицы и хромированный обод.", 3400, Road),
            ("Beadlock", "Бэдлок", "Внедорожный диск с болтовым кольцом.", 3000, Rough),
            ("Steel", "Штампованные", "Простые стальные диски.", 900, Rough),
        };

        public static CarCatalog EnsureCarCatalog()
        {
            var catalog = Load<CarCatalog>(CarCatalogPath);
            var specs = PhysicsAssets.EnsureCarSpecs();
            catalog.Cars = CarRoster.Cars.Select(c => new CarEntry
            {
                Key = c.Key,
                DisplayName = c.Name,
                Prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Cars/Car_{c.Key}.prefab"),
                Spec = specs[c.Key],
                Class = c.Class,
                Price = c.Price,
                Unlock = c.Unlock,
                DefaultColor = ArtLibrary.Hex(c.Paint),
                DefaultFinish = PaintFinish.Metallic,
            }).ToList();
            catalog.Palette = Palette.Select(p => new PaletteColor { Name = p.name, Color = ArtLibrary.Hex(p.hex) }).ToList();
            var parts = Kits.Select(k => new PartOption { Slot = k.slot, Variant = k.variant, Name = k.name, Description = k.desc, Price = k.price }).ToList();
            parts.Add(new PartOption { Slot = PartSlot.Rims, Variant = CarBuild.Stock, Name = "Заводские диски" });
            foreach (var r in RimParts)
                parts.Add(new PartOption
                {
                    Slot = PartSlot.Rims, Variant = r.variant, Name = r.name, Description = r.desc, Price = r.price,
                    Prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{BuilderKit.ModelsDir}/Parts/Rim_{r.variant}.fbx"),
                    Classes = r.classes != null ? r.classes.ToList() : new List<CarClass>(),
                });
            parts.Add(new PartOption { Slot = PartSlot.Tires, Variant = CarBuild.Stock, Name = "Дорожные шины", Description = "Заводские шины: сцепление без изменений." });
            parts.Add(new PartOption { Slot = PartSlot.Tires, Variant = "Sport", Name = "Спортивные шины", Description = "Низкий профиль, диски крупнее. Асфальт +5 %, рыхлое покрытие −12 %.", Price = 3000,
                Classes = new List<CarClass> { CarClass.Compact, CarClass.Sport, CarClass.Sedan, CarClass.Muscle, CarClass.Rally, CarClass.Super, CarClass.Hyper, CarClass.Classic } });
            parts.Add(new PartOption { Slot = PartSlot.Tires, Variant = "Offroad", Name = "Внедорожные шины", Description = "Грубый протектор, высокий профиль. Асфальт −7 %, рыхлое покрытие +18 %.", Price = 2500,
                Classes = Rough.ToList() });
            catalog.Parts = parts;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        public static MapCatalog EnsureMapCatalog()
        {
            var catalog = Load<MapCatalog>(MapCatalogPath);
            var maps = new List<MapEntry>
            {
                Map("Hills", "Холмы Рандома", "Автодром в предгорьях: 2.9 км, серпантин со шпилькой, хребет на 40 м выше старта, скоростной спуск и лес. 2 круга.", "Map_Hills", "#6DBB45"),
                Map("City", "Город", "Вечерний город, 3.1 км: проспект на 4 полосы, деловой квартал, бульвар, брусчатка старого города и десятки перекрёстков. 2 круга.", "Map_City", "#FF6A2A"),
                Map("Desert", "Пустыня", "Песок, каньон и длинные скоростные прямые под палящим солнцем.", "Map_Desert", "#E3C48A"),
                Map("TelAviv", "Тель-Авив", "Ночной мегаполис у моря: приморская прямая, вантовый мост, эстакада у небоскрёбов, тоннель и ракеты над городом. 2 круга.", "Map_TelAviv", "#3F7BFF", "TelAviv"),
                // Полигон испытаний: не трасса для гонок — только режим «Полигон испытаний» из главного меню.
                Hidden(Map(ArenaMapBuilder.Key, ArenaMapBuilder.Title, "Испытательный центр: любое усложнение игры на любой машине.", "Map_Arena", "#FFD23F")),
            };
            // В каталог попадают только собранные карты.
            catalog.Maps = maps.Where(m => File.Exists($"Assets/Scenes/{m.SceneName}.unity")).ToList();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        static MapEntry Hidden(MapEntry entry)
        {
            entry.Hidden = true;
            return entry;
        }

        static MapEntry Map(string key, string name, string description, string scene, string accent, string playlist = RandomCar.Audio.MusicLibrary.MainPlaylist) => new MapEntry
        {
            Playlist = playlist,
            Key = key,
            DisplayName = name,
            Description = description,
            SceneName = scene,
            Preview = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/UI/Maps/Map_{key}.png"),
            Accent = ArtLibrary.Hex(accent),
        };

        /// Сцены сборки: Game первой, затем карты. Старая сцена Main больше не используется.
        public static void SetBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(GameScenePath, true) };
            foreach (var m in EnsureMapCatalog().Maps) scenes.Add(new EditorBuildSettingsScene($"Assets/Scenes/{m.SceneName}.unity", true));
            EditorBuildSettings.scenes = scenes.ToArray();
            if (File.Exists("Assets/Scenes/Main.unity")) AssetDatabase.DeleteAsset("Assets/Scenes/Main.unity");
        }

        static T Load<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
