using RandomCar.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RandomCar.Car
{
    /// Управление игроком через Input System: клавиатура (W/S/A/D, стрелки, пробел) и геймпад
    /// (триггеры, левый стик, кнопка A). Руль с клавиатуры набирается плавно, скорость набора — из настроек
    /// чувствительности; на скорости руль возвращается к центру быстрее. Помощь при вождении — из настроек.
    [RequireComponent(typeof(CarController))]
    public sealed class PlayerDriver : MonoBehaviour
    {
        CarController car;
        InputAction throttle, brake, steer, handbrake, useItem;

        /// Клавиша бонуса по умолчанию (переназначается в настройках).
        public const string DefaultItemKey = "<Keyboard>/e";
        float steerValue;

        void Awake()
        {
            car = GetComponent<CarController>();
            throttle = new InputAction("Throttle", InputActionType.Value);
            throttle.AddBinding("<Keyboard>/w");
            throttle.AddBinding("<Keyboard>/upArrow");
            throttle.AddBinding("<Gamepad>/rightTrigger");
            brake = new InputAction("Brake", InputActionType.Value);
            brake.AddBinding("<Keyboard>/s");
            brake.AddBinding("<Keyboard>/downArrow");
            brake.AddBinding("<Gamepad>/leftTrigger");
            steer = new InputAction("Steer", InputActionType.Value);
            steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
            steer.AddBinding("<Gamepad>/leftStick/x");
            handbrake = new InputAction("Handbrake", InputActionType.Button);
            handbrake.AddBinding("<Keyboard>/space");
            handbrake.AddBinding("<Gamepad>/buttonSouth");
            useItem = new InputAction("UseItem", InputActionType.Button);
            useItem.AddBinding(DefaultItemKey);
            useItem.AddBinding("<Gamepad>/buttonEast");
            ApplyItemKey();
        }

        /// Клавиша бонуса из настроек: первая привязка — клавиатурная.
        void ApplyItemKey()
        {
            if (useItem == null) return;
            string key = string.IsNullOrEmpty(GameSettings.ItemKey) ? DefaultItemKey : GameSettings.ItemKey;
            if (useItem.bindings[0].effectivePath != key) useItem.ApplyBindingOverride(0, key);
        }

        void OnEnable()
        {
            throttle.Enable();
            brake.Enable();
            steer.Enable();
            handbrake.Enable();
            useItem.Enable();
            ApplyAssists();
            ApplyItemKey();
            GameSettings.Changed += ApplyAssists;
            GameSettings.Changed += ApplyItemKey;
        }

        void OnDisable()
        {
            throttle.Disable();
            brake.Disable();
            steer.Disable();
            handbrake.Disable();
            useItem.Disable();
            GameSettings.Changed -= ApplyAssists;
            GameSettings.Changed -= ApplyItemKey;
        }

        void OnDestroy()
        {
            throttle.Dispose();
            brake.Dispose();
            steer.Dispose();
            handbrake.Dispose();
            useItem.Dispose();
        }

        void ApplyAssists() => car.Physics.Assists = DrivingAssists.For(GameSettings.Assists);

        void UseItem()
        {
            var powerUps = PowerUps.PowerUpDirector.Current;
            var race = Race.RaceManager.Find(car);
            if (powerUps != null && race != null) powerUps.RequestUse(race.Slot);
        }

        void Update()
        {
            float raw = Mathf.Clamp(steer.ReadValue<float>(), -1f, 1f);
            bool analog = steer.activeControl != null && steer.activeControl.device is Gamepad;
            // Экранные кнопки (телефон): руль ◀ ▶ — как клавиши, с тем же плавным набором.
            if (TouchInput.Active && Mathf.Abs(raw) < 0.01f && TouchInput.Steer != 0f)
            {
                raw = TouchInput.Steer;
                analog = false;
            }
            float dt = Time.deltaTime;
            if (analog)
            {
                // Стик: мёртвая зона и мягкая кривая около центра.
                float a = Mathf.Abs(raw) < 0.08f ? 0f : (Mathf.Abs(raw) - 0.08f) / 0.92f;
                steerValue = Mathf.Sign(raw) * Mathf.Pow(a, 1.4f);
            }
            else
            {
                float sensitivity = GameSettings.SteerSensitivity;
                float speed01 = Mathf.Clamp01(Mathf.Abs(car.Speed) / 45f);
                bool returning = Mathf.Abs(raw) < 0.01f || Mathf.Sign(raw) != Mathf.Sign(steerValue);
                float rate = returning ? Mathf.Lerp(5f, 7f, speed01) : Mathf.Lerp(3.6f, 2.4f, speed01) * sensitivity;
                steerValue = Mathf.MoveTowards(steerValue, raw, rate * dt);
            }

            // Бонус: клавиша E (переназначается) или B на геймпаде.
            if (useItem.WasPressedThisFrame() || TouchInput.ConsumeItem()) UseItem();

            car.SetInput(new CarInput
            {
                Throttle = Mathf.Max(Mathf.Clamp01(throttle.ReadValue<float>()), TouchInput.Active && TouchInput.Gas ? 1f : 0f),
                Brake = Mathf.Max(Mathf.Clamp01(brake.ReadValue<float>()), TouchInput.Active && TouchInput.Brake ? 1f : 0f),
                Steer = steerValue,
                Handbrake = handbrake.IsPressed() || TouchInput.Active && TouchInput.Handbrake,
            });
        }
    }
}
