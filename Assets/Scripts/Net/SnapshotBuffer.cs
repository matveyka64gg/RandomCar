using UnityEngine;

namespace RandomCar.Net
{
    /// Буфер снимков чужой машины: интерполяция между двумя снимками вокруг времени показа,
    /// при потере пакетов — экстраполяция по скорости не дольше MaxExtrapolation, дальше машина ждёт.
    public sealed class SnapshotBuffer
    {
        public const int Capacity = 32;
        public const double MaxExtrapolation = 0.25;

        readonly CarState[] items = new CarState[Capacity];
        int count;

        public int Count => count;
        public bool HasData => count > 0;
        public CarState Latest => items[count - 1];

        public void Clear() => count = 0;

        /// Добавить снимок; устаревшие (не новее последнего) отбрасываются — пакеты могут прийти не по порядку.
        public bool Push(CarState state)
        {
            if (count > 0 && state.Time <= items[count - 1].Time) return false;
            if (count == Capacity)
            {
                System.Array.Copy(items, 1, items, 0, Capacity - 1);
                count--;
            }
            items[count++] = state;
            return true;
        }

        /// Состояние на момент time. extrapolated — данных новее нет, позиция досчитана по скорости.
        public bool Sample(double time, out CarState result, out bool extrapolated)
        {
            result = default;
            extrapolated = false;
            if (count == 0) return false;
            if (time <= items[0].Time)
            {
                result = items[0];
                return true;
            }
            for (int i = 0; i < count - 1; i++)
            {
                var a = items[i];
                var b = items[i + 1];
                if (time > b.Time) continue;
                float t = (float)((time - a.Time) / System.Math.Max(1e-6, b.Time - a.Time));
                result = b;
                result.Time = time;
                result.Position = Hermite(a.Position, a.Velocity, b.Position, b.Velocity, t, (float)(b.Time - a.Time));
                result.Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
                result.Velocity = Vector3.Lerp(a.Velocity, b.Velocity, t);
                result.AngularVelocity = Vector3.Lerp(a.AngularVelocity, b.AngularVelocity, t);
                return true;
            }
            var last = items[count - 1];
            float ahead = (float)System.Math.Min(time - last.Time, MaxExtrapolation);
            result = last;
            result.Time = last.Time + ahead;
            result.Position = last.Position + last.Velocity * ahead;
            if (last.AngularVelocity.sqrMagnitude > 1e-6f)
                result.Rotation = Quaternion.AngleAxis(last.AngularVelocity.magnitude * Mathf.Rad2Deg * ahead, last.AngularVelocity.normalized) * last.Rotation;
            extrapolated = true;
            return true;
        }

        /// Кубическая кривая по положениям и скоростям: на поворотах машина не «срезает» между снимками.
        static Vector3 Hermite(Vector3 p0, Vector3 v0, Vector3 p1, Vector3 v1, float t, float span)
        {
            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0 + (t3 - 2f * t2 + t) * span * v0 + (-2f * t3 + 3f * t2) * p1 + (t3 - t2) * span * v1;
        }
    }
}
