using UnityEngine;

namespace RandomCar.Car
{
    /// Следы шин: один динамический меш с кольцевым буфером отрезков. Старые следы заменяются новыми,
    /// память не растёт. Объект заранее лежит в сцене, меш только обновляется.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SkidMarks : MonoBehaviour
    {
        const int MaxSegments = 1600;
        /// Ширина следа, если шина неизвестна, м.
        public const float DefaultWidth = 0.24f;
        const float MinStep = 0.35f;
        const float Lift = 0.02f;

        public static SkidMarks Instance { get; private set; }

        Mesh mesh;
        Vector3[] vertices;
        Color32[] colors;
        Vector2[] uvs;
        int[] triangles;
        int next;
        bool dirty;

        /// Последняя точка следа каждого колеса (ключ — индекс, выданный NewTrack).
        Vector3[] lastPoint = new Vector3[64];
        Vector3[] lastLeft = new Vector3[64];
        Vector3[] lastRight = new Vector3[64];
        Color32[] lastColor = new Color32[64];
        bool[] active = new bool[64];
        int tracks;

        void Awake()
        {
            Instance = this;
            mesh = new Mesh { name = "SkidMarks" };
            mesh.MarkDynamic();
            vertices = new Vector3[MaxSegments * 4];
            colors = new Color32[MaxSegments * 4];
            uvs = new Vector2[MaxSegments * 4];
            triangles = new int[MaxSegments * 6];
            for (int i = 0; i < MaxSegments; i++)
            {
                int v = i * 4, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 2; triangles[t + 4] = v + 3; triangles[t + 5] = v + 1;
                uvs[v] = new Vector2(0f, 0f); uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(0f, 1f); uvs[v + 3] = new Vector2(1f, 1f);
            }
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 5000f);
            GetComponent<MeshFilter>().sharedMesh = mesh;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (mesh != null) Destroy(mesh);
        }

        /// Новый след для колеса: возвращает ключ для Add/End.
        public int NewTrack() => tracks < active.Length ? tracks++ : active.Length - 1;

        /// Продолжить след колеса в точке. intensity 0..1 — непрозрачность, width — ширина пятна шины, м.
        public void Add(int track, Vector3 point, Vector3 normal, Vector3 forward, float intensity, Color color, float width = DefaultWidth)
        {
            Vector3 right = Vector3.Cross(normal, forward).normalized * (Mathf.Clamp(width, 0.08f, 0.5f) * 0.5f);
            point += normal * Lift;
            if (!active[track])
            {
                active[track] = true;
                lastPoint[track] = point;
                lastLeft[track] = point - right;
                lastRight[track] = point + right;
                lastColor[track] = new Color32(0, 0, 0, 0);
                return;
            }
            if ((point - lastPoint[track]).sqrMagnitude < MinStep * MinStep) return;

            color.a *= Mathf.Clamp01(intensity);
            Color32 c = color;
            int v = next * 4;
            vertices[v] = lastLeft[track];
            vertices[v + 1] = lastRight[track];
            vertices[v + 2] = point - right;
            vertices[v + 3] = point + right;
            colors[v] = colors[v + 1] = lastColor[track];
            colors[v + 2] = colors[v + 3] = c;
            next = (next + 1) % MaxSegments;
            dirty = true;

            lastPoint[track] = point;
            lastLeft[track] = point - right;
            lastRight[track] = point + right;
            lastColor[track] = c;
        }

        /// Колесо перестало скользить: след обрывается.
        public void End(int track) => active[track] = false;

        /// Стереть всё (рестарт гонки).
        public void Clear()
        {
            System.Array.Clear(vertices, 0, vertices.Length);
            System.Array.Clear(active, 0, active.Length);
            dirty = true;
        }

        void LateUpdate()
        {
            if (!dirty) return;
            dirty = false;
            mesh.vertices = vertices;
            mesh.colors32 = colors;
        }
    }
}
