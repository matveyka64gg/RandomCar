using System.Collections.Generic;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Дорожка ускорения: три полосы-ускорителя друг за другом на одной полосе дороги. Каждая даёт рывок
    /// (+25%, 2 с, таймер продлевается) — кто проедет все три, держит рывок дольше. Выбор: ехать ли по этой полосе,
    /// если рядом другая опасность. Держится, пока игрок не проедет участок.
    public sealed class BoostChainEvent : RaceEvent
    {
        public const int Pads = 3;
        const float Spacing = 14f;

        readonly Transform[] pads = new Transform[Pads];
        readonly HashSet<CarController>[] taken = new HashSet<CarController>[Pads];
        TelegraphMarker marker;
        Vector3 forward;

        void Awake()
        {
            for (int i = 0; i < Pads; i++)
            {
                pads[i] = Child("Pad_" + i);
                taken[i] = new HashSet<CarController>();
            }
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnTelegraph()
        {
            Radius = 2.2f;
            var center = Spot(2.2f);
            Center = center.Position;
            forward = center.Forward;
            float length = Context.Track.Length;
            for (int i = 0; i < Pads; i++)
            {
                var h = At(Progress + (i - 1) * Spacing / length, center.Lateral);
                pads[i].gameObject.SetActive(true);
                pads[i].SetPositionAndRotation(h.Position + Vector3.up * 0.01f, Quaternion.LookRotation(h.Forward));
                pads[i].localScale = new Vector3(1f, 1f, 0.01f);
                taken[i].Clear();
            }
            var first = At(Progress - Spacing / length, center.Lateral);
            marker.Show(first.Position - first.Forward * 5f, first.Forward, 2.2f, true, true, 2.2f);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            for (int i = 0; i < Pads; i++)
                pads[i].localScale = new Vector3(1f, 1f, Mathf.Max(0.01f, EaseOutBack(Mathf.Clamp01(progress * 1.4f - i * 0.15f))));
        }

        protected override void OnActivate()
        {
            marker.Hide();
            foreach (var pad in pads) pad.localScale = Vector3.one;
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            for (int i = 0; i < Pads; i++)
            {
                foreach (var racer in Racers)
                {
                    Vector3 local = pads[i].InverseTransformPoint(racer.Car.position);
                    if (Mathf.Abs(local.x) > 2.0f || Mathf.Abs(local.z) > 3.2f) continue;
                    if (!taken[i].Add(racer.Controller)) continue;
                    racer.Controller.ApplyBoost(FairnessLimits.MaxBoostFactor);
                }
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            foreach (var pad in pads) pad.localScale = new Vector3(1f, 1f, Mathf.Max(0.01f, 1f - progress));
        }

        protected override void Cleanup()
        {
            marker.Hide();
            foreach (var pad in pads) pad.gameObject.SetActive(false);
        }
    }
}
