using UnityEngine;

namespace RandomCar.Core
{
    /// Точка входа сцены Game. Миры карт собраны и сохранены в своих сценах, здесь только запуск сценария игры.
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] GameFlow flow;

        public void Configure(GameFlow gameFlow) => flow = gameFlow;

        void Start()
        {
            if (Application.isMobilePlatform)
            {
                // Телефон: 60 кадров (по умолчанию Android ограничивает 30), экран не гаснет во время гонки.
                Application.targetFrameRate = 60;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
            else Application.targetFrameRate = -1;
            if (flow != null) flow.Begin();
        }
    }
}
