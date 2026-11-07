using UnityEngine;

namespace RandomCar.Car
{
    /// Эффекты машины по её реальному состоянию: следы (ширина — по шине) и дым при скольжении и пробуксовке
    /// на месте, пыль на грунте и из-под колёс при приземлении, искры при ударах и когда кузов скользит по дороге
    /// или стене, хлопки из выхлопа при переключении под наддувом, на отсечке и при старте, «пшик» перепускного
    /// клапана турбины, пламя рывка. Частицы — дочерние системы префаба, здесь они только выпускаются.
    [RequireComponent(typeof(CarController))]
    public sealed class CarFx : MonoBehaviour
    {
        [SerializeField] ParticleSystem tireSmoke;
        [SerializeField] ParticleSystem dust;
        [SerializeField] ParticleSystem sparks;
        [SerializeField] ParticleSystem exhaustPop;
        [SerializeField] ParticleSystem boostFlame;

        /// Не чаще: искры скольжения кузова (с), пыль посадки (с).
        const float ScrapeInterval = 0.035f;
        const float LandingInterval = 0.3f;

        CarController car;
        CarPhysics physics;
        int[] skidTracks;
        float[] skidWidth;
        float[] smokeDebt;
        float[] dustDebt;
        bool wasLimiter;
        bool wasLaunching;
        float scrapeAt;
        float landedAt = -10f;

        public void Configure(ParticleSystem smoke, ParticleSystem dustFx, ParticleSystem sparksFx, ParticleSystem pop, ParticleSystem boost)
        {
            tireSmoke = smoke;
            dust = dustFx;
            sparks = sparksFx;
            exhaustPop = pop;
            boostFlame = boost;
        }

        /// Сколько раз кузов высек искры скольжением (проверки).
        public int Scrapes { get; private set; }

        void Awake()
        {
            car = GetComponent<CarController>();
            physics = GetComponent<CarPhysics>();
            int n = physics.Wheels.Length;
            smokeDebt = new float[n];
            dustDebt = new float[n];
            skidWidth = new float[n];
            for (int i = 0; i < n; i++)
            {
                // След чуть уже шины: пятно контакта, а не боковины.
                var r = physics.Wheels[i].Visual != null ? physics.Wheels[i].Visual.GetComponent<Renderer>() : null;
                skidWidth[i] = r != null ? Mathf.Clamp(r.localBounds.size.x * 0.82f, 0.14f, 0.34f) : SkidMarks.DefaultWidth;
            }
        }

        void OnEnable()
        {
            car.Impact += OnImpact;
            car.Bumped += OnBumped;
            physics.Landed += OnLanded;
            physics.Drivetrain.Shifted += OnShifted;
            if (physics.Turbo != null) physics.Turbo.BlowOff += OnBlowOff;
        }

        void OnDisable()
        {
            car.Impact -= OnImpact;
            car.Bumped -= OnBumped;
            physics.Landed -= OnLanded;
            if (physics.Drivetrain != null) physics.Drivetrain.Shifted -= OnShifted;
            if (physics.Turbo != null) physics.Turbo.BlowOff -= OnBlowOff;
        }

        void OnImpact(Vector3 point, float strength)
        {
            if (sparks == null || strength < 2f) return;
            var p = new ParticleSystem.EmitParams { position = point, applyShapeToPosition = true };
            sparks.Emit(p, Mathf.Clamp((int)(strength * 5f), 6, 60));
        }

        void OnBumped(float strength)
        {
            if (dust == null || strength < 2.5f) return;
            var p = new ParticleSystem.EmitParams { position = transform.position + Vector3.up * 0.2f, applyShapeToPosition = true };
            dust.Emit(p, Mathf.Clamp((int)(strength * 3f), 6, 30));
        }

        /// Посадка: из-под каждого колеса на земле — кольцо пыли (цвет покрытия; на асфальте — светлая пыль).
        void OnLanded(float strength)
        {
            if (dust == null || strength < 3f || Time.time - landedAt < LandingInterval) return;
            landedAt = Time.time;
            int perWheel = Mathf.Clamp((int)(strength * 0.7f), 3, 10);
            foreach (var w in physics.Wheels)
            {
                if (!w.Grounded) continue;
                Color tint = w.Surface != null && w.Surface.Dusty ? w.Surface.DustColor : new Color(0.82f, 0.8f, 0.76f, 0.55f);
                for (int k = 0; k < perWheel; k++)
                {
                    float a = (k + Random.value * 0.5f) / perWheel * Mathf.PI * 2f;
                    Vector3 out_ = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var p = new ParticleSystem.EmitParams
                    {
                        position = w.HitPoint + Vector3.up * 0.08f + out_ * 0.15f,
                        velocity = out_ * Mathf.Clamp(strength * 0.35f, 1.5f, 6f) + Vector3.up * Random.Range(0.3f, 1.2f),
                        startColor = tint,
                        applyShapeToPosition = false,
                    };
                    dust.Emit(p, 1);
                }
            }
        }

        /// Кузов скользит по дороге или стене (на крыше, боком, вдоль отбойника): искры из точки касания.
        void OnCollisionStay(Collision collision)
        {
            if (sparks == null || Time.time - scrapeAt < ScrapeInterval || !physics.Simulated) return;
            if (collision.contactCount == 0) return;
            // Только о дорогу, стены и другие машины (не о мячи, желе и прочие тела событий).
            var other = collision.rigidbody;
            if (other != null && other.GetComponent<CarController>() == null) return;
            var contact = collision.GetContact(0);
            Vector3 relative = physics.Body.GetPointVelocity(contact.point) - (other != null ? other.GetPointVelocity(contact.point) : Vector3.zero);
            Vector3 slide = Vector3.ProjectOnPlane(relative, contact.normal);
            float speed = slide.magnitude;
            if (speed < 5f) return;
            scrapeAt = Time.time;
            var surface = Surface.Of(collision.collider);
            if (surface != null && surface.Dusty)
            {
                if (dust == null) return;
                var d = new ParticleSystem.EmitParams { position = contact.point + contact.normal * 0.1f, startColor = surface.DustColor, applyShapeToPosition = false,
                    velocity = -slide.normalized * 1.5f + contact.normal * 1.2f };
                dust.Emit(d, 2);
                return;
            }
            Scrapes++;
            // Искры летят назад по скольжению и чуть от поверхности.
            int count = Mathf.Clamp((int)(speed * 0.25f), 2, 7);
            for (int k = 0; k < count; k++)
            {
                var p = new ParticleSystem.EmitParams
                {
                    position = contact.point + contact.normal * 0.05f,
                    velocity = (-slide.normalized * Random.Range(0.4f, 0.9f) + contact.normal * Random.Range(0.1f, 0.5f) + Random.insideUnitSphere * 0.25f) * speed * 0.6f,
                    applyShapeToPosition = false,
                };
                sparks.Emit(p, 1);
            }
        }

        void OnShifted(int from, int to)
        {
            // Хлопок из выхлопа при переключении вверх под наддувом.
            if (to > from && from > 0 && physics.Turbo.Boost01 > 0.45f) Pop(4);
        }

        /// Сброс газа под наддувом: перепускной клапан выпускает облачко у переда машины.
        void OnBlowOff(float pressure)
        {
            if (tireSmoke == null || pressure < 0.3f || !isActiveAndEnabled) return;
            var p = new ParticleSystem.EmitParams
            {
                position = transform.TransformPoint(new Vector3(0.35f, 0.55f, physics.Wheelbase * 0.5f + 0.6f)),
                velocity = transform.right * 1.2f + Vector3.up * 0.8f,
                startColor = new Color(1f, 1f, 1f, 0.35f),
                startSize = 0.45f,
                applyShapeToPosition = false,
            };
            tireSmoke.Emit(p, Mathf.Clamp((int)(pressure * 3f), 1, 4));
        }

        void Pop(int count)
        {
            if (exhaustPop != null) exhaustPop.Emit(count);
        }

        void Update()
        {
            if (skidTracks == null && SkidMarks.Instance != null)
            {
                skidTracks = new int[physics.Wheels.Length];
                for (int i = 0; i < skidTracks.Length; i++) skidTracks[i] = SkidMarks.Instance.NewTrack();
            }

            float dt = Time.deltaTime;
            var wheels = physics.Wheels;
            float speed = physics.SpeedAbs;
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                float slip = Mathf.Max(Mathf.Clamp01((w.LateralSlip - 1.2f) / 4f), w.LongitudinalSlip);
                if (w.Locked && speed > 3f) slip = Mathf.Max(slip, 0.7f);
                // Пробуксовка на месте («в дым»): колесо крутится, машина почти стоит.
                float burnout = w.Grounded ? Mathf.Clamp01((Mathf.Abs(w.Omega * w.Radius - physics.ForwardSpeed) - 2f) / 8f) : 0f;
                var surface = w.Surface;
                bool dusty = surface != null && surface.Dusty;

                // Во время смены карты следов ещё нет (сцена карты грузится).
                if (skidTracks != null && SkidMarks.Instance != null)
                {
                    bool marks = w.Grounded && (slip > 0.25f && speed > 2f || burnout > 0.3f) && (surface == null || surface.SkidMarks);
                    if (marks)
                    {
                        Color color = surface != null ? surface.SkidColor : new Color(0.08f, 0.08f, 0.09f, 0.75f);
                        Vector3 fwd = Quaternion.AngleAxis(w.SteerAngle, transform.up) * transform.forward;
                        SkidMarks.Instance.Add(skidTracks[i], w.HitPoint, w.HitNormal, fwd, Mathf.Max(slip, burnout), color, skidWidth[i]);
                    }
                    else SkidMarks.Instance.End(skidTracks[i]);
                }

                if (!w.Grounded) continue;
                Vector3 at = w.HitPoint + Vector3.up * 0.15f;
                float smoke = Mathf.Max(slip > 0.4f && speed > 1f ? slip : 0f, burnout);
                if (!dusty && tireSmoke != null && smoke > 0f)
                {
                    smokeDebt[i] += smoke * (32f + 30f * burnout) * dt;
                    Emit(tireSmoke, at, ref smokeDebt[i], Color.white);
                }
                if (dusty && dust != null && (speed > 3f || burnout > 0.2f))
                {
                    dustDebt[i] += (speed * 0.35f + slip * 18f + burnout * 20f) * dt;
                    Emit(dust, at, ref dustDebt[i], surface.DustColor);
                }
            }

            bool limiter = physics.Drivetrain.OnLimiter;
            if (limiter && !wasLimiter && Random.value < 0.35f) Pop(2);
            wasLimiter = limiter;

            // Старт с места в пол: залп хлопков из выхлопа.
            bool launching = !physics.Locked && physics.Input.Throttle > 0.9f && Mathf.Abs(physics.ForwardSpeed) < 3f && physics.IsGrounded;
            if (launching && !wasLaunching) Pop(5);
            wasLaunching = launching;

            if (boostFlame != null)
            {
                var emission = boostFlame.emission;
                emission.enabled = car.IsBoosted && speed > 1f;
            }
        }

        static void Emit(ParticleSystem ps, Vector3 position, ref float debt, Color tint)
        {
            int count = (int)debt;
            if (count <= 0) return;
            debt -= count;
            var p = new ParticleSystem.EmitParams { position = position, applyShapeToPosition = true };
            if (tint != Color.white) p.startColor = tint;
            ps.Emit(p, count);
        }
    }
}
