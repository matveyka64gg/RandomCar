using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Гравитационный сбой: вокруг машины вспыхивает фиолетовый круг, и на пару секунд сила тяжести
    /// переворачивается — всех в круге (машины, мячи, утки) медленно уносит в небо, кузова вращаются,
    /// а потом гравитация возвращается и всё с грохотом падает обратно (отскоки, кувырки, волны).
    public sealed class GravFlipGag : GagEvent
    {
        const float Range = 15f;
        const float Flip = -0.85f;

        TelegraphMarker marker;
        ParticleSystem bubbles;
        Vector3 center;
        readonly HashSet<CarController> lifted = new HashSet<CarController>();

        void Awake()
        {
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            bubbles = Fx("FxBubbles");
        }

        protected override void OnGagTelegraph()
        {
            lifted.Clear();
            center = Ground(Point.Position);
            marker.Show(center, Point.Forward, Range, false, false);
            ChaosEffects?.Sound(SoundBank.Warp, center, 0.9f, 0.6f);
        }

        protected override void OnTelegraphUpdate(float progress, float time) => marker.Pulse(progress);

        protected override void OnActivate()
        {
            marker.Hide();
            if (bubbles != null)
            {
                var shape = bubbles.shape;
                shape.scale = new Vector3(Range * 1.4f, 2f, Range * 1.4f);
                bubbles.transform.position = center + Vector3.up;
                bubbles.Play(true);
            }
            ChaosEffects?.Pop(center + Vector3.up * 4f, "ГРАВИТАЦИЯ СБОИТ!", new Color(0.8f, 0.5f, 1f), 1.6f);
            ChaosEffects?.Flash(center + Vector3.up * 2f, new Color(0.7f, 0.4f, 1f), 6f, 0.4f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            float dt = Time.deltaTime;
            foreach (var racer in SimulatedNear(center, Range))
            {
                var car = racer.Controller;
                car.ApplyGravity(Flip, 0.15f);
                if (!lifted.Add(car)) continue;
                // Отрыв от земли — с медленным кувырком в случайную сторону.
                car.ApplyLaunch(Vector3.up * 4f, Random.onUnitSphere * 2.5f, "вверх тормашками");
            }
            foreach (var body in ChaosBody.Active)
            {
                if (body == null || !body.Live || body.Remote || Flat(body.transform.position - center) > Range) continue;
                body.Body.AddForce(Vector3.up * 9.81f * 1.85f * dt, ForceMode.VelocityChange);
            }
        }

        protected override void OnEnd()
        {
            if (bubbles != null) bubbles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            ChaosEffects?.Sound(SoundBank.Warp, center, 0.8f, 1.3f);
        }

        protected override void Cleanup()
        {
            marker.Hide();
            Stop(bubbles);
        }
    }
}
