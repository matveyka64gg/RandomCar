using UnityEngine;

namespace RandomCar.Car
{
    /// Результат расчёта шины на шаг.
    public struct TireForce
    {
        /// Продольная сила вдоль колеса, Н.
        public float Longitudinal;
        /// Боковая сила поперёк колеса, Н.
        public float Lateral;
        /// Продольная сила упёрлась в сцепление: колесо буксует или блокируется.
        public bool LongSlip;
        /// Лишняя продольная сила сверх сцепления, Н (для оборотов и дыма).
        public float Excess;
        /// Использование сцепления 0..1+ (больше 1 — скольжение).
        public float Utilization;
    }

    /// Модель шины: нормированная кривая боковой силы от угла увода (рост до пика, затем спад к SlideGrip),
    /// продольная сила ограничена сцеплением, обе складываются по эллипсу трения.
    /// Если продольная сила упирается в предел (пробуксовка, блокировка), боковая резко падает — так
    /// получается занос от газа на заднем приводе, от ручника и от блокировки колёс. Чистые функции.
    public static class TireModel
    {
        /// Номинальная нагрузка на колесо, Н, для чувствительности к нагрузке.
        public const float NominalLoad = 3200f;

        /// Показатель «эллипса» трения: 2 — строгий эллипс, больше — шина чуть щедрее на комбинированное сцепление.
        const float CombinedExponent = 2.4f;

        /// Доля сцепления, оставшаяся в одном направлении, если в другом использована доля used (0..1).
        public static float CombinedLimit(float used)
        {
            used = Mathf.Clamp01(used);
            return Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(used, CombinedExponent)), 1f / CombinedExponent);
        }

        /// Доля боковой силы при нормированном угле x = угол / пиковый угол (знак не учитывается).
        public static float LateralCurve(float x, float slideGrip)
        {
            x = Mathf.Abs(x);
            if (x <= 1f) return x * (2f - x);
            float t = Mathf.Clamp01((x - 1f) / 2.5f);
            return Mathf.Lerp(1f, slideGrip, t * t * (3f - 2f * t));
        }

        /// Коэффициент сцепления с учётом нагрузки: нагруженная шина держит чуть хуже на килограмм.
        public static float LoadedGrip(float mu, float load, float sensitivity)
        {
            float k = 1f - sensitivity * (load / NominalLoad - 1f);
            return mu * Mathf.Clamp(k, 0.6f, 1.25f);
        }

        /// Угол увода, градусы. На малой скорости знаменатель ограничен, чтобы не было шума.
        public static float SlipAngle(float vLong, float vLat) => Mathf.Atan2(vLat, Mathf.Max(Mathf.Abs(vLong), 1.5f)) * Mathf.Rad2Deg;

        /// Силы шины. longDemand — желаемая продольная сила (привод минус тормоз), Н.
        /// vLat — боковая скорость пятна, м/с; slipAngle — угол увода, градусы.
        public static TireForce Solve(float longDemand, float vLat, float slipAngle, float load,
                                      float muLong, float muLat, float peakAngle, float slideGrip,
                                      float lowSpeedBlend, float lowSpeedLateral)
        {
            var f = new TireForce();
            if (load <= 0f) return f;

            float maxLong = muLong * load;
            float maxLat = muLat * load;

            // Боковая: по кривой на ходу, на месте — сила, гасящая боковую скорость (без дрожания).
            float dynamic = -Mathf.Sign(slipAngle) * LateralCurve(slipAngle / Mathf.Max(0.5f, peakAngle), slideGrip) * maxLat;
            float lateral = Mathf.Lerp(Mathf.Clamp(lowSpeedLateral, -maxLat, maxLat), dynamic, lowSpeedBlend);

            float longUse = Mathf.Abs(longDemand) / maxLong;
            if (longUse > 1f)
            {
                // Пробуксовка или блокировка: продольная падает к силе скольжения, боковая почти пропадает.
                float slide = Mathf.Lerp(1f, slideGrip, Mathf.Clamp01((longUse - 1f) / 0.6f));
                f.Longitudinal = Mathf.Sign(longDemand) * maxLong * slide;
                f.Excess = Mathf.Abs(longDemand) - maxLong * slide;
                f.LongSlip = true;
                float latCap = maxLat * slide * 0.45f;
                f.Lateral = Mathf.Clamp(lateral, -latCap, latCap);
                f.Utilization = longUse;
            }
            else
            {
                f.Longitudinal = longDemand;
                float latCap = maxLat * CombinedLimit(longUse);
                f.Lateral = Mathf.Clamp(lateral, -latCap, latCap);
                float latUse = Mathf.Abs(slipAngle) / Mathf.Max(0.5f, peakAngle);
                f.Utilization = Mathf.Sqrt(longUse * longUse + latUse * latUse);
            }
            return f;
        }
    }
}
