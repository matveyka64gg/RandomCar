using System;
using System.Collections;
using System.Collections.Generic;
using RandomCar.Core;
using RandomCar.UserContent;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomCar.Audio
{
    /// Что делать в конце трека: по кругу весь плейлист, повтор одного трека или остановка после последнего.
    public enum MusicRepeat { All, One, Off }

    /// Где сейчас игрок: в гонке музыка тише, чтобы не спорить с мотором и событиями.
    public enum MusicContext { Menu, Garage, Race }

    /// Единственный музыкальный проигрыватель игры. Живёт в постоянной сцене Game (карты грузятся аддитивно
    /// и его не трогают), поэтому при переходах между меню, гаражом и гонкой второй копии не появляется;
    /// случайная вторая копия отключается сама. Два источника — для плавной смены треков без обрывов.
    /// Музыка не встаёт на паузу вместе с игрой (пауза в гонке), громкость — своя, отдельно от эффектов.
    public sealed class MusicPlayer : MonoBehaviour
    {
        public const float CrossfadeSeconds = 2.5f;
        const float PauseFadeSeconds = 0.35f;
        const float RestartThreshold = 3f;
        const float RaceGain = 0.55f;

        [SerializeField] MusicLibrary library;

        sealed class Deck
        {
            public AudioSource Source;
            public MusicTrack Track;
            public float Fade;
            public float Target;
            public float Speed;
        }

        readonly Deck[] decks = new Deck[2];
        int active;
        readonly List<MusicTrack> playlist = new List<MusicTrack>();
        readonly List<int> order = new List<int>();
        int position;
        string playlistKey;
        float context = 1f;
        float contextTarget = 1f;
        bool paused;
        bool stopped = true;
        bool advancing;
        System.Random shuffleRandom = new System.Random();
        /// Свои треки игрока (UserMusicLibrary): один объект на трек, клип читается с диска перед воспроизведением
        /// (в памяти — сжатым, не больше трёх своих клипов сразу).
        readonly Dictionary<string, MusicTrack> userTracks = new Dictionary<string, MusicTrack>();
        readonly List<MusicTrack> loadedUser = new List<MusicTrack>();
        MusicTrack loading;
        const int MaxLoadedUserClips = 3;

        public static MusicPlayer Instance { get; private set; }

        /// Сменился трек, пауза, режим или плейлист — интерфейс перерисовывается.
        public event Action Changed;

        public MusicLibrary Library => library;
        public MusicTrack Current => decks[active]?.Track;
        public string PlaylistKey => playlistKey;
        public int TrackCount => playlist.Count;
        public int TrackNumber => playlist.Count == 0 ? 0 : position + 1;
        public bool IsPaused => paused;
        public bool IsPlaying => Current != null && !paused && !stopped;
        public bool HasTracks => playlist.Count > 0;

        /// Доля прослушанного трека, 0..1.
        public float Progress
        {
            get
            {
                var deck = decks[active];
                if (deck?.Track == null || deck.Source.clip == null || deck.Source.clip.length <= 0f) return 0f;
                return Mathf.Clamp01(deck.Source.time / deck.Source.clip.length);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public void Configure(MusicLibrary musicLibrary) => library = musicLibrary;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[RandomCar] Второй MusicPlayer ({name}) отключён: музыка играет из одного проигрывателя.");
                enabled = false;
                return;
            }
            Instance = this;
            for (int i = 0; i < decks.Length; i++)
            {
                var go = new GameObject($"MusicDeck{i}");
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.priority = 0;
                source.bypassReverbZones = true;
                source.bypassListenerEffects = true;
                // Пауза игры ставит на паузу AudioListener — музыка в меню паузы продолжает играть.
                source.ignoreListenerPause = true;
                source.volume = 0f;
                decks[i] = new Deck { Source = source };
            }
        }

        void OnEnable() => UserMusicLibrary.Changed += RefreshPlaylist;

        void OnDisable() => UserMusicLibrary.Changed -= RefreshPlaylist;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var t in loadedUser)
                if (t.Clip != null) Destroy(t.Clip);
        }

        /// Треки плейлиста с учётом источника: встроенные (карты или общие) и/или свои. «Только своя» без своих треков — встроенные.
        List<MusicTrack> Gather(string key)
        {
            var result = new List<MusicTrack>();
            var mine = new List<MusicTrack>();
            foreach (var u in UserMusicLibrary.Tracks)
            {
                if (!userTracks.TryGetValue(u.id, out var t))
                {
                    t = new MusicTrack { Id = "user:" + u.id, Playlist = "User", Gain = 0.85f };
                    userTracks[u.id] = t;
                }
                t.Title = u.title;
                t.Artist = u.artist;
                t.FilePath = u.file;
                t.FileType = UserMusicLibrary.TypeOf(u.ext);
                if (!t.Broken) mine.Add(t);
            }
            var source = GameSettings.MusicSource;
            if ((source != MusicSource.Mine || mine.Count == 0) && library != null) result.AddRange(library.Playlist(key));
            if (source != MusicSource.Game) result.AddRange(mine);
            return result;
        }

        /// Свой трек по id библиотеки (для экрана «Моя музыка»): тот же объект, что в плейлисте.
        public MusicTrack UserTrack(string id)
        {
            Gather(playlistKey);
            return userTracks.TryGetValue(id, out var t) ? t : null;
        }

        /// Библиотека или источник изменились: плейлист пересобирается, играющий трек (если остался) не прерывается.
        public void RefreshPlaylist()
        {
            if (!enabled || playlistKey == null) return;
            var current = Current;
            playlist.Clear();
            playlist.AddRange(Gather(playlistKey));
            int keep = current != null ? playlist.IndexOf(current) : -1;
            BuildOrder(keep);
            if (playlist.Count == 0)
            {
                FadeOutAll(CrossfadeSeconds * 0.5f);
                decks[active].Track = null;
                stopped = true;
                paused = false;
                Changed?.Invoke();
                return;
            }
            if (keep >= 0)
            {
                position = Mathf.Max(0, order.IndexOf(keep));
                Changed?.Invoke();
                return;
            }
            if (paused || GameSettings.MusicPaused)
            {
                position = 0;
                active = 1 - active;
                Load(decks[active], playlist[order[0]]);
                paused = true;
                stopped = false;
                Changed?.Invoke();
                return;
            }
            StartAt(0, CrossfadeSeconds * 0.6f);
        }

        /// Включить конкретный трек (экран «Моя музыка»). Если источник «только встроенная» — переключается на «вся музыка».
        public void Play(MusicTrack track)
        {
            if (track == null || !enabled) return;
            int index = playlist.IndexOf(track);
            if (index < 0)
            {
                if (GameSettings.MusicSource == MusicSource.Game) GameSettings.MusicSource = MusicSource.All;
                track.Broken = false;
                RefreshPlaylist();
                index = playlist.IndexOf(track);
                if (index < 0) return;
            }
            GameSettings.MusicPaused = false;
            StartAt(Mathf.Max(0, order.IndexOf(index)), CrossfadeSeconds * 0.4f);
        }

        /// Плейлист карты или общий. Тот же плейлист — ничего не прерывается; другой — плавный переход.
        /// Пустой плейлист — тишина без ошибок.
        public void UsePlaylist(string key)
        {
            if (!enabled) return;
            if (string.IsNullOrEmpty(key)) key = MusicLibrary.MainPlaylist;
            if (key == playlistKey) return;
            playlistKey = key;
            playlist.Clear();
            playlist.AddRange(Gather(key));
            BuildOrder(-1);
            if (playlist.Count == 0)
            {
                FadeOutAll(CrossfadeSeconds * 0.5f);
                decks[active].Track = null;
                stopped = true;
                paused = false;
                Changed?.Invoke();
                return;
            }

            string last = GameSettings.LastTrack(key);
            int index = Mathf.Max(0, playlist.FindIndex(t => t.Id == last));
            position = Mathf.Max(0, order.IndexOf(index));
            if (GameSettings.MusicPaused)
            {
                // Пользователь выключил музыку: показываем трек, но не играем, пока не нажмёт «продолжить».
                FadeOutAll(PauseFadeSeconds);
                active = 1 - active;
                Load(decks[active], playlist[order[position]]);
                paused = true;
                stopped = false;
                Changed?.Invoke();
                return;
            }
            StartAt(position, CrossfadeSeconds);
        }

        public void SetContext(MusicContext value) => contextTarget = value == MusicContext.Race ? RaceGain : 1f;

        public void Next()
        {
            if (playlist.Count == 0) return;
            StartAt((position + 1) % order.Count, CrossfadeSeconds * 0.6f);
        }

        /// Назад: если трек играет дольше трёх секунд — с начала, иначе предыдущий.
        public void Previous()
        {
            if (playlist.Count == 0) return;
            var deck = decks[active];
            if (deck.Track != null && deck.Source.time > RestartThreshold && !paused)
            {
                StartAt(position, CrossfadeSeconds * 0.3f);
                return;
            }
            StartAt((position - 1 + order.Count) % order.Count, CrossfadeSeconds * 0.6f);
        }

        public void TogglePause()
        {
            if (playlist.Count == 0) return;
            var deck = decks[active];
            if (paused || stopped)
            {
                paused = false;
                GameSettings.MusicPaused = false;
                if (stopped || deck.Track == null || !deck.Source.clip)
                {
                    StartAt(stopped && GameSettings.MusicRepeat == MusicRepeat.Off && position == order.Count - 1 ? 0 : position, PauseFadeSeconds);
                    return;
                }
                if (deck.Source.time > 0f) deck.Source.UnPause();
                else deck.Source.Play();
                FadeTo(deck, 1f, PauseFadeSeconds);
            }
            else
            {
                paused = true;
                GameSettings.MusicPaused = true;
                FadeTo(deck, 0f, PauseFadeSeconds);
                // Второй источник (если ещё звучит после перехода) тоже гасим.
                FadeTo(decks[1 - active], 0f, PauseFadeSeconds);
            }
            Changed?.Invoke();
        }

        public void CycleRepeat()
        {
            GameSettings.MusicRepeat = (MusicRepeat)(((int)GameSettings.MusicRepeat + 1) % 3);
            Changed?.Invoke();
        }

        public void SetShuffle(bool shuffle)
        {
            if (GameSettings.MusicShuffle == shuffle) return;
            GameSettings.MusicShuffle = shuffle;
            int current = order.Count > 0 ? order[position] : -1;
            BuildOrder(current);
            position = Mathf.Max(0, order.IndexOf(current));
            Changed?.Invoke();
        }

        /// Порядок воспроизведения: по библиотеке или перемешанный (текущий трек остаётся первым).
        void BuildOrder(int keepFirst)
        {
            order.Clear();
            for (int i = 0; i < playlist.Count; i++) order.Add(i);
            if (!GameSettings.MusicShuffle) return;
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = shuffleRandom.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            int at = order.IndexOf(keepFirst);
            if (at > 0) (order[0], order[at]) = (order[at], order[0]);
        }

        void StartAt(int orderPosition, float crossfade)
        {
            position = orderPosition;
            var track = playlist[order[position]];
            if (track.IsUser && track.Clip == null)
            {
                // Свой трек: сначала прочитать файл (асинхронно), потом — обычный плавный переход.
                if (loading != track) StartCoroutine(LoadUserClip(track, orderPosition, crossfade));
                return;
            }
            loading = null;
            var old = decks[active];
            FadeTo(old, 0f, crossfade);
            active = 1 - active;
            var deck = decks[active];
            Load(deck, track);
            deck.Source.Play();
            deck.Fade = 0f;
            FadeTo(deck, 1f, crossfade);
            paused = false;
            stopped = false;
            advancing = false;
            GameSettings.SetLastTrack(playlistKey, track.Id);
            Debug.Log($"[RandomCar] Музыка ({playlistKey} {position + 1}/{order.Count}): {track.Artist} — {track.Title}");
            Changed?.Invoke();
        }

        IEnumerator LoadUserClip(MusicTrack track, int orderPosition, float crossfade)
        {
            loading = track;
            advancing = true;
            string uri = new Uri(track.FilePath).AbsoluteUri;
            using var request = UnityWebRequestMultimedia.GetAudioClip(uri, track.FileType);
            // Сжатый клип: в памяти примерно как файл, а не распакованный PCM (важно на телефоне).
            ((DownloadHandlerAudioClip)request.downloadHandler).compressed = true;
            yield return request.SendWebRequest();
            if (loading != track) yield break;   // пока читали, игрок выбрал другой трек
            loading = null;
            advancing = false;
            AudioClip clip = request.result == UnityWebRequest.Result.Success ? DownloadHandlerAudioClip.GetContent(request) : null;
            if (clip == null || clip.length <= 0f)
            {
                track.Broken = true;
                Debug.LogWarning($"[RandomCar] Свой трек не читается и пропущен: {track.FilePath} ({request.error})");
                if (clip != null) Destroy(clip);
                Changed?.Invoke();
                int next = (orderPosition + 1) % Mathf.Max(1, order.Count);
                if (order.Count > 1 && next != orderPosition && next < order.Count && !playlist[order[next]].Broken) StartAt(next, crossfade);
                yield break;
            }
            clip.name = track.Title;
            track.Clip = clip;
            loadedUser.Remove(track);
            loadedUser.Add(track);
            // Не держим в памяти много своих клипов: старые (не на источниках) выгружаются.
            while (loadedUser.Count > MaxLoadedUserClips)
            {
                var old = loadedUser[0];
                if (old == decks[0].Track || old == decks[1].Track) break;
                loadedUser.RemoveAt(0);
                if (old.Clip != null) Destroy(old.Clip);
                old.Clip = null;
            }
            if (orderPosition < order.Count && playlist[order[orderPosition]] == track) StartAt(orderPosition, crossfade);
        }

        static void Load(Deck deck, MusicTrack track)
        {
            deck.Source.Stop();
            deck.Track = track;
            deck.Source.clip = track.Clip;
            deck.Source.time = 0f;
        }

        static void FadeTo(Deck deck, float target, float seconds)
        {
            deck.Target = target;
            deck.Speed = 1f / Mathf.Max(0.05f, seconds);
        }

        void FadeOutAll(float seconds)
        {
            foreach (var d in decks) FadeTo(d, 0f, seconds);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            context = Mathf.MoveTowards(context, contextTarget, dt * 0.5f);
            float master = GameSettings.MusicVolume * context;
            foreach (var deck in decks)
            {
                deck.Fade = Mathf.MoveTowards(deck.Fade, deck.Target, deck.Speed * dt);
                // Равная мощность при перекрёстном переходе: громкость по синусу, без провала посередине.
                float shaped = Mathf.Sin(deck.Fade * Mathf.PI * 0.5f);
                float gain = deck.Track != null ? deck.Track.Gain : 1f;
                deck.Source.volume = shaped * gain * master;
                if (deck.Fade <= 0f && deck.Target <= 0f && deck.Source.isPlaying)
                {
                    if (deck == decks[active] && paused) deck.Source.Pause();
                    else deck.Source.Stop();
                }
            }

            var current = decks[active];
            if (paused || stopped || current.Track == null || current.Source.clip == null || advancing) return;
            // Переход начинается заранее, чтобы конец трека перетёк в следующий; у коротких клипов окно меньше.
            float length = current.Source.clip.length;
            float window = Mathf.Min(CrossfadeSeconds, length * 0.2f);
            bool ended = !current.Source.isPlaying && current.Source.time <= 0f && current.Fade >= 1f;
            if (length - current.Source.time <= window || ended) OnTrackEnd(window);
        }

        void OnTrackEnd(float crossfade)
        {
            advancing = true;
            switch (GameSettings.MusicRepeat)
            {
                case MusicRepeat.One:
                    StartAt(position, crossfade);
                    break;
                case MusicRepeat.Off when position >= order.Count - 1:
                    FadeOutAll(crossfade);
                    stopped = true;
                    Changed?.Invoke();
                    break;
                default:
                    StartAt((position + 1) % order.Count, crossfade);
                    break;
            }
        }
    }
}
