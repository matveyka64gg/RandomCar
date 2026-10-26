using NUnit.Framework;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Tests
{
    public sealed class TrackPathTests
    {
        static TrackPath CreateTrack() => new TrackPath(TrackLayout.ControlPoints(), TrackLayout.SamplesPerSegment);

        [Test]
        public void LengthIsReasonable()
        {
            Assert.That(CreateTrack().Length, Is.GreaterThan(300f));
        }

        [Test]
        public void TrackIsClosed()
        {
            var track = CreateTrack();
            Assert.That(Vector3.Distance(track.PointAt(0f), track.PointAt(0.9999f)), Is.LessThan(1f));
        }

        [TestCase(0.1f)]
        [TestCase(0.37f)]
        [TestCase(0.5f)]
        [TestCase(0.82f)]
        [TestCase(0.99f)]
        public void NearestReturnsSameProgressForPointOnTrack(float progress)
        {
            var track = CreateTrack();
            float found = track.Nearest(track.PointAt(progress));

            float diff = Mathf.Abs(found - progress);
            diff = Mathf.Min(diff, 1f - diff); // прогресс замкнут
            Assert.That(diff, Is.LessThan(0.002f));
        }

        [Test]
        public void StartLineIsOnStraight()
        {
            var track = TrackLayout.CreatePath();
            for (int k = -15; k <= 15; k += 5)
            {
                int i = (k + track.Count) % track.Count;
                Assert.That(Mathf.Abs(track.CurvatureAt(i, 4)), Is.LessThan(1f / 100f), $"сэмпл {k} от старта");
            }
        }

        [Test]
        public void StartOffsetKeepsGeometry()
        {
            var raw = CreateTrack();
            var shifted = TrackLayout.CreatePath();
            Assert.That(shifted.Length, Is.EqualTo(raw.Length).Within(0.01f));
            Assert.That(Vector3.Distance(shifted.Point(0), raw.Point(TrackLayout.StartSample)), Is.LessThan(0.001f));
        }

        [Test]
        public void DirectionPointsAlongTrack()
        {
            var track = CreateTrack();
            Vector3 a = track.PointAt(0.25f);
            Vector3 b = track.PointAt(0.251f);

            Assert.That(Vector3.Dot((b - a).normalized, track.DirectionAt(0.25f)), Is.GreaterThan(0.99f));
        }
    }
}
