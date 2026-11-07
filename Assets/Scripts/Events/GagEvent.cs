using System.Collections.Generic;
using RandomCar.Race;
using UnityEngine;

namespace RandomCar.Events
{
    /// Хаос-трюк v5: короткое мгновенное происшествие рядом с машиной (пружина под колёсами, рука с обочины,
    /// наковальня сверху, НЛО над машиной). Директор запускает трюки своим потоком каждые доли секунды, поверх
    /// обычных событий. Трюк ставится относительно машины, ради которой запущен (HazardSpot.FocusSlot), и действует
    /// на всех, кто окажется рядом. ИИ трюки не объезжает: увернуться можно не всегда — так задумано.
    public abstract class GagEvent : RaceEvent
    {
        /// Машина, ради которой трюк (может быть null: финишировала или ушла).
        protected Racer Target => FocusRacer();

        /// Сторона трюка (обочина слева −1 или справа +1): из сида, у всех игроков одинаковая.
        protected int Side { get; private set; }

        /// Точка трюка на трассе: место из директора (прогресс и сдвиг поперёк).
        protected HazardPoint Point { get; private set; }

        protected override void OnTelegraph()
        {
            Side = RandomRange(-1f, 1f) < 0f ? -1 : 1;
            Point = At(Progress, Mathf.Clamp(Placement.Lateral, -RoadHalf + 1f, RoadHalf - 1f));
            Center = Point.Position;
            Radius = 2f;
            OnGagTelegraph();
        }

        protected abstract void OnGagTelegraph();

        /// Большинство трюков действует в OnActiveUpdate; срабатывание без отдельного кадра — по умолчанию пустое.
        protected override void OnActivate() { }

        /// Трюки не занимают участок и не перекрывают полосы — ИИ их не объезжает.
        public override void CollectHazards(List<HazardInfo> list) { }

        public override void CollectBlocks(List<RoadBlock> list) { }

        /// Точка на обочине рядом с местом трюка: side — сторона, extra — дальше от края дороги, м.
        protected Vector3 RoadsideAt(int side, float extra) => Ground(Point.Axis + Point.Right * side * (RoadHalf + extra));

        /// Поворот «лицом к дороге» для предмета на обочине side.
        protected Quaternion FacingRoad(int side) => Quaternion.LookRotation(-Point.Right * side, Vector3.up);

        /// Машины рядом с точкой (по горизонтали), которые считает этот компьютер.
        protected IEnumerable<Racer> SimulatedNear(Vector3 center, float radius)
        {
            foreach (var r in RacersNear(center, radius))
                if (!r.Finished && Simulates(r)) yield return r;
        }

        protected bool AnySimulatedNear(Vector3 center, float radius)
        {
            foreach (var _ in SimulatedNear(center, radius)) return true;
            return false;
        }

        protected static float Flat(Vector3 d)
        {
            d.y = 0f;
            return d.magnitude;
        }

        protected static Vector3 FlatDir(Vector3 d, Vector3 fallback)
        {
            d.y = 0f;
            return d.sqrMagnitude > 0.01f ? d.normalized : fallback;
        }

        /// Плавное «выскакивание» объекта (масштаб с перелётом).
        protected static void PopScale(Transform t, Vector3 full, float k) => t.localScale = full * Mathf.Max(0.001f, EaseOutBack(Mathf.Clamp01(k)));
    }
}
