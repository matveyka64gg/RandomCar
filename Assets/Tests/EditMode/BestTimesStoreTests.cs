using System.Collections.Generic;
using NUnit.Framework;
using RandomCar.Save;

namespace RandomCar.Tests
{
    public sealed class BestTimesStoreTests
    {
        [Test]
        public void AddToEmptyListReturnsZero()
        {
            var times = new List<float>();

            Assert.That(BestTimesStore.Add(times, 30f), Is.EqualTo(0));
            Assert.That(times, Is.EqualTo(new[] { 30f }));
        }

        [Test]
        public void KeepsAscendingOrder()
        {
            var times = new List<float>();
            Assert.That(BestTimesStore.Add(times, 30f), Is.EqualTo(0));
            Assert.That(BestTimesStore.Add(times, 10f), Is.EqualTo(0));
            Assert.That(BestTimesStore.Add(times, 20f), Is.EqualTo(1));

            Assert.That(times, Is.EqualTo(new[] { 10f, 20f, 30f }));
        }

        [Test]
        public void ReturnsMinusOneWhenNotInTop5()
        {
            var times = new List<float> { 1f, 2f, 3f, 4f, 5f };

            Assert.That(BestTimesStore.Add(times, 6f), Is.EqualTo(-1));
            Assert.That(times.Count, Is.EqualTo(BestTimesStore.Capacity));
        }

        [Test]
        public void FasterTimeDropsSlowestWhenFull()
        {
            var times = new List<float> { 1f, 2f, 3f, 4f, 5f };

            Assert.That(BestTimesStore.Add(times, 0.5f), Is.EqualTo(0));
            Assert.That(times, Is.EqualTo(new[] { 0.5f, 1f, 2f, 3f, 4f }));
        }

        [Test]
        public void EqualTimeGoesAfterExisting()
        {
            var times = new List<float> { 10f };

            Assert.That(BestTimesStore.Add(times, 10f), Is.EqualTo(1));
        }
    }
}
