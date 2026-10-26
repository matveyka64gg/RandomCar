using System;
using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Events
{
    public enum EventKind { Bad, Good }

    public enum EventClass { Light, Medium, Heavy }

    public enum ChaosLevel { Off, Moderate, Many }

    /// Описание события: тексты, тип, вес и длительности стадий.
    [Serializable]
    public sealed class EventDefinition
    {
        public string Id;
        public string Title;
        public string Warning;
        public EventKind Kind;
        public EventClass Class;
        public float Weight = 1f;
        public float WarningSeconds = 2f;
        public float ActiveSeconds = 3f;

        public bool IsHeavy => Class == EventClass.Heavy;
        public bool IsGood => Kind == EventKind.Good;
    }

    /// Единственное место для весов и настроек событий. Хаос меняет только частоту.
    [CreateAssetMenu(menuName = "RandomCar/Event Settings")]
    public sealed class EventSettings : ScriptableObject
    {
        public List<EventDefinition> Events = new List<EventDefinition>();

        /// Интервал между событиями для «умеренно», с.
        public Vector2 ModerateInterval = new Vector2(20f, 30f);

        /// Интервал между событиями для «много», с.
        public Vector2 ManyInterval = new Vector2(8f, 12f);

        /// Первые секунды после старта без событий.
        public float StartGrace = 6f;

        /// Набор событий v1 (см. Docs/EventLibrary.md). Плохие и хорошие в сумме весят одинаково.
        public static List<EventDefinition> DefaultEvents() => new List<EventDefinition>
        {
            Def("worms", "Черви", "Черви под землёй!", EventKind.Bad, EventClass.Medium, 1.00f, 2.0f, 3.0f),
            Def("rocket", "Ракета", "Ракета летит!", EventKind.Bad, EventClass.Heavy, 0.75f, 2.0f, 1.5f),
            Def("tank", "Танк", "Танк целится!", EventKind.Bad, EventClass.Heavy, 0.75f, 2.0f, 4.0f),
            Def("sheep", "Стадо овец", "Овцы на дороге!", EventKind.Bad, EventClass.Medium, 0.75f, 2.5f, 3.0f),
            Def("ball", "Гигантский мяч", "Мяч катится!", EventKind.Bad, EventClass.Medium, 0.75f, 2.5f, 3.0f),
            Def("banana", "Банан", "Скользко!", EventKind.Bad, EventClass.Light, 0.75f, 1.5f, 4.0f),
            Def("pillars", "Каменные столбы", "Земля трескается!", EventKind.Bad, EventClass.Medium, 0.50f, 2.0f, 3.0f),
            Def("wind", "Порыв ветра", "Боковой ветер!", EventKind.Bad, EventClass.Light, 0.75f, 2.0f, 1.2f),
            Def("ramp", "Трамплин", "Трамплин впереди!", EventKind.Good, EventClass.Medium, 1.50f, 1.5f, 4.0f),
            Def("crate", "Бонусные ящики", "Ящики с ускорением!", EventKind.Good, EventClass.Light, 1.50f, 1.5f, 4.0f),
            Def("boostpad", "Ускоритель", "Полоса ускорения!", EventKind.Good, EventClass.Light, 1.50f, 1.5f, 4.0f),
            Def("tailwind", "Попутный ветер", "Попутный ветер!", EventKind.Good, EventClass.Light, 1.50f, 1.5f, 2.0f),
        };

        static EventDefinition Def(string id, string title, string warning, EventKind kind, EventClass cls,
                                   float weight, float warn, float active) => new EventDefinition
        {
            Id = id, Title = title, Warning = warning, Kind = kind, Class = cls,
            Weight = weight, WarningSeconds = warn, ActiveSeconds = active,
        };
    }
}
