using UnityEngine;

namespace RandomCar.Events
{
    /// Правила честности событий из AGENTS.md. Все эффекты на машины проходят через CarController,
    /// который сам обрезает длительности и силу по этим константам. Место события выбирает EventPlacer.
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

        /// Половина ширины самой узкой дороги, м (у каждой трассы своя — TrackPath.HalfWidth).
        public const float RoadHalfWidth = 6f;

        /// Зона опасности не шире половины дороги: радиус не больше четверти ширины.
        public const float MaxHazardRadius = RoadHalfWidth / 2f;

        /// То же правило для дороги заданной полуширины.
        public static float HazardRadiusFor(float halfWidth) => Mathf.Max(MaxHazardRadius, halfWidth) / 2f;

        /// Дольше этого событие не ждёт игрока в предупреждении, с (потом срабатывает само).
        public const float MaxHoldSeconds = 9f;

        /// Дольше этого зона не держится, даже если игрок ещё не доехал, с.
        public const float MaxSustainSeconds = 14f;

        /// Запас на реакцию сверх предупреждения: ни одна машина не доедет до зоны раньше, с.
        public const float MinReactionSeconds = 1.0f;

        /// Скорость, с которой машина может оказаться у зоны, считается с запасом: max(текущая, эта) × 1.15, м/с.
        public const float AssumedMinSpeed = 22f;

        /// Минимальный зазор между зонами событий вдоль трассы, м (меньше — цепочки событий нечитаемы).
        public const float ZoneGap = 18f;

        // ---------- хаос v4 ----------

        /// Подброс вверх (батут, мина) не сильнее, м/с: прыжок до ~8 м, приземление управляемое.
        public const float MaxLaunchUp = 13f;

        /// Мультяшное закручивание (мина, перчатка) не быстрее, рад/с: занос, а не волчок.
        public const float MaxSpin = 2.2f;

        /// Невесомость: сила тяжести не ниже этой доли.
        public const float MinGravityScale = 0.3f;

        /// Изменение вида машины (ворота-уменьшатель) — только визуально, не дольше, с.
        public const float MaxVisualScaleSeconds = 4f;

        /// Удар физического объекта или движущейся конструкции меняет скорость машины не больше, м/с:
        /// никого не выбрасывает за пределы карты.
        public const float MaxEventImpactDeltaV = 12f;
    }
}
