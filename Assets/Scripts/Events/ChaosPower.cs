using UnityEngine;

namespace RandomCar.Events
{
    /// Сила хаоса (v5, «MAXIMUM CHAOS»): насколько сильно события бьют, подбрасывают и крутят машины при выбранном
    /// Хаосе. «Умеренно» сохраняет прежние честные пределы v4, «много» — заметно сильнее, «безумие» (стандарт)
    /// и «абсурд» — без оглядки на баланс: подброс на десятки метров, кувырки во все стороны, удары тел и
    /// конструкций почти без ограничения, машину не выравнивает в воздухе и не тянет обратно к дороге.
    /// Остаются только технические пределы (физика не «взрывается», скорость не бесконечна).
    /// Числа силы в событиях записаны «для безумия» (Punch = 1); остальные уровни их масштабируют.
    public readonly struct ChaosPower
    {
        /// Множитель силы событий (1 — «безумие»).
        public readonly float Punch;
        /// Подброс вверх не сильнее, м/с.
        public readonly float MaxUp;
        /// Отброс вбок и вдоль не сильнее, м/с.
        public readonly float MaxSide;
        /// Закручивание (любая ось) не быстрее, рад/с.
        public readonly float MaxSpin;
        /// Удар физического тела или конструкции события меняет скорость машины не больше, м/с.
        public readonly float ImpactDeltaV;
        /// Сколько секунд после сильного подброса машину не выравнивает в воздухе (свободный кувырок), с.
        public readonly float TumbleSeconds;
        /// Сила «воздушного поводка» после подброса (1 — как в v4, 0 — нет).
        public readonly float Guide;
        /// Множитель старых толчков (ApplyImpulse: мяч, ветер, столбы).
        public readonly float ImpulseScale;
        /// Сила тяжести в «невесомости» и «гравитационном сбое» не ниже (минус — тянет вверх).
        public readonly float MinGravity;
        /// Рывок не больше (множитель ограничителя скорости).
        public readonly float MaxBoost;
        /// Замедление не ниже этой доли скорости.
        public readonly float MinSlow;
        /// Летящая после события машина сбивает другие («бильярд»).
        public readonly bool CarBilliards;
        /// Жёсткое приземление после подброса — отскок и ударная волна.
        public readonly bool LandingBounce;

        ChaosPower(float punch, float up, float side, float spin, float impact, float tumble, float guide, float impulse,
                   float gravity, float boost, float slow, bool billiards, bool bounce)
        {
            Punch = punch;
            MaxUp = up;
            MaxSide = side;
            MaxSpin = spin;
            ImpactDeltaV = impact;
            TumbleSeconds = tumble;
            Guide = guide;
            ImpulseScale = impulse;
            MinGravity = gravity;
            MaxBoost = boost;
            MinSlow = slow;
            CarBilliards = billiards;
            LandingBounce = bounce;
        }

        /// Сила одна на всех уровнях Хаоса (решение пользователя 2026-10-29: Хаос — только частота событий).
        /// «Выключен» — честные пределы v4 (событий нет, остаются бонусы).
        public static ChaosPower For(ChaosLevel chaos) => chaos == ChaosLevel.Off
            ? new ChaosPower(0.5f, FairnessLimits.MaxLaunchUp, FairnessLimits.MaxImpulse, FairnessLimits.MaxSpin,
                             FairnessLimits.MaxEventImpactDeltaV, 0f, 1f, 1f, FairnessLimits.MinGravityScale,
                             FairnessLimits.MaxBoostFactor, FairnessLimits.MinSlowFactor, false, false)
            : new ChaosPower(0.8f, 30f, 24f, 8f, 32f, 1.1f, 0f, 1.8f, -0.7f, 1.45f, 0.3f, true, true);

        static ChaosLevel level = ChaosLevel.Mayhem;
        static ChaosPower current = For(ChaosLevel.Mayhem);

        /// Сила текущей гонки (задаёт директор событий по Хаосу; у клиента сети — по Хаосу хоста).
        public static ChaosPower Current => current;
        public static ChaosLevel Level => level;

        public static void Set(ChaosLevel chaos)
        {
            if (chaos == level) return;
            level = chaos;
            current = For(chaos);
        }

        /// Без оглядки на баланс: «безумие» и «абсурд».
        public bool Wild => TumbleSeconds > 1f;

        /// Скорость отброса после масштаба и пределов.
        public Vector3 Scale(Vector3 velocity)
        {
            Vector3 v = velocity * Punch;
            Vector3 horizontal = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(v, Vector3.up), MaxSide);
            return horizontal + Vector3.up * Mathf.Clamp(v.y, 0f, MaxUp);
        }

        public Vector3 ScaleSpin(Vector3 spin) => Vector3.ClampMagnitude(spin * Mathf.Lerp(1f, Punch, 0.5f), MaxSpin);
    }
}
