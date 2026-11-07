using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Гигантская рука: с обочины рядом с машиной вырастает огромная белая перчатка, замахивается
    /// и хлопает поперёк дороги. Кого задела — «ШЛЁП!»: машина улетает через всю дорогу, кувыркаясь «бочкой».
    public sealed class HandGag : GagEvent
    {
        const float SwingSeconds = 0.28f;
        const float WindUp = -28f;
        const float Slam = 88f;

        Transform hand;
        Vector3 root;
        Quaternion facing;
        float scale;
        bool slapped;
        readonly HashSet<CarController> hit = new HashSet<CarController>();

        void Awake()
        {
            hand = Child("Hand");
            hand.gameObject.SetActive(false);
        }

        protected override void OnGagTelegraph()
        {
            hit.Clear();
            slapped = false;
            // Рука вырастает на той обочине, к которой машина ближе: хлопок через её полосу.
            int side = Point.Lateral > 1f ? 1 : Point.Lateral < -1f ? -1 : Side;
            scale = Mathf.Clamp(RoadHalf / 5.5f, 1.1f, 1.8f);
            root = RoadsideAt(side, 1.6f);
            // Ладонь (+x модели) смотрит на дорогу; хлопок — поворот вокруг оси вдоль трассы.
            facing = Quaternion.LookRotation(-Point.Forward * side, Vector3.up);
            hand.localScale = Vector3.one * scale;
            hand.gameObject.SetActive(true);
            Pose(-14f, -1f);
            ChaosEffects?.Blast(root, BlastStyle.ColorPuff, 1.2f, new Color(0.8f, 0.72f, 0.6f, 0.7f));
            ChaosEffects?.Sound(SoundBank.Whoosh, root, 0.7f, 0.7f);
        }

        /// angle — наклон к дороге (минус — замах назад), rise — 0 под землёй, 1 на месте.
        void Pose(float angle, float rise)
        {
            float depth = 7.5f * scale;
            hand.SetPositionAndRotation(root + Vector3.up * depth * Mathf.Min(0f, rise), facing * Quaternion.AngleAxis(-angle, Vector3.forward));
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            float rise = EaseOutBack(Mathf.Clamp01(progress * 1.6f)) - 1f;
            float wind = Mathf.Lerp(-10f, WindUp, Mathf.Clamp01((progress - 0.5f) * 2f)) + Mathf.Sin(time * 25f) * 3f * progress;
            Pose(wind, rise);
        }

        protected override void OnActivate() => ChaosEffects?.Sound(SoundBank.Whoosh, root, 1f, 1.3f);

        protected override void OnActiveUpdate(float progress, float time)
        {
            float k = Mathf.Clamp01(time / SwingSeconds);
            float angle = Mathf.Lerp(WindUp, Slam, k * k);
            if (time > SwingSeconds + 0.35f) angle = Slam - (time - SwingSeconds - 0.35f) * 60f;
            Pose(angle, 0f);
            if (k < 0.45f || time > SwingSeconds + 0.1f) return;
            // Ладонь проходит над дорогой: всех на полосе у руки сносит через дорогу.
            Vector3 across = facing * Vector3.right;
            float reach = 7.2f * scale;
            foreach (var racer in SimulatedNear(root, reach + 1.5f))
            {
                Vector3 d = racer.Car.position - root;
                float along = Mathf.Abs(Vector3.Dot(d, Point.Forward));
                if (along > 3.2f * scale || !hit.Add(racer.Controller)) continue;
                var car = racer.Car;
                float roll = Vector3.Dot(car.right, across) >= 0f ? 1f : -1f;
                racer.Controller.ApplyLaunch(across * 28f + Point.Forward * 3f + Vector3.up * 11f, car.forward * -roll * 10f, "шлепок гигантской руки");
                ChaosEffects?.Pop(car.position + Vector3.up * 2.5f, "ШЛЁП!", new Color(1f, 0.95f, 0.85f), 1.7f);
            }
            if (slapped) return;
            slapped = true;
            Vector3 palm = root + across * reach * 0.6f;
            ChaosEffects?.Sound(SoundBank.Slap, palm, 1f, Random.Range(0.9f, 1.1f));
            ChaosEffects?.Blast(palm, BlastStyle.ColorPuff, 1.5f, new Color(0.85f, 0.78f, 0.65f, 0.75f));
            ShakeCameraNear(palm, 0.55f, 35f);
        }

        protected override void OnEndUpdate(float progress) => Pose(Mathf.Lerp(40f, 0f, progress), -progress);

        protected override void Cleanup() => hand.gameObject.SetActive(false);
    }
}
