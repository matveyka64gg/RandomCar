using System;
using RandomCar.Audio;
using UnityEngine.UIElements;

namespace RandomCar.UI
{
    /// Общие строительные блоки интерфейса: элементы с классами USS, кнопки меню со звуком и фокусом.
    public static class UiKit
    {
        public static VisualElement El(string cls, VisualElement parent = null)
        {
            var e = new VisualElement();
            foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries)) e.AddToClassList(c);
            parent?.Add(e);
            return e;
        }

        public static Label Text(string text, string cls, VisualElement parent)
        {
            var l = new Label(text);
            foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries)) l.AddToClassList(c);
            parent.Add(l);
            return l;
        }

        public static Button MenuButton(string text, Action action, VisualElement parent, string extra = "")
        {
            var b = new Button(() =>
            {
                AudioDirector.Instance?.PlayUiClick();
                action();
            }) { text = text };
            b.AddToClassList("menu-button");
            foreach (var c in extra.Split(' ', StringSplitOptions.RemoveEmptyEntries)) b.AddToClassList(c);
            b.RegisterCallback<FocusInEvent>(_ => AudioDirector.Instance?.PlayUiMove());
            b.RegisterCallback<PointerEnterEvent>(_ => b.Focus());
            parent.Add(b);
            return b;
        }

        public static Button ChoiceButton(string text, Action action, VisualElement parent, out Label value)
        {
            var b = MenuButton("", action, parent, "secondary");
            b.AddToClassList("choice");
            Text(text, "", b);
            value = Text("", "choice-value", b);
            return b;
        }

        /// Строка выбора «◀ значение ▶»: стрелки и клик листают варианты.
        public static Label Stepper(string title, Action<int> step, VisualElement parent)
        {
            var row = El("stepper", parent);
            Text(title, "stepper-title", row);
            MenuButton("◀", () => step(-1), row, "stepper-arrow");
            var value = Text("", "stepper-value", row);
            MenuButton("▶", () => step(1), row, "stepper-arrow");
            return value;
        }
    }
}
