using UnityEngine;

namespace RandomCar.Events
{
    /// Банановая кожура: жёлтая метка «скользко» 1.5 с, падает кожура, на 4 с остаётся лужа.
    /// На луже машину ведёт в занос (не дольше 1.5 с на заезд).
    public sealed class BananaEvent : RaceEvent
    {
        Transform banana;
        Transform puddle;
        TelegraphMarker marker;
        float yaw;

        void Awake()
        {
            banana = Child("Banana");
            puddle = Child("Puddle");
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnTelegraph()
        {
            Radius = 2.2f;
            var h = Spot(Radius);
            Center = h.Position;
            yaw = RandomRange(0f, 360f);
            banana.gameObject.SetActive(true);
            banana.localScale = Vector3.one * 1.8f;
            puddle.gameObject.SetActive(false);
            marker.Show(Center, h.Forward, Radius, false, false);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            float fall = Mathf.Clamp01(progress * 1.4f);
            banana.SetPositionAndRotation(Center + Vector3.up * Mathf.Lerp(7f, 0.02f, fall * fall),
                                          Quaternion.Euler(0f, yaw + (1f - fall) * 540f, 0f));
        }

        protected override void OnActivate()
        {
            marker.Hide();
            puddle.gameObject.SetActive(true);
            puddle.SetPositionAndRotation(Center + Vector3.up * 0.025f, Quaternion.Euler(0f, yaw, 0f));
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            float grow = Mathf.Clamp01(time / 0.3f);
            puddle.localScale = new Vector3(Radius, 1f, Radius * 0.85f) * grow;
            foreach (var racer in RacersNear(Center, Radius))
                racer.Controller.ApplySlip(1.0f);
        }

        protected override void OnEndUpdate(float progress)
        {
            puddle.localScale = new Vector3(Radius, 1f, Radius * 0.85f) * (1f - progress);
            banana.localScale = Vector3.one * 1.8f * (1f - progress);
        }

        protected override void Cleanup()
        {
            marker.Hide();
            banana.gameObject.SetActive(false);
            puddle.gameObject.SetActive(false);
        }
    }
}
