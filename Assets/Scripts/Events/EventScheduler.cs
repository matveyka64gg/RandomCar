using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Events
{
    /// Выбор событий без Unity-объектов (тестируется в EditMode).
    /// Правила: два тяжёлых подряд не выпадают; когда тяжёлые временно исключены, веса оставшихся плохих
    /// растягиваются, чтобы сумма плохих не менялась и доля плохих/хороших осталась равной весам из настроек.
    public sealed class EventScheduler
    {
        readonly IReadOnlyList<EventDefinition> events;
        readonly System.Random random;
        readonly float badTotal;
        readonly float goodTotal;

        public EventScheduler(IReadOnlyList<EventDefinition> definitions, int seed)
        {
            events = definitions;
            random = new System.Random(seed);
            foreach (var e in events)
            {
                if (e.IsGood) goodTotal += e.Weight;
                else badTotal += e.Weight;
            }
        }

        public EventDefinition Last { get; private set; }

        /// Пауза до следующего события. «Выключено» — событий нет совсем.
        public float NextDelay(ChaosLevel chaos, Vector2 moderate, Vector2 many)
        {
            switch (chaos)
            {
                case ChaosLevel.Moderate: return Mathf.Lerp(moderate.x, moderate.y, (float)random.NextDouble());
                case ChaosLevel.Many: return Mathf.Lerp(many.x, many.y, (float)random.NextDouble());
                default: return float.PositiveInfinity;
            }
        }

        public EventDefinition Pick()
        {
            bool blockHeavy = Last != null && Last.IsHeavy;
            float badAvailable = 0f;
            foreach (var e in events)
                if (!e.IsGood && !(blockHeavy && e.IsHeavy)) badAvailable += e.Weight;
            float scale = badAvailable > 0f ? badTotal / badAvailable : 0f;

            float total = goodTotal + badAvailable * scale;
            float roll = (float)random.NextDouble() * total;
            EventDefinition pick = null;
            foreach (var e in events)
            {
                float w = Weight(e, blockHeavy, scale);
                if (w <= 0f) continue;
                pick = e;
                if (roll < w) break;
                roll -= w;
            }
            Last = pick;
            return pick;
        }

        float Weight(EventDefinition e, bool blockHeavy, float scale)
        {
            if (e.IsGood) return e.Weight;
            if (blockHeavy && e.IsHeavy) return 0f;
            return e.Weight * scale;
        }

        /// Цель: равновероятно любая из машин, позиция в гонке не учитывается.
        public int PickTarget(int count) => random.Next(count);

        /// Сдвиг прицела поперёк дороги, не больше FairnessLimits.TargetLateralJitter.
        public float LateralJitter() => ((float)random.NextDouble() * 2f - 1f) * FairnessLimits.TargetLateralJitter;

        public float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
    }
}
