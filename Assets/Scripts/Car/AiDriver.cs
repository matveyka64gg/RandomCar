using RandomCar.Events;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Car
{
    /// ИИ едет по трассе с собственной боковой линией и долей от максимальной скорости.
    /// На события реагирует по тем же предупреждениям, что видит игрок: объезжает опасность и заезжает в полезную зону.
    /// Реагирует не всегда (как живой водитель, может не успеть).
    [RequireComponent(typeof(CarController))]
    public sealed class AiDriver : MonoBehaviour
    {
        const float ReactChance = 0.7f;
        const float LaneLimit = 4.6f;

        TrackPath track;
        CarController car;
        float lineOffset;
        float skill;
        float lane;
        float laneOverride;
        float overrideUntil;
        System.Random random;

        public void Init(TrackPath path, float lateralOffset, float skillFactor)
        {
            track = path;
            lineOffset = lateralOffset;
            lane = lateralOffset;
            skill = skillFactor;
            car = GetComponent<CarController>();
            random = new System.Random(name.GetHashCode());
        }

        /// Предупреждение о событии: плохое — уйти с его полосы, хорошее — заехать.
        public void ReactToHazard(RaceEvent ev, bool good, float until)
        {
            if (track == null) return;
            if (!ev.IsObstacle && random.NextDouble() > ReactChance) return;
            float p = track.Nearest(ev.Center);
            float hazardLateral = Vector3.Dot(ev.Center - track.PointAt(p), track.SideAt(p));
            float desired;
            if (good)
            {
                desired = hazardLateral;
            }
            else
            {
                float clearance = ev.Radius + 2.2f;
                if (Mathf.Abs(lineOffset - hazardLateral) > clearance) return;
                desired = hazardLateral > 0f ? hazardLateral - clearance : hazardLateral + clearance;
            }
            laneOverride = Mathf.Clamp(desired, -LaneLimit, LaneLimit);
            overrideUntil = until;
        }

        void Update()
        {
            if (track == null) return;

            float wanted = Time.time < overrideUntil ? laneOverride : lineOffset;
            lane = Mathf.MoveTowards(lane, wanted, 3.5f * Time.deltaTime);

            Vector3 position = transform.position;
            float progress = track.Nearest(position);

            // Смотрим вперёд тем дальше, чем быстрее машина.
            float lookAhead = Mathf.Clamp(car.Speed * 0.9f + 6f, 8f, 30f);
            float aheadProgress = progress + lookAhead / track.Length;
            Vector3 aim = track.PointAt(aheadProgress) + track.SideAt(aheadProgress) * lane;

            Vector3 toAim = aim - position;
            toAim.y = 0f;
            float error = Vector3.SignedAngle(transform.forward, toAim, Vector3.up);
            car.Steer = Mathf.Clamp(error / 25f, -1f, 1f);

            // В повороте (большой угол к цели) ИИ сбрасывает скорость.
            float corner = Mathf.Clamp01(Mathf.Abs(error) / 60f);
            float desired = CarController.MaxSpeed * skill * Mathf.Lerp(1f, 0.55f, corner);
            if (car.Speed > desired + 8f) car.Throttle = -1f;
            else car.Throttle = car.Speed < desired ? 1f : 0f;
        }
    }
}
