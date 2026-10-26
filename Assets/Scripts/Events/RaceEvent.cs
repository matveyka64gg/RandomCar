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

        public string Id => id;
        public EventDefinition Definition { get; private set; }
        public Racer Target { get; private set; }
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

        protected EventContext Context { get; private set; }
        protected IReadOnlyList<Racer> Racers => Context.Race.Racers;

        public void Configure(string eventId) => id = eventId;

        public void Begin(EventDefinition definition, Racer target, EventContext context)
        {
            Definition = definition;
            Target = target;
            Context = context;
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

        IEnumerator Run()
        {
            Stage = EventStage.Telegraph;
            OnTelegraph();
            float warn = Mathf.Max(Definition.WarningSeconds, FairnessLimits.MinWarningSeconds);
            for (float t = 0f; t < warn; t += Time.deltaTime)
            {
                StageProgress = t / warn;
                OnTelegraphUpdate(StageProgress, t);
                yield return null;
            }

            Stage = EventStage.Active;
            StageProgress = 0f;
            OnActivate();
            for (float t = 0f; t < Definition.ActiveSeconds; t += Time.deltaTime)
            {
                StageProgress = t / Definition.ActiveSeconds;
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

        // ---------- помощники ----------

        /// Где окажется цель к концу предупреждения: на её текущей полосе со сдвигом до ±1 м, зона целиком на дороге.
        protected HazardPoint Predict(Racer target, float lead, float radius, float minSpeed = 12f)
        {
            var track = Context.Track;
            float speed = Mathf.Max(target.Controller.Speed, minSpeed);
            float progress = TrackPath.Wrap(target.Progress + speed * lead / track.Length);
            float current = Lateral(target.Car.position, target.Progress);
            float limit = FairnessLimits.RoadHalfWidth - radius;
            float lateral = Mathf.Clamp(current + Context.Random.LateralJitter(), -limit, limit);
            return At(progress, lateral);
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
