using RandomCar.Car;
using RandomCar.Race;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RandomCar.Core
{
    public enum CameraView { Chase, Cockpit }

    /// Камера своей машины (в сети — тоже только своей).
    /// Сзади: следует за курсом машины (сглаженное направление скорости, а не нос — в заносе не дёргается),
    /// поле зрения растёт со скоростью, высота сглажена отдельно (прыжки без рывков), колесо мыши — ближе/дальше,
    /// сфера от машины к камере не пускает её сквозь дома, мосты, потолок тоннеля и землю.
    /// ПКМ: пока зажата — мышь вращает камеру вокруг машины (вертикаль ограничена, курсор скрыт), после
    /// отпускания камера плавно возвращается назад за машину.
    /// Из салона: место водителя за рулём (по модели), камера жёстко с кузовом — видно руль, приборы, подвеску;
    /// без салона — над капотом. Переключение — клавиша C (переназначается в настройках) или Y на геймпаде.
    [RequireComponent(typeof(Camera))]
    public sealed class FollowCamera : MonoBehaviour
    {
        const float MinDistance = 4.2f;
        const float MaxDistance = 14f;
        const float MinPitch = -8f;
        const float MaxPitch = 62f;
        const float ReturnDelay = 0.9f;
        const float CollisionRadius = 0.32f;

        [SerializeField] Transform target;
        [SerializeField] RaceManager race;
        [SerializeField] float distance = 7.6f;
        [SerializeField] float pitch = 14f;
        [SerializeField] float lookHeight = 1.2f;
        [SerializeField] float baseFov = 60f;
        [SerializeField] float speedFov = 10f;

        Camera cam;
        CarController car;
        CarVisuals visuals;
        bool snapped;
        int attempt = -1;
        float heading;
        float pivotY;
        float lookYaw, lookPitch;
        float releasedAt = -10f;
        bool orbiting;
        float zoom;
        float collided;
        float avoidLift;
        float shakeAmplitude;
        float shakeDuration = 1f;
        float shakeUntil;
        InputAction toggle;
        readonly RaycastHit[] hits = new RaycastHit[16];

        /// Камера слушает мышь и клавишу вида только в гонке (не в меню, паузе и на экране результатов).
        public bool AllowInput { get; set; }

        public CameraView View { get; private set; }

        /// ПКМ сейчас вращает камеру (для отладки и интерфейса).
        public bool Orbiting => orbiting;

        public Transform Target
        {
            get => target;
            set
            {
                RestoreLiner();
                Unsubscribe();
                target = value;
                car = value != null ? value.GetComponent<CarController>() : null;
                visuals = value != null ? value.GetComponent<CarVisuals>() : null;
                Subscribe();
                snapped = false;
            }
        }

        /// Тряска камеры: амплитуда ограничена 0.6 м, затухает к концу.
        public void Shake(float amplitude, float duration)
        {
            amplitude = Mathf.Clamp(amplitude, 0f, 0.6f);
            float current = Time.time < shakeUntil ? shakeAmplitude * (shakeUntil - Time.time) / shakeDuration : 0f;
            if (amplitude < current) return;
            shakeAmplitude = amplitude;
            shakeDuration = Mathf.Max(0.05f, duration);
            shakeUntil = Time.time + shakeDuration;
        }

        void Subscribe()
        {
            if (car != null) car.Bumped += OnBumped;
        }

        void Unsubscribe()
        {
            if (car != null) car.Bumped -= OnBumped;
        }

        /// Тряска по силе удара: обычный толчок — слегка, мощный подброс хаоса — заметно (но не дольше полсекунды).
        void OnBumped(float strength) => Shake(Mathf.Clamp(strength * 0.022f, 0f, 0.55f), strength > 20f ? 0.5f : 0.35f);

        void OnDestroy()
        {
            Unsubscribe();
            if (race != null) race.RacersChanged -= FollowPlayer;
            toggle?.Dispose();
            GameSettings.Changed -= ApplyBinding;
        }

        public void Configure(Transform targetTransform, RaceManager raceManager)
        {
            target = targetTransform;
            race = raceManager;
        }

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (target != null) Target = target;
            if (race != null) race.RacersChanged += FollowPlayer;
            View = (CameraView)GameSettings.CameraMode;
            toggle = new InputAction("CameraView", InputActionType.Button);
            toggle.AddBinding(DefaultKey);
            toggle.AddBinding("<Gamepad>/buttonNorth");
            ApplyBinding();
            toggle.Enable();
            GameSettings.Changed += ApplyBinding;
        }

        public const string DefaultKey = "<Keyboard>/c";

        /// Клавиша вида из настроек (переназначение): первая привязка — клавиатурная.
        void ApplyBinding()
        {
            if (toggle == null) return;
            string key = string.IsNullOrEmpty(GameSettings.CameraKey) ? DefaultKey : GameSettings.CameraKey;
            if (toggle.bindings[0].effectivePath != key) toggle.ApplyBindingOverride(0, key);
        }

        void FollowPlayer()
        {
            if (race.Player != null) Target = race.Player.Car;
        }

        public void SetView(CameraView view)
        {
            View = view;
            GameSettings.CameraMode = (int)view;
            snapped = false;
        }

        void Update()
        {
            if (!AllowInput)
            {
                EndOrbit();
                return;
            }
            if (toggle.WasPressedThisFrame() || TouchInput.ConsumeCamera()) SetView(View == CameraView.Chase ? CameraView.Cockpit : CameraView.Chase);
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (mouse.rightButton.isPressed)
            {
                if (!orbiting)
                {
                    orbiting = true;
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                Vector2 delta = mouse.delta.ReadValue() * (0.12f * GameSettings.MouseSensitivity);
                lookYaw = Mathf.Clamp(lookYaw + delta.x, -180f, 180f);
                float pitchMin = View == CameraView.Chase ? MinPitch - pitch : -40f;
                float pitchMax = View == CameraView.Chase ? MaxPitch - pitch : 40f;
                lookPitch = Mathf.Clamp(lookPitch - delta.y, pitchMin, pitchMax);
                if (View == CameraView.Cockpit) lookYaw = Mathf.Clamp(lookYaw, -125f, 125f);
            }
            else EndOrbit();
            float scroll = mouse.scroll.ReadValue().y;
            if (View == CameraView.Chase && Mathf.Abs(scroll) > 0.01f)
                zoom = Mathf.Clamp(zoom - Mathf.Sign(scroll) * 0.7f, MinDistance - distance, MaxDistance - distance);
        }

        void EndOrbit()
        {
            if (!orbiting) return;
            orbiting = false;
            releasedAt = Time.time;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;
            if (race != null && race.Attempt != attempt)
            {
                attempt = race.Attempt;
                snapped = false;
            }
            // Отпустили ПКМ — через паузу камера плавно возвращается назад за машину.
            if (!orbiting && Time.time - releasedAt > ReturnDelay)
            {
                float k = 1f - Mathf.Exp(-3.5f * dt);
                lookYaw = Mathf.LerpAngle(lookYaw, 0f, k);
                lookPitch = Mathf.Lerp(lookPitch, 0f, k);
            }

            if (View == CameraView.Cockpit) Cockpit();
            else Chase(dt);

            if (Time.time < shakeUntil)
            {
                float k = (shakeUntil - Time.time) / shakeDuration;
                float t = Time.time * 28f;
                float amp = View == CameraView.Cockpit ? 0.35f : 1f;
                var offset = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * (2f * shakeAmplitude * k * amp);
                transform.position += transform.rotation * offset;
            }
        }

        void Chase(float dt)
        {
            RestoreLiner();
            cam.nearClipPlane = 0.3f;
            // Курс: направление скорости на ходу (занос не разворачивает камеру), нос — на месте.
            Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
            // Хаос v5: машина кувыркается в полёте — камера держит курс по скорости (или прежний), а не крутится
            // вместе с кузовом: игрок видит, куда летит машина.
            bool flying = car != null && !car.IsGrounded && (car.Tumbling || Time.time - car.LaunchedAt < 6f);
            if (car != null && car.Physics.Body != null)
            {
                Vector3 velocity = Vector3.ProjectOnPlane(car.Physics.Body.linearVelocity, Vector3.up);
                float blend = Mathf.Clamp01((velocity.magnitude - 4f) / 10f) * 0.65f;
                if (flying) forward = velocity.sqrMagnitude > 9f ? velocity.normalized : Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
                else if (velocity.sqrMagnitude > 1f && Vector3.Dot(velocity, forward) > 0f) forward = Vector3.Slerp(forward, velocity.normalized, blend).normalized;
            }
            float wanted = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            if (!snapped)
            {
                heading = wanted;
                pivotY = target.position.y;
                collided = distance + zoom;
            }
            heading = Mathf.LerpAngle(heading, wanted, 1f - Mathf.Exp((flying ? -2.5f : -6f) * dt));
            // Высота опоры сглажена отдельно: в прыжке и на кочках камера не прыгает вместе с кузовом.
            pivotY = Mathf.Lerp(pivotY, target.position.y, 1f - Mathf.Exp(-5f * dt));

            // Чужая машина вплотную за нами: камера поднимается над ней, а не влезает в кузов.
            float lift = 0f;
            if (race != null)
                foreach (var racer in race.Racers)
                {
                    if (racer.Car == target || !racer.Car.gameObject.activeInHierarchy) continue;
                    Vector3 d = racer.Car.position - transform.position;
                    d.y = 0f;
                    if (d.magnitude < 4.5f) lift = Mathf.Max(lift, (4.5f - d.magnitude) / 4.5f * 14f);
                }
            avoidLift = Mathf.Lerp(avoidLift, lift, 1f - Mathf.Exp(-6f * dt));

            float dist = distance + zoom;
            var rotation = Quaternion.Euler(Mathf.Clamp(pitch + lookPitch + avoidLift, MinPitch, MaxPitch), heading + lookYaw, 0f);
            Vector3 pivot = new Vector3(target.position.x, pivotY, target.position.z) + Vector3.up * lookHeight;
            Vector3 desired = pivot - rotation * Vector3.forward * dist;

            // Препятствие между машиной и камерой: подтягиваемся вплотную к нему (быстро внутрь, медленно наружу).
            float allowed = Clear(pivot, desired - pivot, dist);
            collided = allowed < collided ? allowed : Mathf.Lerp(collided, allowed, 1f - Mathf.Exp(-2.5f * dt));
            if (!snapped) collided = allowed;
            snapped = true;
            Vector3 position = pivot - rotation * Vector3.forward * collided;
            // Не ниже земли под камерой.
            if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out var ground, 6f, ~0, QueryTriggerInteraction.Ignore) && ground.collider.attachedRigidbody == null)
                position.y = Mathf.Max(position.y, ground.point.y + 0.4f);
            transform.position = position;
            transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * 0.1f - position);

            float speed01 = car != null ? Mathf.Clamp01(Mathf.Abs(car.Speed) / 55f) : 0f;
            // В высоком полёте камера шире: видно и машину, и дорогу под ней.
            float air = flying ? Mathf.Clamp01((target.position.y - pivotGround) / 30f) * 14f : 0f;
            if (!flying) pivotGround = target.position.y;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFov + speedFov * speed01 + air, 1f - Mathf.Exp(-3f * dt));
        }

        /// Высота машины перед полётом (для расширения обзора в воздухе).
        float pivotGround;

        /// Свободное расстояние от опоры вдоль направления (сфера, без машин и триггеров).
        float Clear(Vector3 from, Vector3 direction, float length)
        {
            int count = Physics.SphereCastNonAlloc(from, CollisionRadius, direction.normalized, hits, length, ~0, QueryTriggerInteraction.Ignore);
            float best = length;
            for (int i = 0; i < count; i++)
            {
                var h = hits[i];
                if (h.collider.attachedRigidbody != null) continue;
                if (h.distance <= 0f) continue;
                best = Mathf.Min(best, h.distance - 0.15f);
            }
            return Mathf.Max(1.2f, best);
        }

        /// Верх кузова в координатах машины — по габаритам кузова (верх коллайдера кабины на 4 см ниже крыши).
        float RoofHeight()
        {
            if (car == null) return 1.4f;
            var b = car.BodyBounds;
            return b.max.y + 0.04f;
        }

        readonly System.Collections.Generic.List<Renderer> liner = new System.Collections.Generic.List<Renderer>();

        /// Из салона замкнутая обшивка кабины (CabinLiner: снаружи она даёт салону тень за стёклами)
        /// закрыла бы весь вид, а пилот (Driver) сидит прямо на месте камеры — у своей машины на время вида
        /// из салона они не рисуются.
        void HideLiner()
        {
            if (liner.Count > 0 || target == null) return;
            foreach (var r in target.GetComponentsInChildren<Renderer>())
            {
                if (!r.name.StartsWith("CabinLiner") && !r.name.StartsWith("Driver")) continue;
                liner.Add(r);
                r.forceRenderingOff = true;
            }
        }

        void RestoreLiner()
        {
            foreach (var r in liner)
                if (r != null) r.forceRenderingOff = false;
            liner.Clear();
        }

        void Cockpit()
        {
            HideLiner();
            cam.nearClipPlane = 0.05f;
            snapped = true;
            Vector3 eyeLocal;
            var wheel = visuals != null ? visuals.SteeringWheel : null;
            if (wheel != null)
            {
                // Голова водителя — за рулём и выше его центра, но под крышей (у низких суперкаров крыша близко).
                Vector3 wheelLocal = target.InverseTransformPoint(wheel.position);
                float roof = RoofHeight();
                eyeLocal = wheelLocal + new Vector3(0.06f, 0f, -0.66f);
                eyeLocal.y = Mathf.Min(wheelLocal.y + 0.34f, roof - 0.2f);
            }
            else eyeLocal = new Vector3(0f, 1.25f, 0.6f);
            transform.position = target.TransformPoint(eyeLocal);
            transform.rotation = target.rotation * Quaternion.Euler(4f + lookPitch, lookYaw, 0f);
            float speed01 = car != null ? Mathf.Clamp01(Mathf.Abs(car.Speed) / 55f) : 0f;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 70f + 6f * speed01, 1f - Mathf.Exp(-3f * Time.deltaTime));
        }
    }
}
