namespace RandomCar.Events
{
    /// Правила честности событий из AGENTS.md. Все эффекты на машины проходят через CarController,
    /// который сам обрезает длительности и силу по этим константам.
    public static class FairnessLimits
    {
        /// Предупреждение каждого события не короче этого времени, с.
        public const float MinWarningSeconds = 1.5f;

        /// Замедление не длиннее, с.
        public const float MaxSlowSeconds = 1.5f;

        /// Замедление не сильнее: скорость падает не ниже этой доли.
        public const float MinSlowFactor = 0.45f;

        /// Буст ровно столько, с.
        public const float BoostSeconds = 2f;

        /// Буст не больше +25%.
        public const float MaxBoostFactor = 1.25f;

        /// Занос (скользко) не дольше, с.
        public const float MaxSlipSeconds = 1.5f;

        /// Импульс от события не сильнее, м/с. Отбрасывание без урона и без выбывания.
        public const float MaxImpulse = 10f;

        /// Половина ширины дороги, м.
        public const float RoadHalfWidth = 6f;

        /// Зона опасности не шире половины дороги: радиус не больше четверти ширины.
        public const float MaxHazardRadius = RoadHalfWidth / 2f;

        /// Прицел ставится на полосу цели со случайным сдвигом не больше, м.
        public const float TargetLateralJitter = 1f;
    }
}
