using UnityEngine;

namespace RandomCar.Track
{
    /// Контрольные точки трассы: овал с волнами по радиусу. Точки идут по углу, поэтому трасса не самопересекается.
    public static class TrackLayout
    {
        public const int SamplesPerSegment = 24;
        const int ControlCount = 16;

        /// Сэмпл стартовой линии: середина самой длинной прямой (радиус больше 110 м на 110 м пути).
        public const int StartSample = 78;

        public static Vector3[] ControlPoints()
        {
            var points = new Vector3[ControlCount];
            for (int i = 0; i < ControlCount; i++)
            {
                float a = 2f * Mathf.PI * i / ControlCount;
                float r = 90f + 25f * Mathf.Sin(2f * a + 0.5f) + 12f * Mathf.Cos(3f * a);
                points[i] = new Vector3(Mathf.Cos(a) * r * 1.3f, 0f, Mathf.Sin(a) * r);
            }
            return points;
        }

        /// Трасса гонки: прогресс 0 на стартовой линии.
        public static TrackPath CreatePath() => new TrackPath(ControlPoints(), SamplesPerSegment, StartSample);
    }
}
