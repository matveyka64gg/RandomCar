using System.Collections.Generic;
using RandomCar.Audio;
using UnityEngine;

namespace RandomCar.Events
{
    /// Утиный дождь: над дорогой рядом с машиной с неба валятся гигантские резиновые утки. Они крякают,
    /// прыгают по асфальту, сталкиваются друг с другом и отбрасывают машины. Через несколько секунд — «пуф».
    public sealed class DucksGag : GagEvent
    {
        public const int Count = 5;

        readonly ChaosBody[] ducks = new ChaosBody[Count];
        readonly List<ChaosBody> live = new List<ChaosBody>();
        TelegraphMarker marker;
        float quackAt;

        void Awake()
        {
            for (int i = 0; i < Count; i++)
            {
                ducks[i] = Child("Duck_" + i).GetComponent<ChaosBody>();
                ducks[i].gameObject.SetActive(false);
            }
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnGagTelegraph()
        {
            live.Clear();
            marker.Show(Ground(Point.Position), Point.Forward, RoadHalf * 0.8f, false, false);
            ChaosEffects?.Sound(SoundBank.Quack, Point.Position + Vector3.up * 15f, 0.9f, 0.8f);
        }

        protected override void OnTelegraphUpdate(float progress, float time) => marker.Pulse(progress);

        protected override void OnActivate()
        {
            marker.Hide();
            float length = Context.Track.Length;
            for (int i = 0; i < Count; i++)
            {
                var duck = ducks[i];
                var h = At(Progress + RandomRange(-9f, 9f) / length, Mathf.Clamp(Point.Lateral + RandomRange(-5f, 5f), -RoadHalf + 1.5f, RoadHalf - 1.5f));
                Vector3 start = Ground(h.Position) + Vector3.up * RandomRange(8f, 15f);
                duck.NetId = BodyId(i);
                duck.transform.localScale = Vector3.one * RandomRange(0.9f, 1.25f);
                var rotation = Quaternion.LookRotation(Quaternion.Euler(0f, RandomRange(0f, 360f), 0f) * h.Forward);
                duck.Spawn(Context.Track, RoadHalf, start, rotation, Vector3.down * RandomRange(2f, 6f) + h.Right * RandomRange(-3f, 3f), Random.insideUnitSphere * 3f, !Authority);
                live.Add(duck);
            }
            ChaosEffects?.Pop(Point.Position + Vector3.up * 12f, "КРЯ-КРЯ!", new Color(1f, 0.85f, 0.2f), 1.6f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            if (Time.time >= quackAt && live.Count > 0)
            {
                quackAt = Time.time + Random.Range(0.4f, 0.9f);
                var duck = live[Random.Range(0, live.Count)];
                if (duck.Live) ChaosEffects?.Sound(SoundBank.Quack, duck.transform.position, 0.6f, Random.Range(0.8f, 1.3f));
            }
            foreach (var duck in live)
            {
                if (!duck.Live || !duck.Lost) continue;
                ChaosEffects?.Blast(duck.transform.position, BlastStyle.ColorPuff, 0.8f, new Color(1f, 0.9f, 0.3f, 0.8f));
                duck.Despawn();
            }
        }

        protected override void OnEnd()
        {
            foreach (var duck in live)
                if (duck.Live) ChaosEffects?.Blast(duck.transform.position, BlastStyle.ColorPuff, 0.9f, new Color(1f, 0.9f, 0.3f, 0.8f));
        }

        protected override void OnEndUpdate(float progress)
        {
            foreach (var duck in live)
                if (duck.Live) duck.transform.localScale = Vector3.one * Mathf.Max(0.05f, 1f - progress);
        }

        protected override void Cleanup()
        {
            marker.Hide();
            foreach (var duck in ducks) if (duck.Live) duck.Despawn();
            live.Clear();
        }
    }
}
