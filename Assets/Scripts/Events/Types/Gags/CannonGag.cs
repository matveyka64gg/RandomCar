using RandomCar.Audio;
using UnityEngine;

namespace RandomCar.Events
{
    /// Пушка: на обочине выкатывается старинная пушка, наводится туда, где окажется машина, фитиль шипит —
    /// «БДЫЩ!» — ядро летит поперёк дороги. Кого задело, того сносит (ядро тяжёлое и быстрое), а ядро
    /// катится дальше, расталкивая мячи и бочки.
    public sealed class CannonGag : GagEvent
    {
        const float Speed = 36f;

        Transform cannon, barrel;
        ChaosBody ball;
        Vector3 root, aim;
        bool fired;
        float fuseAt;

        void Awake()
        {
            cannon = Child("Cannon");
            barrel = cannon.Find("Barrel");
            cannon.gameObject.SetActive(false);
            ball = Child("Ball_0").GetComponent<ChaosBody>();
            ball.gameObject.SetActive(false);
        }

        protected override void OnGagTelegraph()
        {
            fired = false;
            int side = Side;
            root = RoadsideAt(side, 3f);
            // Наводка: в точку, где будет машина, чуть выше дороги.
            Vector3 target = Ground(Point.Position) + Vector3.up * 0.9f;
            aim = target - (root + Vector3.up * 1.45f * 1.4f);
            aim.y = Mathf.Max(aim.y, -0.5f);
            aim.Normalize();
            cannon.SetPositionAndRotation(root - Vector3.up * 4f, Quaternion.LookRotation(new Vector3(aim.x, 0f, aim.z), Vector3.up));
            cannon.localScale = Vector3.one * 1.4f;
            cannon.gameObject.SetActive(true);
            fuseAt = 0f;
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            cannon.position = root - Vector3.up * 4f * (1f - EaseOutBack(Mathf.Clamp01(progress * 1.8f)));
            // Ствол поднимается к цели и трясётся от нетерпения; фитиль искрит.
            float pitch = -Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (barrel != null) barrel.localRotation = Quaternion.Euler(Mathf.Lerp(-25f, pitch, progress) + Mathf.Sin(time * 50f) * progress * 1.5f, 0f, 0f);
            if (Time.time >= fuseAt && progress > 0.3f)
            {
                fuseAt = Time.time + 0.15f;
                ChaosEffects?.Blast(Breech, BlastStyle.Spark, 0.25f);
                ChaosEffects?.Sound(SoundBank.Crackle, Breech, 0.25f, 2f);
            }
        }

        Vector3 Muzzle => barrel != null ? barrel.TransformPoint(new Vector3(0f, 0f, 2.6f)) : root + Vector3.up * 2f;
        Vector3 Breech => barrel != null ? barrel.TransformPoint(new Vector3(0f, 0.4f, -1.4f)) : root + Vector3.up * 2f;

        protected override void OnActivate()
        {
            fired = true;
            Vector3 muzzle = Muzzle;
            Vector3 dir = barrel != null ? barrel.forward : aim;
            ball.NetId = BodyId(0);
            ball.Spawn(Context.Track, RoadHalf, muzzle + dir * 0.8f, Quaternion.identity, dir * Speed + Vector3.up * 1.5f, Vector3.Cross(Vector3.up, dir) * 20f, !Authority);
            var fx = ChaosEffects;
            fx?.Sound(SoundBank.Cannon, muzzle, 1f, Random.Range(0.9f, 1.05f));
            fx?.Blast(muzzle, BlastStyle.Big, 1.1f, new Color(0.85f, 0.85f, 0.85f, 0.85f));
            fx?.Flash(muzzle, new Color(1f, 0.7f, 0.3f), 7f, 0.15f);
            fx?.Pop(muzzle + Vector3.up * 2.5f, "БДЫЩ!", new Color(1f, 0.6f, 0.3f), 1.6f);
            ShakeCameraNear(muzzle, 0.5f, 40f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            // Отдача: пушка отскакивает назад и возвращается.
            float recoil = time < 0.6f ? Mathf.Sin(time / 0.6f * Mathf.PI) * 1.2f : 0f;
            cannon.position = root - (barrel != null ? barrel.forward : aim) * recoil;
            if (fired && ball.Live && ball.Lost)
            {
                ChaosEffects?.Blast(ball.transform.position, BlastStyle.ColorPuff, 0.8f, new Color(0.3f, 0.3f, 0.3f, 0.8f));
                ball.Despawn();
            }
        }

        protected override void OnEnd()
        {
            if (ball.Live) ChaosEffects?.Blast(ball.transform.position, BlastStyle.ColorPuff, 0.9f, new Color(0.3f, 0.3f, 0.3f, 0.8f));
        }

        protected override void OnEndUpdate(float progress)
        {
            cannon.position = root - Vector3.up * 4f * progress;
            if (ball.Live) ball.transform.localScale = Vector3.one * Mathf.Max(0.05f, 1f - progress);
        }

        protected override void Cleanup()
        {
            cannon.gameObject.SetActive(false);
            if (ball.Live) ball.Despawn();
            ball.transform.localScale = Vector3.one;
        }
    }
}
