using UnityEngine;

namespace RandomCar.Events
{
    /// Каменные столбы: трещины и свечение на половине дороги (где едет цель), затем базальтовые столбы
    /// перекрывают эту половину на 3 с. Вторая половина всегда свободна. Машины внутри зоны выталкиваются заранее.
    public sealed class PillarsEvent : RaceEvent
    {
        const float Sink = 4.4f;

        Transform columns;
        Transform cracks;
        Collider blocker;
        TelegraphMarker marker;
        ParticleSystem debris;
        ParticleSystem dust;
        Vector3 right;
        float half;
        readonly System.Collections.Generic.Dictionary<RandomCar.Car.CarController, float> nextPush =
            new System.Collections.Generic.Dictionary<RandomCar.Car.CarController, float>();

        public override bool IsObstacle => true;

        void Awake()
        {
            columns = Child("Columns");
            cracks = Child("Cracks");
            blocker = columns.GetComponent<Collider>();
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            debris = Fx("FxDebris");
            dust = Fx("FxDust");
        }

        protected override void OnTelegraph()
        {
            Radius = FairnessLimits.MaxHazardRadius;
            // Столбы вырастают впереди цели, чтобы их было видно и было куда отвернуть.
            var target = Predict(Target, Definition.WarningSeconds + 0.5f, 1f);
            nextPush.Clear();
            half = target.Lateral >= 0f ? 1f : -1f;
            var h = At(target.Progress, half * FairnessLimits.RoadHalfWidth / 2f);
            Center = h.Position;
            right = h.Right;
            var rotation = Quaternion.LookRotation(h.Forward);
            columns.gameObject.SetActive(true);
            columns.SetPositionAndRotation(Center - Vector3.up * Sink, rotation);
            blocker.enabled = false;
            cracks.gameObject.SetActive(true);
            cracks.SetPositionAndRotation(Center + Vector3.up * 0.02f, rotation);
            cracks.localScale = Vector3.zero;
            marker.Show(Center, h.Forward, Radius, false, false, 0.55f);
            Play(dust, Center);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            cracks.localScale = new Vector3(Radius, 1f, Radius * 0.6f) * Mathf.Clamp01(progress * 1.5f);
            columns.position = Center - Vector3.up * (Sink - 0.25f * progress) + right * Mathf.Sin(time * 50f) * 0.02f * progress;
        }

        protected override void OnActivate()
        {
            marker.Hide();
            Stop(dust);
            // Сначала выталкиваем машины из зоны на свободную половину, потом поднимаем столбы.
            foreach (var racer in RacersNear(Center, Radius + 0.8f))
                racer.Controller.ApplyImpulse(-half * right * 7f + Vector3.up * 2f);
            Play(debris, Center);
            ShakeCameraNear(Center, 0.45f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            float rise = EaseOutBack(Mathf.Clamp01(time / 0.35f));
            columns.position = Center - Vector3.up * Sink * (1f - rise);
            if (time > 0.15f) blocker.enabled = true;

            // Кто упёрся в столбы, того мягко выталкиваем на свободную половину: никто не стоит дольше секунды.
            foreach (var racer in RacersNear(Center, Radius + 2.5f))
            {
                if (Mathf.Abs(racer.Controller.Speed) > 3f) continue;
                nextPush.TryGetValue(racer.Controller, out float at);
                if (Time.time < at) continue;
                nextPush[racer.Controller] = Time.time + 0.6f;
                racer.Controller.ApplyImpulse(-half * right * 6f + Vector3.up * 1f);
            }
        }

        protected override void OnEnd()
        {
            blocker.enabled = false;
            Play(dust, Center);
        }

        protected override void OnEndUpdate(float progress)
        {
            columns.position = Center - Vector3.up * Sink * progress;
            cracks.localScale = new Vector3(Radius, 1f, Radius * 0.6f) * (1f - progress);
        }

        protected override void Cleanup()
        {
            blocker.enabled = false;
            marker.Hide();
            Stop(dust);
            columns.gameObject.SetActive(false);
            cracks.gameObject.SetActive(false);
        }
    }
}
