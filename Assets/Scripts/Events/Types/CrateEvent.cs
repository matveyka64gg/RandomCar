using UnityEngine;

namespace RandomCar.Events
{
    /// Бонусные ящики: три ящика опускаются с неба на выбранный участок трассы. Касание: буст +25% на 2 с.
    public sealed class CrateEvent : RaceEvent
    {
        const int Count = 3;
        const float Hover = 0.35f;

        readonly Transform[] crates = new Transform[Count];
        readonly Vector3[] spots = new Vector3[Count];
        readonly bool[] taken = new bool[Count];
        TelegraphMarker marker;
        ParticleSystem confetti;

        void Awake()
        {
            for (int i = 0; i < Count; i++) crates[i] = Child("Crate_" + i);
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            confetti = Fx("FxConfetti");
        }

        protected override void OnTelegraph()
        {
            Radius = 2.8f;
            var h = Spot(Radius);
            Center = h.Position;
            for (int i = 0; i < Count; i++)
            {
                float lateral = Mathf.Clamp(h.Lateral + (i - 1) * 2.1f, -RoadHalf + 0.8f, RoadHalf - 0.8f);
                spots[i] = At(h.Progress + (i - 1) * 2.5f / Context.Track.Length, lateral).Position;
                taken[i] = false;
                crates[i].gameObject.SetActive(true);
                crates[i].localScale = Vector3.one;
            }
            marker.Show(Center, h.Forward, Radius, true, true);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            float drop = 1f - Mathf.Pow(1f - progress, 2f);
            for (int i = 0; i < Count; i++)
                crates[i].SetPositionAndRotation(spots[i] + Vector3.up * Mathf.Lerp(16f, Hover, drop), Quaternion.Euler(0f, time * 90f + i * 40f, 0f));
        }

        protected override void OnActivate() => marker.Hide();

        protected override void OnActiveUpdate(float progress, float time)
        {
            for (int i = 0; i < Count; i++)
            {
                if (taken[i]) continue;
                crates[i].SetPositionAndRotation(spots[i] + Vector3.up * (Hover + Mathf.Sin(time * 3f + i) * 0.15f),
                                                 Quaternion.Euler(0f, time * 90f + i * 40f, 0f));
                foreach (var racer in RacersNear(spots[i], 1.7f))
                {
                    taken[i] = true;
                    crates[i].gameObject.SetActive(false);
                    Play(confetti, spots[i] + Vector3.up * 0.8f);
                    racer.Controller.ApplyBoost(FairnessLimits.MaxBoostFactor);
                    break;
                }
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            for (int i = 0; i < Count; i++)
                if (!taken[i]) crates[i].localScale = Vector3.one * (1f - progress);
        }

        protected override void Cleanup()
        {
            marker.Hide();
            for (int i = 0; i < Count; i++) crates[i].gameObject.SetActive(false);
        }
    }
}
