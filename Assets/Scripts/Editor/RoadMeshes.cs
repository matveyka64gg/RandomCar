using RandomCar.Car;
using RandomCar.Track;
using UnityEngine;
using static RandomCar.EditorTools.BuilderKit;
using MeshBuilder = RandomCar.EditorTools.TrackMeshBuilder.MeshBuilder;

namespace RandomCar.EditorTools
{
    /// Дорожное полотно и разметка по участкам трассы — для карт с разными покрытиями.
    public static class RoadMeshes
    {
        public static Vector3 At(TrackPath path, float distance, float lateral, float y = 0f)
        {
            float p = TrackPath.Wrap(distance / path.Length);
            return path.PointAt(p) + path.SideAt(p) * lateral + Vector3.up * y;
        }

        public static bool InRange(float d, float from, float to, float length)
        {
            d = Mathf.Repeat(d, length);
            from = Mathf.Repeat(from, length);
            to = Mathf.Repeat(to, length);
            return from <= to ? d >= from && d <= to : d >= from || d <= to;
        }

        /// Полотно на участках, где include(расстояние) истинно; tile — шаг текстуры вдоль, м. С коллайдером и покрытием.
        public static GameObject AddRoad(TrackPath path, GameObject parent, string name, string material, SurfaceType surface,
                                         System.Func<float, bool> include, float tile, string folder, float halfWidth = 6f)
        {
            var b = new MeshBuilder(name, 1);
            float length = path.Length;
            const float step = 2f;
            int count = Mathf.CeilToInt(length / step);
            for (int k = 0; k < count; k++)
            {
                float d0 = k * step, d1 = Mathf.Min(length, (k + 1) * step);
                if (!include((d0 + d1) * 0.5f)) continue;
                Vector3 a0 = At(path, d0, -halfWidth - 0.05f), a1 = At(path, d0, halfWidth + 0.05f);
                Vector3 b0 = At(path, d1, -halfWidth - 0.05f), b1 = At(path, d1, halfWidth + 0.05f);
                int i0 = b.Vertex(a0, new Vector2(0f, d0 / tile)), i1 = b.Vertex(a1, new Vector2(2f * halfWidth / tile, d0 / tile));
                int i2 = b.Vertex(b1, new Vector2(2f * halfWidth / tile, d1 / tile)), i3 = b.Vertex(b0, new Vector2(0f, d1 / tile));
                b.QuadUp(0, i0, i1, i2, i3);
            }
            var go = Place(SaveMesh(b.Build(recalcNormals: true), folder), name, parent.transform, material);
            go.AddComponent<MeshCollider>();
            go.AddComponent<Surface>().Type = surface;
            return go;
        }

        /// Прерывистая осевая и сплошные края на участках include.
        public static Mesh Markings(TrackPath path, string name, System.Func<float, bool> include, float halfWidth = 6f)
        {
            var b = new MeshBuilder(name, 1);
            float length = path.Length;
            for (float d = 0f; d < length; d += 2f)
            {
                if (!include(d)) continue;
                foreach (float lat in new[] { -(halfWidth - 0.45f), halfWidth - 0.45f })
                    b.QuadUpPoints(0, At(path, d, lat - 0.1f, 0.012f), At(path, d, lat + 0.1f, 0.012f), At(path, d + 2f, lat + 0.1f, 0.012f), At(path, d + 2f, lat - 0.1f, 0.012f));
                if (Mathf.Repeat(d, 12f) < 4f)
                    b.QuadUpPoints(0, At(path, d, -0.09f, 0.012f), At(path, d, 0.09f, 0.012f), At(path, d + 2f, 0.09f, 0.012f), At(path, d + 2f, -0.09f, 0.012f));
            }
            return b.Build();
        }

        /// Невидимая стена на расстоянии lateral от оси с обеих сторон (там, где include).
        public static Mesh Walls(TrackPath path, string name, float lateral, float height, System.Func<float, bool> include = null)
        {
            var b = new MeshBuilder(name, 1);
            const float step = 3f;
            float length = path.Length;
            int count = Mathf.CeilToInt(length / step);
            foreach (int side in new[] { -1, 1 })
                for (int k = 0; k < count; k++)
                {
                    float d0 = k * step, d1 = Mathf.Min(length, (k + 1) * step);
                    if (include != null && !include((d0 + d1) * 0.5f)) continue;
                    Vector3 a = At(path, d0, side * lateral, -2f), c = At(path, d1, side * lateral, -2f);
                    Vector3 face = At(path, d0, 0f) - a;
                    face.y = 0f;
                    b.QuadFacing(0, a, c, c + Vector3.up * height, a + Vector3.up * height, face);
                }
            return b.Build();
        }
    }
}
