using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Карусель: на одной половине дороги вырастает ярмарочная вертушка с тремя мягкими руками.
    /// Руки сметают машины по касательной («ВЖУХ!»), вторая половина дороги свободна — можно объехать
    /// или проскочить между руками. Поворот — по часам события, у всех одинаково.
    public sealed class SpinnerEvent : RaceEvent
    {
        const float ModelArm = 5.1f;
        const float Sink = 3.5f;

        readonly Dictionary<CarController, float> lastHit = new Dictionary<CarController, float>();
        Transform spinnerBase;
        Transform arms;
        TelegraphMarker marker;
        float armLength;
        float scale;
        float direction;
        float lateral;
        HazardPoint hub;
        float spinUp;
        Collider post;

        void Awake()
        {
            spinnerBase = Child("Base");
            arms = Child("Arms");
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            post = spinnerBase.GetComponent<Collider>();
        }

        protected override void OnTelegraph()
        {
            lastHit.Clear();
            armLength = Mathf.Min(ModelArm, RoadHalf * 0.48f);
            scale = armLength / ModelArm;
            float side = Placement.Lateral >= 0f ? 1f : -1f;
            lateral = side * (RoadHalf - armLength - 0.3f);
            hub = At(Progress, lateral);
            direction = RandomRange(0f, 1f) < 0.5f ? -1f : 1f;
            Center = hub.Position;
            Radius = armLength + 0.4f;
            spinnerBase.gameObject.SetActive(true);
            arms.gameObject.SetActive(true);
            spinnerBase.localScale = arms.localScale = Vector3.one * scale;
            Place(1f);
            if (post != null) post.enabled = false;
            marker.Show(hub.Position, hub.Forward, Radius, false, false);
            spinUp = 0f;
        }

        void Place(float sunk)
        {
            Vector3 p = hub.Position - Vector3.up * Sink * sunk;
            spinnerBase.SetPositionAndRotation(p, Quaternion.LookRotation(hub.Forward));
            float angle = direction * EventTime * 80f * spinUp;
            arms.SetPositionAndRotation(p, Quaternion.LookRotation(hub.Forward) * Quaternion.AngleAxis(angle, Vector3.up));
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            spinUp = Mathf.Clamp01((progress - 0.3f) / 0.7f);
            Place(1f - EaseOutBack(Mathf.Clamp01(progress / 0.5f)));
        }

        protected override void OnActivate()
        {
            marker.Hide();
            spinUp = 1f;
            Place(0f);
            if (post != null) post.enabled = true;
            ChaosEffects?.Sound(SoundBank.Spring, hub.Position, 0.6f, 0.7f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            Place(0f);
            float baseAngle = arms.eulerAngles.y;
            foreach (var racer in RacersNear(hub.Position, armLength + 1.4f))
            {
                Vector3 d = racer.Car.position - hub.Position;
                d.y = 0f;
                float r = d.magnitude;
                if (r < 0.8f * scale + 0.9f) continue;
                float carAngle = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float half = Mathf.Atan2(0.35f * scale + 1.1f, r) * Mathf.Rad2Deg;
                bool hit = false;
                for (int k = 0; k < 3; k++)
                {
                    // Руки модели — под углами 0, 120, 240° от оси x (в Unity углы от +z: 90 − a).
                    float armAngle = baseAngle + 90f - k * 120f;
                    if (Mathf.Abs(Mathf.DeltaAngle(carAngle, armAngle)) < half) hit = true;
                }
                if (!hit) continue;
                lastHit.TryGetValue(racer.Controller, out float at);
                if (Time.time - at < 0.9f) continue;
                lastHit[racer.Controller] = Time.time;
                Vector3 radial = d / r;
                Vector3 tangent = Vector3.Cross(Vector3.up, radial) * -direction;
                // Карусель подхватывает и швыряет по касательной, раскручивая волчком.
                if (racer.Controller.Physics.Simulated)
                    racer.Controller.ApplyLaunch(tangent * 22f + radial * 9f + Vector3.up * 9f, Vector3.up * -direction * 11f + tangent * 3f, "карусель");
                Vector3 contact = hub.Position + radial * Mathf.Min(r, armLength) + Vector3.up * 0.8f;
                ChaosEffects?.Blast(contact, BlastStyle.Spark, 1.2f);
                ChaosEffects?.Sound(SoundBank.Boing, contact, 0.8f, 0.9f);
                ChaosEffects?.Pop(contact + Vector3.up * 1.4f, "ВЖУХ!", new Color(0.35f, 0.9f, 1f), 1.1f);
            }
        }

        protected override void OnEnd()
        {
            if (post != null) post.enabled = false;
        }

        protected override void OnEndUpdate(float progress)
        {
            spinUp = 1f - progress;
            Place(progress * progress);
        }

        protected override void Cleanup()
        {
            marker.Hide();
            if (post != null) post.enabled = false;
            spinnerBase.gameObject.SetActive(false);
            arms.gameObject.SetActive(false);
        }

        public override bool IsObstacle => true;

        public override void CollectBlocks(List<RoadBlock> list)
        {
            if (Stage != EventStage.Active) return;
            list.Add(new RoadBlock { Progress = Progress, HalfLength = armLength, LateralMin = lateral - armLength - 0.4f, LateralMax = lateral + armLength + 0.4f });
        }
    }
}
