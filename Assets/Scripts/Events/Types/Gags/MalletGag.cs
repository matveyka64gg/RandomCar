using RandomCar.Audio;
using UnityEngine;

namespace RandomCar.Events
{
    /// Кувалда: из-за обочины поднимается деревянная кувалда размером с дом, замахивается и бьёт по дороге
    /// туда, где окажется машина. Под самим бойком — «ПЛЮЩ!»: машина сплющена в лепёшку и вязнет;
    /// рядом — катапульта: удар подбрасывает соседние машины высоко вверх, волна раскидывает шары и поджигает мины.
    public sealed class MalletGag : GagEvent
    {
        const float Length = 8.9f;
        const float SlamSeconds = 0.17f;
        const float WindUp = -30f;
        const float FlatRadius = 3.3f;
        const float CatapultRadius = 10f;

        Transform mallet;
        TelegraphMarker marker;
        Vector3 root, impact;
        Quaternion facing;
        float scale;
        bool struck;

        void Awake()
        {
            mallet = Child("Mallet");
            mallet.gameObject.SetActive(false);
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnGagTelegraph()
        {
            struck = false;
            scale = Mathf.Clamp(RoadHalf / 6f, 1f, 1.4f);
            int side = Point.Lateral > 0.5f ? 1 : Point.Lateral < -0.5f ? -1 : Side;
            float rootLateral = Point.Lateral + side * Length * scale;
            root = Ground(Point.Axis + Point.Right * rootLateral);
            impact = Ground(Point.Position);
            facing = Quaternion.LookRotation(-Point.Forward * side, Vector3.up);
            mallet.localScale = Vector3.one * scale;
            mallet.gameObject.SetActive(true);
            Pose(WindUp, -1f);
            marker.Show(impact, Point.Forward, FlatRadius, false, false);
            ChaosEffects?.Sound(SoundBank.Whoosh, root, 0.6f, 0.6f);
        }

        void Pose(float angle, float rise)
        {
            float depth = 10f * scale;
            mallet.SetPositionAndRotation(root + Vector3.up * depth * Mathf.Min(0f, rise), facing * Quaternion.AngleAxis(-angle, Vector3.forward));
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            Pose(WindUp + Mathf.Sin(time * 18f) * 2f * progress, EaseOutBack(Mathf.Clamp01(progress * 1.5f)) - 1f);
        }

        protected override void OnActivate() => marker.Hide();

        protected override void OnActiveUpdate(float progress, float time)
        {
            float k = Mathf.Clamp01(time / SlamSeconds);
            float angle = time < SlamSeconds ? Mathf.Lerp(WindUp, 90f, k * k)
                        : time < SlamSeconds + 0.5f ? 90f - Mathf.Sin((time - SlamSeconds) * 25f) * Mathf.Exp(-(time - SlamSeconds) * 9f) * 6f
                        : Mathf.Lerp(90f, 20f, Mathf.Clamp01((time - SlamSeconds - 0.5f) / 0.8f));
            Pose(angle, 0f);
            if (struck || time < SlamSeconds) return;
            struck = true;
            Strike();
        }

        void Strike()
        {
            var fx = ChaosEffects;
            fx?.Sound(SoundBank.Thwomp, impact, 1f, Random.Range(0.9f, 1.05f));
            fx?.Blast(impact, BlastStyle.Big, 1.6f, new Color(0.8f, 0.7f, 0.55f, 0.8f));
            fx?.Scorch(impact, 5f, true);
            fx?.Pop(impact + Vector3.up * 3.5f, "ТУДУХ!", new Color(0.95f, 0.75f, 0.45f), 1.8f);
            ShakeCameraNear(impact, 0.8f, 45f);
            foreach (var racer in SimulatedNear(impact, CatapultRadius))
            {
                float d = Flat(racer.Car.position - impact);
                var car = racer.Controller;
                if (d < FlatRadius)
                {
                    // Под бойком: лепёшка.
                    car.ApplySquash(new Vector3(1.35f, 0.3f, 1.3f), 1.8f, "сплющен кувалдой");
                    car.ApplySlow(0.3f, 1.2f);
                    car.ApplyLaunch(Vector3.up * 3f, Vector3.zero, "сплющен кувалдой");
                    fx?.Pop(racer.Car.position + Vector3.up * 1.6f, "ПЛЮЩ!", new Color(1f, 0.6f, 0.6f), 1.5f);
                    continue;
                }
                // Рядом: катапульта — асфальт подбрасывает машину.
                float k = 1f - (d - FlatRadius) / (CatapultRadius - FlatRadius) * 0.6f;
                Vector3 away = FlatDir(racer.Car.position - impact, Point.Forward);
                racer.Controller.ApplyLaunch(away * 12f * k + Vector3.up * 23f * k, Random.onUnitSphere * 8f * k, "катапульта кувалды");
            }
            fx?.Shockwave(impact, CatapultRadius, 12f, 1);
        }

        protected override void OnEndUpdate(float progress) => Pose(20f, -progress);

        protected override void Cleanup()
        {
            marker.Hide();
            mallet.gameObject.SetActive(false);
        }
    }
}
