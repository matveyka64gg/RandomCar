using UnityEngine;

namespace RandomCar.Events
{
    /// Стадо овец: овцы собираются у обочины (2.5 с), затем компактной группой перебегают дорогу за 3 с.
    /// Касание замедляет на 1 с, овца отскакивает.
    public sealed class SheepEvent : RaceEvent
    {
        const int Count = 6;
        const float SideDistance = 9.5f;

        readonly Transform[] sheep = new Transform[Count];
        readonly Vector3[] lanes = new Vector3[Count];
        readonly float[] hop = new float[Count];
        TelegraphMarker marker;
        ParticleSystem dust;
        Vector3 right;
        Vector3 axis;
        float side;

        void Awake()
        {
            for (int i = 0; i < Count; i++) sheep[i] = Child("Sheep_" + i);
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            dust = Fx("FxDust");
        }

        protected override void OnTelegraph()
        {
            Radius = 3f;
            var h = Predict(Target, Definition.WarningSeconds + 1.2f, 1f);
            axis = h.Axis;
            right = h.Right;
            side = Context.Random.Range(0f, 1f) < 0.5f ? -1f : 1f;
            Center = axis;
            for (int i = 0; i < Count; i++)
            {
                lanes[i] = axis + h.Forward * (-2.5f + i * 1.0f);
                hop[i] = 0f;
                sheep[i].gameObject.SetActive(true);
                sheep[i].localScale = Vector3.one * 1.3f;
                sheep[i].SetPositionAndRotation(Position(i, side * (SideDistance + (i % 2) * 1.3f), 0f), Quaternion.LookRotation(-side * right));
            }
            marker.Show(axis, -side * right, 3f, false, true, 2f);
            Play(dust, axis + right * side * SideDistance);
        }

        Vector3 Position(int i, float lateral, float bob) => lanes[i] + right * lateral + Vector3.up * bob;

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            for (int i = 0; i < Count; i++)
            {
                float bob = Mathf.Abs(Mathf.Sin(time * 5f + i)) * 0.12f;
                sheep[i].position = Position(i, side * (SideDistance + (i % 2) * 1.3f), bob);
            }
        }

        protected override void OnActivate()
        {
            marker.Hide();
            Stop(dust);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            float run = Definition.ActiveSeconds * 0.85f;
            for (int i = 0; i < Count; i++)
            {
                float s = Mathf.Clamp01((time - i * 0.12f) / run);
                float lateral = Mathf.Lerp(side * (SideDistance + (i % 2) * 1.3f), -side * (SideDistance + 1f), s);
                hop[i] = Mathf.Max(0f, hop[i] - Time.deltaTime * 4f);
                float bob = Mathf.Abs(Mathf.Sin(time * 14f + i)) * 0.25f + hop[i];
                Vector3 pos = Position(i, lateral, bob);
                sheep[i].SetPositionAndRotation(pos, Quaternion.LookRotation(-side * right));
                foreach (var racer in RacersNear(pos, 1.4f))
                {
                    if (!FirstHit(racer.Controller)) continue;
                    hop[i] = 1.4f;
                    racer.Controller.ApplySlow(0.7f, 1.0f);
                    racer.Controller.ApplyImpulse(-side * right * 2.5f + Vector3.up * 1f);
                }
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            for (int i = 0; i < Count; i++) sheep[i].localScale = Vector3.one * 1.3f * (1f - progress);
        }

        protected override void Cleanup()
        {
            marker.Hide();
            Stop(dust);
            for (int i = 0; i < Count; i++) sheep[i].gameObject.SetActive(false);
        }
    }
}
