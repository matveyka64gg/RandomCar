using System;
using System.Collections.Generic;
using System.Linq;
using RandomCar.Car;
using RandomCar.Save;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Race
{
    public enum RacePhase { Idle, Countdown, Running, Finished }

    /// Участник в сцене: машина уже стоит на своей позиции решётки.
    [Serializable]
    public sealed class RaceEntry
    {
        public string Name;
        public CarController Car;
        public bool IsPlayer;
    }

    public sealed class Racer
    {
        public string Name;
        public bool IsPlayer;
        public Transform Car;
        public CarController Controller;
        public LapCounter Laps;
        public float Progress;
        public readonly List<float> LapTimes = new List<float>();
        public bool Finished;
        public float FinishTime;
        public Vector3 StartPosition;
        public Quaternion StartRotation;
        public float StartProgress;

        /// Общий прогресс гонки: круги плюс доля круга. Пока машина не пересекла линию старта, прогресс отрицательный.
        public float Score => Laps.Laps + Progress - (Laps.Started ? 0f : 1f);
    }

    /// Гонка на заранее собранной трассе: отсчёт со стартовыми огнями, круги, время, позиции, финиш, таблица лучших.
    /// Рестарт возвращает машины на решётку, мир не пересоздаётся.
    public sealed class RaceManager : MonoBehaviour
    {
        public const int TotalLaps = 3;
        public const float CountdownSeconds = 3f;
        const float LampInterval = 0.5f;

        [SerializeField] RaceTrack track;
        [SerializeField] List<RaceEntry> entries = new List<RaceEntry>();
        [SerializeField] StartLights startLights;
        [SerializeField] int aiSeed = 2024;

        readonly List<Racer> racers = new List<Racer>();
        float lapStartTime;
        bool built;

        public TrackPath Course => track.Path;
        public IReadOnlyList<Racer> Racers => racers;
        public Racer Player { get; private set; }
        public RacePhase Phase { get; private set; } = RacePhase.Idle;
        public float Countdown { get; private set; } = CountdownSeconds;
        public float RaceTime { get; private set; }
        public List<float> BestTimes { get; private set; } = new List<float>();

        /// Место игрока в таблице лучших после финиша, с нуля. -1, если не попал.
        public int BestRank { get; private set; } = -1;

        /// Номер попытки: растёт при каждом рестарте. Системы (события, камера) сбрасываются по нему.
        public int Attempt { get; private set; }

        public event Action RaceRestarted;
        public event Action RaceStarted;
        public event Action RaceFinished;

        public int PlayerPosition => racers.Count(r => r.Score > Player.Score) + 1;

        public void Configure(RaceTrack raceTrack, List<RaceEntry> raceEntries, StartLights lights)
        {
            track = raceTrack;
            entries = raceEntries;
            startLights = lights;
        }

        void Awake() => Build();

        void Build()
        {
            if (built) return;
            built = true;
            var random = new System.Random(aiSeed);
            foreach (var entry in entries)
            {
                var t = entry.Car.transform;
                var racer = new Racer
                {
                    Name = entry.Name,
                    IsPlayer = entry.IsPlayer,
                    Car = t,
                    Controller = entry.Car,
                    StartPosition = t.position,
                    StartRotation = t.rotation,
                    StartProgress = Course.Nearest(t.position),
                };
                racers.Add(racer);
                if (entry.IsPlayer) Player = racer;

                var ai = entry.Car.GetComponent<AiDriver>();
                if (ai != null && !entry.IsPlayer)
                {
                    float offset = (float)(random.NextDouble() * 5.0 - 2.5);
                    float skill = 0.82f + (float)random.NextDouble() * 0.13f;
                    ai.Init(Course, offset, skill);
                }
            }
            Player ??= racers.FirstOrDefault();
            BestTimes = BestTimesStore.Load(BestTimesStore.DefaultPath);
        }

        /// Возвращает всех на решётку и запускает отсчёт.
        public void Restart()
        {
            PrepareGrid();
            Phase = RacePhase.Countdown;
            RaceRestarted?.Invoke();
        }

        /// Машины на решётке, гонка не идёт (фон главного меню).
        public void PrepareGrid()
        {
            Build();
            Attempt++;
            foreach (var r in racers)
            {
                r.Controller.ResetTo(r.StartPosition, r.StartRotation);
                r.Controller.Locked = true;
                r.Laps = new LapCounter(r.StartProgress);
                r.Progress = r.StartProgress;
                r.LapTimes.Clear();
                r.Finished = false;
                r.FinishTime = 0f;
            }
            Phase = RacePhase.Idle;
            Countdown = CountdownSeconds;
            RaceTime = 0f;
            lapStartTime = 0f;
            BestRank = -1;
            if (startLights != null) startLights.SetLit(0);
        }

        /// Останавливает гонку без отсчёта (например, при выходе в меню).
        public void Stop()
        {
            Phase = RacePhase.Idle;
            foreach (var r in racers) r.Controller.Locked = true;
            if (startLights != null) startLights.SetLit(0);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            switch (Phase)
            {
                case RacePhase.Countdown:
                    Countdown -= dt;
                    float elapsed = CountdownSeconds - Countdown;
                    if (startLights != null) startLights.SetLit(Mathf.Clamp(Mathf.FloorToInt(elapsed / LampInterval) + 1, 0, 5));
                    if (Countdown <= 0f) StartRace();
                    break;

                case RacePhase.Running:
                    RaceTime += dt;
                    UpdateRacers();
                    break;
            }
        }

        void StartRace()
        {
            Phase = RacePhase.Running;
            RaceTime = 0f;
            lapStartTime = 0f;
            if (startLights != null) startLights.SetLit(0);
            foreach (var racer in racers) racer.Controller.Locked = false;
            RaceStarted?.Invoke();
        }

        void UpdateRacers()
        {
            foreach (var racer in racers)
            {
                if (racer.Finished) continue;

                racer.Progress = Course.Nearest(racer.Car.position);
                if (!racer.Laps.Update(racer.Progress)) continue;

                if (racer == Player)
                {
                    racer.LapTimes.Add(RaceTime - lapStartTime);
                    lapStartTime = RaceTime;
                }

                if (racer.Laps.Laps >= TotalLaps) FinishRacer(racer);
            }

            if (Player != null && Player.Finished) FinishRace();
        }

        void FinishRacer(Racer racer)
        {
            racer.Finished = true;
            racer.FinishTime = RaceTime;
            racer.Controller.Locked = true;
        }

        void FinishRace()
        {
            Phase = RacePhase.Finished;
            foreach (var racer in racers) racer.Controller.Locked = true;

            BestRank = BestTimesStore.Add(BestTimes, Player.FinishTime);
            BestTimesStore.Save(BestTimesStore.DefaultPath, BestTimes);
            RaceFinished?.Invoke();
        }
    }
}
