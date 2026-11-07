namespace RandomCar.Car
{
    /// Управление машиной: одинаковое для игрока, ИИ и сети.
    public struct CarInput
    {
        /// Газ 0..1.
        public float Throttle;
        /// Тормоз 0..1 (на месте — задний ход).
        public float Brake;
        /// Руль -1 (влево) .. 1 (вправо).
        public float Steer;
        public bool Handbrake;

        public static CarInput None => default;
    }

    public enum AssistLevel { Off, Standard, Strong }

    /// Помощь при вождении. Одинаковые правила для всех; ИИ ездит со «стандартом».
    public readonly struct DrivingAssists
    {
        /// Разрешённая пробуксовка сверх сцепления (0 — нет пробуксовки, большое — без контроля).
        public readonly float TractionSlip;
        /// ABS: тормоз не блокирует колёса.
        public readonly bool Abs;
        /// Сила стабилизации рыскания 0..1.
        public readonly float Stability;
        /// Угол заноса кузова, после которого включается стабилизация, градусы.
        public readonly float StabilityAngle;
        /// Помощь контррулём 0..1.
        public readonly float CounterSteer;
        /// Ограничение угла руля по сцеплению: множитель допустимого угла (больше — свободнее).
        public readonly float SteerLimit;

        public DrivingAssists(float tractionSlip, bool abs, float stability, float stabilityAngle, float counterSteer, float steerLimit)
        {
            TractionSlip = tractionSlip;
            Abs = abs;
            Stability = stability;
            StabilityAngle = stabilityAngle;
            CounterSteer = counterSteer;
            SteerLimit = steerLimit;
        }

        public static DrivingAssists For(AssistLevel level)
        {
            switch (level)
            {
                case AssistLevel.Off: return new DrivingAssists(100f, false, 0f, 90f, 0f, 1.8f);
                case AssistLevel.Strong: return new DrivingAssists(0f, true, 1f, 14f, 0.6f, 1.05f);
                default: return new DrivingAssists(0.06f, true, 0.7f, 22f, 0.35f, 1.25f);
            }
        }
    }
}
