using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using RandomCar.Race;
using RandomCar.UI;
using UnityEngine;

namespace RandomCar.Events
{
    /// ГИПЕРГАМИЯ (шоу-событие v7): прямо на дороге с «вжух» вырастает огромная карточка в золотой раме с картинкой
    /// пользователя (Assets/Art/Textures/Events/hypergamy.jpg) и парит над асфальтом, покачиваясь и поворачиваясь
    /// к камере. Коснулся её своей машиной — картинка мгновенно на весь экран ровно на 10 с под музыку пользователя
    /// (HypergamyScreen), карточка исчезает в конфетти. Можно объехать — а можно и нарочно.
    /// Место (прогресс и смещение поперёк) приходит от хоста: в половине случаев — на полосе участника, ради которого
    /// карточка появилась. Касание решает компьютер, который считает машину: свою машину игрока — он сам (ИИ и чужие
    /// машины картинку не вызывают). Сигнал «попался» уходит через хоста всем: карточка исчезает у всех, остальные видят
    /// надпись, кто попался. Повторно не срабатывает: карточка одноразовая, а экран не перезапускается, пока идёт.
    public sealed class HypergamyEvent : RaceEvent
    {
        const int SignalCaught = 1;
        /// Высота парения низа карточки над дорогой, м: машина проезжает сквозь нижнюю часть картинки.
        public const float Hover = 0.45f;
        /// Время «вырастания» из дороги, с.
        const float GrowSeconds = 0.9f;
        static readonly Color Pink = new Color(1f, 0.32f, 0.62f);

        Transform card, pivot;
        Collider trigger;
        ParticleSystem sparkle;
        TelegraphMarker marker;
        float appearAt, caughtAt;
        bool caught;
        string caughtBy;
        Quaternion facing = Quaternion.identity;

        /// Карточку уже задели (у всех — по сигналу), кто и сколько раз её касалась своя машина на этом компьютере.
        public bool Caught => caught;
        public string CaughtBy => caughtBy;
        public int LocalTouches { get; private set; }
        public Transform Card => card;
        public Collider Trigger => trigger;

        void Awake()
        {
            card = Child("Card");
            pivot = card.Find("Pivot");
            trigger = card.Find("Trigger").GetComponent<Collider>();
            var fx = pivot.Find("FxSparkle");
            if (fx != null) sparkle = fx.GetComponent<ParticleSystem>();
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            card.gameObject.SetActive(false);
        }

        protected override void OnTelegraph()
        {
            caught = false;
            caughtBy = null;
            LocalTouches = 0;
            float limit = Mathf.Max(0f, RoadHalf - 1.6f);
            var h = At(Progress, Mathf.Clamp(Placement.Lateral, -limit, limit));
            Vector3 ground = Ground(h.Position);
            card.SetPositionAndRotation(ground, Quaternion.LookRotation(h.Forward, Vector3.up));
            // Лицом к тем, кто едет навстречу, пока камера не повернёт её к себе.
            facing = Quaternion.LookRotation(-h.Forward, Vector3.up);
            pivot.rotation = facing;
            pivot.localPosition = Vector3.up * Hover;
            pivot.localScale = Vector3.zero;
            trigger.enabled = false;
            card.gameObject.SetActive(true);
            appearAt = Time.time;
            Center = ground;
            Radius = 2.4f;
            marker.Hide();
            var fx = ChaosEffects;
            // Небольшое прозрачное облачко у самой дороги: большое и плотное закрывало картинку первые секунды.
            fx?.Blast(ground, BlastStyle.ColorPuff, 0.6f, new Color(1f, 0.6f, 0.82f, 0.45f));
            fx?.Sound(SoundBank.Warp, ground, 1f, 1.15f);
            fx?.Pop(ground + Vector3.up * 6f, "ГИПЕРГАМИЯ?!", Pink, 1.8f);
            if (sparkle != null) sparkle.Play();
        }

        /// Касание засчитывается, как только карточка выросла (ещё в предупреждении): игрок, который успел доехать
        /// раньше срока, не проскакивает её насквозь без эффекта.
        bool Touchable => !caught && (Stage == EventStage.Telegraph || Stage == EventStage.Active) && Time.time - appearAt >= GrowSeconds * 0.8f;

        protected override void OnActivate()
        {
            trigger.enabled = Touchable;
        }

        // ---------- касание ----------

        /// Машина въехала в карточку (HypergamyContact). Картинку на весь экран получает только игрок этого компьютера.
        public void OnTouch(Collider other)
        {
            if (!Touchable || !trigger.enabled) return;
            var body = other.attachedRigidbody;
            var car = body != null ? body.GetComponent<CarController>() : null;
            if (car == null) return;
            var me = Context != null && Context.Race != null ? Context.Race.Player : null;
            if (me == null || me.Controller != car || !car.Physics.Simulated || me.Finished) return;
            Catch(me);
        }

        /// Полигон испытаний: коснуться карточки, не подъезжая (то же, что касание своей машиной). false — нечего касаться.
        public bool TouchNow()
        {
            var me = Context != null && Context.Race != null ? Context.Race.Player : null;
            if (me == null || !Touchable) return false;
            return Catch(me);
        }

        bool Catch(Racer me)
        {
            LocalTouches++;
            var screen = HypergamyScreen.Instance;
            if (screen == null)
            {
                Debug.LogError("[RandomCar] ГИПЕРГАМИЯ: нет HypergamyScreen в сцене Game (пересобрать сцену)");
                return false;
            }
            // Картинка уже на экране (другая карточка) — таймер не перезапускается, эта карточка остаётся на дороге.
            if (!screen.Show()) return false;
            Signal(SignalCaught, me.Slot, card.position);
            return true;
        }

        protected override void OnSignal(int code, int index, Vector3 value)
        {
            if (code != SignalCaught || caught) return;
            caught = true;
            caughtAt = Time.time;
            trigger.enabled = false;
            var racer = Context != null && Context.Race != null ? Context.Race.BySlot(index) : null;
            caughtBy = racer != null ? racer.Name : "?";
            bool mine = Context != null && Context.Race != null && Context.Race.Player != null && Context.Race.Player.Slot == index;
            Vector3 at = card.gameObject.activeSelf ? card.position + Vector3.up * (Hover + 2.2f) : value;
            var fx = ChaosEffects;
            fx?.Blast(at, BlastStyle.Fake, 1.6f);
            fx?.Blast(at, BlastStyle.ColorPuff, 1.8f, new Color(1f, 0.45f, 0.75f, 0.9f));
            fx?.Sound(SoundBank.Ding, at, 1f, 1.1f);
            fx?.Pop(at + Vector3.up * 2.5f, mine ? "ПОПАЛСЯ!" : $"{caughtBy}: ГИПЕРГАМИЯ!", Pink, 1.7f);
            card.gameObject.SetActive(false);
            Debug.Log($"[RandomCar] ГИПЕРГАМИЯ: розыгрыш {Serial} — картинку задел «{caughtBy}»" + (mine ? " (этот компьютер: картинка на весь экран)" : ""));
        }

        // ---------- вид: вырастает, парит, покачивается, смотрит на камеру ----------

        void LateUpdate()
        {
            if (!card.gameObject.activeSelf) return;
            if (!trigger.enabled && Touchable) trigger.enabled = true;
            float t = Time.time - appearAt;
            float grow = Stage == EventStage.Ending ? 1f - Mathf.Clamp01(StageProgress * 1.4f) : EaseOutBack(Mathf.Clamp01(t / GrowSeconds));
            pivot.localScale = new Vector3(Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(grow)) * Mathf.Max(grow, 0f), Mathf.Max(grow, 0f), Mathf.Max(grow, 0f));
            pivot.localPosition = Vector3.up * (Hover + Mathf.Sin(t * 2.1f) * 0.16f * Mathf.Clamp01(t / GrowSeconds));
            var cam = Context != null && Context.Camera != null ? Context.Camera.transform : Camera.main != null ? Camera.main.transform : null;
            if (cam != null)
            {
                Vector3 toCam = cam.position - card.position;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 1f)
                    facing = Quaternion.Slerp(facing, Quaternion.LookRotation(toCam.normalized, Vector3.up), 1f - Mathf.Exp(-4f * Time.deltaTime));
            }
            // Кокетливо покачивается: рыскание ±9°, крен ±3°.
            pivot.rotation = facing * Quaternion.Euler(0f, Mathf.Sin(t * 1.6f) * 9f, Mathf.Sin(t * 2.3f + 1f) * 3f);
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.9f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            if (card.gameObject.activeSelf) Center = card.position;
            // Задели — через секунду (конфетти отлетели) шоу кончается и освобождает место в потоке шоу.
            if (caught && Time.time - caughtAt > 1f) FinishActive();
        }

        protected override void OnEnd()
        {
            trigger.enabled = false;
            if (!caught && card.gameObject.activeSelf)
            {
                var at = card.position + Vector3.up * (Hover + 2f);
                ChaosEffects?.Blast(at, BlastStyle.ColorPuff, 1.2f, new Color(1f, 0.6f, 0.85f, 0.7f));
                ChaosEffects?.Sound(SoundBank.Pop, at, 0.8f, 1.2f);
            }
        }

        protected override void OnEndUpdate(float progress) { }

        protected override void Cleanup()
        {
            if (appearAt > 0f)
                Debug.Log($"[RandomCar] ГИПЕРГАМИЯ: розыгрыш {Serial} окончен — " + (caught ? $"задел «{caughtBy}»" : "никто не задел")
                          + $", касаний своей машиной {LocalTouches}");
            appearAt = 0f;
            trigger.enabled = false;
            if (sparkle != null) sparkle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            card.gameObject.SetActive(false);
            marker.Hide();
        }

        /// ИИ карточку не объезжает и не вызывает: она для живых игроков.
        public override void CollectHazards(List<HazardInfo> list) { }

        public override void CollectBlocks(List<RoadBlock> list) { }
    }
}
