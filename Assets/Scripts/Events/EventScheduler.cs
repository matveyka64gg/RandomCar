using System;
using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Events
{
    /// Выбор событий без Unity-объектов (тестируется в EditMode).
    /// Правила: два тяжёлых подряд не выпадают и одновременно не идут; когда часть событий временно исключена
    /// (тяжёлые, закрытые Хаосом или накалом, без места в бюджете физики), веса оставшихся **своей стороны**
    /// растягиваются, чтобы суммы плохих и хороших не менялись и доля плохих/хороших осталась равной весам
    /// из настроек. Цель-машину планировщик не выбирает вовсе: место события — забота EventPlacer.
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

        /// Последнее состоявшееся событие.
        public EventDefinition Last { get; private set; }

        /// Пауза до следующего события. «Выключено» — событий нет совсем.
        public float NextDelay(ChaosLevel chaos, Vector2 moderate, Vector2 many) =>
            NextDelay(chaos, moderate, many, many * 0.7f);

        public float NextDelay(ChaosLevel chaos, Vector2 moderate, Vector2 many, Vector2 mayhem) =>
            NextDelay(chaos, moderate, many, mayhem, mayhem * 0.55f);

        public float NextDelay(ChaosLevel chaos, Vector2 moderate, Vector2 many, Vector2 mayhem, Vector2 absurd)
        {
            switch (chaos)
            {
                case ChaosLevel.Moderate: return Mathf.Lerp(moderate.x, moderate.y, (float)random.NextDouble());
                case ChaosLevel.Many: return Mathf.Lerp(many.x, many.y, (float)random.NextDouble());
                case ChaosLevel.Mayhem: return Mathf.Lerp(mayhem.x, mayhem.y, (float)random.NextDouble());
                case ChaosLevel.Absurd: return Mathf.Lerp(absurd.x, absurd.y, (float)random.NextDouble());
                default: return float.PositiveInfinity;
            }
        }

        /// Кандидат на следующее событие. heavyActive — тяжёлое событие ещё идёт; allowed — дополнительный фильтр
        /// (Хаос, накал, бюджет физики, совместимость для комбинации). Ничего не меняет: событие считается
        /// состоявшимся только после Commit (место могло не найтись).
        public EventDefinition Pick(bool heavyActive = false, Func<EventDefinition, bool> allowed = null, bool heavyInARow = false)
        {
            bool blockHeavy = heavyActive || (!heavyInARow && Last != null && Last.IsHeavy);
            float badAvailable = 0f, goodAvailable = 0f;
            foreach (var e in events)
            {
                if (!Available(e, blockHeavy, allowed)) continue;
                if (e.IsGood) goodAvailable += e.Weight;
                else badAvailable += e.Weight;
            }
            // Каждая сторона растягивается до своей полной суммы: 50/50 сохраняется при любом фильтре.
            // Если одна сторона исключена целиком (например, для комбинации нужен только плохой) — выбор из другой.
            float badScale = badAvailable > 0f ? badTotal / badAvailable : 0f;
            float goodScale = goodAvailable > 0f ? goodTotal / goodAvailable : 0f;
            float total = badAvailable * badScale + goodAvailable * goodScale;
            if (total <= 0f) return null;
            float roll = (float)random.NextDouble() * total;
            EventDefinition pick = null;
            foreach (var e in events)
            {
                if (!Available(e, blockHeavy, allowed)) continue;
                float w = e.Weight * (e.IsGood ? goodScale : badScale);
                pick = e;
                if (roll < w) break;
                roll -= w;
            }
            return pick;
        }

        static bool Available(EventDefinition e, bool blockHeavy, Func<EventDefinition, bool> allowed) =>
            e.Weight > 0f && !(blockHeavy && e.IsHeavy) && (allowed == null || allowed(e));

        /// Событие состоялось.
        public void Commit(EventDefinition definition) => Last = definition;

        /// Pick + Commit: для тестов распределения и мест, где место гарантировано.
        public EventDefinition PickAndCommit(bool heavyActive = false, Func<EventDefinition, bool> allowed = null, bool heavyInARow = false)
        {
            var e = Pick(heavyActive, allowed, heavyInARow);
            Commit(e);
            return e;
        }

        public float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

        public System.Random Random => random;
    }
}
