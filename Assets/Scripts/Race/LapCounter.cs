using System;

namespace RandomCar.Race
{
    /// Счёт кругов по трём секторам трассы. Круг засчитан, когда машина проехала сектор 1 и сектор 2
    /// вперёд и пересекла финиш (переход 2 → 0). Езда назад, срезание через финиш и топтание на финише круг не дают.
    /// Если старт за линией (сектор 2), первое пересечение линии только начинает гонку и кругом не считается.
    public sealed class LapCounter
    {
        const int SectorCount = 3;

        int lastSector;
        bool passedMiddle;
        bool passedLast;

        public LapCounter(float startProgress)
        {
            lastSector = SectorOf(startProgress);
            Started = lastSector != SectorCount - 1;
        }

        public int Laps { get; private set; }

        /// Машина пересекла стартовую линию (или стартовала уже за ней).
        public bool Started { get; private set; }

        /// Возвращает true, если в этом обновлении круг засчитан.
        public bool Update(float progress)
        {
            int sector = SectorOf(progress);
            if (sector == lastSector) return false;

            bool forward = sector == (lastSector + 1) % SectorCount;
            lastSector = sector;
            if (!forward) return false;

            if (sector == 1)
            {
                passedMiddle = true;
                return false;
            }
            if (sector == 2)
            {
                passedLast = true;
                return false;
            }

            // Переход 2 → 0: пересечение линии.
            Started = true;
            if (!passedMiddle || !passedLast) return false;

            Laps++;
            passedMiddle = false;
            passedLast = false;
            return true;
        }

        static int SectorOf(float progress)
        {
            double wrapped = progress - Math.Floor(progress);
            return Math.Min((int)(wrapped * SectorCount), SectorCount - 1);
        }
    }
}
