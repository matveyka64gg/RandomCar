using System;
using System.Collections.Generic;
using RandomCar.Audio;
using RandomCar.Car;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Events
{
    /// Вид физического объекта хаоса: масса, упругость, звук и характер движения.
    public enum BodyKind
    {
        /// Пляжный мяч: лёгкий, медленный, непредсказуемо виляет между полосами.
        Beach,
        /// Резиновый: высоко подпрыгивает от дороги и машин.
        Rubber,
        /// Стальной: тяжёлый, быстрый, катится по прямой и отскакивает от препятствий.
        Metal,
        /// Деревянная бочка (бывает взрывоопасной).
        Barrel,
        /// Кубик-кость: кувыркается.
        Dice,
        /// Шар-переросток.
        Giant,
        /// Разноцветный мячик из «роя».
        Mini,
        /// Гигантская резиновая утка (v5): прыгучая, крякает.
        Duck,
        /// Пушечное ядро (v5): тяжёлое и очень быстрое.
        Cannonball,
        /// Автобус EL DRIFTO (v6): 12 т, им управляет событие (дрифт, вылет в кювет). Без «бортика» и предела
        /// по краю дороги — должен улетать с трассы; удары по машинам и звук — у события.
        Bus,
    }

    /// Физический объект события: настоящий Rigidbody с массой, вращением, отскоками и столкновениями.
    /// Страховки: скорость и вращение ограничены, объект мягко удерживается у дороги (мультяшный «бортик»),
    /// улетевший с карты помечается потерянным (событие уберёт его). Звук удара и сплющивание — по виду и силе.
    /// В сети физику считает хост; у клиентов копия кинематическая и следует за присланным состоянием.
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ChaosBody : MonoBehaviour
    {
        /// Все объекты хаоса на трассе (бюджет физики, ударная волна, сетевая синхронизация).
        public static readonly List<ChaosBody> Active = new List<ChaosBody>();

        /// Объект ударил машину: объект, машина, сила (м/с). Для проверки динамики.
        public static event Action<ChaosBody, CarController, float> HitCar;

        /// Объект ударился о другой объект хаоса (шары сталкиваются и меняют направление).
        public static event Action<ChaosBody, ChaosBody, float> HitBody;

        [SerializeField] BodyKind kind;
        [SerializeField] float radius = 1.2f;
        [SerializeField] Transform visual;

        Rigidbody body;
        Collider shape;
        Vector3 visualScale = Vector3.one;
        float squash;
        float squashAt = -10f;
        float maxSpeed = 20f;
        float wobblePhase;
        float lastSound;
        TrackPath track;
        float roadHalf = 6f;
        float lateral;
        float axisY;
        int checkFrame;

        // Сетевая копия: последнее присланное состояние.
        Vector3 netPosition, netVelocity;
        Quaternion netRotation = Quaternion.identity;
        float netStamp;

        public BodyKind Kind => kind;
        public float Radius => radius * transform.localScale.x;
        public Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();

        /// Объект на трассе и участвует в игре.
        public bool Live { get; private set; }

        /// Сетевая копия (кинематическая): физику считает хост.
        public bool Remote { get; private set; }

        /// Объект улетел с карты или провалился: событие уберёт его.
        public bool Lost { get; private set; }

        /// Номер для сетевой синхронизации: серийный номер события × 32 + номер объекта в событии.
        public int NetId { get; set; } = -1;

        /// Сила «виляния» пляжного мяча, м/с²: 0 — катится ровно.
        public float Wobble { get; set; }

        /// Взрывоопасная бочка: ударная волна или сильный удар взрывают её (решает событие).
        public bool Explosive { get; set; }

        /// Катится вдоль дороги навстречу гонке с этой скоростью, м/с (0 — свободно): шар-переросток
        /// «едет» по трассе, а не улетает на первом повороте.
        public float Cruise { get; set; }

        /// Куда по ширине дороги клонится катящийся вдоль трассы шар, м.
        public float TargetLateral { get; set; }

        /// Скорость: у копии — присланная хостом.
        public Vector3 Velocity => Remote ? netVelocity : Body.linearVelocity;

        public float SpawnedAt { get; private set; }

        public void Configure(BodyKind bodyKind, float bodyRadius, Transform visualRoot)
        {
            kind = bodyKind;
            radius = bodyRadius;
            visual = visualRoot;
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            shape = GetComponent<Collider>();
            if (visual != null) visualScale = visual.localScale;
            ApplyKind();
        }

        void ApplyKind()
        {
            // Масса и характер: пляжный мяч отлетает от машины, стальной толкает её (с пределом в CarController).
            (float mass, float drag, float angular, float bounce, float friction, float speed) = kind switch
            {
                BodyKind.Beach => (45f, 0.25f, 0.4f, 0.82f, 0.4f, 17f),
                BodyKind.Rubber => (260f, 0.06f, 0.2f, 0.92f, 0.7f, 21f),
                BodyKind.Metal => (2600f, 0.02f, 0.05f, 0.3f, 0.15f, 25f),
                BodyKind.Barrel => (180f, 0.08f, 0.25f, 0.35f, 0.5f, 18f),
                BodyKind.Dice => (320f, 0.1f, 0.3f, 0.45f, 0.6f, 17f),
                BodyKind.Giant => (9000f, 0.03f, 0.05f, 0.2f, 0.4f, 15f),
                BodyKind.Duck => (140f, 0.1f, 0.3f, 0.88f, 0.5f, 24f),
                BodyKind.Cannonball => (1800f, 0.01f, 0.05f, 0.25f, 0.2f, 42f),
                BodyKind.Bus => (12000f, 0.02f, 0.4f, 0.12f, 0.12f, 46f),
                _ => (90f, 0.12f, 0.25f, 0.85f, 0.5f, 19f),
            };
            body.mass = mass;
            body.linearDamping = drag;
            body.angularDamping = angular;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.maxAngularVelocity = 18f;
            maxSpeed = speed;
            var material = Material(kind, bounce, friction);
            foreach (var c in GetComponents<Collider>()) c.sharedMaterial = material;
            if (kind == BodyKind.Bus)
            {
                // Низкий центр масс: в дрифте не опрокидывается сам — переворачивает его событие у кювета.
                body.centerOfMass = new Vector3(0f, 0.85f, 0f);
                body.maxAngularVelocity = 9f;
            }
        }

        /// Тело ведёт событие (автобус): нет «бортика», круиза и виляния, «потерян» — только очень далеко от трассы.
        public bool Free => kind == BodyKind.Bus;

        static readonly Dictionary<BodyKind, PhysicsMaterial> materials = new Dictionary<BodyKind, PhysicsMaterial>();

        static PhysicsMaterial Material(BodyKind kind, float bounce, float friction)
        {
            if (materials.TryGetValue(kind, out var m) && m != null) return m;
            m = new PhysicsMaterial("PM_Chaos_" + kind)
            {
                bounciness = bounce,
                dynamicFriction = friction,
                staticFriction = friction,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                frictionCombine = PhysicsMaterialCombine.Average,
            };
            materials[kind] = m;
            return m;
        }

        /// Выпустить объект на трассу. remote — сетевая копия (кинематическая, следует за хостом).
        public void Spawn(TrackPath course, float halfWidth, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity, bool remote)
        {
            track = course;
            roadHalf = halfWidth;
            Remote = remote;
            Lost = false;
            Live = true;
            SpawnedAt = Time.time;
            checkFrame = 9;
            wobblePhase = UnityEngine.Random.Range(0f, 100f);
            // Сначала место, потом включение, и явно — позу тела: у тела с интерполяцией после повторного включения
            // поза иногда оставалась прежней (автобус EL DRIFTO появлялся там, где лежал прошлый, в сотнях метров от трассы).
            transform.SetPositionAndRotation(position, rotation);
            gameObject.SetActive(true);
            Body.position = position;
            Body.rotation = rotation;
            Body.isKinematic = remote;
            if (!remote)
            {
                body.linearVelocity = velocity;
                body.angularVelocity = angularVelocity;
            }
            netPosition = position;
            netRotation = rotation;
            netVelocity = velocity;
            netStamp = Time.time;
            if (visual != null) visual.localScale = visualScale;
            if (!Active.Contains(this)) Active.Add(this);
        }

        public void Despawn()
        {
            Live = false;
            Lost = false;
            Cruise = 0f;
            Wobble = 0f;
            Active.Remove(this);
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            gameObject.SetActive(false);
        }

        void OnDisable() => Active.Remove(this);

        /// Толчок (ударная волна, взрыв): изменение скорости, м/с. У сетевой копии не действует — толкнёт хост.
        public void Kick(Vector3 velocityChange)
        {
            if (!Live || Remote) return;
            // 12-тонный автобус от взрыва только вздрагивает.
            if (Free) velocityChange *= 0.25f;
            body.AddForce(Vector3.ClampMagnitude(velocityChange, 22f), ForceMode.VelocityChange);
            body.AddTorque(UnityEngine.Random.insideUnitSphere * 6f, ForceMode.VelocityChange);
            Squash(0.5f);
        }

        /// Состояние от хоста (сеть).
        public void SetNetState(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            netPosition = position;
            netRotation = rotation;
            netVelocity = velocity;
            netStamp = Time.time;
        }

        void FixedUpdate()
        {
            if (!Live) return;
            if (Remote)
            {
                // Копия: прогноз по последнему состоянию (не дольше 0.25 с) и плавное подтягивание.
                float ahead = Mathf.Min(Time.time - netStamp, 0.25f);
                Vector3 target = netPosition + netVelocity * ahead;
                float k = 1f - Mathf.Exp(-14f * Time.fixedDeltaTime);
                body.MovePosition(Vector3.Distance(body.position, target) > 6f ? target : Vector3.Lerp(body.position, target, k));
                body.MoveRotation(Quaternion.Slerp(body.rotation, netRotation, k));
                return;
            }

            // Предел скорости: объект не разгоняется бесконечно (склон, толчок машины, цепочка взрывов).
            Vector3 v = body.linearVelocity;
            float speed = v.magnitude;
            if (speed > maxSpeed) body.linearVelocity = v * (maxSpeed / speed);

            if (track == null) return;
            if (Free)
            {
                // Тело события (автобус): только проверка «улетел с карты» — далеко вниз или в сторону.
                if (++checkFrame % 10 == 0)
                {
                    float fp = track.Nearest(transform.position);
                    Vector3 faxis = track.PointAt(fp);
                    float flat = Vector3.Dot(transform.position - faxis, track.SideAt(fp));
                    if (transform.position.y < faxis.y - 30f || Mathf.Abs(flat) > roadHalf + 90f) Lost = true;
                }
                return;
            }
            // Положение относительно дороги — 10 раз в секунду (поиск ближайшей точки трассы недёшев).
            if (++checkFrame % (Cruise > 0f ? 3 : 10) == 0)
            {
                float p = track.Nearest(transform.position);
                Vector3 axis = track.PointAt(p);
                lateral = Vector3.Dot(transform.position - axis, track.SideAt(p));
                axisY = axis.y;
                sideDirection = track.SideAt(p);
                forwardDirection = track.DirectionAt(p);
                if (transform.position.y < axisY - 14f || Mathf.Abs(lateral) > roadHalf + 32f) Lost = true;
            }
            // Мультяшный «бортик»: укатившийся за обочину объект мягко возвращается к дороге.
            float edge = roadHalf + 1.5f;
            if (Mathf.Abs(lateral) > edge)
                body.AddForce(-Mathf.Sign(lateral) * sideDirection * Mathf.Min(18f, (Mathf.Abs(lateral) - edge) * 6f), ForceMode.Acceleration);
            // Катится вдоль трассы навстречу: скорость подтягивается к Cruise по направлению дороги, плюс снос к своей полосе.
            if (Cruise > 0f)
            {
                Vector3 horizontal = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
                Vector3 desired = -forwardDirection * Cruise + sideDirection * Mathf.Clamp(TargetLateral - lateral, -3f, 3f) * 0.8f;
                body.AddForce(Vector3.ClampMagnitude((desired - horizontal) * 1.2f, 7f), ForceMode.Acceleration);
            }
            // Пляжный мяч виляет между полосами: плавный случайный боковой «ветерок».
            if (Wobble > 0f)
            {
                float w = Mathf.PerlinNoise(wobblePhase, Time.time * 0.6f) * 2f - 1f;
                body.AddForce(sideDirection * w * Wobble, ForceMode.Acceleration);
            }
        }

        Vector3 sideDirection = Vector3.right;
        Vector3 forwardDirection = Vector3.forward;

        void Update()
        {
            if (visual == null) return;
            // Сплющивание при ударе: преувеличенная мультяшная анимация (тянется вверх, затем сжимается).
            float t = Time.time - squashAt;
            float wave = t < 0.5f ? Mathf.Sin(t * 30f) * Mathf.Exp(-t * 7f) * squash : 0f;
            visual.localScale = new Vector3(visualScale.x * (1f + wave * 0.35f), visualScale.y * (1f - wave * 0.4f), visualScale.z * (1f + wave * 0.35f));
        }

        float cartoonAt = -10f;

        /// Мультяшный удар по машине (её считает этот компьютер): кроме обычного столкновения тел машину
        /// отбрасывает и закручивает по характеру объекта — от резинового шара машины разлетаются, ядро сносит.
        void CartoonHit(CarController car, float strength)
        {
            if (Free || strength < 4.5f || !car.Physics.Simulated || Time.time - cartoonAt < 0.4f) return;
            float punch = kind switch
            {
                BodyKind.Beach => 0.45f,
                BodyKind.Mini => 0.3f,
                BodyKind.Rubber => 1.1f,
                BodyKind.Duck => 0.9f,
                BodyKind.Metal => 1.15f,
                BodyKind.Cannonball => 1.5f,
                BodyKind.Giant => 1.6f,
                BodyKind.Dice => 0.75f,
                _ => 0.7f,
            };
            cartoonAt = Time.time;
            Vector3 away = car.transform.position - transform.position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : car.transform.right;
            float s = Mathf.Min(strength, 30f) * punch;
            car.ApplyLaunch(away * Mathf.Min(s * 0.95f, 32f) + Vector3.up * Mathf.Min(5f + s * 0.55f, 22f),
                            UnityEngine.Random.onUnitSphere * Mathf.Min(s * 0.3f, 9f), "удар: " + KindName);
        }

        string KindName => kind switch
        {
            BodyKind.Beach => "мяч",
            BodyKind.Rubber => "резиновый шар",
            BodyKind.Metal => "стальной шар",
            BodyKind.Barrel => "бочка",
            BodyKind.Dice => "кость",
            BodyKind.Giant => "шар-переросток",
            BodyKind.Duck => "утка",
            BodyKind.Cannonball => "ядро",
            BodyKind.Bus => "автобус",
            _ => "мячик",
        };

        void Squash(float amount)
        {
            squash = Mathf.Clamp01(Mathf.Max(amount, Time.time - squashAt < 0.2f ? squash : 0f));
            squashAt = Time.time;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!Live) return;
            float strength = collision.relativeVelocity.magnitude;
            if (strength < 1.2f) return;
            Squash(Mathf.Clamp01(strength / 12f));
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;

            var car = collision.rigidbody != null ? collision.rigidbody.GetComponent<CarController>() : null;
            var other = collision.rigidbody != null ? collision.rigidbody.GetComponent<ChaosBody>() : null;
            if (car != null)
            {
                HitCar?.Invoke(this, car, strength);
                CartoonHit(car, strength);
            }
            if (other != null) HitBody?.Invoke(this, other, strength);

            // Звук и надписи автобуса — у его события (ElDriftoEvent).
            if (Free || Time.time - lastSound < 0.12f || ChaosFx.Instance == null) return;
            lastSound = Time.time;
            var fx = ChaosFx.Instance;
            float volume = Mathf.Clamp01(strength / 14f);
            switch (kind)
            {
                case BodyKind.Beach: fx.Sound(SoundBank.Bonk, point, 0.3f + volume * 0.6f, UnityEngine.Random.Range(0.9f, 1.2f)); break;
                case BodyKind.Rubber: fx.Sound(SoundBank.Boing, point, 0.3f + volume * 0.6f, UnityEngine.Random.Range(0.85f, 1.15f)); break;
                case BodyKind.Mini: fx.Sound(SoundBank.Squeak, point, 0.25f + volume * 0.5f, UnityEngine.Random.Range(0.8f, 1.3f)); break;
                case BodyKind.Metal:
                case BodyKind.Giant: fx.Sound(SoundBank.Gong, point, 0.35f + volume * 0.65f, kind == BodyKind.Giant ? 0.6f : UnityEngine.Random.Range(0.9f, 1.1f)); break;
                case BodyKind.Duck: fx.Sound(SoundBank.Quack, point, 0.4f + volume * 0.6f, UnityEngine.Random.Range(0.8f, 1.2f)); break;
                case BodyKind.Cannonball: fx.Sound(SoundBank.Gong, point, 0.5f + volume * 0.5f, 0.75f); break;
                default: fx.Sound(SoundBank.Knock, point, 0.3f + volume * 0.6f, UnityEngine.Random.Range(0.85f, 1.15f)); break;
            }
            if (car == null || strength < 6f) return;
            // Удар о машину — надпись и искры: смешно и понятно, что случилось.
            string word = kind switch
            {
                BodyKind.Beach => "БОНК!",
                BodyKind.Rubber => "БОЙНЬ!",
                BodyKind.Mini => "ПИСК!",
                BodyKind.Metal => "ДЗЫНЬ!",
                BodyKind.Giant => "БУМ!",
                BodyKind.Duck => "КРЯ!",
                BodyKind.Cannonball => "БДЫЩ!",
                _ => "БАЦ!",
            };
            fx.Pop(point + Vector3.up * 1.5f, word, new Color(1f, 0.85f, 0.25f), 1f);
            fx.Blast(point, BlastStyle.Spark, 0.6f);
        }
    }
}
