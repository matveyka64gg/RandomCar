using System;
using UnityEngine;

namespace RandomCar.Audio
{
    /// Процедурные звуки: синтез из синусов, пилы и шума при запуске игры. Внешних аудиофайлов нет.
    public static class SoundBank
    {
        const int Rate = 44100;

        static AudioClip uiMove, uiClick, beep, go, warnBad, warnGood, explosion, thud, whoosh, engine, crowd, bleat, pop;

        public static AudioClip UiMove => Get(ref uiMove, () => Make("ui_move", 0.05f, t => Tone(t, 1250f) * Env(t, 0.002f, 0.04f) * 0.35f));
        public static AudioClip UiClick => Get(ref uiClick, () => Make("ui_click", 0.09f, t => (Tone(t, 880f) + 0.5f * Tone(t, 1320f)) * Env(t, 0.002f, 0.07f) * 0.4f));
        public static AudioClip Beep => Get(ref beep, () => Make("beep", 0.22f, t => Square(t, 660f) * Env(t, 0.004f, 0.2f) * 0.22f));
        public static AudioClip Go => Get(ref go, () => Make("go", 0.6f, t => (Square(t, 990f) * 0.6f + Tone(t, 1980f) * 0.3f) * Env(t, 0.004f, 0.55f) * 0.24f));

        public static AudioClip WarnBad => Get(ref warnBad, () => Make("warn_bad", 0.5f, t =>
            (t < 0.22f ? Square(t, 440f) * Env(t, 0.004f, 0.2f) : Square(t, 330f) * Env(t - 0.25f, 0.004f, 0.22f)) * 0.2f));

        public static AudioClip WarnGood => Get(ref warnGood, () => Make("warn_good", 0.45f, t =>
            (Tone(t, Mathf.Lerp(660f, 1320f, t / 0.45f)) + 0.4f * Tone(t, Mathf.Lerp(990f, 1980f, t / 0.45f))) * Env(t, 0.01f, 0.42f) * 0.3f));

        public static AudioClip Explosion => Get(ref explosion, () => Filtered("explosion", 1.6f, 0.08f, t => Noise() * Mathf.Exp(-t * 3.2f) * 1.2f + Tone(t, 48f) * Mathf.Exp(-t * 4f) * 0.8f));
        public static AudioClip Thud => Get(ref thud, () => Make("thud", 0.35f, t => (Tone(t, 70f * (1f - t)) * 0.9f + Noise() * 0.25f * Mathf.Exp(-t * 30f)) * Mathf.Exp(-t * 9f) * 0.8f));
        public static AudioClip Whoosh => Get(ref whoosh, () => Filtered("whoosh", 0.9f, 0.3f, t => Noise() * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.9f)) * 0.9f));
        public static AudioClip Pop => Get(ref pop, () => Make("pop", 0.25f, t => (Tone(t, 520f + 900f * t) * 0.6f + Noise() * 0.2f) * Env(t, 0.002f, 0.2f) * 0.5f));

        /// «Бее»: вибрато на носовом тоне.
        public static AudioClip Bleat => Get(ref bleat, () => Make("bleat", 0.6f, t =>
        {
            float f = 380f + 25f * Mathf.Sin(t * 2f * Mathf.PI * 22f);
            return (Saw(t, f) * 0.5f + Tone(t, f * 2f) * 0.3f) * Env(t, 0.03f, 0.5f) * 0.25f;
        }));

        /// Мотор: гармоники на 55 Гц, ровно 55 периодов за секунду, поэтому петля без щелчков.
        public static AudioClip Engine => Get(ref engine, () => Make("engine", 1f, t =>
            Saw(t, 55f) * 0.35f + Tone(t, 110f) * 0.25f + Tone(t, 165f) * 0.12f + Square(t, 27.5f) * 0.12f, loop: true));

        /// Шум трибун: отфильтрованный шум с медленной волной.
        public static AudioClip Crowd => Get(ref crowd, () => Filtered("crowd", 4f, 0.06f, t => Noise() * (0.6f + 0.4f * Mathf.Sin(t * Mathf.PI / 2f)) * 0.5f));


        /// Клипы живут только в одной Play-сессии. Проверка через оператор Unity: уничтоженный клип считается null
        /// (в проекте Play запускается без перезагрузки домена, статические ссылки переживают сессию).
        static AudioClip Get(ref AudioClip clip, Func<AudioClip> create)
        {
            if (clip == null) clip = create();
            return clip;
        }

        // ---------- синтез ----------

        static readonly System.Random random = new System.Random(5);

        static float Tone(float t, float f) => Mathf.Sin(2f * Mathf.PI * f * t);
        static float Square(float t, float f) => Mathf.Sign(Mathf.Sin(2f * Mathf.PI * f * t)) * 0.7f;
        static float Saw(float t, float f) => 2f * (t * f - Mathf.Floor(t * f + 0.5f));
        static float Noise() => (float)(random.NextDouble() * 2.0 - 1.0);

        /// Огибающая: быстрая атака и спад к концу.
        static float Env(float t, float attack, float length)
        {
            if (t < 0f) return 0f;
            if (t < attack) return t / attack;
            return Mathf.Clamp01(1f - (t - attack) / Mathf.Max(0.001f, length - attack));
        }

        static AudioClip Make(string name, float seconds, Func<float, float> f, bool loop = false)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
            if (!loop)
            {
                // Короткое затухание в конце убирает щелчок.
                int fade = Mathf.Min(n, 220);
                for (int i = 0; i < fade; i++) data[n - 1 - i] *= i / (float)fade;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// Однополюсный фильтр низких частот: из шума получается гул взрыва или порыв ветра.
        static AudioClip Filtered(string name, float seconds, float smoothing, Func<float, float> f)
        {
            float state = 0f;
            return Make(name, seconds, t =>
            {
                state += (f(t) - state) * smoothing;
                return state * 2.2f;
            });
        }
    }
}
