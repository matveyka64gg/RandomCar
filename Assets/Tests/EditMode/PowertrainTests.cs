using NUnit.Framework;
using RandomCar.Car;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Tests
{
    /// Двигатель, коробка, турбина, шина и гоночная линия — без сцены, чистые модели.
    public sealed class PowertrainTests
    {
        const float Dt = 0.01f;

        static CarSpec Spec()
        {
            var s = ScriptableObject.CreateInstance<CarSpec>();
            s.Turbo.MaxBoost = 0.8f;
            s.Turbo.Lag = 0.5f;
            return s;
        }

        [Test]
        public void TurboSpoolsUnderLoadWithLag()
        {
            var spec = Spec();
            var turbo = new Turbo(spec.Turbo);
            turbo.Step(1f, 6000f, 7600f, Dt);
            Assert.That(turbo.Boost, Is.LessThan(0.1f), "мгновенного наддува нет");
            float t = 0f;
            while (turbo.Boost01 < 0.9f && t < 10f)
            {
                turbo.Step(1f, 6000f, 7600f, Dt);
                t += Dt;
            }
            Assert.That(t, Is.InRange(0.3f, 4f), $"давление 90% за {t:F2} с");
            Assert.That(turbo.Boost, Is.LessThanOrEqualTo(spec.Turbo.MaxBoost + 1e-4f), "не выше максимума");
            Assert.That(turbo.TorqueMultiplier, Is.LessThanOrEqualTo(1f + spec.Turbo.TorquePerBar * spec.Turbo.MaxBoost + 1e-4f));
        }

        [Test]
        public void TurboNeedsRevsAndLoad()
        {
            var spec = Spec();
            var low = new Turbo(spec.Turbo);
            var idle = new Turbo(spec.Turbo);
            for (int i = 0; i < 500; i++)
            {
                low.Step(1f, spec.Turbo.SpoolStartRpm - 200f, 7600f, Dt);
                idle.Step(0f, 7000f, 7600f, Dt);
            }
            Assert.That(low.Boost, Is.LessThan(0.02f), "ниже порога раскрутки наддува нет");
            Assert.That(idle.Boost, Is.LessThan(0.02f), "без нагрузки наддува нет");
        }

        [Test]
        public void TurboDropsAndBlowsOffWhenLifting()
        {
            var spec = Spec();
            var turbo = new Turbo(spec.Turbo);
            int blowOffs = 0;
            turbo.BlowOff += _ => blowOffs++;
            for (int i = 0; i < 400; i++) turbo.Step(1f, 6500f, 7600f, Dt);
            float before = turbo.Boost;
            for (int i = 0; i < 30; i++) turbo.Step(0f, 6500f, 7600f, Dt);
            Assert.That(blowOffs, Is.EqualTo(1), "клапан срабатывает один раз при сбросе газа");
            Assert.That(turbo.Boost, Is.LessThan(before * 0.3f), "давление быстро падает");
        }

        [Test]
        public void GearsGoUpUnderThrottleAndRpmStaysBelowLimit()
        {
            var spec = Spec();
            var dt = new Drivetrain(spec, new Turbo(spec.Turbo));
            int maxGear = 1;
            float speed = 0f;
            float r = 0.36f;
            for (int i = 0; i < 4000; i++)
            {
                var s = new DriveState { Throttle = 1f, RawThrottle = 1f, ForwardSpeed = speed, WheelOmega = speed / r, TopSpeed = 80f };
                float torque = dt.Step(s, Dt);
                // Простейшая машина 1200 кг без сопротивления, для проверки логики коробки.
                speed += torque / r / 1200f * Dt - speed * speed * 0.0004f * Dt;
                Assert.That(dt.Rpm, Is.LessThanOrEqualTo(spec.Engine.MaxRpm * 1.011f));
                Assert.That(dt.Gear, Is.GreaterThanOrEqualTo(maxGear - 0), "под полным газом коробка не переключается вниз");
                maxGear = Mathf.Max(maxGear, dt.Gear);
            }
            Assert.That(maxGear, Is.GreaterThanOrEqualTo(4), "коробка дошла до высоких передач");
        }

        [Test]
        public void ShiftCutsTorque()
        {
            var spec = Spec();
            var dt = new Drivetrain(spec, new Turbo(spec.Turbo));
            float r = 0.36f;
            // Скорость на отсечке первой передачи: коробка должна начать переключение.
            float speed = spec.SpeedAtRedline(1, r);
            float torque = dt.Step(new DriveState { Throttle = 1f, RawThrottle = 1f, ForwardSpeed = speed, WheelOmega = speed / r, TopSpeed = 80f }, Dt);
            Assert.That(dt.Gear, Is.EqualTo(2));
            Assert.That(dt.Shifting, Is.True);
            Assert.That(torque, Is.EqualTo(0f), "во время переключения тяги нет");
        }

        [Test]
        public void ReverseEngagesOnlyWhenStoppedWithBrake()
        {
            var spec = Spec();
            var dt = new Drivetrain(spec, new Turbo(spec.Turbo));
            for (int i = 0; i < 100; i++) dt.Step(new DriveState { Brake = 1f, ForwardSpeed = 10f, WheelOmega = 10f / 0.36f, TopSpeed = 80f }, Dt);
            Assert.That(dt.Gear, Is.GreaterThan(0), "на ходу тормоз не включает заднюю");
            for (int i = 0; i < 100; i++) dt.Step(new DriveState { Brake = 1f, ForwardSpeed = 0f, TopSpeed = 80f }, Dt);
            Assert.That(dt.Gear, Is.EqualTo(-1));
            dt.Step(new DriveState { RawThrottle = 1f, Throttle = 1f, ForwardSpeed = -0.2f, TopSpeed = 80f }, Dt);
            Assert.That(dt.Gear, Is.EqualTo(1), "газ возвращает вперёд");
        }

        [Test]
        public void TorqueCurvePeaksAtPeakRpm()
        {
            var spec = Spec();
            float peak = spec.TorqueAt(spec.Engine.PeakTorqueRpm);
            Assert.That(peak, Is.EqualTo(spec.Engine.PeakTorque).Within(0.01f));
            Assert.That(spec.TorqueAt(spec.Engine.IdleRpm), Is.LessThan(peak));
            Assert.That(spec.TorqueAt(spec.Engine.MaxRpm), Is.LessThan(peak));
        }

        [Test]
        public void TireLosesLateralGripWhenWheelSpins()
        {
            const float load = 3000f;
            var grip = TireModel.Solve(0f, 1f, 6f, load, 1.2f, 1.3f, 7f, 0.7f, 1f, 0f);
            var spin = TireModel.Solve(load * 3f, 1f, 6f, load, 1.2f, 1.3f, 7f, 0.7f, 1f, 0f);
            Assert.That(spin.LongSlip, Is.True);
            Assert.That(Mathf.Abs(spin.Lateral), Is.LessThan(Mathf.Abs(grip.Lateral) * 0.6f), "буксующее колесо держит вбок хуже");
            Assert.That(Mathf.Abs(spin.Longitudinal), Is.LessThanOrEqualTo(1.2f * load + 1f), "продольная не больше сцепления");
        }

        [Test]
        public void TireCurvePeaksThenFalls()
        {
            Assert.That(TireModel.LateralCurve(1f, 0.7f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(TireModel.LateralCurve(0.5f, 0.7f), Is.LessThan(1f));
            Assert.That(TireModel.LateralCurve(4f, 0.7f), Is.EqualTo(0.7f).Within(1e-3f));
        }

        [Test]
        public void RacingLineBrakesBeforeHairpin()
        {
            var path = TrackLayout.CreatePath();
            var line = new RacingLine(path, 1.2f, 9f, 30f, 58f, 4f);
            float minSpeed = float.MaxValue, maxSpeed = 0f;
            int tightest = 0;
            float tightestCurv = 0f;
            for (int i = 0; i < path.Count; i++)
            {
                float p = i / (float)path.Count;
                minSpeed = Mathf.Min(minSpeed, line.SpeedAt(p));
                maxSpeed = Mathf.Max(maxSpeed, line.SpeedAt(p));
                float c = Mathf.Abs(path.CurvatureAt(i, 4));
                if (c > tightestCurv) { tightestCurv = c; tightest = i; }
            }
            Assert.That(minSpeed, Is.InRange(12f, 26f), "шпилька проходится на 45–95 км/ч");
            Assert.That(maxSpeed, Is.GreaterThan(40f), "на прямых ИИ разгоняется");
            // 40 м до шпильки скорость уже снижена относительно прямой.
            float before = line.SpeedAt((tightest - 40f / (path.Length / path.Count)) / path.Count);
            Assert.That(before, Is.LessThan(maxSpeed));
            for (int i = 0; i < path.Count; i++)
                Assert.That(Mathf.Abs(line.LateralAt(i / (float)path.Count)), Is.LessThanOrEqualTo(4f + 1e-3f));
        }
    }
}
