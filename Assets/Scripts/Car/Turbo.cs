using UnityEngine;

namespace RandomCar.Car
{
    /// Турбина как часть двигателя. Давление стремится к цели, которая зависит от оборотов и нагрузки (газа);
    /// рост идёт с задержкой, тем быстрее, чем больше выхлопа (обороты × газ). При резком сбросе газа
    /// под давлением открывается перепускной клапан: давление быстро падает, звучит «пш-ш».
    /// Чистый C#, без Unity-объектов: тестируется в EditMode.
    public sealed class Turbo
    {
        const float BlowOffOpenSeconds = 0.35f;

        readonly TurboSpec spec;
        float previousLoad;
        float valveOpenFor;

        public Turbo(TurboSpec turboSpec) => spec = turboSpec;

        /// Текущее давление наддува, бар.
        public float Boost { get; private set; }

        /// Давление 0..1 от максимального.
        public float Boost01 => spec.MaxBoost > 0f ? Boost / spec.MaxBoost : 0f;

        /// Скорость вала 0..1: отстаёт от давления и медленнее падает (для свиста).
        public float ShaftSpeed01 { get; private set; }

        /// Перепускной клапан сейчас открыт.
        public bool BlowOffOpen => valveOpenFor > 0f;

        /// Срабатывание клапана: сила 0..1. Для звука и эффектов.
        public event System.Action<float> BlowOff;

        /// Множитель момента от текущего давления.
        public float TorqueMultiplier => 1f + spec.TorquePerBar * Boost;

        public void Reset()
        {
            Boost = 0f;
            ShaftSpeed01 = 0f;
            previousLoad = 0f;
            valveOpenFor = 0f;
        }

        /// load — открытие дросселя 0..1 (0 при переключении и на отсечке), rpm — обороты двигателя.
        /// Турбина есть (у атмосферных двигателей давление 0 — ни наддува, ни свиста).
        public bool Present => spec.MaxBoost > 0.01f;

        public void Step(float load, float rpm, float maxRpm, float dt)
        {
            if (dt <= 0f) return;
            if (!Present)
            {
                Boost = 0f;
                ShaftSpeed01 = 0f;
                return;
            }
            load = Mathf.Clamp01(load);

            // Резкое закрытие дросселя под давлением — клапан.
            if (previousLoad > 0.55f && load < 0.2f && Boost > spec.BlowOffThreshold && valveOpenFor <= 0f)
            {
                valveOpenFor = BlowOffOpenSeconds;
                BlowOff?.Invoke(Mathf.Clamp01(Boost / Mathf.Max(0.01f, spec.MaxBoost)));
            }
            previousLoad = load;

            float rpmFactor = Smooth(Mathf.InverseLerp(spec.SpoolStartRpm, spec.FullBoostRpm, rpm));
            float target = spec.MaxBoost * load * rpmFactor;

            float rate;
            if (target > Boost)
            {
                // Чем больше выхлопа, тем быстрее раскручивается турбина: задержка сильнее на низах.
                float exhaust = 0.2f + 0.8f * Mathf.Clamp01(rpm / Mathf.Max(1f, maxRpm)) * (0.3f + 0.7f * load);
                rate = exhaust / Mathf.Max(0.05f, spec.Lag);
            }
            else
            {
                rate = valveOpenFor > 0f ? 9f : 1f / Mathf.Max(0.05f, spec.Decay);
            }
            Boost += (target - Boost) * (1f - Mathf.Exp(-rate * dt));
            Boost = Mathf.Clamp(Boost, 0f, spec.MaxBoost);

            valveOpenFor = Mathf.Max(0f, valveOpenFor - dt);

            // Вал: раскручивается за давлением, тормозится медленнее (свист затихает плавно).
            float shaftTarget = Mathf.Max(Boost01, rpmFactor * load * 0.6f);
            float shaftRate = shaftTarget > ShaftSpeed01 ? 6f : 1.6f;
            ShaftSpeed01 += (shaftTarget - ShaftSpeed01) * (1f - Mathf.Exp(-shaftRate * dt));
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
