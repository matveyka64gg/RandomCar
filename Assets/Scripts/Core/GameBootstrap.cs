using UnityEngine;

namespace RandomCar.Core
{
    /// Точка входа сцены Main. Мир уже собран и сохранён в сцене, здесь только запуск сценария игры.
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] GameFlow flow;

        public void Configure(GameFlow gameFlow) => flow = gameFlow;

        void Start()
        {
            Application.targetFrameRate = -1;
            if (flow != null) flow.Begin();
        }
    }
}
