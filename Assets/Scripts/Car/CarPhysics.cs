using System;
using UnityEngine;

namespace RandomCar.Car
{
    /// Колесо: геометрия из модели и состояние на текущем шаге физики.
    [Serializable]
    public sealed class CarWheel
    {
        public Transform Visual;
        /// Ступица: следует за колесом и поворачивается рулём, но не вращается (суппорт тормоза).
        public Transform Hub;
        [Tooltip("Центр колеса в покое, в координатах корня машины")] public Vector3 LocalCenter;
        public float Radius = 0.36f;
        public bool Front;
        public bool Left;
        [Tooltip("Насколько колесо может подняться от положения покоя, не задев арку и подкрылок, м (меряет сборщик префаба)")]
        public float ArchRoom = 1f;

        [NonSerialized] public bool Grounded;
        [NonSerialized] public float Compression;
        [NonSerialized] public float CompressionVelocity;
        [NonSerialized] public float Load;
        [NonSerialized] public Vector3 HitPoint;
        [NonSerialized] public Vector3 HitNormal;
        [NonSerialized] public SurfaceType Surface;
        [NonSerialized] public Vector3 WorldCenter;
        [NonSerialized] public float SteerAngle;
        /// Угловая скорость колеса, рад/с (для вращения, оборотов и звука).
        [NonSerialized] public float Omega;
        [NonSerialized] public float SpinExtra;
        /// Боковое скольжение пятна, м/с.
        [NonSerialized] public float LateralSlip;
        /// Продольная пробуксовка/блокировка 0..1.
        [NonSerialized] public float LongitudinalSlip;
        [NonSerialized] public bool Locked;
        [NonSerialized] public float SpinAngle;
    }

    /// Физика машины на Rigidbody: подвеска на лучах (пружина, демпфер, стабилизаторы), шины с эллипсом трения,
    /// двигатель с турбиной и автоматической коробкой, тормоза с ABS, сопротивление качению и воздуха, прижим,
    /// помощь при вождении. Одна и та же модель для игрока, ИИ и сети: управление приходит через Input.
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarPhysics : MonoBehaviour
    {
        const float Gravity = 9.81f;
        /// Аркадное сцепление: шины держат сильнее, машина уверенно входит в поворот, а не уходит в занос.
        public const float ArcadeGrip = 1.35f;
        const float ForceHeightFraction = 0.35f; // точка приложения сил шины чуть выше пятна: меньше склонность к опрокидыванию

        [SerializeField] CarSpec spec;
        [SerializeField] CarWheel[] wheels = Array.Empty<CarWheel>();

        readonly RaycastHit[] hits = new RaycastHit[8];
        Rigidbody body;
        float wheelbase;
        float rearAxleZ;
        float frontStatic;
        float rearStatic;
        float frontK, rearK, frontDamp, rearDamp;
        float steerAngle;
        int groundedCount;
        float airTime;
        Vector3 lastVelocity;

        public CarSpec Spec => spec;
        public CarWheel[] Wheels => wheels;
        public Rigidbody Body => body;
        Drivetrain drivetrain;

        /// Двигатель, коробка и турбина. Создаётся при первом обращении: другие компоненты подписываются в OnEnable.
        public Drivetrain Drivetrain => drivetrain ??= new Drivetrain(spec, new Turbo(spec.Turbo));
        public Turbo Turbo => Drivetrain?.Turbo;
        public CarEffects Effects { get; } = new CarEffects();

        /// Управление водителя на следующий шаг.
        public CarInput Input { get; set; }

        public DrivingAssists Assists { get; set; } = DrivingAssists.For(AssistLevel.Standard);

        /// Блокировка: тормоза и ручник, коробка в нейтрали (мотор можно погазовать).
        public bool Locked { get; set; } = true;

        /// Физика выключена (машина — сетевая копия, тело кинематическое).
        public bool Simulated { get; private set; } = true;

        /// Продольная скорость, м/с (минус — назад).
        public float ForwardSpeed { get; private set; }

        /// Модуль скорости, м/с.
        public float SpeedAbs { get; private set; }

        /// Продольное и боковое ускорение, м/с² (сглаженные), для визуала и камеры.
        public float LongitudinalAcceleration { get; private set; }
        public float LateralAcceleration { get; private set; }

        /// Угол заноса кузова, градусы (плюс — скорость правее курса).
        public float BodySlipAngle { get; private set; }

        /// Реальный угол поворота передних колёс, градусы.
        public float SteerAngle => steerAngle;

        /// Угол колёс при полном руле на текущей скорости, градусы (руль ограничен сцеплением).
        public float SteerLimit { get; private set; } = 30f;

        /// Колёсная база, м.
        public float Wheelbase => wheelbase;

        public int GroundedWheels => groundedCount;
        public bool IsGrounded => groundedCount > 0;

        /// Сколько секунд машина в воздухе.
        public float AirTime => airTime;

        /// Посадка после прыжка: сила удара (для камеры, звука, пыли).
        public event Action<float> Landed;

        public void Configure(CarSpec carSpec, CarWheel[] carWheels)
        {
            spec = carSpec;
            wheels = carWheels;
        }

        /// Характеристики с учётом настройки сборки. Вызывать до Awake (машина создаётся выключенной).
        public void SetSpec(CarSpec carSpec)
        {
            spec = carSpec;
            drivetrain = null;
            if (initialized) ApplySpec();
        }

        PhysicsScene physicsScene;
        bool initialized;

        void Awake() => Initialize();

        /// Подготовка тела и констант. Вызывается из Awake; тесты в редакторе вызывают сами.
        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            body = GetComponent<Rigidbody>();
            physicsScene = gameObject.scene.IsValid() ? gameObject.scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
            ApplySpec();
        }

        /// Масса, центр масс, инерция и константы подвески из CarSpec.
        public void ApplySpec()
        {
            body.mass = spec.Mass;
            body.ResetCenterOfMass();
            body.ResetInertiaTensor();
            body.centerOfMass = spec.CenterOfMass;
            body.inertiaTensor = Vector3.Scale(body.inertiaTensor, spec.InertiaScale);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.maxAngularVelocity = 8f;
            body.angularDamping = 0.05f;
            body.linearDamping = 0f;
            // Против дрожания в завалах и при сильных ударах: больше итераций решателя и мягкое выталкивание
            // из геометрии (по умолчанию 10 м/с — машина, вдавленная телом события, «выстреливает»).
            body.solverIterations = 10;
            body.solverVelocityIterations = 4;
            body.maxDepenetrationVelocity = 6f;
            ApplyBodyMaterial();

            float frontZ = 0f, rearZ = 0f;
            int nf = 0, nr = 0;
            foreach (var w in wheels)
            {
                if (w.Front) { frontZ += w.LocalCenter.z; nf++; }
                else { rearZ += w.LocalCenter.z; nr++; }
            }
            frontZ /= Mathf.Max(1, nf);
            rearZ /= Mathf.Max(1, nr);
            wheelbase = Mathf.Max(0.5f, frontZ - rearZ);
            rearAxleZ = rearZ;
            float frontShare = Mathf.Clamp01((spec.CenterOfMass.z - rearZ) / wheelbase);
            frontStatic = spec.Mass * Gravity * frontShare / Mathf.Max(1, nf);
            rearStatic = spec.Mass * Gravity * (1f - frontShare) / Mathf.Max(1, nr);
            Spring(spec.Front, frontStatic, out frontK, out frontDamp);
            Spring(spec.Rear, rearStatic, out rearK, out rearDamp);
        }

        static readonly System.Collections.Generic.Dictionary<(float, float), PhysicsMaterial> BodyMaterials =
            new System.Collections.Generic.Dictionary<(float, float), PhysicsMaterial>();

        /// Материал кузова по характеру машины: упругость (лёгкие отскакивают) и трение о стены и соседей.
        /// Материалы общие для одинаковых значений; после выхода из Play без перезагрузки домена пересоздаются.
        void ApplyBodyMaterial()
        {
            var key = (Mathf.Round(spec.Bounciness * 100f) / 100f, Mathf.Round(spec.BodyFriction * 100f) / 100f);
            if (!BodyMaterials.TryGetValue(key, out var material) || material == null)
            {
                material = new PhysicsMaterial($"PM_Car_{key.Item1:F2}_{key.Item2:F2}")
                {
                    bounciness = key.Item1,
                    dynamicFriction = key.Item2,
                    staticFriction = key.Item2,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Average,
                };
                BodyMaterials[key] = material;
            }
            foreach (var c in GetComponents<Collider>())
                if (!c.isTrigger) c.sharedMaterial = material;
        }

        static void Spring(AxleSpec axle, float staticLoad, out float k, out float damp)
        {
            float cornerMass = staticLoad / Gravity;
            float w = 2f * Mathf.PI * axle.SpringFrequency;
            k = cornerMass * w * w;
            damp = 2f * axle.DampingRatio * Mathf.Sqrt(k * cornerMass);
        }

        /// Ставит машину без физики (старт, рестарт, возвращение на трассу). Снимает эффекты и скорость.
        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            body.position = position;
            body.rotation = rotation;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            Drivetrain.Reset();
            Effects.Clear();
            steerAngle = 0f;
            airTime = 0f;
            lastVelocity = Vector3.zero;
            foreach (var w in wheels)
            {
                w.Compression = w.CompressionVelocity = 0f;
                w.Omega = w.SpinExtra = 0f;
                w.Grounded = false;
            }
            Input = CarInput.None;
        }

        /// Сетевая копия: физику считает владелец, здесь тело кинематическое.
        public void SetSimulated(bool simulated)
        {
            Simulated = simulated;
            body.isKinematic = !simulated;
            if (simulated) RemoteDriven = false;
        }

        /// Копию двигает сеть (RemoteStep): подвеска, колёса, обороты и турбина считаются по присланному состоянию.
        public bool RemoteDriven { get; private set; }

        /// Шаг сетевой копии: тело уже поставлено сетью, силы не прикладываются. По скорости и вводу владельца
        /// считаются руль, сжатие подвески (лучи), вращение и проскальзывание колёс, обороты, передача и турбина —
        /// чтобы колёса, дым, следы и звук мотора чужой машины выглядели так же, как у своей.
        public void RemoteStep(Vector3 velocity, CarInput input, bool locked, float dt)
        {
            if (dt <= 0f || Simulated) return;
            RemoteDriven = true;
            Input = input;
            Locked = locked;
            Vector3 up = transform.up;
            Vector3 forward = transform.forward;
            ForwardSpeed = Vector3.Dot(velocity, forward);
            SpeedAbs = velocity.magnitude;
            Vector3 accel = (velocity - lastVelocity) / dt;
            lastVelocity = velocity;
            float k = 1f - Mathf.Exp(-8f * dt);
            LongitudinalAcceleration = Mathf.Lerp(LongitudinalAcceleration, Vector3.Dot(accel, forward), k);
            LateralAcceleration = Mathf.Lerp(LateralAcceleration, Vector3.Dot(accel, transform.right), k);
            Vector3 planar = Vector3.ProjectOnPlane(velocity, up);
            BodySlipAngle = planar.sqrMagnitude > 4f && ForwardSpeed > 1f ? Vector3.SignedAngle(forward, planar, up) : 0f;

            UpdateSteering(input.Steer, dt);
            ProbeWheels(up, dt);

            float rawThrottle = Mathf.Clamp01(input.Throttle);
            float rawBrake = Mathf.Clamp01(input.Brake);
            foreach (var w in wheels)
            {
                w.SteerAngle = w.Front ? steerAngle : 0f;
                Vector3 heading = Quaternion.AngleAxis(w.SteerAngle, up) * forward;
                float vLong = Vector3.Dot(velocity, heading);
                float vLat = Vector3.Dot(velocity, Vector3.Cross(up, heading));
                bool wheelLocked = w.Grounded && !w.Front && input.Handbrake && Mathf.Abs(vLong) > 1f;
                w.Locked = wheelLocked;
                w.Omega = wheelLocked ? 0f : vLong / w.Radius;
                w.LateralSlip = w.Grounded ? Mathf.Abs(vLat) : 0f;
                w.LongitudinalSlip = 0f;
                w.Load = 0f;
            }

            bool reversing = Drivetrain.Reversing;
            var state = new DriveState
            {
                Throttle = locked ? rawThrottle : reversing ? rawBrake : rawThrottle,
                Brake = rawBrake,
                RawThrottle = rawThrottle,
                ForwardSpeed = ForwardSpeed,
                WheelOmega = DrivenOmega(),
                GroundOmega = ForwardSpeed / DrivenRadius(),
                Locked = locked,
                TopSpeed = spec.TopSpeed * Effects.BoostFactor,
            };
            Drivetrain.Step(state, dt);
        }

        void FixedUpdate()
        {
            if (Simulated) Step(Time.fixedDeltaTime);
        }

        /// Один шаг сил машины. Само тело двигает движок физики после шага.
        public void Step(float dt)
        {
            Vector3 up = transform.up;
            Vector3 forward = transform.forward;
            Vector3 velocity = body.linearVelocity;
            ForwardSpeed = Vector3.Dot(velocity, forward);
            SpeedAbs = velocity.magnitude;

            Vector3 accel = (velocity - lastVelocity) / dt;
            lastVelocity = velocity;
            float k = 1f - Mathf.Exp(-8f * dt);
            LongitudinalAcceleration = Mathf.Lerp(LongitudinalAcceleration, Vector3.Dot(accel, forward), k);
            LateralAcceleration = Mathf.Lerp(LateralAcceleration, Vector3.Dot(accel, transform.right), k);

            // Угол заноса — по скорости задней оси: в обычном повороте она идёт по курсу (геометрия руля
            // не считается заносом), в заносе — уходит вбок.
            Vector3 rearVelocity = body.GetPointVelocity(transform.TransformPoint(new Vector3(0f, spec.CenterOfMass.y, rearAxleZ)));
            Vector3 planar = Vector3.ProjectOnPlane(rearVelocity, up);
            BodySlipAngle = planar.sqrMagnitude > 4f && ForwardSpeed > 1f
                ? Vector3.SignedAngle(forward, planar, up)
                : 0f;

            var input = Input;
            float rawThrottle = Mathf.Clamp01(input.Throttle);
            float rawBrake = Mathf.Clamp01(input.Brake);

            UpdateSteering(input.Steer, dt);
            ProbeWheels(up, dt);

            // Педали: на задней передаче тормоз становится газом и наоборот.
            bool reversing = Drivetrain.Reversing;
            float drivePedal = Locked ? rawThrottle : reversing ? rawBrake : rawThrottle;
            float brakePedal = Locked ? 1f : reversing ? rawThrottle : rawBrake;

            float drivenOmega = DrivenOmega();
            var state = new DriveState
            {
                Throttle = drivePedal,
                Brake = rawBrake,
                RawThrottle = rawThrottle,
                ForwardSpeed = ForwardSpeed,
                WheelOmega = drivenOmega,
                GroundOmega = ForwardSpeed / DrivenRadius(),
                Locked = Locked,
                TopSpeed = spec.TopSpeed * Effects.BoostFactor,
            };
            float wheelTorque = Drivetrain.Step(state, dt);

            ApplyWheelForces(up, wheelTorque, brakePedal, Locked || input.Handbrake, dt);
            ApplyAero(velocity, up);
            ApplyEffects(velocity, forward);
            if (!Locked && ForwardSpeed > 5f) ApplyStability(up);
            ApplyAirControl(up, dt);
        }

        void UpdateSteering(float steerInput, float dt)
        {
            float v = Mathf.Max(SpeedAbs, 1f);
            float grip = spec.Front.LateralGrip * ArcadeGrip * Gravity;
            // Угол, который шина ещё держит на этой скорости, плюс пиковый угол увода.
            float gripAngle = Mathf.Atan(wheelbase * grip / (v * v)) * Mathf.Rad2Deg + spec.Front.PeakSlipAngle;
            float limit = Mathf.Min(spec.MaxSteerAngle, gripAngle * Assists.SteerLimit);
            SteerLimit = limit;
            float target = Mathf.Clamp(steerInput, -1f, 1f) * limit;
            if (!Locked && ForwardSpeed > 4f && Assists.CounterSteer > 0f)
                target += Mathf.Clamp(BodySlipAngle, -40f, 40f) * Assists.CounterSteer;
            target = Mathf.Clamp(target, -spec.MaxSteerAngle, spec.MaxSteerAngle);
            // Скорость рейки — характер машины (CarSpec.SteerSpeed): спорткар отзывчивый, внедорожник ленивый.
            steerAngle = Mathf.MoveTowards(steerAngle, target, Mathf.Max(60f, spec.SteerSpeed) * dt);
        }

        void ProbeWheels(Vector3 up, float dt)
        {
            groundedCount = 0;
            foreach (var w in wheels)
            {
                var axle = w.Front ? spec.Front : spec.Rear;
                float travel = axle.Travel;
                Vector3 origin = transform.TransformPoint(w.LocalCenter + Vector3.up * (travel * 0.5f));
                float length = travel + w.Radius;
                bool found = Cast(origin, -up, length, out var hit);
                float previous = w.Compression;
                bool wasGrounded = w.Grounded;
                if (found)
                {
                    float s = hit.distance - w.Radius;
                    float c = travel - s;
                    var surface = Surface.Of(hit.collider);
                    if (surface != null && surface.Bumpiness > 0f)
                    {
                        float l = Mathf.Max(0.2f, surface.BumpWavelength);
                        c += (Mathf.PerlinNoise(hit.point.x / l, hit.point.z / l) - 0.5f) * 2f * surface.Bumpiness;
                    }
                    w.Grounded = true;
                    w.Compression = c;
                    // Первое касание после полёта — без всплеска скорости сжатия (иначе демпфер подбрасывает машину).
                    w.CompressionVelocity = wasGrounded ? (c - previous) / dt : 0f;
                    w.HitPoint = hit.point;
                    w.HitNormal = hit.normal;
                    w.Surface = surface;
                    w.WorldCenter = origin - up * Mathf.Clamp(s, 0f, travel);
                    groundedCount++;
                }
                else
                {
                    w.Grounded = false;
                    w.Compression = 0f;
                    w.CompressionVelocity = 0f;
                    w.Surface = null;
                    w.WorldCenter = origin - up * travel;
                }
            }

            if (groundedCount == 0) airTime += dt;
            else
            {
                if (airTime > 0.25f)
                {
                    float impact = Mathf.Max(0f, -Vector3.Dot(body.linearVelocity, up));
                    Landed?.Invoke(impact + airTime * 2f);
                }
                airTime = 0f;
            }
        }

        bool Cast(Vector3 origin, Vector3 direction, float length, out RaycastHit best)
        {
            best = default;
            int count = physicsScene.Raycast(origin, direction, hits, length, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var h = hits[i];
                if (h.collider.attachedRigidbody == body) continue;
                if (h.distance < nearest)
                {
                    nearest = h.distance;
                    best = h;
                    found = true;
                }
            }
            return found;
        }

        float DrivenOmega()
        {
            float sum = 0f;
            int n = 0;
            foreach (var w in wheels)
            {
                if (!IsDriven(w)) continue;
                sum += w.Grounded ? w.Omega : ForwardSpeed / w.Radius + w.SpinExtra;
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        float DrivenRadius()
        {
            float sum = 0f;
            int n = 0;
            foreach (var w in wheels)
            {
                if (!IsDriven(w)) continue;
                sum += w.Radius;
                n++;
            }
            return n > 0 ? sum / n : 0.35f;
        }

        bool IsDriven(CarWheel w) => spec.Drive == DriveType.AllWheel || (spec.Drive == DriveType.FrontWheel) == w.Front;

        float DriveShare(CarWheel w)
        {
            int front = 0, rear = 0;
            foreach (var x in wheels) { if (x.Front) front++; else rear++; }
            switch (spec.Drive)
            {
                case DriveType.FrontWheel: return w.Front ? 1f / Mathf.Max(1, front) : 0f;
                case DriveType.RearWheel: return w.Front ? 0f : 1f / Mathf.Max(1, rear);
                default: return w.Front ? spec.AwdFrontShare / Mathf.Max(1, front) : (1f - spec.AwdFrontShare) / Mathf.Max(1, rear);
            }
        }

        void ApplyWheelForces(Vector3 up, float wheelTorque, float brakePedal, bool handbrake, float dt)
        {
            // Пружины, демпферы и стабилизаторы.
            Span<float> load = stackalloc float[wheels.Length];
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                if (!w.Grounded) { load[i] = 0f; continue; }
                var axle = w.Front ? spec.Front : spec.Rear;
                float k = w.Front ? frontK : rearK;
                float damp = w.Front ? frontDamp : rearDamp;
                float stat = w.Front ? frontStatic : rearStatic;
                float f = stat + k * (w.Compression - axle.Travel * 0.5f) + damp * w.CompressionVelocity;
                if (w.Compression > axle.Travel) f += k * 10f * (w.Compression - axle.Travel) + damp * 2f * Mathf.Max(0f, w.CompressionVelocity);
                load[i] = f;
            }
            for (int i = 0; i < wheels.Length; i++)
            {
                var a = wheels[i];
                if (!a.Left) continue;
                for (int j = 0; j < wheels.Length; j++)
                {
                    var b = wheels[j];
                    if (b.Left || b.Front != a.Front) continue;
                    var axle = a.Front ? spec.Front : spec.Rear;
                    float k = a.Front ? frontK : rearK;
                    float roll = axle.AntiRoll * k * (a.Compression - b.Compression);
                    if (a.Grounded) load[i] += roll;
                    if (b.Grounded) load[j] -= roll;
                }
            }

            float brakeTotal = spec.BrakeDecel * spec.Mass * Gravity * brakePedal;
            int frontCount = 0, rearCount = 0;
            foreach (var w in wheels) { if (w.Front) frontCount++; else rearCount++; }

            Vector3 com = body.worldCenterOfMass;
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                var axle = w.Front ? spec.Front : spec.Rear;
                w.SteerAngle = w.Front ? steerAngle : 0f;
                float driveTorque = wheelTorque * DriveShare(w);

                if (!w.Grounded)
                {
                    // В воздухе колесо раскручивает мотор и тормозит тормоз.
                    w.Load = 0f;
                    float free = ForwardSpeed / w.Radius;
                    w.Omega = brakePedal > 0.5f ? 0f : free + w.SpinExtra;
                    w.SpinExtra *= Mathf.Exp(-3f * dt);
                    w.LateralSlip = 0f;
                    w.LongitudinalSlip = 0f;
                    w.Locked = false;
                    continue;
                }

                float fz = Mathf.Max(0f, load[i]);
                w.Load = fz;
                Vector3 normal = w.HitNormal;
                body.AddForceAtPosition(up * fz, w.HitPoint);

                Vector3 heading = Quaternion.AngleAxis(w.SteerAngle, up) * transform.forward;
                Vector3 fwd = Vector3.ProjectOnPlane(heading, normal).normalized;
                Vector3 side = Vector3.Cross(normal, fwd);
                Vector3 v = body.GetPointVelocity(w.HitPoint);
                float vLong = Vector3.Dot(v, fwd);
                float vLat = Vector3.Dot(v, side);

                float surfaceGrip = w.Surface != null ? w.Surface.Grip * (w.Surface.Dusty ? spec.LooseSurfaceGrip : 1f) : 1f;
                float grip = surfaceGrip * Effects.GripMultiplier * ArcadeGrip;
                float muLong = TireModel.LoadedGrip(axle.LongitudinalGrip, fz, axle.LoadSensitivity) * grip;
                float muLat = TireModel.LoadedGrip(axle.LateralGrip, fz, axle.LoadSensitivity) * grip;
                float maxLong = muLong * fz;

                float slipAngle = TireModel.SlipAngle(vLong, vLat);
                float cornerMass = fz / Gravity;
                float lowSpeedLateral = -vLat * cornerMass / dt * 0.35f;
                float blend = Mathf.Clamp01((Mathf.Abs(vLong) - 0.5f) / 3f);
                // Сколько сцепления уже занято поворотом: контроль пробуксовки и ABS оставляют шине запас вбок.
                float latUse = TireModel.LateralCurve(slipAngle / Mathf.Max(0.5f, axle.PeakSlipAngle), axle.SlideGrip) * blend;
                float combined = TireModel.CombinedLimit(latUse);

                // Привод с контролем пробуксовки.
                float driveForce = driveTorque / w.Radius;
                float tcLimit = maxLong * Mathf.Max(0.15f, combined) * (1f + Assists.TractionSlip);
                driveForce = Mathf.Clamp(driveForce, -tcLimit, tcLimit);

                // Тормоз: на ходу против движения, на месте удерживает машину. ABS не даёт заблокировать колесо.
                float share = w.Front ? axle.BrakeShare / Mathf.Max(1, frontCount) : axle.BrakeShare / Mathf.Max(1, rearCount);
                float brake = brakeTotal * share;
                bool abs = Assists.Abs && !Locked;
                if (abs) brake = Mathf.Min(brake, maxLong * Mathf.Max(0.3f, combined) * 0.97f);
                if (handbrake && !w.Front) brake = Mathf.Max(brake, spec.HandbrakeForce * fz * (Locked ? 3f : 1.3f));
                float brakeForce = -Mathf.Clamp(vLong / 0.4f, -1f, 1f) * brake;

                float surfaceRoll = w.Surface != null ? w.Surface.RollingResistance : 0f;
                float rolling = -Mathf.Clamp(vLong / 0.5f, -1f, 1f) * (spec.RollingResistance + surfaceRoll) * fz;

                var tire = TireModel.Solve(driveForce + brakeForce + rolling, vLat, slipAngle, fz,
                                           muLong, muLat, axle.PeakSlipAngle, axle.SlideGrip, blend, lowSpeedLateral);

                Vector3 point = w.HitPoint + up * (Vector3.Dot(com - w.HitPoint, up) * ForceHeightFraction);
                body.AddForceAtPosition(fwd * tire.Longitudinal + side * tire.Lateral, point);

                // Вращение колеса: качение плюс пробуксовка, блокировка — ноль.
                bool spinning = tire.LongSlip && Mathf.Abs(driveForce) > Mathf.Abs(brakeForce);
                bool locked = tire.LongSlip && !spinning && Mathf.Abs(brakeForce) > 0f;
                // Пробуксовка: колесо крутится быстрее дороги на долю, зависящую от лишней силы (до 45%),
                // на месте — пробуксовка «в дым». Без накопления: обороты не залипают на отсечке.
                float spinTarget = 0f;
                if (spinning)
                {
                    float excess01 = Mathf.Clamp01(tire.Excess / Mathf.Max(1f, maxLong));
                    spinTarget = Mathf.Sign(driveForce) * ((Mathf.Abs(vLong) / w.Radius) * (0.08f + 0.37f * excess01) + 25f * excess01 * (1f - blend));
                }
                w.SpinExtra = Mathf.MoveTowards(w.SpinExtra, spinTarget, (spinning ? 120f : 200f) * dt);
                w.Locked = locked;
                w.Omega = locked ? 0f : vLong / w.Radius + w.SpinExtra;
                w.LateralSlip = Mathf.Abs(vLat) * Mathf.Clamp01((Mathf.Abs(slipAngle) - axle.PeakSlipAngle * 0.6f) / axle.PeakSlipAngle);
                w.LongitudinalSlip = tire.LongSlip ? Mathf.Clamp01((tire.Utilization - 1f) * 2f + 0.3f) : 0f;
            }
        }

        void ApplyAero(Vector3 velocity, Vector3 up)
        {
            float speed = velocity.magnitude;
            body.AddForce(-velocity * (spec.Drag * speed));
            if (groundedCount > 0)
            {
                float v = Mathf.Max(0f, ForwardSpeed);
                body.AddForce(-up * (spec.Downforce * v * v));
            }
        }

        void ApplyEffects(Vector3 velocity, Vector3 forward)
        {
            float cap = Effects.SlowCap;
            float speed = velocity.magnitude;
            if (speed > cap && speed > 0.1f)
            {
                float decel = Mathf.Min(9f, (speed - cap) * 3f);
                body.AddForce(-velocity / speed * decel, ForceMode.Acceleration);
            }
            float boost = Effects.BoostAcceleration;
            if (boost > 0f && groundedCount >= 2 && !Locked && ForwardSpeed < spec.TopSpeed * Effects.BoostFactor)
                body.AddForce(forward * boost, ForceMode.Acceleration);
            // Невесомость: часть силы тяжести снимается — прыжки выше и дольше, машина «плывёт».
            float gravity = Effects.GravityScale;
            if (gravity < 1f) body.AddForce(Vector3.up * Gravity * (1f - gravity), ForceMode.Acceleration);
        }

        /// После удара события (физический объект, движущаяся конструкция): изменение скорости за шаг
        /// не больше maxDeltaV, вверх — не больше части подброса текущего Хаоса, вращение — не быстрее его предела
        /// (в «умеренно» — без кувырков, в «безумии» — кувырки разрешены). Только технический предел: физика не «взрывается».
        public void LimitEventImpact(float maxDeltaV, bool shielded = false)
        {
            if (!Simulated || body == null) return;
            var power = Events.ChaosPower.Current;
            Vector3 dv = body.linearVelocity - lastVelocity;
            Vector3 horizontal = Vector3.ProjectOnPlane(dv, Vector3.up);
            // Под защитой — без подброса вверх и без закручивания.
            float up = Mathf.Min(dv.y, shielded ? 0.3f : power.MaxUp * 0.6f);
            Vector3 limited = Vector3.ClampMagnitude(horizontal, maxDeltaV) + Vector3.up * up;
            if ((limited - dv).sqrMagnitude > 0.01f) body.linearVelocity = lastVelocity + limited;
            float spin = shielded ? 1.5f : power.Wild ? power.MaxSpin : 4f;
            Vector3 w = body.angularVelocity;
            if (w.sqrMagnitude > spin * spin) body.angularVelocity = w * (spin / w.magnitude);
        }

        void ApplyStability(Vector3 up)
        {
            if (Assists.Stability <= 0f || groundedCount < 2) return;
            float beta = BodySlipAngle;
            float excess = Mathf.Abs(beta) - Assists.StabilityAngle;
            if (excess <= 0f) return;
            float yawRate = Vector3.Dot(body.angularVelocity, up);
            float correct = Mathf.Sign(beta) * Mathf.Min(excess / 12f, 1.5f) * 6f;
            // Гасим вращение, которое увеличивает занос.
            float damping = yawRate * Mathf.Sign(beta) < 0f ? -yawRate * 3f : 0f;
            body.AddTorque(up * ((correct + damping) * Assists.Stability), ForceMode.Acceleration);
        }

        void ApplyAirControl(Vector3 up, float dt)
        {
            // Кувырок после удара события (хаос): машину не выравнивает, она крутится по инерции.
            if (groundedCount > 0 || Effects.Tumbling) return;
            // В воздухе машина мягко выравнивается колёсами вниз: аркада, без кувырков от трамплина.
            // Сила выравнивания и гашение — характер машины: тяжёлый внедорожник летит ровно, лёгкая машинка вертлявее.
            Vector3 axis = Vector3.Cross(up, Vector3.up);
            body.AddTorque(axis * spec.AirLeveling - body.angularVelocity * spec.AirDamping, ForceMode.Acceleration);
            // После подброса (батут, мина) руль немного поворачивает машину в воздухе: водитель успевает
            // выровнять её к приземлению и вернуть контроль.
            if (airTime > 0.25f && !Locked) body.AddTorque(Vector3.up * Input.Steer * spec.AirSteer, ForceMode.Acceleration);
        }

        /// Машина лежит на боку или крыше.
        public bool IsUpsideDown => Vector3.Dot(transform.up, Vector3.up) < 0.3f;
    }
}
