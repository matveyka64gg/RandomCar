using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Масляное пятно: на полосе разливается радужная лужа. Колёса теряют почти всё сцепление, машину
    /// разворачивает волчком — «пончики» посреди трассы, пока шины не нащупают асфальт.
    public sealed class OilGag : GagEvent
    {
        const float SlickRadius = 2.9f;

        Transform oil;
        Vector3 home;
        readonly HashSet<CarController> spun = new HashSet<CarController>();

        void Awake()
        {
            oil = Child("Oil");
            oil.gameObject.SetActive(false);
        }

        protected override void OnGagTelegraph()
        {
            spun.Clear();
            home = Ground(Point.Position);
            oil.SetPositionAndRotation(home + Vector3.up * 0.015f, Quaternion.Euler(0f, RandomRange(0f, 360f), 0f));
            oil.localScale = Vector3.one * 0.01f;
            oil.gameObject.SetActive(true);
            ChaosEffects?.Sound(SoundBank.Slurp, home, 0.6f, 0.8f);
        }

        protected override void OnTelegraphUpdate(float progress, float time) => oil.localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, Mathf.Sqrt(progress));

        protected override void OnActivate() => oil.localScale = Vector3.one;

        protected override void OnActiveUpdate(float progress, float time)
        {
            foreach (var racer in SimulatedNear(home, SlickRadius))
            {
                var car = racer.Controller;
                car.ApplySlip(1.5f, 0.12f);
                if (!spun.Add(car)) continue;
                float sign = RandomRange(0f, 1f) < 0.5f ? -1f : 1f;
                car.ApplyLaunch(Vector3.up * 1.5f, Vector3.up * sign * 7f, "масло");
                ChaosEffects?.Sound(SoundBank.Slurp, racer.Car.position, 1f, Random.Range(0.9f, 1.15f));
                ChaosEffects?.Pop(racer.Car.position + Vector3.up * 2.2f, "ВЖУУУХ!", new Color(0.55f, 0.45f, 1f), 1.2f);
                ChaosEffects?.Blast(racer.Car.position, BlastStyle.ColorPuff, 0.6f, new Color(0.2f, 0.2f, 0.25f, 0.6f));
            }
        }

        protected override void OnEndUpdate(float progress) => oil.localScale = Vector3.one * Mathf.Max(0.01f, 1f - progress);

        protected override void Cleanup() => oil.gameObject.SetActive(false);
    }
}
