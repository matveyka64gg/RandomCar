using UnityEngine;

namespace RandomCar.Events
{
    /// Черви: холмики и пыль на полосе цели, затем три червя вылезают из земли.
    /// Касание подбрасывает и замедляет (не дольше 1.5 с).
    public sealed class WormsEvent : RaceEvent
    {
        const int Count = 3;
        const float Depth = 2.4f;

        readonly Transform[] worms = new Transform[Count];
        readonly Transform[] mounds = new Transform[Count];
        readonly Vector3[] spots = new Vector3[Count];
        readonly float[] yaw = new float[Count];
        TelegraphMarker marker;
        ParticleSystem dust;
        ParticleSystem debris;

        void Awake()
        {
            for (int i = 0; i < Count; i++)
            {
                worms[i] = Child("Worm_" + i);
                mounds[i] = Child("Mound_" + i);
            }
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            dust = Fx("FxDust");
            debris = Fx("FxDebris");
        }

        protected override void OnTelegraph()
        {
            Radius = 2.6f;
            // Чуть впереди цели: черви вылезают у неё перед носом, а не под колёсами.
            var h = Predict(Target, Definition.WarningSeconds + 0.4f, Radius);
            Center = h.Position;
            spots[0] = h.Position - h.Right * 1.3f + h.Forward * 1.5f;
            spots[1] = h.Position - h.Forward * 1.7f;
            spots[2] = h.Position + h.Right * 1.3f + h.Forward * 0.5f;
            for (int i = 0; i < Count; i++)
            {
                yaw[i] = Context.Random.Range(0f, 360f);
                mounds[i].gameObject.SetActive(true);
                mounds[i].SetPositionAndRotation(spots[i], Quaternion.Euler(0f, yaw[i], 0f));
                mounds[i].localScale = Vector3.zero;
                worms[i].gameObject.SetActive(true);
                worms[i].SetPositionAndRotation(spots[i] - Vector3.up * Depth, Quaternion.Euler(0f, yaw[i], 0f));
            }
            marker.Show(h.Position, h.Forward, Radius, false, false);
            Play(dust, h.Position);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            float s = EaseOutBack(Mathf.Clamp01(progress * 2.5f)) * 1.25f;
            for (int i = 0; i < Count; i++)
            {
                float shake = Mathf.Sin(time * 40f + i) * 0.04f * progress;
                mounds[i].localScale = new Vector3(s, s * (1f + shake), s);
            }
        }

        protected override void OnActivate()
        {
            marker.Hide();
            Stop(dust);
            Play(debris, Center);
            ShakeCameraNear(Center, 0.25f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            float rise = EaseOutBack(Mathf.Clamp01(time / 0.35f));
            for (int i = 0; i < Count; i++)
            {
                worms[i].SetPositionAndRotation(
                    spots[i] + Vector3.up * (-Depth + Depth * rise),
                    Quaternion.Euler(Mathf.Sin(time * 6f + i) * 12f, yaw[i] + Mathf.Sin(time * 3f + i) * 25f, Mathf.Cos(time * 5f + i) * 10f));
                if (rise < 0.6f) continue;
                foreach (var racer in RacersNear(spots[i], 1.6f))
                {
                    if (!FirstHit(racer.Controller)) continue;
                    Vector3 away = racer.Car.position - spots[i];
                    away.y = 0f;
                    racer.Controller.ApplySlow(0.5f, 1.5f);
                    racer.Controller.ApplyImpulse(away.normalized * 3f + Vector3.up * 4.5f);
                }
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            for (int i = 0; i < Count; i++)
            {
                worms[i].position = spots[i] - Vector3.up * Depth * progress;
                mounds[i].localScale = Vector3.one * 1.25f * (1f - progress);
            }
        }

        protected override void Cleanup()
        {
            marker.Hide();
            Stop(dust);
            for (int i = 0; i < Count; i++)
            {
                worms[i].gameObject.SetActive(false);
                mounds[i].gameObject.SetActive(false);
            }
        }
    }
}
