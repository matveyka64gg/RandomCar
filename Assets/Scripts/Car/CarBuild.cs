using System;
using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Car
{
    /// Слоты тюнинга. Обвес (бамперы, пороги, арки, капот, решётка, свет, зеркала, крыша, защита, спойлер)
    /// построен под каждую машину; диски — общие детали; шины влияют на сцепление (явно, см. TuningSetup).
    public enum PartSlot { FrontBumper, RearBumper, Skirts, Fenders, Hood, Grille, Lights, Mirrors, Roof, Guard, Spoiler, Rims, Tires }

    /// Покрытие краски: металличность, глянец и лак.
    public enum PaintFinish { Gloss, Metallic, Pearl, Satin, Matte, Chrome }

    /// Двухцветная схема: какие зоны кузова красятся акцентным цветом.
    public enum PaintScheme { Solid, Roof, Hood, RoofAndHood, Lower, LowerAndRoof }

    /// Цвет деталей обвеса: в цвет кузова, чёрные или акцентные.
    public enum PartsColor { Body, Black, Accent }

    [Serializable]
    public sealed class PartChoice
    {
        public PartSlot Slot;
        public string Variant = CarBuild.Stock;
    }

    /// Окраска машины.
    [Serializable]
    public sealed class PaintJob
    {
        public Color Body = new Color(0.85f, 0.25f, 0.15f);
        public PaintFinish BodyFinish = PaintFinish.Metallic;
        public Color Accent = new Color(0.08f, 0.08f, 0.09f);
        public PaintFinish AccentFinish = PaintFinish.Gloss;
        public PaintScheme Scheme = PaintScheme.Solid;
        public PartsColor Parts = PartsColor.Body;
        public Color Rims = new Color(0.78f, 0.8f, 0.84f);
        public PaintFinish RimFinish = PaintFinish.Metallic;
        public Color Calipers = new Color(0.78f, 0.12f, 0.14f);
        /// Тонировка стёкол 0..1 (0 — прозрачные, 1 — почти чёрные).
        [Range(0f, 1f)] public float WindowTint = 0.35f;
    }

    /// Куда проецируется наклейка.
    public enum DecalSide { Left, Right, Hood, Roof, Trunk, Front, Rear }

    /// Слой наклейки (винила). Положение — доли габарита выбранной стороны машины.
    [Serializable]
    public sealed class DecalLayer
    {
        /// "builtin:<id>", "number:<стиль>" или "custom:<хэш>" (пользовательская картинка).
        public string Source = "";
        public int Number = 7;
        public DecalSide Side = DecalSide.Left;
        /// Центр: U — вдоль (назад → вперёд для бортов, слева → направо сверху), V — снизу вверх; 0..1.
        public float U = 0.5f;
        public float V = 0.5f;
        /// Размер — доля длины стороны (0.05..1.2).
        public float Size = 0.35f;
        /// Растяжение по ширине (0.3..3).
        public float Aspect = 1f;
        /// Поворот, градусы.
        public float Rotation;
        public Color Tint = Color.white;
        [Range(0.1f, 1f)] public float Opacity = 1f;
        /// Повторить на противоположной стороне (зеркально).
        public bool Mirror;
        public bool FlipX;

        public bool IsCustom => Source.StartsWith("custom:", StringComparison.Ordinal);
    }

    /// Настройка поведения: только компромиссы, видимые в цифрах. Значения -1..1, 0 — заводская.
    [Serializable]
    public sealed class TuningSetup
    {
        /// Главная пара: минус — короче (резвее разгон, ниже максималка), плюс — длиннее.
        [Range(-1f, 1f)] public float Gearing;
        /// Подвеска: минус — мягче (кочки, грунт), плюс — жёстче (меньше крен, острее руль).
        [Range(-1f, 1f)] public float Suspension;
        /// Баланс тормозов: минус — к задней оси (машина охотнее поворачивает), плюс — к передней (стабильнее).
        [Range(-1f, 1f)] public float BrakeBias;

        public bool IsStock => Mathf.Abs(Gearing) < 0.01f && Mathf.Abs(Suspension) < 0.01f && Mathf.Abs(BrakeBias) < 0.01f;
    }

    /// Сборка машины: обвес, окраска, наклейки, настройка. Сохраняется по машинам (несколько сборок на машину)
    /// и передаётся по сети как JSON (после Sanitize на стороне хоста).
    [Serializable]
    public sealed class CarBuild
    {
        public const string Stock = "Stock";
        public const int MaxDecals = 24;
        public const int MaxNameLength = 24;

        public string Name = "Повседневная";
        public List<PartChoice> Parts = new List<PartChoice>();
        public PaintJob Paint = new PaintJob();
        public List<DecalLayer> Decals = new List<DecalLayer>();
        public TuningSetup Tuning = new TuningSetup();

        public string Get(PartSlot slot)
        {
            foreach (var p in Parts)
                if (p.Slot == slot) return string.IsNullOrEmpty(p.Variant) ? Stock : p.Variant;
            return Stock;
        }

        public void Set(PartSlot slot, string variant)
        {
            foreach (var p in Parts)
                if (p.Slot == slot)
                {
                    p.Variant = variant;
                    return;
                }
            Parts.Add(new PartChoice { Slot = slot, Variant = variant });
        }

        public CarBuild Clone() => JsonUtility.FromJson<CarBuild>(JsonUtility.ToJson(this));

        public string ToJson() => JsonUtility.ToJson(this);

        /// Чтение из JSON (сохранение, сеть). Повреждённые данные — null.
        public static CarBuild FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonUtility.FromJson<CarBuild>(json);
            }
            catch (ArgumentException error)
            {
                Debug.LogWarning($"[RandomCar] Сборка машины не прочитана: {error.Message}");
                return null;
            }
        }

        /// Приведение к допустимому: неизвестные и несовместимые детали → заводские, числа — в пределах,
        /// наклеек не больше MaxDecals. allowCustomDecals = false убирает пользовательские картинки (чужие сборки в сети).
        public void Sanitize(CarEntry car, CarCatalog catalog, bool allowCustomDecals = true)
        {
            Name = string.IsNullOrWhiteSpace(Name) ? "Сборка" : Name.Trim();
            if (Name.Length > MaxNameLength) Name = Name.Substring(0, MaxNameLength);
            Parts ??= new List<PartChoice>();
            var seen = new HashSet<PartSlot>();
            for (int i = Parts.Count - 1; i >= 0; i--)
            {
                var p = Parts[i];
                if (p == null || !Enum.IsDefined(typeof(PartSlot), p.Slot) || !seen.Add(p.Slot))
                {
                    Parts.RemoveAt(i);
                    continue;
                }
                if (p.Variant != Stock && (catalog == null || car == null || !catalog.IsCompatible(car, p.Slot, p.Variant)))
                    p.Variant = Stock;
            }
            Paint ??= new PaintJob();
            Paint.Body = Opaque(Paint.Body);
            Paint.Accent = Opaque(Paint.Accent);
            Paint.Rims = Opaque(Paint.Rims);
            Paint.Calipers = Opaque(Paint.Calipers);
            Paint.WindowTint = Mathf.Clamp01(Paint.WindowTint);
            Paint.BodyFinish = ClampEnum(Paint.BodyFinish);
            Paint.AccentFinish = ClampEnum(Paint.AccentFinish);
            Paint.RimFinish = ClampEnum(Paint.RimFinish);
            Paint.Scheme = ClampEnum(Paint.Scheme);
            Paint.Parts = ClampEnum(Paint.Parts);
            Decals ??= new List<DecalLayer>();
            Decals.RemoveAll(d => d == null || string.IsNullOrEmpty(d.Source) || (!allowCustomDecals && d.IsCustom));
            if (Decals.Count > MaxDecals) Decals.RemoveRange(MaxDecals, Decals.Count - MaxDecals);
            foreach (var d in Decals)
            {
                d.Side = ClampEnum(d.Side);
                d.U = Mathf.Clamp(d.U, -0.1f, 1.1f);
                d.V = Mathf.Clamp(d.V, -0.1f, 1.1f);
                d.Size = Mathf.Clamp(d.Size, 0.04f, 1.2f);
                d.Aspect = Mathf.Clamp(d.Aspect, 0.25f, 4f);
                d.Rotation = Mathf.Repeat(d.Rotation + 180f, 360f) - 180f;
                d.Opacity = Mathf.Clamp(d.Opacity, 0.1f, 1f);
                d.Number = Mathf.Clamp(d.Number, 0, 99);
                d.Tint = Opaque(d.Tint);
            }
            Tuning ??= new TuningSetup();
            Tuning.Gearing = Mathf.Clamp(Tuning.Gearing, -1f, 1f);
            Tuning.Suspension = Mathf.Clamp(Tuning.Suspension, -1f, 1f);
            Tuning.BrakeBias = Mathf.Clamp(Tuning.BrakeBias, -1f, 1f);
        }

        static Color Opaque(Color c) => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);

        static T ClampEnum<T>(T value) where T : struct, Enum => Enum.IsDefined(typeof(T), value) ? value : default;

        /// Заводская сборка машины: её цвет, остальное — стандартное.
        public static CarBuild Default(CarEntry car)
        {
            var build = new CarBuild { Name = "Заводская" };
            if (car != null)
            {
                build.Paint.Body = car.DefaultColor;
                build.Paint.BodyFinish = car.DefaultFinish;
            }
            return build;
        }
    }
}
