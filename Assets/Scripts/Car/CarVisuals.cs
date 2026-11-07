using UnityEngine;

namespace RandomCar.Car
{
    /// Визуал колёс и кузова по физике. Колесо стоит там, где его держит подвеска, крутится с угловой скоростью
    /// колеса (буксует и блокируется вместе с ним), передние поворачиваются на реальный угол руля.
    /// Кузов (объект Model) поверх физического крена получает мультяшный крен, клевок и приседание на пружине
    /// (BodyMotion) и проседает при жёстком приземлении. Колесо никогда не заходит в арку: если подвеска сжата
    /// сильнее, чем позволяет вырез (CarWheel.ArchRoom), кузов визуально приподнимается — колесо остаётся на дороге.
    /// Только картинка: коллайдеры, физика и сеть этого не видят.
    [RequireComponent(typeof(CarPhysics))]
    public sealed class CarVisuals : MonoBehaviour
    {
        /// Руль в салоне: поворачивается вокруг своей оси (локальная z) на угол колёс × передаточное число.
        [SerializeField] Transform steeringWheel;
        [SerializeField] float steeringRatio = 9f;

        CarPhysics physics;
        Quaternion[] baseRotation;
        Quaternion[] hubRotation;
        Quaternion steeringRest;
        float[] shownCompression;
        Transform model;
        Vector3 modelScale = Vector3.one;
        Vector3 modelPosition;
        Quaternion modelRotation = Quaternion.identity;
        Vector3 pivot;
        Vector3 leanPivot;
        Vector3 shownScale = Vector3.one;
        readonly BodyMotion motion = new BodyMotion();
        CarSpec leanSpec;
        float leanPerG = 2f;

        public void Configure(Transform steering) => steeringWheel = steering;

        /// Руль в салоне (камера из салона ставится за ним); null — салона нет.
        public Transform SteeringWheel => steeringWheel;

        /// Текущий показанный крен и клевок кузова (после ограничения аркой), градусы (проверки и отладка).
        public float ShownRoll { get; private set; }
        public float ShownPitch { get; private set; }

        /// Насколько кузов визуально поднят над физикой, чтобы колесо не зашло в арку, м (проверки).
        public float ArchLift { get; private set; }

        void Awake()
        {
            physics = GetComponent<CarPhysics>();
            var wheels = physics.Wheels;
            baseRotation = new Quaternion[wheels.Length];
            hubRotation = new Quaternion[wheels.Length];
            shownCompression = new float[wheels.Length];
            Quaternion inverse = Quaternion.Inverse(transform.rotation);
            if (steeringWheel != null) steeringRest = steeringWheel.localRotation;
            // Модель машины (визуал): ворота-уменьшатель масштабируют только её, вокруг точки на земле под машиной.
            model = transform.Find("Model");
            if (model != null)
            {
                modelScale = model.localScale;
                modelPosition = model.localPosition;
                modelRotation = model.localRotation;
            }
            if (wheels.Length > 0) pivot = new Vector3(0f, wheels[0].LocalCenter.y - wheels[0].Radius, 0f);
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                baseRotation[i] = w.Visual != null ? inverse * w.Visual.rotation : Quaternion.identity;
                hubRotation[i] = w.Hub != null ? inverse * w.Hub.rotation : Quaternion.identity;
                shownCompression[i] = (w.Front ? physics.Spec.Front : physics.Spec.Rear).Travel * 0.5f;
            }
        }

        void OnEnable()
        {
            if (physics == null) physics = GetComponent<CarPhysics>();
            physics.Landed += OnLanded;
        }

        void OnDisable()
        {
            if (physics != null) physics.Landed -= OnLanded;
        }

        /// Посадка после прыжка: кузов проседает и пружинит (сила — примерно скорость удара, м/с).
        void OnLanded(float strength) => motion.Land(strength);

        /// Крен на 1 g и точка, вокруг которой кренится кузов (ось крена — на высоте центров колёс, клевок — у центра масс).
        void RefreshLean()
        {
            var spec = physics.Spec;
            if (spec == leanSpec) return;
            leanSpec = spec;
            var wheels = physics.Wheels;
            float halfTrack = 0.8f, axleY = 0.35f;
            if (wheels.Length > 0)
            {
                halfTrack = 0f;
                axleY = 0f;
                foreach (var w in wheels)
                {
                    halfTrack += Mathf.Abs(w.LocalCenter.x);
                    axleY += w.LocalCenter.y;
                }
                halfTrack /= wheels.Length;
                axleY /= wheels.Length;
            }
            leanPerG = spec.LeanPerG(halfTrack);
            leanPivot = new Vector3(0f, axleY, spec.CenterOfMass.z);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            var wheels = physics.Wheels;
            var spec = physics.Spec;
            Quaternion root = transform.rotation;
            RefreshLean();

            // Кузов: цель крена и клевка — по ускорениям (своя машина — по физике, сетевая копия — по присланной скорости).
            bool live = physics.Simulated || physics.RemoteDriven;
            bool grounded = live && physics.GroundedWheels >= 2;
            float g = 9.81f;
            float rollTarget = grounded ? Mathf.Clamp(physics.LateralAcceleration / g, -1.3f, 1.3f) * leanPerG : 0f;
            float pitchTarget = grounded ? Mathf.Clamp(-physics.LongitudinalAcceleration / g, -1.2f, 1.4f) * leanPerG * 0.55f : 0f;
            motion.Step(rollTarget, pitchTarget, dt);

            Vector3 scale = physics.Effects.VisualScale3;
            if ((scale - shownScale).sqrMagnitude > 1e-6f || shownScale != Vector3.one)
            {
                // Мультяшное «сдутие» и сплющивание: с перелётом и пружинкой, а не линейно.
                shownScale = Vector3.Lerp(shownScale, scale, 1f - Mathf.Exp(-12f * dt));
                if ((shownScale - Vector3.one).sqrMagnitude < 4e-6f && scale == Vector3.one) shownScale = Vector3.one;
            }

            // Колёса: положение по подвеске. Мультяшный крен, клевок и проседание кузова занимают только тот запас
            // в арке, который оставила физическая подвеска (иначе нос «нырял» бы на колесо); если же подвеска сама
            // сжата сильнее, чем позволяет вырез (жёсткая посадка), кузов визуально приподнимается.
            Quaternion leanFull = Quaternion.Euler(motion.Pitch, 0f, motion.Roll);
            float allowed = 1f;
            float lift = 0f;
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                float travel = (w.Front ? spec.Front : spec.Rear).Travel;
                // Сжатие сглаживается чуть-чуть: шум неровностей виден, дрожания на кадрах нет.
                // Без физики (гараж, сетевая копия без данных) колесо стоит в положении покоя; копия с данными — по подвеске.
                float target = live ? Mathf.Clamp(w.Compression, 0f, travel) : travel * 0.5f;
                shownCompression[i] = Mathf.Lerp(shownCompression[i], target, 1f - Mathf.Exp(-40f * dt));
                float up = shownCompression[i] - travel * 0.5f;
                float room = w.ArchRoom - up;
                lift = Mathf.Max(lift, -room);
                // На сколько мультяшное движение опускает кузов над этим колесом.
                float down = -((leanPivot + leanFull * (w.LocalCenter - leanPivot)).y - w.LocalCenter.y + motion.Heave);
                if (down > 1e-4f) allowed = Mathf.Min(allowed, Mathf.Clamp01(Mathf.Max(0f, room) / down));
            }
            ArchLift = Mathf.Max(0f, lift);
            Quaternion lean = Quaternion.Slerp(Quaternion.identity, leanFull, allowed);
            float heave = motion.Heave * allowed + ArchLift;
            ShownRoll = motion.Roll * allowed;
            ShownPitch = motion.Pitch * allowed;

            if (model != null)
            {
                Vector3 position = pivot + Vector3.Scale(modelPosition - pivot, shownScale);
                position = leanPivot + lean * (position - leanPivot) + Vector3.up * heave;
                model.localPosition = position;
                model.localRotation = lean * modelRotation;
                model.localScale = Vector3.Scale(modelScale, shownScale);
            }

            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                if (w.Visual == null) continue;
                float travel = (w.Front ? spec.Front : spec.Rear).Travel;
                Vector3 local = w.LocalCenter + Vector3.up * (shownCompression[i] - travel * 0.5f);
                if (shownScale != Vector3.one) local = pivot + Vector3.Scale(local - pivot, shownScale);
                w.SpinAngle = Mathf.Repeat(w.SpinAngle + w.Omega * Mathf.Rad2Deg * dt, 360f);
                Quaternion steer = root * Quaternion.AngleAxis(w.SteerAngle, Vector3.up);
                Vector3 centre = transform.TransformPoint(local);
                w.Visual.SetPositionAndRotation(centre, steer * Quaternion.AngleAxis(w.SpinAngle, Vector3.right) * baseRotation[i]);
                if (w.Hub != null) w.Hub.SetPositionAndRotation(centre, steer * hubRotation[i]);
            }
            if (steeringWheel != null)
            {
                float angle = 0f;
                int front = 0;
                foreach (var w in wheels)
                    if (w.Front) { angle += w.SteerAngle; front++; }
                if (front > 0) angle /= front;
                steeringWheel.localRotation = steeringRest * Quaternion.AngleAxis(-angle * steeringRatio, Vector3.forward);
            }
        }
    }

    /// Мультяшное движение кузова на пружине-демпфере: крен и клевок догоняют цель с лёгким перелётом,
    /// проседание после приземления пружинит. Чистая математика (без Unity-объектов) — проверяется тестами.
    public sealed class BodyMotion
    {
        const float Frequency = 2.3f;   // Гц
        const float Damping = 0.42f;    // доля критического: заметный, но короткий «покач»
        const float HeaveFrequency = 3.2f;
        const float HeaveDamping = 0.35f;

        public float Roll { get; private set; }
        public float Pitch { get; private set; }
        public float Heave { get; private set; }

        float rollVelocity, pitchVelocity, heaveVelocity;

        /// Посадка: кузов уходит вниз со скоростью по силе удара (до 1.6 м/с) и пружинит обратно.
        public void Land(float strength)
        {
            if (strength < 1.5f) return;
            heaveVelocity -= Mathf.Min(1.6f, strength * 0.09f);
            pitchVelocity += Mathf.Clamp(strength, 0f, 20f) * (Random.value < 0.5f ? -0.6f : 0.6f);
        }

        public void Step(float rollTarget, float pitchTarget, float dt)
        {
            dt = Mathf.Min(dt, 0.05f);
            if (dt <= 0f) return;
            // Полушаги — устойчивость при редких кадрах.
            int steps = dt > 0.02f ? 2 : 1;
            float h = dt / steps;
            float w = 2f * Mathf.PI * Frequency;
            float wh = 2f * Mathf.PI * HeaveFrequency;
            for (int i = 0; i < steps; i++)
            {
                rollVelocity += (w * w * (rollTarget - Roll) - 2f * Damping * w * rollVelocity) * h;
                pitchVelocity += (w * w * (pitchTarget - Pitch) - 2f * Damping * w * pitchVelocity) * h;
                heaveVelocity += (-wh * wh * Heave - 2f * HeaveDamping * wh * heaveVelocity) * h;
                Roll += rollVelocity * h;
                Pitch += pitchVelocity * h;
                Heave += heaveVelocity * h;
            }
            Roll = Mathf.Clamp(Roll, -8f, 8f);
            Pitch = Mathf.Clamp(Pitch, -6f, 6f);
            Heave = Mathf.Clamp(Heave, -0.18f, 0.12f);
        }

        public void Reset()
        {
            Roll = Pitch = Heave = 0f;
            rollVelocity = pitchVelocity = heaveVelocity = 0f;
        }
    }
}
