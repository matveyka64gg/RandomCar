using System;
using RandomCar.Car;

namespace RandomCar.Race
{
    public enum ParticipantKind { LocalPlayer, Ai, Remote }

    /// Участник гонки до появления машины: имя, машина, сборка (вид и настройка), кто управляет.
    [Serializable]
    public sealed class Participant
    {
        public string Name;
        public string CarKey;
        public CarBuild Build;
        public ParticipantKind Kind;
        /// Сетевой идентификатор игрока (для удалённых и для хоста).
        public ulong ClientId;
    }
}
