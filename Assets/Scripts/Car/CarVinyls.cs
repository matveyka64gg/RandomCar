using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Car
{
    /// Наклейки на кузове: каждая — меш, вырезанный из окрашенной части кузова объёмом наклейки
    /// (стёкла, колёса, фары и дорогу не задевает) с UV из проекции. Работает в любой сборке без настроек URP.
    /// Слой выше — позже рисуется (очередь отрисовки по порядку слоёв).
    public sealed class CarVinyls : MonoBehaviour
    {
        [SerializeField] MeshFilter body;
        [SerializeField] int[] paintSubmeshes = new int[0];
        [SerializeField] Material template;
        /// Зоны сверху по длине (z в координатах кузова): капот, крыша, багажник.
        [SerializeField] Vector2 hoodZ = new Vector2(0.9f, 2.1f);
        [SerializeField] Vector2 roofZ = new Vector2(-0.6f, 0.1f);
        [SerializeField] Vector2 trunkZ = new Vector2(-2.1f, -1.5f);

        const float Lift = 0.0025f;
        const int MaxTrianglesPerLayer = 6000;

        readonly List<GameObject> layers = new List<GameObject>();
        readonly List<Material> materials = new List<Material>();
        Vector3[] vertices;
        Vector3[] normals;
        List<int> triangles;
        Bounds bounds;

        /// Сколько треугольников в последней построенной наклейке (для тестов и отладки).
        public int LastTriangles { get; private set; }

        public void Configure(MeshFilter bodyFilter, int[] submeshes, Material vinylTemplate, Vector2 hood, Vector2 roof, Vector2 trunk)
        {
            body = bodyFilter;
            paintSubmeshes = submeshes;
            template = vinylTemplate;
            hoodZ = hood;
            roofZ = roof;
            trunkZ = trunk;
        }

        void OnDestroy() => Clear();

        void Clear()
        {
            foreach (var go in layers) if (go != null) Destroy(go);
            layers.Clear();
            foreach (var m in materials) if (m != null) Destroy(m);
            materials.Clear();
        }

        void CacheMesh()
        {
            if (vertices != null || body == null) return;
            var mesh = body.sharedMesh;
            if (mesh == null || !mesh.isReadable)
            {
                Debug.LogWarning($"[RandomCar] Наклейки на {name} недоступны: сетка кузова не читаема.");
                return;
            }
            vertices = mesh.vertices;
            normals = mesh.normals;
            triangles = new List<int>();
            foreach (int s in paintSubmeshes)
                if (s >= 0 && s < mesh.subMeshCount) triangles.AddRange(mesh.GetTriangles(s));
            bounds = mesh.bounds;
        }

        public void Apply(List<DecalLayer> decals, PaintJob paint)
        {
            Clear();
            if (decals == null || decals.Count == 0 || template == null) return;
            CacheMesh();
            if (vertices == null) return;
            for (int i = 0; i < decals.Count && i < CarBuild.MaxDecals; i++)
            {
                var d = decals[i];
                var texture = DecalLibrary.Resolve(d);
                if (texture == null) continue;
                Build(d, texture, i, false);
                if (d.Mirror) Build(d, texture, i, true);
            }
        }

        /// Кадр проекции стороны: центр, оси изображения (вправо, вверх), направление взгляда внутрь, размеры.
        public void SideFrame(DecalSide side, out Vector3 centre, out Vector3 right, out Vector3 up, out Vector3 look, out Vector2 size)
        {
            var b = bounds;
            float midY = b.center.y;
            switch (side)
            {
                case DecalSide.Left:
                    centre = new Vector3(b.min.x, midY, b.center.z);
                    right = Vector3.back; up = Vector3.up; look = Vector3.right;
                    size = new Vector2(b.size.z, b.size.y);
                    break;
                case DecalSide.Right:
                    centre = new Vector3(b.max.x, midY, b.center.z);
                    right = Vector3.forward; up = Vector3.up; look = Vector3.left;
                    size = new Vector2(b.size.z, b.size.y);
                    break;
                case DecalSide.Front:
                    centre = new Vector3(0f, midY, b.max.z);
                    right = Vector3.left; up = Vector3.up; look = Vector3.back;
                    size = new Vector2(b.size.x, b.size.y);
                    break;
                case DecalSide.Rear:
                    centre = new Vector3(0f, midY, b.min.z);
                    right = Vector3.right; up = Vector3.up; look = Vector3.forward;
                    size = new Vector2(b.size.x, b.size.y);
                    break;
                default:
                    var z = side == DecalSide.Hood ? hoodZ : side == DecalSide.Roof ? roofZ : trunkZ;
                    centre = new Vector3(0f, b.max.y, (z.x + z.y) * 0.5f);
                    right = Vector3.right; up = Vector3.forward; look = Vector3.down;
                    size = new Vector2(b.size.x, Mathf.Abs(z.y - z.x));
                    break;
            }
        }

        static DecalSide Opposite(DecalSide side) => side switch
        {
            DecalSide.Left => DecalSide.Right,
            DecalSide.Right => DecalSide.Left,
            _ => side,
        };

        void Build(DecalLayer d, Texture2D texture, int order, bool mirrored)
        {
            var side = mirrored ? Opposite(d.Side) : d.Side;
            SideFrame(side, out var centre, out var right, out var up, out var look, out var size);
            bool sideways = side == DecalSide.Left || side == DecalSide.Right;
            // Положение: для бортов U — от кормы к носу (одинаково с обеих сторон), сверху U — слева направо.
            float along = d.U - 0.5f;
            Vector3 alongAxis = sideways ? Vector3.forward : right;
            float alongLen = sideways ? size.x : size.x;
            Vector3 pos = centre + alongAxis * along * alongLen + up * (d.V - 0.5f) * size.y;
            if (!sideways && side != DecalSide.Front && side != DecalSide.Rear)
            {
                // Сверху: U — поперёк машины, V — вдоль зоны.
                pos = centre + right * (d.U - 0.5f) * size.x + up * (d.V - 0.5f) * size.y;
            }
            float refLen = sideways ? size.x : size.x;
            float w = d.Size * refLen;
            float h = w * texture.height / Mathf.Max(1, texture.width) / Mathf.Max(0.25f, d.Aspect);
            float rotation = mirrored && sideways ? -d.Rotation : d.Rotation;
            var rot = Quaternion.AngleAxis(rotation, look);
            Vector3 ax = rot * right;
            Vector3 ay = rot * up;
            float depth = sideways ? bounds.size.x * 0.55f : side == DecalSide.Front || side == DecalSide.Rear ? 0.9f : 0.9f;
            Vector3 boxCentre = pos + look * depth * 0.5f - look * 0.08f;
            bool flip = d.FlipX;

            var outVerts = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outUv = new List<Vector2>();
            var outTris = new List<int>();
            var poly = new List<Vector3>(9);
            var polyN = new List<Vector3>(9);
            var tmp = new List<Vector3>(9);
            var tmpN = new List<Vector3>(9);
            int added = 0;
            for (int t = 0; t + 2 < triangles.Count && added < MaxTrianglesPerLayer; t += 3)
            {
                int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                Vector3 p0 = vertices[i0], p1 = vertices[i1], p2 = vertices[i2];
                Vector3 fn = Vector3.Cross(p1 - p0, p2 - p0);
                if (Vector3.Dot(fn, look) > -0.2f * fn.magnitude) continue;   // грань смотрит не на зрителя
                poly.Clear();
                polyN.Clear();
                foreach (int idx in new[] { i0, i1, i2 })
                {
                    Vector3 q = vertices[idx] - boxCentre;
                    poly.Add(new Vector3(Vector3.Dot(q, ax) / w, Vector3.Dot(q, ay) / h, Vector3.Dot(q, look) / depth));
                    polyN.Add(normals.Length > idx ? normals[idx] : fn.normalized);
                }
                // Быстрый отказ: все вершины за одной гранью объёма.
                if (Outside(poly)) continue;
                for (int axis = 0; axis < 3 && poly.Count > 0; axis++)
                {
                    Clip(poly, polyN, tmp, tmpN, axis, 0.5f);
                    Clip(tmp, tmpN, poly, polyN, axis, -0.5f);
                }
                if (poly.Count < 3) continue;
                int start = outVerts.Count;
                for (int k = 0; k < poly.Count; k++)
                {
                    var q = poly[k];
                    Vector3 local = boxCentre + ax * (q.x * w) + ay * (q.y * h) + look * (q.z * depth);
                    var n = polyN[k].normalized;
                    outVerts.Add(local + n * Lift);
                    outNormals.Add(n);
                    float u = q.x + 0.5f;
                    if (flip) u = 1f - u;
                    if (mirrored && sideways) u = 1f - u;
                    outUv.Add(new Vector2(u, q.y + 0.5f));
                }
                for (int k = 1; k + 1 < poly.Count; k++)
                {
                    outTris.Add(start);
                    outTris.Add(start + k);
                    outTris.Add(start + k + 1);
                }
                added++;
            }
            LastTriangles = outTris.Count / 3;
            if (outTris.Count == 0) return;

            var mesh = new Mesh { name = "Vinyl" };
            mesh.indexFormat = outVerts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(outVerts);
            mesh.SetNormals(outNormals);
            mesh.SetUVs(0, outUv);
            mesh.SetTriangles(outTris, 0);
            mesh.RecalculateBounds();

            var go = new GameObject(mirrored ? $"Vinyl_{order}_M" : $"Vinyl_{order}");
            go.transform.SetParent(body.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mat = new Material(template) { name = "Vinyl_" + order };
            mat.SetTexture("_BaseMap", texture);
            var tint = d.Tint;
            tint.a = d.Opacity;
            mat.SetColor("_BaseColor", tint);
            mat.renderQueue = template.renderQueue + 1 + order;
            mr.sharedMaterial = mat;
            materials.Add(mat);
            layers.Add(go);
        }

        static bool Outside(List<Vector3> p)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                bool allHigh = true, allLow = true;
                foreach (var v in p)
                {
                    if (v[axis] <= 0.5f) allHigh = false;
                    if (v[axis] >= -0.5f) allLow = false;
                }
                if (allHigh || allLow) return true;
            }
            return false;
        }

        /// Отсечение многоугольника плоскостью axis = limit (Сазерленд — Ходжман), с нормалями.
        static void Clip(List<Vector3> src, List<Vector3> srcN, List<Vector3> dst, List<Vector3> dstN, int axis, float limit)
        {
            dst.Clear();
            dstN.Clear();
            int n = src.Count;
            if (n == 0) return;
            bool keepBelow = limit > 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = src[i], b = src[(i + 1) % n];
                Vector3 na = srcN[i], nb = srcN[(i + 1) % n];
                bool ain = keepBelow ? a[axis] <= limit : a[axis] >= limit;
                bool bin = keepBelow ? b[axis] <= limit : b[axis] >= limit;
                if (ain)
                {
                    dst.Add(a);
                    dstN.Add(na);
                }
                if (ain != bin)
                {
                    float t = (limit - a[axis]) / (b[axis] - a[axis]);
                    dst.Add(Vector3.Lerp(a, b, t));
                    dstN.Add(Vector3.Lerp(na, nb, t));
                }
            }
        }
    }
}
