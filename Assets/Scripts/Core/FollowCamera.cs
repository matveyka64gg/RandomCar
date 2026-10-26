using RandomCar.Car;
using RandomCar.Race;
using UnityEngine;

namespace RandomCar.Core
{
    /// Камера за машиной игрока: плавный догон, поле зрения растёт со скоростью, мгновенная установка на рестарте.
    [RequireComponent(typeof(Camera))]
    public sealed class FollowCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] RaceManager race;
        [SerializeField] float distance = 7.2f;
        [SerializeField] float height = 2.6f;
        [SerializeField] float lookHeight = 1.1f;
        [SerializeField] float baseFov = 60f;
        [SerializeField] float speedFov = 10f;

        Camera cam;
        CarController car;
        bool snapped;
        int attempt = -1;
        Vector3 basePosition;
        float avoidLift;
        float shakeAmplitude;
        float shakeDuration = 1f;
        float shakeUntil;

        public Transform Target
        {
            get => target;
            set { Unsubscribe(); target = value; car = value != null ? value.GetComponent<CarController>() : null; Subscribe(); snapped = false; }
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

        void OnBumped(float strength) => Shake(Mathf.Clamp(strength * 0.03f, 0f, 0.3f), 0.35f);

        void OnDestroy() => Unsubscribe();

        public void Configure(Transform targetTransform, RaceManager raceManager)
        {
            target = targetTransform;
            race = raceManager;
        }

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (target != null) car = target.GetComponent<CarController>();
            Subscribe();
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (race != null && race.Attempt != attempt)
            {
                attempt = race.Attempt;
                snapped = false;
            }

            Vector3 flatForward = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
            Vector3 desired = target.position - flatForward * distance + Vector3.up * height;

            // Чужая машина вплотную к камере: камера приподнимается, чтобы не влезать в неё.
            float lift = 0f;
            if (race != null)
                foreach (var racer in race.Racers)
                {
                    if (racer.Car == target) continue;
                    Vector3 d = racer.Car.position - basePosition;
                    d.y = 0f;
                    float m = d.magnitude;
                    if (m < 4f) lift = Mathf.Max(lift, (4f - m) / 4f * 2.6f);
                }
            avoidLift = Mathf.Lerp(avoidLift, lift, 1f - Mathf.Exp(-6f * Time.deltaTime));
            desired += Vector3.up * avoidLift;

            if (!snapped)
            {
                basePosition = desired;
                snapped = true;
            }
            else
            {
                float t = 1f - Mathf.Exp(-7f * Time.deltaTime);
                basePosition = Vector3.Lerp(basePosition, desired, t);
            }
            transform.position = basePosition;
            transform.LookAt(target.position + Vector3.up * lookHeight);

            // Тряска накладывается поверх базовой позиции и не накапливается (в том числе на паузе).
            if (Time.time < shakeUntil)
            {
                float k = (shakeUntil - Time.time) / shakeDuration;
                float t = Time.time * 28f;
                var offset = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * (2f * shakeAmplitude * k);
                transform.position = basePosition + transform.rotation * offset;
            }

            float speed01 = car != null ? Mathf.Clamp01(car.Speed / CarController.MaxSpeed) : 0f;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFov + speedFov * speed01, 1f - Mathf.Exp(-3f * Time.deltaTime));
        }
    }
}
