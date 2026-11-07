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
        AudioSource ambience;
        CarController player;
        int lastCount = -1;
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
            ambience = NewSource("Ambience", true, AmbienceVolume);
            ambience.clip = SoundBank.Crowd;
            ambience.Play();
            ApplyVolume();
            GameSettings.Changed += ApplyVolume;
        }

        void OnDestroy()
        {
            GameSettings.Changed -= ApplyVolume;
            if (race != null) race.RacersChanged -= BindPlayer;
            UnbindPlayer();
        }

        void Start()
        {
            race.RacersChanged += BindPlayer;
            BindPlayer();
        }

        void BindPlayer()
        {
            UnbindPlayer();
            player = race.Player?.Controller;
            if (player == null) return;
            player.Bumped += OnBump;
            player.Impact += OnImpact;
        }

        void UnbindPlayer()
        {
            if (player == null) return;
            player.Bumped -= OnBump;
            player.Impact -= OnImpact;
            player = null;
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

        const float AmbienceVolume = 0.12f;

        /// Общая громкость — на слушателе (действует и на музыку), громкость эффектов — на источниках эффектов.
        void ApplyVolume()
        {
            AudioListener.volume = GameSettings.Volume;
            float sfxVolume = GameSettings.SfxVolume;
            ui.volume = sfxVolume;
            sfx.volume = sfxVolume;
            ambience.volume = AmbienceVolume * sfxVolume;
        }

        public void PlayUiMove() => ui.PlayOneShot(SoundBank.UiMove, 0.8f);
        public void PlayUiClick() => ui.PlayOneShot(SoundBank.UiClick, 0.9f);

        float lastImpact;

        void OnBump(float strength)
        {
            if (strength > 1.5f && Time.time - lastImpact > 0.15f) sfx.PlayOneShot(SoundBank.Thud, Mathf.Clamp01(strength / 8f));
        }

        /// Удар о стену или машину: металлический лязг, сильнее — громче и глуше.
        void OnImpact(Vector3 point, float strength)
        {
            if (Time.time - lastImpact < 0.12f) return;
            lastImpact = Time.time;
            sfx.PlayOneShot(SoundBank.Clank, Mathf.Clamp01(0.25f + strength / 9f));
            if (strength > 4f) sfx.PlayOneShot(SoundBank.Thud, Mathf.Clamp01(strength / 12f));
        }

        void Update()
        {
            if (race == null) return;
            UpdateCountdown();
            UpdateBoost();
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

        /// Рывок от события — отдельный звук, не путать с турбиной двигателя.
        void UpdateBoost()
        {
            if (player == null) return;
            if (player.IsBoosted && !wasBoosted) sfx.PlayOneShot(SoundBank.Whoosh, 0.7f);
            wasBoosted = player.IsBoosted;
        }

        float lastWarnAt = -10f;
        readonly System.Collections.Generic.Dictionary<RaceEvent, EventStage> heard = new System.Collections.Generic.Dictionary<RaceEvent, EventStage>();
        readonly System.Collections.Generic.List<RaceEvent> forget = new System.Collections.Generic.List<RaceEvent>();

        /// Звуки событий: предупреждение — всем (громче, если близко к игроку), удар — с затуханием по расстоянию.
        void UpdateEvents()
        {
            if (director == null) return;
            foreach (var ev in director.ActiveEvents)
            {
                float distance = player != null ? Vector3.Distance(player.transform.position, ev.Center) : 0f;
                float near = Mathf.Clamp01(1f - distance / 160f);
                if (!heard.TryGetValue(ev, out var stage))
                {
                    heard[ev] = ev.Stage;
                    // В «Безумии» событий много: сигнал предупреждения не чаще раза в 0.7 с, чтобы не сливался в вой.
                    if (Time.time - lastWarnAt < 0.7f) continue;
                    lastWarnAt = Time.time;
                    sfx.PlayOneShot(ev.Definition.IsGood ? SoundBank.WarnGood : SoundBank.WarnBad, 0.35f + 0.5f * near);
                    if (ev.Id == "sheep") sfx.PlayOneShot(SoundBank.Bleat, 0.3f + 0.4f * near);
                    continue;
                }
                if (ev.Stage == stage) continue;
                heard[ev] = ev.Stage;
                if (ev.Stage != EventStage.Active) continue;
                switch (ev.Id)
                {
                    case "rocket":
                    case "tank":
                        sfx.PlayOneShot(SoundBank.Explosion, 0.25f + 0.75f * near);
                        break;
                    case "worms":
                    case "pillars":
                        sfx.PlayOneShot(SoundBank.Thud, 0.3f + 0.7f * near);
                        break;
                    case "wind":
                    case "tailwind":
                        sfx.PlayOneShot(SoundBank.Whoosh, 0.3f + 0.6f * near);
                        break;
                    case "sheep":
                        sfx.PlayOneShot(SoundBank.Bleat, 0.2f + 0.5f * near);
                        break;
                    case "crate":
                    case "boostpad":
                    case "ramp":
                        sfx.PlayOneShot(SoundBank.Pop, 0.25f + 0.5f * near);
                        break;
                }
            }
            // Закончившиеся события забываем, чтобы повторный запуск того же экземпляра снова прозвучал.
            forget.Clear();
            foreach (var pair in heard) if (!pair.Key.IsRunning) forget.Add(pair.Key);
            foreach (var ev in forget) heard.Remove(ev);
        }
    }
}
