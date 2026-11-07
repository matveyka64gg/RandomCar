using System.Collections.Generic;
using RandomCar.Events;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Car
{
    /// Уровень водителя ИИ. Отличия — только в решениях: какую долю сцепления использовать, насколько рано
    /// тормозить, как точно держать линию, как быстро реагировать и насколько охотно обгонять.
    /// Физических бонусов нет: машина ИИ — та же модель, что у игрока.
    public readonly struct AiSkill
    {
        public readonly float GripUse;
        public readonly float BrakeUse;
        public readonly float LineNoise;
        public readonly float Reaction;
        public readonly float Aggression;
        public readonly float Pace;

        public AiSkill(float gripUse, float brakeUse, float lineNoise, float reaction, float aggression, float pace)
        {
            GripUse = gripUse;
            BrakeUse = brakeUse;
            LineNoise = lineNoise;
            Reaction = reaction;
            Aggression = aggression;
            Pace = pace;
        }

        /// 0 — лёгкий, 1 — средний, 2 — сложный. variation 0..1 — небольшой разброс характера между машинами.
        public static AiSkill ForDifficulty(int difficulty, float variation)
        {
            float v = Mathf.Clamp01(variation);
            switch (difficulty)
            {
                case 0: return new AiSkill(0.80f + 0.04f * v, 0.70f, 0.9f, 0.6f, 0.25f, 0.93f + 0.03f * v);
                case 2: return new AiSkill(0.97f + 0.02f * v, 0.92f, 0.15f, 0.18f, 0.9f, 0.995f + 0.005f * v);
                default: return new AiSkill(0.90f + 0.04f * v, 0.82f, 0.45f, 0.35f, 0.6f, 0.97f + 0.02f * v);
            }
        }
    }

    /// ИИ-водитель. Управляет машиной через тот же CarInput, что и игрок: руль — преследование точки на
    /// своей линии, газ и тормоз — по профилю скорости (RacingLine) с поправкой на занос и соседей.
    /// Видит соперников: обходит медленных, не въезжает в зад, прикрывает внутреннюю траекторию.
    /// На предупреждения событий реагирует с задержкой реакции своего уровня.
    [RequireComponent(typeof(CarController))]
    public sealed class AiDriver : MonoBehaviour
    {
        const float LaneRate = 2.6f;

        /// Насколько далеко от оси ИИ уходит на своей полосе: полуширина дороги без запаса на корпус.
        float laneLimit = 4.6f;

        TrackPath track;
        RacingLine line;
        CarController car;
        IReadOnlyList<CarController> rivals;
        AiSkill skill;
        float laneBias;
        float noiseSeed;
        float lane;
        float passOffset;
        float passUntil;
        bool avoiding;
        float stuckFor;
        float reverseUntil;
        float reverseSteer;
        System.Random random;

        public AiSkill Skill => skill;
        public RacingLine Line => line;

        /// Целевая скорость на текущем шаге (для отладки и тестов).
        public float TargetSpeed { get; private set; }

        public void Init(TrackPath path, IReadOnlyList<CarController> allCars, AiSkill aiSkill, int seed)
        {
            track = path;
            car = GetComponent<CarController>();
            rivals = allCars;
            skill = aiSkill;
            random = new System.Random(seed);
            laneBias = ((float)random.NextDouble() * 2f - 1f) * 0.6f;
            noiseSeed = (float)random.NextDouble() * 100f;
            var spec = car.Spec;
            // Запас: нагруженная внешняя шина в повороте держит хуже номинала.
            float grip = Mathf.Min(spec.Front.LateralGrip, spec.Rear.LateralGrip) * 0.92f * skill.GripUse;
            float brake = spec.BrakeDecel * 9.81f * skill.BrakeUse * 0.82f;
            laneLimit = Mathf.Max(3f, track.HalfWidth - 1.4f);
            line = new RacingLine(track, grip, brake, 30f, spec.TopSpeed * skill.Pace, laneLimit - 1.1f, SurfaceGrip(track, spec));
            lane = Lateral(transform.position, track.Nearest(transform.position));
            passUntil = 0f;
            stuckFor = 0f;
            reverseUntil = 0f;
        }

        /// Перекрытая опасностями полоса поперёк дороги: от и до, препятствие ли (стена), где начинается, м.
        readonly List<(float lo, float hi, bool obstacle, float near, bool soft)> blocked = new List<(float, float, bool, float, bool)>();
        readonly List<float> candidates = new List<float>();

        /// Опасности и бонусы впереди (EventDirector.Hazards). Плохие ближайшего участка (комбинации: мины и шары,
        /// лёд и молоты, разделитель развилки) вместе перекрывают полосы — ИИ ищет ближайший к своей линии
        /// свободный проезд между ними; если его нет, выбирает проезд без стен и сбрасывает скорость.
        /// Движущиеся опасности — там, где они будут к подъезду. Нет плохих рядом — заезжает на ближайшую хорошую.
        /// Видит их с задержкой реакции своего уровня, как игрок.
        bool PickHazard(float progress, float speed, out float desired, out bool bad, out float speedCap)
        {
            desired = 0f;
            bad = false;
            speedCap = float.MaxValue;
            var director = Events.EventDirector.Current;
            if (director == null) return false;
            var list = director.Hazards;
            float range = Mathf.Max(70f, speed * 3.5f);
            float bestGood = float.MaxValue, goodLane = 0f, nearestBad = float.MaxValue;
            blocked.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var h = list[i];
                if (Time.time - h.Since < skill.Reaction) continue;
                float ahead = (Mathf.Repeat(h.Progress - progress + 0.5f, 1f) - 0.5f) * track.Length;
                if (ahead < -h.HalfLength - 3f || ahead > range) continue;
                float hazardProgress = h.Progress;
                Vector3 hazardCenter = h.Center;
                // Движущаяся опасность (шар, бочка, стена): объезжаем место, где она окажется к нашему подъезду.
                if (h.Velocity.sqrMagnitude > 0.25f)
                {
                    float eta = Mathf.Clamp(ahead / Mathf.Max(speed, 6f), 0f, 2.5f);
                    hazardCenter += h.Velocity * eta;
                    float along = Vector3.Dot(h.Velocity, track.DirectionAt(h.Progress)) * eta;
                    hazardProgress = Mathf.Repeat(h.Progress + along / track.Length, 1f);
                    ahead += along;
                    if (ahead < -h.HalfLength - 3f || ahead > range) continue;
                }
                float lat = Lateral(hazardCenter, hazardProgress);
                if (h.Good)
                {
                    if (ahead < bestGood && Mathf.Abs(lat) <= laneLimit)
                    {
                        bestGood = ahead;
                        goodLane = lat;
                    }
                    continue;
                }
                // Стену объезжаем с бо́льшим запасом, чем лужу: задеть блок — потерять всё.
                float clearance = h.Radius + (h.Obstacle ? 2.6f : h.Soft ? 1.2f : 2.0f);
                float near = Mathf.Max(0f, ahead - h.HalfLength);
                blocked.Add((lat - clearance, lat + clearance, h.Obstacle, near, h.Soft));
                if (!h.Soft) nearestBad = Mathf.Min(nearestBad, near);
            }

            if (blocked.Count > 0)
            {
                // Вместе учитываем только то, что стоит в одном сечении с первой опасностью (мины и шар рядом,
                // лёд под молотом): шикану и ряды мин ИИ проходит по очереди, слаломом.
                if (nearestBad == float.MaxValue) foreach (var b in blocked) nearestBad = Mathf.Min(nearestBad, b.near);
                float window = nearestBad + Mathf.Max(12f, speed * 0.45f);
                if (Blocked(lane, window, false) || Blocked(lane, window, true) || BlockedSoft(lane, window))
                {
                    candidates.Clear();
                    candidates.Add(lane);
                    foreach (var b in blocked)
                    {
                        if (b.near > window) continue;
                        candidates.Add(b.lo - 0.05f);
                        candidates.Add(b.hi + 0.05f);
                    }
                    float best = float.NaN, bestCost = float.MaxValue;
                    foreach (float c in candidates)
                    {
                        float x = Mathf.Clamp(c, -laneLimit, laneLimit);
                        // Цена: сначала не задеть стену, потом не задеть опасность, потом — меньше перекладки.
                        float cost = (Blocked(x, window, true) ? 1000f : 0f) + (Blocked(x, window, false) ? 100f : 0f) + (BlockedSoft(x, window) ? 10f : 0f) + Mathf.Abs(x - lane);
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            best = x;
                        }
                    }
                    desired = best;
                    bad = true;
                    // Проезда без опасности нет — едем осторожно (фиксированный потолок, а не «каждый кадр медленнее»).
                    // В «безумии» опасность впереди всегда: ИИ не тормозит перед ней, а несётся сквозь хаос, как игрок.
                    if (bestCost >= 100f && !Events.ChaosPower.Current.Wild) speedCap = Mathf.Min(speedCap, 16f);
                    // Перекладка к стене занимает время: если до неё не успеть, сбросить скорость.
                    foreach (var b in blocked)
                    {
                        if (!b.obstacle || b.near > window || (lane < b.lo || lane > b.hi)) continue;
                        float shift = Mathf.Abs(desired - lane);
                        float needed = shift / (LaneRate * 1.4f);
                        if (needed > 0.05f) speedCap = Mathf.Min(speedCap, Mathf.Max(9f, b.near / needed));
                    }
                    if (nearestBad < 45f || bestGood == float.MaxValue) return true;
                }
            }
            if (bestGood < float.MaxValue)
            {
                // Хорошая цель не должна вести в плохую полосу.
                desired = Blocked(goodLane, nearestBad + 30f, false) || Blocked(goodLane, nearestBad + 30f, true) ? lane : goodLane;
                return true;
            }
            return false;
        }

        /// Полоса x перекрыта опасностями (или только стенами), которые начинаются ближе window метров.
        bool Blocked(float x, float window, bool obstacles)
        {
            foreach (var b in blocked)
                if (b.near <= window && !b.soft && b.obstacle == obstacles && x > b.lo && x < b.hi) return true;
            return false;
        }

        bool BlockedSoft(float x, float window)
        {
            foreach (var b in blocked)
                if (b.near <= window && b.soft && x > b.lo && x < b.hi) return true;
            return false;
        }

        /// Сцепление покрытия вдоль трассы для этой машины: луч вниз в каждом сэмпле оси.
        /// ИИ заранее знает, где грунт или брусчатка, — как живой водитель, видящий дорогу.
        static float[] SurfaceGrip(TrackPath path, CarSpec spec)
        {
            var result = new float[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                result[i] = 1f;
                if (!Physics.Raycast(path.Point(i) + Vector3.up * 4f, Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider.attachedRigidbody != null) continue;
                var surface = Surface.Of(hit.collider);
                if (surface != null) result[i] = surface.Grip * (surface.Dusty ? spec.LooseSurfaceGrip : 1f);
            }
            return result;
        }

        float Lateral(Vector3 position, float progress) => Vector3.Dot(position - track.PointAt(progress), track.SideAt(progress));

        void Update()
        {
            if (track == null || car == null) return;
            if (car.Locked)
            {
                car.SetInput(new CarInput { Throttle = 0f });
                lane = Lateral(transform.position, track.Nearest(transform.position));
                return;
            }

            float dt = Time.deltaTime;
            Vector3 position = transform.position;
            float progress = track.Nearest(position);
            float speed = car.Speed;
            var physics = car.Physics;

            // Своя линия: гоночная + характер + медленный «шум» (не едем след в след).
            float noise = (Mathf.PerlinNoise(noiseSeed, progress * 18f) - 0.5f) * 2f * skill.LineNoise;
            float target = line.LateralAt(progress + 12f / track.Length) + laneBias + noise;
            float speedCap = float.MaxValue;
            Traffic(progress, speed, ref target, ref speedCap);

            // Смещаемся, только когда зона близко, и возвращаемся на свою линию, проехав её.
            avoiding = false;
            if (PickHazard(progress, speed, out float hazardLane, out bool bad, out float hazardCap))
            {
                target = Mathf.Clamp(hazardLane, -laneLimit + 0.8f, laneLimit - 0.8f);
                avoiding = bad;
                speedCap = Mathf.Min(speedCap, hazardCap);
            }
            // Нет бонуса — заезжаем в ящик «?» впереди, если до него недалеко перестроиться.
            if (!avoiding && PowerUps.PowerUpDirector.Current != null && PowerUps.PowerUpDirector.Current.BoxLane(car, progress, lane, out float boxLane))
                target = Mathf.Clamp(boxLane, -laneLimit + 0.6f, laneLimit - 0.6f);
            target = Mathf.Clamp(target, -laneLimit, laneLimit);
            lane = Mathf.MoveTowards(lane, target, LaneRate * dt * Mathf.Lerp(0.6f, 1.4f, Mathf.Clamp01(speed / 30f)));

            // Руль: преследование точки впереди на своей полосе.
            float lookAhead = Mathf.Clamp(Mathf.Abs(speed) * 0.5f + 6f, 8f, 26f);
            float aheadP = progress + lookAhead / track.Length;
            // Хорда до точки впереди срезает дугу внутрь на L²/8R — сдвигаем цель наружу на столько же.
            float chord = line.CurvatureAt(progress + lookAhead * 0.5f / track.Length) * lookAhead * lookAhead / 8f;
            float aimLane = Mathf.Clamp(lane + Mathf.Clamp(chord, -1.8f, 1.8f), -laneLimit - 0.5f, laneLimit + 0.5f);
            Vector3 aim = track.PointAt(aheadP) + track.SideAt(aheadP) * aimLane;
            Vector3 local = transform.InverseTransformDirection(aim - position);
            float alpha = Mathf.Atan2(local.x, Mathf.Max(0.1f, local.z));
            float delta = Mathf.Atan(2f * physics.Wheelbase * Mathf.Sin(alpha) / lookAhead) * Mathf.Rad2Deg;
            float steer = Mathf.Clamp(delta / Mathf.Max(2f, physics.SteerLimit), -1f, 1f);

            // Газ и тормоз по профилю: смотрим чуть вперёд, чтобы скомпенсировать время реакции.
            float lead = Mathf.Max(0f, speed) * (0.25f + skill.Reaction * 0.5f);
            float vTarget = Mathf.Min(line.MinSpeedAhead(progress, lead + 4f), speedCap);
            // Объезжая опасность, едем не по своей линии — с запасом по скорости.
            if (avoiding) vTarget *= 0.92f;
            // Выносит наружу поворота: не цепляться за линию, а сбросить скорость.
            float curvature = line.CurvatureAt(progress + 8f / track.Length);
            float myLat = Lateral(position, progress);
            float outward = -Mathf.Sign(curvature) * (myLat - lane);
            if (Mathf.Abs(curvature) > 1f / 150f && outward > 1f)
                vTarget = Mathf.Min(vTarget, Mathf.Max(8f, speed - (outward - 1f) * 2.5f));
            // Вне дороги: спокойно возвращаемся на трассу, а не стоим.
            // В «безумии» машину то и дело выбрасывает с дороги — возвращается быстро, а не ползком.
            if (Mathf.Abs(myLat) > track.HalfWidth + 0.5f) vTarget = Mathf.Min(vTarget, Events.ChaosPower.Current.Wild ? 22f : 12f);
            TargetSpeed = vTarget;
            float throttle, brake = 0f;
            float err = vTarget - speed;
            if (err < -0.8f)
            {
                throttle = 0f;
                brake = Mathf.Clamp(-err / 2.5f, 0.25f, 1f);
            }
            else if (err < 0.4f) throttle = 0.45f;
            else throttle = 1f;

            // Занос: сбросить газ и дать машине поймать сцепление, а не дёргаться к линии.
            float slip = Mathf.Abs(physics.BodySlipAngle);
            if (slip > 10f) throttle *= Mathf.Clamp01(1f - (slip - 10f) / 18f);
            if (Mathf.Abs(steer) > 0.95f && speed > 15f) throttle *= 0.7f;

            UpdateStuck(dt, speed, alpha, ref throttle, ref brake, ref steer);
            car.SetInput(new CarInput { Throttle = throttle, Brake = brake, Steer = steer });
        }

        void Traffic(float progress, float speed, ref float target, ref float speedCap)
        {
            if (rivals == null) return;
            float myLat = Lateral(transform.position, progress);
            float length = track.Length;
            bool passing = Time.time < passUntil;
            foreach (var other in rivals)
            {
                if (other == null || other == car) continue;
                Vector3 otherPos = other.transform.position;
                float op = track.Nearest(otherPos);
                float d = (Mathf.Repeat(op - progress + 0.5f, 1f) - 0.5f) * length;
                float otherLat = Lateral(otherPos, op);
                float latGap = otherLat - myLat;
                float closing = speed - other.Speed;

                if (d > 0f && d < 32f && Mathf.Abs(latGap) < 2.6f)
                {
                    // Впереди на нашей полосе. Догоняем быстро — обходим сразу; медленно — по характеру.
                    bool urgent = closing > 3f || other.Speed < 5f;
                    bool wants = urgent || (closing > 0.3f && random.NextDouble() < skill.Aggression * 0.03f);
                    if (!passing && wants)
                    {
                        float left = otherLat - 3.2f, right = otherLat + 3.2f;
                        bool leftOk = left > -laneLimit, rightOk = right < laneLimit;
                        if (leftOk || rightOk)
                        {
                            float pick = leftOk && rightOk ? (Mathf.Abs(target - left) < Mathf.Abs(target - right) ? left : right) : leftOk ? left : right;
                            passOffset = pick;
                            passUntil = Time.time + 1.6f + d / Mathf.Max(5f, Mathf.Abs(speed)) * 0.5f;
                            passing = true;
                        }
                    }
                    // Пока не разошлись по полосам — держим дистанцию: скорость сближения гаснет к 6 м.
                    if (Mathf.Abs(latGap) < 2.2f)
                        speedCap = Mathf.Min(speedCap, other.Speed + Mathf.Sqrt(2f * 6f * Mathf.Max(0f, d - 6f)));
                }
                else if (d < 0f && d > -14f && closing < -0.8f && skill.Aggression > 0.5f && Mathf.Abs(latGap) > 1.5f)
                {
                    // Сзади догоняют: прикрываем сторону, но без резких бросков.
                    target += Mathf.Sign(latGap) * 0.8f * skill.Aggression;
                }
                if (Mathf.Abs(d) < 5.5f && Mathf.Abs(latGap) < 2.4f)
                {
                    // Бок о бок: раздвигаемся.
                    target -= Mathf.Sign(latGap == 0f ? 1f : latGap) * 1.4f;
                }
            }
            if (passing) target = passOffset;
        }

        void UpdateStuck(float dt, float speed, float alpha, ref float throttle, ref float brake, ref float steer)
        {
            if (Time.time < reverseUntil)
            {
                throttle = 0f;
                brake = 1f;
                steer = reverseSteer;
                return;
            }
            stuckFor = throttle > 0.5f && Mathf.Abs(speed) < 1f ? stuckFor + dt : 0f;
            if (stuckFor > 1.5f)
            {
                stuckFor = 0f;
                reverseUntil = Time.time + 1.4f;
                reverseSteer = -Mathf.Sign(alpha);
            }
        }
    }
}
