using System;
using UnityEngine;

namespace RandomCar.Car
{
    public enum DriveType { FrontWheel, RearWheel, AllWheel }

    /// Двигатель: момент по оборотам задаётся тремя точками (холостые, пик, отсечка) и сглаживается.
    [Serializable]
    public sealed class EngineSpec
    {
        [Tooltip("Обороты холостого хода")] public float IdleRpm = 950f;
        [Tooltip("Обороты пикового момента")] public float PeakTorqueRpm = 4600f;
        [Tooltip("Отсечка")] public float MaxRpm = 7600f;
        [Tooltip("Пиковый момент без наддува, Н·м")] public float PeakTorque = 270f;
        [Tooltip("Доля пикового момента на холостых")] [Range(0.1f, 1f)] public float TorqueAtIdle = 0.55f;
        [Tooltip("Доля пикового момента на отсечке")] [Range(0.1f, 1f)] public float TorqueAtMax = 0.78f;
        [Tooltip("Торможение двигателем на отсечке, Н·м")] public float EngineBraking = 70f;
        [Tooltip("Скорость набора и сброса оборотов без нагрузки, об/мин в секунду")] public float FreeRevRate = 14000f;
        [Tooltip("Обороты, на которых сцепление отпускают при старте")] public float LaunchRpm = 3600f;
        [Tooltip("Цилиндров (для звука)")] public int Cylinders = 4;
    }

    /// Автоматическая коробка: передаточные числа, главная пара, время переключения.
    [Serializable]
    public sealed class GearboxSpec
    {
        public float[] Ratios = { 3.10f, 2.10f, 1.58f, 1.27f, 1.06f, 0.92f };
        public float Reverse = 3.2f;
        public float FinalDrive = 4.0f;
        [Tooltip("КПД трансмиссии")] [Range(0.6f, 1f)] public float Efficiency = 0.9f;
        [Tooltip("Разрыв тяги при переключении, с")] public float ShiftTime = 0.16f;
        [Tooltip("Доля отсечки, на которой переключаемся вверх")] [Range(0.6f, 1f)] public float UpshiftAt = 0.93f;
        [Tooltip("Доля отсечки, ниже которой переключаемся вниз")] [Range(0.2f, 0.8f)] public float DownshiftAt = 0.5f;
    }

    /// Турбина: давление наддува растёт от оборотов и нагрузки с задержкой, даёт дополнительный момент.
    [Serializable]
    public sealed class TurboSpec
    {
        [Tooltip("Максимальное давление, бар")] public float MaxBoost = 0.8f;
        [Tooltip("Прирост момента на 1 бар (0.5 = +50%)")] public float TorquePerBar = 0.5f;
        [Tooltip("Обороты, с которых турбина начинает дуть")] public float SpoolStartRpm = 2400f;
        [Tooltip("Обороты полного наддува")] public float FullBoostRpm = 4400f;
        [Tooltip("Задержка отклика (постоянная времени), с")] public float Lag = 0.55f;
        [Tooltip("Постоянная времени сброса давления без перепускного клапана, с")] public float Decay = 0.45f;
        [Tooltip("Давление, выше которого при сбросе газа срабатывает перепускной клапан, бар")] public float BlowOffThreshold = 0.35f;
    }

    /// Ось: подвеска, шины и тормоза. Сцепление — коэффициенты трения шины на сухом асфальте.
    [Serializable]
    public sealed class AxleSpec
    {
        [Header("Шины")]
        [Tooltip("Продольное сцепление")] public float LongitudinalGrip = 1.25f;
        [Tooltip("Боковое сцепление")] public float LateralGrip = 1.3f;
        [Tooltip("Угол увода пикового сцепления, градусы")] public float PeakSlipAngle = 7f;
        [Tooltip("Доля сцепления при полном скольжении")] [Range(0.3f, 1f)] public float SlideGrip = 0.72f;
        [Tooltip("Падение сцепления с ростом нагрузки (0 — нет)")] [Range(0f, 0.5f)] public float LoadSensitivity = 0.12f;

        [Header("Подвеска")]
        [Tooltip("Полный ход, м")] public float Travel = 0.24f;
        [Tooltip("Собственная частота, Гц")] public float SpringFrequency = 1.9f;
        [Tooltip("Демпфирование (доля критического)")] [Range(0.1f, 1.2f)] public float DampingRatio = 0.42f;
        [Tooltip("Стабилизатор, доля жёсткости пружины")] [Range(0f, 2f)] public float AntiRoll = 0.8f;

        [Header("Тормоза")]
        [Tooltip("Доля тормозного усилия на ось")] [Range(0f, 1f)] public float BrakeShare = 0.6f;
    }

    /// Все характеристики машины в одном месте. Физика читает только этот ассет; косметика сюда не попадает.
    [CreateAssetMenu(menuName = "RandomCar/Car Spec")]
    public sealed class CarSpec : ScriptableObject
    {
        [Header("Описание")]
        public string DisplayName = "Машина";
        [TextArea] public string Description = "";

        [Header("Кузов")]
        [Tooltip("Масса, кг")] public float Mass = 1200f;
        [Tooltip("Центр масс относительно корня машины, м")] public Vector3 CenterOfMass = new Vector3(0f, 0.38f, 0.02f);
        [Tooltip("Множитель моментов инерции (больше — ленивее вращается)")] public Vector3 InertiaScale = new Vector3(1.1f, 1.1f, 1f);

        [Header("Двигатель и трансмиссия")]
        public EngineSpec Engine = new EngineSpec();
        public GearboxSpec Gearbox = new GearboxSpec();
        public TurboSpec Turbo = new TurboSpec();
        public DriveType Drive = DriveType.RearWheel;
        [Tooltip("Доля момента на переднюю ось для полного привода")] [Range(0f, 1f)] public float AwdFrontShare = 0.4f;

        [Header("Оси")]
        public AxleSpec Front = new AxleSpec();
        public AxleSpec Rear = new AxleSpec { BrakeShare = 0.4f, PeakSlipAngle = 8f };

        [Tooltip("Множитель сцепления на рыхлом покрытии (гравий, песок, трава): раллийные шины держат лучше")] public float LooseSurfaceGrip = 1f;

        [Header("Тормоза и руль")]
        [Tooltip("Тормозное замедление на сухом асфальте, доля g (ограничено сцеплением)")] public float BrakeDecel = 1.15f;
        [Tooltip("Ручник: момент на задних колёсах как доля веса")] public float HandbrakeForce = 0.9f;
        [Tooltip("Максимальный угол поворота колёс, градусы")] public float MaxSteerAngle = 34f;

        [Header("Сопротивление")]
        [Tooltip("Аэродинамическое сопротивление: F = k·v², Н/(м/с)²")] public float Drag = 0.42f;
        [Tooltip("Прижимная сила: F = k·v², Н/(м/с)²")] public float Downforce = 0.9f;
        [Tooltip("Сопротивление качению, доля нагрузки")] public float RollingResistance = 0.014f;
        [Tooltip("Ограничитель скорости, км/ч")] public float TopSpeedKmh = 200f;

        [Header("Характер (руль, полёт, удары)")]
        [Tooltip("Скорость рулевой рейки, градусов в секунду: спорткар — быстрая, внедорожник — ленивая")]
        public float SteerSpeed = 200f;
        [Tooltip("Выравнивание колёсами вниз в воздухе, рад/с²: больше — устойчивее в прыжке")]
        public float AirLeveling = 6f;
        [Tooltip("Гашение вращения в воздухе, 1/с")]
        public float AirDamping = 1.5f;
        [Tooltip("Доворот рулём в полёте, рад/с²")]
        public float AirSteer = 2.4f;
        [Tooltip("Стойкость к ударам событий: 1 — средняя машина, больше — тяжелее сдвинуть и подбросить")]
        [Range(0.5f, 2f)] public float KnockResistance = 1f;
        [Tooltip("Упругость кузова при столкновениях (0 — глухой удар, 1 — мячик)")]
        [Range(0f, 0.8f)] public float Bounciness = 0.12f;
        [Tooltip("Трение кузова о стены и соседей (скольжение вдоль стены, а не залипание)")]
        [Range(0f, 1f)] public float BodyFriction = 0.15f;
        [Tooltip("Отскок при жёстком приземлении после полёта (множитель мультяшного отскока)")]
        [Range(0f, 2f)] public float LandingBounce = 1f;

        public float TopSpeed => TopSpeedKmh / 3.6f;

        /// Момент двигателя без наддува на оборотах rpm, Н·м (кривая из трёх точек со сглаживанием).
        public float TorqueAt(float rpm)
        {
            var e = Engine;
            float shape;
            if (rpm <= e.PeakTorqueRpm)
            {
                float t = Mathf.InverseLerp(e.IdleRpm, e.PeakTorqueRpm, rpm);
                shape = Mathf.Lerp(e.TorqueAtIdle, 1f, t * t * (3f - 2f * t));
            }
            else
            {
                float t = Mathf.InverseLerp(e.PeakTorqueRpm, e.MaxRpm, rpm);
                shape = Mathf.Lerp(1f, e.TorqueAtMax, t * t);
            }
            return e.PeakTorque * shape;
        }

        /// Скорость машины на отсечке в передаче gear (1..N), м/с.
        public float SpeedAtRedline(int gear, float wheelRadius)
        {
            float ratio = Gearbox.Ratios[Mathf.Clamp(gear, 1, Gearbox.Ratios.Length) - 1] * Gearbox.FinalDrive;
            return Engine.MaxRpm * Mathf.PI / 30f / ratio * wheelRadius;
        }

        public int GearCount => Gearbox.Ratios.Length;

        /// Пиковая мощность с полным наддувом, кВт: максимум момента × оборотов по кривой двигателя.
        public float PeakPowerKw()
        {
            float best = 0f;
            float boost = 1f + Turbo.TorquePerBar * Turbo.MaxBoost;
            for (float rpm = Engine.IdleRpm; rpm <= Engine.MaxRpm; rpm += 100f)
            {
                float factor = Mathf.Lerp(1f, boost, Mathf.Clamp01(Mathf.InverseLerp(Turbo.SpoolStartRpm, Turbo.FullBoostRpm, rpm)));
                best = Mathf.Max(best, TorqueAt(rpm) * factor * rpm * Mathf.PI / 30f / 1000f);
            }
            return best;
        }

        /// Индекс производительности для подбора соперников: мощность на массу и сцепление.
        public float Rating => PeakPowerKw() * 1000f / Mass * (Front.LateralGrip + Rear.LateralGrip) * 0.5f;

        /// Визуальный крен кузова, градусов на 1 g бокового ускорения (только вид, CarVisuals): чем выше центр масс
        /// относительно колеи и мягче пружины, тем сильнее машина «валится» в повороте и клюёт при торможении.
        public float LeanPerG(float halfTrack)
        {
            float softness = 1.6f / Mathf.Max(0.8f, (Front.SpringFrequency + Rear.SpringFrequency) * 0.5f);
            return Mathf.Clamp(5.5f * CenterOfMass.y / Mathf.Max(0.5f, halfTrack) * softness * softness, 0.8f, 6.5f);
        }
    }
}
