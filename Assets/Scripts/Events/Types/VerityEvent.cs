using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using RandomCar.Race;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Events
{
    /// VERITY (шоу-событие v6): огромный жёлтый смайлик выскакивает из-за обочины с хихиканьем и скачет по трассе
    /// рядом с машинами — низкие подскоки, высокие прыжки и мега-прыжки с сальто на 30+ м. Часто приземляется
    /// прямо перед участником, ради которого появился, иногда меняет направление и скачет к соседям. Каждое
    /// приземление — мощная ударная волна (ChaosFx.Shockwave): машины рядом подбрасывает, мины и бочки
    /// детонируют, летящие машины сбивают других — цепные реакции. Машину прямо под шаром сплющивает.
    /// Сеть: куда и как высоко прыгать, решает хост (сигнал с целью и высотой) — у всех одинаковые дуги;
    /// приземление каждый компьютер считает сам и толкает только свои машины. Шар — кинематическое тело:
    /// машины о него ударяются по-настоящему, но он не застревает в геометрии.
    public sealed class VerityEvent : RaceEvent
    {
        /// Радиус шара, м (модель Verity.fbx).
        public const float BallRadius = 2.6f;

        /// Мультяшная тяжесть прыжка: высоко, но не «висит».
        const float JumpGravity = 9.81f * 1.7f;
        const float Crouch = 0.2f;
        const int SignalJump = 1;
        const int SignalExit = 2;
        /// За сколько секунд до конца действия — прощальный прыжок за обочину.
        const float ExitBefore = 4.2f;

        static readonly string[] LandWords = { "БУМ!", "ВЕРИТИ!", "ХА-ХА!", "ПРЫГ!", "ОП!" };
        static readonly Color Yellow = new Color(1f, 0.82f, 0.1f);

        Transform character, pivot, eyeL, eyeR, smile, mouthO;
        Rigidbody body;
        TelegraphMarker marker;
        Vector3 eyeScale = Vector3.one;

        bool flying, exitJump, exited;
        Vector3 from, to, groundPos;
        float peakY, takeoffAt, upTime, duration, apex;
        int lastJump, planned;
        float landedAt = -10f, landPower, idleUntil;
        float nextBlinkAt, blinkUntil, flipSign;
        Quaternion facing = Quaternion.identity;

        /// Сколько раз приземлилась и сколько машин подбросило ударами этого розыгрыша на этом компьютере (проверка).
        public int Landings { get; private set; }
        public int DirectHits { get; private set; }

        /// Приземление: событие, точка, сила волны (проверка и счётчики щупа).
        public static event System.Action<VerityEvent, Vector3, float> Landed;

        /// Сейчас в прыжке и куда приземлится (подсказки ИИ, проверки).
        public bool Flying => flying;
        public Vector3 LandingPoint => to;
        public Vector3 Position => character != null ? character.position : Vector3.zero;

        void Awake()
        {
            character = Child("Verity");
            body = character.GetComponent<Rigidbody>();
            pivot = character.Find("Pivot");
            var model = pivot.Find("Model");
            eyeL = model.Find("EyeL");
            eyeR = model.Find("EyeR");
            smile = model.Find("Smile");
            mouthO = model.Find("MouthO");
            if (eyeL != null) eyeScale = eyeL.localScale;
            if (mouthO != null) mouthO.gameObject.SetActive(false);
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            character.gameObject.SetActive(false);
        }

        // ---------- появление ----------

        protected override void OnTelegraph()
        {
            flying = exitJump = exited = false;
            lastJump = planned = 0;
            Landings = DirectHits = 0;
            landedAt = -10f;
            var h = At(Progress, 0f);
            int side = Placement.Lateral < 0f ? -1 : 1;
            groundPos = GroundNear(h.Axis + h.Right * side * (RoadHalf + 4f), h.Axis.y);
            // За обочиной стена или дом (город, «Тель-Авив»), обрыв или низкий потолок — выскакивает у края дороги.
            if (Blocked(groundPos) || Mathf.Abs(groundPos.y - h.Axis.y) > 4f)
                groundPos = GroundNear(h.Axis + h.Right * side * Mathf.Max(0f, RoadHalf - 2.5f), h.Axis.y);
            from = to = groundPos;
            Center = groundPos;
            Radius = 9f;
            facing = Quaternion.LookRotation(-h.Right * side, Vector3.up);
            character.gameObject.SetActive(true);
            Place(groundPos + Vector3.up * -BallRadius);
            pivot.localScale = Vector3.one;
            nextBlinkAt = Time.time + 1f;
            marker.Show(groundPos, h.Forward, 3f, false, false);
            var fx = ChaosEffects;
            fx?.Sound(SoundBank.VerityGiggle, groundPos, 1f, 1f);
            fx?.Blast(groundPos, BlastStyle.ColorPuff, 1.3f, new Color(0.82f, 0.72f, 0.55f, 0.85f));
            fx?.Pop(groundPos + Vector3.up * 7f, "ВЕРИТИ!", Yellow, 1.8f);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            // Выскакивает из-под обочины с перелётом и пружинит.
            float k = EaseOutBack(Mathf.Clamp01(progress * 1.35f));
            Place(groundPos + Vector3.up * (BallRadius * (2f * k - 1f)));
            marker.Pulse(progress);
        }

        protected override void OnActivate()
        {
            marker.Hide();
            Place(groundPos + Vector3.up * BallRadius);
            idleUntil = Time.time + 0.15f;
            if (Authority) PlanJump(false);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            if (exited) return;
            if (flying && Time.time >= takeoffAt + duration) Land();
            if (flying)
            {
                // Метка на месте приземления растёт по мере падения: видно, куда шар рухнет.
                float k = Mathf.Clamp01((Time.time - takeoffAt) / duration);
                marker.Show(to, Context.Track.DirectionAt(Context.Track.Nearest(to)), Mathf.Lerp(2f, 6f + apex * 0.12f, k), false, false);
                marker.Pulse(k);
            }
            Center = flying ? to : groundPos;
            if (!Authority || flying || exitJump || lastJump != planned || Time.time < idleUntil) return;
            PlanJump(ActiveElapsed > Definition.ActiveSeconds - ExitBefore);
        }

        protected override void OnEnd()
        {
            marker.Hide();
            if (!exited && character.gameObject.activeSelf) Poof(character.position);
        }

        protected override void OnEndUpdate(float progress)
        {
            if (character.gameObject.activeSelf) pivot.localScale = Vector3.one * Mathf.Max(0.01f, 1f - progress);
        }

        protected override void Cleanup()
        {
            if (Landings > 0) Debug.Log($"[RandomCar] Верити: розыгрыш {Serial} окончен — прыжков {lastJump}, приземлений {Landings}, сверху на машину {DirectHits}");
            flying = false;
            character.gameObject.SetActive(false);
            pivot.localScale = Vector3.one;
            pivot.localRotation = Quaternion.identity;
            if (mouthO != null) mouthO.gameObject.SetActive(false);
            if (smile != null) smile.gameObject.SetActive(true);
            marker.Hide();
        }

        /// ИИ от Верити не уворачивается (как от трюков): она сама выбирает, на кого прыгнуть.
        public override void CollectHazards(List<HazardInfo> list) { }

        public override void CollectBlocks(List<RoadBlock> list) { }

        // ---------- прыжки ----------

        /// Хост: куда и как высоко прыгнуть дальше. Сигнал применяется здесь и уходит клиентам.
        void PlanJump(bool exit)
        {
            var focus = FocusRacer() ?? NearestRacer(groundPos);
            float roll = Random.value;
            float h = exit ? 26f : roll < 0.38f ? Random.Range(6f, 11f) : roll < 0.76f ? Random.Range(14f, 22f) : Random.Range(27f, 38f);
            var track = Context.Track;
            float length = track.Length;
            float here = track.Nearest(groundPos);
            Vector3 target;
            if (exit)
            {
                // Прощальный прыжок: далеко за обочину впереди, там исчезнет.
                var p = At(TrackPath.Wrap(here + 30f / length), 0f);
                float side = Mathf.Sign(Vector3.Dot(groundPos - p.Axis, p.Right) + 0.01f);
                target = p.Axis + p.Right * side * (RoadHalf + 38f);
            }
            else
            {
                // Отстал от участника — мега-прыжок вдогонку.
                if (focus != null && Mathf.Abs(Ahead(here, focus.Progress) * length) > 110f) h = Mathf.Max(h, 30f);
                float flight = FlightTime(h) + Crouch;
                float pick = Random.value;
                Racer other = focus != null && pick >= 0.62f && pick < 0.86f ? NeighbourOf(focus) : null;
                if (focus != null && pick < 0.62f) target = Predict(focus, flight, Random.Range(2f, 11f), Random.Range(-1.5f, 1.5f));
                else if (other != null) target = Predict(other, flight, Random.Range(1f, 7f), Random.Range(-1f, 1f));
                else
                {
                    // Смена направления: в сторону, назад или вперёд по трассе.
                    float along = Random.Range(-25f, 35f);
                    var p = At(TrackPath.Wrap(here + along / length), 0f);
                    target = p.Axis + p.Right * Random.Range(-(RoadHalf - 1.5f), RoadHalf - 1.5f);
                }
                // Не дальше разумного за один прыжок (мультяшно, но не телепорт).
                Vector3 d = target - groundPos;
                d.y = 0f;
                float reach = 45f * flight;
                if (d.magnitude > reach) target = groundPos + d.normalized * reach;
            }
            // Потолок (тоннель, мост над дорогой): прыжок не выше, чем есть места.
            Vector3 mid = (groundPos + target) * 0.5f;
            float room = Mathf.Min(Ceiling(groundPos), Mathf.Min(Ceiling(GroundNear(mid, mid.y)), Ceiling(GroundNear(target, groundPos.y))));
            h = Mathf.Clamp(Mathf.Min(h, room - 2f * BallRadius - 1f), 1f, 40f);
            idleUntil = Time.time + Random.Range(0.25f, 0.7f) + FlightTime(h) + Crouch;
            Signal(exit ? SignalExit : SignalJump, ++planned, new Vector3(target.x, h, target.z));
        }

        protected override void OnSignal(int code, int index, Vector3 value)
        {
            if ((code != SignalJump && code != SignalExit) || index <= lastJump || exited) return;
            lastJump = index;
            if (flying) Land();
            StartJump(new Vector3(value.x, groundPos.y, value.z), value.y, code == SignalExit);
        }

        void StartJump(Vector3 target, float height, bool exit)
        {
            from = groundPos;
            to = GroundNear(target, Context.Track.PointAt(Context.Track.Nearest(target)).y);
            apex = height;
            peakY = Mathf.Max(from.y, to.y) + height;
            upTime = Mathf.Sqrt(2f * Mathf.Max(0.05f, peakY - from.y) / JumpGravity);
            duration = upTime + Mathf.Sqrt(2f * Mathf.Max(0.05f, peakY - to.y) / JumpGravity);
            takeoffAt = Time.time + Crouch;
            flying = true;
            exitJump = exit;
            // Сальто в мега-прыжке (сторона — из сида: у всех игроков одинаково).
            flipSign = height >= 24f ? (RandomRange(0f, 1f) < 0.5f ? -1f : 1f) : 0f;
            var fx = ChaosEffects;
            fx?.Sound(SoundBank.VerityJump, from, 0.8f, Mathf.Lerp(1.25f, 0.85f, Mathf.InverseLerp(6f, 38f, height)));
            if (height >= 24f) fx?.Pop(from + Vector3.up * 7f, exit ? "ПОКА-А-А!" : "ВЖУ-У-УХ!", Yellow, 1.6f);
            fx?.Blast(from, BlastStyle.ColorPuff, 0.8f, new Color(0.85f, 0.78f, 0.62f, 0.7f));
        }

        static float FlightTime(float height) => 2f * Mathf.Sqrt(2f * Mathf.Max(0.05f, height) / JumpGravity);

        /// Точка на дуге прыжка (центр шара) в момент time.
        Vector3 ArcAt(float time)
        {
            float u = time - takeoffAt;
            if (u <= 0f) return from + Vector3.up * BallRadius;
            if (u >= duration) return to + Vector3.up * BallRadius;
            float s = u / duration;
            float y = u < upTime
                ? from.y + Mathf.Sqrt(2f * JumpGravity * (peakY - from.y)) * u - 0.5f * JumpGravity * u * u
                : peakY - 0.5f * JumpGravity * (u - upTime) * (u - upTime);
            Vector3 flat = Vector3.Lerp(from, to, s);
            return new Vector3(flat.x, y + BallRadius, flat.z);
        }

        void Land()
        {
            flying = false;
            groundPos = to;
            landedAt = Time.time;
            Landings++;
            marker.Hide();
            Place(to + Vector3.up * BallRadius);
            if (exitJump)
            {
                exited = true;
                Poof(to + Vector3.up * BallRadius);
                character.gameObject.SetActive(false);
                return;
            }
            float strength = 9f + apex * 0.42f;
            float radius = 10f + apex * 0.32f;
            landPower = Mathf.Clamp01(apex / 35f);
            var fx = ChaosEffects;
            // Прямое попадание: машину под шаром сплющивает и подбрасывает (только свои машины этого компьютера).
            foreach (var r in RacersNear(to, BallRadius + 1.4f))
            {
                if (r.Finished || !Simulates(r)) continue;
                var car = r.Controller;
                Vector3 away = car.transform.position - to;
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : car.transform.right;
                car.ApplySquash(new Vector3(1.35f, 0.45f, 1.35f), 1.6f, "расплющило Верити");
                car.ApplyLaunch(away * 7f + Vector3.up * (strength * 0.85f), Random.onUnitSphere * 5f, "Верити приземлилась сверху");
                DirectHits++;
            }
            fx?.Shockwave(to, radius, strength, 1, null);
            fx?.Sound(SoundBank.VerityLand, to, 0.7f + 0.3f * landPower, Mathf.Lerp(1.15f, 0.8f, landPower));
            if (landPower > 0.6f) fx?.Sound(SoundBank.Thwomp, to, 0.8f, 0.9f);
            fx?.Blast(to, BlastStyle.ColorPuff, 1.2f + apex * 0.035f, new Color(0.84f, 0.74f, 0.58f, 0.85f));
            fx?.Blast(to + Vector3.up * 0.4f, BlastStyle.Spark, 0.7f + landPower * 0.6f);
            fx?.Scorch(to, 3.5f + apex * 0.1f, true);
            fx?.Pop(to + Vector3.up * (BallRadius * 2f + 1.5f), LandWords[Mathf.Abs(lastJump * 7 + Serial) % LandWords.Length], Yellow, 1.2f + landPower * 0.8f);
            ShakeCameraNear(to, 0.35f + landPower * 0.7f, 70f);
            Landed?.Invoke(this, to, strength);
        }

        void Poof(Vector3 at)
        {
            var fx = ChaosEffects;
            fx?.Blast(at, BlastStyle.Fake, 1.6f);
            fx?.Blast(at, BlastStyle.ColorPuff, 1.8f, new Color(1f, 0.86f, 0.2f, 0.85f));
            fx?.Sound(SoundBank.VerityGiggle, at, 0.9f, 1.15f);
            fx?.Pop(at + Vector3.up * 4f, "ПОКА!", Yellow, 1.5f);
        }

        // ---------- движение и анимация ----------

        void FixedUpdate()
        {
            if (!character.gameObject.activeSelf || Stage != EventStage.Active || exited) return;
            body.MovePosition(flying ? ArcAt(Time.time) : groundPos + Vector3.up * BallRadius);
        }

        void Place(Vector3 center)
        {
            character.position = center;
            body.position = center;
        }

        void LateUpdate()
        {
            if (!character.gameObject.activeSelf) return;
            float now = Time.time;
            // Лицо — к камере этого игрока (по рысканию, плавно): смайлик всегда «смотрит» на того, кто играет.
            var cam = Context != null && Context.Camera != null ? Context.Camera.transform : Camera.main != null ? Camera.main.transform : null;
            if (cam != null)
            {
                Vector3 toCam = cam.position - character.position;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 0.5f) facing = Quaternion.Slerp(facing, Quaternion.LookRotation(toCam.normalized, Vector3.up), 1f - Mathf.Exp(-5f * Time.deltaTime));
            }
            Quaternion flip = Quaternion.identity;
            Vector3 scale = Vector3.one;
            float sinceLand = now - landedAt;
            if (flying)
            {
                float u = now - takeoffAt;
                if (u < 0f)
                {
                    // Присел перед прыжком.
                    float c = 1f - Mathf.Clamp01(-u / Crouch);
                    scale = new Vector3(1f + 0.2f * c, 1f - 0.3f * c, 1f + 0.2f * c);
                }
                else
                {
                    float k = Mathf.Clamp01(u / duration);
                    // Вытянулся на взлёте, круглый в полёте, чуть вытянут перед падением.
                    float stretch = Mathf.Exp(-u * 5f) * 0.28f + Mathf.Clamp01((k - 0.8f) / 0.2f) * 0.12f;
                    scale = new Vector3(1f - stretch * 0.5f, 1f + stretch, 1f - stretch * 0.5f);
                    if (flipSign != 0f) flip = Quaternion.AngleAxis(flipSign * 360f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.85f, k)), Vector3.right);
                }
            }
            else if (sinceLand < 1.2f)
            {
                // Плюх и пружинка после приземления: сильнее после высокого прыжка.
                float w = Mathf.Exp(-sinceLand * 6f) * Mathf.Cos(sinceLand * 19f) * (0.25f + 0.25f * landPower);
                scale = new Vector3(1f + w * 0.7f, 1f - w, 1f + w * 0.7f);
            }
            else
            {
                // Ждёт — подпрыгивает на месте.
                float b = Mathf.Abs(Mathf.Sin(now * 6f)) * 0.06f;
                scale = new Vector3(1f + b * 0.4f, 1f - b, 1f + b * 0.4f);
            }
            pivot.rotation = facing * flip;
            pivot.localScale = scale;
            // Моргание раз в 2–4 с.
            if (now >= nextBlinkAt)
            {
                blinkUntil = now + 0.13f;
                nextBlinkAt = now + Random.Range(1.8f, 4f);
            }
            float eye = now < blinkUntil ? 0.12f : 1f;
            if (eyeL != null) eyeL.localScale = new Vector3(eyeScale.x, eyeScale.y * eye, eyeScale.z);
            if (eyeR != null) eyeR.localScale = new Vector3(eyeScale.x, eyeScale.y * eye, eyeScale.z);
            // Рот «О» — в больших прыжках и сразу после жёсткого приземления.
            bool wow = flying && apex >= 20f && now > takeoffAt || !flying && sinceLand < 0.45f && landPower > 0.55f;
            if (mouthO != null && mouthO.gameObject.activeSelf != wow) mouthO.gameObject.SetActive(wow);
            if (smile != null && smile.gameObject.activeSelf == wow) smile.gameObject.SetActive(!wow);
        }

        // ---------- помощники ----------

        /// Где окажется участник через seconds (по скорости), плюс ahead вперёд по трассе; на дороге.
        Vector3 Predict(Racer racer, float seconds, float ahead, float jitter)
        {
            var track = Context.Track;
            var car = racer.Controller;
            Vector3 v = car.Physics.Body != null && !car.Physics.Body.isKinematic ? car.Physics.Body.linearVelocity : racer.Car.forward * car.Speed;
            v.y = 0f;
            Vector3 p = racer.Car.position + v * seconds;
            float prog = track.Nearest(p);
            Vector3 axis = track.PointAt(prog);
            Vector3 right = track.SideAt(prog);
            float limit = RoadHalf - 1.5f;
            float lat = Mathf.Clamp(Vector3.Dot(p - axis, right) + jitter, -limit, limit);
            return axis + right * lat + track.DirectionAt(prog) * ahead;
        }

        Racer NearestRacer(Vector3 at)
        {
            Racer best = null;
            float bestD = float.MaxValue;
            foreach (var r in Racers)
            {
                if (r.Finished || r.Retired || r.Controller == null) continue;
                float d = (r.Car.position - at).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = r;
                }
            }
            return best;
        }

        /// Соперник рядом с участником (до 80 м) — цепные реакции на соседях.
        Racer NeighbourOf(Racer focus)
        {
            Racer best = null;
            float bestD = 80f;
            foreach (var r in Racers)
            {
                if (r == focus || r.Finished || r.Retired || r.Controller == null) continue;
                float d = Vector3.Distance(r.Car.position, focus.Car.position);
                if (d < bestD)
                {
                    bestD = d;
                    best = r;
                }
            }
            return best;
        }

        static float Ahead(float from, float to)
        {
            float d = TrackPath.Wrap(to - from);
            return d > 0.5f ? d - 1f : d;
        }

        /// Земля под точкой рядом с высотой refY (мост или эстакада над дорогой не считаются землёй).
        static Vector3 GroundNear(Vector3 p, float refY)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, refY + 6f, p.z), Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 result = new Vector3(p.x, refY, p.z);
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

        /// В объёме шара над точкой земли — неподвижная геометрия карты (дом, стена) или потолок ниже шара.
        static bool Blocked(Vector3 ground)
        {
            if (Ceiling(ground) < 2f * BallRadius + 1f) return true;
            foreach (var c in Physics.OverlapSphere(ground + Vector3.up * (BallRadius + 0.3f), BallRadius * 0.9f, ~0, QueryTriggerInteraction.Ignore))
                if (c.attachedRigidbody == null && c.GetComponentInParent<RaceEvent>() == null) return true;
            return false;
        }

        /// Сколько места над землёй (тоннель, мост, эстакада), м; 60 — открытое небо.
        static float Ceiling(Vector3 ground)
        {
            var hits = Physics.RaycastAll(ground + Vector3.up * 0.3f, Vector3.up, 60f, ~0, QueryTriggerInteraction.Ignore);
            float best = 60f;
            foreach (var hit in hits)
            {
                if (hit.collider.attachedRigidbody != null || hit.collider.GetComponentInParent<RaceEvent>() != null) continue;
                best = Mathf.Min(best, hit.distance + 0.3f);
            }
            return best;
        }
    }
}
