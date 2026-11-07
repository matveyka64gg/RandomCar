using RandomCar.Audio;
using UnityEngine;

namespace RandomCar.Events
{
    /// Мина-сюрприз: в нескольких метрах перед машиной из асфальта выпрыгивает мина и бешено пищит.
    /// Наехал — взрыв, машину подбрасывает высоко вверх с кувырком; не наехал — мина всё равно взрывается
    /// через пару секунд, и ударная волна достаёт соседей (и поджигает другие мины).
    public sealed class PopMineGag : GagEvent, IChaosReactive
    {
        const int SignalBoom = 1;
        const float TouchRadius = 2.6f;
        const float BlastRadius = 5.4f;

        Transform mine;
        Renderer lamp;
        Vector3 home;
        bool exploded;
        float beepAt;
        MaterialPropertyBlock block;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            block = new MaterialPropertyBlock();
            mine = Child("Mine");
            var l = mine.Find("Lamp");
            lamp = l != null ? l.GetComponent<Renderer>() : null;
            mine.gameObject.SetActive(false);
        }

        protected override void OnGagTelegraph()
        {
            exploded = false;
            fuseAt = float.MaxValue;
            generation = 0;
            home = Ground(Point.Position);
            ChaosFx.Register(this);
            mine.SetPositionAndRotation(home - Vector3.up * 0.8f, Quaternion.Euler(0f, RandomRange(0f, 360f), 0f));
            mine.localScale = Vector3.one * 1.5f;
            mine.gameObject.SetActive(true);
            ChaosEffects?.Blast(home, BlastStyle.ColorPuff, 0.5f, new Color(0.6f, 0.55f, 0.5f, 0.7f));
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            // Выпрыгивает из асфальта с отскоком и вращается.
            float k = Mathf.Clamp01(progress * 1.6f);
            float hop = Mathf.Sin(k * Mathf.PI) * 1.2f;
            mine.position = Vector3.Lerp(home - Vector3.up * 0.8f, home, EaseOutBack(k)) + Vector3.up * hop;
            Blink(time, 10f);
            Beep(0.12f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            if (exploded) return;
            if (Time.time >= fuseAt)
            {
                Explode();
                return;
            }
            mine.position = home;
            Blink(time, 16f);
            Beep(Mathf.Lerp(0.18f, 0.06f, progress));
            if (AnySimulatedNear(home, TouchRadius))
            {
                Signal(SignalBoom, 0, home);
                return;
            }
            // Не наехал никто — к концу мина взрывается сама (у всех в одно время по часам события).
            if (progress >= 0.95f) Explode();
        }

        void Blink(float time, float rate)
        {
            if (lamp == null) return;
            float on = Mathf.Repeat(time * rate, 1f) < 0.5f ? 1f : 0.1f;
            lamp.GetPropertyBlock(block);
            block.SetColor(EmissionColor, new Color(1f, 0.15f, 0.1f) * (on * 8f));
            lamp.SetPropertyBlock(block);
        }

        void Beep(float interval)
        {
            if (Time.time < beepAt) return;
            beepAt = Time.time + interval;
            ChaosEffects?.Sound(SoundBank.MineBeep, home, 0.5f, 1.4f);
        }

        protected override void OnSignal(int code, int index, Vector3 value)
        {
            if (code == SignalBoom) Explode();
        }

        void Explode()
        {
            if (exploded) return;
            exploded = true;
            mine.gameObject.SetActive(false);
            var fx = ChaosEffects;
            fx?.Blast(home, BlastStyle.Big, 1.5f);
            fx?.Flash(home + Vector3.up * 1.5f, new Color(1f, 0.55f, 0.2f), 9f, 0.25f);
            fx?.Sound(SoundBank.BigBoom, home, 1f, Random.Range(1f, 1.2f));
            fx?.Pop(home + Vector3.up * 3f, "СЮРПРИЗ!", new Color(1f, 0.5f, 0.15f), 1.5f);
            ShakeCameraNear(home, 0.6f, 40f);
            foreach (var racer in SimulatedNear(home, BlastRadius))
            {
                Vector3 away = FlatDir(racer.Car.position - home, Point.Right);
                float k = 1f - Flat(racer.Car.position - home) / BlastRadius * 0.5f;
                var car = racer.Car;
                Vector3 spin = (car.forward * Random.Range(-9f, 9f) + car.right * Random.Range(-7f, 7f) + Vector3.up * Random.Range(-3f, 3f)) * k;
                racer.Controller.ApplyLaunch(away * 9f * k + Vector3.up * 26f * k, spin, "мина-сюрприз");
            }
            fx?.Shockwave(home, 9f, 13f, generation + 1);
        }

        float fuseAt = float.MaxValue;
        int generation;

        /// Соседний взрыв поджигает мину (цепная реакция): короткий фитиль. Решает хост — сигнал всем.
        public void OnShock(Vector3 position, float radius, float strength, int chain)
        {
            if (exploded || Stage == EventStage.Idle || Flat(position - home) > radius || Flat(position - home) < 0.1f || fuseAt != float.MaxValue) return;
            fuseAt = Time.time + 0.2f;
            generation = chain;
            ChaosFx.NoteChain(chain);
        }

        protected override void Cleanup()
        {
            ChaosFx.Unregister(this);
            mine.gameObject.SetActive(false);
        }
    }
}
