using System.IO;
using UnityEditor;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Общая библиотека материалов и процедурных текстур по палитре из Docs/ArtDirection.md.
    /// Модели из Blender ссылаются на эти материалы по имени (см. ModelImportRules).
    public static class ArtLibrary
    {
        public const string MaterialsDir = "Assets/Art/Materials";
        public const string TexturesDir = "Assets/Art/Textures";

        public static void CreateAll()
        {
            EnsureFolder(MaterialsDir);
            EnsureFolder(TexturesDir);

            var asphalt = NoiseTexture("T_Asphalt", 512, Hex("#36373E"), Hex("#45464D"), 11, 16, 0.10f, Hex("#2A2B30"), Hex("#62636A"));
            var grass = NoiseTexture("T_Grass", 512, Hex("#5B9442"), Hex("#73AB52"), 12, 4, 0.05f, Hex("#4D8439"), Hex("#86B962"));
            var gravel = NoiseTexture("T_Gravel", 512, Hex("#D8B87C"), Hex("#E8CC94"), 13, 16, 0.22f, Hex("#B8955C"), Hex("#F4E2B8"));

            // Машины
            Lit("M_Paint_Red", "#FF4B2B", 0.82f, 0.15f);
            Lit("M_Paint_Blue", "#2F6BFF", 0.82f, 0.15f);
            Lit("M_Paint_Yellow", "#FFC21A", 0.82f, 0.15f);
            Lit("M_Paint_Mint", "#2DD4A0", 0.82f, 0.15f);
            Lit("M_Glass", "#1B2A3A", 0.95f, 0.35f);
            Lit("M_Tire", "#1C1C20", 0.15f);
            Lit("M_Rim", "#C9CED6", 0.75f, 0.9f);
            Lit("M_RimDark", "#2E3238", 0.65f, 0.8f);
            Lit("M_RimWhite", "#F2F0E8", 0.5f);
            Lit("M_Chrome", "#E6EAF0", 0.92f, 1f);
            Lit("M_Trim", "#26282E", 0.35f);
            Lit("M_LightHead", "#FFF6DC", 0.8f, 0f, "#FFF2D0", 2.2f);
            Lit("M_LightTail", "#FF2A2A", 0.8f, 0f, "#FF1A1A", 2.6f);
            Lit("M_Decal_White", "#F2F0E8", 0.45f);
            Lit("M_Decal_Black", "#15161A", 0.4f);
            Lit("M_Skin", "#E8B48A", 0.3f);

            // Окружение
            Lit("M_Wood", "#7A5233", 0.12f);
            Lit("M_Pine", "#2F7D4A", 0.12f);
            Lit("M_Pine2", "#3E9455", 0.12f);
            Lit("M_Leaf", "#5DB04A", 0.15f);
            Lit("M_Leaf2", "#7BC24F", 0.15f);
            Lit("M_Rock", "#8C8478", 0.1f);
            Lit("M_RockDark", "#6E675E", 0.1f);
            Lit("M_Concrete", "#D3CEC4", 0.2f);
            Lit("M_ConcreteDark", "#9A958C", 0.2f);
            Lit("M_Steel", "#B8C0CC", 0.6f, 0.6f);
            Lit("M_Brand", "#FF6A2A", 0.45f);
            Lit("M_BrandDark", "#121826", 0.4f);
            Lit("M_SignYellow", "#FFD23F", 0.4f);
            Lit("M_ConeOrange", "#FF6A1A", 0.4f);
            Lit("M_KerbRed", "#E0352B", 0.35f);
            Lit("M_KerbWhite", "#F2F0E8", 0.35f);
            Lit("M_Snow", "#F4F8FB", 0.3f);
            Lit("M_Mountain", "#7F8C99", 0.05f);
            Lit("M_Hill", "#5A8C4C", 0.05f);
            Lit("M_Cloud", "#FFFFFF", 0f, 0f, "#FFFFFF", 0.35f);
            Lit("M_StartLamp", "#2A0808", 0.7f, 0f, "#000000", 1f);
            Lit("M_LampGlow", "#FFF3D6", 0.5f, 0f, "#FFF3D6", 1.5f);
            Lit("M_Crowd1", "#FF4B2B", 0.3f);
            Lit("M_Crowd2", "#2F6BFF", 0.3f);
            Lit("M_Crowd3", "#FFC21A", 0.3f);
            Lit("M_Crowd4", "#2DD4A0", 0.3f);
            Lit("M_Crowd5", "#F2F0E8", 0.3f);
            Lit("M_Crowd6", "#8E5BFF", 0.3f);

            // События
            Lit("M_Worm", "#FF8FA3", 0.55f);
            Lit("M_Worm2", "#E2627F", 0.55f);
            Lit("M_Dirt", "#8A5A3B", 0.08f);
            Lit("M_Hole", "#2A1A12", 0.02f);
            Lit("M_TankOlive", "#6E7D3C", 0.35f, 0.15f);
            Lit("M_TankSand", "#C9B27A", 0.35f);
            Lit("M_TankTrack", "#33352F", 0.2f, 0.3f);
            Lit("M_Wool", "#F4F1EA", 0.05f);
            Lit("M_SheepFace", "#2A2A2E", 0.3f);
            Lit("M_BallRed", "#FF4B2B", 0.7f);
            Lit("M_BallYellow", "#FFC21A", 0.7f);
            Lit("M_BallBlue", "#2F6BFF", 0.7f);
            Lit("M_BallGreen", "#2DD4A0", 0.7f);
            Lit("M_Banana", "#FFD93B", 0.55f);
            Lit("M_BananaDark", "#7A5A1E", 0.3f);
            Lit("M_Basalt", "#4A4E58", 0.25f);
            Lit("M_BasaltTop", "#7A808C", 0.3f);
            Lit("M_GoodCyan", "#24E0C8", 0.6f, 0f, "#24E0C8", 1.8f);
            Lit("M_GoodLime", "#A6F23A", 0.6f, 0f, "#A6F23A", 1.6f);
            Lit("M_CrateWood", "#C98B4F", 0.25f);
            Lit("M_CrateWoodDark", "#8E5B2F", 0.25f);

            // Трасса
            Lit("M_Asphalt", "#FFFFFF", 0.28f, 0f, null, 0f, asphalt);
            Lit("M_Line", "#F2F0E8", 0.3f);
            Lit("M_Grass", "#FFFFFF", 0.08f, 0f, null, 0f, grass);
            Lit("M_Gravel", "#FFFFFF", 0.05f, 0f, null, 0f, gravel);

            AssetDatabase.SaveAssets();
        }

        public static Material Get(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsDir}/{name}.mat");

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        public static Material Lit(string name, string hex, float smoothness, float metallic = 0f,
                                   string emissionHex = null, float emission = 0f, Texture2D baseMap = null)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", Hex(hex));
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            material.SetTexture("_BaseMap", baseMap);
            if (emissionHex != null)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Hex(emissionHex) * emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// Бесшовная текстура: шум по решётке с заворотом краёв плюс крапинки.
        static Texture2D NoiseTexture(string name, int size, Color dark, Color light, int seed,
                                      int cells, float speckle, Color speckleDark, Color speckleLight)
        {
            string path = $"{TexturesDir}/{name}.png";
            var random = new System.Random(seed);
            var pixels = new Color[size * size];
            float[] octave = new float[size * size];
            float total = 0f;
            float weight = 1f;
            for (int o = 0, c = cells; o < 4; o++, c *= 2, weight *= 0.5f)
            {
                var grid = new float[c, c];
                for (int y = 0; y < c; y++)
                    for (int x = 0; x < c; x++)
                        grid[x, y] = (float)random.NextDouble();
                for (int py = 0; py < size; py++)
                {
                    float gy = (float)py / size * c;
                    int y0 = (int)gy;
                    float ty = Smooth(gy - y0);
                    for (int px = 0; px < size; px++)
                    {
                        float gx = (float)px / size * c;
                        int x0 = (int)gx;
                        float tx = Smooth(gx - x0);
                        float a = grid[x0 % c, y0 % c], b = grid[(x0 + 1) % c, y0 % c];
                        float d = grid[x0 % c, (y0 + 1) % c], e = grid[(x0 + 1) % c, (y0 + 1) % c];
                        octave[py * size + px] += weight * Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(d, e, tx), ty);
                    }
                }
                total += weight;
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                float n = octave[i] / total;
                Color c = Color.Lerp(dark, light, Mathf.SmoothStep(0.2f, 0.8f, n));
                double r = random.NextDouble();
                if (r < speckle * 0.5) c = Color.Lerp(c, speckleDark, 0.6f);
                else if (r < speckle) c = Color.Lerp(c, speckleLight, 0.5f);
                pixels[i] = c;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 8;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
