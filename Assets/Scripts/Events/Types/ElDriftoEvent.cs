using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using RandomCar.Core;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Events
{
    /// EL DRIFTO BUS (шоу-событие v6). Сценарий: звучит крик «EL DRIFTO!» (файл пользователя, ровно один раз
    /// за появление), из-за обочины (или, если там стена, — с дороги впереди, из-за поворота) вылетает 12-тонный
    /// автобус, срывается в дрифт и боком пересекает трассу, перекладывается обратно (с «пончиком» на 360°),
    /// сносит машины и всё на пути, затем теряет управление, скользит и опрокидывается в кювет. Полежит там
    /// и исчезает в облаке дыма.
    /// Физика: настоящий Rigidbody (ChaosBody вида Bus, коллайдеры кузова), им управляет хост силами;
    /// у клиентов — кинематическая копия по состоянию хоста (20 раз/с). Удар по машине решает компьютер,
    /// который её считает (свою машину — сам), — мультяшный отброс с кувырком сверх обычного столкновения.
    /// Цепные реакции: ударная волна от ударов и падения в кювет поджигает мины и бочки, летящие машины сбивают других.
    public sealed class ElDriftoEvent : RaceEvent
    {
        const int SignalCrash = 1;
        const int SignalGone = 2;
        const float WheelRadius = 0.56f;
        /// Проходов дрифта через дорогу до кювета; время одного прохода (не короче / не длиннее), предел всего дрифта.
        const int Passes = 3;
        const float MinPass = 1.3f, MaxPass = 3.2f, MaxDrift = 10f;
        /// Полуколея и полубаза модели (м): крен и клевок кузова приподнимают его, чтобы нижние колёса не уходили в дорогу.
        const float HalfTrack = 1.2f, HalfBase = 3.1f;

        enum Phase { None, Entry, Drift, Spin, Ditch, Lying }

        /// Крик «EL DRIFTO!» — оригинальная запись пользователя (Assets/Art/Audio/Sfx/Events/el-drifto.mp3).
        [SerializeField] AudioClip scream;

        AudioSource voice, screech;
        ChaosBody bus;
        Rigidbody rb;
        Transform lean;
        Transform[] wheels;
        Quaternion[] wheelRest;
        float[] wheelSpin;
        ParticleSystem smokeL, smokeR, sparks, dust;
        TelegraphMarker marker;
        Collider[] busColliders;

        Phase phase;
        float phaseAt;
        Vector3 spawnPos;
        Quaternion spawnRot;
        int side;
        float entryProgress;
        bool onRoad;
        float along;
        int dir;
        int passes;
        int entryHops;
        float stuckFor;
        float activeAt;
        /// Для проверки: смены фаз со временем от выезда и сколько секунд автобус провёл на дороге (только хост).
        readonly System.Text.StringBuilder timeline = new System.Text.StringBuilder();
        float onRoadSeconds;
        /// Для проверки: как близко подъехал участник, ради которого выехал автобус (м, по земле, центр — центр).
        float closestFocus;
        bool ditchKick, crashed, gone;
        float spinDir;
        int screamSerial = -1;
        float leanRoll, leanPitch;
        float trackAt = -1f, trackP;
        Vector3 trackForward = Vector3.forward, trackRight = Vector3.right, trackAxis;
        readonly Dictionary<CarController, float> lastHit = new Dictionary<CarController, float>();
        readonly List<(Collider, Collider)> ignored = new List<(Collider, Collider)>();
        float sparkAt;

        /// Сколько машин сбил автобус на этом компьютере за розыгрыш, сколько раз прозвучал крик (проверки).
        public int CarHits { get; private set; }
        public int Screams { get; private set; }
        public bool OnRoadEntry => onRoad;
        public string PhaseName => phase.ToString();
        public ChaosBody Bus => bus;
        public AudioSource Voice => voice;
        public AudioClip ScreamClip => scream;

        /// Автобус сбил машину: событие, машина, сила удара (м/с).
        public static event System.Action<ElDriftoEvent, CarController, float> HitCar;

        public void SetScream(AudioClip clip) => scream = clip;

        void Awake()
        {
            voice = Child("Voice").GetComponent<AudioSource>();
            bus = Child("Bus").GetComponent<ChaosBody>();
            rb = bus.GetComponent<Rigidbody>();
            busColliders = bus.GetComponents<Collider>();
            var visual = bus.transform.Find("Visual");
            lean = visual.Find("Lean");
            var model = lean.Find("Model");
            string[] names = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            wheels = new Transform[names.Length];
            wheelRest = new Quaternion[names.Length];
            wheelSpin = new float[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                wheels[i] = model.Find(names[i]);
                if (wheels[i] != null) wheelRest[i] = wheels[i].localRotation;
            }
            smokeL = bus.transform.Find("FxSmokeL").GetComponent<ParticleSystem>();
            smokeR = bus.transform.Find("FxSmokeR").GetComponent<ParticleSystem>();
            sparks = bus.transform.Find("FxSparks").GetComponent<ParticleSystem>();
            dust = bus.transform.Find("FxDust").GetComponent<ParticleSystem>();
            screech = bus.transform.Find("Screech").GetComponent<AudioSource>();
            screech.clip = SoundBank.TireScreech;
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            bus.gameObject.SetActive(false);
        }

        // ---------- предупреждение: крик ----------

        protected override void OnTelegraph()
        {
            phase = Phase.None;
            passes = 0;
            entryHops = 0;
            stuckFor = 0f;
            ditchKick = crashed = gone = false;
            CarHits = 0;
            lastHit.Clear();
            trackAt = -1f;
            PlanEntry();
            var h = At(Progress, 0f);
            Center = h.Axis;
            Radius = RoadHalf;
            marker.Show(h.Axis, h.Forward, Mathf.Min(RoadHalf, 9f), false, true, 1.4f);
            PlayScream(spawnPos);
            var fx = ChaosEffects;
            fx?.Pop(spawnPos + Vector3.up * 5f, "EL DRIFTO!", new Color(1f, 0.55f, 0.1f), 2.2f);
            fx?.Flash(spawnPos + Vector3.up * 1.2f + spawnRot * Vector3.forward * 5f, new Color(1f, 0.95f, 0.75f), 6f, 0.4f);
            fx?.Blast(spawnPos, BlastStyle.ColorPuff, 1.2f, new Color(0.8f, 0.72f, 0.58f, 0.8f));
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            // Автобус «газует» за обочиной: дым и вспышки фар, пока звучит крик.
            if (Time.frameCount % 12 == 0) ChaosEffects?.Blast(spawnPos + Vector3.up * 0.5f, BlastStyle.ColorPuff, 0.5f, new Color(0.9f, 0.9f, 0.9f, 0.6f));
        }

        /// Громкость крика: от громкости эффектов, но так, чтобы его было слышно (выключенные эффекты — тишина).
        static float VoiceVolume()
        {
            float sfx = GameSettings.SfxVolume;
            return sfx <= 0.001f ? 0f : Mathf.Clamp01(Mathf.Max(sfx * 2f, 0.55f));
        }

        /// Крик звучит ровно один раз за появление автобуса: повторный вызов того же розыгрыша — без звука.
        /// В сети клиент, получивший событие позже хоста, начинает с того же места записи (крик у всех одновременно).
        void PlayScream(Vector3 at)
        {
            if (scream == null)
            {
                Debug.LogError("[RandomCar] EL DRIFTO: нет записи крика (Assets/Art/Audio/Sfx/Events/el-drifto.mp3)");
                return;
            }
            if (screamSerial == Serial && voice.isPlaying) return;
            screamSerial = Serial;
            voice.transform.position = at + Vector3.up * 2f;
            voice.clip = scream;
            voice.volume = VoiceVolume();
            voice.time = ClockOffset > 0.15f && ClockOffset < scream.length - 0.2f ? ClockOffset : 0f;
            voice.Play();
            Screams++;
            Debug.Log($"[RandomCar] EL DRIFTO: крик «{scream.name}» ({scream.length:F2} с) с {voice.time:F2} с, розыгрыш {Serial}, громкость {voice.volume:F2}");
        }

        // ---------- выход на трассу ----------

        /// Сдвиги места выезда вдоль трассы (м) и отступы от кромки (м), которые пробует автобус: в городе обочина
        /// занята фонарями, пальмами и домами — ищется переулок или площадка рядом.
        static readonly float[] EntryShifts = { 0f, 12f, -12f, 24f, 36f };
        static readonly float[] EntryOffsets = { 13f, 9f, 18f, 6f };

        /// Откуда вылетает автобус: с обочины на стороне Placement (или с другой), если там есть земля и нет стены;
        /// иначе — с дороги впереди. Только неподвижная геометрия карты: у хоста и клиентов выбор одинаковый.
        void PlanEntry()
        {
            int preferred = Placement.Lateral < 0f ? -1 : 1;
            float length = Context.Track.Length;
            foreach (float shift in EntryShifts)
            {
                var hs = At(TrackPath.Wrap(Progress + shift / length), 0f);
                foreach (int s in new[] { preferred, -preferred })
                    foreach (float offset in EntryOffsets)
                    {
                        if (!SideClear(hs, s, offset, out var ground, out var rotation)) continue;
                        side = s;
                        spawnPos = ground;
                        spawnRot = rotation;
                        entryProgress = TrackPath.Wrap(Progress + shift / length);
                        onRoad = false;
                        return;
                    }
            }
            var h = At(Progress, 0f);
            // Стены по обе стороны (тоннель, мост, дома вплотную): автобус выходит из-за поворота прямо на дорогу впереди.
            onRoad = true;
            side = preferred;
            entryProgress = TrackPath.Wrap(Progress + 30f / length);
            var ahead = At(entryProgress, 0f);
            spawnPos = Ground(ahead.Axis + ahead.Right * preferred * RoadHalf * 0.45f);
            spawnRot = Quaternion.LookRotation(ahead.Forward * 0.8f - ahead.Right * preferred * 0.6f, Vector3.up);
        }

        static readonly Vector3 BusHalf = new Vector3(1.4f, 1.5f, 5.6f);

        bool SideClear(HazardPoint h, int s, float offset, out Vector3 ground, out Quaternion rotation)
        {
            Vector3 guess = h.Axis + h.Right * s * (RoadHalf + offset);
            ground = GroundNear(guess, h.Axis.y);
            Vector3 toRoad = (h.Axis - ground);
            toRoad.y = 0f;
            Vector3 heading = (toRoad.normalized * 0.8f + h.Forward * 0.6f).normalized;
            rotation = Quaternion.LookRotation(heading, Vector3.up);
            if (Mathf.Abs(ground.y - h.Axis.y) > 3.5f) return false;
            Vector3 center = ground + Vector3.up * (BusHalf.y + 0.7f);
            foreach (var c in Physics.OverlapBox(center, BusHalf, rotation, ~0, QueryTriggerInteraction.Ignore))
                if (Static(c)) return false;
            // Путь до дороги: низкие бордюры и отбойники автобус перепрыгнет, стену или дом — нет.
            float distance = toRoad.magnitude - RoadHalf;
            foreach (var hit in Physics.BoxCastAll(center, BusHalf * 0.9f, toRoad.normalized, rotation, Mathf.Max(1f, distance), ~0, QueryTriggerInteraction.Ignore))
                if (Static(hit.collider) && hit.point.y > ground.y + 1.6f) return false;
            return true;
        }

        static bool Static(Collider c) => c.attachedRigidbody == null && c.GetComponentInParent<RaceEvent>() == null;

        protected override void OnActivate()
        {
            marker.Hide();
            var h = At(entryProgress, 0f);
            Vector3 velocity = Vector3.zero;
            if (Authority)
            {
                var focus = FocusRacer();
                float speed = focus != null ? Mathf.Max(focus.Controller.Speed, 10f) : 20f;
                along = Mathf.Clamp(speed * 0.6f, 12f, 30f);
                Vector3 across = -h.Right * side;
                // С обочины — вылетает подпрыгнув (через бордюр и отбойник), с дороги — сразу в занос.
                velocity = onRoad ? h.Forward * along + across * 7f : h.Forward * along + across * 15f + Vector3.up * 4.5f;
            }
            bus.NetId = BodyId(0);
            bus.Spawn(Context.Track, RoadHalf, spawnPos + Vector3.up * 0.05f, spawnRot, velocity, Vector3.zero, !Authority);
            lean.localRotation = Quaternion.identity;
            lean.localPosition = Vector3.zero;
            lean.localScale = Vector3.one;
            IgnoreProxies(true);
            phase = Phase.Entry;
            phaseAt = activeAt = Time.time;
            timeline.Clear().Append("Entry 0");
            onRoadSeconds = 0f;
            closestFocus = float.PositiveInfinity;
            dir = -side;
            var fx = ChaosEffects;
            fx?.Sound(SoundBank.BusHorn, spawnPos, 1f, 1f);
            fx?.Blast(spawnPos, BlastStyle.Big, 1.2f, new Color(0.75f, 0.68f, 0.5f, 0.85f));
            ShakeCameraNear(spawnPos, 0.5f, 60f);
        }

        /// Хост: удары о кинематические копии чужих машин не считаются (их владельцы сами получат удар своей копии автобуса).
        void IgnoreProxies(bool ignore)
        {
            if (!ignore)
            {
                foreach (var (a, b) in ignored)
                    if (a != null && b != null) Physics.IgnoreCollision(a, b, false);
                ignored.Clear();
                return;
            }
            if (!Authority) return;
            foreach (var r in Racers)
            {
                if (r.Controller == null || r.Controller.Physics.Simulated) continue;
                foreach (var c in r.Car.GetComponentsInChildren<Collider>())
                    foreach (var b in busColliders)
                    {
                        Physics.IgnoreCollision(b, c, true);
                        ignored.Add((b, c));
                    }
            }
        }

        // ---------- дрифт (хост) ----------

        void FixedUpdate()
        {
            if (!Authority || !bus.Live || bus.Remote || Stage != EventStage.Active) return;
            float dt = Time.fixedDeltaTime;
            Vector3 pos = rb.position;
            RefreshTrack(pos);
            float lateral = Vector3.Dot(pos - trackAxis, trackRight);
            bool grounded = Grounded(pos);
            if (Mathf.Abs(lateral) < RoadHalf) onRoadSeconds += dt;
            var focusCar = FocusRacer();
            if (focusCar != null && focusCar.Car != null)
            {
                Vector3 d = focusCar.Car.position - pos;
                d.y = 0f;
                closestFocus = Mathf.Min(closestFocus, d.magnitude);
            }
            Vector3 v = rb.linearVelocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            float t = Time.time - phaseAt;
            if (phase == Phase.Entry || phase == Phase.Drift || phase == Phase.Spin) Pace(dt);
            Vector3 desired = trackForward * along;
            Vector3 nose = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, Vector3.up).normalized;
            bool steer = true, upright = true;
            switch (phase)
            {
                case Phase.Entry:
                    desired += trackRight * dir * 15f;
                    nose = flat.sqrMagnitude > 1f ? flat.normalized : nose;
                    // Упёрся за обочиной (камень, отбойник, бархан) — подпрыгивает к дороге; не помогло — мультяшный
                    // прыжок с расчётом приземлиться прямо на середину дороги (полёт 1.5 с).
                    if (grounded && entryHops < 2 && t > 0.5f + entryHops * 0.8f && Mathf.Abs(lateral) > RoadHalf - 1f
                        && Vector3.Dot(flat, trackRight * dir) < 5f)
                    {
                        entryHops++;
                        if (entryHops == 1) rb.AddForce(Vector3.up * 7f + trackRight * dir * 9f, ForceMode.VelocityChange);
                        else
                        {
                            Vector3 toRoad = trackAxis - pos;
                            toRoad.y = 0f;
                            rb.linearVelocity = toRoad / 1.5f + trackForward * along * 0.3f + Vector3.up * 7.4f;
                        }
                    }
                    if (Mathf.Abs(lateral) < RoadHalf - 1f && grounded || t > 3.2f) SetPhase(Phase.Drift);
                    break;
                case Phase.Drift:
                    // Боком через дорогу: поперёк — к цели за кромкой на той стороне (не триггер «доехал до края»:
                    // на узкой дороге он срабатывал за долю секунды), нос — вдоль трассы с контрвыворотом (занос ~60°).
                    // Три прохода: первый кончается «пончиком» на 360°, второй — перекладкой, после третьего — кювет.
                    {
                        float target = dir * (RoadHalf + 0.6f);
                        // Участник в 3–35 м (подъезжает или уже впереди) — занос «ловит» его полосу: проскочить можно, но надо постараться.
                        if (AimLateral(out float aim)) target = Mathf.Lerp(target, aim, 0.7f);
                        // Далеко за обочиной (вынесло ударом, вход через кювет) — к дороге на полной скорости.
                        float crossSpeed = Mathf.Abs(lateral) > RoadHalf + 2f ? 15f : Mathf.Clamp(RoadHalf * 1.15f, 7f, 13f);
                        desired += trackRight * Mathf.Clamp((target - lateral) * 1.8f, -crossSpeed, crossSpeed);
                        nose = (trackForward * 0.85f - trackRight * dir * 0.55f).normalized;
                        bool atEdge = dir * lateral > RoadHalf - 1.6f;
                        if (atEdge && t > MinPass || t > MaxPass)
                        {
                            passes++;
                            if (passes >= Passes || Time.time - activeAt > MaxDrift) SetPhase(Phase.Ditch);
                            else if (passes == 1)
                            {
                                spinDir = dir;
                                SetPhase(Phase.Spin);
                            }
                            else
                            {
                                dir = -dir;
                                SetPhase(Phase.Drift);
                            }
                        }
                    }
                    break;
                case Phase.Spin:
                    // «Пончик» на 360°, понемногу съезжая обратно к середине; потом дрифт на другую сторону.
                    desired += trackRight * -dir * 5f;
                    steer = false;
                    if (grounded) rb.AddTorque(Vector3.up * (spinDir * 4.6f - Vector3.Dot(rb.angularVelocity, Vector3.up)) * 3f, ForceMode.Acceleration);
                    if (t > 1.35f)
                    {
                        dir = -dir;
                        SetPhase(Phase.Drift);
                    }
                    break;
                case Phase.Ditch:
                    // Потерял управление: крутится, скользит к ближнему краю, у кромки подпрыгивает и опрокидывается в кювет.
                    float outSide = Mathf.Sign(lateral == 0f ? side : lateral);
                    desired = trackForward * along * 0.7f + trackRight * outSide * 16f;
                    steer = false;
                    upright = false;
                    if (grounded && !ditchKick) rb.AddTorque(Vector3.up * spinDir * 2.2f, ForceMode.Acceleration);
                    if (!ditchKick && Mathf.Abs(lateral) > RoadHalf - 0.8f)
                    {
                        ditchKick = true;
                        rb.AddForce(Vector3.up * 5.5f + trackRight * outSide * 3f, ForceMode.VelocityChange);
                        Vector3 rollAxis = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, Vector3.up).normalized;
                        float sideOf = Mathf.Sign(Vector3.Dot(Vector3.Cross(Vector3.up, rollAxis), trackRight * outSide));
                        rb.AddTorque(-rollAxis * sideOf * 2.8f, ForceMode.VelocityChange);
                    }
                    if (t > 3.6f || ditchKick && t > 1.2f && grounded && flat.magnitude < 4f) SetPhase(Phase.Lying);
                    break;
                case Phase.Lying:
                    steer = upright = false;
                    desired = flat;
                    // Перевернулся на дороге (зажали машины, лёг на бок до кромки) — не лежит поперёк трассы: исчезает в дыму.
                    if (t > 2.5f && Mathf.Abs(lateral) < RoadHalf + 0.5f && !gone) Signal(SignalGone, 0, pos);
                    // Не оставлять 12-тонный автобус поперёк дороги: выталкивается за обочину.
                    if (Mathf.Abs(lateral) < RoadHalf + 2f && t > 1f)
                        rb.AddForce(trackRight * Mathf.Sign(lateral == 0f ? side : lateral) * 7f, ForceMode.Acceleration);
                    break;
            }
            if (grounded && phase != Phase.Lying)
                rb.AddForce(Vector3.ClampMagnitude((desired - flat) * 3f, 26f), ForceMode.Acceleration);
            if (steer && grounded)
            {
                Vector3 bodyNose = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, Vector3.up).normalized;
                float err = Vector3.SignedAngle(bodyNose, nose, Vector3.up) * Mathf.Deg2Rad;
                float rate = Vector3.Dot(rb.angularVelocity, Vector3.up);
                rb.AddTorque(Vector3.up * Mathf.Clamp(err * 7f - rate * 2.8f, -9f, 9f), ForceMode.Acceleration);
            }
            if (upright)
            {
                Vector3 up = rb.rotation * Vector3.up;
                Vector3 w = rb.angularVelocity;
                Vector3 tilt = w - Vector3.up * Vector3.Dot(w, Vector3.up);
                rb.AddTorque(Vector3.Cross(up, Vector3.up) * 14f - tilt * 4f, ForceMode.Acceleration);
            }
            // Застрял (упёрся в стену или кучу машин) — сразу в кювет. Медленный дрифт «в ожидании» участника — не застрял:
            // считается, только если автобус хочет ехать, а не едет.
            if (phase == Phase.Drift || phase == Phase.Spin || phase == Phase.Entry)
            {
                Vector3 wantFlat = new Vector3(desired.x, 0f, desired.z);
                stuckFor = flat.magnitude < 3f && wantFlat.magnitude > 6f && t > 0.6f ? stuckFor + dt : 0f;
                if (stuckFor > 1.2f) SetPhase(Phase.Ditch);
            }
        }

        /// Скорость вдоль трассы — по участнику, ради которого автобус выехал: отстаёт — автобус притормаживает
        /// и «ждёт» его поперёк дороги, проскочил — догоняет. Так дрифт приходится на машины, а не на пустую дорогу.
        /// (Плавное «скорость участника − 0.3·отрыв» не годилось: участник догонял слишком медленно и проезжал позже дрифта.)
        void Pace(float dt)
        {
            var focus = FocusRacer();
            if (focus == null || focus.Controller == null) return;
            float length = Context.Track.Length;
            float gap = TrackPath.Wrap(trackP - focus.Progress) * length;
            if (gap > length * 0.5f) gap -= length;
            float speed = focus.Controller.Speed;
            // Отстаёт — автобус вдвое медленнее (участник нагоняет ~за 2 с; встал — автобус дрифтует почти на месте),
            // рядом — чуть медленнее (въезжает в автобус), проскочил (разогнался за время крика, подбросило вперёд) —
            // догоняет тем быстрее, чем дальше ушёл.
            float want = gap > 8f ? speed * 0.55f : gap > -6f ? speed * 0.75f : speed + 6f - gap * 0.15f;
            along = Mathf.MoveTowards(along, Mathf.Clamp(want, 2f, 44f), 14f * dt);
        }

        /// Где поперёк дороги участник, ради которого выехал автобус, если он в 3–35 м сзади (подъезжает) или впереди (автобус догоняет).
        bool AimLateral(out float lateral)
        {
            lateral = 0f;
            var focus = FocusRacer();
            if (focus == null || focus.Car == null) return false;
            float length = Context.Track.Length;
            float gap = TrackPath.Wrap(trackP - focus.Progress) * length;
            if (gap > length * 0.5f) gap -= length;
            if (Mathf.Abs(gap) < 3f || Mathf.Abs(gap) > 35f) return false;
            lateral = Mathf.Clamp(Vector3.Dot(focus.Car.position - trackAxis, trackRight), -RoadHalf, RoadHalf);
            return true;
        }

        /// Ближайшая точка трассы под автобусом — 10 раз в секунду (поиск недёшев).
        void RefreshTrack(Vector3 pos)
        {
            if (Time.time - trackAt < 0.1f) return;
            trackAt = Time.time;
            float p = trackP = Context.Track.Nearest(pos);
            trackForward = Context.Track.DirectionAt(p);
            trackRight = Context.Track.SideAt(p);
            trackAxis = Context.Track.PointAt(p);
        }

        void SetPhase(Phase next)
        {
            timeline.Append(" → ").Append(next).Append(' ').Append((Time.time - activeAt).ToString("F1"));
            phase = next;
            phaseAt = Time.time;
            stuckFor = 0f;
            if (next == Phase.Ditch && spinDir == 0f) spinDir = dir;
        }

        readonly RaycastHit[] groundHits = new RaycastHit[8];

        bool Grounded(Vector3 pos)
        {
            int n = Physics.RaycastNonAlloc(pos + Vector3.up * 0.6f, Vector3.down, groundHits, 1.5f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (groundHits[i].collider.attachedRigidbody != rb) return true;
            return false;
        }

        // ---------- удары ----------

        /// Столкновение автобуса (DriftBusContact на теле автобуса).
        public void OnBusContact(Collision collision, bool enter)
        {
            if (!IsRunning || !bus.Live) return;
            var car = collision.rigidbody != null ? collision.rigidbody.GetComponent<CarController>() : null;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : bus.transform.position;
            if (car != null)
            {
                if (enter) HitCarBy(car, collision.relativeVelocity.magnitude, point);
                return;
            }
            // Скольжение кузовом по земле или стене — искры; падение в кювет — грохот и ударная волна (решает хост).
            float speed = bus.Velocity.magnitude;
            if (!enter && speed > 4f && Time.time - sparkAt > 0.05f && Static(collision.collider))
            {
                sparkAt = Time.time;
                var p = new ParticleSystem.EmitParams { position = point + Vector3.up * 0.1f, applyShapeToPosition = true };
                sparks.Emit(p, 3);
            }
            if (enter && Authority && !crashed && (phase == Phase.Ditch || phase == Phase.Lying) && ditchKick
                && collision.relativeVelocity.magnitude > 5f && Static(collision.collider))
            {
                crashed = true;
                Signal(SignalCrash, 0, point);
            }
        }

        void HitCarBy(CarController car, float relative, Vector3 point)
        {
            // Удар по машине решает тот, кто её считает; повтор по той же машине — не чаще 0.6 с. В заносе автобус
            // отбрасывает и от касания (машина въехала в борт на почти равной скорости), в кювете — только от удара.
            // Фаза есть только у хоста; у клиента копия автобуса — порог как в заносе, пока он не упал (сигнал crashed).
            bool sliding = Authority ? phase == Phase.Entry || phase == Phase.Drift || phase == Phase.Spin : !crashed;
            if (!car.Physics.Simulated || relative < (sliding ? 2.5f : 4f)) return;
            if (lastHit.TryGetValue(car, out float at) && Time.time - at < 0.6f) return;
            lastHit[car] = Time.time;
            float s = Mathf.Clamp(relative, 10f, 34f);
            Vector3 away = car.transform.position - bus.transform.position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : car.transform.right;
            Vector3 busVel = bus.Velocity;
            busVel.y = 0f;
            Vector3 push = Vector3.ClampMagnitude(busVel * 0.7f + away * s * 0.55f, 30f);
            car.ApplyLaunch(push + Vector3.up * (7f + s * 0.45f), Random.onUnitSphere * s * 0.25f, "сбит автобусом EL DRIFTO");
            CarHits++;
            HitCar?.Invoke(this, car, relative);
            var fx = ChaosEffects;
            fx?.Sound(SoundBank.BusCrash, point, Mathf.Clamp01(0.5f + s / 40f), Random.Range(0.9f, 1.05f));
            fx?.Blast(point, BlastStyle.Spark, 1.2f);
            fx?.Pop(point + Vector3.up * 3f, CarHits % 2 == 1 ? "EL DRIFTO!" : "БАМ!", new Color(1f, 0.5f, 0.15f), 1.5f);
            // Удар отбрасывает и всё вокруг (мячи, бочки, мины) — цепочка.
            fx?.Shockwave(point, 7f, 9f, 1, car);
            ShakeCameraNear(point, 0.6f, 35f);
        }

        protected override void OnSignal(int code, int index, Vector3 value)
        {
            var fx = ChaosEffects;
            if (code == SignalCrash)
            {
                crashed = true;
                fx?.Sound(SoundBank.BigBoom, value, 1f, 0.8f);
                fx?.Sound(SoundBank.BusCrash, value, 1f, 0.75f);
                fx?.Blast(value, BlastStyle.Big, 1.8f, new Color(0.7f, 0.62f, 0.48f, 0.85f));
                fx?.Pop(value + Vector3.up * 5f, "БА-БАХ!", new Color(1f, 0.45f, 0.1f), 2f);
                fx?.Scorch(value, 7f, true);
                fx?.Shockwave(value, 14f, 15f, 1, null);
                ShakeCameraNear(value, 1f, 70f);
            }
            else if (code == SignalGone && !gone)
            {
                gone = true;
                Vanish();
            }
        }

        // ---------- вид: колёса, крен, дым, визг ----------

        void LateUpdate()
        {
            if (!bus.Live) return;
            float dt = Time.deltaTime;
            RefreshTrack(bus.transform.position);
            Vector3 vel = bus.Velocity;
            Vector3 local = bus.transform.InverseTransformDirection(vel);
            float slide = Mathf.Abs(local.x);
            bool down = bus.transform.up.y > 0.8f && Mathf.Abs(bus.transform.position.y - trackAxis.y) < 3f;
            // Колёса: вращение по продольной скорости, передние — в контрвыворот против заноса.
            float counter = Mathf.Clamp(-Mathf.Atan2(local.x, Mathf.Abs(local.z) + 1f) * Mathf.Rad2Deg * 0.8f, -35f, 35f);
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null) continue;
                wheelSpin[i] = Mathf.Repeat(wheelSpin[i] + local.z / WheelRadius * Mathf.Rad2Deg * dt, 360f);
                Quaternion steer = i < 2 ? Quaternion.AngleAxis(counter, Vector3.up) : Quaternion.identity;
                wheels[i].localRotation = steer * Quaternion.AngleAxis(wheelSpin[i], Vector3.right) * wheelRest[i];
            }
            // Кузов кренится в сторону скольжения и клюёт при торможении (мультяшно, только вид).
            float rollTarget = down ? -Mathf.Clamp(local.x * 0.6f, -9f, 9f) : 0f;
            float pitchTarget = down ? Mathf.Clamp(-local.z * 0.05f, -2f, 2f) : 0f;
            float k = 1f - Mathf.Exp(-6f * dt);
            leanRoll = Mathf.Lerp(leanRoll, rollTarget, k);
            leanPitch = Mathf.Lerp(leanPitch, pitchTarget, k);
            if (Stage != EventStage.Ending)
            {
                lean.localRotation = Quaternion.Euler(leanPitch, 0f, leanRoll);
                lean.localPosition = Vector3.up * (Mathf.Abs(Mathf.Sin(leanRoll * Mathf.Deg2Rad)) * HalfTrack
                                                   + Mathf.Abs(Mathf.Sin(leanPitch * Mathf.Deg2Rad)) * HalfBase);
            }
            // Дым из-под задних колёс и визг шин в дрифте.
            bool drifting = down && slide > 5f && vel.magnitude > 6f;
            Emit(smokeL, drifting ? slide : 0f, dt);
            Emit(smokeR, drifting ? slide : 0f, dt);
            float volume = drifting ? Mathf.Clamp01((slide - 4f) / 12f) * GameSettings.SfxVolume : 0f;
            screech.volume = Mathf.Lerp(screech.volume, volume, 1f - Mathf.Exp(-10f * dt));
            screech.pitch = 0.9f + Mathf.Clamp01(slide / 25f) * 0.25f;
            if (screech.volume > 0.01f && !screech.isPlaying) screech.Play();
            else if (screech.volume <= 0.01f && screech.isPlaying) screech.Stop();
            if (!down && vel.magnitude > 3f && dust != null && Time.frameCount % 4 == 0)
            {
                var p = new ParticleSystem.EmitParams { position = bus.transform.position, applyShapeToPosition = true };
                dust.Emit(p, 2);
            }
        }

        static void Emit(ParticleSystem ps, float slide, float dt)
        {
            if (ps == null || slide <= 0f) return;
            int count = Mathf.FloorToInt(slide * 2.4f * dt + Random.value);
            if (count > 0) ps.Emit(count);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            if (!bus.Live) return;
            Center = bus.transform.position;
            // Улетел с карты (мост, обрыв) — исчезает у всех.
            if (Authority && bus.Lost && !gone) Signal(SignalGone, 0, bus.transform.position);
        }

        protected override void OnEnd()
        {
            if (bus.Live && !gone) Vanish();
        }

        void Vanish()
        {
            Vector3 at = bus.transform.position;
            var fx = ChaosEffects;
            fx?.Blast(at, BlastStyle.Big, 1.6f, new Color(0.85f, 0.85f, 0.85f, 0.85f));
            fx?.Sound(SoundBank.Pshik, at, 0.9f, 0.8f);
            fx?.Pop(at + Vector3.up * 4f, "ПШ-Ш-Ш!", new Color(1f, 0.6f, 0.2f), 1.4f);
            screech.Stop();
            IgnoreProxies(false);
            if (bus.Live) bus.Despawn();
        }

        protected override void OnEndUpdate(float progress) { }

        protected override void Cleanup()
        {
            if (phase != Phase.None)
                Debug.Log($"[RandomCar] EL DRIFTO: розыгрыш {Serial} окончен — выход {(onRoad ? "с дороги" : "с обочины")}, сбито машин {CarHits}, подскоков на выезде {entryHops}, кювет {crashed}, последняя фаза {(Authority ? phase.ToString() : "у хоста")}"
                          + (Authority ? $"; фазы: {timeline}; на дороге {onRoadSeconds:F1} с; участник ближе всего {closestFocus:F0} м" : ""));
            screech.Stop();
            IgnoreProxies(false);
            if (bus.Live) bus.Despawn();
            lean.localRotation = Quaternion.identity;
            marker.Hide();
            phase = Phase.None;
        }

        /// ИИ автобус не объезжает (как трюки): шоу-событие должно сносить машины, а не разгонять их заранее.
        public override void CollectHazards(List<HazardInfo> list) { }

        public override void CollectBlocks(List<RoadBlock> list) { }

        /// Земля под точкой рядом с высотой refY (мосты и эстакады над дорогой не считаются).
        static Vector3 GroundNear(Vector3 p, float refY)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, refY + 6f, p.z), Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 result = new Vector3(p.x, refY - 100f, p.z);
            foreach (var hit in hits)
            {
                if (!Static(hit.collider)) continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    result = hit.point;
                }
            }
            return result;
        }
    }
}
