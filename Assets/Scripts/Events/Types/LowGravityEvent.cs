using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Невесомость: на прямой встают две светящиеся арки — вход и выход. Между ними сила тяжести ~38%:
    /// на входе машину мягко подбрасывает, и она «плывёт» по участку длинным лунным прыжком, в воздухе
    /// парят пузыри. С батутами и трамплином прыжки становятся огромными. Сцена на целый участок.
    public sealed class LowGravityEvent : RaceEvent
    {
        const float Gravity = 0.38f;

        readonly Transform[] posts = new Transform[4];
        readonly Transform[] beams = new Transform[2];
        readonly HashSet<CarController> inside = new HashSet<CarController>();
        ParticleSystem bubbles;
        TelegraphMarker marker;
        HazardPoint entry, exit;
        float postOut;

        void Awake()
        {
            for (int i = 0; i < 4; i++) posts[i] = Child("Post_" + i);
            for (int i = 0; i < 2; i++) beams[i] = Child("Beam_" + i);
            bubbles = Fx("FxBubbles");
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            Show(false);
        }

        void Show(bool on)
        {
            foreach (var p in posts) p.gameObject.SetActive(on);
            foreach (var b in beams) b.gameObject.SetActive(on);
        }

        protected override void OnTelegraph()
        {
            inside.Clear();
            float length = Context.Track.Length;
            var map = Context.Race.Map;
            postOut = Mathf.Min(RoadHalf + 1f, map != null ? Mathf.Max(RoadHalf + 0.6f, map.Roadside) : RoadHalf + 1f);
            entry = At(Progress - ZoneLength * 0.5f / length, 0f);
            exit = At(Progress + ZoneLength * 0.5f / length, 0f);
            Show(true);
            Gate(0, entry, 1f);
            Gate(1, exit, 1f);
            var c = At(Progress, 0f);
            Center = c.Position;
            Radius = RoadHalf;
            marker.Show(c.Position, c.Forward, RoadHalf * 0.9f, true, true, ZoneLength * 0.5f / (RoadHalf * 0.9f));
        }

        void Gate(int g, HazardPoint h, float sunk)
        {
            Quaternion rot = Quaternion.LookRotation(h.Forward);
            Vector3 down = Vector3.up * 8f * sunk;
            posts[g * 2].SetPositionAndRotation(Ground(h.Axis - h.Right * postOut) - down, rot);
            posts[g * 2 + 1].SetPositionAndRotation(Ground(h.Axis + h.Right * postOut) - down, rot);
            beams[g].SetPositionAndRotation(h.Axis + Vector3.up * 7.3f - down, rot);
            beams[g].localScale = new Vector3(postOut * 2f, 1f, 1f);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            float rise = 1f - EaseOutBack(Mathf.Clamp01(progress / 0.6f));
            Gate(0, entry, rise);
            Gate(1, exit, rise);
        }

        protected override void OnActivate()
        {
            marker.Hide();
            Gate(0, entry, 0f);
            Gate(1, exit, 0f);
            Play(bubbles, Center + Vector3.up * 2f);
            if (bubbles != null) bubbles.transform.rotation = Quaternion.LookRotation(entry.Forward);
            ChaosEffects?.Sound(SoundBank.Warp, Center, 0.7f, 0.8f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            foreach (var racer in Racers)
            {
                bool now = InZone(racer, ZoneLength * 0.5f, -RoadHalf - 3f, RoadHalf + 3f);
                if (!now)
                {
                    inside.Remove(racer.Controller);
                    continue;
                }
                racer.Controller.ApplyLowGravity(ChaosPower.Current.Wild ? 0.25f : Gravity);
                if (!inside.Add(racer.Controller)) continue;
                // Вход: мягкий подброс — лунный прыжок через участок.
                if (racer.Controller.Physics.Simulated) racer.Controller.ApplyLaunch(Vector3.up * 10f, Random.onUnitSphere * 3f, "невесомость");
                ChaosEffects?.Sound(SoundBank.Warp, racer.Car.position, 0.6f, 1.2f);
                ChaosEffects?.Pop(racer.Car.position + Vector3.up * 2.5f, "УИИИ!", new Color(0.8f, 0.55f, 1f), 1.2f);
            }
        }

        protected override void OnEnd() => Stop(bubbles);

        protected override void OnEndUpdate(float progress)
        {
            Gate(0, entry, progress * progress);
            Gate(1, exit, progress * progress);
        }

        protected override void Cleanup()
        {
            marker.Hide();
            Stop(bubbles);
            Show(false);
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage != EventStage.Telegraph && Stage != EventStage.Active) return;
            list.Add(new HazardInfo { Progress = Progress, Center = Center, Radius = RoadHalf * 0.5f, HalfLength = ZoneLength * 0.5f, Good = true, Since = StartedAt });
        }
    }
}
