using RandomCar.Audio;
using UnityEngine;

namespace RandomCar.Events
{
    /// Вентилятор-ураган: на обочине вырастает огромный вентилятор и раскручивается. Поток воздуха поперёк дороги
    /// сдувает машины с траектории (и подкидывает лёгкие), мячи и утки улетают вместе с ними. Кто встанет боком —
    /// того уносит на другую обочину.
    public sealed class FanGag : GagEvent
    {
        const float Push = 34f;
        const float Lift = 10f;

        Transform fan, blades;
        ParticleSystem wind;
        Vector3 root, blow;
        float spin, halfWidth;

        void Awake()
        {
            fan = Child("Fan");
            blades = fan.Find("Blades");
            fan.gameObject.SetActive(false);
            wind = Fx("FxWind");
        }

        protected override void OnGagTelegraph()
        {
            spin = 0f;
            int side = Side;
            root = RoadsideAt(side, 3.5f);
            blow = -Point.Right * side;
            halfWidth = ZoneLength * 0.5f;
            fan.SetPositionAndRotation(root - Vector3.up * 8f, Quaternion.LookRotation(blow, Vector3.up));
            fan.localScale = Vector3.one * 1.3f;
            fan.gameObject.SetActive(true);
            ChaosEffects?.Sound(SoundBank.Fan, root, 0.9f, 1f);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            fan.position = root - Vector3.up * 8f * (1f - EaseOutBack(Mathf.Clamp01(progress * 1.4f)));
            Spin(Mathf.Lerp(60f, 600f, progress));
        }

        void Spin(float degreesPerSecond)
        {
            spin += degreesPerSecond * Time.deltaTime;
            if (blades != null) blades.localRotation = Quaternion.Euler(0f, 0f, spin);
        }

        protected override void OnActivate()
        {
            if (wind != null)
            {
                wind.transform.SetPositionAndRotation(root + Vector3.up * 4f, Quaternion.LookRotation(blow, Vector3.up));
                wind.Play(true);
            }
            ChaosEffects?.Pop(root + Vector3.up * 8f, "ФУУУУХ!", new Color(0.6f, 0.95f, 1f), 1.6f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            Spin(1500f);
            float dt = Time.deltaTime;
            // Поток: полоса вдоль трассы ±halfWidth, сила слабеет к дальней обочине.
            foreach (var racer in Racers)
            {
                if (racer.Finished || !Simulates(racer)) continue;
                Vector3 d = racer.Car.position - root;
                float along = Vector3.Dot(d, Point.Forward);
                float across = Vector3.Dot(d, blow);
                if (Mathf.Abs(along) > halfWidth || across < -2f || across > RoadHalf * 2f + 12f) continue;
                float k = 1f - Mathf.Clamp01(across / (RoadHalf * 2f + 12f)) * 0.5f;
                racer.Controller.ApplyPush(blow * Push * k + Vector3.up * Lift * k, dt, "сдуло вентилятором");
                // Шины в урагане почти не держат: машину сносит боком, а не «отруливает» обратно.
                racer.Controller.ApplySlip(0.3f, 0.3f);
            }
            foreach (var body in ChaosBody.Active)
            {
                if (body == null || !body.Live || body.Remote) continue;
                Vector3 d = body.transform.position - root;
                if (Mathf.Abs(Vector3.Dot(d, Point.Forward)) > halfWidth || Vector3.Dot(d, blow) < -2f) continue;
                body.Body.AddForce(blow * 25f * dt + Vector3.up * 8f * dt, ForceMode.VelocityChange);
            }
        }

        protected override void OnEnd()
        {
            if (wind != null) wind.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        protected override void OnEndUpdate(float progress)
        {
            Spin(Mathf.Lerp(1500f, 0f, progress));
            fan.position = root - Vector3.up * 8f * progress * progress;
        }

        protected override void Cleanup()
        {
            Stop(wind);
            fan.gameObject.SetActive(false);
        }
    }
}
