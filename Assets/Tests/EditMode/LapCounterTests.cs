using NUnit.Framework;
using RandomCar.Race;

namespace RandomCar.Tests
{
    public sealed class LapCounterTests
    {
        [Test]
        public void CountsOneLapWhenFinishingFullLoop()
        {
            var counter = new LapCounter(0.02f);
            Assert.That(counter.Update(0.2f), Is.False);
            Assert.That(counter.Update(0.4f), Is.False);
            Assert.That(counter.Update(0.7f), Is.False);
            Assert.That(counter.Update(0.98f), Is.False);

            Assert.That(counter.Update(0.01f), Is.True);
            Assert.That(counter.Laps, Is.EqualTo(1));
        }

        [Test]
        public void CountsEachLapExactlyOnce()
        {
            var counter = new LapCounter(0.02f);
            int completed = 0;
            float[] lap = { 0.1f, 0.4f, 0.7f, 0.99f, 0.02f, 0.05f };

            for (int i = 0; i < 3; i++)
                foreach (float p in lap)
                    if (counter.Update(p)) completed++;

            Assert.That(completed, Is.EqualTo(3));
            Assert.That(counter.Laps, Is.EqualTo(3));
        }

        [Test]
        public void ShortcutAcrossFinishDoesNotCount()
        {
            var counter = new LapCounter(0.02f);
            counter.Update(0.99f);

            Assert.That(counter.Update(0.01f), Is.False);
            Assert.That(counter.Laps, Is.EqualTo(0));
        }

        [Test]
        public void SkippingMiddleSectorDoesNotCount()
        {
            var counter = new LapCounter(0.02f);
            counter.Update(0.7f);

            Assert.That(counter.Update(0.01f), Is.False);
            Assert.That(counter.Laps, Is.EqualTo(0));
        }

        [Test]
        public void DrivingBackwardsDoesNotCount()
        {
            var counter = new LapCounter(0.5f);
            counter.Update(0.2f);
            counter.Update(0.9f);
            counter.Update(0.6f);
            counter.Update(0.2f);

            Assert.That(counter.Laps, Is.EqualTo(0));
        }

        [Test]
        public void StartBehindLineFirstCrossingOnlyStartsRace()
        {
            var counter = new LapCounter(0.97f);
            Assert.That(counter.Started, Is.False);

            Assert.That(counter.Update(0.02f), Is.False);
            Assert.That(counter.Started, Is.True);
            Assert.That(counter.Laps, Is.EqualTo(0));

            counter.Update(0.4f);
            counter.Update(0.7f);
            Assert.That(counter.Update(0.01f), Is.True);
            Assert.That(counter.Laps, Is.EqualTo(1));
        }

        [Test]
        public void ScoreIsNegativeUntilStartLineCrossed()
        {
            var behind = new Racer { Laps = new LapCounter(0.97f), Progress = 0.97f };
            var crossed = new Racer { Laps = new LapCounter(0.97f), Progress = 0.02f };
            crossed.Laps.Update(0.02f);

            Assert.That(behind.Score, Is.LessThan(0f));
            Assert.That(crossed.Score, Is.GreaterThan(behind.Score));
        }

        [Test]
        public void OscillatingAtFinishLineDoesNotCountAgain()
        {
            var counter = new LapCounter(0.02f);
            counter.Update(0.4f);
            counter.Update(0.7f);
            counter.Update(0.98f);
            counter.Update(0.01f);
            Assert.That(counter.Laps, Is.EqualTo(1));

            for (int i = 0; i < 4; i++)
            {
                counter.Update(0.99f);
                counter.Update(0.01f);
            }

            Assert.That(counter.Laps, Is.EqualTo(1));
        }
    }
}
