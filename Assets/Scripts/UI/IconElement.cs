using UnityEngine;
using UnityEngine.UIElements;

namespace RandomCar.UI
{
    public enum Icon { Previous, Next, Play, Pause, RepeatAll, RepeatOne, RepeatOff, Shuffle, Note }

    /// Векторный значок, нарисованный Painter2D: не зависит от наличия символов в шрифте.
    /// Цвет задаётся в USS свойством --icon-color (меняется при наведении и фокусе).
    public sealed class IconElement : VisualElement
    {
        static readonly CustomStyleProperty<Color> IconColor = new CustomStyleProperty<Color>("--icon-color");

        Icon icon;
        Color color = new Color(0.95f, 0.94f, 0.91f);

        public Icon Icon
        {
            get => icon;
            set
            {
                if (icon == value) return;
                icon = value;
                MarkDirtyRepaint();
            }
        }

        public IconElement(Icon value)
        {
            icon = value;
            AddToClassList("icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(e =>
            {
                if (e.customStyle.TryGetValue(IconColor, out var c) && c != color)
                {
                    color = c;
                    MarkDirtyRepaint();
                }
            });
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float s = Mathf.Min(r.width, r.height);
            if (s <= 1f) return;
            var p = ctx.painter2D;
            p.fillColor = color;
            p.strokeColor = color;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            Vector2 o = new Vector2(r.x + (r.width - s) * 0.5f, r.y + (r.height - s) * 0.5f);
            Vector2 P(float x, float y) => o + new Vector2(x * s, y * s);

            switch (icon)
            {
                case Icon.Play:
                    Triangle(p, P(0.28f, 0.18f), P(0.28f, 0.82f), P(0.82f, 0.5f));
                    break;
                case Icon.Pause:
                    Rect(p, P(0.25f, 0.18f), P(0.42f, 0.82f));
                    Rect(p, P(0.58f, 0.18f), P(0.75f, 0.82f));
                    break;
                case Icon.Previous:
                    Rect(p, P(0.2f, 0.2f), P(0.31f, 0.8f));
                    Triangle(p, P(0.8f, 0.2f), P(0.8f, 0.8f), P(0.33f, 0.5f));
                    break;
                case Icon.Next:
                    Triangle(p, P(0.2f, 0.2f), P(0.2f, 0.8f), P(0.67f, 0.5f));
                    Rect(p, P(0.69f, 0.2f), P(0.8f, 0.8f));
                    break;
                case Icon.RepeatAll:
                case Icon.RepeatOne:
                case Icon.RepeatOff:
                    DrawRepeat(p, P, s);
                    break;
                case Icon.Shuffle:
                    p.lineWidth = s * 0.09f;
                    p.BeginPath();
                    p.MoveTo(P(0.14f, 0.3f));
                    p.BezierCurveTo(P(0.45f, 0.3f), P(0.5f, 0.7f), P(0.78f, 0.7f));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(0.14f, 0.7f));
                    p.BezierCurveTo(P(0.45f, 0.7f), P(0.5f, 0.3f), P(0.78f, 0.3f));
                    p.Stroke();
                    Triangle(p, P(0.74f, 0.18f), P(0.74f, 0.42f), P(0.9f, 0.3f));
                    Triangle(p, P(0.74f, 0.58f), P(0.74f, 0.82f), P(0.9f, 0.7f));
                    break;
                case Icon.Note:
                    p.BeginPath();
                    p.Arc(P(0.36f, 0.72f), s * 0.13f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                    Rect(p, P(0.44f, 0.16f), P(0.52f, 0.72f));
                    p.BeginPath();
                    p.MoveTo(P(0.44f, 0.16f));
                    p.LineTo(P(0.78f, 0.28f));
                    p.LineTo(P(0.78f, 0.4f));
                    p.LineTo(P(0.52f, 0.31f));
                    p.ClosePath();
                    p.Fill();
                    break;
            }
        }

        void DrawRepeat(Painter2D p, System.Func<float, float, Vector2> P, float s)
        {
            p.lineWidth = s * 0.09f;
            p.BeginPath();
            p.MoveTo(P(0.24f, 0.56f));
            p.LineTo(P(0.24f, 0.36f));
            p.LineTo(P(0.7f, 0.36f));
            p.Stroke();
            Triangle(p, P(0.68f, 0.22f), P(0.68f, 0.5f), P(0.86f, 0.36f));
            p.BeginPath();
            p.MoveTo(P(0.76f, 0.44f));
            p.LineTo(P(0.76f, 0.66f));
            p.LineTo(P(0.3f, 0.66f));
            p.Stroke();
            Triangle(p, P(0.32f, 0.52f), P(0.32f, 0.8f), P(0.14f, 0.66f));
            if (icon == Icon.RepeatOne)
            {
                p.fillColor = color;
                Rect(p, P(0.46f, 0.43f), P(0.55f, 0.6f));
            }
            else if (icon == Icon.RepeatOff)
            {
                p.lineWidth = s * 0.08f;
                p.BeginPath();
                p.MoveTo(P(0.14f, 0.86f));
                p.LineTo(P(0.86f, 0.14f));
                p.Stroke();
            }
        }

        static void Triangle(Painter2D p, Vector2 a, Vector2 b, Vector2 c)
        {
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.LineTo(c);
            p.ClosePath();
            p.Fill();
        }

        static void Rect(Painter2D p, Vector2 min, Vector2 max)
        {
            p.BeginPath();
            p.MoveTo(min);
            p.LineTo(new Vector2(max.x, min.y));
            p.LineTo(max);
            p.LineTo(new Vector2(min.x, max.y));
            p.ClosePath();
            p.Fill();
        }
    }
}
