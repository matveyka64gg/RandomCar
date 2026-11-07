using RandomCar.Race;
using RandomCar.Track;
using UnityEngine;

namespace RandomCar.Maps
{
    /// Кадр облёта камеры: откуда смотрит, куда, сколько длится.
    [System.Serializable]
    public struct CameraShot
    {
        public Vector3 From;
        public Vector3 To;
        public Vector3 LookFrom;
        public Vector3 LookTo;
        public float Seconds;
        public float Fov;
    }

    /// Корень сцены карты: трасса, стартовые огни, решётка, точки камер меню и заставок.
    /// Сцена карты грузится аддитивно к сцене Game; свет, небо, туман и постобработка — из сцены карты.
    public sealed class MapDefinition : MonoBehaviour
    {
        public const int MaxGrid = 6;
        static readonly float[] GridDistances = { 8f, 15f, 22f, 29f, 36f, 43f };
        static readonly float[] GridLaterals = { -3f, 3f, -3f, 3f, -3f, 3f };

        [SerializeField] string key = "Hills";
        [SerializeField] string displayName = "Холмы Рандома";
        [SerializeField] RaceTrack track;
        [SerializeField] StartLights startLights;
        [SerializeField] Vector3 menuOrbitCenter;
        [SerializeField] float menuOrbitYaw;
        [SerializeField] CameraShot[] introShots = new CameraShot[0];
        [Tooltip("Свободное место у обочины для объектов событий (танк, ветровой конус), м от оси")]
        [SerializeField] float roadside = 25f;
        [Tooltip("Замены событий на этой карте: «rocket=debris» — вместо ракеты падают обломки (вес и правила те же)")]
        [SerializeField] string[] eventSubstitutions = new string[0];

        /// Событие, которое выпадает на этой карте вместо id (или сам id).
        public string Substitute(string id)
        {
            foreach (var s in eventSubstitutions)
            {
                var parts = s.Split('=');
                if (parts.Length == 2 && parts[0] == id) return parts[1];
            }
            return id;
        }

        public void SetEventSubstitutions(params string[] pairs) => eventSubstitutions = pairs;

        [Tooltip("Кругов в гонке на этой карте (длинные трассы — меньше)")]
        [SerializeField] int laps = RandomCar.Race.RaceManager.DefaultLaps;

        public int Laps => Mathf.Clamp(laps, 1, 9);

        public void SetLaps(int count) => laps = Mathf.Clamp(count, 1, 9);

        public string Key => key;
        public string DisplayName => displayName;
        public RaceTrack Track => track;
        public StartLights StartLights => startLights;
        public Vector3 MenuOrbitCenter => menuOrbitCenter;
        public float MenuOrbitYaw => menuOrbitYaw;
        public CameraShot[] IntroShots => introShots;
        public float Roadside => roadside;
        public void SetRoadside(float meters) => roadside = meters;

        public void Configure(string mapKey, string title, RaceTrack raceTrack, StartLights lights, Vector3 orbitCenter, float orbitYaw, CameraShot[] shots)
        {
            key = mapKey;
            displayName = title;
            track = raceTrack;
            startLights = lights;
            menuOrbitCenter = orbitCenter;
            menuOrbitYaw = orbitYaw;
            introShots = shots;
        }

        /// Место на стартовой решётке: за линией старта, в шахматном порядке.
        public static Pose GridPose(TrackPath path, int slot)
        {
            slot = Mathf.Clamp(slot, 0, MaxGrid - 1);
            float p = TrackPath.Wrap((path.Length - GridDistances[slot]) / path.Length);
            Vector3 pos = path.PointAt(p) + path.SideAt(p) * GridLaterals[slot];
            return new Pose(pos, Quaternion.LookRotation(path.DirectionAt(p)));
        }

        public static float GridDistance(int slot) => GridDistances[Mathf.Clamp(slot, 0, MaxGrid - 1)];
        public static float GridLateral(int slot) => GridLaterals[Mathf.Clamp(slot, 0, MaxGrid - 1)];

        public Pose GridPose(int slot) => GridPose(track.Path, slot);
    }
}
