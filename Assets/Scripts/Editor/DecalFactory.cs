using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Плоские метки на дороге для событий: кольцо, заливка, стрелка, трещины, кратер, лужа, лазер.
    public static class DecalFactory
    {
        const string MeshDir = "Assets/Art/Generated/Decals";

        public static Mesh Ring, Disc, Arrow, Quad;
        public static Material Marker, Cracks, Crater, Puddle, Laser;

        public static void EnsureAssets()
        {
            ArtLibrary.EnsureFolder(MeshDir);
            Ring = Save(RingMesh(0.76f, 1f, 64), "Ring");
            Disc = Save(RingMesh(0f, 1f, 64), "Disc");
            Arrow = Save(ArrowMesh(), "Arrow");
            Quad = Save(QuadMesh(), "Quad");

            Marker = Unlit("M_Marker", null, Color.white, false);
            Laser = Unlit("M_Laser", null, new Color(3f, 0.35f, 0.25f, 1f), true);
            Cracks = Unlit("M_Cracks", CracksTexture(), new Color(0.12f, 0.08f, 0.06f, 0.95f), false);
            Crater = Unlit("M_Crater", RadialTexture("T_Crater", new Color(0.12f, 0.09f, 0.07f), new Color(0.35f, 0.27f, 0.2f), 0.92f), Color.white, false);
            Puddle = Unlit("M_Puddle", RadialTexture("T_Puddle", new Color(1f, 0.92f, 0.45f), new Color(1f, 0.85f, 0.3f), 0.7f), new Color(1f, 1f, 1f, 0.85f), false);
        }

        // ---------- меши ----------

        static Mesh RingMesh(float inner, float outer, int segments)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            if (inner <= 0f)
            {
                verts.Add(Vector3.zero);
                uvs.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i <= segments; i++)
                {
                    float a = 2f * Mathf.PI * i / segments;
                    verts.Add(new Vector3(Mathf.Cos(a) * outer, 0f, Mathf.Sin(a) * outer));
                    uvs.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                    if (i > 0) { tris.Add(0); tris.Add(i + 1); tris.Add(i); }
                }
            }
            else
            {
                for (int i = 0; i <= segments; i++)
                {
                    float a = 2f * Mathf.PI * i / segments;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    verts.Add(d * inner);
                    verts.Add(d * outer);
                    uvs.Add(new Vector2((float)i / segments, 0f));
                    uvs.Add(new Vector2((float)i / segments, 1f));
                    if (i == 0) continue;
                    int b = i * 2;
                    tris.AddRange(new[] { b - 2, b + 1, b - 1, b - 2, b, b + 1 });
                }
            }
            return Build(verts, uvs, tris);
        }

        static Mesh ArrowMesh()
        {
            // Стрелка по +z: древко и наконечник, лежит в плоскости XZ.
            var outline = new[]
            {
                new Vector2(-0.18f, -0.9f), new Vector2(0.18f, -0.9f), new Vector2(0.18f, 0.1f), new Vector2(0.5f, 0.1f),
                new Vector2(0f, 0.9f), new Vector2(-0.5f, 0.1f), new Vector2(-0.18f, 0.1f),
            };
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            foreach (var p in outline)
            {
                verts.Add(new Vector3(p.x, 0.01f, p.y));
                uvs.Add(p * 0.5f + Vector2.one * 0.5f);
            }
            var tris = new List<int> { 0, 2, 1, 0, 6, 2, 3, 5, 4, 6, 5, 3, 6, 3, 2 };
            return Build(verts, uvs, tris);
        }

        static Mesh QuadMesh()
        {
            var verts = new List<Vector3> { new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f) };
            var uvs = new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            var tris = new List<int> { 0, 2, 1, 0, 3, 2 };
            return Build(verts, uvs, tris);
        }

        static Mesh Build(List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            // Все треугольники смотрят вверх.
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 n = Vector3.Cross(verts[tris[i + 1]] - verts[tris[i]], verts[tris[i + 2]] - verts[tris[i]]);
                if (n.y < 0f) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh Save(Mesh mesh, string name)
        {
            mesh.name = name;
            string path = $"{MeshDir}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ---------- материалы и текстуры ----------

        static Material Unlit(string name, Texture2D texture, Color color, bool additive)
        {
            string path = $"{ArtLibrary.MaterialsDir}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetColor("_BaseColor", color);
            m.SetTexture("_BaseMap", texture);
            FxFactory.Transparent(m, additive);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D RadialTexture(string name, Color center, Color edge, float edgeAlpha)
        {
            string path = $"{ArtLibrary.TexturesDir}/{name}.png";
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var random = new System.Random(name.Length * 31);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) + (float)(random.NextDouble() - 0.5) * 0.06f;
                    Color c = Color.Lerp(center, edge, Mathf.SmoothStep(0.2f, 0.95f, r));
                    c.a = r > 1f ? 0f : Mathf.Lerp(1f, edgeAlpha, Mathf.SmoothStep(0.6f, 1f, r)) * Mathf.SmoothStep(1f, 0.93f, r);
                    tex.SetPixel(x, y, c);
                }
            return SaveTexture(tex, path);
        }

        static Texture2D CracksTexture()
        {
            string path = $"{ArtLibrary.TexturesDir}/T_Cracks.png";
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var clear = new Color(1f, 1f, 1f, 0f);
            for (int i = 0; i < size * size; i++) tex.SetPixel(i % size, i / size, clear);
            var random = new System.Random(77);
            for (int crack = 0; crack < 14; crack++)
            {
                float x = size / 2f + (float)(random.NextDouble() - 0.5) * 40f;
                float y = size / 2f + (float)(random.NextDouble() - 0.5) * 40f;
                float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
                int length = 40 + random.Next(80);
                for (int s = 0; s < length; s++)
                {
                    angle += (float)(random.NextDouble() - 0.5) * 0.7f;
                    x += Mathf.Cos(angle) * 1.5f;
                    y += Mathf.Sin(angle) * 1.5f;
                    int w = s < length / 3 ? 2 : 1;
                    for (int ox = -w; ox <= w; ox++)
                        for (int oy = -w; oy <= w; oy++)
                        {
                            int px = (int)x + ox, py = (int)y + oy;
                            if (px >= 0 && py >= 0 && px < size && py < size) tex.SetPixel(px, py, Color.white);
                        }
                }
            }
            return SaveTexture(tex, path);
        }

        static Texture2D SaveTexture(Texture2D tex, string path)
        {
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------- объекты ----------

        public static GameObject Flat(string name, Transform parent, Mesh mesh, Material material, Vector3 localPosition, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }
    }
}
