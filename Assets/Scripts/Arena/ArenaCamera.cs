using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RandomCar.Arena
{
    /// Камера обзора на полигоне испытаний: облёт вокруг зоны испытания (событие, а если его нет — место перед машиной).
    /// Включается вместо камеры машины (FollowCamera) на том же объекте камеры. Управление — Input System, без привязки
    /// к мыши: перетаскивание мышью или пальцем (не над меню), колесо мыши или плечевые кнопки геймпада — ближе/дальше,
    /// правый стик геймпада — вращение.
    [RequireComponent(typeof(Camera))]
    public sealed class ArenaCamera : MonoBehaviour
    {
        const float MinPitch = 6f, MaxPitch = 80f, MinDistance = 10f, MaxDistance = 150f;

        /// Точка, вокруг которой облёт (полигон обновляет каждый кадр).
        public Func<Vector3> Focus;
        /// Указатель над интерфейсом (экранные координаты Input System): перетаскивание с меню камеру не вращает.
        public Func<Vector2, bool> PointerOverUi;

        float yaw, pitch = 24f, distance = 42f;
        Vector3 focus;
        bool snapped, dragging;
        InputAction pointerDelta, press, stick, scroll, closer, farther;

        void Awake()
        {
            pointerDelta = new InputAction("ArenaLook", InputActionType.Value, "<Pointer>/delta");
            press = new InputAction("ArenaDrag", InputActionType.Button);
            press.AddBinding("<Mouse>/leftButton");
            press.AddBinding("<Mouse>/rightButton");
            press.AddBinding("<Touchscreen>/primaryTouch/press");
            stick = new InputAction("ArenaStick", InputActionType.Value, "<Gamepad>/rightStick");
            scroll = new InputAction("ArenaZoom", InputActionType.Value, "<Mouse>/scroll/y");
            closer = new InputAction("ArenaCloser", InputActionType.Button, "<Gamepad>/rightShoulder");
            farther = new InputAction("ArenaFarther", InputActionType.Button, "<Gamepad>/leftShoulder");
        }

        void OnEnable()
        {
            snapped = false;
            dragging = false;
            foreach (var a in Actions()) a?.Enable();
        }

        void OnDisable()
        {
            foreach (var a in Actions()) a?.Disable();
        }

        void OnDestroy()
        {
            foreach (var a in Actions()) a?.Dispose();
        }

        InputAction[] Actions() => new[] { pointerDelta, press, stick, scroll, closer, farther };

        /// Начать облёт: камера сзади-сбоку от машины, смотрит на зону (курс — по направлению машины).
        public void Frame(Vector3 heading)
        {
            heading.y = 0f;
            yaw = heading.sqrMagnitude > 0.01f ? Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg + 35f : 0f;
            pitch = 24f;
            distance = 42f;
            snapped = false;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            Vector3 target = Focus != null ? Focus() : transform.position + transform.forward * 20f;
            focus = snapped ? Vector3.Lerp(focus, target, 1f - Mathf.Exp(-3f * dt)) : target;
            snapped = true;

            if (press.WasPressedThisFrame())
            {
                var pointer = Pointer.current;
                dragging = pointer == null || PointerOverUi == null || !PointerOverUi(pointer.position.ReadValue());
            }
            if (!press.IsPressed()) dragging = false;
            if (dragging)
            {
                Vector2 d = pointerDelta.ReadValue<Vector2>();
                yaw += d.x * 0.22f;
                pitch -= d.y * 0.18f;
            }
            Vector2 s = stick.ReadValue<Vector2>();
            yaw += s.x * 110f * dt;
            pitch -= s.y * 70f * dt;
            float wheel = scroll.ReadValue<float>();
            if (Mathf.Abs(wheel) > 0.01f) distance *= Mathf.Pow(0.999f, wheel);
            if (closer.IsPressed()) distance *= Mathf.Pow(0.4f, dt);
            if (farther.IsPressed()) distance *= Mathf.Pow(2.5f, dt);
            pitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
            distance = Mathf.Clamp(distance, MinDistance, MaxDistance);

            Vector3 look = focus + Vector3.up * 2f;
            Vector3 position = look + Quaternion.Euler(pitch, yaw, 0f) * Vector3.back * distance;
            position.y = Mathf.Max(position.y, focus.y + 1.5f);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(look - position, Vector3.up));
        }
    }
}
