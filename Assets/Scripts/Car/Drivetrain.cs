using UnityEngine;

namespace RandomCar.Car
{
    /// Входные данные трансмиссии на шаг физики.
    public struct DriveState
    {
        /// Педаль газа 0..1 (в режиме задней передачи — уже «газ назад»).
        public float Throttle;
        /// Педаль тормоза 0..1 (сырой ввод водителя, до перестановки педалей на задней).
        public float Brake;
        /// Сырой газ водителя 0..1 (для выбора задней передачи).
        public float RawThrottle;
        /// Продольная скорость машины, м/с (минус — назад).
        public float ForwardSpeed;
        /// Средняя угловая скорость ведущих колёс, рад/с (знак — направление вращения).
        public float WheelOmega;
        /// Угловая скорость, которая была бы у колёс без пробуксовки (по скорости машины), рад/с.
        /// По ней коробка решает о переключениях: буксующие колёса не должны гонять передачи туда-сюда.
        public float GroundOmega;
        /// Машина заблокирована (отсчёт, финиш): нейтраль, мотор крутится вхолостую.
        public bool Locked;
        /// Ограничитель скорости, м/с.
        public float TopSpeed;
    }

    /// Двигатель и автоматическая коробка. Обороты считаются от ведущих колёс (со сцеплением, которое
    /// пробуксовывает при старте), момент — по кривой двигателя с наддувом турбины, торможение двигателем
    /// при сброшенном газе. Переключение передач рвёт тягу на время ShiftTime.
    /// Чистый C#, без Unity-объектов: тестируется в EditMode.
    public sealed class Drivetrain
    {
        const float LimiterCut = 0.06f;
        const float ShiftCooldown = 0.45f;
        const float ReverseEngageDelay = 0.3f;

        readonly CarSpec spec;
        float shiftTimer;
        float cooldown;
        float limiterTimer;
        float reverseHold;

        public Drivetrain(CarSpec carSpec, Turbo turbo)
        {
            spec = carSpec;
            Turbo = turbo;
            Reset();
        }

        public Turbo Turbo { get; }

        /// -1 — задняя, 1..N — передачи вперёд.
        public int Gear { get; private set; } = 1;

        /// Обороты двигателя.
        public float Rpm { get; private set; }

        public float Rpm01 => Mathf.InverseLerp(0f, spec.Engine.MaxRpm, Rpm);

        /// Идёт переключение: тяга разорвана.
        public bool Shifting => shiftTimer > 0f;

        /// Отсечка: двигатель на мгновение глушит подачу.
        public bool OnLimiter => limiterTimer > 0f;

        /// Реальное открытие дросселя после переключений и отсечки (нагрузка для звука и турбины).
        public float Load { get; private set; }

        /// Момент двигателя на маховике за последний шаг, Н·м.
        public float EngineTorque { get; private set; }

        public bool Reversing => Gear < 0;

        /// Переключение: (откуда, куда).
        public event System.Action<int, int> Shifted;

        public void Reset()
        {
            Gear = 1;
            Rpm = spec.Engine.IdleRpm;
            shiftTimer = cooldown = limiterTimer = reverseHold = 0f;
            Load = 0f;
            EngineTorque = 0f;
            Turbo.Reset();
        }

        public float Ratio(int gear)
        {
            var g = spec.Gearbox;
            if (gear < 0) return g.Reverse * g.FinalDrive;
            if (gear == 0) return 0f;
            return g.Ratios[Mathf.Clamp(gear, 1, g.Ratios.Length) - 1] * g.FinalDrive;
        }

        /// Шаг: возвращает суммарный момент на ведущих колёсах, Н·м (знак — направление движения машины).
        public float Step(in DriveState s, float dt)
        {
            if (dt <= 0f) return 0f;
            var e = spec.Engine;
            cooldown = Mathf.Max(0f, cooldown - dt);
            limiterTimer = Mathf.Max(0f, limiterTimer - dt);
            if (shiftTimer > 0f) shiftTimer = Mathf.Max(0f, shiftTimer - dt);

            if (!s.Locked) SelectDirection(s, dt);

            float ratio = Ratio(Gear);
            float wheelRpm = Mathf.Abs(s.WheelOmega) * ratio * 30f / Mathf.PI;

            float groundRpm = Mathf.Abs(s.GroundOmega != 0f ? s.GroundOmega : s.WheelOmega) * ratio * 30f / Mathf.PI;
            if (!s.Locked && Gear > 0) AutoShift(s, groundRpm);
            ratio = Ratio(Gear);
            wheelRpm = Mathf.Abs(s.WheelOmega) * ratio * 30f / Mathf.PI;

            float throttle = Mathf.Clamp01(s.Throttle);
            float target;
            float rate;
            if (s.Locked)
            {
                // Нейтраль: мотор свободно раскручивается газом.
                target = Mathf.Lerp(e.IdleRpm, e.MaxRpm * 0.96f, throttle);
                rate = 1f - Mathf.Exp(-dt * e.FreeRevRate / Mathf.Max(1f, e.MaxRpm) * 2.2f);
            }
            else
            {
                // Сцепление пробуксовывает на первой и задней, пока колёса медленные.
                float clutch = Mathf.Abs(Gear) == 1 ? Mathf.Lerp(e.IdleRpm, e.LaunchRpm, throttle) : e.IdleRpm;
                target = Mathf.Max(wheelRpm, clutch);
                if (Shifting) target = Mathf.Max(wheelRpm, e.IdleRpm);
                rate = 1f - Mathf.Exp(-dt * 22f);
            }
            Rpm = Mathf.Clamp(Rpm + (target - Rpm) * rate, e.IdleRpm * 0.9f, e.MaxRpm * 1.01f);

            if (Rpm >= e.MaxRpm * 0.995f && throttle > 0.05f && limiterTimer <= 0f) limiterTimer = LimiterCut;

            float load = throttle;
            if (Shifting || OnLimiter) load = 0f;
            if (!s.Locked && Gear > 0 && s.ForwardSpeed > s.TopSpeed - 1.5f)
                load *= Mathf.Clamp01((s.TopSpeed - s.ForwardSpeed) / 1.5f);
            Load = load;

            Turbo.Step(s.Locked ? load * 0.25f : load, Rpm, e.MaxRpm, dt);

            float brakingShare = 1f - load;
            float engineBrake = e.EngineBraking * Mathf.Clamp01((Rpm - e.IdleRpm) / Mathf.Max(1f, e.MaxRpm - e.IdleRpm));
            EngineTorque = load * spec.TorqueAt(Rpm) * Turbo.TorqueMultiplier - brakingShare * engineBrake;

            if (s.Locked || Shifting) return 0f;
            // Торможение двигателем не крутит машину назад: на почти нулевой скорости его нет.
            float torque = EngineTorque;
            if (torque < 0f && Mathf.Abs(s.ForwardSpeed) < 1f) torque = 0f;
            float wheelTorque = torque * ratio * spec.Gearbox.Efficiency;
            return Gear < 0 ? -wheelTorque : wheelTorque;
        }

        void SelectDirection(in DriveState s, float dt)
        {
            if (Gear > 0)
            {
                bool wantsReverse = s.ForwardSpeed < 0.7f && s.Brake > 0.5f && s.RawThrottle < 0.1f;
                reverseHold = wantsReverse ? reverseHold + dt : 0f;
                if (reverseHold > ReverseEngageDelay) SetGear(-1, 0.05f);
            }
            else if ((s.RawThrottle > 0.1f && s.ForwardSpeed > -0.7f) || s.ForwardSpeed > 1.5f)
            {
                // Газ на месте или машина уже катится вперёд: задняя выключается.
                reverseHold = 0f;
                SetGear(1, 0.05f);
            }
        }

        void AutoShift(in DriveState s, float wheelRpm)
        {
            if (Shifting || cooldown > 0f) return;
            var e = spec.Engine;
            var g = spec.Gearbox;
            int count = g.Ratios.Length;

            bool atLimiter = wheelRpm >= e.MaxRpm * 0.995f;
            if (Gear < count && (atLimiter || (wheelRpm >= e.MaxRpm * g.UpshiftAt && s.Throttle > 0.2f)))
            {
                SetGear(Gear + 1, g.ShiftTime);
                return;
            }

            if (Gear > 1)
            {
                float lowerRpm = wheelRpm * Ratio(Gear - 1) / Ratio(Gear);
                bool braking = s.Brake > 0.3f;
                bool lugging = wheelRpm < e.MaxRpm * g.DownshiftAt && lowerRpm < e.MaxRpm * g.UpshiftAt * 0.9f;
                bool brakeDown = braking && lowerRpm < e.MaxRpm * 0.82f;
                if (lugging || brakeDown) SetGear(Gear - 1, g.ShiftTime * 0.6f);
            }
        }

        void SetGear(int gear, float time)
        {
            if (gear == Gear) return;
            int from = Gear;
            Gear = gear;
            shiftTimer = time;
            cooldown = ShiftCooldown;
            Shifted?.Invoke(from, gear);
        }
    }
}
