using NUnit.Framework;
using RandomCar.Car;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Tests
{
    /// ИИ: уровни отличаются решениями, а не физическими бонусами; линия остаётся на дороге.
    public sealed class AiTests
    {
        [Test]
        public void DifficultyChangesDecisionsOnly()
        {
            for (float v = 0f; v <= 1f; v += 0.25f)
            {
                var easy = AiSkill.ForDifficulty(0, v);
                var medium = AiSkill.ForDifficulty(1, v);
                var hard = AiSkill.ForDifficulty(2, v);
                Assert.That(easy.GripUse, Is.LessThan(medium.GripUse));
                Assert.That(medium.GripUse, Is.LessThan(hard.GripUse));
                Assert.That(easy.BrakeUse, Is.LessThan(hard.BrakeUse));
                Assert.That(easy.Reaction, Is.GreaterThan(hard.Reaction), "сложный реагирует быстрее");
                Assert.That(easy.LineNoise, Is.GreaterThan(hard.LineNoise), "сложный точнее держит линию");
                foreach (var s in new[] { easy, medium, hard })
                {
                    Assert.That(s.GripUse, Is.LessThanOrEqualTo(1f), "не больше сцепления, чем у машины");
                    Assert.That(s.Pace, Is.LessThanOrEqualTo(1f), "не быстрее ограничителя машины");
                }
            }
        }

        [Test]
        public void RacingLineStaysOnRoadAndUsesWidth()
        {
            var path = TrackLayout.CreatePath();
            var line = new RacingLine(path, 1.2f, 9f, 30f, 58f, 4f);
            float min = 0f, max = 0f;
            for (int i = 0; i < line.Count; i++)
            {
                float l = line.LateralAt(i / (float)line.Count);
                Assert.That(Mathf.Abs(l), Is.LessThanOrEqualTo(4f + 1e-3f));
                min = Mathf.Min(min, l);
                max = Mathf.Max(max, l);
            }
            Assert.That(max - min, Is.GreaterThan(6f), "линия использует ширину дороги, а не едет по оси");
        }
    }
}
