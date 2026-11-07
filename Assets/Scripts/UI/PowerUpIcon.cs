using RandomCar.PowerUps;
using UnityEngine;
using UnityEngine.UIElements;

namespace RandomCar.UI
{
    /// Значок бонуса, нарисованный Painter2D (не зависит от шрифта): ракета, ракета-гроза лидеров, пулемёт, турбо,
    /// пружина-прыжок, молния, щит, мина, клаксон; пусто — ящик «?».
    public sealed class PowerUpIcon : VisualElement
    {
        PowerUpKind kind;
        Color accent = Color.white;

        public PowerUpKind Kind
        {
            get => kind;
            set
            {
                if (kind == value) return;
                kind = value;
                MarkDirtyRepaint();
            }
        }

        public Color Accent
        {
            get => accent;
            set
            {
                if (accent == value) return;
                accent = value;
                MarkDirtyRepaint();
            }
        }

        public PowerUpIcon()
        {
            AddToClassList("item-icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float s = Mathf.Min(r.width, r.height);
            if (s <= 1f) return;
            var p = ctx.painter2D;
            Vector2 o = new Vector2(r.x + (r.width - s) * 0.5f, r.y + (r.height - s) * 0.5f);
            Vector2 P(float x, float y) => o + new Vector2(x * s, y * s);
            var white = new Color(0.97f, 0.96f, 0.92f);
            var dark = new Color(0.12f, 0.12f, 0.15f);
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            switch (kind)
            {
                case PowerUpKind.None:
                    p.fillColor = new Color(1f, 1f, 1f, 0.12f);
                    RoundRect(p, P(0.18f, 0.18f), P(0.82f, 0.82f));
                    p.fillColor = new Color(1f, 1f, 1f, 0.35f);
                    Question(p, P, s);
                    break;
                case PowerUpKind.HomingRocket:
                case PowerUpKind.LeaderRocket:
                    bool leader = kind == PowerUpKind.LeaderRocket;
                    p.fillColor = leader ? new Color(1f, 0.82f, 0.2f) : new Color(1f, 0.85f, 0.25f);
                    Poly(p, P(0.18f, 0.62f), P(0.32f, 0.5f), P(0.32f, 0.78f), P(0.18f, 0.86f));
                    Poly(p, P(0.38f, 0.82f), P(0.5f, 0.68f), P(0.22f, 0.68f), P(0.14f, 0.82f));
                    p.fillColor = accent;
                    Poly(p, P(0.28f, 0.62f), P(0.62f, 0.28f), P(0.72f, 0.38f), P(0.38f, 0.72f));
                    p.fillColor = white;
                    Poly(p, P(0.62f, 0.28f), P(0.86f, 0.14f), P(0.72f, 0.38f));
                    p.fillColor = new Color(1f, 0.55f, 0.15f);
                    Poly(p, P(0.28f, 0.62f), P(0.1f, 0.9f), P(0.38f, 0.72f));
                    if (leader)
                    {
                        // Корона над ракетой: «на лидера».
                        p.fillColor = new Color(1f, 0.82f, 0.2f);
                        Poly(p, P(0.52f, 0.06f), P(0.58f, 0.18f), P(0.64f, 0.08f), P(0.7f, 0.18f), P(0.76f, 0.06f), P(0.78f, 0.24f), P(0.5f, 0.24f));
                    }
                    break;
                case PowerUpKind.MachineGun:
                    p.fillColor = dark;
                    RoundRect(p, P(0.2f, 0.5f), P(0.56f, 0.72f));
                    p.fillColor = accent;
                    Rect(p, P(0.5f, 0.42f), P(0.9f, 0.5f));
                    Rect(p, P(0.5f, 0.56f), P(0.9f, 0.64f));
                    p.fillColor = new Color(0.45f, 0.5f, 0.25f);
                    Rect(p, P(0.24f, 0.72f), P(0.42f, 0.84f));
                    p.fillColor = new Color(1f, 0.6f, 0.2f);
                    Poly(p, P(0.9f, 0.4f), P(0.98f, 0.46f), P(0.9f, 0.52f));
                    Poly(p, P(0.9f, 0.54f), P(0.98f, 0.6f), P(0.9f, 0.66f));
                    break;
                case PowerUpKind.Turbo:
                    p.fillColor = accent;
                    Poly(p, P(0.12f, 0.3f), P(0.55f, 0.3f), P(0.55f, 0.18f), P(0.9f, 0.5f), P(0.55f, 0.82f), P(0.55f, 0.7f), P(0.12f, 0.7f));
                    p.fillColor = new Color(1f, 0.95f, 0.5f);
                    Poly(p, P(0.18f, 0.42f), P(0.5f, 0.42f), P(0.5f, 0.58f), P(0.18f, 0.58f));
                    break;
                case PowerUpKind.SuperJump:
                    p.strokeColor = white;
                    p.lineWidth = s * 0.07f;
                    p.BeginPath();
                    p.MoveTo(P(0.3f, 0.82f));
                    for (int k = 0; k < 5; k++)
                    {
                        float y = 0.76f - k * 0.1f;
                        p.LineTo(P(k % 2 == 0 ? 0.7f : 0.3f, y));
                    }
                    p.Stroke();
                    p.fillColor = accent;
                    Poly(p, P(0.5f, 0.06f), P(0.76f, 0.3f), P(0.24f, 0.3f));
                    p.fillColor = dark;
                    Rect(p, P(0.22f, 0.82f), P(0.78f, 0.9f));
                    break;
                case PowerUpKind.Shock:
                    p.fillColor = accent;
                    Poly(p, P(0.56f, 0.06f), P(0.24f, 0.56f), P(0.48f, 0.56f), P(0.38f, 0.94f), P(0.78f, 0.4f), P(0.54f, 0.4f), P(0.66f, 0.06f));
                    break;
                case PowerUpKind.Shield:
                    p.fillColor = accent;
                    Poly(p, P(0.5f, 0.08f), P(0.84f, 0.2f), P(0.8f, 0.56f), P(0.5f, 0.92f), P(0.2f, 0.56f), P(0.16f, 0.2f));
                    p.fillColor = new Color(1f, 1f, 1f, 0.55f);
                    Poly(p, P(0.5f, 0.16f), P(0.74f, 0.25f), P(0.71f, 0.52f), P(0.5f, 0.8f));
                    break;
                case PowerUpKind.Mine:
                    p.fillColor = dark;
                    Circle(p, P(0.5f, 0.6f), s * 0.28f);
                    p.strokeColor = dark;
                    p.lineWidth = s * 0.07f;
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * Mathf.PI / 3f;
                        p.BeginPath();
                        p.MoveTo(P(0.5f + Mathf.Cos(a) * 0.26f, 0.6f + Mathf.Sin(a) * 0.26f));
                        p.LineTo(P(0.5f + Mathf.Cos(a) * 0.4f, 0.6f + Mathf.Sin(a) * 0.4f));
                        p.Stroke();
                    }
                    p.fillColor = accent;
                    Circle(p, P(0.5f, 0.28f), s * 0.08f);
                    break;
                case PowerUpKind.Horn:
                    p.fillColor = accent;
                    Poly(p, P(0.12f, 0.4f), P(0.32f, 0.4f), P(0.62f, 0.18f), P(0.62f, 0.82f), P(0.32f, 0.6f), P(0.12f, 0.6f));
                    p.strokeColor = white;
                    p.lineWidth = s * 0.06f;
                    for (int k = 0; k < 2; k++)
                    {
                        float rad = s * (0.14f + k * 0.12f);
                        p.BeginPath();
                        p.Arc(P(0.64f, 0.5f), rad, Angle.Degrees(-45f), Angle.Degrees(45f));
                        p.Stroke();
                    }
                    break;
            }
        }

        static void Question(Painter2D p, System.Func<float, float, Vector2> P, float s)
        {
            p.strokeColor = p.fillColor;
            p.lineWidth = s * 0.1f;
            p.BeginPath();
            p.Arc(P(0.5f, 0.38f), s * 0.14f, Angle.Degrees(180f), Angle.Degrees(400f));
            p.LineTo(P(0.5f, 0.6f));
            p.Stroke();
            Circle(p, P(0.5f, 0.73f), s * 0.06f);
        }

        static void Circle(Painter2D p, Vector2 c, float radius)
        {
            p.BeginPath();
            p.Arc(c, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
        }

        static void Rect(Painter2D p, Vector2 min, Vector2 max) => Poly(p, min, new Vector2(max.x, min.y), max, new Vector2(min.x, max.y));

        static void RoundRect(Painter2D p, Vector2 min, Vector2 max) => Rect(p, min, max);

        static void Poly(Painter2D p, params Vector2[] points)
        {
            p.BeginPath();
            p.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) p.LineTo(points[i]);
            p.ClosePath();
            p.Fill();
        }
    }
}
