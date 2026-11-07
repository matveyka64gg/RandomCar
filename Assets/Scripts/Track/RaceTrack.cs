using UnityEngine;

namespace RandomCar.Track
{
    /// Трасса в сцене карты. Геометрия и декор уже сохранены в сцене, здесь — математическая ось для ИИ,
    /// кругов и событий.
    public sealed class RaceTrack : MonoBehaviour
    {
        [SerializeField] TrackDefinition definition;

        public TrackDefinition Definition => definition;
        public TrackPath Path => definition.Path;
        public float HalfWidth => definition.HalfWidth;

        public void Configure(TrackDefinition trackDefinition) => definition = trackDefinition;

        void OnDrawGizmosSelected()
        {
            if (definition == null) return;
            var p = Path;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < p.Count; i++)
                Gizmos.DrawLine(p.Point(i) + Vector3.up * 0.3f, p.Point((i + 1) % p.Count) + Vector3.up * 0.3f);
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(p.Point(0) + Vector3.up, 1f);
        }
    }
}
