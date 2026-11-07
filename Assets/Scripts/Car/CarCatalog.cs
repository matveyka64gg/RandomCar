using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Car
{
    /// Класс машины: подбор соперников, совместимость деталей, раздел гаража.
    public enum CarClass { Compact, Sport, Sedan, Muscle, Rally, Offroad, LuxurySuv, Super, Hyper, Classic }

    [System.Serializable]
    public sealed class CarEntry
    {
        public string Key;
        public string DisplayName;
        public GameObject Prefab;
        public CarSpec Spec;
        public CarClass Class;
        /// Цена в кредитах; 0 — доступна сразу.
        public int Price;
        /// Условие открытия (пусто — только цена): "wins:3", "races:10", "map:TelAviv".
        public string Unlock = "";
        public Color DefaultColor = Color.red;
        public PaintFinish DefaultFinish = PaintFinish.Metallic;
    }

    /// Готовый цвет палитры гаража (быстрый выбор; любой другой цвет — ползунками).
    [System.Serializable]
    public sealed class PaletteColor
    {
        public string Name;
        public Color Color = Color.white;
    }

    /// Деталь тюнинга. Обвес (Prefab = null) построен в каждой машине: доступен, если в её префабе есть
    /// вариант Kit/<Slot>/<Variant>. Диски — общие модели (Prefab), подгоняются под колесо машины.
    /// Детали только меняют вид; влияние шин на сцепление показано в описании и в настройке.
    [System.Serializable]
    public sealed class PartOption
    {
        public PartSlot Slot;
        public string Variant;
        public string Name;
        [TextArea] public string Description;
        public int Price;
        public GameObject Prefab;
        /// Для общих деталей: подходящие классы машин (пусто — все).
        public List<CarClass> Classes = new List<CarClass>();

        public string Id => $"{Slot}.{Variant}";
    }

    /// Машины, детали и палитра гаража. Физика машины читает только CarSpec (и явную настройку из сборки).
    [CreateAssetMenu(menuName = "RandomCar/Car Catalog")]
    public sealed class CarCatalog : ScriptableObject
    {
        public List<CarEntry> Cars = new List<CarEntry>();
        public List<PartOption> Parts = new List<PartOption>();
        public List<PaletteColor> Palette = new List<PaletteColor>();

        public CarEntry Get(string key) => Cars.Find(c => c.Key == key) ?? (Cars.Count > 0 ? Cars[0] : null);
        public int IndexOf(string key) => Mathf.Max(0, Cars.FindIndex(c => c.Key == key));

        public PartOption Part(PartSlot slot, string variant) => Parts.Find(p => p.Slot == slot && p.Variant == variant);

        /// Деталь подходит машине: обвес — есть в префабе; общая деталь — по классу.
        public bool IsCompatible(CarEntry car, PartSlot slot, string variant)
        {
            if (variant == CarBuild.Stock) return true;
            var part = Part(slot, variant);
            if (part == null || car == null) return false;
            if (part.Prefab == null && slot != PartSlot.Tires)
            {
                var cosmetics = car.Prefab != null ? car.Prefab.GetComponent<CarCosmetics>() : null;
                return cosmetics != null && cosmetics.HasKit(slot, variant);
            }
            if (slot == PartSlot.Tires)
            {
                var cosmetics = car.Prefab != null ? car.Prefab.GetComponent<CarCosmetics>() : null;
                if (cosmetics == null || !cosmetics.HasTire(variant)) return false;
            }
            return part.Classes.Count == 0 || part.Classes.Contains(car.Class);
        }

        /// Варианты слота для машины: «заводской» первым, затем совместимые детали каталога.
        public List<PartOption> PartsFor(CarEntry car, PartSlot slot)
        {
            var list = new List<PartOption>();
            foreach (var p in Parts)
                if (p.Slot == slot && IsCompatible(car, slot, p.Variant)) list.Add(p);
            return list;
        }
    }
}
