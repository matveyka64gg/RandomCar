using System.Collections.Generic;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.PowerUps
{
    /// Где ряд ящиков: на прямой или перед поворотом (видно издалека, можно выбрать ящик).
    public enum BoxSpot { Straight, BeforeTurn }

    /// Место ящика на трассе: прогресс, сдвиг поперёк, номер ряда и вид места.
    public struct BoxPlace
    {
        public float Progress;
        public float Lateral;
        public int Row;
        public BoxSpot Spot;
    }

    /// Расстановка ящиков с бонусами по трассе: ряды через RowSpacing метров, каждый ряд сдвигается
    /// (не дальше ±35 м) к входу в поворот или на прямую; в ряду 3–5 ящиков по полосам. У всех игроков
    /// одинаково (зависит только от трассы) — номер ящика один и тот же у хоста и клиентов.
    public static class PowerUpLayout
    {
        const float CurveThreshold = 1f / 60f;
        const float StartClear = 30f;

        public static List<BoxPlace> Build(TrackPath course, float spacing)
        {
            var result = new List<BoxPlace>();
            if (course == null || course.Count < 8) return result;
            float length = course.Length;
            int rows = Mathf.Max(3, Mathf.RoundToInt(length / Mathf.Max(60f, spacing)));
            float step = length / rows;
            float half = course.HalfWidth;
            int perRow = Mathf.Clamp(Mathf.FloorToInt(half * 2f / 4.2f), 3, 5);
            for (int row = 0; row < rows; row++)
            {
                float baseDistance = (row + 0.5f) * step;
                float best = baseDistance;
                float bestScore = float.MinValue;
                BoxSpot bestSpot = BoxSpot.Straight;
                for (float shift = -35f; shift <= 35f; shift += 5f)
                {
                    float d = baseDistance + shift;
                    float p = TrackPath.Wrap(d / length);
                    if (d < StartClear || d > length - StartClear) continue;
                    float here = Curvature(course, p);
                    float ahead = Mathf.Max(Curvature(course, p + 25f / length), Curvature(course, p + 45f / length));
                    // Вход в поворот (здесь прямо, через 25–45 м — дуга) — лучшее место: выбор полосы перед виражом.
                    bool beforeTurn = here < CurveThreshold && ahead > CurveThreshold * 1.5f;
                    float score = (beforeTurn ? 2f : 0f) - here * 60f - Mathf.Abs(shift) * 0.01f;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = d;
                    bestSpot = beforeTurn ? BoxSpot.BeforeTurn : BoxSpot.Straight;
                }
                float progress = TrackPath.Wrap(best / length);
                for (int k = 0; k < perRow; k++)
                {
                    float t = perRow == 1 ? 0.5f : k / (float)(perRow - 1);
                    result.Add(new BoxPlace
                    {
                        Progress = progress,
                        Lateral = Mathf.Lerp(-half + 1.8f, half - 1.8f, t),
                        Row = row,
                        Spot = bestSpot,
                    });
                }
            }
            return result;
        }

        static float Curvature(TrackPath course, float progress)
        {
            int index = Mathf.FloorToInt(TrackPath.Wrap(progress) * course.Count) % course.Count;
            return Mathf.Abs(course.CurvatureAt(index, 4));
        }
    }
}
