using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Track
{
    /// Замкнутая трасса: кривая Catmull-Rom по контрольным точкам, дискретизированная по длине дуги.
    /// Прогресс 0..1, ноль на стартовой линии. Трасса плоская, ось Y не используется.
    public sealed class TrackPath
    {
        readonly Vector3[] points;
        readonly float[] distances; // distances[i] — длина дуги до точки i, distances[Count] = Length

        /// startSample сдвигает начало отсчёта: прогресс 0 будет в этом сэмпле исходной кривой.
        public TrackPath(IReadOnlyList<Vector3> control, int samplesPerSegment, int startSample = 0)
        {
            int segments = control.Count;
            int count = segments * samplesPerSegment;
            var raw = new Vector3[count];
            points = new Vector3[count];
            distances = new float[count + 1];

            for (int s = 0; s < segments; s++)
            {
                Vector3 p0 = control[(s - 1 + segments) % segments];
                Vector3 p1 = control[s];
                Vector3 p2 = control[(s + 1) % segments];
                Vector3 p3 = control[(s + 2) % segments];
                for (int k = 0; k < samplesPerSegment; k++)
                    raw[s * samplesPerSegment + k] = CatmullRom(p0, p1, p2, p3, (float)k / samplesPerSegment);
            }

            int offset = ((startSample % count) + count) % count;
            for (int i = 0; i < count; i++)
                points[i] = raw[(i + offset) % count];

            for (int i = 0; i < count; i++)
                distances[i + 1] = distances[i] + Vector3.Distance(points[i], points[(i + 1) % count]);
            Length = distances[count];
        }

        public int Count => points.Length;

        /// Половина ширины проезжей части, м (из TrackDefinition): ИИ, события и проверки «на дороге ли машина».
        public float HalfWidth { get; set; } = 6f;

        /// Длина трассы по осевой линии, м.
        public float Length { get; }

        public Vector3 Point(int index) => points[index];

        /// Длина дуги от старта до сэмпла, м.
        public float DistanceAt(int index) => distances[index];

        /// Кривизна в сэмпле, 1/м: плюс — поворот налево, минус — направо. Считается по изменению курса на ±window сэмплов.
        public float CurvatureAt(int index, int window = 3)
        {
            int n = points.Length;
            Vector3 a = TangentAt((index - window + n) % n);
            Vector3 b = TangentAt((index + window) % n);
            float angle = Vector3.SignedAngle(a, b, Vector3.up) * Mathf.Deg2Rad;
            float span = 0f;
            for (int k = -window; k < window; k++)
            {
                int i = (index + k + n) % n;
                span += Vector3.Distance(points[i], points[(i + 1) % n]);
            }
            // SignedAngle вокруг Y положителен по часовой (поворот направо), поэтому знак меняем.
            return span > 0f ? -angle / span : 0f;
        }

        public Vector3 TangentAt(int index)
        {
            int n = points.Length;
            return Flat(points[(index + 1) % n] - points[(index - 1 + n) % n]).normalized;
        }

        public Vector3 RightAt(int index) => Vector3.Cross(Vector3.up, TangentAt(index));

        /// Точка трассы на прогрессе 0..1.
        public Vector3 PointAt(float progress)
        {
            float s = Wrap(progress) * Length;
            int lo = 0;
            int hi = points.Length;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (distances[mid] <= s) lo = mid;
                else hi = mid;
            }

            float segment = distances[lo + 1] - distances[lo];
            float t = segment > 0f ? (s - distances[lo]) / segment : 0f;
            return Vector3.Lerp(points[lo], points[(lo + 1) % points.Length], t);
        }

        /// Направление движения на прогрессе (горизонтальное, единичное).
        public Vector3 DirectionAt(float progress)
        {
            float d = 0.5f / points.Length;
            return Flat(PointAt(progress + d) - PointAt(progress - d)).normalized;
        }

        /// Вектор вправо от направления движения на прогрессе.
        public Vector3 SideAt(float progress) => Vector3.Cross(Vector3.up, DirectionAt(progress));

        /// Прогресс ближайшей к позиции точки трассы.
        public float Nearest(Vector3 position)
        {
            int n = points.Length;
            Vector3 flat = Flat(position);

            int best = 0;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float d = (Flat(points[i]) - flat).sqrMagnitude;
                if (d < bestSqr)
                {
                    bestSqr = d;
                    best = i;
                }
            }

            // Точка могла лечь на соседний отрезок, проверяем оба.
            float sNext = ProjectOnSegment(best, flat, out float distNext);
            float sPrev = ProjectOnSegment((best - 1 + n) % n, flat, out float distPrev);
            float s = distNext <= distPrev ? sNext : sPrev;
            return Wrap(s / Length);
        }

        /// Прогресс 0..1 из любого вещественного значения.
        public static float Wrap(float progress) => ((progress % 1f) + 1f) % 1f;

        float ProjectOnSegment(int segment, Vector3 flat, out float distanceSqr)
        {
            int n = points.Length;
            Vector3 a = Flat(points[segment]);
            Vector3 ab = Flat(points[(segment + 1) % n]) - a;
            float lengthSqr = ab.sqrMagnitude;
            float t = lengthSqr > 0f ? Mathf.Clamp01(Vector3.Dot(flat - a, ab) / lengthSqr) : 0f;

            distanceSqr = (a + ab * t - flat).sqrMagnitude;
            return distances[segment] + t * (distances[segment + 1] - distances[segment]);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1)
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
