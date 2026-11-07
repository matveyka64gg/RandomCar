using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Мини-ворота: поперёк дороги арка с сиреневым лучом над одной половиной. Под лучом (сторона по сиду) — «МИНИ»:
    /// машина на 4 с становится игрушечной и получает небольшой рывок; под другой половиной — «ГИГАНТ»: машина
    /// раздувается (только вид, физика та же). Стойки — у обочин, дорога свободна целиком.
    public sealed class ShrinkEvent : RaceEvent
    {
        const float GateDepth = 2.2f;

        readonly Transform[] posts = new Transform[3];
        readonly Transform[] beams = new Transform[2];
        readonly HashSet<CarController> passed = new HashSet<CarController>();
        ParticleSystem rays;
        TelegraphMarker marker;
        HazardPoint gate;
        float miniSide;
        float postOut;
        Collider middle;

        void Awake()
        {
            for (int i = 0; i < 3; i++) posts[i] = Child("Post_" + i);
            for (int i = 0; i < 2; i++) beams[i] = Child("Beam_" + i);
            rays = Fx("FxRays");
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            middle = posts[1].GetComponent<Collider>();
            Show(false);
        }

        void Show(bool on)
        {
            // Средней стойки нет: она посреди дороги провоцировала столкновения.
            posts[0].gameObject.SetActive(on);
            posts[2].gameObject.SetActive(on);
            posts[1].gameObject.SetActive(false);
            foreach (var b in beams) b.gameObject.SetActive(on);
        }

        protected override void OnTelegraph()
        {
            passed.Clear();
            var map = Context.Race.Map;
            postOut = Mathf.Min(RoadHalf + 0.9f, map != null ? Mathf.Max(RoadHalf + 0.5f, map.Roadside) : RoadHalf + 0.9f);
            gate = At(Progress, 0f);
            miniSide = RandomRange(0f, 1f) < 0.5f ? -1f : 1f;
            Show(true);
            Place(1f);
            if (middle != null) middle.enabled = false;
            Center = gate.Position + gate.Right * miniSide * RoadHalf * 0.5f;
            Radius = RoadHalf * 0.4f;
            marker.Show(Center, gate.Forward, RoadHalf * 0.45f, true, true);
        }

        void Place(float sunk)
        {
            Quaternion rot = Quaternion.LookRotation(gate.Forward);
            Vector3 down = Vector3.up * 8f * sunk;
            posts[0].SetPositionAndRotation(Ground(gate.Axis - gate.Right * postOut) - down, rot);
            posts[1].SetPositionAndRotation(gate.Axis - down, rot);
            posts[2].SetPositionAndRotation(Ground(gate.Axis + gate.Right * postOut) - down, rot);
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;
                beams[i].SetPositionAndRotation(gate.Axis + gate.Right * s * postOut * 0.5f + Vector3.up * 7.3f - down, rot);
                beams[i].localScale = new Vector3(postOut, 1f, 1f);
            }
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            Place(1f - EaseOutBack(Mathf.Clamp01(progress / 0.6f)));
        }

        protected override void OnActivate()
        {
            marker.Hide();
            Place(0f);
            Play(rays, gate.Axis + gate.Right * miniSide * RoadHalf * 0.5f + Vector3.up * 6.8f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            foreach (var racer in Racers)
            {
                if (passed.Contains(racer.Controller)) continue;
                Locate(racer, out float along, out float lat);
                if (Mathf.Abs(along) > GateDepth || Mathf.Abs(lat) > RoadHalf + 0.5f) continue;
                passed.Add(racer.Controller);
                bool mini = Mathf.Sign(lat) == miniSide;
                racer.Controller.ApplyVisualScale(mini ? 0.55f : 1.32f, FairnessLimits.MaxVisualScaleSeconds);
                if (mini && racer.Controller.Physics.Simulated) racer.Controller.ApplyBoost(1.12f);
                ChaosEffects?.Sound(SoundBank.Warp, racer.Car.position, 0.8f, mini ? 1.5f : 0.6f);
                ChaosEffects?.Pop(racer.Car.position + Vector3.up * 2.5f, mini ? "МИНИ!" : "ГИГАНТ!", mini ? new Color(0.75f, 0.5f, 1f) : new Color(1f, 0.7f, 0.25f), 1.2f);
                ChaosEffects?.Blast(racer.Car.position + Vector3.up, BlastStyle.ColorPuff, 0.6f, new Color(0.75f, 0.45f, 1f, 0.6f));
            }
        }

        protected override void OnEnd()
        {
            if (middle != null) middle.enabled = false;
            Stop(rays);
        }

        protected override void OnEndUpdate(float progress) => Place(progress * progress);

        protected override void Cleanup()
        {
            marker.Hide();
            if (middle != null) middle.enabled = false;
            Stop(rays);
            Show(false);
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage != EventStage.Telegraph && Stage != EventStage.Active) return;
            list.Add(new HazardInfo { Progress = Progress, Center = gate.Axis + gate.Right * miniSide * RoadHalf * 0.5f, Radius = RoadHalf * 0.3f, HalfLength = GateDepth, Good = true, Since = StartedAt });
        }
    }
}
