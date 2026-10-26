using UnityEngine;

namespace RandomCar.Events
{
    /// Трамплин: бирюзовая стрелка на полосе цели, трамплин поднимается из асфальта. Наезд даёт прыжок.
    public sealed class RampEvent : RaceEvent
    {
        const float Sink = 1.6f;

        Transform ramp;
        TelegraphMarker marker;
        Vector3 forward;

        void Awake()
        {
            ramp = Child("Ramp");
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnTelegraph()
        {
            Radius = 2.3f;
            var h = Predict(Target, Definition.WarningSeconds + 0.3f, 2.2f);
            Center = h.Position;
            forward = h.Forward;
            ramp.gameObject.SetActive(true);
            ramp.SetPositionAndRotation(Center - Vector3.up * Sink, Quaternion.LookRotation(forward));
            marker.Show(Center - forward * 5.5f, forward, 2.2f, true, true, 1.4f);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            float rise = EaseOutBack(Mathf.Clamp01((progress - 0.45f) / 0.45f));
            ramp.position = Center - Vector3.up * Sink * (1f - rise);
        }

        protected override void OnActivate()
        {
            marker.Hide();
            ramp.position = Center;
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            foreach (var racer in Racers)
            {
                Vector3 local = ramp.InverseTransformPoint(racer.Car.position);
                if (Mathf.Abs(local.x) > 2.3f || local.z < -3.2f || local.z > 2.8f) continue;
                if (!racer.Controller.IsGrounded || racer.Controller.Speed < 4f) continue;
                if (!FirstHit(racer.Controller)) continue;
                racer.Controller.ApplyImpulse(forward * 2f + Vector3.up * 7.5f);
                racer.Controller.ApplyBoost(1.1f);
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            ramp.position = Center - Vector3.up * Sink * progress;
        }

        protected override void Cleanup()
        {
            marker.Hide();
            ramp.gameObject.SetActive(false);
        }
    }
}
