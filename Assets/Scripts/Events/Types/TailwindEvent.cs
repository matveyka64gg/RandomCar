using UnityEngine;

namespace RandomCar.Events
{
    /// Попутный ветер: лаймовое кольцо и полосы ветра вокруг цели 1.5 с, затем буст +12% на 2 с.
    public sealed class TailwindEvent : RaceEvent
    {
        TelegraphMarker marker;
        ParticleSystem wind;

        void Awake()
        {
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            wind = Fx("FxWind");
        }

        void Follow()
        {
            Center = Target.Car.position;
            if (wind != null)
                wind.transform.SetPositionAndRotation(Target.Car.position - Target.Car.forward * 6f + Vector3.up * 1.2f, Target.Car.rotation);
        }

        protected override void OnTelegraph()
        {
            Radius = 2.2f;
            Follow();
            marker.Show(Center, Target.Car.forward, 2.4f, true, true, 1.6f);
            if (wind != null) wind.Play(true);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            Follow();
            marker.transform.SetPositionAndRotation(Center + Vector3.up * 0.03f, Quaternion.LookRotation(Target.Car.forward));
            marker.Pulse(progress);
        }

        protected override void OnActivate()
        {
            marker.Hide();
            Target.Controller.ApplyBoost(1.12f);
        }

        protected override void OnActiveUpdate(float progress, float time) => Follow();

        protected override void OnEnd() => Stop(wind);

        protected override void Cleanup()
        {
            marker.Hide();
            Stop(wind);
        }
    }
}
