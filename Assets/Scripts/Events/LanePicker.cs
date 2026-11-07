using UnityEngine;

namespace RandomCar.Events
{
    /// Сторона дороги для нового события: полосы по очереди (не два раза подряд одна и та же, не всё время слева
    /// или справа), а часть плохих — прямо на полосу игрока, чтобы пришлось менять траекторию.
    public sealed class LanePicker
    {
        static readonly float[] Lanes = { -0.72f, -0.36f, 0f, 0.36f, 0.72f };

        readonly System.Random random;
        int last = -1;
        int previous = -1;

        public LanePicker(int seed) => random = new System.Random(seed);

        public float Roll() => (float)random.NextDouble();

        public float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

        /// Смещение от оси дороги, м. aim — на полосу игрока (focusLateral) с небольшим разбросом.
        public float Next(bool aim, float focusLateral, float halfWidth, float radius)
        {
            float limit = Mathf.Max(0f, halfWidth - radius - 0.4f);
            float lateral;
            int lane;
            if (aim)
            {
                lateral = Mathf.Clamp(focusLateral + Range(-0.9f, 0.9f), -limit, limit);
                lane = Nearest(limit > 0f ? lateral / limit : 0f);
            }
            else
            {
                lane = random.Next(Lanes.Length);
                for (int i = 0; i < 8 && (lane == last || lane == previous); i++) lane = random.Next(Lanes.Length);
                lateral = Lanes[lane] * limit;
            }
            previous = last;
            last = lane;
            return lateral;
        }

        static int Nearest(float fraction)
        {
            int best = 0;
            for (int i = 1; i < Lanes.Length; i++)
                if (Mathf.Abs(Lanes[i] - fraction) < Mathf.Abs(Lanes[best] - fraction)) best = i;
            return best;
        }
    }
}
