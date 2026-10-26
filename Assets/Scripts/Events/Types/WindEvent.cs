using UnityEngine;

namespace RandomCar.Events
{
    /// Порыв ветра: у обочины поднимается ветровой конус, полосы ветра показывают направление.
    /// Порыв сдвигает машины в зоне шириной в половину дороги (мягкий импульс без урона).
    public sealed class WindEvent : RaceEvent
    {
        Transform sock;
        Transform sockCone;
        TelegraphMarker marker;
        ParticleSystem wind;
        Vector3 windDirection;
        Vector3 sockPosition;

        void Awake()
        {
            sock = Child("Windsock");
            sockCone = sock;
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            wind = Fx("FxWind");
        }

        protected override void OnTelegraph()
        {
            Radius = FairnessLimits.MaxHazardRadius;
            var h = Predict(Target, Definition.WarningSeconds, Radius);
            Center = h.Position;
            float side = Context.Random.Range(0f, 1f) < 0.5f ? -1f : 1f;
            windDirection = -side * h.Right;
            sockPosition = h.Axis + h.Right * side * 13.5f;
            sock.gameObject.SetActive(true);
            // Модель конуса смотрит по +x: поворачиваем так, чтобы он «дул» в сторону дороги.
            sock.SetPositionAndRotation(sockPosition - Vector3.up * 4.5f, Quaternion.LookRotation(Vector3.Cross(windDirection, Vector3.up)) );
            marker.Show(Center, windDirection, Radius, false, true, 1.6f);
            if (wind != null)
            {
                wind.transform.SetPositionAndRotation(sockPosition + Vector3.up * 1.5f, Quaternion.LookRotation(windDirection));
                wind.Play(true);
            }
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            float rise = EaseOutBack(Mathf.Clamp01(time / 0.5f));
            sock.position = sockPosition - Vector3.up * 4.5f * (1f - rise);
            sockCone.localScale = new Vector3(1f + Mathf.Sin(time * 12f) * 0.05f * progress, 1f, 1f);
            if (wind != null)
            {
                var emission = wind.emission;
                emission.rateOverTimeMultiplier = Mathf.Lerp(10f, 70f, progress);
            }
        }

        protected override void OnActivate()
        {
            marker.Hide();
            ShakeCameraNear(Center, 0.15f, 20f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            foreach (var racer in RacersNear(Center, Radius + 1.5f))
            {
                if (!FirstHit(racer.Controller)) continue;
                racer.Controller.ApplyImpulse(windDirection * 6f + Vector3.up * 0.6f);
            }
        }

        protected override void OnEnd()
        {
            if (wind != null) wind.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        protected override void OnEndUpdate(float progress)
        {
            sock.position = sockPosition - Vector3.up * 4.5f * progress;
        }

        protected override void Cleanup()
        {
            marker.Hide();
            Stop(wind);
            sock.gameObject.SetActive(false);
        }
    }
}
