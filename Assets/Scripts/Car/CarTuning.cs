using UnityEngine;

namespace RandomCar.Car
{
    /// Влияние сборки на физику: шины и настройка. Только компромиссы с явными цифрами; ИИ и игрок
    /// с одинаковой сборкой получают одинаковые характеристики. Детали обвеса физику не меняют.
    public static class CarTuning
    {
        public const float GearingRange = 0.10f;      // главная пара ±10 %
        public const float TopSpeedRange = 0.06f;     // ограничитель скорости ±6 %
        public const float SpringRange = 0.15f;       // частота подвески ±15 %
        public const float AntiRollRange = 0.30f;     // стабилизаторы ±30 %
        public const float TravelRange = 0.10f;       // ход подвески ∓10 %
        public const float BrakeShareRange = 0.08f;   // доля передних тормозов ±8 %

        public struct TireEffect
        {
            public float Tarmac;
            public float Loose;
        }

        /// Шины: множители сцепления на асфальте и на рыхлом покрытии (гравий, песок, трава, грунт).
        public static TireEffect Tires(string tire) => tire switch
        {
            "Sport" => new TireEffect { Tarmac = 1.05f, Loose = 0.88f },
            "Offroad" => new TireEffect { Tarmac = 0.93f, Loose = 1.18f },
            _ => new TireEffect { Tarmac = 1f, Loose = 1f },
        };

        /// Характеристики с учётом сборки. Без изменений — тот же ассет; иначе копия (её уничтожает владелец).
        public static CarSpec Apply(CarSpec spec, CarBuild build)
        {
            if (spec == null || build == null) return spec;
            string tire = build.Get(PartSlot.Tires);
            var t = build.Tuning ?? new TuningSetup();
            if (tire == CarBuild.Stock && t.IsStock) return spec;

            var s = Object.Instantiate(spec);
            s.name = spec.name + "_Tuned";
            float g = Mathf.Clamp(t.Gearing, -1f, 1f);
            s.Gearbox.FinalDrive *= 1f - GearingRange * g;
            s.TopSpeedKmh *= 1f + TopSpeedRange * g;

            float v = Mathf.Clamp(t.Suspension, -1f, 1f);
            foreach (var axle in new[] { s.Front, s.Rear })
            {
                axle.SpringFrequency *= 1f + SpringRange * v;
                axle.AntiRoll *= 1f + AntiRollRange * v;
                axle.Travel *= 1f - TravelRange * v;
                axle.DampingRatio = Mathf.Clamp(axle.DampingRatio + 0.05f * v, 0.15f, 1.1f);
            }

            float b = Mathf.Clamp(t.BrakeBias, -1f, 1f);
            float front = Mathf.Clamp(spec.Front.BrakeShare + BrakeShareRange * b, 0.4f, 0.8f);
            s.Front.BrakeShare = front;
            s.Rear.BrakeShare = 1f - front;

            var tires = Tires(tire);
            foreach (var axle in new[] { s.Front, s.Rear })
            {
                axle.LateralGrip *= tires.Tarmac;
                axle.LongitudinalGrip *= tires.Tarmac;
            }
            s.LooseSurfaceGrip *= tires.Loose / tires.Tarmac;
            return s;
        }

        /// Пояснение для гаража: что меняет значение настройки.
        public static string Describe(string what, float value)
        {
            int pct(float range) => Mathf.RoundToInt(Mathf.Abs(value) * range * 100f);
            if (Mathf.Abs(value) < 0.01f) return "Заводская настройка.";
            switch (what)
            {
                case "gearing":
                    return value < 0
                        ? $"Короче на {pct(GearingRange)} %: быстрее разгон, максималка ниже на {pct(TopSpeedRange)} %."
                        : $"Длиннее на {pct(GearingRange)} %: медленнее разгон, максималка выше на {pct(TopSpeedRange)} %.";
                case "suspension":
                    return value < 0
                        ? $"Мягче на {pct(SpringRange)} %: лучше на кочках и грунте, больше крен и раскачка."
                        : $"Жёстче на {pct(SpringRange)} %: меньше крен, острее руль, сильнее трясёт на неровностях.";
                default:
                    return value < 0
                        ? $"Больше тормозов сзади (+{pct(BrakeShareRange)} %): машина охотнее поворачивает на торможении, легче сорвать в занос."
                        : $"Больше тормозов спереди (+{pct(BrakeShareRange)} %): торможение стабильнее, машина хуже поворачивает на тормозах.";
            }
        }
    }
}
