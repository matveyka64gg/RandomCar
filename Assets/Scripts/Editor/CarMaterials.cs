using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Материалы машин v2: краска с лаком (Complex Lit, clear coat), прозрачные стёкла, фары, салон, тормоза.
    /// Имена совпадают с материалами в Blender (Tools/Blender/car_parts.py → car_materials).
    /// Цвет и покрытие краски каждой машины задаются в игре блоком свойств (CarCosmetics), материалы общие.
    public static class CarMaterials
    {
        public static void CreateAll()
        {
            foreach (var name in new[] { "M_Paint", "M_PaintRoof", "M_PaintHood", "M_PaintLower" })
                Paint(name, "#D8442E");
            Glass("M_CarGlass", "#0C141C", 0.30f);
            ArtLibrary.Lit("M_GlassFrit", "#07080A", 0.88f);
            ArtLibrary.Lit("M_TrimGloss", "#0A0B0D", 0.9f, 0.1f);
            ArtLibrary.Lit("M_Liner", "#0C0D0F", 0.05f);
            ArtLibrary.Lit("M_Underbody", "#121316", 0.1f);
            ArtLibrary.Lit("M_LightHousing", "#2A2F37", 0.92f, 0.85f);
            ArtLibrary.Lit("M_LightDrl", "#EAF4FF", 0.9f, 0f, "#EAF4FF", 3.2f);
            ArtLibrary.Lit("M_LightLens", "#C8D4E0", 0.95f, 0.8f);
            ArtLibrary.Lit("M_LightTailDark", "#4A070B", 0.9f);
            ArtLibrary.Lit("M_LightAmber", "#FF9A1A", 0.85f, 0f, "#FF8A00", 1.2f);
            // Задний ход (v3): белая секция фонаря, CarLights зажигает её на задней передаче.
            ArtLibrary.Lit("M_LightReverse", "#EEF1F6", 0.9f, 0f, "#FFFFFF", 0.35f);
            ArtLibrary.Lit("M_Grille", "#0E0F11", 0.35f, 0.2f);
            ArtLibrary.Lit("M_Plate", "#F2F2EE", 0.45f);
            ArtLibrary.Lit("M_Interior", "#1B1C20", 0.25f);
            ArtLibrary.Lit("M_Seat", "#26272C", 0.2f);
            ArtLibrary.Lit("M_InteriorAccent", "#A8302A", 0.25f);
            ArtLibrary.Lit("M_Display", "#08121E", 0.9f, 0f, "#3FA0FF", 0.9f);
            ArtLibrary.Lit("M_BrakeDisc", "#6E7074", 0.55f, 0.9f);
            ArtLibrary.Lit("M_Caliper", "#C8202A", 0.7f, 0.2f);
            ArtLibrary.Lit("M_Carbon", "#16181C", 0.8f, 0.35f);
            ArtLibrary.Lit("M_Tire", "#18181B", 0.12f);
            ArtLibrary.Lit("M_GrilleMesh", "#FFFFFF", 0.6f, 0.7f, null, 0f, Honeycomb());
            ArtLibrary.Lit("M_Cargo", "#5E6B3A", 0.3f);
            Vinyl("M_Vinyl");
            AssetDatabase.SaveAssets();
        }

        /// Краска: база (металлик/сглаживание задаёт отделка) под прозрачным лаком.
        public static Material Paint(string name, string hex)
        {
            var material = Ensure(name, "Universal Render Pipeline/Complex Lit");
            material.SetColor("_BaseColor", ArtLibrary.Hex(hex));
            material.SetFloat("_Metallic", 0.35f);
            material.SetFloat("_Smoothness", 0.6f);
            material.SetFloat("_ClearCoat", 1f);
            material.SetFloat("_ClearCoatMask", 1f);
            material.SetFloat("_ClearCoatSmoothness", 0.94f);
            material.SetFloat("_EnvironmentReflections", 1f);
            Validate(material);
            return material;
        }

        /// Стекло: прозрачное, блики и отражения сохраняются (Preserve Specular).
        public static Material Glass(string name, string hex, float alpha)
        {
            var material = Ensure(name, "Universal Render Pipeline/Lit");
            var c = ArtLibrary.Hex(hex);
            c.a = alpha;
            material.SetColor("_BaseColor", c);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.97f);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_BlendModePreserveSpecular", 1f);
            material.SetFloat("_Cull", 2f);
            // Без отражений зонда карты: он видит оранжевые боксы и бежевые стены — все окна становились
            // грязно-оранжевыми (у синей машины — фиолетовыми). Блик солнца остаётся, тонировка — синеватая.
            material.SetFloat("_EnvironmentReflections", 0f);
            Validate(material);
            return material;
        }

        /// Шаблон наклеек: прозрачный Lit поверх краски (текстура и цвет задаются каждому слою в игре).
        public static Material Vinyl(string name)
        {
            var material = Ensure(name, "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0.05f);
            material.SetFloat("_Smoothness", 0.62f);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_BlendModePreserveSpecular", 0f);
            material.SetFloat("_Cull", 2f);
            Validate(material);
            return material;
        }

        /// Сотовая решётка: тёмные соты с металлическим ободком, бесшовно.
        static Texture2D Honeycomb()
        {
            const string path = ArtLibrary.TexturesDir + "/T_Honeycomb.png";
            int size = 256;
            var pixels = new Color32[size * size];
            float s = size / 8f;
            for (int py = 0; py < size; py++)
                for (int px = 0; px < size; px++)
                {
                    var p = new Vector2(px / s, py / s);
                    var r = new Vector2(1f, 1.7320508f);
                    var h = r * 0.5f;
                    Vector2 a = new Vector2(Mod(p.x, r.x), Mod(p.y, r.y)) - h;
                    Vector2 b = new Vector2(Mod(p.x - h.x, r.x), Mod(p.y - h.y, r.y)) - h;
                    Vector2 g = a.sqrMagnitude < b.sqrMagnitude ? a : b;
                    float d = Mathf.Max(Mathf.Abs(g.x) * 0.866f + Mathf.Abs(g.y) * 0.5f, Mathf.Abs(g.y));
                    float rim = Mathf.SmoothStep(0.36f, 0.42f, d);
                    byte v = (byte)Mathf.Lerp(18f, 150f, rim);
                    pixels[py * size + px] = new Color32(v, v, (byte)(v + 4), 255);
                }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static float Mod(float a, float m) => a - m * Mathf.Floor(a / m);

        static Material Ensure(string name, string shaderName)
        {
            string path = $"{ArtLibrary.MaterialsDir}/{name}.mat";
            var shader = Shader.Find(shaderName);
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
            material.enableInstancing = true;
            return material;
        }

        /// Ключевые слова и очередь отрисовки — как при правке материала в инспекторе URP.
        static void Validate(Material material)
        {
            BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
        }
    }
}
