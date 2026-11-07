using System;
using System.Collections.Generic;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Events
{
    /// Участник глазами расстановщика: только где он и как быстро едет. Место в гонке и круги не передаются вовсе.
    public struct RacerState
    {
        public float Progress;
        public float Speed;
        /// Живой игрок (свой или по сети): события ставятся впереди людей, действуют на всех.
        public bool Human;
        public int Slot;

        public RacerState(float progress, float speed, bool human = false, int slot = -1)
        {
            Progress = progress;
            Speed = speed;
            Human = human;
            Slot = slot;
        }
    }

    /// Занятый участок трассы: центр (прогресс) и длина вдоль трассы, м. Definition — какое событие его занимает
    /// (для правил наложения ChaosRules; null — участок занят целиком).
    public struct EventZone
    {
        public float Progress;
        public float Length;
        public EventDefinition Definition;
        public float Lateral;

        public EventZone(float progress, float length, EventDefinition definition = null, float lateral = 0f)
        {
            Progress = progress;
            Length = length;
            Definition = definition;
            Lateral = lateral;
        }
    }

    /// Перекрытый участок дороги для проверки проезда: вдоль трассы (центр и полудлина, м) и поперёк (от и до, м).
    public struct RoadBlock
    {
        public float Progress;
        public float HalfLength;
        public float LateralMin;
        public float LateralMax;
    }

    /// Выбранное место события на трассе.
    public struct HazardSpot
    {
        public float Progress;
        /// Смещение поперёк дороги от оси, м (плюс — вправо).
        public float Lateral;
        /// Сид случайных деталей события (сторона, поворот): одинаковый у хоста и клиентов.
        public int Seed;
        /// Участник (номер на решётке), ради которого событие поставлено впереди: оно ждёт его подъезда
        /// и держится, пока он не проедет. Действует событие на всех в зоне. -1 — без привязки.
        public int FocusSlot;
    }

    /// Опасность или бонус на дороге для ИИ: где, какой ширины, что делать.
    public struct HazardInfo
    {
        public float Progress;
        public Vector3 Center;
        public float Radius;
        public float HalfLength;
        public bool Good;
        public bool Obstacle;
        /// Время появления предупреждения (Time.time): ИИ реагирует с задержкой своего уровня.
        public float Since;
        /// Скорость движущейся опасности (шар, бочка, стена), м/с: ИИ объезжает место, где она будет.
        public Vector3 Velocity;
        /// «Мягкая» опасность — покрытие (лёд, желе): лучше объехать, но тормозить перед ней не нужно.
        public bool Soft;
    }

    /// Расстановка событий по трассе. Событие не следует за машиной и не выбирает цель: из случайных мест трассы
    /// берутся только честные (ни одна машина не доедет до зоны раньше конца предупреждения плюс запаса на реакцию),
    /// не пересекающиеся с идущими событиями и такие, через которые кто-то проедет, пока событие идёт.
    /// Из подходящих мест выбор равновероятный — лидер и отстающий для расстановщика неразличимы.
    public sealed class EventPlacer
    {
        public const int Candidates = 48;
        const float RelevanceSlack = 2.5f;
        const float StraightCurvature = 1f / 70f;

        readonly float length;
        readonly Func<float, float> curvature;
        readonly System.Random random;
        readonly List<float> valid = new List<float>(Candidates);

        /// trackLength — длина трассы, м; curvatureAt — модуль кривизны по прогрессу (1/м).
        public EventPlacer(float trackLength, Func<float, float> curvatureAt, System.Random rng)
        {
            length = trackLength;
            curvature = curvatureAt;
            random = rng;
        }

        public float TrackLength => length;

        /// Полуширина дороги трассы, м (правила наложения на узкой дороге строже).
        public float RoadHalf { get; set; } = 11f;

        /// «Безумие» (v5): событие можно ставить вплотную перед машиной (честность — только «не внутри зоны»),
        /// зазор между зонами минимальный, накладывается почти всё (ChaosRules.CanStack с wild).
        public bool Wild { get; set; }

        /// Зазор между зонами, м.
        float Gap => Wild ? 3f : FairnessLimits.ZoneGap;

        /// Подобрать место. false — подходящего места сейчас нет (событие откладывается).
        public bool TryPlace(EventDefinition definition, IReadOnlyList<RacerState> racers, IReadOnlyList<EventZone> active, out HazardSpot spot)
        {
            spot = default;
            valid.Clear();
            for (int i = 0; i < Candidates; i++)
            {
                float p = (float)random.NextDouble();
                if (Acceptable(definition, p, racers, active)) valid.Add(p);
            }
            if (valid.Count == 0) return false;
            spot.Progress = valid[random.Next(valid.Count)];
            float limit = FairnessLimits.RoadHalfWidth - 1.5f;
            spot.Lateral = ((float)random.NextDouble() * 2f - 1f) * limit;
            return true;
        }

        public bool Acceptable(EventDefinition definition, float progress, IReadOnlyList<RacerState> racers, IReadOnlyList<EventZone> active) =>
            IsFair(definition, progress, racers) && !Conflicts(definition, progress, active) &&
            IsRelevant(definition, progress, racers) && (!definition.NeedsStraight || IsStraight(progress, definition.ZoneLength));

        /// Расстояние по трассе вперёд от from до to, м (0..длина).
        public float Ahead(float from, float to) => TrackPath.Wrap(to - from) * length;

        /// Место впереди выбранного участника: ищется ближайшее к желаемой дистанции честное место
        /// (никто не доедет раньше конца предупреждения, нет наложения с идущими событиями, прямая — если нужна).
        /// Сначала около желаемой дистанции, затем дальше вперёд — но никогда позади и не на другом конце трассы.
        public bool TryPlaceAhead(EventDefinition definition, RacerState focus, float distance, IReadOnlyList<RacerState> racers,
                                  IReadOnlyList<EventZone> active, out float progress)
        {
            progress = 0f;
            float min = Wild ? definition.ZoneLength * 0.5f + 6f : Mathf.Max(definition.ZoneLength * 0.5f + 20f, distance * 0.7f);
            float max = Mathf.Min(length * 0.45f, distance * 1.6f + 60f);
            for (int k = 0; k < 24; k++)
            {
                // 0, +12, −12, +24, −24 … затем шаги только вперёд.
                float offset = k < 9 ? ((k + 1) / 2) * 12f * (k % 2 == 1 ? 1f : -1f) : (k - 4) * 12f;
                float d = distance + offset;
                if (d < min || d > max) continue;
                float p = TrackPath.Wrap(focus.Progress + d / length);
                if (!IsFair(definition, p, racers) || Conflicts(definition, p, active)) continue;
                if (definition.NeedsStraight && !IsStraight(p, definition.ZoneLength)) continue;
                progress = p;
                return true;
            }
            return false;
        }

        /// Ни одна машина не успеет к зоне до конца предупреждения плюс запаса на реакцию, даже ускоряясь.
        public bool IsFair(EventDefinition definition, float progress, IReadOnlyList<RacerState> racers)
        {
            float half = definition.ZoneLength * 0.5f;
            float warn = Mathf.Max(definition.WarningSeconds, FairnessLimits.MinWarningSeconds);
            foreach (var r in racers)
            {
                float ahead = Ahead(r.Progress, progress);
                // Машина в зоне или только что въехала в неё.
                if (ahead < half + 3f || ahead > length - half - 3f) return false;
                // «Безумие»: успеть среагировать не обязательно — лишь бы предметы не появились внутри машины.
                if (Wild) continue;
                float fast = Mathf.Max(r.Speed, FairnessLimits.AssumedMinSpeed) * 1.15f;
                if ((ahead - half) / fast < warn + FairnessLimits.MinReactionSeconds) return false;
            }
            return true;
        }

        /// Хотя бы одна машина проедет зону, пока событие идёт: событие не пропадает впустую.
        public bool IsRelevant(EventDefinition definition, float progress, IReadOnlyList<RacerState> racers)
        {
            float half = definition.ZoneLength * 0.5f;
            float from = definition.WarningSeconds;
            float to = definition.WarningSeconds + definition.ActiveSeconds + RelevanceSlack;
            foreach (var r in racers)
            {
                float t = (Ahead(r.Progress, progress) - half) / Mathf.Max(r.Speed, 8f);
                if (t >= from && t <= to) return true;
            }
            return false;
        }

        readonly List<EventDefinition> stacked = new List<EventDefinition>(4);

        /// Место занято: зона пересекается с идущими событиями (с зазором ZoneGap), кроме наложения по правилам
        /// хаоса — слой поверх участка или задуманная пара, вместе не шире ChaosRules.MaxBlocked (проезд остаётся).
        public bool Conflicts(EventDefinition definition, float progress, IReadOnlyList<EventZone> active)
        {
            if (active == null) return false;
            stacked.Clear();
            foreach (var z in active)
            {
                float d = Mathf.Min(Ahead(z.Progress, progress), Ahead(progress, z.Progress));
                float touch = (definition.ZoneLength + z.Length) * 0.5f;
                if (d >= touch + Gap) continue;
                // Наложение — только по-настоящему на тот же участок (не «краем»): иначе сцена нечитаема.
                if (z.Definition == null || d > Mathf.Max(touch - Mathf.Min(definition.ZoneLength, z.Length) * 0.5f, 0f)) return true;
                stacked.Add(z.Definition);
            }
            return stacked.Count > 0 && !ChaosRules.CanStackAll(definition, stacked, RoadHalf, Wild);
        }

        /// Зона пересекается с идущими событиями (с зазором ZoneGap).
        public bool Overlaps(float progress, float zoneLength, IReadOnlyList<EventZone> active)
        {
            if (active == null) return false;
            foreach (var z in active)
            {
                float d = Mathf.Min(Ahead(z.Progress, progress), Ahead(progress, z.Progress));
                if (d < (zoneLength + z.Length) * 0.5f + FairnessLimits.ZoneGap) return true;
            }
            return false;
        }

        /// Участок зоны достаточно прямой.
        public bool IsStraight(float progress, float zoneLength)
        {
            int steps = 6;
            float span = (zoneLength + 20f) / length;
            for (int i = 0; i <= steps; i++)
                if (curvature(progress - span * 0.5f + span * i / steps) > StraightCurvature) return false;
            return true;
        }
    }
}
