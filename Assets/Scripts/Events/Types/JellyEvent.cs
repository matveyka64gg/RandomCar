using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Желейная дорога: на одной половине дороги колышется розовое желе. Машину на нём подбрасывает
    /// мелкими прыжками («пружинящая поверхность»), колёса теряют дорогу, скорость немного падает.
    /// Сочетается с минами («Прыгучие мины») и обстрелом. Вторая половина дороги обычная.
    public sealed class JellyEvent : RaceEvent
    {
        public const int Blocks = 12;

        readonly Transform[] blocks = new Transform[Blocks];
        readonly Vector3[] homes = new Vector3[Blocks];
        readonly Dictionary<CarController, float> nextHop = new Dictionary<CarController, float>();
        TelegraphMarker marker;
        float latMin, latMax;
        float grow;
        float cell;

        void Awake()
        {
            for (int i = 0; i < Blocks; i++)
            {
                blocks[i] = Child("Jelly_" + i);
                blocks[i].gameObject.SetActive(false);
            }
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnTelegraph()
        {
            nextHop.Clear();
            float side = Placement.Lateral >= 0f ? 1f : -1f;
            float width = RoadHalf * 2f * 0.55f;
            latMin = side > 0f ? RoadHalf - width : -RoadHalf;
            latMax = latMin + width;
            float length = Context.Track.Length;
            int cols = 2, rowsCount = Blocks / cols;
            cell = ZoneLength / rowsCount;
            for (int i = 0; i < Blocks; i++)
            {
                int row = i / cols, col = i % cols;
                float along = (row - (rowsCount - 1) * 0.5f) * cell;
                float lat = Mathf.Lerp(latMin, latMax, (col + 0.5f) / cols);
                var h = At(Progress + along / length, lat);
                homes[i] = h.Position + Vector3.up * 0.01f;
                blocks[i].gameObject.SetActive(true);
                blocks[i].SetPositionAndRotation(homes[i], Quaternion.LookRotation(h.Forward));
            }
            grow = 0f;
            Scale();
            var c = At(Progress, (latMin + latMax) * 0.5f);
            Center = c.Position;
            Radius = width * 0.5f;
            marker.Show(c.Position, c.Forward, width * 0.5f, false, false, ZoneLength * 0.5f / (width * 0.5f));
        }

        void Scale()
        {
            float width = (latMax - latMin) / 2f;
            for (int i = 0; i < Blocks; i++)
            {
                // Колыхание: волна бежит по желе, каждая плитка дышит со своей фазой.
                float wobble = 1f + Mathf.Sin(Time.time * 9f + i * 0.8f) * 0.25f;
                float k = Mathf.Clamp01(grow * 1.4f - (i / 2) * 0.05f);
                blocks[i].localScale = new Vector3(width * k * (2f - wobble) * 0.5f + width * k * 0.5f, 0.32f * k * wobble, cell * 0.98f * k);
            }
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            grow = Mathf.Clamp01(progress / 0.7f);
            Scale();
        }

        protected override void OnActivate()
        {
            marker.Hide();
            ChaosEffects?.Sound(SoundBank.Jelly, Center, 0.7f, 1f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            Scale();
            foreach (var racer in Racers)
            {
                if (!InZone(racer, ZoneLength * 0.5f, latMin - 0.4f, latMax + 0.4f)) continue;
                nextHop.TryGetValue(racer.Controller, out float at);
                if (Time.time < at || !racer.Controller.IsGrounded) continue;
                nextHop[racer.Controller] = Time.time + 0.42f;
                if (racer.Controller.Physics.Simulated)
                {
                    racer.Controller.ApplyLaunch(Vector3.up * 8f, Random.onUnitSphere * 2.5f, "желе");
                    racer.Controller.ApplySlow(0.9f, 0.5f);
                }
                ChaosEffects?.Sound(SoundBank.Jelly, racer.Car.position, 0.45f, Random.Range(0.85f, 1.25f));
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            grow = 1f - progress;
            Scale();
        }

        protected override void Cleanup()
        {
            marker.Hide();
            foreach (var b in blocks) b.gameObject.SetActive(false);
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage != EventStage.Telegraph && Stage != EventStage.Active) return;
            list.Add(new HazardInfo { Progress = Progress, Center = Center, Radius = (latMax - latMin) * 0.5f, HalfLength = ZoneLength * 0.5f, Good = false, Soft = true, Since = StartedAt });
        }
    }
}
