using UnityEngine;

namespace RandomCar.Car
{
    /// Визуал машины: вращение колёс по скорости, поворот передних колёс за рулём, лёгкий крен и клевок кузова.
    /// На поведение машины не влияет.
    [RequireComponent(typeof(CarController))]
    public sealed class CarVisuals : MonoBehaviour
    {
        [SerializeField] Transform body;
        [SerializeField] Transform[] frontWheels = new Transform[0];
        [SerializeField] Transform[] rearWheels = new Transform[0];
        [SerializeField] float wheelRadius = 0.36f;
        [SerializeField] float maxSteerAngle = 26f;
        [SerializeField] float maxRoll = 3.5f;
        [SerializeField] float maxPitch = 2f;
        [SerializeField] ParticleSystem boostFx;
        [SerializeField] ParticleSystem dustFx;

        CarController car;
        Quaternion[] frontBase;
        Quaternion[] rearBase;
        Quaternion bodyBase;
        Vector3 bodyBasePosition;
        float spin;
        float steerAngle;
        float roll;
        float pitch;

        public void Configure(Transform bodyTransform, Transform[] front, Transform[] rear, float radius)
        {
            body = bodyTransform;
            frontWheels = front;
            rearWheels = rear;
            wheelRadius = radius;
        }

        public void ConfigureFx(ParticleSystem boost, ParticleSystem dust)
        {
            boostFx = boost;
            dustFx = dust;
        }

        void OnEnable()
        {
            if (car == null) car = GetComponent<CarController>();
            car.Bumped += OnBumped;
        }

        void OnDisable() => car.Bumped -= OnBumped;

        void OnBumped(float strength)
        {
            if (dustFx != null && strength > 1.5f) dustFx.Emit(Mathf.Clamp((int)(strength * 3f), 6, 30));
        }

        void UpdateFx()
        {
            if (boostFx == null) return;
            var emission = boostFx.emission;
            emission.enabled = car.IsBoosted && car.Speed > 1f;
            if (dustFx != null && car.IsSlipping && Mathf.Abs(car.Speed) > 6f && Random.value < 0.5f) dustFx.Emit(1);
        }

        void Awake()
        {
            car = GetComponent<CarController>();
            frontBase = Capture(frontWheels);
            rearBase = Capture(rearWheels);
            if (body != null)
            {
                bodyBase = body.localRotation;
                bodyBasePosition = body.localPosition;
            }
        }

        static Quaternion[] Capture(Transform[] wheels)
        {
            var result = new Quaternion[wheels.Length];
            for (int i = 0; i < wheels.Length; i++) result[i] = wheels[i] != null ? wheels[i].localRotation : Quaternion.identity;
            return result;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            spin = Mathf.Repeat(spin + car.Speed / Mathf.Max(0.1f, wheelRadius) * Mathf.Rad2Deg * dt, 360f);
            steerAngle = Mathf.MoveTowards(steerAngle, car.AppliedSteer * maxSteerAngle, 180f * dt);

            Quaternion spinRotation = Quaternion.AngleAxis(spin, Vector3.right);
            Quaternion steerRotation = Quaternion.AngleAxis(steerAngle, Vector3.up);
            for (int i = 0; i < frontWheels.Length; i++)
                if (frontWheels[i] != null) frontWheels[i].localRotation = steerRotation * spinRotation * frontBase[i];
            for (int i = 0; i < rearWheels.Length; i++)
                if (rearWheels[i] != null) rearWheels[i].localRotation = spinRotation * rearBase[i];

            UpdateFx();
            if (body == null) return;
            // Крен наружу поворота и клевок при торможении: только картинка.
            float speed01 = Mathf.Clamp01(Mathf.Abs(car.Speed) / CarController.MaxSpeed);
            float targetRoll = -car.AppliedSteer * maxRoll * speed01;
            float targetPitch = Mathf.Clamp(-car.Acceleration / 22f, -1f, 1f) * maxPitch;
            roll = Mathf.Lerp(roll, targetRoll, 1f - Mathf.Exp(-6f * dt));
            pitch = Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-6f * dt));
            body.localRotation = Quaternion.Euler(pitch, 0f, roll) * bodyBase;
            body.localPosition = bodyBasePosition + Vector3.up * 0.02f * Mathf.Abs(roll) / maxRoll;
        }
    }
}
