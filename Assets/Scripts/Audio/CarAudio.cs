using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Audio
{
    /// Звук машины по её реальному состоянию: обороты, нагрузка, турбина, клапан, скольжение шин, грунт.
    /// Машина игрока звучит «в салоне» (2D, с ветром), соперники — в пространстве с эффектом Доплера.
    [RequireComponent(typeof(CarController))]
    public sealed class CarAudio : MonoBehaviour
    {
        CarController car;
        EngineSynth synth;
        AudioSource source;
        bool player;

        void Awake()
        {
            car = GetComponent<CarController>();
            var go = new GameObject("EngineAudio");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.5f, 1.2f);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.dopplerLevel = 0.5f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 6f;
            source.maxDistance = 160f;
            synth = go.AddComponent<EngineSynth>();
            synth.MaxRpm = car.Spec.Engine.MaxRpm;
            synth.Cylinders = car.Spec.Engine.Cylinders;
        }

        void Start()
        {
            player = GetComponent<PlayerDriver>() != null;
            source.spatialBlend = player ? 0.15f : 1f;
            source.dopplerLevel = player ? 0f : 0.5f;
            source.Play();
        }

        void OnEnable() => car.Turbo.BlowOff += OnBlowOff;
        void OnDisable() => car.Turbo.BlowOff -= OnBlowOff;

        void OnBlowOff(float strength) => synth.TriggerBlowOff(strength);

        /// Машина игрока стала другой (рестарт с другой машиной): переключить режим звука.
        public void SetPlayer(bool isPlayer)
        {
            player = isPlayer;
            source.spatialBlend = player ? 0.15f : 1f;
            source.dopplerLevel = player ? 0f : 0.5f;
        }

        void Update()
        {
            var drivetrain = car.Drivetrain;
            var physics = car.Physics;
            synth.Volume = (player ? 0.85f : 0.9f) * RandomCar.Core.GameSettings.SfxVolume;
            synth.Rpm = drivetrain.Rpm;
            synth.Load = drivetrain.Load;
            synth.Limiter = drivetrain.OnLimiter;
            synth.TurboShaft = car.Turbo.ShaftSpeed01;
            synth.Boost = car.Turbo.Boost01;

            float skid = 0f, gravel = 0f;
            int grounded = 0;
            foreach (var w in physics.Wheels)
            {
                if (!w.Grounded) continue;
                grounded++;
                bool dusty = w.Surface != null && w.Surface.Dusty;
                float slip = Mathf.Max(Mathf.Clamp01((w.LateralSlip - 1.5f) / 5f), w.LongitudinalSlip, w.Locked && physics.SpeedAbs > 3f ? 0.8f : 0f);
                if (dusty) gravel += Mathf.Clamp01(physics.SpeedAbs / 25f) * 0.6f + slip * 0.4f;
                else skid += slip;
            }
            synth.TireSkid = grounded > 0 ? Mathf.Clamp01(skid / 2f) : 0f;
            synth.Gravel = grounded > 0 ? Mathf.Clamp01(gravel / 2f) : 0f;
            float speed01 = Mathf.Clamp01(physics.SpeedAbs / 60f);
            synth.Wind = player ? speed01 * speed01 * 0.5f : 0f;
        }
    }
}
