using System;
using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Events
{
    /// Как связать событие комбинации с предыдущим: поверх того же участка или сразу за ним.
    public enum ComboLink { Anchor, Stack, After }

    /// Шаг сцены: какое событие и как оно связано с предыдущим.
    public readonly struct ComboStep
    {
        public readonly string Id;
        public readonly ComboLink Link;

        public ComboStep(string id, ComboLink link)
        {
            Id = id;
            Link = link;
        }
    }

    /// Сцена-рецепт: несколько событий, которые вместе дают смешную ситуацию («Боулинг на минном поле»).
    public sealed class ChaosRecipe
    {
        public string Id;
        public string Title;
        public ComboStep[] Steps;
        public ChaosLevel MinChaos = ChaosLevel.Moderate;
        public float MinLevel;
        /// Сцена последнего круга.
        public bool Finale;
    }

    /// Правила совместимости событий хаоса и рецепты сцен. Без Unity-объектов (тестируется в EditMode).
    /// Главное правило: наложенные на один участок события вместе перекрывают не больше MaxBlocked ширины
    /// дороги — проезд остаётся всегда; два тяжёлых, два покрытия и два «перекрытия полос» не накладываются.
    public static class ChaosRules
    {
        /// Наложенные события перекрывают не больше этой доли ширины дороги (в худшем сечении).
        public const float MaxBlocked = 0.72f;

        /// Зазор между событиями «сразу за» в комбинации, м: перекладка успевает, но передышки нет.
        public const float ChainGap = 10f;

        /// Дорога уже этой полуширины — «узкая»: размах молота или карусели в метрах занимает большую часть ширины,
        /// поэтому два предмета на один участок не накладываются, только слои (покрытие, невесомость, бонусы).
        public const float NarrowRoadHalf = 8f;

        /// «Безумие» (v5): наложенные события вместе могут перекрыть почти всю дорогу — проезд не гарантирован.
        public const float WildMaxBlocked = 1f;

        /// Можно ли поставить b поверх того же участка, где идёт a (roadHalf — полуширина дороги трассы).
        /// wild — «безумие»: накладывается почти всё, кроме того, что физически не помещается в одно место
        /// (два перекрытия полос, два покрытия, два больших движущихся механизма).
        public static bool CanStack(EventDefinition a, EventDefinition b, float roadHalf = 11f, bool wild = false)
        {
            if (wild)
            {
                if (a == null || b == null || a.Id == b.Id) return false;
                if (a.Has(EventTags.Lanes) && b.Has(EventTags.Lanes)) return false;
                if (a.Has(EventTags.Surface) && b.Has(EventTags.Surface)) return false;
                if (a.Has(EventTags.Moving) && b.Has(EventTags.Moving) && a.Blocks >= 0.45f && b.Blocks >= 0.45f) return false;
                return a.Blocks + b.Blocks <= WildMaxBlocked + 1e-4f;
            }
            if (roadHalf < NarrowRoadHalf && a != null && b != null && !a.Overlay && !b.Overlay) return false;
            if (a == null || b == null || a.Id == b.Id) return false;
            if (a.IsHeavy && b.IsHeavy) return false;
            if (a.Blocks + b.Blocks > MaxBlocked) return false;
            if (a.Has(EventTags.Surface) && b.Has(EventTags.Surface)) return false;
            if (a.Has(EventTags.Lanes) && b.Has(EventTags.Lanes)) return false;
            if (a.NeedsStraight && b.Has(EventTags.Lanes) || b.NeedsStraight && a.Has(EventTags.Lanes)) return false;
            // Слой поверх участка (покрытие, невесомость, бонусы) ложится на что угодно из оставшегося;
            // два «предмета» — только задуманные пары (физика и место проверены).
            return a.Overlay || b.Overlay || StackPair(a.Id, b.Id);
        }

        /// Можно ли поставить b сразу за a (цепочка).
        public static bool CanFollow(EventDefinition a, EventDefinition b, bool wild = false) =>
            a != null && b != null && a.Id != b.Id && (wild || !(a.IsHeavy && b.IsHeavy));

        /// Можно ли наложить событие на участок, где уже идут эти: попарно совместимо и вместе не шире MaxBlocked.
        public static bool CanStackAll(EventDefinition candidate, IReadOnlyList<EventDefinition> present, float roadHalf = 11f, bool wild = false)
        {
            float blocked = candidate.Blocks;
            foreach (var p in present)
            {
                if (!CanStack(p, candidate, roadHalf, wild)) return false;
                blocked += p.Blocks;
            }
            return blocked <= (wild ? WildMaxBlocked : MaxBlocked) + 1e-4f;
        }

        static readonly (string, string)[] Pairs =
        {
            ("mines", "megaballs"),
            ("mines", "rollers"),
            ("kickers", "megaballs"),
            ("hammers", "megaballs"),
            ("barrage", "megaballs"),
            ("rocketbarrage", "megaballs"),
            ("bouncers", "megaballs"),
            ("bouncers", "mines"),
            ("spinner", "rollers"),
        };

        static bool StackPair(string a, string b)
        {
            foreach (var (x, y) in Pairs)
                if (x == a && y == b || x == b && y == a) return true;
            return false;
        }

        /// Сцены. Каждый шаг проходит обычные проверки (честность, место, бюджет физики, тяжёлые):
        /// если шаг сейчас невозможен, сцена играется без него.
        public static readonly ChaosRecipe[] Recipes =
        {
            R("bowling", "Боулинг на минном поле", 0f, false, S("mines"), Stack("megaballs")),
            R("icehammers", "Каток с молотами", 0.3f, false, S("hammers"), Stack("ice")),
            R("moonjump", "Лунные батуты", 0.2f, false, ChaosLevel.Many, S("lowgrav"), Stack("bouncers"), After("rings")),
            R("surprise", "Сюрприз за поворотом", 0f, false, S("roadblock"), After("ramp")),
            R("icebarrels", "Бочки на льду", 0.2f, false, S("rollers"), Stack("ice")),
            R("punchparty", "Карусель с перчатками", 0.35f, false, S("spinner"), After("kickers")),
            R("jellystrike", "Желейный обстрел", 0.4f, false, S("barrage"), Stack("jelly")),
            R("squeeze", "Бутылочное горлышко", 0.45f, false, S("walls"), After("mines"), After("boostchain")),
            R("forkrings", "Развилка с кольцами", 0.35f, false, S("fork"), After("rings")),
            R("pinball", "Пинбол", 0.3f, false, S("kickers"), Stack("megaballs"), After("bouncers")),
            R("minesjelly", "Прыгучие мины", 0.25f, false, S("mines"), Stack("jelly")),
            R("slipspin", "Карусель на льду", 0.35f, false, S("spinner"), Stack("ice")),
            // Последний круг: несколько событий собираются в большую сцену.
            R("parade", "ФИНАЛЬНЫЙ ПАРАД", 0f, true, S("barrage"), After("megaballs"), After("boostchain")),
            R("carnival", "ФИНАЛЬНЫЙ КАРНАВАЛ", 0f, true, S("hammers"), Stack("ice"), After("rollers"), After("rings")),
            R("apocalypse", "АПОКАЛИПСИС", 0f, true, ChaosLevel.Mayhem, S("walls"), After("mines"), Stack("megaballs"), After("lowgrav"), Stack("bouncers")),
        };

        static ComboStep S(string id) => new ComboStep(id, ComboLink.Anchor);
        static ComboStep Stack(string id) => new ComboStep(id, ComboLink.Stack);
        static ComboStep After(string id) => new ComboStep(id, ComboLink.After);

        static ChaosRecipe R(string id, string title, float minLevel, bool finale, params ComboStep[] steps) =>
            R(id, title, minLevel, finale, ChaosLevel.Moderate, steps);

        static ChaosRecipe R(string id, string title, float minLevel, bool finale, ChaosLevel minChaos, params ComboStep[] steps) =>
            new ChaosRecipe { Id = id, Title = title, Steps = steps, MinLevel = minLevel, Finale = finale, MinChaos = minChaos };
    }

    /// Сборщик комбинаций: решает, будет ли одиночное событие, случайная пара по правилам совместимости
    /// или сцена из рецепта. Случайность — свой генератор (детали места и сид — у директора).
    public sealed class ChaosComposer
    {
        readonly IReadOnlyList<EventDefinition> events;
        readonly System.Random random;
        readonly Dictionary<string, EventDefinition> byId = new Dictionary<string, EventDefinition>();
        string lastRecipe;

        public ChaosComposer(IReadOnlyList<EventDefinition> definitions, int seed)
        {
            events = definitions;
            random = new System.Random(seed);
            foreach (var d in events) byId[d.Id] = d;
        }

        public EventDefinition Find(string id) => id != null && byId.TryGetValue(id, out var d) ? d : null;

        public float Roll() => (float)random.NextDouble();

        /// Рецепт сцены: подходит по Хаосу, накалу и финалу, все его события существуют и якорь сейчас разрешён.
        /// Тот же рецепт два раза подряд не выпадает.
        public ChaosRecipe PickRecipe(EventIntensity intensity, bool finale, Func<EventDefinition, bool> allowed)
        {
            var options = new List<ChaosRecipe>();
            foreach (var r in ChaosRules.Recipes)
            {
                if (r.Finale != finale || intensity.Level < r.MinLevel || r.Id == lastRecipe) continue;
                bool ok = true;
                foreach (var step in r.Steps)
                    if (Find(step.Id) == null) ok = false;
                var anchor = Find(r.Steps[0].Id);
                if (!ok || anchor == null || (allowed != null && !allowed(anchor))) continue;
                options.Add(r);
            }
            if (options.Count == 0) return null;
            // Финал «Безумия» — всегда самый большой из доступных.
            if (finale && intensity.Wild)
            {
                ChaosRecipe biggest = options[0];
                foreach (var o in options) if (o.Steps.Length > biggest.Steps.Length) biggest = o;
                lastRecipe = biggest.Id;
                return biggest;
            }
            var pick = options[random.Next(options.Count)];
            lastRecipe = pick.Id;
            return pick;
        }

        /// Как прицепить случайного партнёра к якорю: поверх участка, если пара совместима, иначе — сразу за ним.
        public ComboLink PartnerLink(EventDefinition anchor, EventDefinition partner, float roadHalf = 11f, bool wild = false) =>
            ChaosRules.CanStack(anchor, partner, roadHalf, wild) && Roll() < (wild ? 0.75f : 0.6f) ? ComboLink.Stack : ComboLink.After;

        /// Фильтр партнёра: другое событие, совместимое с якорем хотя бы одним способом.
        public static Func<EventDefinition, bool> PartnerFilter(EventDefinition anchor, Func<EventDefinition, bool> allowed, bool wild = false) =>
            d => d.Id != anchor.Id && !d.Gag && (allowed == null || allowed(d)) && (ChaosRules.CanStack(anchor, d, 11f, wild) || ChaosRules.CanFollow(anchor, d, wild));
    }
}
