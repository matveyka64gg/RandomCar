using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// НЛО: с горизонта прилетает летающая тарелка с зелёным пилотом, зависает над машиной и включает луч.
    /// Машину (и всех, кто рядом) затягивает вверх, она медленно вращается в луче… а потом пришельцы
    /// передумывают — луч гаснет, «ВЕРНИТЕ!», и машина падает с высоты на дорогу.
    public sealed class UfoGag : GagEvent
    {
        const float Altitude = 14f;
        const float LiftHeight = 8f;
        const float BeamRadius = 4.2f;
        const float DropAt = 2.7f;

        Transform ufo, beam;
        Vector3 hover, from;
        bool dropped;
        readonly HashSet<CarController> caught = new HashSet<CarController>();

        void Awake()
        {
            ufo = Child("Ufo");
            beam = Child("Beam");
            ufo.gameObject.SetActive(false);
            beam.gameObject.SetActive(false);
        }

        protected override void OnGagTelegraph()
        {
            caught.Clear();
            dropped = false;
            hover = Ground(Point.Position) + Vector3.up * Altitude;
            from = hover + Point.Forward * 90f + Point.Right * Side * 40f + Vector3.up * 30f;
            ufo.SetPositionAndRotation(from, Quaternion.identity);
            ufo.localScale = Vector3.one;
            ufo.gameObject.SetActive(true);
            ChaosEffects?.Sound(SoundBank.Ufo, hover, 0.9f, 1f);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            hover = HoverPoint(hover);
            float k = 1f - (1f - progress) * (1f - progress);
            ufo.position = Vector3.Lerp(from, hover, k) + Vector3.up * Mathf.Sin(time * 6f) * 0.4f;
            ufo.rotation = Quaternion.Euler(Mathf.Sin(time * 3f) * 8f, time * 140f, 0f);
        }

        /// Над машиной, ради которой прилетели (догоняет её, если она едет), иначе — над местом.
        Vector3 HoverPoint(Vector3 current)
        {
            var target = Target;
            if (target == null) return current;
            Vector3 want = new Vector3(target.Car.position.x, Ground(target.Car.position).y + Altitude, target.Car.position.z);
            return Vector3.Lerp(current, want, 1f - Mathf.Exp(-4f * Time.deltaTime));
        }

        protected override void OnActivate()
        {
            beam.gameObject.SetActive(true);
            ChaosEffects?.Pop(ufo.position - Vector3.up * 3f, "ВЖЖЖ… ЗАБИРАЕМ!", new Color(0.5f, 1f, 0.4f), 1.4f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            hover = HoverPoint(hover);
            // Тарелка медленно ползёт вперёд по трассе вместе с добычей.
            hover += Point.Forward * 4f * Time.deltaTime;
            ufo.position = hover + Vector3.up * Mathf.Sin(time * 5f) * 0.3f;
            ufo.rotation = Quaternion.Euler(Mathf.Sin(time * 2f) * 4f, time * 200f, 0f);
            if (!dropped && time >= DropAt)
            {
                dropped = true;
                beam.gameObject.SetActive(false);
                ChaosEffects?.Sound(SoundBank.Zap, ufo.position, 0.9f, 0.7f);
                ChaosEffects?.Pop(ufo.position - Vector3.up * 2f, "НЕ ТО! ВЕРНИТЕ!", new Color(0.5f, 1f, 0.4f), 1.5f);
                foreach (var car in caught) car.ApplyLaunch(Vector3.up * 2f, Random.onUnitSphere * 3f, "уронили пришельцы");
            }
            if (dropped) return;
            beam.SetPositionAndRotation(ufo.position - Vector3.up * 0.5f, Quaternion.identity);
            float dt = Time.deltaTime;
            Vector3 ground = Ground(hover - Vector3.up * Altitude);
            foreach (var racer in SimulatedNear(ufo.position, BeamRadius))
            {
                var car = racer.Controller;
                if (caught.Add(car)) ChaosEffects?.Sound(SoundBank.Warp, racer.Car.position, 0.7f, 1.6f);
                // Подъём к точке под тарелкой: пружина с затуханием, гравитация выключена, медленное вращение.
                var body = car.Physics.Body;
                Vector3 target = new Vector3(ufo.position.x, ground.y + LiftHeight, ufo.position.z);
                Vector3 pull = (target - racer.Car.position) * 6f - body.linearVelocity * 2.5f;
                car.ApplyGravity(0f, 0.15f);
                car.ApplyPush(Vector3.ClampMagnitude(pull, 40f), dt, "похищен НЛО");
                if (!car.Invulnerable) body.AddTorque(Vector3.up * 3f * dt, ForceMode.VelocityChange);
            }
        }

        protected override void OnEnd()
        {
            beam.gameObject.SetActive(false);
            from = ufo.position;
        }

        protected override void OnEndUpdate(float progress) => ufo.position = from + (Vector3.up * 40f + Point.Forward * 60f) * progress * progress;

        protected override void Cleanup()
        {
            ufo.gameObject.SetActive(false);
            beam.gameObject.SetActive(false);
        }
    }
}
