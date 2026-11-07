using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Жвачка: на полосе перед машиной шлёпается розовая клякса. Наехал — колёса прилипают: машина вязнет
    /// и вытягивается, как резиновая, а через секунду жвачка отпускает её рогаткой — «ЧПОК!» и рывок вперёд.
    public sealed class GumGag : GagEvent
    {
        const float StickRadius = 2.8f;
        const float HoldSeconds = 0.9f;

        Transform gum, bubble;
        Vector3 home;
        readonly Dictionary<CarController, float> stuck = new Dictionary<CarController, float>();
        readonly HashSet<CarController> released = new HashSet<CarController>();

        void Awake()
        {
            gum = Child("Gum");
            bubble = gum.Find("Bubble");
            gum.gameObject.SetActive(false);
        }

        protected override void OnGagTelegraph()
        {
            stuck.Clear();
            released.Clear();
            home = Ground(Point.Position);
            gum.SetPositionAndRotation(home + Vector3.up * 0.02f, Quaternion.Euler(0f, RandomRange(0f, 360f), 0f));
            gum.localScale = Vector3.one * 0.01f;
            gum.gameObject.SetActive(true);
            ChaosEffects?.Sound(SoundBank.Splat, home, 0.8f, Random.Range(0.9f, 1.2f));
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            PopScale(gum, new Vector3(1.2f, 1f, 1.2f), progress * 1.5f);
            if (bubble != null) bubble.localScale = Vector3.one * (0.4f + 0.6f * progress);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            if (bubble != null) bubble.localScale = Vector3.one * (1f + Mathf.Sin(time * 3f) * 0.15f + progress * 0.6f);
            foreach (var racer in SimulatedNear(home, StickRadius))
            {
                var car = racer.Controller;
                if (released.Contains(car) || stuck.ContainsKey(car)) continue;
                stuck[car] = Time.time;
                car.ApplySlow(0.25f, HoldSeconds + 0.3f);
                car.ApplySquash(new Vector3(0.85f, 0.8f, 1.5f), HoldSeconds, "прилип к жвачке");
                ChaosEffects?.Sound(SoundBank.Splat, racer.Car.position, 1f, 0.8f);
                ChaosEffects?.Pop(racer.Car.position + Vector3.up * 2.4f, "ПРИЛИП!", new Color(1f, 0.5f, 0.75f), 1.2f);
            }
            // Рогатка: прилипшая машина через секунду отрывается с рывком вперёд.
            foreach (var pair in stuck)
            {
                if (released.Contains(pair.Key) || Time.time - pair.Value < HoldSeconds) continue;
                released.Add(pair.Key);
                var t = pair.Key.transform;
                pair.Key.ApplyLaunch(t.forward * 18f + Vector3.up * 6f, t.right * -3f, "рогатка из жвачки");
                pair.Key.ApplyBoost(1.5f);
                ChaosEffects?.Sound(SoundBank.Coil, t.position, 0.9f, 1.4f);
                ChaosEffects?.Pop(t.position + Vector3.up * 2.4f, "ЧПОК!", new Color(1f, 0.45f, 0.7f), 1.5f);
                ChaosEffects?.Blast(t.position, BlastStyle.ColorPuff, 0.8f, new Color(1f, 0.55f, 0.75f, 0.8f));
            }
        }

        protected override void OnEnd()
        {
            // Пузырь лопается.
            if (bubble != null) ChaosEffects?.Blast(bubble.position, BlastStyle.Fake, 0.5f, new Color(1f, 0.55f, 0.75f, 0.85f));
            ChaosEffects?.Sound(SoundBank.MiniPop, home, 0.6f, 1.4f);
        }

        protected override void OnEndUpdate(float progress) => gum.localScale = new Vector3(1.2f, 1f, 1.2f) * Mathf.Max(0.01f, 1f - progress);

        protected override void Cleanup() => gum.gameObject.SetActive(false);
    }
}
