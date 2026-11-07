using UnityEngine;

namespace RandomCar.Events
{
    /// Накал гонки: растёт с долей пройденной гонки (не со временем). Управляет не одним таймером, а набором:
    /// промежутком между встречами с событиями, числом одновременных, допуском тяжёлых, комбинациями,
    /// долей событий «на твоей полосе» и запасом времени на реакцию. Хаос задаёт базу частоты.
    /// С v5 («MAXIMUM CHAOS») в «безумии» и «абсурде» накал не нужен для настоящего хаоса: он стартует сразу
    /// с третьей ступени («жарко»), тяжёлые, комбинации и сцены открыты с первой секунды, предупреждения короткие,
    /// события ставятся вплотную к машинам. Эскалация по кругам остаётся только добавкой сверху.
    public readonly struct EventIntensity
    {
        /// 0 — начало первого круга, 1 — конец гонки.
        public readonly float Level;
        public readonly ChaosLevel Chaos;

        public EventIntensity(float raceFraction, ChaosLevel chaos)
        {
            float f = Mathf.Clamp01(raceFraction);
            float ramp = Mathf.Pow(f, 0.85f);
            Level = chaos != ChaosLevel.Off ? Mathf.Clamp01(0.62f + 0.38f * ramp) : Mathf.Clamp01(0.18f + 0.82f * ramp);
            Chaos = chaos;
        }

        /// «Безумие» или «абсурд»: хаос без оглядки на баланс.
        /// Поведение событий одно на всех уровнях (Хаос — только частота).
        public bool Wild => Chaos != ChaosLevel.Off;

        bool Absurd => false;

        /// Ступень для интерфейса: 1 — знакомство, 2 — разгон, 3 — жарко, 4 — финальный штурм.
        public int Stage => Level < 0.38f ? 1 : Level < 0.6f ? 2 : Level < 0.82f ? 3 : 4;

        /// Множитель промежутка между событиями (к интервалу Хаоса): к концу гонки встречи почти вдвое чаще.
        public float GapFactor => Wild ? Mathf.Lerp(1f, 0.7f, (Level - 0.62f) / 0.38f) : Mathf.Lerp(1.15f, 0.6f, Level);

        /// Множитель промежутка между трюками: к финалу ещё чаще (не обязателен — трюки часты с первой секунды).
        public float GagFactor => Wild ? Mathf.Lerp(1f, 0.75f, (Level - 0.62f) / 0.38f) : Mathf.Lerp(1.1f, 0.75f, Level);

        /// Сколько событий может идти одновременно: база Хаоса плюс одно на высоком накале (в «безумии» — больше).
        public int MaxActive(int chaosBase) => chaosBase <= 0 ? 0 : chaosBase + (Level >= 0.6f ? 1 : 0) + (Wild && Level >= 0.85f ? 3 : 0);

        /// Тяжёлые события — после знакомства с остальными (в «безумии» — сразу).
        public bool AllowHeavy => Wild || Level >= 0.34f;

        /// Вероятность комбинации: к событию добавляется второе (сразу за ним или поверх того же участка).
        public float ComboChance
        {
            get
            {
                if (Wild) return Mathf.Lerp(Absurd ? 0.7f : 0.55f, Absurd ? 0.95f : 0.85f, (Level - 0.62f) / 0.38f);
                const float from = 0.42f;
                if (Level < from) return 0f;
                float t = (Level - from) / (1f - from);
                return Chaos switch
                {
                    ChaosLevel.Many => Mathf.Lerp(0.2f, 0.55f, t),
                    _ => Mathf.Lerp(0.15f, 0.4f, t),
                };
            }
        }

        /// Доля комбинаций, собранных как сцена из готового рецепта (3–4 события), а не из случайной пары.
        public float SceneChance => Chaos switch
        {
            ChaosLevel.Absurd => 0.7f,
            ChaosLevel.Mayhem => Mathf.Lerp(0.45f, 0.7f, Level),
            ChaosLevel.Many => Mathf.Lerp(0.15f, 0.4f, Level),
            _ => Mathf.Lerp(0.05f, 0.25f, Level),
        };

        /// Доля плохих событий, поставленных на полосу игрока: придётся менять траекторию (в «безумии» — почти всегда).
        public float AimChance => Wild ? 0.55f : Mathf.Lerp(0.4f, 0.7f, Level);

        /// Запас времени до встречи с событием сверх предупреждения, с: в «безумии» запаса нет — прямо перед носом.
        public float ExtraLead => Wild ? 0f : Mathf.Lerp(2.2f, 1.1f, Level);

        /// Предупреждение короче во столько раз (в «безумии» — вдвое, в «абсурде» — ещё короче).
        public float WarningScale => Chaos switch
        {
            ChaosLevel.Off => 1f,
            _ => 0.7f,
        };

        /// Предупреждение не короче, с.
        public float MinWarning => Chaos switch
        {
            ChaosLevel.Off => FairnessLimits.MinWarningSeconds,
            _ => 1.1f,
        };

        /// Дольше этого без событий впереди игрок не едет: страховка от пустых отрезков, с.
        public float MaxQuiet => Chaos switch
        {
            ChaosLevel.Absurd => 0.8f,
            ChaosLevel.Mayhem => 1.8f,
            ChaosLevel.Many => Mathf.Lerp(4.5f, 2.2f, Level),
            _ => Mathf.Lerp(7f, 4f, Level),
        };
    }
}
