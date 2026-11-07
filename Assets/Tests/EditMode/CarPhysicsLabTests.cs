using System;
using System.Collections.Generic;
using NUnit.Framework;
using RandomCar.Car;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RandomCar.Tests
{
    /// Контролируемые сценарии физики на настоящих префабах машин в изолированной физической сцене:
    /// разгон, торможение, круг, занос и восстановление, удар в стену, гребёнка, оседание на подвеске.
    /// Числа — границы «правдоподобной аркады», а не реализм; ощущение управления проверяет человек.
    public sealed class CarPhysicsLabTests
    {
        const float Dt = 0.01f;
        /// Все машины автопарка: правила «правдоподобной аркады» общие, характеры разные.
        static readonly string[] Cars = { "Iskra", "Kometa", "Pchelka", "Myata", "Bastion", "Bizon", "Taiga", "Lastochka", "Grom", "Sapsan" };

        sealed class Lab : IDisposable
        {
            public readonly Scene Scene;
            readonly PhysicsScene physics;

            public Lab()
            {
                Scene = EditorSceneManager.NewPreviewScene();
                physics = Scene.GetPhysicsScene();
                Box("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(6000f, 1f, 6000f));
            }

            public GameObject Box(string name, Vector3 center, Vector3 size)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, Scene);
                go.transform.position = center;
                go.AddComponent<BoxCollider>().size = size;
                return go;
            }

            public CarPhysics Car(string key, Vector3 position, float yaw = 0f)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Cars/Car_{key}.prefab");
                Assert.That(prefab, Is.Not.Null, key);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, Scene);
                var car = go.GetComponent<CarPhysics>();
                car.Initialize();
                car.ResetTo(position, Quaternion.Euler(0f, yaw, 0f));
                car.Locked = false;
                car.Assists = DrivingAssists.For(AssistLevel.Standard);
                return car;
            }

            public void Run(CarPhysics car, float seconds, Func<CarPhysics, CarInput> drive, Action<CarPhysics> each = null)
            {
                int steps = Mathf.RoundToInt(seconds / Dt);
                for (int i = 0; i < steps; i++)
                {
                    car.Input = drive(car);
                    car.Step(Dt);
                    physics.Simulate(Dt);
                    each?.Invoke(car);
                }
            }

            public void Settle(CarPhysics car)
            {
                car.Locked = true;
                Run(car, 1.5f, _ => CarInput.None);
                car.Locked = false;
            }

            public void Dispose() => EditorSceneManager.ClosePreviewScene(Scene);
        }

        static float Kmh(CarPhysics car) => car.ForwardSpeed * 3.6f;

        [Test]
        public void CarSettlesOnSuspensionWithoutBouncing([ValueSource(nameof(Cars))] string key)
        {
            using var lab = new Lab();
            var car = lab.Car(key, new Vector3(0f, 0.1f, 0f));
            car.Locked = true;
            lab.Run(car, 3f, _ => CarInput.None);
            Assert.That(Mathf.Abs(car.Body.linearVelocity.y), Is.LessThan(0.05f), "машина успокоилась");
            Assert.That(car.GroundedWheels, Is.EqualTo(4));
            foreach (var w in car.Wheels)
            {
                float travel = (w.Front ? car.Spec.Front : car.Spec.Rear).Travel;
                Assert.That(w.Compression / travel, Is.InRange(0.3f, 0.7f), "подвеска в середине хода");
            }
        }

        [Test]
        public void AccelerationAndTopSpeed([ValueSource(nameof(Cars))] string key)
        {
            using var lab = new Lab();
            var car = lab.Car(key, Vector3.zero);
            lab.Settle(car);
            float t = 0f, to100 = -1f, maxRpm = 0f;
            int gear = 1;
            bool gearsMonotonic = true;
            float maxBoost = 0f;
            lab.Run(car, 40f, _ => new CarInput { Throttle = 1f }, c =>
            {
                t += Dt;
                if (to100 < 0f && Kmh(c) >= 100f) to100 = t;
                maxRpm = Mathf.Max(maxRpm, c.Drivetrain.Rpm);
                if (c.Drivetrain.Gear < gear) gearsMonotonic = false;
                gear = c.Drivetrain.Gear;
                maxBoost = Mathf.Max(maxBoost, c.Turbo.Boost01);
            });
            TestContext.WriteLine($"{key}: 0–100 {to100:F2} с, макс. {Kmh(car):F0} км/ч, передача {gear}, наддув {maxBoost:P0}");
            // Гиперкар быстрее 3 с — это его характер; рамка общая для всего автопарка.
            Assert.That(to100, Is.InRange(2f, 8.5f), "0–100 км/ч");
            Assert.That(Kmh(car), Is.GreaterThan(car.Spec.TopSpeedKmh * 0.88f), "почти до ограничителя за 40 с");
            Assert.That(Kmh(car), Is.LessThan(car.Spec.TopSpeedKmh + 3f), "ограничитель держит");
            Assert.That(maxRpm, Is.LessThanOrEqualTo(car.Spec.Engine.MaxRpm * 1.011f));
            Assert.That(gearsMonotonic, Is.True, "под газом коробка не скачет вниз");
            Assert.That(gear, Is.GreaterThanOrEqualTo(4));
            if (car.Turbo.Present) Assert.That(maxBoost, Is.GreaterThan(0.85f), "турбина выходит на давление под нагрузкой");
            Assert.That(Mathf.Abs(car.transform.position.x), Is.LessThan(3f), "едет прямо");
        }

        [Test]
        public void BrakingFrom100([ValueSource(nameof(Cars))] string key)
        {
            using var lab = new Lab();
            var car = lab.Car(key, Vector3.zero);
            lab.Settle(car);
            car.Body.linearVelocity = car.transform.forward * (100f / 3.6f);
            float start = car.transform.position.z;
            float t = 0f;
            lab.Run(car, 6f, c => new CarInput { Brake = c.ForwardSpeed > 0.3f ? 1f : 0f }, c => { if (c.ForwardSpeed > 0.3f) t += Dt; });
            float distance = car.transform.position.z - start;
            TestContext.WriteLine($"{key}: тормозной путь {distance:F1} м за {t:F2} с");
            Assert.That(distance, Is.InRange(26f, 48f));
            Assert.That(Mathf.Abs(car.transform.position.x), Is.LessThan(1.5f), "тормозит прямо");
            Assert.That(Vector3.Dot(car.transform.up, Vector3.up), Is.GreaterThan(0.95f));
        }

        [Test]
        public void SteadyCornerStaysInControl([ValueSource(nameof(Cars))] string key)
        {
            using var lab = new Lab();
            var car = lab.Car(key, Vector3.zero);
            lab.Settle(car);
            float maxLat = 0f, maxSlip = 0f;
            float yaw0 = car.transform.eulerAngles.y;
            float turned = 0f, lastYaw = yaw0;
            // Скорость ниже предела сцепления на этом радиусе: машина должна ехать ровно, без заноса.
            lab.Run(car, 10f, c => new CarInput { Throttle = Mathf.Clamp01((10f - c.ForwardSpeed) * 0.4f), Steer = 0.7f }, c =>
            {
                if (c.ForwardSpeed > 8f) maxLat = Mathf.Max(maxLat, Mathf.Abs(c.LateralAcceleration));
                maxSlip = Mathf.Max(maxSlip, Mathf.Abs(c.BodySlipAngle));
                float y = c.transform.eulerAngles.y;
                turned += Mathf.DeltaAngle(lastYaw, y);
                lastYaw = y;
            });
            float limit = Mathf.Max(car.Spec.Front.LateralGrip, car.Spec.Rear.LateralGrip) * 9.81f;
            TestContext.WriteLine($"{key}: боковое {maxLat / 9.81f:F2} g, занос {maxSlip:F1}°, повернул {turned:F0}°");
            Assert.That(turned, Is.GreaterThan(180f), "машина поворачивает");
            Assert.That(maxLat, Is.LessThan(limit * 1.35f), "боковое ускорение в пределах сцепления");
            Assert.That(maxSlip, Is.LessThan(8f), "ровный поворот без заноса");
            Assert.That(Vector3.Dot(car.transform.up, Vector3.up), Is.GreaterThan(0.9f));
        }

        [Test]
        public void PowerSlideStaysBoundedWithAssists([ValueSource(nameof(Cars))] string key)
        {
            using var lab = new Lab();
            var car = lab.Car(key, Vector3.zero);
            lab.Settle(car);
            float maxSlip = 0f;
            float maxYaw = 0f;
            // Полный газ в тугом повороте: занос допустим, разворот — нет.
            lab.Run(car, 8f, _ => new CarInput { Throttle = 1f, Steer = 0.8f }, c =>
            {
                maxSlip = Mathf.Max(maxSlip, Mathf.Abs(c.BodySlipAngle));
                maxYaw = Mathf.Max(maxYaw, Mathf.Abs(c.Body.angularVelocity.y));
            });
            TestContext.WriteLine($"{key}: силовой занос до {maxSlip:F1}°, рыскание до {maxYaw:F2} рад/с");
            Assert.That(maxSlip, Is.LessThan(40f), "стабилизация не даёт развернуться");
            Assert.That(Vector3.Dot(car.transform.up, Vector3.up), Is.GreaterThan(0.9f));
        }

        [Test]
        public void SlideIsCausedAndRecovered([ValueSource(nameof(Cars))] string key)
        {
            using var lab = new Lab();
            var car = lab.Car(key, Vector3.zero);
            lab.Settle(car);
            car.Body.linearVelocity = car.transform.forward * 24f;
            float peak = 0f;
            lab.Run(car, 0.8f, _ => new CarInput { Steer = 1f, Handbrake = true }, c => peak = Mathf.Max(peak, Mathf.Abs(c.BodySlipAngle)));
            float recoveredAt = -1f, t = 0f;
            lab.Run(car, 4f, _ => new CarInput { Throttle = 0.2f }, c =>
            {
                t += Dt;
                if (recoveredAt < 0f && Mathf.Abs(c.BodySlipAngle) < 4f && Mathf.Abs(c.Body.angularVelocity.y) < 0.3f) recoveredAt = t;
            });
            TestContext.WriteLine($"{key}: занос до {peak:F1}°, восстановление за {recoveredAt:F2} с");
            Assert.That(peak, Is.GreaterThan(8f), "резкий руль с ручником даёт занос");
            Assert.That(recoveredAt, Is.InRange(0f, 3f), "сцепление восстанавливается");
            Assert.That(Vector3.Dot(car.transform.up, Vector3.up), Is.GreaterThan(0.9f), "без переворота");
        }

        [Test]
        public void WallStopsCarWithoutTunnelingOrLaunch()
        {
            using var lab = new Lab();
            lab.Box("Wall", new Vector3(0f, 1.5f, 60.5f), new Vector3(40f, 3f, 1f));
            var car = lab.Car("Kometa", Vector3.zero);
            lab.Settle(car);
            car.Body.linearVelocity = car.transform.forward * 28f;
            float maxY = 0f, maxZ = 0f;
            lab.Run(car, 4f, _ => new CarInput { Throttle = 1f }, c =>
            {
                maxY = Mathf.Max(maxY, c.transform.position.y);
                maxZ = Mathf.Max(maxZ, c.transform.position.z);
            });
            TestContext.WriteLine($"макс. z {maxZ:F2}, макс. высота {maxY:F2}");
            Assert.That(maxZ, Is.LessThan(60f), "сквозь стену не проходит");
            Assert.That(maxY, Is.LessThan(1.5f), "не взлетает от удара");
            Assert.That(Vector3.Dot(car.transform.up, Vector3.up), Is.GreaterThan(0.8f));
        }

        [Test]
        public void BumpsDoNotFlipOrLaunch([ValueSource(nameof(Cars))] string key)
        {
            using var lab = new Lab();
            for (int i = 0; i < 14; i++)
                lab.Box("Bump" + i, new Vector3(0f, 0.035f, 30f + i * 2.4f + (i % 2) * 0.7f), new Vector3(12f, 0.07f, 0.6f));
            var car = lab.Car(key, Vector3.zero);
            lab.Settle(car);
            car.Body.linearVelocity = car.transform.forward * 25f;
            float minUp = 1f, maxY = 0f;
            lab.Run(car, 3f, _ => new CarInput { Throttle = 0.6f }, c =>
            {
                minUp = Mathf.Min(minUp, Vector3.Dot(c.transform.up, Vector3.up));
                maxY = Mathf.Max(maxY, c.transform.position.y);
            });
            TestContext.WriteLine($"{key}: мин. cos крена {minUp:F3}, макс. высота {maxY:F2}");
            Assert.That(car.transform.position.z, Is.GreaterThan(70f), "проехал гребёнку");
            Assert.That(minUp, Is.GreaterThan(0.95f), "без опасного крена");
            Assert.That(maxY, Is.LessThan(0.45f), "без подпрыгивания");
            Assert.That(Mathf.Abs(car.transform.position.x), Is.LessThan(2f));
        }

        // ---------- v3: посадка, удары, характеры, кузов и арки ----------

        static CarSpec Spec(string key) => AssetDatabase.LoadAssetAtPath<CarSpec>($"Assets/Settings/Cars/Spec_{key}.asset");

        [Test]
        public void JumpLandingSettlesWithoutJitter([ValueSource(nameof(Cars))] string key)
        {
            using var lab = new Lab();
            var car = lab.Car(key, new Vector3(0f, 3.5f, 0f));
            car.Body.linearVelocity = car.transform.forward * 16f;
            float bounce = 0f, landedAt = -1f, t = 0f, minUp = 1f;
            float settleAt = -1f;
            lab.Run(car, 5f, _ => new CarInput { Throttle = 0.3f }, c =>
            {
                t += Dt;
                if (landedAt < 0f && c.GroundedWheels >= 2) landedAt = t;
                if (landedAt > 0f && t - landedAt > 0.15f) bounce = Mathf.Max(bounce, c.transform.position.y);
                minUp = Mathf.Min(minUp, Vector3.Dot(c.transform.up, Vector3.up));
                bool calm = c.GroundedWheels == 4 && Mathf.Abs(c.Body.linearVelocity.y) < 0.15f && c.Body.angularVelocity.magnitude < 0.25f;
                if (landedAt > 0f && calm && settleAt < 0f) settleAt = t - landedAt;
                if (!calm) settleAt = -1f;
            });
            TestContext.WriteLine($"{key}: посадка на {landedAt:F2} с, отскок до {bounce:F2} м, успокоилась за {settleAt:F2} с, мин. cos {minUp:F2}");
            Assert.That(landedAt, Is.GreaterThan(0f), "приземлилась");
            Assert.That(bounce, Is.LessThan(1.2f), "с 3.5 м — без подлёта выше метра");
            Assert.That(settleAt, Is.InRange(0f, 2.5f), "успокаивается, без бесконечного дрожания");
            Assert.That(minUp, Is.GreaterThan(0.8f), "не опрокидывается от прямого прыжка");
            Assert.That(car.transform.position.y, Is.GreaterThan(-0.2f), "не проваливается сквозь землю");
        }

        [Test]
        public void HeavyCarHitsWallWithoutTunnelingOrLaunch([Values("Taiga", "Myata", "Sapsan")] string key)
        {
            using var lab = new Lab();
            lab.Box("Wall", new Vector3(0f, 1.5f, 40.5f), new Vector3(40f, 3f, 1f));
            var car = lab.Car(key, Vector3.zero);
            lab.Settle(car);
            car.Body.linearVelocity = car.transform.forward * 40f;
            float maxY = 0f, maxZ = 0f, maxSpin = 0f;
            lab.Run(car, 3f, _ => new CarInput { Throttle = 1f }, c =>
            {
                maxY = Mathf.Max(maxY, c.transform.position.y);
                maxZ = Mathf.Max(maxZ, c.transform.position.z);
                maxSpin = Mathf.Max(maxSpin, c.Body.angularVelocity.magnitude);
            });
            TestContext.WriteLine($"{key}: 144 км/ч в стену — макс. z {maxZ:F2}, высота {maxY:F2}, вращение {maxSpin:F2} рад/с");
            Assert.That(maxZ, Is.LessThan(40f), "сквозь стену не проходит");
            Assert.That(maxY, Is.LessThan(2.5f), "не взлетает");
            Assert.That(maxSpin, Is.LessThanOrEqualTo(8.01f), "вращение в пределах тела");
        }

        [Test]
        public void HeavyCarPushesLightCarHarder()
        {
            // Удар на 12 м/с в стоящую машину: «Тайга» сдвигает «Мяту» сильнее, чем «Мята» — «Тайгу».
            float Push(string bullet, string target)
            {
                using var lab = new Lab();
                var a = lab.Car(bullet, new Vector3(0f, 0f, -7f));
                var b = lab.Car(target, new Vector3(0f, 0f, 0f));
                var physics = lab.Scene.GetPhysicsScene();
                void Both(int steps, bool locked, System.Action each = null)
                {
                    a.Locked = b.Locked = locked;
                    for (int i = 0; i < steps; i++)
                    {
                        a.Input = CarInput.None;
                        b.Input = CarInput.None;
                        a.Step(Dt);
                        b.Step(Dt);
                        physics.Simulate(Dt);
                        each?.Invoke();
                    }
                }
                Both(150, true);
                a.Body.linearVelocity = a.transform.forward * 12f;
                float dv = 0f;
                // Оба без тормозов: удар передаёт импульс, а не тормоза стоящей машины.
                Both(150, false, () => dv = Mathf.Max(dv, b.Body.linearVelocity.magnitude));
                return dv;
            }
            float heavyHitsLight = Push("Taiga", "Myata");
            float lightHitsHeavy = Push("Myata", "Taiga");
            TestContext.WriteLine($"Тайга → Мята: {heavyHitsLight:F1} м/с, Мята → Тайга: {lightHitsHeavy:F1} м/с");
            Assert.That(heavyHitsLight, Is.GreaterThan(lightHitsHeavy * 1.8f));
        }

        [Test]
        public void EventKnocksDependOnCarWeight()
        {
            var before = Events.ChaosPower.Level;
            try
            {
                Events.ChaosPower.Set(Events.ChaosLevel.Moderate);
                float Knock(string key)
                {
                    using var lab = new Lab();
                    var car = lab.Car(key, Vector3.zero);
                    lab.Settle(car);
                    var controller = car.GetComponent<CarController>();
                    Vector3 v0 = car.Body.linearVelocity;
                    controller.ApplyImpulse(new Vector3(4f, 3f, 0f));
                    lab.Run(car, Dt, _ => CarInput.None);
                    return (car.Body.linearVelocity - v0).magnitude;
                }
                float light = Knock("Myata"), heavy = Knock("Bastion"), mid = Knock("Kometa");
                TestContext.WriteLine($"толчок: Мята {light:F2}, Комета {mid:F2}, Бастион {heavy:F2} м/с");
                Assert.That(light, Is.GreaterThan(mid));
                Assert.That(mid, Is.GreaterThan(heavy));
                var limits = Events.ChaosPower.Current;
                Assert.That(light, Is.LessThanOrEqualTo(limits.MaxSide + limits.MaxUp + 0.01f), "пределы Хаоса соблюдаются");
            }
            finally
            {
                Events.ChaosPower.Set(before);
            }
        }

        [Test]
        public void CharactersAreMorePronounced()
        {
            var specs = new Dictionary<string, CarSpec>();
            foreach (string key in Cars) specs[key] = Spec(key);
            // Тяжёлые — ленивый руль, стойкие к ударам, глухо садятся; лёгкие — наоборот.
            foreach (var heavy in new[] { "Bastion", "Taiga" })
                foreach (var light in new[] { "Myata", "Iskra", "Lastochka" })
                {
                    Assert.That(specs[heavy].SteerSpeed, Is.LessThan(specs[light].SteerSpeed), $"{heavy}/{light}: руль");
                    Assert.That(specs[heavy].KnockResistance, Is.GreaterThan(specs[light].KnockResistance), $"{heavy}/{light}: удары");
                    Assert.That(specs[heavy].LandingBounce, Is.LessThan(specs[light].LandingBounce), $"{heavy}/{light}: отскок");
                    Assert.That(specs[heavy].Bounciness, Is.LessThan(specs[light].Bounciness), $"{heavy}/{light}: упругость");
                    Assert.That(specs[heavy].AirLeveling, Is.GreaterThan(specs[light].AirLeveling), $"{heavy}/{light}: полёт");
                    Assert.That(specs[heavy].Mass, Is.GreaterThan(specs[light].Mass * 2f), $"{heavy}/{light}: масса");
                }
            // Заносчивые заднеприводные: задняя ось слабее передней, сцепление в скольжении ниже, чем у раллийной.
            foreach (var drifter in new[] { "Bizon", "Iskra", "Lastochka" })
            {
                Assert.That(specs[drifter].Rear.LateralGrip, Is.LessThan(specs[drifter].Front.LateralGrip), drifter);
                Assert.That(specs[drifter].Rear.SlideGrip, Is.LessThan(specs["Pchelka"].Rear.SlideGrip), drifter);
            }
            // Визуальный крен: внедорожники валятся заметно сильнее суперкаров.
            Assert.That(specs["Taiga"].LeanPerG(0.93f), Is.GreaterThan(specs["Sapsan"].LeanPerG(0.88f) * 3f));
        }

        [Test]
        public void BodyIsCompoundAndWheelsFitArches([ValueSource(nameof(Cars))] string key)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Cars/Car_{key}.prefab");
            var boxes = prefab.GetComponents<BoxCollider>();
            Assert.That(boxes.Length, Is.EqualTo(2), "низ + кабина");
            var chassis = boxes[0];
            var cabin = boxes[1];
            Assert.That(cabin.size.x, Is.LessThanOrEqualTo(chassis.size.x + 1e-3f), "кабина не шире низа");
            Assert.That(cabin.center.y, Is.GreaterThan(chassis.center.y), "кабина над низом");
            float bottom = chassis.center.y - chassis.size.y * 0.5f;
            Assert.That(bottom, Is.GreaterThanOrEqualTo(0.29f), "днище не цепляет дорогу");
            var physics = prefab.GetComponent<CarPhysics>();
            foreach (var w in physics.Wheels)
            {
                var axle = w.Front ? physics.Spec.Front : physics.Spec.Rear;
                TestContext.WriteLine($"{key}: колесо {(w.Front ? "пер" : "зад")}{(w.Left ? "Л" : "П")} запас в арке {w.ArchRoom:F3} м, полуход {axle.Travel * 0.5f:F3} м");
                Assert.That(w.ArchRoom, Is.InRange(0.03f, 0.3f), "запас хода в арке измерен");
                Assert.That(w.Visual.GetComponent<Renderer>().bounds.min.y, Is.GreaterThan(-0.03f), "колесо стоит на земле, а не в ней");
            }
            Assert.That(prefab.GetComponent<CarLights>(), Is.Not.Null, "огни");
            Assert.That(prefab.transform.Find("Model/Driver"), Is.Not.Null, "пилот");
            Assert.That(prefab.GetComponentInChildren<LODGroup>(), Is.Not.Null, "мелочи салона прячутся вдали");
        }

        [Test]
        public void BodyMotionLeansSettlesAndPopsBack()
        {
            var m = new BodyMotion();
            float peak = 0f;
            for (int i = 0; i < 120; i++)
            {
                m.Step(4f, -2f, 1f / 60f);
                peak = Mathf.Max(peak, m.Roll);
            }
            Assert.That(m.Roll, Is.EqualTo(4f).Within(0.05f), "крен доходит до цели");
            Assert.That(m.Pitch, Is.EqualTo(-2f).Within(0.05f));
            Assert.That(peak, Is.InRange(4.1f, 5.6f), "мультяшный перелёт, но не качели");
            m.Land(15f);
            float dip = 0f;
            for (int i = 0; i < 180; i++)
            {
                m.Step(0f, 0f, 1f / 60f);
                dip = Mathf.Min(dip, m.Heave);
            }
            Assert.That(dip, Is.LessThan(-0.03f), "кузов проседает при приземлении");
            Assert.That(Mathf.Abs(m.Heave), Is.LessThan(0.005f), "и возвращается");
            Assert.That(Mathf.Abs(m.Roll), Is.LessThan(0.05f));
        }

        [Test]
        public void PlayerAndAiShareOnePhysicsModel()
        {
            foreach (string key in Cars)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Cars/Car_{key}.prefab");
                Assert.That(prefab.GetComponent<CarPhysics>(), Is.Not.Null, key);
                Assert.That(prefab.GetComponent<Rigidbody>(), Is.Not.Null, key);
                Assert.That(prefab.GetComponent<CharacterController>(), Is.Null, "старого контроллера нет");
                Assert.That(prefab.GetComponent<CarPhysics>().Spec, Is.Not.Null);
            }
            // Водители только задают CarInput: ни у одного нет своей физики движения.
            foreach (var type in new[] { typeof(PlayerDriver), typeof(AiDriver) })
            {
                Assert.That(type.GetMethod("FixedUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic), Is.Null, type.Name);
            }
        }

        [Test]
        public void CarsHaveDistinctCharacters()
        {
            var specs = new List<CarSpec>();
            foreach (string key in Cars) specs.Add(AssetDatabase.LoadAssetAtPath<CarSpec>($"Assets/Settings/Cars/Spec_{key}.asset"));
            var drives = new HashSet<DriveType>();
            var masses = new HashSet<float>();
            var steer = new HashSet<float>();
            var bounce = new HashSet<float>();
            foreach (var s in specs)
            {
                Assert.That(s, Is.Not.Null);
                drives.Add(s.Drive);
                masses.Add(s.Mass);
                steer.Add(s.SteerSpeed);
                bounce.Add(s.LandingBounce);
            }
            Assert.That(drives.Count, Is.GreaterThanOrEqualTo(3), "разные приводы");
            Assert.That(masses.Count, Is.EqualTo(Cars.Length), "у каждой машины своя масса");
            Assert.That(steer.Count, Is.GreaterThanOrEqualTo(Cars.Length - 1), "свой руль");
            Assert.That(bounce.Count, Is.GreaterThanOrEqualTo(7), "свой отскок");
        }
    }
}
