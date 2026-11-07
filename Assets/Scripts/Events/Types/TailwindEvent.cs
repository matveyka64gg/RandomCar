using UnityEngine;

namespace RandomCar.Events
{
    /// Попутный ветер: на прямом участке половину дороги размечает лаймовый коридор, по нему бегут полосы ветра.
    /// Кто проедет коридор, пока дует ветер, получает рывок +12% на 2 с. Работает на всех одинаково.
    public sealed class TailwindEvent : RaceEvent
    {
        const float HalfWidth = 3f;

        TelegraphMarker marker;
        ParticleSystem wind;
        Vector3 forward;
        Vector3 right;
        float halfLength;

        void Awake()
        {
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            wind = Fx("FxWind");
        }

        protected override void OnTelegraph()
        {
            Radius = HalfWidth;
            var h = Spot(HalfWidth);
            Center = h.Position;
            forward = h.Forward;
            right = h.Right;
            halfLength = Definition.ZoneLength * 0.5f;
            marker.Show(Center, forward, HalfWidth, true, true, halfLength / HalfWidth);
            if (wind != null)
            {
                wind.transform.SetPositionAndRotation(Center - forward * (halfLength + 4f) + Vector3.up * 1.2f, Quaternion.LookRotation(forward));
                wind.Play(true);
            }
        }

        protected override void OnTelegraphUpdate(float progress, float time) => marker.Pulse(progress);

        protected override void OnActivate()
        {
            marker.Pulse(1f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            marker.Pulse(0.5f + 0.5f * Mathf.Sin(time * 3f));
            foreach (var racer in Racers)
            {
                Vector3 d = racer.Car.position - Center;
                if (Mathf.Abs(Vector3.Dot(d, forward)) > halfLength || Mathf.Abs(Vector3.Dot(d, right)) > HalfWidth + 0.8f) continue;
                if (FirstHit(racer.Controller)) racer.Controller.ApplyBoost(1.12f);
            }
        }

        protected override void OnEnd()
        {
            marker.Hide();
            Stop(wind);
        }

        protected override void Cleanup()
        {
            marker.Hide();
            Stop(wind);
        }
    }
}
