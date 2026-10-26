using UnityEngine;
using UnityEngine.InputSystem;

namespace RandomCar.Car
{
    /// Управление игроком: W/S или стрелки — газ и тормоз, A/D или стрелки — руль.
    [RequireComponent(typeof(CarController))]
    public sealed class PlayerDriver : MonoBehaviour
    {
        CarController car;

        void Awake() => car = GetComponent<CarController>();

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            car.Throttle = Axis(keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed,
                                keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed);
            car.Steer = Axis(keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed,
                             keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed);
        }

        static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }
}
