using System;
using System.Text;
using RandomCar.Car;
using RandomCar.Events;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RandomCar.EditorTools
{
    /// Сравнение характеров машин в цифрах (контролируемые сценарии на настоящих префабах в изолированной сцене):
    /// разгон, тормоз, отклик руля, боковое сцепление, занос ручником, посадка после прыжка, толчок события,
    /// удар в стену, визуальный крен. Меню RandomCar/Car Character Report → консоль и Logs/CarCharacter.txt.
    public static class CarCharacterLab
    {
        const float Dt = 0.01f;

        [MenuItem("RandomCar/Car Character Report")]
        public static void ReportMenu() => Debug.Log(Report());

        public static string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("машина     | масса | 0-100 | макс км/ч | тормоз 100 | руль 90% | бок g | занос°/восст с | прыжок 3.5 м: отскок/покой | толчок м/с | стена 40 м/с: отскок | крен°/g");
            foreach (string key in CarRoster.Keys)
            {
                try
                {
                    sb.AppendLine(Row(key));
                }
                catch (Exception e)
                {
                    sb.AppendLine($"{key}: ошибка {e.Message}");
                    Debug.LogException(e);
                }
            }
            System.IO.File.WriteAllText("Logs/CarCharacter.txt", sb.ToString());
            return sb.ToString();
        }

        sealed class Lab : IDisposable
        {
            public readonly Scene Scene = EditorSceneManager.NewPreviewScene();
            public PhysicsScene Physics => Scene.GetPhysicsScene();

            public Lab()
            {
                Box(new Vector3(0f, -0.5f, 0f), new Vector3(6000f, 1f, 6000f));
            }

            public void Box(Vector3 center, Vector3 size)
            {
                var go = new GameObject("Box");
                SceneManager.MoveGameObjectToScene(go, Scene);
                go.transform.position = center;
                go.AddComponent<BoxCollider>().size = size;
            }

            public CarPhysics Car(string key, Vector3 at)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Cars/Car_{key}.prefab");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, Scene);
                var car = go.GetComponent<CarPhysics>();
                car.Initialize();
                car.ResetTo(at, Quaternion.identity);
                car.Assists = DrivingAssists.For(AssistLevel.Standard);
                car.Locked = true;
                Run(car, 1f, _ => CarInput.None);
                car.Locked = false;
                return car;
            }

            public void Run(CarPhysics car, float seconds, Func<CarPhysics, CarInput> drive, Action<CarPhysics> each = null)
            {
                int steps = Mathf.RoundToInt(seconds / Dt);
                for (int i = 0; i < steps; i++)
                {
                    car.Input = drive(car);
                    car.Step(Dt);
                    Physics.Simulate(Dt);
                    each?.Invoke(car);
                }
            }

            public void Dispose() => EditorSceneManager.ClosePreviewScene(Scene);
        }

        static string Row(string key)
        {
            var spec = AssetDatabase.LoadAssetAtPath<CarSpec>($"Assets/Settings/Cars/Spec_{key}.asset");
            float to100 = -1f, top = 0f, brake, steer90 = -1f, lateral = 0f, slidePeak = 0f, recover = -1f;
            float bounce = 0f, settle = -1f, knock, wallBack = 0f;
            using (var lab = new Lab())
            {
                var car = lab.Car(key, Vector3.zero);
                float t = 0f;
                lab.Run(car, 40f, _ => new CarInput { Throttle = 1f }, c => { t += Dt; if (to100 < 0f && c.ForwardSpeed * 3.6f >= 100f) to100 = t; });
                top = car.ForwardSpeed * 3.6f;
            }
            using (var lab = new Lab())
            {
                var car = lab.Car(key, Vector3.zero);
                car.Body.linearVelocity = Vector3.forward * (100f / 3.6f);
                lab.Run(car, 6f, c => new CarInput { Brake = c.ForwardSpeed > 0.3f ? 1f : 0f });
                brake = car.transform.position.z;
            }
            using (var lab = new Lab())
            {
                // Отклик руля на 20 м/с: за сколько колёса доходят до 90% угла.
                var car = lab.Car(key, Vector3.zero);
                car.Body.linearVelocity = Vector3.forward * 20f;
                lab.Run(car, 0.3f, _ => new CarInput { Throttle = 0.3f });
                float t = 0f;
                lab.Run(car, 1.5f, _ => new CarInput { Throttle = 0.3f, Steer = 1f }, c =>
                {
                    t += Dt;
                    if (steer90 < 0f && c.SteerAngle >= c.SteerLimit * 0.9f) steer90 = t;
                    if (t > 0.6f) lateral = Mathf.Max(lateral, Mathf.Abs(c.LateralAcceleration) / 9.81f);
                });
            }
            using (var lab = new Lab())
            {
                var car = lab.Car(key, Vector3.zero);
                car.Body.linearVelocity = Vector3.forward * 24f;
                lab.Run(car, 0.8f, _ => new CarInput { Steer = 1f, Handbrake = true }, c => slidePeak = Mathf.Max(slidePeak, Mathf.Abs(c.BodySlipAngle)));
                float t = 0f;
                lab.Run(car, 5f, _ => new CarInput { Throttle = 0.2f }, c =>
                {
                    t += Dt;
                    if (recover < 0f && Mathf.Abs(c.BodySlipAngle) < 4f && Mathf.Abs(c.Body.angularVelocity.y) < 0.3f) recover = t;
                });
            }
            using (var lab = new Lab())
            {
                var car = lab.Car(key, Vector3.zero);
                car.ResetTo(new Vector3(0f, 3.5f, 0f), Quaternion.identity);
                car.Locked = false;
                car.Body.linearVelocity = Vector3.forward * 16f;
                float t = 0f, landed = -1f;
                lab.Run(car, 5f, _ => new CarInput { Throttle = 0.3f }, c =>
                {
                    t += Dt;
                    if (landed < 0f && c.GroundedWheels >= 2) landed = t;
                    if (landed > 0f && t - landed > 0.15f) bounce = Mathf.Max(bounce, c.transform.position.y);
                    bool calm = c.GroundedWheels == 4 && Mathf.Abs(c.Body.linearVelocity.y) < 0.15f && c.Body.angularVelocity.magnitude < 0.25f;
                    if (landed > 0f && calm && settle < 0f) settle = t - landed;
                    if (!calm) settle = -1f;
                });
            }
            var before = ChaosPower.Level;
            try
            {
                ChaosPower.Set(ChaosLevel.Mayhem);
                using var lab = new Lab();
                var car = lab.Car(key, Vector3.zero);
                Vector3 v0 = car.Body.linearVelocity;
                car.GetComponent<CarController>().ApplyImpulse(new Vector3(4f, 3f, 0f));
                lab.Run(car, Dt, _ => CarInput.None);
                knock = (car.Body.linearVelocity - v0).magnitude;
            }
            finally
            {
                ChaosPower.Set(before);
            }
            using (var lab = new Lab())
            {
                lab.Box(new Vector3(0f, 1.5f, 30.5f), new Vector3(40f, 3f, 1f));
                var car = lab.Car(key, Vector3.zero);
                car.Body.linearVelocity = Vector3.forward * 40f;
                lab.Run(car, 2f, _ => CarInput.None, c => wallBack = Mathf.Max(wallBack, -c.Body.linearVelocity.z));
            }
            float halfTrack = 0.8f;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Cars/Car_{key}.prefab");
            if (prefab != null) halfTrack = Mathf.Abs(prefab.GetComponent<CarPhysics>().Wheels[0].LocalCenter.x);
            return $"{key,-10} | {spec.Mass,5:F0} | {to100,5:F2} | {top,9:F0} | {brake,8:F1} м | {steer90,6:F2} с | {lateral,5:F2} | {slidePeak,5:F0}° / {recover,4:F2} | {bounce,10:F2} м / {settle,4:F2} с | {knock,9:F2} | {wallBack,14:F1} м/с | {spec.LeanPerG(halfTrack),5:F1}";
        }
    }
}
