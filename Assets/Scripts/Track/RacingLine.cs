using UnityEngine;

namespace RandomCar.Track
{
    /// Гоночная линия и профиль скорости для ИИ.
    /// Линия — «резиновая лента»: точки на оси трассы многократно подтягиваются к середине соседей (спрямление)
    /// в пределах ширины дороги. Так сама получается траектория «снаружи — апекс — снаружи» с наибольшим радиусом.
    /// Скорость считается по кривизне именно этой линии: v = √(μ·g / кривизна), затем обратный проход —
    /// успеть оттормозиться, и прямой — ограничение реального разгона. Чистый C#: тестируется в EditMode.
    public sealed class RacingLine
    {
        const float Gravity = 9.81f;
        const int Iterations = 400;

        readonly TrackPath path;
        readonly float[] lateral;
        readonly float[] speed;
        readonly float[] curvature;
        readonly Vector3[] points;

        /// grip — используемое боковое ускорение в долях g; brake и accel — замедление и разгон, м/с².
        /// halfWidth — сколько метров от оси линия может использовать.
        /// gripScale — множитель сцепления покрытия по сэмплам трассы (грунт, брусчатка); null — везде асфальт.
        public RacingLine(TrackPath trackPath, float grip, float brake, float accel, float topSpeed, float halfWidth, float[] gripScale = null)
        {
            path = trackPath;
            int n = path.Count;
            lateral = new float[n];
            speed = new float[n];
            curvature = new float[n];
            points = new Vector3[n];

            var axis = new Vector3[n];
            var side = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                axis[i] = path.Point(i);
                side[i] = path.RightAt(i);
            }

            // Спрямление: шаг к середине соседей на расстоянии k, затем ограничение шириной дороги.
            int k = 3;
            var next = new float[n];
            for (int it = 0; it < Iterations; it++)
            {
                for (int i = 0; i < n; i++)
                {
                    int a = (i - k + n) % n, b = (i + k) % n;
                    Vector3 pa = axis[a] + side[a] * lateral[a];
                    Vector3 pb = axis[b] + side[b] * lateral[b];
                    float wanted = Vector3.Dot((pa + pb) * 0.5f - axis[i], side[i]);
                    next[i] = Mathf.Clamp(Mathf.Lerp(lateral[i], wanted, 0.5f), -halfWidth, halfWidth);
                }
                System.Array.Copy(next, lateral, n);
            }
            for (int i = 0; i < n; i++) points[i] = axis[i] + side[i] * lateral[i];

            // Кривизна линии по трём точкам (окружность через них); берём пик в окрестности — с запасом.
            var raw = new float[n];
            for (int i = 0; i < n; i++) raw[i] = Menger(points[(i - k + n) % n], points[i], points[(i + k) % n]);
            for (int i = 0; i < n; i++)
            {
                float peak = raw[i];
                for (int j = -2; j <= 2; j++)
                {
                    float c = raw[(i + j + n) % n];
                    if (Mathf.Abs(c) > Mathf.Abs(peak)) peak = c;
                }
                curvature[i] = peak;
            }

            // Вертикальная кривизна: на гребне машина разгружается — держит хуже и может оторваться от дороги.
            for (int i = 0; i < n; i++)
            {
                int a = (i - k + n) % n, b = (i + k) % n;
                float ds = Vector3.Distance(points[a], points[b]) * 0.5f;
                float vertical = ds > 0.1f ? (points[a].y - 2f * points[i].y + points[b].y) / (ds * ds) : 0f;
                float c = Mathf.Abs(curvature[i]);
                float g = grip * Scale(gripScale, i);
                float denominator = c - g * Mathf.Min(0f, vertical);
                float corner = denominator > 1e-4f ? Mathf.Sqrt(g * Gravity / denominator) : float.MaxValue;
                float crest = vertical < -1e-4f ? Mathf.Sqrt(Gravity / -vertical) * 0.85f : float.MaxValue;
                speed[i] = Mathf.Min(topSpeed, Mathf.Min(corner, crest));
            }

            // Обратный проход дважды (трасса замкнута): успеть оттормозиться. Шина, занятая поворотом,
            // тормозит слабее (эллипс трения), поэтому в дуге торможение начинается раньше.
            for (int pass = 0; pass < 2; pass++)
                for (int i = n - 1; i >= 0; i--)
                {
                    int nextIndex = (i + 1) % n;
                    float ds = Vector3.Distance(points[i], points[nextIndex]);
                    float v = speed[nextIndex];
                    float g = grip * Scale(gripScale, i);
                    float lateralUse = Mathf.Clamp01(v * v * Mathf.Abs(curvature[i]) / (g * Gravity));
                    float available = brake * Scale(gripScale, i) * Mathf.Max(0.25f, Mathf.Sqrt(1f - lateralUse * lateralUse));
                    speed[i] = Mathf.Min(speed[i], Mathf.Sqrt(v * v + 2f * available * ds));
                }
            // Прямой проход: машина не разгоняется быстрее, чем может.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    int prev = (i - 1 + n) % n;
                    float ds = Vector3.Distance(points[prev], points[i]);
                    speed[i] = Mathf.Min(speed[i], Mathf.Sqrt(speed[prev] * speed[prev] + 2f * accel * ds));
                }
        }

        static float Scale(float[] scale, int i) => scale != null && i < scale.Length ? Mathf.Clamp(scale[i], 0.3f, 1.2f) : 1f;

        /// Кривизна окружности через три точки, 1/м: плюс — поворот налево.
        static float Menger(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector2 p = new Vector2(a.x, a.z), q = new Vector2(b.x, b.z), r = new Vector2(c.x, c.z);
            float cross = (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
            float d = (q - p).magnitude * (r - q).magnitude * (r - p).magnitude;
            return d > 1e-5f ? 2f * cross / d : 0f;
        }

        public int Count => lateral.Length;

        int Index(float progress) => Mathf.Clamp(Mathf.FloorToInt(TrackPath.Wrap(progress) * lateral.Length), 0, lateral.Length - 1);

        /// Смещение линии от оси на прогрессе, м (плюс — вправо).
        public float LateralAt(float progress)
        {
            float f = TrackPath.Wrap(progress) * lateral.Length;
            int i = Mathf.FloorToInt(f) % lateral.Length;
            return Mathf.Lerp(lateral[i], lateral[(i + 1) % lateral.Length], f - Mathf.Floor(f));
        }

        /// Целевая скорость на прогрессе, м/с.
        public float SpeedAt(float progress) => speed[Index(progress)];

        /// Кривизна линии на прогрессе, 1/м (плюс — налево).
        public float CurvatureAt(float progress) => curvature[Index(progress)];

        /// Минимальная целевая скорость на отрезке [progress, progress + distance/length].
        public float MinSpeedAhead(float progress, float distance)
        {
            int i = Index(progress);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(0.1f, path.Length / lateral.Length)));
            float min = float.MaxValue;
            for (int k = 0; k <= steps; k++) min = Mathf.Min(min, speed[(i + k) % speed.Length]);
            return min;
        }
    }
}
