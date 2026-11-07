using UnityEngine;

namespace RandomCar.Core
{
    /// Состояние экранных кнопок (телефон): руль, газ, тормоз, ручник, бонус, камера, пауза. Заполняет UI.TouchControls,
    /// читают PlayerDriver, FollowCamera и GameFlow вместе со своими действиями Input System (клавиатура и геймпад работают как раньше).
    public static class TouchInput
    {
        /// Кнопки на экране и игрок в гонке.
        public static bool Active;
        public static bool Left, Right, Gas, Brake, Handbrake;
        static bool item, camera, pause;

        /// Руль с кнопок: -1, 0 или 1 (сглаживание — как у клавиатуры, в PlayerDriver).
        public static float Steer => (Right ? 1f : 0f) - (Left ? 1f : 0f);

        public static void PressItem() => item = true;
        public static void PressCamera() => camera = true;
        public static void PressPause() => pause = true;

        /// Одноразовые нажатия: true один раз за нажатие.
        public static bool ConsumeItem() => Take(ref item);
        public static bool ConsumeCamera() => Take(ref camera);
        public static bool ConsumePause() => Take(ref pause);

        static bool Take(ref bool flag)
        {
            bool was = flag;
            flag = false;
            return was;
        }

        /// Отпустить всё (выход из гонки, пауза, кнопки скрыты): машина не едет сама с «залипшей» кнопкой.
        public static void ReleaseAll()
        {
            Left = Right = Gas = Brake = Handbrake = false;
            item = camera = pause = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active = false;
            ReleaseAll();
        }
    }
}
