using System;
using System.Text;
using RandomCar.Car;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RandomCar.EditorTools
{
    /// Лаборатория физики для разработчика: прогоняет машину по сценарию в изолированной физической сцене
    /// и пишет телеметрию (скорость, передача, обороты, наддув, занос, нагрузки колёс).
    /// Меню RandomCar/Physics Lab Report — отчёт по всем машинам в консоль и в Logs/PhysicsLab.txt.
    public static class PhysicsLab
    {
        const float Dt = 0.01f;
        static string[] Cars => CarRoster.Keys;

        public enum Scenario { Accelerate, Brake, Corner, Slide }

        [MenuItem("RandomCar/Physics Lab Report")]
        public static void ReportMenu()
        {
            var sb = new StringBuilder();
            foreach (string key in Cars)
                foreach (Scenario s in Enum.GetValues(typeof(Scenario)))
                    sb.AppendLine(Run(key, s, 0.5f));
            System.IO.File.WriteAllText("Logs/PhysicsLab.txt", sb.ToString());
            Debug.Log(sb.ToString());
        }

        /// Прогон сценария; every — период записи телеметрии, с.
        public static string Run(string key, Scenario scenario, float every)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var physics = scene.GetPhysicsScene();
                var ground = new GameObject("Ground");
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.AddComponent<BoxCollider>().size = new Vector3(6000f, 1f, 6000f);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Cars/Car_{key}.prefab");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var car = go.GetComponent<CarPhysics>();
                car.Initialize();
                car.ResetTo(Vector3.zero, Quaternion.identity);
                car.Locked = false;

                var sb = new StringBuilder($"== {key} / {scenario} ==\n");
                void Sim(float seconds, Func<CarPhysics, CarInput> drive, bool log)
                {
                    int steps = Mathf.RoundToInt(seconds / Dt);
                    int logEvery = Mathf.Max(1, Mathf.RoundToInt(every / Dt));
                    for (int i = 0; i < steps; i++)
                    {
                        car.Input = drive(car);
                        car.Step(Dt);
                        physics.Simulate(Dt);
                        if (log && i % logEvery == 0) sb.AppendLine(Line(car, i * Dt));
                    }
                }

                car.Locked = true;
                Sim(1f, _ => CarInput.None, false);
                car.Locked = false;
                switch (scenario)
                {
                    case Scenario.Accelerate:
                        Sim(30f, _ => new CarInput { Throttle = 1f }, true);
                        break;
                    case Scenario.Brake:
                        car.Body.linearVelocity = car.transform.forward * (100f / 3.6f);
                        Sim(5f, c => new CarInput { Brake = c.ForwardSpeed > 0.3f ? 1f : 0f }, true);
                        sb.AppendLine($"путь {car.transform.position.z:F1} м");
                        break;
                    case Scenario.Corner:
                        Sim(10f, c => new CarInput { Throttle = Mathf.Clamp01((20f - c.ForwardSpeed) * 0.3f), Steer = 0.7f }, true);
                        break;
                    case Scenario.Slide:
                        car.Body.linearVelocity = car.transform.forward * 24f;
                        Sim(0.8f, _ => new CarInput { Steer = 1f, Handbrake = true }, true);
                        Sim(4f, _ => new CarInput { Throttle = 0.2f }, true);
                        break;
                }
                return sb.ToString();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static string Line(CarPhysics c, float t)
        {
            var w = c.Wheels;
            var sb = new StringBuilder();
            sb.Append($"t={t,5:F2} v={c.ForwardSpeed * 3.6f,6:F1} g={c.Drivetrain.Gear,2} rpm={c.Drivetrain.Rpm,5:F0} boost={c.Turbo.Boost,4:F2} ");
            sb.Append($"beta={c.BodySlipAngle,6:F1} yaw={c.Body.angularVelocity.y,5:F2} ax={c.LongitudinalAcceleration / 9.81f,5:F2}g ay={c.LateralAcceleration / 9.81f,5:F2}g steer={c.SteerAngle,5:F1} ");
            sb.Append("load=");
            foreach (var x in w) sb.Append($"{x.Load,5:F0}/");
            sb.Append(" slip=");
            foreach (var x in w) sb.Append($"{x.LateralSlip:F1}|{x.LongitudinalSlip:F1} ");
            return sb.ToString();
        }
    }
}
