using RandomCar.Events;
using UnityEngine;

namespace RandomCar.Car
{
    /// Аркадная машина на CharacterController. Водитель задаёт газ и руль, машина сама считает скорость и поворот.
    /// Эффекты событий (замедление, буст, толчок, занос) применяются только через методы Apply*,
    /// которые обрезают силу и длительность по FairnessLimits.
    [RequireComponent(typeof(CharacterController))]
    public sealed class CarController : MonoBehaviour
    {
        public const float MaxSpeed = 28f;     // м/с, около 100 км/ч
        const float MaxReverseSpeed = 8f;
        const float AccelRate = 9f;            // м/с² при разгоне
        const float BrakeRate = 22f;           // при торможении и остановке
        const float CoastRate = 4f;            // накат без газа
        const float TurnRate = 110f;           // градусов/с на полном руле
        const float Gravity = 20f;
        const float ImpulseDecay = 2.6f;       // затухание внешнего толчка, 1/с

        /// Газ от -1 (тормоз или назад) до 1 (полный газ).
        public float Throttle { get; set; }

        /// Руль от -1 (влево) до 1 (вправо).
        public float Steer { get; set; }

        /// Машина тормозит до остановки и не слушает водителя.
        public bool Locked { get; set; } = true;

        /// Скорость вдоль машины, м/с.
        public float Speed { get; private set; }

        /// Руль, который реально применяется (0, если машина заблокирована).
        public float AppliedSteer => Locked ? 0f : Mathf.Clamp(Steer, -1f, 1f);

        /// Ускорение вдоль машины за последний кадр, м/с².
        public float Acceleration { get; private set; }

        public bool IsGrounded => controller != null && controller.isGrounded;
        public bool IsBoosted => Time.time < boostUntil;
        public bool IsSlowed => Time.time < slowUntil;
        public bool IsSlipping => Time.time < slipUntil;

        /// Сработал толчок или посадка после прыжка: для пыли и тряски камеры.
        public event System.Action<float> Bumped;

        CharacterController controller;
        float verticalSpeed;
        Vector3 externalVelocity;
        float slowFactor = 1f;
        float slowUntil;
        float boostFactor = 1f;
        float boostUntil;
        float slipUntil;
        float slipPhase;
        bool wasGrounded = true;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            // Высота капсулы не меньше двух радиусов, иначе Unity растягивает её вниз и машина висит над дорогой.
            controller.radius = 0.85f;
            controller.height = 1.7f;
            controller.center = new Vector3(0f, 0.85f + controller.skinWidth, 0f);
        }

        /// Ставит машину на позицию без физики: для старта и рестарта. Снимает все эффекты.
        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
            Speed = 0f;
            verticalSpeed = 0f;
            Throttle = 0f;
            Steer = 0f;
            Acceleration = 0f;
            externalVelocity = Vector3.zero;
            slowUntil = boostUntil = slipUntil = 0f;
            slowFactor = boostFactor = 1f;
        }

        // ---------- эффекты событий ----------

        /// Замедление: потолок скорости = factor * MaxSpeed на seconds (не дольше 1.5 с, не ниже 45%).
        public void ApplySlow(float factor, float seconds)
        {
            factor = Mathf.Clamp(factor, FairnessLimits.MinSlowFactor, 1f);
            seconds = Mathf.Clamp(seconds, 0f, FairnessLimits.MaxSlowSeconds);
            slowFactor = IsSlowed ? Mathf.Min(slowFactor, factor) : factor;
            slowUntil = Mathf.Max(slowUntil, Time.time + seconds);
            slowUntil = Mathf.Min(slowUntil, Time.time + FairnessLimits.MaxSlowSeconds);
        }

        /// Буст: потолок и скорость выше на (factor-1), ровно 2 с, не больше +25%.
        public void ApplyBoost(float factor)
        {
            factor = Mathf.Clamp(factor, 1f, FairnessLimits.MaxBoostFactor);
            boostFactor = IsBoosted ? Mathf.Max(boostFactor, factor) : factor;
            boostUntil = Time.time + FairnessLimits.BoostSeconds;
            if (!Locked) Speed = Mathf.Max(Speed, Mathf.Min(Speed + 4f, MaxSpeed * boostFactor));
        }

        /// Толчок без урона: горизонтальная часть затухает, вертикальная — прыжок.
        public void ApplyImpulse(Vector3 velocity)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up);
            horizontal = Vector3.ClampMagnitude(horizontal, FairnessLimits.MaxImpulse);
            externalVelocity = Vector3.ClampMagnitude(externalVelocity + horizontal, FairnessLimits.MaxImpulse);
            float up = Mathf.Clamp(velocity.y, 0f, FairnessLimits.MaxImpulse);
            if (up > 0f) verticalSpeed = Mathf.Max(verticalSpeed, up);
            Bumped?.Invoke(horizontal.magnitude + up);
        }

        /// Скользко: руль слабее и машину ведёт, не дольше 1.5 с.
        public void ApplySlip(float seconds)
        {
            seconds = Mathf.Clamp(seconds, 0f, FairnessLimits.MaxSlipSeconds);
            if (!IsSlipping) slipPhase = Random.value * 10f;
            slipUntil = Mathf.Max(slipUntil, Time.time + seconds);
            slipUntil = Mathf.Min(slipUntil, Time.time + FairnessLimits.MaxSlipSeconds);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float throttle = Locked ? 0f : Mathf.Clamp(Throttle, -1f, 1f);
            float steer = AppliedSteer;
            float cap = MaxSpeed * (IsSlowed ? slowFactor : 1f) * (IsBoosted ? boostFactor : 1f);

            float target = throttle > 0f ? throttle * cap : throttle * MaxReverseSpeed;
            if (IsBoosted && !Locked) target = Mathf.Max(target, cap * 0.85f);
            float rate;
            if (Locked) rate = BrakeRate;
            else if (target == 0f) rate = CoastRate;
            else rate = Mathf.Abs(target) > Mathf.Abs(Speed) && Speed * target >= 0f ? AccelRate : BrakeRate;
            if (Speed > cap) rate = Mathf.Max(rate, BrakeRate * 0.7f);   // замедление события
            float before = Speed;
            Speed = Mathf.MoveTowards(Speed, target, rate * dt);

            // Руль работает на движении. Упёршись в стену с нажатым газом, машина всё равно может отвернуть.
            float drive = Mathf.Max(Mathf.Abs(Speed), Mathf.Abs(throttle) * 3f);
            float direction = Mathf.Abs(Speed) > 0.5f ? Mathf.Sign(Speed) : (throttle < 0f ? -1f : 1f);
            float turn = Mathf.Clamp01(drive / 6f) * direction;
            float grip = 1f;
            float drift = 0f;
            if (IsSlipping)
            {
                grip = 0.45f;
                drift = Mathf.Sin((Time.time + slipPhase) * 4.2f) * 55f * Mathf.Clamp01(Mathf.Abs(Speed) / 10f);
            }
            if (!controller.isGrounded) grip *= 0.35f;
            transform.Rotate(0f, (steer * TurnRate * turn * grip + drift) * dt, 0f);

            verticalSpeed = controller.isGrounded && verticalSpeed <= 0f ? -2f : verticalSpeed - Gravity * dt;
            externalVelocity = Vector3.MoveTowards(externalVelocity, Vector3.zero, externalVelocity.magnitude * ImpulseDecay * dt + 0.5f * dt);
            Vector3 motion = transform.forward * Speed + externalVelocity + Vector3.up * verticalSpeed;
            CollisionFlags flags = controller.Move(motion * dt);

            // Удар о стену или машину: скорость падает до того, что реально получилось проехать.
            if ((flags & CollisionFlags.Sides) != 0)
            {
                float actual = Vector3.Dot(controller.velocity, transform.forward);
                if (Speed > 0f) Speed = Mathf.Clamp(actual, 0f, Speed);
                else Speed = Mathf.Clamp(actual, Speed, 0f);
                externalVelocity *= 0.5f;
            }
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;

            bool grounded = controller.isGrounded;
            if (grounded && !wasGrounded && before > 3f) Bumped?.Invoke(2f);
            wasGrounded = grounded;
            Acceleration = (Speed - before) / dt;
        }
    }
}
