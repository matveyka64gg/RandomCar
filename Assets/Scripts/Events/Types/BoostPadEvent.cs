using UnityEngine;

namespace RandomCar.Events
{
    /// Ускорительная полоса: бирюзовые шевроны проявляются на полосе цели. Наезд: буст +25% на 2 с.
    public sealed class BoostPadEvent : RaceEvent
    {
        Transform pad;
        TelegraphMarker marker;

        void Awake()
        {
            pad = Child("Pad");
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnTelegraph()
        {
            Radius = 2.2f;
            var h = Predict(Target, Definition.WarningSeconds + 0.2f, 2f);
            Center = h.Position;
            pad.gameObject.SetActive(true);
            pad.SetPositionAndRotation(Center + Vector3.up * 0.01f, Quaternion.LookRotation(h.Forward));
            pad.localScale = new Vector3(1f, 1f, 0.01f);
            marker.Show(Center - h.Forward * 4.5f, h.Forward, 2f, true, true, 1.6f);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            pad.localScale = new Vector3(1f, 1f, Mathf.Max(0.01f, EaseOutBack(Mathf.Clamp01(progress * 1.3f))));
        }

        protected override void OnActivate()
        {
            marker.Hide();
            pad.localScale = Vector3.one;
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            foreach (var racer in Racers)
            {
                Vector3 local = pad.InverseTransformPoint(racer.Car.position);
                if (Mathf.Abs(local.x) > 2.0f || Mathf.Abs(local.z) > 3.2f) continue;
                if (!FirstHit(racer.Controller)) continue;
                racer.Controller.ApplyBoost(FairnessLimits.MaxBoostFactor);
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            pad.localScale = new Vector3(1f, 1f, Mathf.Max(0.01f, 1f - progress));
        }

        protected override void Cleanup()
        {
            marker.Hide();
            pad.gameObject.SetActive(false);
        }
    }
}
