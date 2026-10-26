using System.Collections.Generic;
using RandomCar.Track;
using UnityEngine;
using UnityEngine.Rendering;

namespace RandomCar.EditorTools
{
    /// Генерация мешей трассы в редакторе. Результат сохраняется ассетами и кладётся в сцену один раз,
    /// в игре ничего не строится.
    public sealed class TrackMeshBuilder
    {
        public const float HalfWidth = 6f;
        public const float KerbWidth = 1.15f;
        public const float RailBase = 11f;
        public const float GravelWidth = 10f;
        public const float WallZoneBefore = 75f;
        public const float WallZoneAfter = 95f;

        const float KerbRadius = 100f;
        const float GravelRadius = 70f;

        readonly TrackPath path;
        readonly int n;
        readonly float[] curvature;
        readonly bool[] kerb;
        readonly float[] gravelLeft;
        readonly float[] gravelRight;

        public TrackMeshBuilder(TrackPath trackPath)
        {
            path = trackPath;
            n = path.Count;
            curvature = new float[n];
            for (int i = 0; i < n; i++) curvature[i] = path.CurvatureAt(i, 4);

            kerb = Dilate(Mask(i => Mathf.Abs(curvature[i]) > 1f / KerbRadius), 5);
            var leftMask = Dilate(Mask(i => curvature[i] < -1f / GravelRadius), 6);   // правый поворот: снаружи слева
            var rightMask = Dilate(Mask(i => curvature[i] > 1f / GravelRadius), 6);   // левый поворот: снаружи справа
            gravelLeft = Ramp(leftMask, 10);
            gravelRight = Ramp(rightMask, 10);
        }

        public TrackPath Path => path;
        public int Count => n;
        public float Curvature(int i) => curvature[(i % n + n) % n];
        public float GravelAmount(int i, int side) => side > 0 ? gravelRight[(i % n + n) % n] : gravelLeft[(i % n + n) % n];

        /// Расстояние от оси до отбойника в сэмпле: дальше там, где гравий.
        public float RailOffset(int i, int side) => RailBase + 1.5f + GravelWidth * GravelAmount(i, side);

        public bool InWallZone(int i)
        {
            float d = path.DistanceAt(i);
            return d < WallZoneAfter || d > path.Length - WallZoneBefore;
        }

        // ---------- меши ----------

        public Mesh Road()
        {
            var b = new MeshBuilder("Road", 1);
            int first = -1, prev = -1;
            for (int i = 0; i <= n; i++)
            {
                int k = i % n;
                Vector3 p = path.Point(k);
                Vector3 r = path.RightAt(k);
                float v = (i == n ? path.Length : path.DistanceAt(k)) / 8f;
                int a = b.Vertex(p - r * (HalfWidth + 0.05f), new Vector2(0f, v));
                b.Vertex(p + r * (HalfWidth + 0.05f), new Vector2(1.5f, v));
                if (prev >= 0) b.QuadUp(0, prev, prev + 1, a + 1, a);
                if (first < 0) first = a;
                prev = a;
            }
            return b.Build();
        }

        public Mesh Markings(float[] gridDistances, float[] gridLaterals)
        {
            var b = new MeshBuilder("Markings", 1);
            const float y = 0.012f;
            // Сплошные краевые линии
            foreach (float side in new[] { -1f, 1f })
                Strip(b, 0, 0f, path.Length, side * (HalfWidth - 0.45f), 0.2f, y, 1.5f);
            // Прерывистая ось (на стартовой прямой у решётки и линии не рисуем)
            for (float d = 30f; d < path.Length - 60f; d += 12f)
                Strip(b, 0, d, d + 4f, 0f, 0.18f, y, 1f);
            // Разметка мест на решётке: поперечная черта и короткие «уши»
            for (int s = 0; s < gridDistances.Length; s++)
            {
                float d = path.Length - gridDistances[s];
                float x = gridLaterals[s];
                Cross(b, 0, d + 2.45f, x, 2.4f, 0.16f, y);
                foreach (float side in new[] { -1f, 1f })
                    Strip(b, 0, d + 1.1f, d + 2.45f, x + side * 1.2f, 0.14f, y, 0.5f);
            }
            return b.Build();
        }

        /// Шахматная стартовая линия: две подсетки (белый, чёрный).
        public Mesh StartLine()
        {
            var b = new MeshBuilder("StartLine", 2);
            const float y = 0.014f;
            int columns = 12;
            float cell = 2f * HalfWidth / columns;
            for (int row = 0; row < 2; row++)
            {
                float d0 = -0.9f + row * 0.9f;
                for (int c = 0; c < columns; c++)
                {
                    float x0 = -HalfWidth + c * cell;
                    int sub = (c + row) % 2;
                    Patch(b, sub, d0, d0 + 0.9f, x0, x0 + cell, y);
                }
            }
            return b.Build();
        }

        /// Бордюры с профилем на поворотах, красные и белые блоки по 2.4 м.
        public Mesh Kerbs()
        {
            var b = new MeshBuilder("Kerbs", 2);
            float[] lateral = { 0f, 0.55f, KerbWidth };
            float[] height = { 0.0f, 0.07f, 0.025f };
            foreach (int side in new[] { -1, 1 })
            {
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    if (!kerb[i] || !kerb[j]) continue;
                    float ti = RunTaper(kerb, i), tj = RunTaper(kerb, j);
                    float d = path.DistanceAt(i);
                    int sub = Mathf.FloorToInt(d / 2.4f) % 2;
                    for (int s = 0; s < 2; s++)
                    {
                        Vector3 a0 = KerbPoint(i, side, HalfWidth - 0.05f + lateral[s], height[s] * ti);
                        Vector3 a1 = KerbPoint(i, side, HalfWidth - 0.05f + lateral[s + 1], height[s + 1] * ti);
                        Vector3 b0 = KerbPoint(j, side, HalfWidth - 0.05f + lateral[s], height[s] * tj);
                        Vector3 b1 = KerbPoint(j, side, HalfWidth - 0.05f + lateral[s + 1], height[s + 1] * tj);
                        b.QuadUpPoints(sub, a0, a1, b1, b0);
                    }
                }
            }
            return b.Build(recalcNormals: true);
        }

        Vector3 KerbPoint(int i, int side, float offset, float h) =>
            path.Point(i) + path.RightAt(i) * (side * offset) + Vector3.up * (0.004f + h);

        /// Гравийные зоны безопасности снаружи крутых поворотов.
        public Mesh Gravel()
        {
            var b = new MeshBuilder("Gravel", 1);
            foreach (int side in new[] { -1, 1 })
            {
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    float gi = GravelAmount(i, side), gj = GravelAmount(j, side);
                    if (gi <= 0.001f && gj <= 0.001f) continue;
                    float inner = HalfWidth + KerbWidth + 0.3f;
                    Vector3 a0 = Edge(i, side, inner, -0.006f);
                    Vector3 a1 = Edge(i, side, inner + GravelWidth * gi + 0.6f, -0.006f);
                    Vector3 b0 = Edge(j, side, inner, -0.006f);
                    Vector3 b1 = Edge(j, side, inner + GravelWidth * gj + 0.6f, -0.006f);
                    b.QuadUpPoints(0, a0, a1, b1, b0, worldUv: 6f);
                }
            }
            return b.Build();
        }

        Vector3 Edge(int i, int side, float offset, float y) => path.Point(i) + path.RightAt(i) * (side * offset) + Vector3.up * y;

        /// Отбойник: стальная полоса на высоте 0.5–0.85 м вдоль всей трассы, кроме стартовой прямой.
        public Mesh Rails(int side, List<Pose> posts)
        {
            var b = new MeshBuilder("Rail_" + (side > 0 ? "Right" : "Left"), 1);
            float lastPost = -10f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if (InWallZone(i) || InWallZone(j)) continue;
                Vector3 pi = path.Point(i) + path.RightAt(i) * (side * RailOffset(i, side));
                Vector3 pj = path.Point(j) + path.RightAt(j) * (side * RailOffset(j, side));
                Box(b, 0, pi, pj, 0.5f, 0.85f, 0.09f);

                float d = path.DistanceAt(i);
                if (d - lastPost >= 4f)
                {
                    lastPost = d;
                    Vector3 back = path.RightAt(i) * (side * 0.12f);
                    posts.Add(new Pose(pi + back, Quaternion.LookRotation(path.TangentAt(i), Vector3.up)));
                }
            }
            return b.Build(recalcNormals: true);
        }

        /// Бетонная стена с оранжевой полосой вдоль стартовой прямой.
        public Mesh Walls()
        {
            var b = new MeshBuilder("Walls", 2);
            foreach (int side in new[] { -1, 1 })
            {
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    if (!InWallZone(i) || !InWallZone(j)) continue;
                    Vector3 pi = path.Point(i) + path.RightAt(i) * (side * (RailBase + 0.6f));
                    Vector3 pj = path.Point(j) + path.RightAt(j) * (side * (RailBase + 0.6f));
                    Box(b, 0, pi, pj, 0f, 0.82f, 0.45f);
                    Box(b, 1, pi, pj, 0.82f, 1.0f, 0.46f);
                }
            }
            return b.Build(recalcNormals: true);
        }

        public static Mesh Ground(float size, float y)
        {
            var b = new MeshBuilder("Ground", 1);
            float h = size / 2f;
            b.QuadUpPoints(0, new Vector3(-h, y, -h), new Vector3(h, y, -h), new Vector3(h, y, h), new Vector3(-h, y, h), worldUv: 12f);
            return b.Build();
        }

        // ---------- помощники ----------

        void Strip(MeshBuilder b, int sub, float d0, float d1, float lateral, float width, float y, float step)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt((d1 - d0) / step));
            for (int k = 0; k < count; k++)
            {
                float da = d0 + (d1 - d0) * k / count;
                float db = d0 + (d1 - d0) * (k + 1) / count;
                Vector3 a0 = At(da, lateral - width / 2f, y), a1 = At(da, lateral + width / 2f, y);
                Vector3 b0 = At(db, lateral - width / 2f, y), b1 = At(db, lateral + width / 2f, y);
                b.QuadUpPoints(sub, a0, a1, b1, b0);
            }
        }

        void Cross(MeshBuilder b, int sub, float d, float lateral, float length, float depth, float y)
        {
            Vector3 a0 = At(d - depth / 2f, lateral - length / 2f, y), a1 = At(d - depth / 2f, lateral + length / 2f, y);
            Vector3 b0 = At(d + depth / 2f, lateral - length / 2f, y), b1 = At(d + depth / 2f, lateral + length / 2f, y);
            b.QuadUpPoints(sub, a0, a1, b1, b0);
        }

        void Patch(MeshBuilder b, int sub, float d0, float d1, float x0, float x1, float y)
        {
            b.QuadUpPoints(sub, At(d0, x0, y), At(d0, x1, y), At(d1, x1, y), At(d1, x0, y));
        }

        Vector3 At(float distance, float lateral, float y)
        {
            float p = distance / path.Length;
            return path.PointAt(p) + path.SideAt(p) * lateral + Vector3.up * y;
        }

        /// Брус вдоль отрезка a-b: высота от y0 до y1, толщина t. Отдельные вершины на грань: плоское освещение.
        static void Box(MeshBuilder b, int sub, Vector3 a, Vector3 c, float y0, float y1, float t)
        {
            Vector3 dir = c - a;
            dir.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * (t / 2f);
            Vector3 A0 = a - side + Vector3.up * y0, A1 = a + side + Vector3.up * y0;
            Vector3 A2 = a + side + Vector3.up * y1, A3 = a - side + Vector3.up * y1;
            Vector3 C0 = c - side + Vector3.up * y0, C1 = c + side + Vector3.up * y0;
            Vector3 C2 = c + side + Vector3.up * y1, C3 = c - side + Vector3.up * y1;
            b.QuadFacing(sub, A3, A2, C2, C3, Vector3.up);
            b.QuadFacing(sub, A1, C1, C2, A2, side);
            b.QuadFacing(sub, A0, A3, C3, C0, -side);
            b.QuadFacing(sub, A0, C0, C1, A1, Vector3.down);
        }

        bool[] Mask(System.Func<int, bool> test)
        {
            var m = new bool[n];
            for (int i = 0; i < n; i++) m[i] = test(i);
            return m;
        }

        bool[] Dilate(bool[] m, int radius)
        {
            var r = new bool[n];
            for (int i = 0; i < n; i++)
                for (int k = -radius; k <= radius && !r[i]; k++)
                    r[i] = m[((i + k) % n + n) % n];
            // Короткие куски убираем: бордюр или гравий меньше 8 сэмплов выглядит как мусор.
            for (int i = 0; i < n; i++)
            {
                if (!r[i] || r[(i - 1 + n) % n]) continue;
                int len = 0;
                while (len < n && r[(i + len) % n]) len++;
                if (len < 8) for (int k = 0; k < len; k++) r[(i + k) % n] = false;
            }
            return r;
        }

        float[] Ramp(bool[] m, int length)
        {
            var r = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (!m[i]) continue;
                int before = 0, after = 0;
                while (before < length && m[((i - before - 1) % n + n) % n]) before++;
                while (after < length && m[(i + after + 1) % n]) after++;
                float t = Mathf.Min(before, after) / (float)length;
                r[i] = Mathf.SmoothStep(0f, 1f, t);
            }
            return r;
        }

        float RunTaper(bool[] m, int i)
        {
            int before = 0, after = 0;
            while (before < 3 && m[((i - before - 1) % n + n) % n]) before++;
            while (after < 3 && m[(i + after + 1) % n]) after++;
            return Mathf.Min(before, after) / 3f;
        }

        /// Накопитель меша с несколькими подсетками.
        sealed class MeshBuilder
        {
            readonly string name;
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<int>[] triangles;

            public MeshBuilder(string meshName, int submeshes)
            {
                name = meshName;
                triangles = new List<int>[submeshes];
                for (int i = 0; i < submeshes; i++) triangles[i] = new List<int>();
            }

            public int Vertex(Vector3 p, Vector2 uv)
            {
                vertices.Add(p);
                uvs.Add(uv);
                normals.Add(Vector3.up);
                return vertices.Count - 1;
            }

            /// Четырёхугольник по индексам, развёрнутый вверх.
            public void QuadUp(int sub, int a, int b, int c, int d)
            {
                Tri(sub, a, b, c, Vector3.up);
                Tri(sub, a, c, d, Vector3.up);
            }

            public void QuadUpPoints(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float worldUv = 0f)
            {
                QuadFacing(sub, a, b, c, d, Vector3.up, worldUv);
            }

            public void QuadFacing(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, float worldUv = 0f)
            {
                Vector3 normal = facing.sqrMagnitude > 0 ? facing.normalized : Vector3.up;
                int ia = Add(a, normal, worldUv), ib = Add(b, normal, worldUv), ic = Add(c, normal, worldUv), id = Add(d, normal, worldUv);
                Tri(sub, ia, ib, ic, normal);
                Tri(sub, ia, ic, id, normal);
            }

            int Add(Vector3 p, Vector3 normal, float worldUv)
            {
                vertices.Add(p);
                normals.Add(normal);
                uvs.Add(worldUv > 0f ? new Vector2(p.x / worldUv, p.z / worldUv) : Vector2.zero);
                return vertices.Count - 1;
            }

            void Tri(int sub, int a, int b, int c, Vector3 facing)
            {
                Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                // Unity: лицевая сторона — обход по часовой, нормаль = cross(b-a, c-a).
                if (Vector3.Dot(n, facing) < 0f) (b, c) = (c, b);
                var list = triangles[sub];
                list.Add(a);
                list.Add(b);
                list.Add(c);
            }

            public Mesh Build(bool recalcNormals = false)
            {
                var mesh = new Mesh { name = name };
                if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = triangles.Length;
                for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
                if (recalcNormals) mesh.RecalculateNormals();
                else mesh.SetNormals(normals);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
