using System;
using RandomCar.Core;
using RandomCar.Race;
using UnityEngine;
using UnityEngine.UIElements;

namespace RandomCar.UI
{
    /// Эффект ГИПЕРГАМИИ: картинка пользователя мгновенно на весь экран поверх игры и интерфейса ровно на 10 с
    /// и одновременно громко — музыка пользователя (звук из его видео). Отдельная панель UI Toolkit с порядком
    /// выше игрового интерфейса. Картинка вписана по высоте без искажений, свободное место — цветом её же краёв
    /// (у этой картинки — белым): кадр выглядит одной картинкой во весь экран при любом соотношении сторон.
    /// Таймер — реальное время с момента касания, не перезапускается повторными касаниями; на паузе замирает
    /// (картинка прячется, чтобы было видно меню, музыка ставится на паузу вместе со всем звуком). Остальные звуки
    /// игры на это время приглушены. Управление машиной не блокируется. Перезапуск гонки или выход в меню — сразу конец.
    [RequireComponent(typeof(UIDocument), typeof(AudioSource))]
    public sealed class HypergamyScreen : MonoBehaviour
    {
        /// Ровно столько картинка держится на экране, с (решение пользователя; музыка 9.9 с — картинка всё равно до конца).
        public const float Duration = 10f;
        /// Громкость остальных звуков игры, пока идёт эффект (доля от общей громкости).
        public const float DuckGain = 0.2f;

        [SerializeField] Texture2D picture;
        [SerializeField] AudioClip music;
        [SerializeField] Color fill = Color.white;
        [SerializeField] GameFlow flow;
        [SerializeField] RaceManager race;

        AudioSource source;
        VisualElement overlay, image;
        bool active, hidden;
        float elapsed;

        public static HypergamyScreen Instance { get; private set; }

        /// Эффект начался (true) или закончился (false).
        public static event Action<bool> Changed;

        public bool Active => active;
        public float Elapsed => elapsed;
        /// Картинка сейчас видна (идёт эффект и не пауза).
        public bool Visible => active && !hidden;
        public int Shows { get; private set; }
        /// Касания, пока картинка уже на экране (не перезапускают таймер).
        public int Repeats { get; private set; }
        public Texture2D Picture => picture;
        public AudioClip Music => music;
        public Color Fill => fill;
        public AudioSource Source => source;
        public VisualElement Overlay => overlay;

        public void Configure(Texture2D image, AudioClip clip, Color background, GameFlow gameFlow, RaceManager raceManager)
        {
            picture = image;
            music = clip;
            fill = background;
            flow = gameFlow;
            race = raceManager;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Changed = null;
        }

        void Awake()
        {
            Instance = this;
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            // Громкость своя (общая громкость игры), а не слушателя: слушатель на время эффекта приглушает остальное.
            source.ignoreListenerVolume = true;
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            overlay = new VisualElement { name = "HypergamyOverlay", pickingMode = PickingMode.Ignore };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0f;
            overlay.style.top = 0f;
            overlay.style.right = 0f;
            overlay.style.bottom = 0f;
            overlay.style.backgroundColor = fill;
            image = new VisualElement { name = "HypergamyImage", pickingMode = PickingMode.Ignore };
            image.style.position = Position.Absolute;
            image.style.left = 0f;
            image.style.top = 0f;
            image.style.right = 0f;
            image.style.bottom = 0f;
            image.style.backgroundImage = new StyleBackground(picture);
            image.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Contain));
            image.style.backgroundRepeat = new StyleBackgroundRepeat(new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat));
            image.style.backgroundPositionX = new StyleBackgroundPosition(new BackgroundPosition(BackgroundPositionKeyword.Center));
            image.style.backgroundPositionY = new StyleBackgroundPosition(new BackgroundPosition(BackgroundPositionKeyword.Center));
            overlay.Add(image);
            root.Add(overlay);
            overlay.style.display = DisplayStyle.None;
            if (race != null) race.RaceRestarted += OnRaceRestarted;
        }

        void OnDisable()
        {
            if (race != null) race.RaceRestarted -= OnRaceRestarted;
            Stop("выключен");
            overlay?.RemoveFromHierarchy();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnRaceRestarted() => Stop("гонка заново");

        /// Показать картинку и включить музыку. false — уже идёт (таймер не трогается) или нечего показывать.
        public bool Show()
        {
            if (active)
            {
                Repeats++;
                return false;
            }
            if (picture == null || music == null)
            {
                Debug.LogError("[RandomCar] ГИПЕРГАМИЯ: нет картинки или музыки (Assets/Art/Textures/Events/hypergamy.jpg, Assets/Art/Audio/Sfx/Events/hypergamy.wav)");
                return false;
            }
            active = true;
            hidden = false;
            elapsed = 0f;
            Shows++;
            overlay.style.display = DisplayStyle.Flex;
            image.style.scale = new StyleScale(new Scale(Vector3.one * 1.08f));
            source.clip = music;
            source.time = 0f;
            source.volume = GameSettings.Volume;
            source.Play();
            AudioListener.volume = GameSettings.Volume * DuckGain;
            Debug.Log($"[RandomCar] ГИПЕРГАМИЯ: картинка {picture.width}×{picture.height} на весь экран {Screen.width}×{Screen.height} на {Duration:F0} с, "
                      + $"музыка «{music.name}» ({music.length:F2} с), громкость {source.volume:F2}, остальной звук ×{DuckGain:F1}");
            Changed?.Invoke(true);
            return true;
        }

        void Update()
        {
            if (!active) return;
            var state = flow != null ? flow.State : FlowState.Racing;
            if (state != FlowState.Racing && state != FlowState.Paused && state != FlowState.Results)
            {
                Stop("выход из гонки");
                return;
            }
            bool paused = state == FlowState.Paused;
            if (paused != hidden)
            {
                hidden = paused;
                overlay.style.display = paused ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (paused) return;
            elapsed += Time.unscaledDeltaTime;
            // Мгновенно на весь экран и «шлёп»: картинка чуть больше и за 0.2 с садится на место.
            float punch = 1f + 0.08f * Mathf.Exp(-elapsed * 16f);
            image.style.scale = new StyleScale(new Scale(new Vector3(punch, punch, 1f)));
            source.volume = GameSettings.Volume;
            AudioListener.volume = GameSettings.Volume * DuckGain;
            if (elapsed >= Duration) Stop("прошло 10 с");
        }

        /// Конец эффекта: картинка исчезает, музыка останавливается, звук игры — как был.
        public void Stop(string reason)
        {
            if (!active) return;
            active = false;
            hidden = false;
            if (overlay != null) overlay.style.display = DisplayStyle.None;
            if (source != null) source.Stop();
            AudioListener.volume = GameSettings.Volume;
            Debug.Log($"[RandomCar] ГИПЕРГАМИЯ: картинка снята через {elapsed:F2} с ({reason})");
            Changed?.Invoke(false);
        }
    }
}
