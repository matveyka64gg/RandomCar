using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Смерч: с обочины впереди на дорогу выходит пыльная воронка и ползёт поперёк трассы навстречу машинам.
    /// Кого затянуло — того поднимает, крутит вокруг воронки и выбрасывает в сторону. Мячи, утки и бочки
    /// кружатся вместе с машинами.
    public sealed class TornadoGag : GagEvent
    {
        const float CoreRadius = 6f;
        const float Lift = 24f;
        const float Swirl = 26f;

        Transform funnel;
        ParticleSystem dust, debris;
        Vector3 start, end;
        float spin;
        readonly HashSet<CarController> grabbed = new HashSet<CarController>();

        void Awake()
        {
            funnel = Child("Funnel");
            funnel.gameObject.SetActive(false);
            dust = Fx("FxDust");
            debris = Fx("FxDebris");
        }

        protected override void OnGagTelegraph()
        {
            grabbed.Clear();
            int side = Side;
            // Вырастает в паре метров от того места, где будет машина, и ползёт поперёк дороги и навстречу гонке:
            // кто едет следом, тоже попадает в воронку.
            start = Ground(Point.Axis + Point.Right * (Point.Lateral + side * 2.5f));
            end = Ground(Point.Axis - Point.Right * side * (RoadHalf + 4f) - Point.Forward * 26f);
            funnel.position = start;
            funnel.localScale = Vector3.one * 0.05f;
            funnel.gameObject.SetActive(true);
            Play(dust, start);
            ChaosEffects?.Sound(SoundBank.Tornado, start, 0.9f, 1f);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            funnel.localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, progress);
            Turn(progress * 400f);
            if (dust != null) dust.transform.position = funnel.position;
        }

        void Turn(float degreesPerSecond)
        {
            spin += degreesPerSecond * Time.deltaTime;
            funnel.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.7f) * 4f, -spin, Mathf.Cos(Time.time * 1.3f) * 4f);
        }

        protected override void OnActivate()
        {
            Play(debris, funnel.position);
            ChaosEffects?.Pop(funnel.position + Vector3.up * 10f, "СМЕРЧ!", new Color(0.8f, 0.75f, 0.65f), 1.7f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            Turn(500f);
            Vector3 axis = Vector3.Lerp(start, end, progress) + Point.Right * Mathf.Sin(time * 2.3f) * 1.5f;
            funnel.position = axis;
            Center = axis;
            if (dust != null) dust.transform.position = axis;
            if (debris != null) debris.transform.position = axis;
            float dt = Time.deltaTime;
            foreach (var racer in SimulatedNear(axis, CoreRadius))
            {
                var car = racer.Controller;
                Vector3 d = racer.Car.position - axis;
                d.y = 0f;
                float dist = Mathf.Max(0.5f, d.magnitude);
                Vector3 radial = d / dist;
                Vector3 tangent = Vector3.Cross(Vector3.up, radial);
                float height = racer.Car.position.y - axis.y;
                float k = 1f - dist / CoreRadius * 0.5f;
                // Поднимает до ~14 м, крутит вокруг оси, у вершины — выбрасывает наружу.
                Vector3 push = tangent * Swirl * k + Vector3.up * (height < 14f ? Lift * k : 0f) + radial * (height > 10f ? 18f : -6f);
                car.ApplyPush(push, dt, "затянуло в смерч");
                if (grabbed.Add(car)) car.ApplyLaunch(Vector3.up * 6f, Vector3.up * 9f + Random.onUnitSphere * 3f, "затянуло в смерч");
            }
            foreach (var body in ChaosBody.Active)
            {
                if (body == null || !body.Live || body.Remote) continue;
                Vector3 d = body.transform.position - axis;
                d.y = 0f;
                if (d.magnitude > CoreRadius) continue;
                body.Body.AddForce((Vector3.Cross(Vector3.up, d.normalized) * 20f + Vector3.up * 16f) * dt, ForceMode.VelocityChange);
            }
        }

        protected override void OnEnd()
        {
            Stop(dust);
            Stop(debris);
        }

        protected override void OnEndUpdate(float progress)
        {
            Turn(500f * (1f - progress));
            funnel.localScale = Vector3.one * Mathf.Max(0.05f, 1f - progress);
        }

        protected override void Cleanup()
        {
            Stop(dust);
            Stop(debris);
            funnel.gameObject.SetActive(false);
        }
    }
}
