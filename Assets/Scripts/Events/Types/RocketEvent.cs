using UnityEngine;

namespace RandomCar.Events
{
    /// Ракета: красный прицел на полосе цели, в последнюю секунду предупреждения видно падение.
    /// Взрыв отбрасывает машины в радиусе 3 м и ненадолго замедляет.
    public sealed class RocketEvent : RaceEvent
    {
        const float FallSeconds = 1.0f;
        const float StartHeight = 140f;

        Transform rocket;
        TelegraphMarker marker;
        ParticleSystem explosion;
        ParticleSystem smoke;
        ParticleSystem sparks;
        ParticleSystem trail;
        Vector3 right;

        void Awake()
        {
            rocket = Child("Rocket");
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            explosion = Fx("FxExplosion");
            smoke = Fx("FxSmoke");
            sparks = Fx("FxSparks");
            trail = rocket.Find("FxTrail")?.GetComponent<ParticleSystem>();
        }

        protected override void OnTelegraph()
        {
            Radius = FairnessLimits.MaxHazardRadius;
            var h = Predict(Target, Definition.WarningSeconds, Radius);
            Center = h.Position;
            right = h.Right;
            marker.Show(Center, h.Forward, Radius, false, false);
            rocket.gameObject.SetActive(false);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            float fallStart = Definition.WarningSeconds - FallSeconds;
            if (time < fallStart) return;
            if (!rocket.gameObject.activeSelf)
            {
                rocket.gameObject.SetActive(true);
                if (trail != null) trail.Play(true);
            }
            float f = Mathf.Clamp01((time - fallStart) / FallSeconds);
            rocket.SetPositionAndRotation(Center + Vector3.up * Mathf.Lerp(StartHeight, 0f, f * f),
                                          Quaternion.Euler(0f, time * 240f, 0f));
        }

        protected override void OnActivate()
        {
            rocket.gameObject.SetActive(false);
            marker.Hide();
            Play(explosion, Center + Vector3.up * 0.6f);
            Play(smoke, Center);
            Play(sparks, Center + Vector3.up * 0.3f);
            ShakeCameraNear(Center, 0.6f, 60f);

            const float reach = FairnessLimits.MaxHazardRadius + 1.2f;   // радиус зоны плюс полкорпуса машины
            foreach (var racer in RacersNear(Center, reach))
            {
                Vector3 away = racer.Car.position - Center;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = right;
                float falloff = 1f - Mathf.Clamp01(away.magnitude / reach);
                racer.Controller.ApplyImpulse(away.normalized * (5f + 4f * falloff) + Vector3.up * (3f + 3f * falloff));
                racer.Controller.ApplySlow(0.7f, 1.0f);
            }
        }

        protected override void Cleanup()
        {
            rocket.gameObject.SetActive(false);
            marker.Hide();
        }
    }
}
