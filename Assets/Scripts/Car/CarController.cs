using RandomCar.Events;
using UnityEngine;

namespace RandomCar.Car
{
    /// Фасад машины для гонки, событий, камеры и интерфейса. Физику считает CarPhysics; здесь — управление,
    /// блокировка, сброс на позицию, эффекты событий (только через Apply*, с пределами текущей силы хаоса
    /// ChaosPower), удары и признак «машина застряла» для возвращения на трассу.
    /// Хаос v5: мощные подбросы с кувырками во все стороны, «бильярд» (летящая машина сбивает другие),
    /// отскок при жёстком приземлении, мультяшный переворот на колёса, нелепое управление (икота, чих, балерина).
    [RequireComponent(typeof(CarPhysics))]
    public sealed class CarController : MonoBehaviour
    {
        const float RecoverUpsideDownAfter = 3.2f;
        const float RecoverStuckAfter = 4f;
        const float FlipAfter = 0.45f;

        CarPhysics physics;
        float upsideDownFor;
        float stuckFor;
        float flipAt = -10f;
        float bounceAt = -10f;
        float billiardAt = -10f;
        float gagStepAt;

        public CarPhysics Physics => physics != null ? physics : physics = GetComponent<CarPhysics>();
        public CarSpec Spec => Physics.Spec;

        /// Машина тормозит и не слушает водителя (мотор можно погазовать).
        public bool Locked
        {
            get => Physics.Locked;
            set => Physics.Locked = value;
        }

        /// Продольная скорость, м/с (минус — назад).
        public float Speed => Physics.ForwardSpeed;

        /// Фактическая скорость тела, км/ч (для спидометра).
        public float SpeedKmh => Physics.SpeedAbs * 3.6f;

        /// Ограничитель скорости этой машины, м/с.
        public float TopSpeed => Spec.TopSpeed;

        /// Реально применённый руль -1..1 (доля максимального угла колёс).
        public float AppliedSteer => Physics.SteerAngle / Mathf.Max(1f, Spec.MaxSteerAngle);

        /// Продольное ускорение, м/с².
        public float Acceleration => Physics.LongitudinalAcceleration;

        public bool IsGrounded => Physics.IsGrounded;
        public bool IsBoosted => Physics.Effects.IsBoosted;
        public bool IsSlowed => Physics.Effects.IsSlowed;
        public bool IsSlipping => Physics.Effects.IsSlipping;

        /// Машина кувыркается после удара (камера держит курс по скорости, а не по кузову).
        public bool Tumbling => Physics.Effects.Tumbling;

        public Drivetrain Drivetrain => Physics.Drivetrain;
        public Turbo Turbo => Physics.Turbo;

        /// Последнее управление водителя.
        public CarInput Input => Physics.Input;

        /// Толчок, удар или посадка после прыжка: сила примерно в м/с. Для пыли, звука и тряски камеры.
        public event System.Action<float> Bumped;

        /// Удар о препятствие или машину: точка и сила (м/с). Для искр и звука.
        public event System.Action<Vector3, float> Impact;

        /// Машину подбросило или отбросило событием: машина, скорость отброса (м/с), звено цепной реакции.
        public static event System.Action<CarController, float, int> Launched;

        /// Летящая машина сбила другую («бильярд»): кто, кого, сила.
        public static event System.Action<CarController, CarController, float> Billiard;

        /// Жёсткое приземление после подброса: машина, сила, звено цепной реакции.
        public static event System.Action<CarController, float, int> HardLanding;

        /// Сколько раз на машину подействовали события (отладка и проверки честности).
        public int EffectsReceived { get; private set; }

        /// Сколько раз машину подбрасывало или отбрасывало событие (батут, мина, молот, перчатка, карусель).
        public int Launches { get; private set; }

        /// Самый сильный подброс за гонку (м/с вверх) и самая большая высота над точкой подброса, м.
        public float MaxLaunchUp { get; private set; }
        public float MaxAirHeight { get; private set; }

        /// Что сделало с машиной последнее событие (для ленты HUD): «толчок», «рывок», «замедление», «занос».
        public string LastEffect { get; private set; } = "";

        /// Звено цепной реакции последнего удара (0 — удар самого события, 1 — от взрыва, вызванного им, …).
        public int ChainGeneration { get; private set; }

        /// Когда машину последний раз подбросило событием (Time.time).
        public float LaunchedAt { get; private set; } = -100f;

        /// Трасса для «воздушного поводка»: после подброса события машина в воздухе мягко доворачивает вдоль
        /// дороги (сила — ChaosPower.Guide: в «безумии» поводка нет).
        public Track.TrackPath Guide { get; set; }

        float guideUntil;
        float launchY;
        float settleUntil;

        static ChaosPower Power => ChaosPower.Current;

        /// Машина лежит на крыше или застряла: гонка вернёт её на трассу.
        public bool NeedsRecovery => upsideDownFor > RecoverUpsideDownAfter || stuckFor > RecoverStuckAfter;

        /// Сколько секунд машина подряд едет по земле (после полёта — с момента приземления).
        public float GroundedFor { get; private set; }

        // ---------- защита: после возврата на трассу (2 с) и бонус «Щит» ----------

        /// Защита после возврата на трассу, с: ни одно опасное воздействие (подброс, толчок, сила трюка,
        /// замедление, удар тела события, взрыв, ракета, пулемёт) не действует. Управление, газ и бонусы — как обычно.
        public const float RespawnProtection = 2f;

        float protectedUntil = -10f;
        float shieldUntil = -10f;
        float protectedFor = 1f, shieldFor = 1f;

        /// Защита у чужой (сетевой) машины — из её состояния (флаги CarState), только для показа.
        public bool RemoteProtected { get; set; }
        public bool RemoteShield { get; set; }

        /// Машина сейчас защищена (после возврата или щитом).
        public bool Invulnerable => Time.time < protectedUntil || Time.time < shieldUntil || RemoteProtected || RemoteShield;

        /// Защита после возврата: сколько осталось (0..1 доля), щит — сколько осталось.
        public float ProtectionLeft => Mathf.Max(0f, protectedUntil - Time.time);
        public float ShieldLeft => Mathf.Max(0f, shieldUntil - Time.time);
        public float ProtectionFraction => Mathf.Clamp01(ProtectionLeft / protectedFor);
        public float ShieldFraction => Mathf.Clamp01(ShieldLeft / shieldFor);
        public bool Protected => Time.time < protectedUntil || RemoteProtected;
        public bool Shielded => Time.time < shieldUntil || RemoteShield;

        /// Сколько опасных воздействий поглотила защита (проверка).
        public int Absorbed { get; private set; }

        /// Защита поглотила воздействие (машина, что именно): для звука, надписи и проверки.
        public static event System.Action<CarController, string> Blocked;

        /// Защита после возврата на трассу (вызывает гонка, когда машина снова может ехать).
        public void Protect(float seconds)
        {
            protectedFor = Mathf.Max(0.1f, seconds);
            protectedUntil = Time.time + seconds;
        }

        /// Бонус «Щит».
        public void RaiseShield(float seconds)
        {
            shieldFor = Mathf.Max(0.1f, seconds);
            shieldUntil = Mathf.Max(shieldUntil, Time.time + seconds);
        }

        /// Полигон испытаний, «неуязвимость»: события не накладывают на машину последствий — замедления, нелепого руля,
        /// сплющивания (системы повреждений в игре нет). Физика работает полностью: толчки, подбросы, кувырки, удары
        /// тел и конструкций, сила тяжести, скользкость покрытий.
        public bool IgnoreConsequences { get; set; }

        /// Сколько последствий не наложено из-за IgnoreConsequences (проверка и надпись на полигоне).
        public int IgnoredConsequences { get; private set; }

        /// Последнее не наложенное последствие (для надписи на полигоне).
        public string LastIgnored { get; private set; } = "";

        bool Ignores(string what)
        {
            if (!IgnoreConsequences) return false;
            IgnoredConsequences++;
            LastIgnored = what;
            return true;
        }

        /// Опасное воздействие поглощено защитой.
        bool Blocks(string what)
        {
            if (!Invulnerable) return false;
            Absorbed++;
            Blocked?.Invoke(this, what);
            return true;
        }

        /// Мультяшный «потолок»: выше этого над точкой подброса машину тянет вниз сильнее (полёт не бесконечный), м.
        const float Ceiling = 48f;

        /// Сила тяжести в полёте после удара в «безумии» (доля обычной): быстрые, хлёсткие мультяшные полёты.
        const float FlightGravity = 1.6f;

        /// Летит после удара события и может сбить другую машину.
        public bool Charged => Time.time - LaunchedAt < 3.5f && Physics.SpeedAbs > 9f;

        /// Габариты кузова в координатах машины (объединение коллайдеров кузова): крыша для камеры из салона,
        /// место пулемёта, размер пузыря защиты. Кузов составной (низ + кабина), поэтому один BoxCollider — не габарит.
        public Bounds BodyBounds
        {
            get
            {
                if (!bodyBoundsReady) bodyBounds = MeasureBody(transform, out bodyBoundsReady);
                return bodyBounds;
            }
        }

        Bounds bodyBounds;
        bool bodyBoundsReady;

        static Bounds MeasureBody(Transform root, out bool found)
        {
            var b = new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(1.9f, 1.2f, 4.4f));
            found = false;
            foreach (var c in root.GetComponents<BoxCollider>())
            {
                if (c.isTrigger) continue;
                var local = new Bounds(c.center, c.size);
                if (!found) b = local; else b.Encapsulate(local);
                found = true;
            }
            return b;
        }

        /// Множитель силы удара события по этой машине: тяжёлую (KnockResistance > 1) отбрасывает слабее, лёгкую — сильнее.
        /// Пределы текущего Хаоса применяются после него — правила «умеренно» не нарушаются.
        float Knock => 1f / Mathf.Clamp(Spec.KnockResistance, 0.5f, 2f);

        void Awake()
        {
            physics = GetComponent<CarPhysics>();
            physics.Landed += OnLanded;
        }

        public void SetInput(CarInput input) => Physics.Input = Physics.Effects.Filter(input);

        /// Ставит машину на позицию без физики: для старта, рестарта и возвращения на трассу. Снимает все эффекты.
        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            protectedUntil = shieldUntil = -10f;
            Physics.ResetTo(position, rotation);
            Physics.Body.maxAngularVelocity = 8f;
            upsideDownFor = stuckFor = 0f;
            LaunchedAt = -100f;
            ChainGeneration = 0;
        }

        // ---------- эффекты событий ----------

        /// Замедление: скорость падает не ниже factor от текущей, не дольше 1.5 с.
        public void ApplySlow(float factor, float seconds)
        {
            if (Ignores("замедление") || Blocks("замедление")) return;
            // Зоны (воронка танка) обновляют замедление каждый кадр: считается только начало.
            if (!IsSlowed)
            {
                EffectsReceived++;
                LastEffect = "замедление";
            }
            Physics.Effects.Slow(factor, seconds, Physics.SpeedAbs);
        }

        /// Рывок: ровно 2 с, плюс короткий толчок вперёд.
        public void ApplyBoost(float factor)
        {
            EffectsReceived++;
            LastEffect = factor > FairnessLimits.MaxBoostFactor + 0.01f ? "СУПЕР-рывок" : "рывок +25%";
            Physics.Effects.Boost(factor);
            if (Locked || !Physics.Simulated) return;
            float room = TopSpeed * Physics.Effects.BoostFactor - Speed;
            if (room > 0f) Physics.Body.AddForce(transform.forward * Mathf.Min(3f * Power.ImpulseScale, room), ForceMode.VelocityChange);
        }

        /// Турбо (бонус): мощный разгон на seconds — тяга сильнее обычного рывка, плюс толчок вперёд.
        public void ApplyTurbo(float seconds, float factor = 1.6f)
        {
            LastEffect = "ТУРБО";
            Physics.Effects.Turbo(factor, seconds);
            if (Locked || !Physics.Simulated) return;
            Physics.Body.AddForce(transform.forward * 7f, ForceMode.VelocityChange);
            Bumped?.Invoke(6f);
        }

        /// Суперпрыжок (бонус): резкий подброс вверх без кувырка (в воздухе машина выравнивается как обычно).
        /// Своё действие игрока — защита его не гасит.
        public void ApplyJump(float up)
        {
            LastEffect = "суперпрыжок";
            if (Locked || !Physics.Simulated) return;
            var body = Physics.Body;
            Vector3 v = body.linearVelocity;
            body.linearVelocity = new Vector3(v.x, Mathf.Max(v.y, up), v.z) + transform.forward * 2f;
            Bumped?.Invoke(up);
        }

        /// Толчок без урона (старые события: мяч, ветер, столбы): сила растёт с Хаосом (ChaosPower.ImpulseScale).
        public void ApplyImpulse(Vector3 velocity)
        {
            if (Blocks("толчок")) return;
            var power = Power;
            velocity *= power.ImpulseScale * Knock;
            Vector3 horizontal = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(velocity, Vector3.up), power.MaxSide);
            float up = Mathf.Clamp(velocity.y, 0f, power.MaxUp);
            EffectsReceived++;
            LastEffect = "толчок";
            if (Physics.Simulated)
            {
                Physics.Body.AddForce(horizontal + Vector3.up * up, ForceMode.VelocityChange);
                if (power.Wild && horizontal.magnitude + up > 12f) MarkLaunch(horizontal.magnitude + up, 0, 0.8f);
            }
            Bumped?.Invoke(horizontal.magnitude + up);
        }

        /// Подброс v4 (поворот только по рысканию) — для совместимости.
        public void ApplyLaunch(Vector3 velocity, float yawSpin = 0f, string effect = "подброс") =>
            ApplyLaunch(velocity, Vector3.up * yawSpin, effect);

        /// Мощный мультяшный подброс: скорость «для безумия» (масштабируется силой хаоса и обрезается её пределами),
        /// закручивание — вектор угловой скорости в мире, рад/с (кувырок, бочка, волчок). Вертикальная скорость
        /// заменяется (не складывается при падении с батута на батут); на время кувырка машину не выравнивает.
        /// generation — звено цепной реакции (0 — удар самого события).
        public void ApplyLaunch(Vector3 velocity, Vector3 spin, string effect, int generation = 0)
        {
            if (Blocks(effect)) return;
            var power = Power;
            // Тяжёлую машину подбрасывает ниже и крутит меньше, лёгкую — выше (в пределах силы текущего Хаоса).
            float knock = Knock;
            Vector3 v = power.Scale(velocity * knock);
            Vector3 horizontal = Vector3.ProjectOnPlane(v, Vector3.up);
            // В невесомости подброс слабее: высота как при обычной силе тяжести, зато дольше висит в воздухе.
            // В «безумии» полёт после удара — с мультяшной тяжестью (FlightGravity): скорость подброса больше,
            // высота та же, а падение быстрее и хлёстче — меньше «висения» в воздухе, больше гонки.
            float gravity = Physics.Effects.GravityScale;
            float up = gravity < 0.95f ? v.y * Mathf.Sqrt(Mathf.Clamp(gravity, 0.15f, 1f)) : v.y;
            if (power.Wild && gravity >= 0.95f) up *= Mathf.Sqrt(FlightGravity);
            EffectsReceived++;
            Launches++;
            LastEffect = effect;
            if (Physics.Simulated)
            {
                var body = Physics.Body;
                Vector3 current = body.linearVelocity;
                float rise = Mathf.Max(current.y, 0f);
                Vector3 result = new Vector3(current.x, rise, current.z) + horizontal + Vector3.up * Mathf.Max(0f, up - rise);
                if (power.Wild) result = KeepRaceFlow(result, current, Mathf.Max(up, rise));
                body.linearVelocity = result;
                Vector3 w = power.ScaleSpin(spin * knock);
                if (w.sqrMagnitude > 1e-4f)
                {
                    body.maxAngularVelocity = Mathf.Max(8f, power.MaxSpin + 4f);
                    body.AddTorque(w, ForceMode.VelocityChange);
                }
                guideUntil = Time.time + 4f;
                MaxLaunchUp = Mathf.Max(MaxLaunchUp, up);
                float strength = horizontal.magnitude + up;
                // Кувырок тем дольше, чем сильнее закрутило: в «безумии» — до двух секунд без выравнивания.
                float tumble = power.TumbleSeconds * Mathf.Clamp01(Mathf.Max(w.magnitude / 6f, strength / 30f));
                MarkLaunch(strength, generation, tumble);
            }
            Bumped?.Invoke(horizontal.magnitude + up);
        }

        /// Мультяшный полёт «по течению гонки» («безумие»): машину может закрутить, подбросить на десятки метров
        /// и швырнуть вбок, но она продолжает лететь вперёд по трассе (не назад к старту) и обычно приземляется
        /// у дороги — полёт не отнимает гонку. Примерно каждый пятый удар «без тормозов»: улетает куда угодно.
        Vector3 KeepRaceFlow(Vector3 velocity, Vector3 before, float up)
        {
            if (Guide == null) return velocity;
            float p = Guide.Nearest(transform.position);
            Vector3 dir = Guide.DirectionAt(p);
            Vector3 side = Guide.SideAt(p);
            // Вдоль трассы — не медленнее, чем ехала (и не меньше 14 м/с): удар спереди не отбрасывает к старту.
            float along = Vector3.Dot(velocity, dir);
            float carry = Mathf.Max(20f, Vector3.Dot(before, dir) * 0.95f);
            if (along < carry) velocity += dir * (carry - along);
            if (Random.value < 0.2f) return velocity;
            // Поперёк — так, чтобы приземлиться не дальше ~18 м за краем дороги.
            float flight = Mathf.Max(1.2f, 2f * up / (9.81f * FlightGravity));
            float lateral = Vector3.Dot(transform.position - Guide.PointAt(p), side);
            float across = Vector3.Dot(velocity, side);
            float room = Guide.HalfWidth + 18f - lateral * Mathf.Sign(across);
            float limit = Mathf.Max(4f, room / flight);
            if (Mathf.Abs(across) > limit) velocity -= side * (across - Mathf.Sign(across) * limit);
            return velocity;
        }

        void MarkLaunch(float strength, int generation, float tumble)
        {
            LaunchedAt = Time.time;
            launchY = transform.position.y;
            ChainGeneration = generation;
            Physics.Effects.Tumble(tumble);
            Launched?.Invoke(this, strength, generation);
        }

        /// Непрерывная сила трюка (ветер вентилятора, магнит, смерч, луч НЛО): ускорение, м/с², за кадр dt.
        /// Сила масштабируется Хаосом; машина в такой силе считается «в полёте» (кувырок, цепочки).
        public void ApplyPush(Vector3 acceleration, float dt, string effect)
        {
            if (!Physics.Simulated) return;
            if (Invulnerable)
            {
                // Сила действует каждый кадр — поглощение считается не чаще раза в полсекунды.
                if (Time.time - pushAt > 0.5f && Blocks(effect)) pushAt = Time.time;
                return;
            }
            // Сила действует каждый кадр, а воздействием считается раз в полсекунды.
            if (Time.time - pushAt > 0.5f)
            {
                EffectsReceived++;
                LastEffect = effect;
                pushAt = Time.time;
            }
            Physics.Body.AddForce(acceleration * (Power.Punch * Knock * dt), ForceMode.VelocityChange);
        }

        float pushAt = -10f;

        /// Невесомость, пока машина в зоне (зона вызывает каждый кадр).
        public void ApplyLowGravity(float scale) => Physics.Effects.LowGravity(scale);

        /// «Гравитационный сбой» на время: scale < 0 — машину тянет вверх (в «умеренно» — только невесомость).
        public void ApplyGravity(float scale, float seconds)
        {
            if (Invulnerable) return;
            // Зоны обновляют эффект каждый кадр: считается только начало.
            if (Physics.Effects.GravityScale >= 1f)
            {
                EffectsReceived++;
                LastEffect = scale < 0f ? "вверх тормашками" : "невесомость";
            }
            Physics.Effects.Gravity(scale, seconds);
            if (scale < 0f && Physics.Simulated) Physics.Effects.Tumble(seconds + 0.5f);
        }

        /// Ворота-уменьшатель: машина выглядит меньше на несколько секунд. Только визуал — физика та же.
        public void ApplyVisualScale(float scale, float seconds)
        {
            EffectsReceived++;
            LastEffect = scale < 1f ? "мини-машинка" : "великан";
            Physics.Effects.ScaleVisual(scale, seconds);
        }

        /// Сплющило (наковальня, молот) или вытянуло (жвачка): только вид, на несколько секунд.
        public void ApplySquash(Vector3 scale, float seconds, string effect)
        {
            if (Ignores(effect) || Blocks(effect)) return;
            EffectsReceived++;
            LastEffect = effect;
            Physics.Effects.ScaleVisual(scale, seconds);
        }

        /// Скользко: сцепление падает, занос получается из физики шин. Не дольше 1.5 с. grip — доля сцепления.
        public void ApplySlip(float seconds, float grip = 0.5f)
        {
            if (Invulnerable) return;
            // Зоны обновляют занос каждый кадр: считается только начало.
            if (!IsSlipping)
            {
                EffectsReceived++;
                LastEffect = grip < 0.3f ? "масло" : "занос";
            }
            Physics.Effects.Slip(seconds, Power.Wild ? grip : Mathf.Max(grip, 0.5f));
        }

        /// Нелепое управление на время: руль наоборот, икота, чих, балерина, «пьяный» руль.
        public void ApplyControlGag(ControlGag gag, float seconds)
        {
            if (Ignores("нелепый руль") || Blocks("нелепый руль")) return;
            EffectsReceived++;
            LastEffect = gag switch
            {
                ControlGag.Inverted => "руль наоборот",
                ControlGag.Hiccups => "икота",
                ControlGag.Sneeze => "чих",
                ControlGag.Ballet => "балерина",
                _ => "пьяный руль",
            };
            Physics.Effects.SetControl(gag, seconds);
            gagStepAt = Time.time + 0.25f;
        }

        void FixedUpdate()
        {
            if (!Physics.Simulated) return;
            UpdateControlGag();
            // Слишком высоко — притяжение вдвое сильнее: подброс на десятки метров, но не в стратосферу.
            if (!Physics.IsGrounded && Time.time - LaunchedAt < 12f && transform.position.y - launchY > Ceiling && Physics.Body.linearVelocity.y > -20f)
                Physics.Body.AddForce(Vector3.down * 12f, ForceMode.Acceleration);
            // После жёсткого приземления: в отскоке кузов доворачивает по трассе и встаёт на колёса.
            if (Time.time < settleUntil && Guide != null && !Tumbling)
            {
                var b = Physics.Body;
                Vector3 course = Guide.DirectionAt(Guide.Nearest(transform.position));
                float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(transform.forward, Vector3.up), course, Vector3.up);
                b.AddTorque(Vector3.up * (Mathf.Clamp(yaw * 0.12f, -5f, 5f) - Vector3.Dot(b.angularVelocity, Vector3.up) * 0.8f), ForceMode.Acceleration);
                Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
                b.AddTorque(axis * 8f, ForceMode.Acceleration);
            }
            // Мультяшная тяжесть полёта после удара (не в невесомости, не в луче НЛО и не под магнитом).
            if (Power.Wild && !Physics.IsGrounded && Time.time - LaunchedAt < 8f && Physics.Effects.GravityScale >= 0.95f && Time.time - pushAt > 0.25f)
                Physics.Body.AddForce(Vector3.down * 9.81f * (FlightGravity - 1f), ForceMode.Acceleration);
            float guide = Power.Guide;
            if (guide <= 0f || Guide == null || Time.time > guideUntil || Physics.IsGrounded) return;
            // Воздушный поводок («умеренно», «много»): в воздухе скорость плавно разворачивается вдоль трассы,
            // у края дороги машину мягко возвращает к середине, кузов доворачивается по ходу.
            var body = Physics.Body;
            float p = Guide.Nearest(transform.position);
            Vector3 dir = Guide.DirectionAt(p);
            Vector3 side = Guide.SideAt(p);
            float lateral = Vector3.Dot(transform.position - Guide.PointAt(p), side);
            Vector3 v = body.linearVelocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            float speed = flat.magnitude;
            if (speed > 3f)
            {
                float sign = Vector3.Dot(flat, dir) >= 0f ? 1f : -1f;
                Vector3 want = dir * sign * speed;
                body.AddForce(Vector3.ClampMagnitude((want - flat) * 1.6f, 9f) * guide, ForceMode.Acceleration);
                if (!Tumbling)
                {
                    float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(transform.forward, Vector3.up), dir * sign, Vector3.up);
                    body.AddTorque(Vector3.up * Mathf.Clamp(yaw * 0.08f, -3f, 3f) * guide, ForceMode.Acceleration);
                }
            }
            float edge = Guide.HalfWidth - 1.6f;
            if (Mathf.Abs(lateral) > edge)
                body.AddForce(-Mathf.Sign(lateral) * side * Mathf.Min(12f, (Mathf.Abs(lateral) - edge) * 5f) * guide, ForceMode.Acceleration);
        }

        /// Икота (машина подпрыгивает), чих (рывки вперёд), балерина (крутится на месте).
        void UpdateControlGag()
        {
            var effects = Physics.Effects;
            if (!effects.ControlActive || Locked || Time.time < gagStepAt) return;
            var body = Physics.Body;
            switch (effects.Control)
            {
                case ControlGag.Hiccups:
                    gagStepAt = Time.time + 0.55f;
                    if (!Physics.IsGrounded) return;
                    body.AddForce(Vector3.up * 6.5f * Power.Punch + transform.forward * 1.5f, ForceMode.VelocityChange);
                    body.AddTorque(transform.right * -2.5f, ForceMode.VelocityChange);
                    ChaosFx.Instance?.Pop(transform.position + Vector3.up * 2.4f, "ИК!", new Color(0.7f, 1f, 0.5f), 0.9f);
                    ChaosFx.Instance?.Sound(Audio.SoundBank.Hiccup, transform.position, 0.8f, Random.Range(0.9f, 1.2f));
                    break;
                case ControlGag.Sneeze:
                    gagStepAt = Time.time + 0.95f;
                    body.AddForce(transform.forward * 12f * Power.Punch + Vector3.up * 2f, ForceMode.VelocityChange);
                    body.AddTorque(transform.up * Random.Range(-2.5f, 2.5f), ForceMode.VelocityChange);
                    ChaosFx.Instance?.Pop(transform.position + Vector3.up * 2.4f, "АПЧХИ!", new Color(1f, 0.9f, 0.4f), 1.1f);
                    ChaosFx.Instance?.Blast(transform.position - transform.forward * 2.2f, BlastStyle.ColorPuff, 0.7f, new Color(0.85f, 1f, 0.6f, 0.7f));
                    ChaosFx.Instance?.Sound(Audio.SoundBank.Sneeze, transform.position, 0.9f, Random.Range(0.9f, 1.1f));
                    break;
                case ControlGag.Ballet:
                    gagStepAt = Time.time + 0.1f;
                    body.AddTorque(Vector3.up * 2.2f, ForceMode.VelocityChange);
                    break;
                default:
                    gagStepAt = Time.time + 1f;
                    break;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool upside = Physics.IsUpsideDown;
            // На крыше в воздухе (кувырок) — не считается: перевернуться можно и при приземлении.
            upsideDownFor = upside && (Physics.IsGrounded || Physics.SpeedAbs < 4f) ? upsideDownFor + dt : upside ? upsideDownFor : 0f;
            // Стоит во время гонки (упёрся, застрял вне дороги) — гонка вернёт на трассу.
            stuckFor = !Locked && Physics.SpeedAbs < 0.8f ? stuckFor + dt : 0f;
            GroundedFor = Physics.IsGrounded ? GroundedFor + dt : 0f;
            if (Physics.SpeedAbs > 0.8f || upside) MaxAirHeight = Mathf.Max(MaxAirHeight, Time.time - LaunchedAt < 8f ? transform.position.y - launchY : 0f);

            // Мультяшный переворот: лёг на крышу — подпрыгивает и встаёт на колёса (в хаосе «много» и выше).
            if (Power.LandingBounce && upside && upsideDownFor > FlipAfter && Time.time - flipAt > 1.4f && Physics.Simulated && !Locked)
            {
                flipAt = Time.time;
                var body = Physics.Body;
                body.AddForce(Vector3.up * 6.5f, ForceMode.VelocityChange);
                Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
                if (axis.sqrMagnitude < 0.01f) axis = transform.forward;
                body.AddTorque(axis.normalized * 5.5f, ForceMode.VelocityChange);
                ChaosFx.Instance?.Pop(transform.position + Vector3.up * 2f, "ОП-ПА!", new Color(0.6f, 0.9f, 1f), 0.9f);
                ChaosFx.Instance?.Sound(Audio.SoundBank.Spring, transform.position, 0.6f, 1.3f);
            }
            // После кувырка вращение снова ограничено как обычно.
            if (!Tumbling && Physics.IsGrounded && Physics.Body.maxAngularVelocity > 8f) Physics.Body.maxAngularVelocity = 8f;
        }

        /// Жёсткое приземление после подброса: мультяшный отскок, пыль и ударная волна (цепные реакции).
        void OnLanded(float strength)
        {
            Bumped?.Invoke(strength);
            if (!Power.LandingBounce || !Physics.Simulated || Time.time - LaunchedAt > 9f || Time.time - bounceAt < 0.6f) return;
            var body = Physics.Body;
            if (strength < 9f)
            {
                // Мягкое приземление после полёта — без отскока, но тоже «по течению гонки».
                KeepFlowOnLanding(body);
                return;
            }
            bounceAt = Time.time;
            // Лёгкая машинка подскакивает мячиком, тяжёлый внедорожник глухо «садится» (CarSpec.LandingBounce).
            float character = Mathf.Clamp(Spec.LandingBounce, 0f, 2f);
            float bounce = Mathf.Min(9f * Mathf.Max(1f, character), strength * 0.32f * character) * Power.Punch;
            body.AddForce(Vector3.up * bounce, ForceMode.VelocityChange);
            body.AddTorque(Random.onUnitSphere * Mathf.Min(4f, strength * 0.15f) * Mathf.Sqrt(character), ForceMode.VelocityChange);
            int generation = ChainGeneration + 1;
            HardLanding?.Invoke(this, strength, generation);
            KeepFlowOnLanding(body);
            var fx = ChaosFx.Instance;
            if (fx == null) return;
            fx.Blast(transform.position, strength > 18f ? BlastStyle.Big : BlastStyle.ColorPuff, Mathf.Clamp(strength / 16f, 0.6f, 1.6f), new Color(0.85f, 0.75f, 0.6f, 0.75f));
            fx.Pop(transform.position + Vector3.up * 2.2f, strength > 18f ? "БА-БАХ!" : "БУМ!", new Color(1f, 0.75f, 0.3f), Mathf.Clamp(strength / 15f, 0.9f, 1.6f));
            fx.Sound(Audio.SoundBank.Thud, transform.position, Mathf.Clamp01(strength / 20f), Random.Range(0.85f, 1.05f));
            fx.Shockwave(transform.position, Mathf.Clamp(strength * 0.55f, 6f, 14f), Mathf.Clamp(strength * 0.55f, 6f, 13f), generation, this);
        }

        /// Мультяшное приземление «по течению гонки» («безумие»): машина разворачивается по трассе и сохраняет
        /// ход вперёд — полёт кончился, гонка продолжается сразу, а не после долгого разгона с места.
        void KeepFlowOnLanding(Rigidbody body)
        {
            if (!Power.Wild || Guide == null) return;
            Vector3 dir = Guide.DirectionAt(Guide.Nearest(transform.position));
            Vector3 v = body.linearVelocity;
            float along = Vector3.Dot(v, dir);
            Vector3 lateral = Vector3.ProjectOnPlane(v, Vector3.up) - dir * along;
            body.linearVelocity = dir * Mathf.Max(along, 20f) + lateral * 0.35f + Vector3.up * v.y;
            settleUntil = Time.time + 1.6f;
        }

        /// Удар физического объекта хаоса или конструкции события — с пределом изменения скорости ChaosPower.
        static bool FromEvent(Collision collision) =>
            (collision.rigidbody != null && collision.rigidbody.GetComponent<ChaosBody>() != null)
            || collision.collider.GetComponentInParent<RaceEvent>() != null;

        void OnCollisionStay(Collision collision)
        {
            if (FromEvent(collision)) Physics.LimitEventImpact(Invulnerable ? ShieldedImpact : Power.ImpactDeltaV, Invulnerable);
        }

        /// Под защитой удар тела или конструкции события почти не меняет скорость (не подбрасывает, не крутит).
        const float ShieldedImpact = 1.5f;

        void OnCollisionEnter(Collision collision)
        {
            if (FromEvent(collision))
            {
                if (Invulnerable && collision.impulse.magnitude > 200f) Blocks("удар");
                Physics.LimitEventImpact(Invulnerable ? ShieldedImpact : Power.ImpactDeltaV, Invulnerable);
            }
            float strength = collision.impulse.magnitude / Mathf.Max(1f, Physics.Body.mass);
            TryBilliard(collision);
            if (strength < 0.8f) return;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            Impact?.Invoke(point, strength);
            if (strength > 1.5f) Bumped?.Invoke(strength);
        }

        /// «Бильярд»: в эту машину (её считает этот компьютер) врезалась летящая после события машина —
        /// её отбрасывает с кувырком, а цепная реакция продолжается.
        void TryBilliard(Collision collision)
        {
            if (!Power.CarBilliards || !Physics.Simulated || collision.rigidbody == null || Time.time - billiardAt < 0.8f || Invulnerable) return;
            var other = collision.rigidbody.GetComponent<CarController>();
            if (other == null) return;
            float relative = collision.relativeVelocity.magnitude;
            bool charged = other.Physics.Simulated ? other.Charged : relative > 16f;
            if (!charged || relative < 8f) return;
            billiardAt = Time.time;
            Vector3 away = transform.position - other.transform.position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : transform.right;
            float k = Mathf.Clamp(relative / 20f, 0.5f, 1.5f);
            ApplyLaunch(away * 16f * k + Vector3.up * 10f * k, Random.onUnitSphere * 7f * k, "сбит летящей машиной", other.ChainGeneration + 1);
            Billiard?.Invoke(other, this, relative);
            var fx = ChaosFx.Instance;
            if (fx == null) return;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            fx.Blast(point, BlastStyle.Spark, 1.1f);
            fx.Pop(point + Vector3.up * 2f, "СТРАЙК!", new Color(1f, 0.45f, 0.3f), 1.3f);
            fx.Sound(Audio.SoundBank.Whack, point, 1f, Random.Range(0.85f, 1.1f));
        }
    }
}
