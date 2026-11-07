using System;
using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Events
{
    public enum EventKind { Bad, Good }

    public enum EventClass { Light, Medium, Heavy }

    /// Хаос: «выключен», «умеренно», «много», «безумие» (стандарт с v5 «MAXIMUM CHAOS»: событие каждые доли
    /// секунды, рядом с машинами, мощные подбросы и кувырки) и «абсурд» — ещё безумнее. Один переключатель
    /// в настройках и в лобби.
    public enum ChaosLevel { Off, Moderate, Many, Mayhem, Absurd }

    /// Где хаос-трюк (v5) появляется относительно машины, ради которой он запущен.
    public enum GagPlace
    {
        /// Прямо под колёсами там, где машина окажется к концу предупреждения.
        Under,
        /// На её полосе в нескольких метрах впереди.
        Ahead,
        /// На обочине рядом с ней (рука, вентилятор, пушка, кувалда).
        Side,
        /// Над дорогой впереди (магнит, утиный дождь).
        Above,
        /// Вокруг самой машины (гравитационный сбой, НЛО, безумный руль).
        Car,
    }

    /// Масштаб события: локальная неприятность, крупное препятствие, сцена на целый участок трассы.
    public enum EventScale { Small, Large, Scene }

    /// Категории взаимодействия: по ним сборщик комбинаций (ChaosRules) решает, что с чем сочетается.
    [Flags]
    public enum EventTags
    {
        None = 0,
        /// Катится и скатывается (мячи, бочки).
        Rolling = 1,
        /// Взрывается (мины, ракеты, бочки).
        Explosive = 2,
        /// Меняет покрытие (лёд, желе, банан): накладывается на участок с другим событием.
        Surface = 4,
        /// Движущиеся конструкции (молоты, карусель, перчатки, стены).
        Moving = 8,
        /// Подбрасывает (батуты, трамплин, невесомость).
        Launch = 16,
        /// Создаёт физические тела (занимает бюджет физики).
        Bodies = 32,
        /// Перекрывает полосы (дорожные работы, стены, развилка).
        Lanes = 64,
        /// Прилетает с неба.
        Sky = 128,
        /// Ускоряет (рывок).
        Boost = 256,
    }

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
        /// Длина зоны вдоль трассы, м: по ней проверяется, что зоны не накладываются и никто не окажется внутри до конца предупреждения.
        public float ZoneLength = 8f;
        /// Событию нужен прямой участок (трамплин, полоса ускорения, попутный ветер).
        public bool NeedsStraight;

        /// «Удар»: предупреждение держится, пока игрок, ради которого событие поставлено, не подъедет
        /// на TriggerLead секунд — мяч, стадо, ракета срабатывают, когда он рядом, а не вдали.
        public bool Trigger;
        public float TriggerLead = 1f;

        /// «Зона»: участок (лужа, столбы, ящики, трамплин) держится, пока этот игрок его не проедет.
        public bool Sustain;

        // ---------- хаос v4: категории и совместимость ----------

        public EventScale Scale = EventScale.Small;
        public EventTags Tags;

        /// Доля ширины дороги, перекрытая препятствиями в худшем сечении (0..1). Наложенные события вместе
        /// не перекрывают больше ChaosRules.MaxBlocked — проезд остаётся всегда.
        public float Blocks = 0.3f;

        /// Слой поверх участка (покрытие, невесомость, ветер, бонусы): может делить участок с другим событием.
        public bool Overlay;

        /// Сколько физических тел занимает (бюджет физики Хаоса).
        public int Bodies;

        /// С какого Хаоса появляется: редкие и самые необычные — только в «Безумии».
        public ChaosLevel MinChaos = ChaosLevel.Moderate;

        /// С какого накала появляется (0..1): разнообразие растёт от круга к кругу.
        public float MinLevel;

        /// Только на дорогах не уже этой полуширины, м (развилке нужно место на две полосы с каждой стороны).
        public float MinRoadHalf;

        // ---------- хаос v5: трюки ----------

        /// Хаос-трюк: короткое мгновенное происшествие рядом с машиной (пружина, рука, наковальня, НЛО).
        /// Идёт своим потоком — каждые доли секунды, поверх обычных событий; предупреждение короткое.
        public bool Gag;

        /// Где трюк появляется относительно машины.
        public GagPlace Place;

        /// Сколько экземпляров события заранее лежит в сцене (пул).
        public int Copies = 4;

        // ---------- v6: шоу-события ----------

        /// Шоу-событие: большое представление рядом с участником (Верити, EL DRIFTO BUS). Идёт третьим потоком —
        /// раз в полминуты в «безумии» и чаще в «абсурде», не входит в баланс весов обычных событий и трюков
        /// (вес — только среди шоу-событий). TriggerLead — за сколько секунд пути участника ставится место.
        public bool Spectacle;

        public bool IsHeavy => Class == EventClass.Heavy;
        public bool IsGood => Kind == EventKind.Good;
        public bool Has(EventTags tag) => (Tags & tag) != 0;

        /// Можно ли это событие сейчас: Хаос и накал.
        /// Хаос выбирает только частоту: на любом включённом уровне доступны все события (MinChaos не ограничивает).
        public bool Unlocked(ChaosLevel chaos, float level) => chaos != ChaosLevel.Off && level >= MinLevel - 1e-4f;

        /// Помещается ли событие на дорогу этой полуширины.
        public bool Fits(float roadHalf) => roadHalf >= MinRoadHalf - 1e-3f;
    }

    /// Единственное место для весов и настроек событий: частота, число одновременных событий, бюджет физики
    /// и поток трюков по уровням Хаоса (сила ударов — ChaosPower).
    [CreateAssetMenu(menuName = "RandomCar/Event Settings")]
    public sealed class EventSettings : ScriptableObject
    {
        public List<EventDefinition> Events = new List<EventDefinition>();

        /// Версия правил: 2 — события в зонах трассы; 3 — события впереди игроков, ждут подъезда и держатся,
        /// пока игрок не проедет, накал растёт от круга к кругу; 4 — хаос v4: «Безумие», категории, комбинации,
        /// физические объекты и бюджет физики; 5–6 — MAXIMUM CHAOS; 7 — шоу-события на всех уровнях Хаоса, ГИПЕРГАМИЯ.
        public const int CurrentVersion = 7;
        public int Version;

        /// Интервал между началами событий для «умеренно» на первом круге, с (дальше его сжимает накал, EventIntensity):
        /// за гонку в среднем одно интересное событие в 5–8 с.
        public Vector2 ModerateInterval = new Vector2(6.5f, 8.5f);

        /// «Много»: на первом круге ~3.3–4.5 с, к финалу ~2.3–3 с.
        public Vector2 ManyInterval = new Vector2(3.2f, 4.4f);

        /// «Безумие» (стандарт, v5.1 — играбельный хаос): большое событие каждые ~2 с с первой секунды,
        /// плюс поток трюков (MayhemGagInterval) — вместе происшествие примерно раз в 0.6 с.
        public Vector2 MayhemInterval = new Vector2(1.8f, 2.6f);

        /// «Абсурд»: всё сразу — происшествие примерно каждые 0.2–0.25 с.
        public Vector2 AbsurdInterval = new Vector2(0.9f, 1.4f);

        /// Сколько событий может идти одновременно (накал добавляет ещё одно).
        public int ModerateMaxActive = 3;
        public int ManyMaxActive = 5;
        public int MayhemMaxActive = 8;
        public int AbsurdMaxActive = 14;

        /// Сколько физических тел событий (мячи, бочки, утки) может быть на трассе одновременно.
        public int ModerateMaxBodies = 10;
        public int ManyMaxBodies = 20;
        public int MayhemMaxBodies = 36;
        public int AbsurdMaxBodies = 60;

        /// Поток хаос-трюков (v5): интервал между трюками, с. «Безумие» — около 0.9 с, «абсурд» — около 0.3 с.
        public Vector2 ModerateGagInterval = new Vector2(4f, 6f);
        public Vector2 ManyGagInterval = new Vector2(1f, 1.6f);
        public Vector2 MayhemGagInterval = new Vector2(0.7f, 1.1f);
        public Vector2 AbsurdGagInterval = new Vector2(0.22f, 0.42f);

        /// Сколько трюков может идти одновременно.
        public int ModerateMaxGags = 2;
        public int ManyMaxGags = 6;
        public int MayhemMaxGags = 8;
        public int AbsurdMaxGags = 22;

        /// Шоу-события (Верити, EL DRIFTO BUS, ГИПЕРГАМИЯ) — на всех уровнях Хаоса, кроме «выключен» (решение
        /// пользователя 2026-11-06; Хаос — только частота): интервал между началами, первое — через сколько секунд
        /// после старта, сколько сразу. В «умеренно» за гонку на «Пустыне» (~2.8 мин) — около трёх шоу.
        public Vector2 ModerateSpectacleInterval = new Vector2(45f, 60f);
        public Vector2 ManySpectacleInterval = new Vector2(34f, 46f);
        public Vector2 MayhemSpectacleInterval = new Vector2(26f, 38f);
        public Vector2 AbsurdSpectacleInterval = new Vector2(13f, 19f);
        public float ModerateSpectacleFirst = 15f;
        public float ManySpectacleFirst = 12f;
        public float SpectacleFirst = 9f;
        public float AbsurdSpectacleFirst = 6f;

        public bool SpectaclesOn(ChaosLevel chaos) => chaos != ChaosLevel.Off;

        public Vector2 SpectacleInterval(ChaosLevel chaos) => chaos switch
        {
            ChaosLevel.Moderate => ModerateSpectacleInterval,
            ChaosLevel.Many => ManySpectacleInterval,
            ChaosLevel.Absurd => AbsurdSpectacleInterval,
            _ => MayhemSpectacleInterval,
        };

        /// Через сколько секунд после старта первое шоу.
        public float FirstSpectacle(ChaosLevel chaos) => chaos switch
        {
            ChaosLevel.Moderate => ModerateSpectacleFirst,
            ChaosLevel.Many => ManySpectacleFirst,
            ChaosLevel.Absurd => AbsurdSpectacleFirst,
            _ => SpectacleFirst,
        };

        public int MaxSpectacles(ChaosLevel chaos) => chaos == ChaosLevel.Absurd ? 2 : 1;

        /// Первые секунды после старта без событий: разгон и разъезд со стартовой решётки («умеренно», «много»).
        public float StartGrace = 3f;

        /// В «безумии» и «абсурде» хаос начинается почти сразу после старта, с.
        public float ChaosStartGrace = 0.5f;

        public float Grace(ChaosLevel chaos) => chaos >= ChaosLevel.Mayhem ? ChaosStartGrace : StartGrace;

        /// Сколько тяжёлых событий может идти одновременно.
        public static int MaxHeavy(ChaosLevel chaos) => chaos switch
        {
            ChaosLevel.Absurd => 4,
            ChaosLevel.Mayhem => 3,
            _ => 1,
        };

        public int MaxActive(ChaosLevel chaos) => chaos switch
        {
            ChaosLevel.Absurd => AbsurdMaxActive,
            ChaosLevel.Mayhem => MayhemMaxActive,
            ChaosLevel.Many => ManyMaxActive,
            ChaosLevel.Moderate => ModerateMaxActive,
            _ => 0,
        };

        public int MaxBodies(ChaosLevel chaos) => chaos switch
        {
            ChaosLevel.Absurd => AbsurdMaxBodies,
            ChaosLevel.Mayhem => MayhemMaxBodies,
            ChaosLevel.Many => ManyMaxBodies,
            ChaosLevel.Moderate => ModerateMaxBodies,
            _ => 0,
        };

        public Vector2 Interval(ChaosLevel chaos) => chaos switch
        {
            ChaosLevel.Absurd => AbsurdInterval,
            ChaosLevel.Mayhem => MayhemInterval,
            ChaosLevel.Many => ManyInterval,
            _ => ModerateInterval,
        };

        public Vector2 GagInterval(ChaosLevel chaos) => chaos switch
        {
            ChaosLevel.Absurd => AbsurdGagInterval,
            ChaosLevel.Mayhem => MayhemGagInterval,
            ChaosLevel.Many => ManyGagInterval,
            _ => ModerateGagInterval,
        };

        public int MaxGags(ChaosLevel chaos) => chaos switch
        {
            ChaosLevel.Absurd => AbsurdMaxGags,
            ChaosLevel.Mayhem => MayhemMaxGags,
            ChaosLevel.Many => ManyMaxGags,
            ChaosLevel.Moderate => ModerateMaxGags,
            _ => 0,
        };

        /// Набор событий v1 (см. Docs/EventLibrary.md). Плохие и хорошие в сумме весят одинаково.
        public static List<EventDefinition> DefaultEvents() => new List<EventDefinition>
        {
            // «Удар» (Trigger): срабатывает, когда игрок в TriggerLead секундах; «зона» (Sustain): держится, пока не проедет.
            // Tag(...): масштаб, категории, доля перекрытой ширины, слой поверх участка, физические тела, с какого Хаоса и накала.
            Tag(Sustain(Def("worms", "Черви", "Черви под землёй!", EventKind.Bad, EventClass.Medium, 1.00f, 2.0f, 3.0f, 12f)), EventScale.Small, EventTags.Moving, 0.35f),
            Tag(Strike(Def("rocket", "Ракета", "Ракета летит!", EventKind.Bad, EventClass.Heavy, 0.75f, 2.0f, 1.5f, 8f), 0.5f), EventScale.Large, EventTags.Explosive | EventTags.Sky, 0.4f),
            Tag(Sustain(Strike(Def("tank", "Танк", "Танк целится!", EventKind.Bad, EventClass.Heavy, 0.75f, 2.0f, 4.0f, 10f), 0.6f)), EventScale.Large, EventTags.Explosive, 0.4f),
            Tag(Strike(Def("sheep", "Стадо овец", "Овцы на дороге!", EventKind.Bad, EventClass.Medium, 0.75f, 2.5f, 3.0f, 12f), 1.2f), EventScale.Large, EventTags.Moving, 0.5f),
            Tag(Strike(Def("ball", "Гигантский мяч", "Мяч катится!", EventKind.Bad, EventClass.Medium, 0.75f, 2.5f, 3.0f, 10f), 1.2f), EventScale.Small, EventTags.Rolling, 0.3f),
            Tag(Sustain(Def("banana", "Банан", "Скользко!", EventKind.Bad, EventClass.Light, 0.75f, 1.5f, 4.0f, 8f)), EventScale.Small, EventTags.Surface, 0f, true),
            Tag(Sustain(Def("pillars", "Каменные столбы", "Земля трескается!", EventKind.Bad, EventClass.Medium, 0.50f, 2.0f, 3.0f, 10f)), EventScale.Large, EventTags.Lanes, 0.5f),
            Tag(Strike(Def("wind", "Порыв ветра", "Боковой ветер!", EventKind.Bad, EventClass.Light, 0.75f, 2.0f, 1.2f, 12f), 0.4f), EventScale.Small, EventTags.None, 0f, true),
            Tag(Sustain(Def("ramp", "Трамплин", "Трамплин впереди!", EventKind.Good, EventClass.Medium, 1.50f, 1.5f, 4.0f, 9f, true)), EventScale.Small, EventTags.Launch, 0.3f),
            Tag(Sustain(Def("crate", "Бонусные ящики", "Ящики с ускорением!", EventKind.Good, EventClass.Light, 1.50f, 1.5f, 4.0f, 9f)), EventScale.Small, EventTags.Boost, 0f, true),
            Tag(Sustain(Def("boostpad", "Ускоритель", "Полоса ускорения!", EventKind.Good, EventClass.Light, 1.50f, 1.5f, 4.0f, 11f, true)), EventScale.Small, EventTags.Boost, 0f, true),
            Tag(Sustain(Def("tailwind", "Попутный ветер", "Попутный ветер!", EventKind.Good, EventClass.Light, 1.50f, 1.5f, 4.0f, 28f, true)), EventScale.Large, EventTags.Boost, 0f, true),
            // v3: события-участки. Обстрел — слалом между ударами, перекрытие — шикана из дорожных работ,
            // дорожка ускорения — три рывка подряд на одной полосе. Плохие +1.3 = хорошие +1.3 (баланс 50/50).
            Tag(Sustain(Strike(Def("barrage", "Обстрел", "Обстрел впереди!", EventKind.Bad, EventClass.Heavy, 0.55f, 2.5f, 5.0f, 72f), 1.6f)), EventScale.Scene, EventTags.Explosive | EventTags.Sky, 0.35f),
            Tag(Sustain(Def("roadblock", "Перекрытие", "Дорожные работы — объезд!", EventKind.Bad, EventClass.Medium, 0.75f, 2.0f, 3.0f, 54f)), EventScale.Large, EventTags.Lanes, 0.42f),
            Tag(Sustain(Def("boostchain", "Дорожка ускорения", "Три ускорителя подряд!", EventKind.Good, EventClass.Light, 1.30f, 1.5f, 4.0f, 40f, true)), EventScale.Large, EventTags.Boost, 0f, true),
            // Только замена «Ракеты» на картах, где она указана (MapDefinition.EventSubstitutions): свой вес — 0,
            // выпадает с весом и правилами «Ракеты», баланс 50/50 не меняется.
            Tag(Sustain(Strike(Def("debris", "Обломки", "Обломки падают!", EventKind.Bad, EventClass.Heavy, 0f, 2.5f, 4.0f, 12f), 0.5f)), EventScale.Large, EventTags.Explosive | EventTags.Sky, 0.4f),
            Tag(Sustain(Strike(Def("rocketbarrage", "Ракетный удар", "Ракеты по дороге!", EventKind.Bad, EventClass.Heavy, 0f, 2.5f, 5.0f, 72f), 1.6f)), EventScale.Scene, EventTags.Explosive | EventTags.Sky, 0.35f),

            // ---------- хаос v4 ----------
            // Плохие +5.1 = хорошие +5.1: баланс 50/50 сохраняется. События, закрытые Хаосом или накалом, планировщик
            // исключает и растягивает веса оставшихся своей стороны (как с тяжёлыми).
            Tag(Sustain(Strike(Def("megaballs", "Шары-гиганты", "Шары катятся!", EventKind.Bad, EventClass.Medium, 0.70f, 2.5f, 6.0f, 60f), 2.4f)), EventScale.Large, EventTags.Rolling, 0.2f, false, 5),
            Tag(Sustain(Def("mines", "Минное поле", "Мины на дороге!", EventKind.Bad, EventClass.Medium, 0.70f, 2.0f, 4.0f, 46f)), EventScale.Large, EventTags.Explosive, 0.5f),
            Tag(Sustain(Def("hammers", "Молоты", "Молоты качаются!", EventKind.Bad, EventClass.Medium, 0.50f, 2.0f, 5.0f, 40f)), EventScale.Large, EventTags.Moving | EventTags.Lanes, 0.5f, false, 0, ChaosLevel.Moderate, 0.25f),
            Tag(Sustain(Def("spinner", "Карусель", "Карусель крутится!", EventKind.Bad, EventClass.Medium, 0.40f, 2.0f, 5.0f, 26f)), EventScale.Large, EventTags.Moving, 0.5f, false, 0, ChaosLevel.Moderate, 0.3f),
            Tag(Sustain(Strike(Def("rollers", "Бочкопад", "Бочки катятся!", EventKind.Bad, EventClass.Medium, 0.50f, 2.5f, 6.0f, 50f), 2.2f)), EventScale.Large, EventTags.Rolling | EventTags.Explosive, 0.25f, false, 6),
            Tag(Sustain(Def("walls", "Двигающиеся стены", "Стены сдвигаются!", EventKind.Bad, EventClass.Heavy, 0.45f, 2.5f, 5.0f, 60f)), EventScale.Scene, EventTags.Moving | EventTags.Lanes, 0.66f, false, 0, ChaosLevel.Moderate, 0.45f),
            Tag(Sustain(Def("kickers", "Боксёрские перчатки", "Перчатки бьют!", EventKind.Bad, EventClass.Medium, 0.45f, 2.0f, 4.0f, 48f)), EventScale.Large, EventTags.Moving, 0.3f),
            Tag(Sustain(Def("ice", "Каток", "Лёд на дороге!", EventKind.Bad, EventClass.Light, 0.50f, 1.5f, 4.0f, 50f)), EventScale.Large, EventTags.Surface, 0f, true),
            Tag(Sustain(Def("jelly", "Желейная дорога", "Дорога-желе!", EventKind.Bad, EventClass.Light, 0.35f, 1.5f, 4.0f, 36f)), EventScale.Small, EventTags.Surface | EventTags.Launch, 0f, true),
            Wide(Tag(Sustain(Def("fork", "Развилка", "Развилка: выбирай!", EventKind.Bad, EventClass.Medium, 0.35f, 2.5f, 5.0f, 70f, true)), EventScale.Scene, EventTags.Lanes | EventTags.Explosive | EventTags.Boost, 0.5f, false, 0, ChaosLevel.Moderate, 0.35f), 8f),
            Tag(Sustain(Strike(Def("bigball", "Шар-переросток", "ОГРОМНЫЙ шар!", EventKind.Bad, EventClass.Heavy, 0.20f, 3.0f, 8.0f, 90f), 3.2f)), EventScale.Scene, EventTags.Rolling, 0.5f, false, 1, ChaosLevel.Mayhem, 0.2f),
            Tag(Sustain(Def("bouncers", "Батуты", "Батуты!", EventKind.Good, EventClass.Medium, 1.60f, 1.5f, 4.0f, 40f, true)), EventScale.Large, EventTags.Launch, 0.25f),
            Tag(Sustain(Def("lowgrav", "Невесомость", "Невесомость!", EventKind.Good, EventClass.Medium, 1.20f, 1.5f, 5.0f, 80f, true)), EventScale.Scene, EventTags.Launch, 0f, true, 0, ChaosLevel.Many, 0.2f),
            Tag(Sustain(Def("shrink", "Мини-ворота", "Ворота-уменьшатель!", EventKind.Good, EventClass.Light, 1.00f, 1.5f, 4.0f, 20f)), EventScale.Small, EventTags.Boost, 0f, true),
            Tag(Sustain(Def("rings", "Кольца-рывки", "Кольца ускорения!", EventKind.Good, EventClass.Light, 1.30f, 1.5f, 4.0f, 50f)), EventScale.Large, EventTags.Boost | EventTags.Launch, 0f, true),

            // ---------- хаос v5: трюки (свой поток, каждые доли секунды, рядом с машиной) ----------
            // Предупреждение короткое (0.3–0.9 с): увернуться можно не всегда — так и задумано.
            Gag(Def("spring", "Пружина", "Пружина!", EventKind.Good, EventClass.Light, 1.2f, 0.45f, 1.2f, 6f), GagPlace.Under, EventTags.Launch, ChaosLevel.Moderate, 6),
            Gag(Def("popmine", "Мина-сюрприз", "Пик-пик!", EventKind.Bad, EventClass.Light, 1.3f, 0.4f, 2.0f, 6f), GagPlace.Ahead, EventTags.Explosive, ChaosLevel.Moderate, 6),
            Gag(Def("gum", "Жвачка", "Жвачка!", EventKind.Good, EventClass.Light, 0.8f, 0.4f, 4.0f, 8f), GagPlace.Ahead, EventTags.Surface, ChaosLevel.Moderate, 4),
            Gag(Def("oil", "Масляное пятно", "Масло!", EventKind.Bad, EventClass.Light, 0.8f, 0.4f, 4.0f, 10f), GagPlace.Ahead, EventTags.Surface, ChaosLevel.Moderate, 4),
            Gag(Def("crazy", "Безумный руль", "Что с рулём?!", EventKind.Bad, EventClass.Light, 0.5f, 0.3f, 3.2f, 4f), GagPlace.Car, EventTags.None, ChaosLevel.Moderate, 6),
            Gag(Def("hand", "Гигантская рука", "Рука!", EventKind.Bad, EventClass.Medium, 1.1f, 0.6f, 1.4f, 14f), GagPlace.Side, EventTags.Moving, ChaosLevel.Many, 4),
            Gag(Def("mallet", "Кувалда", "Кувалда!", EventKind.Bad, EventClass.Medium, 1.1f, 0.65f, 1.6f, 10f), GagPlace.Side, EventTags.Moving | EventTags.Launch, ChaosLevel.Many, 4),
            Gag(Def("anvil", "Наковальня", "Сверху!", EventKind.Bad, EventClass.Medium, 0.9f, 0.9f, 3.0f, 8f), GagPlace.Under, EventTags.Sky, ChaosLevel.Many, 4),
            Gag(Def("fan", "Вентилятор", "Ураган-вентилятор!", EventKind.Bad, EventClass.Medium, 0.8f, 0.6f, 3.0f, 34f), GagPlace.Side, EventTags.None, ChaosLevel.Many, 3),
            Gag(Def("magnet", "Магнит", "Магнит!", EventKind.Bad, EventClass.Medium, 0.8f, 0.7f, 3.2f, 24f), GagPlace.Above, EventTags.Sky, ChaosLevel.Many, 3),
            Gag(Def("gravflip", "Гравитационный сбой", "Гравитация сбоит!", EventKind.Bad, EventClass.Medium, 0.7f, 0.5f, 1.8f, 30f), GagPlace.Car, EventTags.Launch, ChaosLevel.Many, 3),
            Gag(Def("ducks", "Утиный дождь", "Утки!", EventKind.Bad, EventClass.Medium, 0.8f, 0.6f, 6.0f, 30f), GagPlace.Above, EventTags.Rolling | EventTags.Sky, ChaosLevel.Many, 3, 5),
            Gag(Def("cannon", "Пушка", "Пушка!", EventKind.Bad, EventClass.Medium, 0.8f, 0.8f, 3.0f, 14f), GagPlace.Side, EventTags.Rolling | EventTags.Explosive, ChaosLevel.Many, 3, 1),
            Gag(Def("ufo", "НЛО", "НЛО!", EventKind.Bad, EventClass.Medium, 0.6f, 1.2f, 3.6f, 12f), GagPlace.Car, EventTags.Sky | EventTags.Launch, ChaosLevel.Many, 3),
            Gag(Def("tornado", "Смерч", "Смерч!", EventKind.Bad, EventClass.Medium, 0.6f, 0.8f, 4.0f, 26f), GagPlace.Ahead, EventTags.Moving | EventTags.Launch, ChaosLevel.Mayhem, 3),

            // ---------- шоу-события (свой поток, все уровни Хаоса, кроме «выключен») ----------
            // Верити: огромный смайлик прыгает по трассе рядом с машинами, приземления — ударные волны.
            Show(Def("verity", "ВЕРИТИ", "Верити прыгает!", EventKind.Bad, EventClass.Medium, 1.0f, 1.6f, 15.0f, 60f), 0.4f, EventTags.Launch | EventTags.Moving, 0),
            // EL DRIFTO BUS: крик, автобус вылетает с обочины и дрифтует боком через трассу, в конце — в кювет.
            Show(Def("eldrifto", "EL DRIFTO BUS", "EL DRIFTO!", EventKind.Bad, EventClass.Medium, 1.0f, 2.2f, 14.0f, 70f), 1.0f, EventTags.Moving | EventTags.Bodies, 1),
            // ГИПЕРГАМИЯ: на дороге вырастает огромная картинка; коснулся — она на весь экран на 10 с под музыку пользователя.
            // «Зона»: стоит, пока участник, ради которого появилась, не проедет мимо.
            Show(Def("hypergamy", "ГИПЕРГАМИЯ", "Не трогай картинку!", EventKind.Bad, EventClass.Light, 1.0f, 1.5f, 8.0f, 30f), 1.5f, EventTags.None, 0, sustain: true),
        };

        static EventDefinition Show(EventDefinition d, float leadSeconds, EventTags tags, int bodies, bool sustain = false)
        {
            d.Spectacle = true;
            d.Sustain = sustain;
            d.TriggerLead = leadSeconds;
            d.Copies = 1;
            d.Scale = EventScale.Scene;
            d.Tags = tags;
            d.Bodies = bodies;
            d.Blocks = 0f;
            d.Overlay = true;
            d.MinChaos = ChaosLevel.Moderate;
            return d;
        }

        static EventDefinition Gag(EventDefinition d, GagPlace place, EventTags tags, ChaosLevel minChaos, int copies, int bodies = 0)
        {
            d.Gag = true;
            d.Place = place;
            d.Copies = copies;
            d.Scale = EventScale.Small;
            d.Tags = tags | (bodies > 0 ? EventTags.Bodies : EventTags.None);
            d.Bodies = bodies;
            d.Blocks = 0f;
            d.Overlay = true;
            d.MinChaos = minChaos;
            return d;
        }

        static EventDefinition Wide(EventDefinition d, float minRoadHalf)
        {
            d.MinRoadHalf = minRoadHalf;
            return d;
        }

        static EventDefinition Tag(EventDefinition d, EventScale scale, EventTags tags, float blocks, bool overlay = false,
                                   int bodies = 0, ChaosLevel minChaos = ChaosLevel.Moderate, float minLevel = 0f)
        {
            d.Scale = scale;
            d.Tags = tags | (bodies > 0 ? EventTags.Bodies : EventTags.None);
            d.Blocks = blocks;
            d.Overlay = overlay;
            d.Bodies = bodies;
            d.MinChaos = minChaos;
            d.MinLevel = minLevel;
            return d;
        }

        static EventDefinition Strike(EventDefinition d, float lead)
        {
            d.Trigger = true;
            d.TriggerLead = lead;
            return d;
        }

        static EventDefinition Sustain(EventDefinition d)
        {
            d.Sustain = true;
            return d;
        }

        static EventDefinition Def(string id, string title, string warning, EventKind kind, EventClass cls,
                                   float weight, float warn, float active, float zone, bool straight = false) => new EventDefinition
        {
            Id = id, Title = title, Warning = warning, Kind = kind, Class = cls,
            Weight = weight, WarningSeconds = warn, ActiveSeconds = active, ZoneLength = zone, NeedsStraight = straight,
        };
    }
}
