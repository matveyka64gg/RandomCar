using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Пружина: прямо под колёсами вылезает люк со знаком «?», дрожит — и выстреливает гигантской пружиной.
    /// Машину подбрасывает на десятки метров, часто с сальто назад. Пружина ещё долго качается «бойнь-бойнь».
    public sealed class SpringGag : GagEvent
    {
        const float HitRadius = 3.1f;
        const float Reach = 3.4f;

        Transform root, coil, top;
        readonly HashSet<CarController> launched = new HashSet<CarController>();
        Vector3 basePosition;

        void Awake()
        {
            root = Child("Spring");
            coil = root.Find("Coil");
            top = root.Find("Top");
            root.gameObject.SetActive(false);
        }

        protected override void OnGagTelegraph()
        {
            launched.Clear();
            basePosition = Ground(Point.Position);
            root.SetPositionAndRotation(basePosition, Quaternion.LookRotation(Point.Forward));
            root.gameObject.SetActive(true);
            root.localScale = Vector3.one * 0.01f;
            SetExtension(0f);
            ChaosEffects?.Sound(SoundBank.MineBeep, basePosition, 0.6f, 0.7f);
        }

        void SetExtension(float k)
        {
            coil.localScale = new Vector3(1f, Mathf.Max(0.12f, k * Reach), 1f);
            top.localPosition = new Vector3(0f, 0.12f + Mathf.Max(0.12f, k * Reach) * 0.97f, 0f);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            // Люк выскакивает и трясётся всё сильнее: «сейчас что-то будет».
            PopScale(root, Vector3.one, progress * 2.5f);
            float shake = progress * progress;
            top.localRotation = Quaternion.Euler(Mathf.Sin(time * 47f) * 9f * shake, 0f, Mathf.Sin(time * 39f) * 9f * shake);
        }

        protected override void OnActivate()
        {
            top.localRotation = Quaternion.identity;
            ChaosEffects?.Sound(SoundBank.Coil, basePosition, 1f, Random.Range(0.9f, 1.1f));
            ChaosEffects?.Pop(basePosition + Vector3.up * 4.5f, "БОЙ-ОЙ-ОЙНЬ!", new Color(1f, 0.85f, 0.2f), 1.5f);
            ChaosEffects?.Blast(basePosition + Vector3.up * 0.5f, BlastStyle.Spark, 1f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            // Выстрел за 0.12 с, затем пружина качается с затуханием.
            float k = time < 0.12f ? EaseOutBack(time / 0.12f) : 1f + Mathf.Sin((time - 0.12f) * 18f) * Mathf.Exp(-(time - 0.12f) * 3f) * 0.25f;
            SetExtension(k);
            if (time > 0.4f) return;
            foreach (var racer in SimulatedNear(basePosition, HitRadius))
            {
                if (racer.Car.position.y > basePosition.y + Reach + 1.5f || !launched.Add(racer.Controller)) continue;
                var car = racer.Car;
                bool backflip = RandomRange(0f, 1f) < 0.6f;
                racer.Controller.ApplyLaunch(car.forward * 6f + Vector3.up * 27f,
                    backflip ? car.right * -6.5f : car.forward * Random.Range(-7f, 7f), backflip ? "сальто с пружины" : "пружина");
                ShakeCameraNear(basePosition, 0.5f, 25f);
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            SetExtension(Mathf.Lerp(1f, 0f, progress));
            root.localScale = Vector3.one * Mathf.Max(0.01f, 1f - progress * progress);
        }

        protected override void Cleanup() => root.gameObject.SetActive(false);
    }
}
