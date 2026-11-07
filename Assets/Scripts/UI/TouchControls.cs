using System;
using System.Collections.Generic;
using RandomCar.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static RandomCar.UI.UiKit;

namespace RandomCar.UI
{
    /// Экранные кнопки для телефона (поверх HUD гонки): слева руль ◀ ▶, справа газ и тормоз, над ними бонус и ручник,
    /// вверху справа пауза и вид камеры. Мультитач: у каждой кнопки свой палец (захват указателя по pointerId).
    /// Размеры — в процентах экрана, края — с запасом под вырез камеры; кнопки полупрозрачные и не закрывают центр.
    public sealed class TouchControls
    {
        readonly VisualElement root;
        readonly List<(VisualElement button, Action<bool> set)> holds = new List<(VisualElement, Action<bool>)>();

        public VisualElement Root => root;

        public TouchControls(VisualElement parent)
        {
            root = El("touch-controls", parent);
            root.pickingMode = PickingMode.Ignore;

            var left = El("touch-cluster touch-left", root);
            left.pickingMode = PickingMode.Ignore;
            Hold(left, "◀", "touch-steer", v => TouchInput.Left = v);
            Hold(left, "▶", "touch-steer", v => TouchInput.Right = v);

            var right = El("touch-cluster touch-right", root);
            right.pickingMode = PickingMode.Ignore;
            var upper = El("touch-row", right);
            upper.pickingMode = PickingMode.Ignore;
            Tap(upper, "БОНУС", "touch-small touch-item", TouchInput.PressItem);
            Hold(upper, "РУЧНИК", "touch-small", v => TouchInput.Handbrake = v);
            var lower = El("touch-row", right);
            lower.pickingMode = PickingMode.Ignore;
            Hold(lower, "ТОРМОЗ", "touch-pedal touch-brake", v => TouchInput.Brake = v);
            Hold(lower, "ГАЗ", "touch-pedal touch-gas", v => TouchInput.Gas = v);

            var top = El("touch-top", root);
            top.pickingMode = PickingMode.Ignore;
            Tap(top, "ВИД", "touch-mini", TouchInput.PressCamera);
            Tap(top, "II", "touch-mini touch-pause", TouchInput.PressPause);
        }

        public bool Visible
        {
            set
            {
                root.EnableInClassList("hidden", !value);
                TouchInput.Active = value;
                if (!value) ReleaseAll();
            }
        }

        public void ReleaseAll()
        {
            foreach (var (button, set) in holds)
            {
                set(false);
                button.RemoveFromClassList("pressed");
            }
            TouchInput.ReleaseAll();
        }

        /// Кнопка, которую держат: нажата, пока палец на ней (или пока не ушёл с экрана).
        void Hold(VisualElement parent, string text, string cls, Action<bool> set)
        {
            var b = MakeButton(parent, text, cls);
            holds.Add((b, set));
            b.RegisterCallback<PointerDownEvent>(e =>
            {
                b.CapturePointer(e.pointerId);
                b.AddToClassList("pressed");
                set(true);
                e.StopPropagation();
            });
            void Release(int pointerId)
            {
                if (b.HasPointerCapture(pointerId)) b.ReleasePointer(pointerId);
                b.RemoveFromClassList("pressed");
                set(false);
            }
            b.RegisterCallback<PointerUpEvent>(e => Release(e.pointerId));
            b.RegisterCallback<PointerCancelEvent>(e => Release(e.pointerId));
            b.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                b.RemoveFromClassList("pressed");
                set(false);
            });
        }

        /// Кнопка-нажатие (бонус, вид, пауза).
        void Tap(VisualElement parent, string text, string cls, Action press)
        {
            var b = MakeButton(parent, text, cls);
            b.RegisterCallback<PointerDownEvent>(e =>
            {
                b.AddToClassList("pressed");
                press();
                e.StopPropagation();
            });
            b.RegisterCallback<PointerUpEvent>(_ => b.RemoveFromClassList("pressed"));
            b.RegisterCallback<PointerLeaveEvent>(_ => b.RemoveFromClassList("pressed"));
        }

        static VisualElement MakeButton(VisualElement parent, string text, string cls)
        {
            var b = El("touch-button " + cls, parent);
            b.pickingMode = PickingMode.Position;
            var label = Text(text, "touch-label", b);
            label.pickingMode = PickingMode.Ignore;
            return b;
        }
    }
}
