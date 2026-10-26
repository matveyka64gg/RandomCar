using UnityEngine;

namespace RandomCar.Track
{
    /// Трасса в сцене. Геометрия и декор уже сохранены в сцене, здесь только математическая ось для ИИ и счёта кругов.
    public sealed class RaceTrack : MonoBehaviour
    {
        TrackPath path;

        public TrackPath Path => path ??= TrackLayout.CreatePath();

        void OnDrawGizmosSelected()
        {
            var p = Path;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < p.Count; i++)
                Gizmos.DrawLine(p.Point(i) + Vector3.up * 0.3f, p.Point((i + 1) % p.Count) + Vector3.up * 0.3f);
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(p.Point(0) + Vector3.up, 1f);
        }
    }
}
