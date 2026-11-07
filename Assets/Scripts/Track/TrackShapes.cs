using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Track
{
    /// Построение контрольных точек трасс из простых фигур. Чистый C#.
    public static class TrackShapes
    {
        /// Замкнутый многоугольник со скруглёнными углами: углы — вершины (x, z), радиусы — скругления в метрах,
        /// heights — высота в каждой вершине (линейно по пути). Точки идут через spacing метров.
        public static Vector3[] RoundedPolygon(IReadOnlyList<Vector2> corners, IReadOnlyList<float> radii, float spacing, IReadOnlyList<float> heights = null)
        {
            int n = corners.Count;
            var result = new List<Vector3>();
            var arcStart = new Vector2[n];
            var arcEnd = new Vector2[n];
            var centers = new Vector2[n];
            var startAngle = new float[n];
            var sweep = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = corners[(i - 1 + n) % n], p = corners[i], next = corners[(i + 1) % n];
                Vector2 d1 = (p - prev).normalized, d2 = (next - p).normalized;
                float turn = Vector2.SignedAngle(d1, d2) * Mathf.Deg2Rad;   // плюс — налево (против часовой)
                float r = radii[i];
                float t = r * Mathf.Tan(Mathf.Abs(turn) * 0.5f);
                arcStart[i] = p - d1 * t;
                arcEnd[i] = p + d2 * t;
                Vector2 normal = turn > 0f ? new Vector2(-d1.y, d1.x) : new Vector2(d1.y, -d1.x);
                centers[i] = arcStart[i] + normal * r;
                Vector2 fromCenter = arcStart[i] - centers[i];
                startAngle[i] = Mathf.Atan2(fromCenter.y, fromCenter.x);
                sweep[i] = turn;
            }

            float Height(int corner) => heights != null ? heights[corner] : 0f;

            for (int i = 0; i < n; i++)
            {
                // Дуга угла i.
                float r = radii[i];
                float arcLen = Mathf.Abs(sweep[i]) * r;
                int arcSteps = Mathf.Max(2, Mathf.CeilToInt(arcLen / spacing));
                for (int k = 0; k < arcSteps; k++)
                {
                    float a = startAngle[i] + sweep[i] * k / arcSteps;
                    Vector2 q = centers[i] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    result.Add(new Vector3(q.x, Height(i), q.y));
                }
                // Прямая до следующего угла.
                int j = (i + 1) % n;
                Vector2 a0 = arcEnd[i], a1 = arcStart[j];
                float len = Vector2.Distance(a0, a1);
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / spacing));
                for (int k = 0; k < steps; k++)
                {
                    float t = (float)k / steps;
                    Vector2 q = Vector2.Lerp(a0, a1, t);
                    result.Add(new Vector3(q.x, Mathf.Lerp(Height(i), Height(j), t), q.y));
                }
            }
            if (heights != null) SmoothHeights(result, 8, 4);
            return result.ToArray();
        }

        /// Сглаживание высот скользящим средним по кругу: без изломов профиля (гребни и впадины плавные).
        static void SmoothHeights(List<Vector3> points, int window, int passes)
        {
            int n = points.Count;
            var y = new float[n];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    float sum = 0f;
                    for (int k = -window; k <= window; k++) sum += points[(i + k + n) % n].y;
                    y[i] = sum / (2 * window + 1);
                }
                for (int i = 0; i < n; i++) points[i] = new Vector3(points[i].x, y[i], points[i].z);
            }
        }

        /// Индекс контрольной точки, ближайшей к позиции (x, z).
        public static int Closest(Vector3[] points, Vector2 xz)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < points.Length; i++)
            {
                float d = (new Vector2(points[i].x, points[i].z) - xz).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }
    }
}
