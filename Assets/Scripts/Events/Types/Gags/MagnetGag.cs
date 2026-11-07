using RandomCar.Audio;
using UnityEngine;

namespace RandomCar.Events
{
    /// Магнит: с неба опускается гигантская красная подкова и гудит. Машины рядом тянет вверх и к магниту —
    /// они повисают в воздухе, стукаются друг о друга, а через пару секунд магнит «отпускает» («ДЗЗЗТ!»)
    /// и разбрасывает их в разные стороны. Стальные шары и бочки тоже липнут.
    public sealed class MagnetGag : GagEvent
    {
        const float Hover = 10f;
        const float Range = 17f;
        const float PullAccel = 34f;
        const float ReleaseAt = 2.4f;

        Transform magnet;
        Vector3 anchor;
        bool released;
        float sway;

        void Awake()
        {
            magnet = Child("Magnet");
            magnet.gameObject.SetActive(false);
        }

        protected override void OnGagTelegraph()
        {
            released = false;
            sway = RandomRange(0f, 10f);
            anchor = Ground(Point.Position) + Vector3.up * Hover;
            magnet.SetPositionAndRotation(anchor + Vector3.up * 30f, Quaternion.LookRotation(Point.Right, Vector3.up));
            magnet.localScale = Vector3.one * 1.4f;
            magnet.gameObject.SetActive(true);
            ChaosEffects?.Sound(SoundBank.Magnet, anchor, 0.9f, 1f);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            magnet.position = anchor + Vector3.up * 30f * (1f - EaseOutBack(progress));
            magnet.rotation = Quaternion.LookRotation(Point.Right, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Sin(time * 4f + sway) * 10f);
        }

        /// Точка притяжения — между концами подковы.
        Vector3 Pole => magnet.position - Vector3.up * 3.6f;

        protected override void OnActiveUpdate(float progress, float time)
        {
            magnet.rotation = Quaternion.LookRotation(Point.Right, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Sin(time * 3f + sway) * 6f);
            magnet.position = anchor + Vector3.up * Mathf.Sin(time * 5f) * 0.3f;
            float dt = Time.deltaTime;
            if (!released && time >= ReleaseAt)
            {
                released = true;
                Release();
                return;
            }
            if (released) return;
            Vector3 pole = Pole;
            foreach (var racer in SimulatedNear(pole, Range))
            {
                Vector3 d = pole - racer.Car.position;
                float dist = d.magnitude;
                float k = 1f - Mathf.Clamp01(dist / Range);
                // Тянет к полюсу, у самого магнита — гасит скорость (машина «прилипла» и болтается).
                Vector3 pull = d / Mathf.Max(0.5f, dist) * PullAccel * (0.35f + k) + Vector3.up * 9.81f * k;
                var body = racer.Controller.Physics.Body;
                if (dist < 3f) pull -= body.linearVelocity * 3f;
                racer.Controller.ApplyPush(pull, dt, "притянут магнитом");
            }
            foreach (var body in ChaosBody.Active)
            {
                if (body == null || !body.Live || body.Remote || (body.Kind != BodyKind.Metal && body.Kind != BodyKind.Cannonball && body.Kind != BodyKind.Barrel)) continue;
                Vector3 d = pole - body.transform.position;
                if (d.magnitude > Range) continue;
                body.Body.AddForce(d.normalized * 30f * dt + Vector3.up * 9.81f * dt, ForceMode.VelocityChange);
            }
        }

        void Release()
        {
            Vector3 pole = Pole;
            ChaosEffects?.Sound(SoundBank.Zap, pole, 1f, 0.9f);
            ChaosEffects?.Blast(pole, BlastStyle.Spark, 1.6f);
            ChaosEffects?.Flash(pole, new Color(0.6f, 0.8f, 1f), 7f, 0.2f);
            ChaosEffects?.Pop(pole + Vector3.up * 1.5f, "ДЗЗЗТ! ОТПУСТИЛ!", new Color(0.6f, 0.85f, 1f), 1.5f);
            foreach (var racer in SimulatedNear(pole, 9f))
            {
                Vector3 away = FlatDir(racer.Car.position - pole, Random.insideUnitSphere);
                racer.Controller.ApplyLaunch(away * 16f + Vector3.up * 6f, Random.onUnitSphere * 7f, "отброшен магнитом");
            }
        }

        protected override void OnEndUpdate(float progress) => magnet.position = anchor + Vector3.up * 30f * progress * progress;

        protected override void Cleanup() => magnet.gameObject.SetActive(false);
    }
}
