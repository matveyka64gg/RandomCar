using System.Collections;
using RandomCar.Events;
using RandomCar.Race;
using RandomCar.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace RandomCar.Core
{
    public enum FlowState { Splash, Menu, Racing, Paused, Results }

    /// Сценарий игры: заставка → меню → гонка (пауза) → результаты → снова гонка или меню.
    /// Мир сцены не пересоздаётся: гонка только сбрасывается на решётку.
    public sealed class GameFlow : MonoBehaviour
    {
        [SerializeField] RaceManager race;
        [SerializeField] EventDirector director;
        [SerializeField] GameUI ui;
        [SerializeField] FollowCamera followCamera;
        [SerializeField] MenuCamera menuCamera;

        public FlowState State { get; private set; } = FlowState.Splash;

        float splashStarted;

        public void Configure(RaceManager raceManager, EventDirector eventDirector, GameUI gameUi, FollowCamera follow, MenuCamera menu)
        {
            race = raceManager;
            director = eventDirector;
            ui = gameUi;
            followCamera = follow;
            menuCamera = menu;
        }

        void Awake()
        {
            race.RaceFinished += OnRaceFinished;
        }

        void OnDestroy()
        {
            if (race != null) race.RaceFinished -= OnRaceFinished;
        }

        /// Вызывается GameBootstrap при запуске сцены.
        public void Begin()
        {
            ApplyQuality();
            race.PrepareGrid();
            UseMenuCamera(true);
            State = FlowState.Splash;
            splashStarted = Time.unscaledTime;
            ui.ShowSplash(ShowMenu);
        }

        public void ShowMenu()
        {
            StopAllCoroutines();
            SetPaused(false);
            if (director != null) director.CancelAll();
            race.PrepareGrid();
            UseMenuCamera(true);
            State = FlowState.Menu;
            ui.ShowMenu();
        }

        public void StartRace()
        {
            StopAllCoroutines();
            SetPaused(false);
            UseMenuCamera(false);
            State = FlowState.Racing;
            ui.ShowHud();
            race.Restart();
        }

        public void RestartRace() => StartRace();

        public void Pause()
        {
            if (State != FlowState.Racing) return;
            State = FlowState.Paused;
            SetPaused(true);
            ui.ShowPause();
        }

        public void Resume()
        {
            if (State != FlowState.Paused) return;
            State = FlowState.Racing;
            SetPaused(false);
            ui.ShowHud();
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// Качество: «высокая» — PC-профиль URP с постобработкой, «низкая» — мобильный профиль без неё.
        public void ApplyQuality()
        {
            bool high = GameSettings.Quality == 1;
            int level = Mathf.Clamp(high ? QualitySettings.names.Length - 1 : 0, 0, QualitySettings.names.Length - 1);
            QualitySettings.SetQualityLevel(level, true);
            var cam = Camera.main;
            if (cam != null && cam.TryGetComponent<UniversalAdditionalCameraData>(out var data)) data.renderPostProcessing = high;
        }

        void SetPaused(bool paused)
        {
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
        }

        void UseMenuCamera(bool menu)
        {
            if (menuCamera != null) menuCamera.enabled = menu;
            if (followCamera != null) followCamera.enabled = !menu;
        }

        void OnRaceFinished()
        {
            if (State != FlowState.Racing) return;
            StartCoroutine(ShowResultsSoon());
        }

        IEnumerator ShowResultsSoon()
        {
            yield return new WaitForSeconds(1.6f);
            State = FlowState.Results;
            ui.ShowResults();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            bool anyPressed = (keyboard != null && keyboard.anyKey.wasPressedThisFrame) ||
                              (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
            switch (State)
            {
                case FlowState.Splash:
                    // Первые полсекунды игнорируем: при старте Input System присылает «нажатия» от синхронизации устройств.
                    if (anyPressed && Time.unscaledTime - splashStarted > 0.5f) ui.SkipSplash();
                    break;
                case FlowState.Menu:
                    if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ui.Back();
                    break;
                case FlowState.Racing:
                    if (keyboard == null) break;
                    if (keyboard.escapeKey.wasPressedThisFrame || keyboard.pKey.wasPressedThisFrame) Pause();
                    else if (keyboard.rKey.wasPressedThisFrame) RestartRace();
                    break;
                case FlowState.Paused:
                    if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !ui.Back()) Resume();
                    break;
            }
        }
    }
}
