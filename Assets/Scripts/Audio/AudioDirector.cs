using RandomCar.Car;
using RandomCar.Core;
using RandomCar.Events;
using RandomCar.Race;
using UnityEngine;

namespace RandomCar.Audio
{
    /// Звук игры: отсчёт, мотор игрока, события, удары, интерфейс. Громкость — из настроек.
    public sealed class AudioDirector : MonoBehaviour
    {
        [SerializeField] RaceManager race;
        [SerializeField] EventDirector director;

        AudioSource ui;
        AudioSource sfx;
        AudioSource engine;
        AudioSource ambience;
        CarController player;
        int lastCount = -1;
        RaceEvent lastEvent;
        EventStage lastStage;
        bool wasBoosted;

        public static AudioDirector Instance { get; private set; }

        public void Configure(RaceManager raceManager, EventDirector eventDirector)
        {
            race = raceManager;
            director = eventDirector;
        }

        void Awake()
        {
            Instance = this;
            ui = NewSource("UI", false, 1f);
            sfx = NewSource("Sfx", false, 1f);
            engine = NewSource("Engine", true, 0f);
            engine.clip = SoundBank.Engine;
            ambience = NewSource("Ambience", true, 0.12f);
            ambience.clip = SoundBank.Crowd;
            ambience.Play();
            ApplyVolume();
            GameSettings.Changed += ApplyVolume;
        }

        void OnDestroy()
        {
            GameSettings.Changed -= ApplyVolume;
            if (player != null) player.Bumped -= OnBump;
        }

        void Start()
        {
            player = race.Player?.Controller;
            if (player != null) player.Bumped += OnBump;
        }

        AudioSource NewSource(string name, bool loop, float volume)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = volume;
            return source;
        }

        static void ApplyVolume() => AudioListener.volume = GameSettings.Volume;

        public void PlayUiMove() => ui.PlayOneShot(SoundBank.UiMove, 0.8f);
        public void PlayUiClick() => ui.PlayOneShot(SoundBank.UiClick, 0.9f);

        void OnBump(float strength)
        {
            if (strength > 1.5f) sfx.PlayOneShot(SoundBank.Thud, Mathf.Clamp01(strength / 8f));
        }

        void Update()
        {
            if (race == null) return;
            UpdateCountdown();
            UpdateEngine();
            UpdateEvents();
        }

        void UpdateCountdown()
        {
            if (race.Phase == RacePhase.Countdown)
            {
                int count = Mathf.CeilToInt(race.Countdown);
                if (count != lastCount && count <= 3 && count > 0) sfx.PlayOneShot(SoundBank.Beep, 0.9f);
                lastCount = count;
            }
            else if (race.Phase == RacePhase.Running && lastCount > 0)
            {
                sfx.PlayOneShot(SoundBank.Go, 1f);
                lastCount = 0;
            }
            else if (race.Phase == RacePhase.Idle)
            {
                lastCount = -1;
            }
        }

        void UpdateEngine()
        {
            bool racing = race.Phase == RacePhase.Countdown || race.Phase == RacePhase.Running;
            if (player == null || !racing)
            {
                engine.volume = Mathf.MoveTowards(engine.volume, 0f, Time.unscaledDeltaTime);
                if (engine.volume <= 0f && engine.isPlaying) engine.Stop();
                return;
            }
            if (!engine.isPlaying) engine.Play();
            float speed01 = Mathf.Clamp01(Mathf.Abs(player.Speed) / (CarController.MaxSpeed * 1.25f));
            float throttle = race.Phase == RacePhase.Countdown ? 0.15f : Mathf.Clamp01(player.Throttle);
            engine.pitch = Mathf.Lerp(engine.pitch, 0.75f + speed01 * 1.35f + throttle * 0.08f, 1f - Mathf.Exp(-8f * Time.deltaTime));
            engine.volume = Mathf.Lerp(engine.volume, 0.16f + speed01 * 0.12f, 1f - Mathf.Exp(-6f * Time.deltaTime));

            if (player.IsBoosted && !wasBoosted) sfx.PlayOneShot(SoundBank.Whoosh, 0.7f);
            wasBoosted = player.IsBoosted;
        }

        void UpdateEvents()
        {
            var ev = director != null ? director.Active : null;
            if (ev == null)
            {
                lastEvent = null;
                return;
            }
            if (ev != lastEvent)
            {
                lastEvent = ev;
                lastStage = ev.Stage;
                sfx.PlayOneShot(ev.Definition.IsGood ? SoundBank.WarnGood : SoundBank.WarnBad, 0.8f);
                if (ev.Id == "sheep") sfx.PlayOneShot(SoundBank.Bleat, 0.7f);
                return;
            }
            if (ev.Stage == lastStage) return;
            lastStage = ev.Stage;
            if (ev.Stage != EventStage.Active) return;

            // Громче, если событие рядом с игроком.
            float distance = player != null ? Vector3.Distance(player.transform.position, ev.Center) : 0f;
            float near = Mathf.Clamp01(1f - distance / 120f);
            switch (ev.Id)
            {
                case "rocket":
                case "tank":
                    sfx.PlayOneShot(SoundBank.Explosion, 0.4f + 0.6f * near);
                    break;
                case "worms":
                case "pillars":
                    sfx.PlayOneShot(SoundBank.Thud, 0.5f + 0.5f * near);
                    break;
                case "wind":
                case "tailwind":
                    sfx.PlayOneShot(SoundBank.Whoosh, 0.5f + 0.5f * near);
                    break;
                case "sheep":
                    sfx.PlayOneShot(SoundBank.Bleat, 0.6f);
                    break;
                case "crate":
                case "boostpad":
                case "ramp":
                    sfx.PlayOneShot(SoundBank.Pop, 0.6f);
                    break;
            }
        }
    }
}
