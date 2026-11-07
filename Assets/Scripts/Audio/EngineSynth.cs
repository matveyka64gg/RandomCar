using UnityEngine;

namespace RandomCar.Audio
{
    /// Синтез звука машины в реальном времени (аудиопоток): двигатель по оборотам и нагрузке
    /// (гармоники частоты вспышек, полупорядок для «рыка», импульсы сгорания, впуск), свист и шипение
    /// турбины по скорости вала, «пш-ш» перепускного клапана, визг шин, хруст грунта, ветер.
    /// Внешних аудиофайлов нет. Параметры пишет главный поток, аудиопоток только читает.
    [RequireComponent(typeof(AudioSource))]
    public sealed class EngineSynth : MonoBehaviour
    {
        // Параметры от главного потока.
        public float Rpm = 900f;
        public float MaxRpm = 7500f;
        public float Load;
        public float TurboShaft;
        public float Boost;
        public bool Limiter;
        public float TireSkid;
        public float Gravel;
        public float Wind;
        public float Volume = 1f;
        public int Cylinders = 4;

        int blowOffCount;
        float blowOffStrength;

        int sampleRate = 48000;
        double phase, halfPhase, whistlePhase, squealPhase, t;
        float rpmSmooth = 900f, loadSmooth, shaftSmooth, skidSmooth, gravelSmooth, windSmooth;
        float blowOff;
        int seenBlowOff;
        float lpIntake, lpHiss, hpHissPrev, lpBlowA, lpBlowB, lpSquealA, lpSquealB, lpGravel, lpWind;
        uint noiseState = 22222u;
        float limiterGate = 1f;
        readonly float[] harmonic = new float[7];

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            noiseState = (uint)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this) * 2654435761u | 1u;
        }

        /// Срабатывание перепускного клапана (сила 0..1).
        public void TriggerBlowOff(float strength)
        {
            blowOffStrength = Mathf.Clamp01(strength);
            blowOffCount++;
        }

        float Noise()
        {
            noiseState ^= noiseState << 13;
            noiseState ^= noiseState >> 17;
            noiseState ^= noiseState << 5;
            return (noiseState & 0xFFFFFF) / 8388608f - 1f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float dt = 1f / sampleRate;
            float rpmTarget = Rpm, loadTarget = Load, shaftTarget = TurboShaft;
            float skidTarget = TireSkid, gravelTarget = Gravel, windTarget = Wind;
            float maxRpm = Mathf.Max(1000f, MaxRpm);
            float volume = Volume;
            float cyl = Mathf.Max(1, Cylinders) * 0.5f;
            bool limiter = Limiter;
            if (blowOffCount != seenBlowOff)
            {
                seenBlowOff = blowOffCount;
                blowOff = Mathf.Max(blowOff, 0.35f + 0.65f * blowOffStrength);
            }

            // Яркость гармоник меняется медленно: считаем раз на буфер.
            float bright = 1.7f - 0.9f * loadSmooth;
            for (int k = 1; k <= 6; k++) harmonic[k] = 1f / Mathf.Pow(k, bright);

            const float smooth = 0.0009f;
            for (int i = 0; i < data.Length; i += channels)
            {
                rpmSmooth += (rpmTarget - rpmSmooth) * 0.0025f;
                loadSmooth += (loadTarget - loadSmooth) * 0.0015f;
                shaftSmooth += (shaftTarget - shaftSmooth) * smooth;
                skidSmooth += (skidTarget - skidSmooth) * 0.002f;
                gravelSmooth += (gravelTarget - gravelSmooth) * 0.002f;
                windSmooth += (windTarget - windSmooth) * 0.001f;
                limiterGate += ((limiter ? 0.25f : 1f) - limiterGate) * 0.01f;

                float rpm01 = rpmSmooth / maxRpm;
                float f = rpmSmooth / 60f * cyl;
                phase += f * dt;
                if (phase >= 1.0) phase -= 1.0;
                halfPhase += f * 0.5 * dt;
                if (halfPhase >= 1.0) halfPhase -= 1.0;
                t += dt;
                if (t > 1000.0) t -= 1000.0;

                float p = (float)phase;
                float load = loadSmooth;

                // Гармоники вспышек: под нагрузкой ярче и жёстче.
                float eng = 0f;
                for (int k = 1; k <= 6; k++)
                    eng += Mathf.Sin(2f * Mathf.PI * (k * p + 0.11f * k)) * harmonic[k];
                float sub = Mathf.Sin(2f * Mathf.PI * (float)halfPhase) * (0.5f - 0.25f * rpm01);
                float pulse = Mathf.Exp(-p * 16f) * Noise() * (0.25f + 0.55f * load);
                lpIntake += (Noise() - lpIntake) * (0.05f + 0.2f * rpm01);
                float intake = lpIntake * (0.08f + 0.5f * load * rpm01);

                float gain = (0.42f + 0.58f * load) * (0.55f + 0.45f * rpm01) * limiterGate;
                float engine = Tanh((eng * 0.5f + sub * 0.45f + pulse * 0.6f + intake) * gain * 1.5f) * 0.55f;

                // Турбина: свист вала и шипение сжатого воздуха.
                float shaft = shaftSmooth;
                whistlePhase += (1400f + 5800f * shaft) * dt;
                if (whistlePhase >= 1.0) whistlePhase -= 1.0;
                float wp = 2f * Mathf.PI * (float)whistlePhase;
                float whistle = (Mathf.Sin(wp) + 0.3f * Mathf.Sin(wp * 2.01f)) * shaft * shaft * (0.35f + 0.65f * load) * 0.075f;
                float n = Noise();
                lpHiss += (n - lpHiss) * 0.45f;
                float hiss = (lpHiss - hpHissPrev) * shaft * load * 0.06f;
                hpHissPrev = lpHiss;

                // Перепускной клапан: полосовой шум с быстрым спадом и лёгким «трепетом».
                float bo = 0f;
                if (blowOff > 0.001f)
                {
                    float x = Noise();
                    lpBlowA += (x - lpBlowA) * 0.55f;
                    lpBlowB += (lpBlowA - lpBlowB) * 0.08f;
                    float flutter = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 26f * (float)t);
                    bo = (lpBlowA - lpBlowB) * blowOff * flutter * 0.9f;
                    blowOff *= 1f - 7f * dt;
                }

                // Шины: тональный визг с плавающей высотой плюс шум.
                float squeal = 0f;
                if (skidSmooth > 0.002f)
                {
                    float sf = 820f + 140f * Mathf.Sin(2f * Mathf.PI * 2.7f * (float)t) + 60f * Mathf.Sin(2f * Mathf.PI * 7.3f * (float)t);
                    squealPhase += sf * dt;
                    if (squealPhase >= 1.0) squealPhase -= 1.0;
                    float sp = 2f * Mathf.PI * (float)squealPhase;
                    float x = Noise();
                    lpSquealA += (x - lpSquealA) * 0.3f;
                    lpSquealB += (lpSquealA - lpSquealB) * 0.12f;
                    squeal = (Mathf.Sin(sp) * 0.55f + Mathf.Sin(sp * 1.51f) * 0.2f + (lpSquealA - lpSquealB) * 0.9f) * skidSmooth * 0.16f;
                }

                // Грунт: низкий шорох с редкими щелчками камешков.
                float gravel = 0f;
                if (gravelSmooth > 0.002f)
                {
                    lpGravel += (Noise() - lpGravel) * 0.12f;
                    float click = Noise() > 0.995f ? Noise() * 0.8f : 0f;
                    gravel = (lpGravel * 0.6f + click) * gravelSmooth * 0.2f;
                }

                float wind = 0f;
                if (windSmooth > 0.002f)
                {
                    lpWind += (Noise() - lpWind) * (0.02f + 0.05f * windSmooth);
                    wind = lpWind * windSmooth * 0.5f;
                }

                float s = (engine + whistle + hiss + bo + squeal + gravel + wind) * volume;
                for (int c = 0; c < channels; c++) data[i + c] = s;
            }
        }

        static float Tanh(float x)
        {
            if (x > 3f) return 1f;
            if (x < -3f) return -1f;
            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }
    }
}
