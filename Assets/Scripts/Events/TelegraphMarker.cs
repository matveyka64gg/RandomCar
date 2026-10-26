using UnityEngine;

namespace RandomCar.Events
{
    /// Предупреждение на дороге: пульсирующее кольцо, полупрозрачная заливка и стрелка. Цвет — по типу события.
    public sealed class TelegraphMarker : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public static readonly Color DangerColor = new Color(1f, 0.29f, 0.11f);
        public static readonly Color GoodColor = new Color(0.14f, 0.88f, 0.78f);

        [SerializeField] Renderer ring;
        [SerializeField] Renderer fill;
        [SerializeField] Renderer arrow;

        MaterialPropertyBlock block;
        Color color = DangerColor;
        float radius = 2f;

        public void Configure(Renderer ringRenderer, Renderer fillRenderer, Renderer arrowRenderer)
        {
            ring = ringRenderer;
            fill = fillRenderer;
            arrow = arrowRenderer;
        }

        /// Показывает метку. Для прямоугольной зоны stretch растягивает её вдоль дороги.
        public void Show(Vector3 position, Vector3 forward, float zoneRadius, bool good, bool showArrow, float stretch = 1f)
        {
            gameObject.SetActive(true);
            radius = zoneRadius;
            color = good ? GoodColor : DangerColor;
            transform.SetPositionAndRotation(position + Vector3.up * 0.03f, Quaternion.LookRotation(forward, Vector3.up));
            transform.localScale = new Vector3(radius, 1f, radius * stretch);
            if (arrow != null) arrow.gameObject.SetActive(showArrow);
            Pulse(0f);
        }

        /// progress 0..1: к концу предупреждения пульсация чаще и ярче.
        public void Pulse(float progress)
        {
            block ??= new MaterialPropertyBlock();
            float speed = Mathf.Lerp(4f, 14f, progress);
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * speed);
            // Кольцо и стрелка ярче единицы: bloom даёт свечение, метку видно издалека и на солнце.
            Set(ring, color * Mathf.Lerp(1.6f, 2.6f, wave), Mathf.Lerp(0.75f, 1f, wave));
            Set(fill, color, Mathf.Lerp(0.2f, 0.42f, wave) * Mathf.Lerp(0.7f, 1f, progress));
            if (arrow != null && arrow.gameObject.activeSelf) Set(arrow, color * Mathf.Lerp(1.4f, 2.2f, wave), Mathf.Lerp(0.7f, 1f, wave));
            if (ring != null) ring.transform.localScale = Vector3.one * Mathf.Lerp(1.08f, 0.98f, wave);
        }

        public void Hide() => gameObject.SetActive(false);

        void Set(Renderer r, Color c, float alpha)
        {
            if (r == null) return;
            c.a = alpha;
            block.SetColor(BaseColor, c);
            r.SetPropertyBlock(block);
        }
    }
}
