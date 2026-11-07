using System.Collections;
using System.Collections.Generic;
using RandomCar.Car;
using RandomCar.Core;
using RandomCar.Race;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Events
{
    public enum EventStage { Idle, Telegraph, Active, Ending }

    /// Точка события на дороге: центр, направление трассы и сдвиг поперёк.
    public struct HazardPoint
    {
        public Vector3 Position;
        public Vector3 Forward;
        public Vector3 Right;
        public float Lateral;
        public float Progress;
        public Vector3 Axis;
    }

    /// Общие данные для событий: гонка, трасса, камера, случайность директора.
    public sealed class EventContext
    {
        public RaceManager Race;
        public TrackPath Track;
        public FollowCamera Camera;
        public EventScheduler Random;
        public EventDirector Director;
    }

    /// Базовое событие: предупреждение → активная фаза → завершение. Объект события заранее лежит в сцене
    /// выключенным и только включается на время розыгрыша; временные объекты не создаются.
    public abstract class RaceEvent : MonoBehaviour
    {
        [SerializeField] string id;

        readonly HashSet<CarController> hitOnce = new HashSet<CarController>();
        System.Random random = new System.Random(0);

        public string Id => id;
        public EventDefinition Definition { get; private set; }

        /// Место события на трассе. Цели-машины нет: в зоне воздействие получает любой.
        public HazardSpot Placement { get; private set; }

        /// Прогресс центра зоны и её длина вдоль трассы, м.
        public float Progress => Placement.Progress;
        public float ZoneLength => Definition != null ? Definition.ZoneLength : 0f;
        public EventStage Stage { get; private set; } = EventStage.Idle;
        public bool IsRunning => Stage != EventStage.Idle;

        /// Прогресс текущей стадии 0..1.
        public float StageProgress { get; private set; }

        /// Центр события в мире: для подсказок ИИ и интерфейса.
        public Vector3 Center { get; protected set; }

        /// Радиус опасной (или полезной) зоны, не больше FairnessLimits.MaxHazardRadius.
        public float Radius { get; protected set; } = 2f;

        /// Физическое препятствие (стена): ИИ объезжает его всегда, а не с вероятностью.
        public virtual bool IsObstacle => false;

        /// Сколько длится предупреждение у этого розыгрыша, с: у «ударов» — пока не подъедет игрок (не меньше минимума).
        public float WarningDuration { get; private set; }

        /// Сколько уже длится (или продлится) действие, с: у «зон» — пока игрок не проедет участок.
        public float ActiveElapsed { get; private set; }

        /// Time.time появления предупреждения.
        public float StartedAt { get; private set; }

        /// Сетевой номер розыгрыша (одинаковый у хоста и клиентов): сигналы и физические объекты события.
        public int Serial { get; private set; }

        /// Поправка часов события у клиента, с: хост запустил его раньше на задержку сети (движущиеся конструкции
        /// у всех в одной фазе).
        public float ClockOffset { get; private set; }

        /// Время с появления события по часам хоста, с.
        protected float EventTime => Time.time - StartedAt + ClockOffset;

        /// Сцена-комбинация, в которую входит розыгрыш (для интерфейса), иначе null.
        public string ComboTitle { get; set; }

        /// Полуширина дороги этой трассы, м.
        protected float RoadHalf => Context != null && Context.Track != null ? Context.Track.HalfWidth : FairnessLimits.RoadHalfWidth;

        /// Самая большая честная зона на этой дороге (не шире половины дороги).
        protected float MaxRadius => FairnessLimits.HazardRadiusFor(RoadHalf);

        protected EventContext Context { get; private set; }
        protected IReadOnlyList<Racer> Racers => Context.Race.Racers;

        public void Configure(string eventId) => id = eventId;

        public void Begin(EventDefinition definition, HazardSpot spot, EventContext context, int serial = 0, float clockOffset = 0f)
        {
            Definition = definition;
            Placement = spot;
            Context = context;
            Serial = serial;
            ClockOffset = Mathf.Clamp(clockOffset, 0f, 0.5f);
            ComboTitle = null;
            random = new System.Random(spot.Seed);
            StartedAt = Time.time;
            hitOnce.Clear();
            gameObject.SetActive(true);
            StartCoroutine(Run());
        }

        public void Cancel()
        {
            StopAllCoroutines();
            if (Stage != EventStage.Idle) Cleanup();
            Stage = EventStage.Idle;
            gameObject.SetActive(false);
        }

        /// Полигон испытаний: «удар», который ждёт подъезда игрока, срабатывает сейчас (после короткого обязательного
        /// предупреждения CommitLead — падающая ракета и бегущее стадо успевают начать анимацию).
        public void CommitNow() => commitRequested = true;

        /// «Удар» ещё ждёт подъезда игрока (предупреждение продлевается).
        public bool WaitingForApproach { get; private set; }

        bool commitRequested;

        /// Событие само решило закончить действие раньше срока (ГИПЕРГАМИЮ задели — место в потоке шоу свободно).
        bool finishActive;

        /// Закончить стадию действия сейчас: дальше обычное завершение (OnEnd, Cleanup). Все компьютеры вызывают
        /// это по одному и тому же сигналу — у всех событие кончается одинаково.
        protected void FinishActive() => finishActive = true;

        IEnumerator Run()
        {
            finishActive = false;
            commitRequested = false;
            Stage = EventStage.Telegraph;
            // Хаос v5: в «безумии» предупреждения вдвое короче, у трюков — доли секунды.
            var intensity = Context.Director != null ? Context.Director.Intensity : default;
            // Шоу-события (v6) — полное предупреждение: крик «EL DRIFTO!» звучит до выезда автобуса, директор ставит их по нему.
            float minWarn = Definition.Gag
                ? Mathf.Max(0.2f, Definition.WarningSeconds)
                : Definition.Spectacle
                    ? Definition.WarningSeconds
                    : Mathf.Max(Definition.WarningSeconds * intensity.WarningScale, intensity.MinWarning);
            bool wild = intensity.Wild;
            WarningDuration = minWarn;
            // «Удар» ждёт игрока: срок предупреждения пересчитывается по его подъезду и фиксируется за CommitLead
            // до срабатывания (падающая ракета и бегущее стадо уже начали анимацию).
            bool committed = !Definition.Trigger || FocusRacer() == null;
            if (!committed) WarningDuration = Mathf.Clamp(Mathf.Max(CommitLead, FocusEta() - Definition.TriggerLead), minWarn, FairnessLimits.MaxHoldSeconds);
            OnTelegraph();
            for (float t = 0f; t < WarningDuration; t += Time.deltaTime)
            {
                if (!committed && commitRequested)
                {
                    committed = true;
                    WarningDuration = Mathf.Min(WarningDuration, t + CommitLead);
                }
                WaitingForApproach = !committed;
                if (!committed)
                {
                    float want = t + Mathf.Max(CommitLead, FocusEta() - Definition.TriggerLead);
                    WarningDuration = Mathf.Clamp(want, minWarn, FairnessLimits.MaxHoldSeconds);
                    if (WarningDuration - t <= CommitLead) committed = true;
                }
                StageProgress = Mathf.Clamp01(t / WarningDuration);
                OnTelegraphUpdate(StageProgress, t);
                yield return null;
            }

            WaitingForApproach = false;
            Stage = EventStage.Active;
            StageProgress = 0f;
            OnActivate();
            // «Зона» держится, пока игрок, ради которого она поставлена, не проедет её (но не дольше предела).
            // В «безумии» зона держится и пока в ней кто-то ещё возится (кувыркается, застрял, летит над ней).
            float maxSustain = wild ? MaxWildSustain : FairnessLimits.MaxSustainSeconds;
            for (float t = 0f; !finishActive && (t < Definition.ActiveSeconds || (Definition.Sustain && t < maxSustain && (!FocusPassed() || wild && AnyoneInside()))); t += Time.deltaTime)
            {
                ActiveElapsed = t;
                StageProgress = Mathf.Clamp01(t / Definition.ActiveSeconds);
                OnActiveUpdate(StageProgress, t);
                yield return null;
            }

            Stage = EventStage.Ending;
            StageProgress = 0f;
            const float endTime = 0.6f;
            OnEnd();
            for (float t = 0f; t < endTime; t += Time.deltaTime)
            {
                StageProgress = t / endTime;
                OnEndUpdate(StageProgress);
                yield return null;
            }

            Cleanup();
            Stage = EventStage.Idle;
            gameObject.SetActive(false);
        }

        protected abstract void OnTelegraph();
        protected virtual void OnTelegraphUpdate(float progress, float time) { }
        protected abstract void OnActivate();
        protected virtual void OnActiveUpdate(float progress, float time) { }
        protected virtual void OnEnd() { }
        protected virtual void OnEndUpdate(float progress) { }

        /// Возвращает объекты в исходное состояние перед возвратом в пул.
        protected abstract void Cleanup();

        // ---------- подъезд игрока ----------

        /// За сколько секунд до срабатывания «удара» его срок больше не меняется, с.
        const float CommitLead = 1.2f;

        /// Участник, ради которого событие поставлено (null — без привязки, ушёл, финишировал).
        protected Racer FocusRacer()
        {
            if (Context == null || Context.Race == null || Placement.FocusSlot < 0) return null;
            var r = Context.Race.BySlot(Placement.FocusSlot);
            return r == null || r.Retired || r.Finished ? null : r;
        }

        /// Расстояние по трассе от участника до центра зоны, м (минус — уже позади).
        protected float SignedAhead(Racer racer)
        {
            float length = Context.Track.Length;
            float d = TrackPath.Wrap(Progress - racer.Progress) * length;
            return d > length * 0.5f ? d - length : d;
        }

        /// Через сколько секунд участник въедет в зону (0 — уже в ней или привязки нет).
        float FocusEta()
        {
            var r = FocusRacer();
            if (r == null) return 0f;
            float d = SignedAhead(r) - ZoneLength * 0.5f;
            return d <= 0f ? 0f : d / Mathf.Max(6f, r.Controller.Speed);
        }

        /// Дольше этого зона «безумия» не держится, с.
        const float MaxWildSustain = 20f;

        /// Кто-то из участников внутри зоны (с запасом по краям).
        bool AnyoneInside()
        {
            if (Context == null || Context.Race == null) return false;
            float half = ZoneLength * 0.5f + 3f;
            foreach (var r in Context.Race.Racers)
            {
                if (r.Finished || r.Retired) continue;
                if (Mathf.Abs(SignedAhead(r)) < half) return true;
            }
            return false;
        }

        /// Участник проехал зону (или привязки нет).
        bool FocusPassed()
        {
            var r = FocusRacer();
            return r == null || SignedAhead(r) < -(ZoneLength * 0.5f + 4f);
        }

        /// Опасности события для ИИ: по умолчанию одна зона вокруг Center. Составные события (обстрел,
        /// перекрытие полос) добавляют каждую свою точку.
        public virtual void CollectHazards(List<HazardInfo> list)
        {
            if (Stage != EventStage.Telegraph && Stage != EventStage.Active) return;
            list.Add(new HazardInfo
            {
                Progress = Progress,
                Center = Center,
                Radius = Radius,
                HalfLength = ZoneLength * 0.5f,
                Good = Definition.IsGood,
                Obstacle = IsObstacle,
                Since = StartedAt,
            });
        }

        /// Перекрытые участки дороги (для проверки, что проезд остаётся): по умолчанию препятствие вокруг Center.
        public virtual void CollectBlocks(List<RoadBlock> list)
        {
            if (!IsObstacle || Stage != EventStage.Active) return;
            float lat = Lateral(Center, Progress);
            list.Add(new RoadBlock { Progress = Progress, HalfLength = Radius, LateralMin = lat - Radius, LateralMax = lat + Radius });
        }

        // ---------- сеть: сигналы события ----------

        /// Сигнал события (мина сработала от машины, которую считает этот компьютер): применяется здесь и
        /// пересылается остальным, чтобы у всех случилось одно и то же. Повторный сигнал должен быть безвреден.
        protected void Signal(int code, int index, Vector3 value)
        {
            OnSignal(code, index, value);
            Context?.Director?.RaiseSignal(this, code, index, value);
        }

        /// Сигнал пришёл по сети.
        public void ReceiveSignal(int code, int index, Vector3 value)
        {
            if (IsRunning) OnSignal(code, index, value);
        }

        protected virtual void OnSignal(int code, int index, Vector3 value) { }

        /// Этот компьютер считает физику машины (своя машина, ИИ хоста): только он решает, что она задела.
        protected static bool Simulates(Racer racer) => racer.Controller.Physics.Simulated;

        /// Хост или одиночная игра: физику объектов события считает он.
        protected bool Authority => Context == null || Context.Director == null || Context.Director.Authoritative;

        protected static ChaosFx ChaosEffects => ChaosFx.Instance;

        /// Номер физического объекта события для сети.
        protected int BodyId(int index) => Serial * 32 + index;

        // ---------- помощники ----------

        /// Случайное число события из сида места: в сетевой игре у всех выходит одно и то же.
        protected float RandomRange(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

        /// Точка события: выбранное место на трассе, зона радиусом radius целиком на дороге.
        protected HazardPoint Spot(float radius)
        {
            float limit = Mathf.Max(0f, RoadHalf - radius);
            return At(Placement.Progress, Mathf.Clamp(Placement.Lateral, -limit, limit));
        }

        protected HazardPoint At(float progress, float lateral)
        {
            var track = Context.Track;
            Vector3 axis = track.PointAt(progress);
            Vector3 right = track.SideAt(progress);
            return new HazardPoint
            {
                Position = axis + right * lateral,
                Forward = track.DirectionAt(progress),
                Right = right,
                Lateral = lateral,
                Progress = progress,
                Axis = axis,
            };
        }

        protected float Lateral(Vector3 position, float progress)
        {
            var track = Context.Track;
            return Vector3.Dot(position - track.PointAt(progress), track.SideAt(progress));
        }

        /// Место у обочины для объекта события: не дальше свободной полосы карты, на высоте земли.
        protected Vector3 Roadside(HazardPoint h, float side, float preferred)
        {
            var map = Context.Race.Map;
            float distance = map != null ? Mathf.Min(preferred, map.Roadside) : preferred;
            return Ground(h.Axis + h.Right * side * distance);
        }

        /// Точка на земле под позицией (луч вниз мимо машин); если земли нет — сама позиция.
        protected static Vector3 Ground(Vector3 position)
        {
            var hits = Physics.RaycastAll(position + Vector3.up * 30f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 result = position;
            foreach (var hit in hits)
            {
                if (hit.collider.attachedRigidbody != null || hit.collider.GetComponentInParent<RaceEvent>() != null) continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    result = hit.point;
                }
            }
            return result;
        }

        /// Где машина относительно центра события: вдоль трассы (плюс — проехала центр), поперёк дороги, м.
        protected void Locate(Racer racer, out float along, out float lateral)
        {
            along = -SignedAhead(racer);
            lateral = Lateral(racer.Car.position, racer.Progress);
        }

        /// Машина на участке [−halfLength, +halfLength] вдоль и [latMin, latMax] поперёк.
        protected bool InZone(Racer racer, float halfLength, float latMin, float latMax)
        {
            Locate(racer, out float along, out float lateral);
            return Mathf.Abs(along) <= halfLength && lateral >= latMin && lateral <= latMax;
        }

        /// Машины в радиусе (по горизонтали).
        protected IEnumerable<Racer> RacersNear(Vector3 center, float radius)
        {
            foreach (var r in Racers)
            {
                Vector3 d = r.Car.position - center;
                d.y = 0f;
                if (d.sqrMagnitude <= radius * radius) yield return r;
            }
        }

        /// Срабатывает один раз на машину за событие.
        protected bool FirstHit(CarController car) => hitOnce.Add(car);

        /// Тряска камеры игрока у точки (для помощников событий вроде MineField).
        public void ShakeAt(Vector3 position, float amplitude, float range) => ShakeCameraNear(position, amplitude, range);

        protected void ShakeCameraNear(Vector3 position, float amplitude, float range = 35f)
        {
            if (Context.Camera == null || Context.Race.Player == null) return;
            float d = Vector3.Distance(Context.Race.Player.Car.position, position);
            if (d < range) Context.Camera.Shake(amplitude * (1f - d / range), 0.45f);
        }

        protected static void Play(ParticleSystem fx, Vector3 position)
        {
            if (fx == null) return;
            fx.transform.position = position;
            fx.Clear(true);
            fx.Play(true);
        }

        protected static void Stop(ParticleSystem fx)
        {
            if (fx != null) fx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        protected Transform Child(string name)
        {
            var t = transform.Find(name);
            if (t == null) Debug.LogError($"[RandomCar] {id}: нет дочернего объекта {name}");
            return t;
        }

        protected ParticleSystem Fx(string name) => Child(name)?.GetComponent<ParticleSystem>();

        protected static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }
}
