using UnityEngine;

namespace RandomCar.Track
{
    /// Описание трассы карты: контрольные точки кривой (с высотой), плотность сэмплов, сэмпл стартовой линии,
    /// полуширина дороги. Один ассет на карту; геометрию по нему строит редактор, ИИ и круги считаются по нему же.
    [CreateAssetMenu(menuName = "RandomCar/Track Definition")]
    public sealed class TrackDefinition : ScriptableObject
    {
        public Vector3[] ControlPoints = new Vector3[0];
        public int SamplesPerSegment = 24;
        public int StartSample;
        [Tooltip("Половина ширины дороги, м")] public float HalfWidth = 6f;

        TrackPath path;
        int pathHash;

        /// Трасса с прогрессом 0 на стартовой линии. Кэшируется до изменения точек.
        public TrackPath Path
        {
            get
            {
                int hash = Hash();
                if (path == null || hash != pathHash)
                {
                    path = new TrackPath(ControlPoints, SamplesPerSegment, StartSample);
                    pathHash = hash;
                }
                path.HalfWidth = HalfWidth;
                return path;
            }
        }

        int Hash()
        {
            unchecked
            {
                int h = SamplesPerSegment * 31 + StartSample;
                foreach (var p in ControlPoints) h = h * 31 + p.GetHashCode();
                return h;
            }
        }
    }
}
