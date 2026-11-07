using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Каток: на одной стороне дороги (около 60% ширины) намерзает лёд, по нему на пузе катается пингвин,
    /// второй машет с обочины. На льду сцепление падает вдвое (занос — из физики шин), из-под колёс
    /// летит ледяная крошка. Сухая полоса остаётся всегда. Слой поверх участка: сочетается с молотами,
    /// бочками и каруселью (их и надо объезжать по льду).
    public sealed class IceEvent : RaceEvent
    {
        public const int Tiles = 6;

        readonly Transform[] tiles = new Transform[Tiles];
        readonly Dictionary<CarController, float> nextSpray = new Dictionary<CarController, float>();
        readonly HashSet<CarController> onIce = new HashSet<CarController>();
        Transform sliding;
        Transform waving;
        TelegraphMarker marker;
        float latMin, latMax;
        float grow;

        void Awake()
        {
            for (int i = 0; i < Tiles; i++)
            {
                tiles[i] = Child("Ice_" + i);
                tiles[i].gameObject.SetActive(false);
            }
            sliding = Child("Penguin_0");
            waving = Child("Penguin_1");
            sliding.gameObject.SetActive(false);
            waving.gameObject.SetActive(false);
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnTelegraph()
        {
            nextSpray.Clear();
            onIce.Clear();
            float side = Placement.Lateral >= 0f ? 1f : -1f;
            float width = RoadHalf * 2f * 0.6f;
            latMin = side > 0f ? RoadHalf - width : -RoadHalf;
            latMax = latMin + width;
            grow = 0f;
            PlaceTiles();
            var c = At(Progress, (latMin + latMax) * 0.5f);
            Center = c.Position;
            Radius = width * 0.5f;
            sliding.gameObject.SetActive(true);
            waving.gameObject.SetActive(true);
            var w = At(Progress - 8f / Context.Track.Length, side * (RoadHalf + 1.6f));
            waving.SetPositionAndRotation(Ground(w.Position), Quaternion.LookRotation(-w.Forward));
            marker.Show(c.Position, c.Forward, width * 0.5f, false, false, ZoneLength * 0.5f / (width * 0.5f));
            ChaosEffects?.Sound(SoundBank.IceCrack, Center, 0.6f, 1f);
        }

        void PlaceTiles()
        {
            float length = Context.Track.Length;
            float step = ZoneLength / Tiles;
            float width = latMax - latMin;
            for (int i = 0; i < Tiles; i++)
            {
                float along = (i - (Tiles - 1) * 0.5f) * step;
                var h = At(Progress + along / length, (latMin + latMax) * 0.5f);
                // Лёд «намерзает»: плитки растут от середины по очереди.
                float k = Mathf.Clamp01(grow * 1.6f - Mathf.Abs(i - (Tiles - 1) * 0.5f) * 0.15f);
                tiles[i].gameObject.SetActive(k > 0.01f);
                tiles[i].SetPositionAndRotation(h.Position + Vector3.up * 0.02f, Quaternion.LookRotation(h.Forward) * Quaternion.Euler(0f, (i % 2 == 0 ? 1.5f : -1.5f), 0f));
                tiles[i].localScale = new Vector3(width * k, 1f, (step + 0.6f) * k);
            }
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            grow = Mathf.Clamp01(progress / 0.7f);
            PlaceTiles();
            AnimatePenguins();
        }

        void AnimatePenguins()
        {
            // Пингвин катится на пузе туда-сюда вдоль льда; второй машет крылом у обочины.
            float t = EventTime;
            float along = Mathf.Sin(t * 0.9f) * ZoneLength * 0.35f;
            float lat = Mathf.Lerp(latMin + 1.5f, latMax - 1.5f, 0.5f + 0.5f * Mathf.Sin(t * 0.55f));
            var h = At(Progress + along / Context.Track.Length, lat);
            Vector3 dir = h.Forward * Mathf.Sign(Mathf.Cos(t * 0.9f) + 0.001f);
            sliding.SetPositionAndRotation(h.Position + Vector3.up * 0.25f, Quaternion.LookRotation(Vector3.down, -dir) * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 8f) * 8f));
            waving.localRotation = Quaternion.Euler(0f, waving.localEulerAngles.y, Mathf.Sin(t * 6f) * 10f);
        }

        protected override void OnActivate() => marker.Hide();

        protected override void OnActiveUpdate(float progress, float time)
        {
            AnimatePenguins();
            foreach (var racer in Racers)
            {
                if (!InZone(racer, ZoneLength * 0.5f, latMin - 0.6f, latMax + 0.6f))
                {
                    onIce.Remove(racer.Controller);
                    continue;
                }
                if (onIce.Add(racer.Controller)) ChaosEffects?.Sound(SoundBank.IceCrack, racer.Car.position, 0.7f, Random.Range(0.9f, 1.2f));
                if (racer.Controller.Physics.Simulated && !racer.Controller.IsSlipping) racer.Controller.ApplySlip(0.6f);
                nextSpray.TryGetValue(racer.Controller, out float at);
                if (Time.time < at || racer.Controller.SpeedKmh < 30f) continue;
                nextSpray[racer.Controller] = Time.time + 0.35f;
                ChaosEffects?.Blast(racer.Car.position - racer.Car.forward * 1.6f, BlastStyle.Ice, 0.5f);
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            grow = 1f - progress;
            PlaceTiles();
        }

        protected override void Cleanup()
        {
            marker.Hide();
            foreach (var t in tiles) t.gameObject.SetActive(false);
            sliding.gameObject.SetActive(false);
            waving.gameObject.SetActive(false);
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage != EventStage.Telegraph && Stage != EventStage.Active) return;
            list.Add(new HazardInfo { Progress = Progress, Center = Center, Radius = (latMax - latMin) * 0.5f, HalfLength = ZoneLength * 0.5f, Good = false, Soft = true, Since = StartedAt });
        }
    }
}
