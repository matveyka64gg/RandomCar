using System;
using UnityEngine;

namespace RandomCar.Audio
{
    /// Процедурные звуки: синтез из синусов, гармоник и фильтрованного шума при запуске игры. Внешних аудиофайлов нет.
    /// Звук двигателя, турбины и шин синтезируется в реальном времени (EngineSynth), здесь — короткие клипы.
    public static class SoundBank
    {
        const int Rate = 44100;
        const float TwoPi = Mathf.PI * 2f;

        static AudioClip uiMove, uiClick, beep, go, warnBad, warnGood, explosion, thud, clank, whoosh, crowd, bleat, pop;
        static AudioClip boing, bonk, gong, knock, mineBeep, pshik, bigBoom, miniPop, whistle, crackle, whack, spring, iceCrack, jelly, warp, ding, squeak;
        static AudioClip hiccup, sneeze, slap, anvil, fan, magnet, zap, cannon, ufo, pinball, splat, slurp, quack, thwomp, tornado, coil;
        static AudioClip pickup, gunShot, horn, rocketHiss, lockOn;
        static AudioClip verityJump, verityLand, verityGiggle, busHorn, tireScreech, busCrash;

        /// Мягкий щелчок навигации.
        public static AudioClip UiMove => Get(ref uiMove, () => Make("ui_move", 0.07f, t =>
            Tone(t, 1500f - 300f * t / 0.07f) * Env(t, 0.002f, 0.06f) * 0.28f));

        /// Подтверждение: две ноты вверх.
        public static AudioClip UiClick => Get(ref uiClick, () => Make("ui_click", 0.16f, t =>
            (Bell(t, 880f, 30f) * Step(t, 0f) + Bell(t - 0.05f, 1320f, 30f) * Step(t, 0.05f)) * 0.32f));

        /// Сигнал отсчёта: тёплый тон с хвостом.
        public static AudioClip Beep => Get(ref beep, () => Echo(Make("beep", 0.55f, t =>
            Harm(t, 660f, 4) * Env(t, 0.004f, 0.22f) * 0.24f), 0.09f, 0.28f));

        /// «Старт!»: яркий аккорд.
        public static AudioClip Go => Get(ref go, () => Echo(Make("go", 0.95f, t =>
            (Harm(t, 990f, 4) * 0.6f + Harm(t, 1485f, 3) * 0.4f + Tone(t, 495f) * 0.25f) * Env(t, 0.004f, 0.7f) * 0.24f), 0.11f, 0.3f));

        /// Опасность: двухтональная сирена с тремоло.
        public static AudioClip WarnBad => Get(ref warnBad, () => Make("warn_bad", 0.6f, t =>
        {
            float f = t < 0.28f ? 520f : 390f;
            float local = t < 0.28f ? t : t - 0.3f;
            float trem = 0.8f + 0.2f * Mathf.Sin(TwoPi * 18f * t);
            return Harm(t, f, 5) * Env(local, 0.006f, 0.26f) * trem * 0.22f;
        }));

        /// Польза: восходящие колокольчики.
        public static AudioClip WarnGood => Get(ref warnGood, () => Echo(Make("warn_good", 0.7f, t =>
            (Bell(t, 784f, 9f) + Bell(t - 0.08f, 1047f, 9f) * Step(t, 0.08f) + Bell(t - 0.16f, 1568f, 8f) * Step(t, 0.16f)) * 0.2f), 0.07f, 0.25f));

        /// Взрыв: низкий удар, треск и обломки.
        public static AudioClip Explosion => Get(ref explosion, () =>
        {
            float lp = 0f, lp2 = 0f;
            return Make("explosion", 2.2f, t =>
            {
                float boom = Mathf.Sin(TwoPi * (70f * t - 18f * t * t)) * Mathf.Exp(-t * 3.5f) * 0.9f;
                float cutoff = Mathf.Lerp(0.5f, 0.03f, Mathf.Clamp01(t / 0.9f));
                lp += (Noise() - lp) * cutoff;
                lp2 += (lp - lp2) * 0.2f;
                float body = lp2 * Mathf.Exp(-t * 2.4f) * 2.4f;
                float crack = Noise() * Mathf.Exp(-t * 28f) * 0.7f;
                float debris = t > 0.25f && Noise() > 0.985f ? Noise() * 0.35f * Mathf.Exp(-(t - 0.25f) * 2.5f) : 0f;
                return Mathf.Clamp(boom + body + crack + debris, -1f, 1f);
            });
        });

        /// Глухой удар кузова.
        public static AudioClip Thud => Get(ref thud, () =>
        {
            float lp = 0f;
            return Make("thud", 0.45f, t =>
            {
                lp += (Noise() - lp) * 0.08f;
                return (Mathf.Sin(TwoPi * (95f * t - 55f * t * t)) * 0.9f + lp * 1.6f) * Mathf.Exp(-t * 11f) * 0.85f;
            });
        });

        /// Металлический лязг: негармонические моды с разным затуханием.
        public static AudioClip Clank => Get(ref clank, () => Make("clank", 0.6f, t =>
            (Mathf.Sin(TwoPi * 523f * t) * Mathf.Exp(-t * 9f) * 0.5f
             + Mathf.Sin(TwoPi * 1187f * t) * Mathf.Exp(-t * 14f) * 0.35f
             + Mathf.Sin(TwoPi * 1873f * t) * Mathf.Exp(-t * 20f) * 0.25f
             + Mathf.Sin(TwoPi * 2711f * t) * Mathf.Exp(-t * 30f) * 0.2f
             + Noise() * Mathf.Exp(-t * 60f) * 0.6f) * 0.6f));

        /// Порыв: шум с плавающим фильтром.
        public static AudioClip Whoosh => Get(ref whoosh, () =>
        {
            float a = 0f, b = 0f;
            return Make("whoosh", 1.0f, t =>
            {
                float sweep = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.0f));
                float cutoff = 0.04f + 0.3f * sweep;
                a += (Noise() - a) * cutoff;
                b += (a - b) * 0.05f;
                return (a - b) * sweep * 1.8f;
            });
        });

        /// Хлопок бонуса: пузырь и искорка.
        public static AudioClip Pop => Get(ref pop, () => Make("pop", 0.35f, t =>
            (Mathf.Sin(TwoPi * (420f * t + 1500f * t * t)) * Env(t, 0.002f, 0.12f) * 0.55f
             + Bell(t - 0.04f, 2093f, 14f) * Step(t, 0.04f) * 0.25f) * 0.7f));

        /// «Бее»: вибрато и носовая окраска.
        public static AudioClip Bleat => Get(ref bleat, () => Make("bleat", 0.7f, t =>
        {
            float f = 360f + 30f * Mathf.Sin(TwoPi * 21f * t) * Mathf.Clamp01(t * 4f);
            return (Saw(t, f) * 0.45f + Tone(t, f * 2f) * 0.3f + Tone(t, f * 3f) * 0.15f) * Env(t, 0.04f, 0.6f) * 0.24f;
        }));

        /// Трибуны: шум толпы с волнами и редкими выкриками; петля без шва.
        public static AudioClip Crowd => Get(ref crowd, () =>
        {
            const float length = 6f;
            float a = 0f, b = 0f;
            var clip = Make("crowd_raw", length, t =>
            {
                a += (Noise() - a) * 0.08f;
                b += (a - b) * 0.02f;
                float swell = 0.55f + 0.25f * Mathf.Sin(TwoPi * t / length) + 0.2f * Mathf.Sin(TwoPi * 3f * t / length + 1f);
                float cheer = Mathf.Sin(TwoPi * (600f + 80f * Mathf.Sin(TwoPi * 0.5f * t)) * t) * 0.04f * Mathf.Max(0f, Mathf.Sin(TwoPi * 2f * t / length));
                return ((a - b) * 1.6f + cheer) * swell;
            }, loop: true);
            return Loopify(clip, 0.5f);
        });

        // ---------- хаос v4: мультяшные звуки ----------

        /// Резиновый мяч: «бойнь» с качающимся тоном.
        public static AudioClip Boing => Get(ref boing, () => Make("boing", 0.7f, t =>
        {
            float f = 170f + 260f * Mathf.Exp(-t * 7f) + 45f * Mathf.Sin(TwoPi * 13f * t) * Mathf.Exp(-t * 3f);
            return (Mathf.Sin(TwoPi * f * t) * 0.8f + Mathf.Sin(TwoPi * f * 2f * t) * 0.2f) * Env(t, 0.003f, 0.7f) * 0.6f;
        }));

        /// Пляжный мяч: пустой «бонк».
        public static AudioClip Bonk => Get(ref bonk, () =>
        {
            float lp = 0f;
            return Make("bonk", 0.32f, t =>
            {
                lp += (Noise() - lp) * 0.15f;
                return (Mathf.Sin(TwoPi * (210f * t - 160f * t * t)) * 0.8f + lp * 0.6f * Mathf.Exp(-t * 40f)) * Mathf.Exp(-t * 13f) * 0.75f;
            });
        });

        /// Стальной шар: тяжёлый гонг с долгим хвостом.
        public static AudioClip Gong => Get(ref gong, () => Make("gong", 1.5f, t =>
            (Mathf.Sin(TwoPi * 174f * t) * Mathf.Exp(-t * 2.2f) * 0.5f
             + Mathf.Sin(TwoPi * 431f * t) * Mathf.Exp(-t * 3.5f) * 0.35f
             + Mathf.Sin(TwoPi * 697f * t) * Mathf.Exp(-t * 5f) * 0.25f
             + Mathf.Sin(TwoPi * 1021f * t) * Mathf.Exp(-t * 8f) * 0.2f
             + Noise() * Mathf.Exp(-t * 50f) * 0.5f) * 0.65f));

        /// Деревянная бочка: глухой стук.
        public static AudioClip Knock => Get(ref knock, () => Make("knock", 0.28f, t =>
            (Mathf.Sin(TwoPi * 310f * t) * Mathf.Exp(-t * 22f) * 0.7f + Mathf.Sin(TwoPi * 745f * t) * Mathf.Exp(-t * 35f) * 0.4f
             + Noise() * Mathf.Exp(-t * 80f) * 0.5f) * 0.8f));

        /// Мина взводится: короткий писк.
        public static AudioClip MineBeep => Get(ref mineBeep, () => Make("mine_beep", 0.11f, t =>
            Harm(t, 1760f, 3) * Env(t, 0.002f, 0.1f) * 0.3f));

        /// Ложная мина: «пшик» и обиженный «пфф».
        public static AudioClip Pshik => Get(ref pshik, () =>
        {
            float a = 0f, b = 0f;
            return Make("pshik", 0.8f, t =>
            {
                a += (Noise() - a) * 0.5f;
                b += (a - b) * 0.12f;
                float hiss = (a - b) * Mathf.Exp(-t * 6f) * 1.2f;
                float f = 140f - 60f * t;
                float raspberry = Saw(t, f) * 0.25f * Mathf.Clamp01((t - 0.15f) * 8f) * Mathf.Exp(-(t - 0.15f) * 4f) * (0.7f + 0.3f * Mathf.Sin(TwoPi * 31f * t));
                return hiss + (t > 0.15f ? raspberry : 0f);
            });
        });

        /// Большой мультяшный взрыв: удар, гул и треск обломков.
        public static AudioClip BigBoom => Get(ref bigBoom, () =>
        {
            float lp = 0f, lp2 = 0f;
            return Make("big_boom", 2.8f, t =>
            {
                float boom = Mathf.Sin(TwoPi * (52f * t - 10f * t * t)) * Mathf.Exp(-t * 2.6f) * 1.0f;
                float cutoff = Mathf.Lerp(0.6f, 0.02f, Mathf.Clamp01(t / 1.2f));
                lp += (Noise() - lp) * cutoff;
                lp2 += (lp - lp2) * 0.25f;
                float body = lp2 * Mathf.Exp(-t * 1.8f) * 2.6f;
                float crack = Noise() * Mathf.Exp(-t * 22f) * 0.8f;
                float debris = t > 0.3f && Noise() > 0.975f ? Noise() * 0.4f * Mathf.Exp(-(t - 0.3f) * 1.8f) : 0f;
                return Mathf.Clamp(boom + body + crack + debris, -1f, 1f);
            });
        });

        /// Маленький взрыв цепочки.
        public static AudioClip MiniPop => Get(ref miniPop, () =>
        {
            float lp = 0f;
            return Make("mini_pop", 0.55f, t =>
            {
                lp += (Noise() - lp) * Mathf.Lerp(0.6f, 0.05f, Mathf.Clamp01(t / 0.4f));
                return (Mathf.Sin(TwoPi * (120f * t - 60f * t * t)) * 0.6f + lp * 1.5f) * Mathf.Exp(-t * 7f) * 0.8f;
            });
        });

        /// Фейерверк взлетает: свист вверх.
        public static AudioClip Whistle => Get(ref whistle, () => Make("whistle", 0.9f, t =>
        {
            float f = 700f + 1900f * t / 0.9f;
            return (Mathf.Sin(TwoPi * f * t) * 0.5f + Noise() * 0.12f) * Env(t, 0.05f, 0.9f) * 0.35f;
        }));

        /// Фейерверк раскрылся: хлопок и треск искр.
        public static AudioClip Crackle => Get(ref crackle, () => Make("crackle", 1.6f, t =>
        {
            float boom = Mathf.Sin(TwoPi * (90f * t - 30f * t * t)) * Mathf.Exp(-t * 9f) * 0.7f + Noise() * Mathf.Exp(-t * 30f) * 0.6f;
            float sparks = t > 0.12f && Noise() > 0.93f ? Noise() * 0.7f * Mathf.Exp(-(t - 0.12f) * 2.2f) : 0f;
            return Mathf.Clamp(boom + sparks, -1f, 1f) * 0.8f;
        }));

        /// Удар молота или перчатки: мультяшное «БАМ».
        public static AudioClip Whack => Get(ref whack, () =>
        {
            float lp = 0f;
            return Make("whack", 0.4f, t =>
            {
                lp += (Noise() - lp) * 0.3f;
                return (Mathf.Sin(TwoPi * (380f * t - 520f * t * t)) * 0.8f + lp * Mathf.Exp(-t * 60f) * 1.2f) * Mathf.Exp(-t * 9f) * 0.8f;
            });
        });

        /// Батут: «бойоойоинг».
        public static AudioClip Spring => Get(ref spring, () => Make("spring", 0.9f, t =>
        {
            float f = 200f + 160f * t + 70f * Mathf.Sin(TwoPi * 24f * t) * Mathf.Exp(-t * 2.5f);
            return (Mathf.Sin(TwoPi * f * t) * 0.7f + Saw(t, f) * 0.15f) * Env(t, 0.004f, 0.9f) * 0.55f;
        }));

        /// Лёд: хруст и звон.
        public static AudioClip IceCrack => Get(ref iceCrack, () => Make("ice_crack", 0.6f, t =>
            (Noise() > 0.9f ? Noise() * Mathf.Exp(-t * 7f) : 0f) * 0.9f + Bell(t, 2400f, 18f) * 0.15f + Bell(t - 0.1f, 3100f, 20f) * Step(t, 0.1f) * 0.1f));

        /// Желе: колыхание.
        public static AudioClip Jelly => Get(ref jelly, () => Make("jelly", 0.7f, t =>
            Mathf.Sin(TwoPi * (110f + 30f * Mathf.Sin(TwoPi * 7f * t)) * t) * (0.5f + 0.5f * Mathf.Sin(TwoPi * 9f * t)) * Env(t, 0.02f, 0.7f) * 0.6f));

        /// Уменьшатель и невесомость: космическое «вуууп».
        public static AudioClip Warp => Get(ref warp, () => Make("warp", 0.9f, t =>
        {
            float f = 1400f * Mathf.Exp(-t * 2.6f) + 120f;
            return (Mathf.Sin(TwoPi * f * t) * 0.6f + Harm(t, f * 0.5f, 3) * 0.2f) * (0.7f + 0.3f * Mathf.Sin(TwoPi * 22f * t)) * Env(t, 0.01f, 0.9f) * 0.4f;
        }));

        /// Кольцо-рывок: звонкий «дзынь».
        public static AudioClip Ding => Get(ref ding, () => Echo(Make("ding", 0.8f, t =>
            (Bell(t, 1760f, 7f) + Bell(t, 2637f, 9f) * 0.5f) * 0.22f), 0.08f, 0.25f));

        /// Резиновая уточка (шары-малыши): писк.
        public static AudioClip Squeak => Get(ref squeak, () => Make("squeak", 0.26f, t =>
        {
            float f = 950f + 450f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.26f));
            return (Saw(t, f) * 0.4f + Tone(t, f) * 0.4f) * Env(t, 0.01f, 0.26f) * 0.35f;
        }));

        // ---------- хаос v5: звуки трюков (у каждого свой характер) ----------

        /// Икота: короткое «ик» с подскоком тона.
        public static AudioClip Hiccup => Get(ref hiccup, () => Make("hiccup", 0.22f, t =>
        {
            float f = 420f + 900f * Mathf.Clamp01(t / 0.05f) - 500f * Mathf.Clamp01((t - 0.05f) / 0.15f);
            return (Saw(t, f) * 0.35f + Tone(t, f * 2f) * 0.25f + Noise() * 0.15f) * Env(t, 0.004f, 0.2f) * 0.6f;
        }));

        /// Чих: вдох «а-а» и взрыв «ПЧХИ».
        public static AudioClip Sneeze => Get(ref sneeze, () =>
        {
            float lp = 0f;
            return Make("sneeze", 0.85f, t =>
            {
                lp += (Noise() - lp) * 0.35f;
                float inhale = t < 0.45f ? (Saw(t, 180f + 140f * t) * 0.2f + Noise() * 0.08f) * Mathf.Clamp01(t / 0.3f) : 0f;
                float burst = t >= 0.45f ? (Noise() * 0.9f + lp * 0.6f) * Mathf.Exp(-(t - 0.45f) * 9f) : 0f;
                return Mathf.Clamp(inhale + burst, -1f, 1f) * 0.8f;
            });
        });

        /// Гигантская рука: звонкая оплеуха.
        public static AudioClip Slap => Get(ref slap, () =>
        {
            float lp = 0f;
            return Make("slap", 0.45f, t =>
            {
                lp += (Noise() - lp) * 0.55f;
                return (lp * 1.6f * Mathf.Exp(-t * 28f) + Mathf.Sin(TwoPi * (160f * t - 80f * t * t)) * 0.5f * Mathf.Exp(-t * 10f)) * 0.85f;
            });
        });

        /// Наковальня: металлический «ДЗАНГ» с долгим звоном.
        public static AudioClip Anvil => Get(ref anvil, () => Make("anvil", 1.8f, t =>
            (Bell(t, 523f, 2.5f) * 0.5f + Bell(t, 1307f, 3.5f) * 0.35f + Bell(t, 2213f, 6f) * 0.25f + Bell(t, 3511f, 9f) * 0.15f
             + Noise() * Mathf.Exp(-t * 45f) * 0.7f + Mathf.Sin(TwoPi * 70f * t) * Mathf.Exp(-t * 6f) * 0.6f) * 0.7f));

        /// Вентилятор: нарастающий гул лопастей.
        public static AudioClip Fan => Get(ref fan, () =>
        {
            float lp = 0f;
            return Make("fan", 2.6f, t =>
            {
                lp += (Noise() - lp) * 0.08f;
                float rate = 8f + 22f * Mathf.Clamp01(t / 1f);
                float chop = 0.6f + 0.4f * Mathf.Sin(TwoPi * rate * t);
                return (lp * 2.4f * chop + Saw(t, 55f + 30f * Mathf.Clamp01(t)) * 0.12f) * Env(t, 0.3f, 2.6f) * 0.7f;
            });
        });

        /// Магнит: электрический гул.
        public static AudioClip Magnet => Get(ref magnet, () => Make("magnet", 2.4f, t =>
            (Saw(t, 60f) * 0.25f + Saw(t, 120.5f) * 0.18f + Tone(t, 240f) * 0.12f * (0.5f + 0.5f * Mathf.Sin(TwoPi * 6f * t))) * Env(t, 0.15f, 2.4f) * 0.7f));

        /// Магнит отпустил / искра: «дзззт».
        public static AudioClip Zap => Get(ref zap, () => Make("zap", 0.35f, t =>
            (Saw(t, 900f + 2500f * Mathf.Exp(-t * 12f)) * 0.3f + (Noise() > 0.6f ? Noise() * 0.4f : 0f)) * Mathf.Exp(-t * 8f) * 0.7f));

        /// Пушка: «БДЫЩ» с хлопком дыма.
        public static AudioClip Cannon => Get(ref cannon, () =>
        {
            float lp = 0f;
            return Make("cannon", 1.3f, t =>
            {
                lp += (Noise() - lp) * Mathf.Lerp(0.7f, 0.04f, Mathf.Clamp01(t / 0.5f));
                return Mathf.Clamp(Mathf.Sin(TwoPi * (75f * t - 30f * t * t)) * Mathf.Exp(-t * 4f) + lp * 2.2f * Mathf.Exp(-t * 3.5f), -1f, 1f) * 0.9f;
            });
        });

        /// НЛО: космическое «уиу-уиу».
        public static AudioClip Ufo => Get(ref ufo, () => Make("ufo", 3f, t =>
        {
            float f = 600f + 250f * Mathf.Sin(TwoPi * 3f * t) + 80f * Mathf.Sin(TwoPi * 0.5f * t);
            return (Tone(t, f) * 0.35f + Tone(t, f * 1.5f) * 0.12f) * Env(t, 0.2f, 3f) * 0.6f;
        }));

        /// Пинбол-бампер: двойной «динь-динь».
        public static AudioClip Pinball => Get(ref pinball, () => Make("pinball", 0.5f, t =>
            (Bell(t, 1568f, 14f) * 0.4f + Bell(t, 2349f, 16f) * 0.25f + Bell(t - 0.09f, 2093f, 14f) * Step(t, 0.09f) * 0.35f + Noise() * Mathf.Exp(-t * 90f) * 0.3f) * 0.8f));

        /// Жвачка: «чпок» и растяжение.
        public static AudioClip Splat => Get(ref splat, () =>
        {
            float lp = 0f;
            return Make("splat", 0.6f, t =>
            {
                lp += (Noise() - lp) * 0.2f;
                float f = 90f + 300f * Mathf.Clamp01(t / 0.4f);
                return (lp * Mathf.Exp(-t * 18f) * 1.4f + Tone(t, f) * 0.35f * Env(t, 0.02f, 0.6f)) * 0.8f;
            });
        });

        /// Масло: скользкое «вжжух».
        public static AudioClip Slurp => Get(ref slurp, () =>
        {
            float a = 0f, b = 0f;
            return Make("slurp", 0.7f, t =>
            {
                a += (Noise() - a) * 0.4f;
                b += (a - b) * 0.05f;
                float f = 300f - 200f * t;
                return ((a - b) * 0.7f + Tone(t, f) * 0.25f * (0.6f + 0.4f * Mathf.Sin(TwoPi * 18f * t))) * Env(t, 0.03f, 0.7f) * 0.7f;
            });
        });

        /// Резиновая утка: «кря».
        public static AudioClip Quack => Get(ref quack, () => Make("quack", 0.3f, t =>
        {
            float f = 520f - 260f * t / 0.3f;
            return (Saw(t, f) * 0.45f + Saw(t, f * 1.01f) * 0.3f) * (0.6f + 0.4f * Mathf.Sin(TwoPi * 40f * t)) * Env(t, 0.01f, 0.3f) * 0.5f;
        }));

        /// Кувалда: тяжёлое «ТУДУХ» с треском асфальта.
        public static AudioClip Thwomp => Get(ref thwomp, () =>
        {
            float lp = 0f;
            return Make("thwomp", 1.1f, t =>
            {
                lp += (Noise() - lp) * Mathf.Lerp(0.5f, 0.05f, Mathf.Clamp01(t / 0.3f));
                float crack = t > 0.05f && Noise() > 0.96f ? Noise() * 0.5f * Mathf.Exp(-(t - 0.05f) * 6f) : 0f;
                return Mathf.Clamp(Mathf.Sin(TwoPi * (48f * t - 12f * t * t)) * Mathf.Exp(-t * 4f) * 1.1f + lp * 1.6f * Mathf.Exp(-t * 6f) + crack, -1f, 1f) * 0.9f;
            });
        });

        /// Смерч: рёв ветра.
        public static AudioClip Tornado => Get(ref tornado, () =>
        {
            float lp = 0f, lp2 = 0f;
            return Make("tornado", 3.5f, t =>
            {
                lp += (Noise() - lp) * (0.05f + 0.04f * Mathf.Sin(TwoPi * 0.7f * t));
                lp2 += (lp - lp2) * 0.3f;
                return lp2 * 3.2f * (0.7f + 0.3f * Mathf.Sin(TwoPi * 1.3f * t)) * Env(t, 0.4f, 3.5f);
            });
        });

        /// Пружина выстреливает: «тюдюдюнь».
        public static AudioClip Coil => Get(ref coil, () => Make("coil", 0.75f, t =>
        {
            float f = 140f + 900f * Mathf.Clamp01(t / 0.12f) * Mathf.Exp(-t * 3f) + 90f * Mathf.Sin(TwoPi * 30f * t) * Mathf.Exp(-t * 4f);
            return (Tone(t, f) * 0.6f + Saw(t, f * 0.5f) * 0.2f) * Env(t, 0.003f, 0.75f) * 0.6f;
        }));

        // ---------- шоу-события v6 ----------

        /// Верити отталкивается: пружинистое «бойнь» и взлетающий свисток «вжу-у-уп».
        public static AudioClip VerityJump => Get(ref verityJump, () => Make("verity_jump", 0.75f, t =>
        {
            float boing = Mathf.Sin(TwoPi * (140f + 120f * Mathf.Exp(-t * 9f)) * t) * Mathf.Exp(-t * 7f) * 0.6f;
            float f = 380f + 1300f * Mathf.Pow(Mathf.Clamp01(t / 0.7f), 1.4f) + 25f * Mathf.Sin(TwoPi * 11f * t);
            float whistle = (Tone(t, f) * 0.8f + Tone(t, f * 2f) * 0.12f) * Env(t, 0.04f, 0.75f) * 0.45f;
            return boing + whistle;
        }));

        /// Верити приземляется: глубокое «БУМФ» с резиновым дрожанием и шорохом пыли.
        public static AudioClip VerityLand => Get(ref verityLand, () =>
        {
            float lp = 0f;
            return Make("verity_land", 1.2f, t =>
            {
                lp += (Noise() - lp) * Mathf.Lerp(0.35f, 0.04f, Mathf.Clamp01(t / 0.4f));
                float thump = Mathf.Sin(TwoPi * (85f * t - 22f * t * t)) * Mathf.Exp(-t * 3.2f) * 1.1f;
                float wobble = Mathf.Sin(TwoPi * (120f + 30f * Mathf.Sin(TwoPi * 9f * t)) * t) * Mathf.Exp(-t * 4f) * 0.35f;
                return Mathf.Clamp(thump + wobble + lp * 1.4f * Mathf.Exp(-t * 5f), -1f, 1f) * 0.9f;
            });
        });

        /// Смешок Верити: «хи-хи-хи» (три коротких мультяшных писка).
        public static AudioClip VerityGiggle => Get(ref verityGiggle, () => Make("verity_giggle", 0.6f, t =>
        {
            float sum = 0f;
            for (int k = 0; k < 3; k++)
            {
                float t0 = k * 0.16f;
                float u = t - t0;
                if (u < 0f || u > 0.12f) continue;
                float f = 760f + 140f * k + 220f * Mathf.Sin(Mathf.PI * u / 0.12f);
                float env = Mathf.Sin(Mathf.PI * u / 0.12f);
                sum += (Tone(u, f) * 0.6f + Tone(u, f * 2.02f) * 0.25f + Tone(u, f * 3.1f) * 0.1f) * env;
            }
            return sum * 0.5f;
        }));

        /// Автобус EL DRIFTO: басовитый двухтональный сигнал «БАА-ДААМ».
        public static AudioClip BusHorn => Get(ref busHorn, () => Make("bus_horn", 1.1f, t =>
        {
            float vib = 1f + 0.006f * Mathf.Sin(TwoPi * 5.5f * t);
            float a = Saw(t, 185f * vib) * 0.5f + Saw(t, 233f * vib) * 0.45f + Tone(t, 92f) * 0.25f;
            float env = Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((1.1f - t) / 0.15f) * (t < 0.42f || t > 0.5f ? 1f : 0.35f);
            return Mathf.Clamp(a * env * 0.55f, -1f, 1f);
        }));

        /// Визг шин в дрифте: шумовой свист с «пением» (петля).
        public static AudioClip TireScreech => Get(ref tireScreech, () =>
        {
            float bp = 0f, lp = 0f;
            var clip = Make("tire_screech", 1.6f, t =>
            {
                float n = Noise();
                lp += (n - lp) * 0.45f;
                bp += (lp - bp) * 0.2f;
                float band = (lp - bp) * 1.8f;
                float f = 1050f + 120f * Mathf.Sin(TwoPi * 1.7f * t) + 60f * Mathf.Sin(TwoPi * 6.3f * t);
                return (band * 0.6f + Tone(t, f) * 0.18f + Tone(t, f * 1.5f) * 0.08f) * 0.7f;
            }, loop: true);
            return Loopify(clip, 0.3f);
        });

        /// Автобус врезается: металлический хруст, звон и глухой удар.
        public static AudioClip BusCrash => Get(ref busCrash, () =>
        {
            float lp = 0f;
            return Make("bus_crash", 1.3f, t =>
            {
                lp += (Noise() - lp) * 0.3f;
                float crunch = (Noise() * 0.6f + lp * 0.8f) * Mathf.Exp(-t * 6f);
                float metal = Bell(t, 310f, 5f) * 0.25f + Bell(t - 0.03f, 520f, 6f) * 0.2f + Bell(t - 0.07f, 870f, 8f) * 0.15f;
                float thud = Mathf.Sin(TwoPi * (60f * t - 15f * t * t)) * Mathf.Exp(-t * 5f) * 0.8f;
                return Mathf.Clamp(crunch + metal + thud, -1f, 1f) * 0.85f;
            });
        });

        // ---------- бонусы ----------

        /// Подбор бонуса: весёлое восходящее арпеджио.
        public static AudioClip Pickup => Get(ref pickup, () => Make("pickup", 0.5f, t =>
        {
            float f = t < 0.08f ? 784f : t < 0.16f ? 988f : t < 0.24f ? 1175f : 1568f;
            return (Tone(t, f) * 0.5f + Tone(t, f * 2f) * 0.15f) * Env(t, 0.005f, 0.5f) * 0.45f;
        }));

        /// Выстрел пулемёта: короткий сухой хлопок.
        public static AudioClip GunShot => Get(ref gunShot, () =>
        {
            float lp = 0f;
            return Make("gun_shot", 0.14f, t =>
            {
                lp += (Noise() - lp) * 0.45f;
                return (lp * 1.6f + Mathf.Sin(TwoPi * 140f * t) * 0.5f) * Mathf.Exp(-t * 38f) * 0.8f;
            });
        });

        /// Суперклаксон: двухтональный мультяшный гудок «би-бип».
        public static AudioClip Horn => Get(ref horn, () => Make("horn", 0.9f, t =>
        {
            float f = t < 0.3f ? 392f : 330f;
            float gate = t < 0.27f || t > 0.33f ? 1f : 0f;
            return (Saw(t, f) * 0.35f + Saw(t, f * 1.26f) * 0.3f + Saw(t, f * 1.5f) * 0.2f) * gate * Env(t, 0.01f, 0.9f) * 0.6f;
        }));

        /// Двигатель ракеты: шипение с гулом (по кругу).
        public static AudioClip RocketHiss => Get(ref rocketHiss, () =>
        {
            float lp = 0f;
            return Loopify(Make("rocket_hiss", 1.2f, t =>
            {
                lp += (Noise() - lp) * 0.3f;
                return (lp * 0.9f + Saw(t, 95f) * 0.15f) * 0.5f;
            }), 0.3f);
        });

        /// Захват цели: два быстрых писка.
        public static AudioClip LockOn => Get(ref lockOn, () => Make("lock_on", 0.3f, t =>
            (Tone(t, 1760f) * (t < 0.1f || (t > 0.15f && t < 0.25f) ? 1f : 0f)) * 0.3f));

        /// Клипы живут только в одной Play-сессии. Проверка через оператор Unity: уничтоженный клип считается null
        /// (в проекте Play запускается без перезагрузки домена, статические ссылки переживают сессию).
        static AudioClip Get(ref AudioClip clip, Func<AudioClip> create)
        {
            if (clip == null) clip = create();
            return clip;
        }

        // ---------- синтез ----------

        static readonly System.Random random = new System.Random(5);

        static float Tone(float t, float f) => Mathf.Sin(TwoPi * f * t);
        static float Saw(float t, float f) => 2f * (t * f - Mathf.Floor(t * f + 0.5f));
        static float Noise() => (float)(random.NextDouble() * 2.0 - 1.0);
        static float Step(float t, float at) => t >= at ? 1f : 0f;

        /// Тон с несколькими нечётными гармониками (мягкий «квадрат»).
        static float Harm(float t, float f, int count)
        {
            float s = 0f;
            for (int k = 0; k < count; k++)
            {
                int n = 2 * k + 1;
                s += Mathf.Sin(TwoPi * f * n * t) / n;
            }
            return s;
        }

        /// Колокольчик: основной тон и негармонический обертон с экспоненциальным спадом.
        static float Bell(float t, float f, float decay)
        {
            if (t < 0f) return 0f;
            return (Mathf.Sin(TwoPi * f * t) + 0.35f * Mathf.Sin(TwoPi * f * 2.76f * t) * Mathf.Exp(-t * decay)) * Mathf.Exp(-t * decay * 0.5f) * Mathf.Clamp01(t / 0.003f);
        }

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

        /// Эхо с затуханием: даёт сигналам «воздух» без внешних ревербераторов.
        static AudioClip Echo(AudioClip source, float delay, float feedback)
        {
            var data = new float[source.samples];
            source.GetData(data, 0);
            int d = Mathf.Max(1, Mathf.RoundToInt(delay * Rate));
            for (int i = d; i < data.Length; i++) data[i] = Mathf.Clamp(data[i] + data[i - d] * feedback, -1f, 1f);
            source.SetData(data, 0);
            return source;
        }

        /// Бесшовная петля: конец плавно перетекает в начало.
        static AudioClip Loopify(AudioClip source, float crossfade)
        {
            var data = new float[source.samples];
            source.GetData(data, 0);
            int x = Mathf.Min(data.Length / 3, Mathf.RoundToInt(crossfade * Rate));
            int n = data.Length - x;
            var result = new float[n];
            Array.Copy(data, result, n);
            for (int i = 0; i < x; i++)
            {
                float k = i / (float)x;
                result[i] = data[i] * k + data[n + i] * (1f - k);
            }
            var clip = AudioClip.Create(source.name.Replace("_raw", ""), n, 1, Rate, false);
            clip.SetData(result, 0);
            UnityEngine.Object.Destroy(source);
            return clip;
        }
    }
}
