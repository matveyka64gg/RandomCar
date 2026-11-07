using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RandomCar.Events;
using UnityEngine;

namespace RandomCar.Tests
{
    /// Хаос v4: правила совместимости, сцены, баланс при фильтрах Хаоса и накала, проезд у мин, «Безумие».
    public sealed class ChaosTests
    {
        static List<EventDefinition> Defs => EventSettings.DefaultEvents().Where(d => !d.Gag && !d.Spectacle).ToList();

        static List<EventDefinition> Gags => EventSettings.DefaultEvents().Where(d => d.Gag).ToList();

        [Test]
        public void RecipesUseExistingEventsAndValidLinks()
        {
            var ids = new HashSet<string>(Defs.Select(d => d.Id));
            foreach (var r in ChaosRules.Recipes)
            {
                Assert.That(r.Steps.Length, Is.GreaterThanOrEqualTo(2), r.Id);
                Assert.That(r.Steps[0].Link, Is.EqualTo(ComboLink.Anchor), r.Id);
                foreach (var step in r.Steps) Assert.That(ids.Contains(step.Id), $"{r.Id}: нет события {step.Id}");
            }
            Assert.That(ChaosRules.Recipes.Count(r => r.Finale), Is.GreaterThanOrEqualTo(2), "финальные сцены последнего круга");
        }

        [Test]
        public void StackedEventsAlwaysLeavePassage()
        {
            var defs = Defs;
            int pairs = 0;
            foreach (var a in defs)
            {
                Assert.That(a.Blocks, Is.LessThan(0.7f), $"{a.Id} само по себе оставляет проезд");
                foreach (var b in defs)
                {
                    if (!ChaosRules.CanStack(a, b)) continue;
                    pairs++;
                    Assert.That(a.Blocks + b.Blocks, Is.LessThanOrEqualTo(ChaosRules.MaxBlocked + 1e-4f), $"{a.Id}+{b.Id}");
                    Assert.IsFalse(a.IsHeavy && b.IsHeavy, $"{a.Id}+{b.Id}: два тяжёлых");
                    Assert.IsFalse(a.Has(EventTags.Surface) && b.Has(EventTags.Surface), $"{a.Id}+{b.Id}: два покрытия");
                    Assert.IsFalse(a.Has(EventTags.Lanes) && b.Has(EventTags.Lanes), $"{a.Id}+{b.Id}: два перекрытия полос");
                    Assert.IsTrue(ChaosRules.CanStack(b, a), $"{a.Id}+{b.Id}: совместимость симметрична");
                }
            }
            Assert.That(pairs, Is.GreaterThan(40), "сочетаний много — комбинации разнообразны");
        }

        [Test]
        public void RecipeStacksAreAllowedByRules()
        {
            var byId = Defs.ToDictionary(d => d.Id);
            foreach (var r in ChaosRules.Recipes)
            {
                var previous = byId[r.Steps[0].Id];
                foreach (var step in r.Steps.Skip(1))
                {
                    var def = byId[step.Id];
                    if (step.Link == ComboLink.Stack) Assert.IsTrue(ChaosRules.CanStack(previous, def), $"{r.Id}: {previous.Id}+{def.Id}");
                    else Assert.IsTrue(ChaosRules.CanFollow(previous, def), $"{r.Id}: {previous.Id}→{def.Id}");
                    if (step.Link == ComboLink.After) previous = def;
                }
            }
        }

        [Test]
        public void BalanceHoldsUnderChaosAndIntensityFilters()
        {
            foreach (var chaos in new[] { ChaosLevel.Moderate, ChaosLevel.Many, ChaosLevel.Mayhem })
                foreach (float level in new[] { 0.18f, 0.5f, 1f })
                {
                    var s = new EventScheduler(Defs, 11);
                    int bad = 0;
                    const int draws = 6000;
                    for (int i = 0; i < draws; i++) if (!s.PickAndCommit(false, d => d.Unlocked(chaos, level)).IsGood) bad++;
                    Assert.That(bad / (float)draws, Is.InRange(0.46f, 0.54f), $"{chaos} накал {level}");
                }
        }

        [Test]
        public void RareEventsOnlyInMayhemAndVarietyGrows()
        {
            // Хаос — только частота: на любом включённом уровне доступны все события; «выключен» — ни одного.
            int moderate = Defs.Count(d => d.Weight > 0f && d.Unlocked(ChaosLevel.Moderate, 1f));
            int absurd = Defs.Count(d => d.Weight > 0f && d.Unlocked(ChaosLevel.Absurd, 1f));
            Assert.That(moderate, Is.EqualTo(absurd));
            Assert.That(moderate, Is.EqualTo(Defs.Count(d => d.Weight > 0f)));
            Assert.That(Defs.Count(d => d.Unlocked(ChaosLevel.Off, 1f)), Is.EqualTo(0));
            Assert.That(Gags.Count(g => g.Unlocked(ChaosLevel.Moderate, 1f)), Is.EqualTo(Gags.Count), "смерч и все трюки — и в «умеренно»");
        }

        [Test]
        public void ChaosChangesOnlyFrequency()
        {
            // Хаос — только частота: поведение событий одно и то же, отличаются интервалы и число одновременных.
            var many = new EventIntensity(0.6f, ChaosLevel.Many);
            var mayhem = new EventIntensity(0.6f, ChaosLevel.Mayhem);
            var moderate = new EventIntensity(0.6f, ChaosLevel.Moderate);
            Assert.That(mayhem.ComboChance, Is.EqualTo(many.ComboChance).Within(1e-4f));
            Assert.That(moderate.WarningScale, Is.EqualTo(mayhem.WarningScale));
            Assert.That(moderate.Wild && many.Wild && mayhem.Wild, Is.True);
            var settings = ScriptableObject.CreateInstance<EventSettings>();
            Assert.That(settings.GagInterval(ChaosLevel.Moderate).x, Is.GreaterThan(settings.GagInterval(ChaosLevel.Mayhem).x));
            Assert.That(settings.Interval(ChaosLevel.Moderate).x, Is.GreaterThan(settings.Interval(ChaosLevel.Absurd).x));
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void MineRowsAlwaysLeaveAGap()
        {
            var random = new System.Random(3);
            float Range(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
            foreach (float half in new[] { 6f, 7.5f, 11f })
                for (int pattern = 0; pattern < 3; pattern++)
                    for (int k = 0; k < 200; k++)
                    {
                        var layout = MineField.Layout(half, 4, 10f, pattern, Range, 10);
                        Assert.That(layout.Count, Is.GreaterThan(0));
                        foreach (var row in layout.GroupBy(m => Mathf.FloorToInt((m.x + 20f) / 10f)))
                        {
                            // Самый широкий свободный промежуток ряда (с учётом радиуса срабатывания 1.75 м).
                            var lats = row.Select(m => m.y).OrderBy(y => y).ToList();
                            float edge = -half, best = 0f;
                            foreach (float y in lats)
                            {
                                best = Mathf.Max(best, y - 1.75f - edge);
                                edge = Mathf.Max(edge, y + 1.75f);
                            }
                            best = Mathf.Max(best, half - edge);
                            Assert.That(best, Is.GreaterThanOrEqualTo(3.4f), $"полудорога {half}, узор {pattern}");
                            foreach (float y in lats) Assert.That(Mathf.Abs(y), Is.LessThanOrEqualTo(half - 0.5f));
                        }
                    }
        }

        [Test]
        public void ComposerPicksFinaleScenes()
        {
            var composer = new ChaosComposer(Defs, 5);
            var many = new EventIntensity(0.9f, ChaosLevel.Many);
            var r = composer.PickRecipe(many, true, null);
            Assert.IsNotNull(r);
            Assert.IsTrue(r.Finale);
            Assert.That(new ChaosComposer(Defs, 6).PickRecipe(new EventIntensity(0.9f, ChaosLevel.Moderate), true, null).Id, Is.EqualTo("apocalypse"), "финал — самая большая сцена на любом уровне");
            string previous = null;
            for (int i = 0; i < 30; i++)
            {
                var scene = composer.PickRecipe(many, false, null);
                Assert.IsNotNull(scene);
                Assert.That(scene.Id, Is.Not.EqualTo(previous), "одна сцена два раза подряд не выпадает");
                previous = scene.Id;
            }
        }

        [Test]
        public void PlacerAllowsOverlaysButNotLaneOverlaps()
        {
            var placer = new EventPlacer(2000f, _ => 0f, new System.Random(1));
            var byId = Defs.ToDictionary(d => d.Id);
            var zones = new List<EventZone> { new EventZone(0.5f, byId["hammers"].ZoneLength, byId["hammers"], 0f) };
            Assert.IsFalse(placer.Conflicts(byId["ice"], 0.5f, zones), "лёд ложится поверх молотов");
            Assert.IsTrue(placer.Conflicts(byId["walls"], 0.5f, zones), "стены поверх молотов — нет");
            Assert.IsTrue(placer.Conflicts(byId["ice"], 0.5f + 30f / 2000f, zones), "наложение «краем» запрещено");
            var two = new List<EventZone>(zones) { new EventZone(0.5f, byId["ice"].ZoneLength, byId["ice"], 0f) };
            Assert.IsTrue(placer.Conflicts(byId["jelly"], 0.5f, two), "два покрытия не накладываются");
        }

        [Test]
        public void NarrowRoadsStackOnlyLayers()
        {
            var byId = Defs.ToDictionary(d => d.Id);
            Assert.IsTrue(ChaosRules.CanStack(byId["mines"], byId["megaballs"], 11f), "широкая дорога: шары по минному полю");
            Assert.IsFalse(ChaosRules.CanStack(byId["mines"], byId["megaballs"], 6f), "узкая дорога: два предмета не накладываются");
            Assert.IsFalse(ChaosRules.CanStack(byId["hammers"], byId["megaballs"], 6f));
            Assert.IsTrue(ChaosRules.CanStack(byId["hammers"], byId["ice"], 6f), "слой (лёд) ложится и на узкой");
            Assert.IsFalse(byId["fork"].Fits(6f), "развилка — только на широкой дороге");
            Assert.IsTrue(byId["fork"].Fits(11f));
        }

        [Test]
        public void ChaosPowerIsTheSameOnEveryLevel()
        {
            // Решение пользователя 2026-10-29: Хаос не меняет силу ударов.
            var levels = new[] { ChaosLevel.Moderate, ChaosLevel.Many, ChaosLevel.Mayhem, ChaosLevel.Absurd };
            var reference = ChaosPower.For(ChaosLevel.Mayhem);
            foreach (var level in levels)
            {
                var p = ChaosPower.For(level);
                Assert.That(p.Punch, Is.EqualTo(reference.Punch), level.ToString());
                Assert.That(p.MaxUp, Is.EqualTo(reference.MaxUp), level.ToString());
                Assert.That(p.MaxSpin, Is.EqualTo(reference.MaxSpin), level.ToString());
            }
            Assert.That(reference.MaxUp, Is.GreaterThanOrEqualTo(25f), "подброс на десятки метров");
            Assert.That(reference.TumbleSeconds, Is.GreaterThan(1f));
            Assert.That(reference.Guide, Is.EqualTo(0f));
            Assert.That(reference.CarBilliards && reference.LandingBounce, Is.True);
            Assert.That(reference.Scale(Vector3.right * 100f).magnitude, Is.EqualTo(reference.MaxSide).Within(0.01f), "технический предел скорости остаётся");
        }

        [Test]
        public void MayhemIsInsaneFromTheFirstSecond()
        {
            var start = new EventIntensity(0f, ChaosLevel.Mayhem);
            Assert.That(start.Stage, Is.GreaterThanOrEqualTo(3), "«безумие» стартует с «жарко»");
            Assert.IsTrue(start.AllowHeavy);
            Assert.That(start.ComboChance, Is.GreaterThanOrEqualTo(0.5f));
            Assert.That(start.ExtraLead, Is.EqualTo(0f), "события — прямо перед носом");
            Assert.That(start.WarningScale, Is.LessThan(1f));
            Assert.That(start.MaxQuiet, Is.LessThanOrEqualTo(2f));
            Assert.That(new EventIntensity(1f, ChaosLevel.Mayhem).Level, Is.GreaterThan(start.Level), "эскалация по кругам — добавка сверху");
            Assert.That(new EventIntensity(0f, ChaosLevel.Absurd).MaxQuiet, Is.LessThan(start.MaxQuiet));
            var tests = new[] { "Mayhem", "Absurd" };
            Assert.That(System.Enum.GetNames(typeof(ChaosLevel)), Is.SupersetOf(tests));
        }

        [Test]
        public void GagsAreQuickVariedAndAlwaysStack()
        {
            var gags = Gags;
            Assert.That(gags.Count, Is.GreaterThanOrEqualTo(14), "большая библиотека трюков");
            Assert.That(gags.Any(g => g.Id == "bumper"), Is.False, "пинбол-бамперы убраны");
            Assert.That(gags.Select(g => g.Place).Distinct().Count(), Is.GreaterThanOrEqualTo(5), "под колёсами, впереди, сбоку, сверху, вокруг машины");
            foreach (var g in gags)
            {
                Assert.IsTrue(g.Overlay, g.Id);
                Assert.That(g.Blocks, Is.EqualTo(0f), g.Id);
                Assert.That(g.Copies, Is.GreaterThanOrEqualTo(3), $"{g.Id}: экземпляров хватает на частый поток");
                Assert.That(g.ActiveSeconds, Is.InRange(1f, 6.5f), g.Id);
            }
            var s = new EventScheduler(gags, 4);
            var seen = Enumerable.Range(0, 3000).Select(_ => s.PickAndCommit(false, null, true).Id).Distinct().Count();
            Assert.That(seen, Is.EqualTo(gags.Count), "все трюки выпадают");
        }

        [Test]
        public void WildPlacerPutsEventsRightAhead()
        {
            var placer = new EventPlacer(2000f, _ => 0f, new System.Random(1)) { Wild = true };
            var byId = Defs.ToDictionary(d => d.Id);
            var mines = byId["mines"];
            var racers = new List<RacerState> { new RacerState(0.1f, 30f, true, 0), new RacerState(0.1f + 150f / 2000f, 30f, false, 1) };
            Assert.IsTrue(placer.TryPlaceAhead(mines, racers[0], 40f, racers, new List<EventZone>(), out float p));
            float ahead = (p - 0.1f) * 2000f;
            Assert.That(ahead, Is.LessThan(70f), "в «безумии» событие — в десятках метров, а не в сотнях");
            // Нечестно близко — можно, но не прямо на машине.
            Assert.IsFalse(placer.IsFair(mines, 0.1f, racers), "внутри зоны машины событие не появляется");
            var fair = new EventPlacer(2000f, _ => 0f, new System.Random(1));
            Assert.IsFalse(fair.IsFair(mines, 0.1f + 40f / 2000f, racers), "в «умеренно» на 40 м при 30 м/с — нельзя");
            Assert.IsTrue(placer.IsFair(mines, 0.1f + 40f / 2000f, racers), "в «безумии» — можно");
        }

        [Test]
        public void ChaosEffectLimits()
        {
            Assert.That(FairnessLimits.MaxLaunchUp, Is.LessThanOrEqualTo(13f));
            Assert.That(FairnessLimits.MaxSpin, Is.LessThanOrEqualTo(2.5f));
            Assert.That(FairnessLimits.MinGravityScale, Is.GreaterThanOrEqualTo(0.3f));
            Assert.That(FairnessLimits.MaxVisualScaleSeconds, Is.LessThanOrEqualTo(4f));
            Assert.That(FairnessLimits.MaxEventImpactDeltaV, Is.LessThanOrEqualTo(12f));
        }
    }
}
