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
            var sand = NoiseTexture("T_Sand", 512, Hex("#C98F57"), Hex("#DDAA70"), 14, 6, 0.08f, Hex("#B07A48"), Hex("#E9C18C"));
            var cobble = TileTexture("T_Cobble", 512, 8, 12, Hex("#7E756C"), Hex("#9C9286"), Hex("#4E4842"), 0.09f, true, 15);
            var paver = TileTexture("T_Paver", 512, 4, 4, Hex("#B9B3A9"), Hex("#C9C3B8"), Hex("#8C877F"), 0.03f, false, 16);

            // Машины
            Lit("M_Paint_Red", "#FF4B2B", 0.82f, 0.15f);
            Lit("M_Paint_Blue", "#2F6BFF", 0.82f, 0.15f);
            Lit("M_Paint_Yellow", "#FFC21A", 0.82f, 0.15f);
            Lit("M_Paint_Mint", "#2DD4A0", 0.82f, 0.15f);
            Lit("M_Glass", "#1B2A3A", 0.95f, 0.35f);
            Lit("M_Tire", "#1C1C20", 0.15f);
            Lit("M_Rim", "#C9CED6", 0.75f, 0.9f);
            Lit("M_RimDark", "#4A515C", 0.7f, 0.85f);
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
            // Город
            Lit("M_Cobble", "#FFFFFF", 0.25f, 0f, null, 0f, cobble);
            Lit("M_Sidewalk", "#FFFFFF", 0.2f, 0f, null, 0f, paver);
            Lit("M_Curb", "#8F8A82", 0.25f);
            Lit("M_CityGround", "#7A766F", 0.15f);
            Lit("M_Brick", "#B5523B", 0.15f);
            Lit("M_Plaster", "#E8D9B5", 0.2f);
            Lit("M_PlasterTeal", "#5FB3A8", 0.2f);
            Lit("M_PlasterMustard", "#E0A93B", 0.2f);
            Lit("M_PlasterLilac", "#B39DDB", 0.2f);
            Lit("M_Roof", "#5A4A44", 0.2f);
            Lit("M_WindowLit", "#FFD9A0", 0.85f, 0f, "#FFC77A", 2.6f);
            Lit("M_WindowDark", "#2B3A52", 0.92f, 0.2f);
            Lit("M_WindowShop", "#FFE3B0", 0.85f, 0f, "#FFD08A", 3.0f);
            Lit("M_GlassBlue", "#3F7FA8", 0.95f, 0.6f);
            Lit("M_Awning1", "#E0352B", 0.3f);
            Lit("M_Awning2", "#24A0E0", 0.3f);
            Lit("M_Awning3", "#2DAE6A", 0.3f);
            Lit("M_Neon", "#FF4FA0", 0.5f, 0f, "#FF4FA0", 3.2f);
            Lit("M_TrafficRed", "#3A0A0A", 0.6f, 0f, "#FF2A1A", 2.5f);
            Lit("M_TrafficGreen", "#0A3A1A", 0.6f, 0f, "#2AFF7A", 2.5f);
            Lit("M_TrafficOff", "#2A2A1A", 0.6f);
            // Пустыня
            Lit("M_Sand", "#FFFFFF", 0.1f, 0f, null, 0f, sand);
            Lit("M_SandRock", "#C9875A", 0.08f);
            Lit("M_SandRockDark", "#9E6544", 0.08f);
            Lit("M_SandRockLight", "#E3B183", 0.08f);
            Lit("M_Cactus", "#4F8F4A", 0.2f);
            Lit("M_Canvas", "#F0E2C4", 0.15f);
            // Гараж
            Lit("M_GarageFloor", "#1C2230", 0.8f, 0.2f);
            Lit("M_FloorLine", "#323C55", 0.6f);
            Lit("M_Cyclorama", "#26304A", 0.15f);
            Lit("M_Turntable", "#2E3548", 0.7f, 0.6f);
            Lit("M_TurntableTop", "#3A4258", 0.65f, 0.4f);
            Lit("M_SoftboxFrame", "#14171F", 0.4f);
            Lit("M_StudioCeiling", "#151A26", 0.1f);
            Lit("M_Softbox", "#FFF6E8", 0.5f, 0f, "#FFF6E8", 2.5f);
            Lit("M_LampOrange", "#FF6A2A", 0.5f, 0f, "#FF6A2A", 2.4f);
            Lit("M_LampTeal", "#24E0C8", 0.5f, 0f, "#24E0C8", 2.0f);
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
            // «Тель-Авив» v2: облицовка тоннеля.
            Lit("M_TunnelTile", "#C9CFD8", 0.55f);
            // События v3: дорожные работы и обстрел.
            Lit("M_BarrierConcrete", "#D9D4C7", 0.15f);
            Lit("M_WorksOrange", "#FF6A2A", 0.5f);
            Lit("M_WorksLamp", "#FFC21A", 0.5f, 0f, "#FFB000", 4.0f);
            Lit("M_WorksArrow", "#FFD23F", 0.5f, 0f, "#FFC21A", 3.0f);
            Lit("M_MeteorRock", "#4A3A36", 0.12f);
            Lit("M_MeteorLava", "#FF5A1F", 0.4f, 0f, "#FF4A12", 5.0f);
            // Хаос v4: шары, мины, молоты, карусель, бочки, батуты, лёд, желе, арки, кольца.
            Lit("M_BallChrome", "#D9DEE6", 0.85f, 0.95f);
            Lit("M_BallRubberA", "#FF3D7F", 0.65f);
            Lit("M_BallRubberB", "#FFE14D", 0.65f);
            Lit("M_BallMini_Red", "#FF4B2B", 0.7f);
            Lit("M_BallMini_Yellow", "#FFC21A", 0.7f);
            Lit("M_BallMini_Teal", "#24E0C8", 0.7f);
            Lit("M_BallMini_Lime", "#A6F23A", 0.7f);
            Lit("M_BallMini_Purple", "#B45CFF", 0.7f);
            Lit("M_BallMini_Blue", "#2F6BFF", 0.7f);
            Lit("M_GiantPurple", "#8B5CF6", 0.65f);
            Lit("M_GiantOrange", "#FF8A1F", 0.65f);
            Lit("M_MineBody", "#2E3A47", 0.55f, 0.3f);
            Lit("M_MineLamp", "#FF2A2A", 0.6f, 0f, "#FF2A2A", 5.0f);
            Lit("M_TNT", "#D7263D", 0.55f);
            Lit("M_HammerHead", "#E0352B", 0.6f);
            Lit("M_TrampBlue", "#2F6BFF", 0.6f);
            Lit("M_TrampMat", "#1E2230", 0.2f);
            Lit("M_Ice", "#BFEFFF", 0.95f);
            Lit("M_IceDeep", "#7FD3F5", 0.92f);
            Lit("M_Jelly", "#FF5FA2", 0.85f);
            Lit("M_JellyHi", "#FFB3D4", 0.9f);
            Lit("M_ArchGlow", "#B45CFF", 0.6f, 0f, "#B45CFF", 4.0f);
            Lit("M_ArchBody", "#3A2E5C", 0.6f, 0.4f);
            Lit("M_RingGold", "#FFC21A", 0.75f, 0.8f, "#FFB000", 1.6f);
            Lit("M_Glove", "#E81E3A", 0.75f);
            Lit("M_PenguinBeak", "#FF9A1F", 0.5f);
            Lit("M_Padding", "#FF4B6E", 0.4f);
            Lit("M_CarnivalA", "#FF4BAA", 0.6f);
            Lit("M_CarnivalB", "#2DD4F0", 0.6f);
            // Хаос v5: трюки (пружина, рука, вентилятор, магнит, наковальня, бампер, жвачка, масло, утка, пушка, НЛО, кувалда, смерч).
            Lit("M_SpringTop", "#FF3B3B", 0.65f);
            Lit("M_Lid", "#59606B", 0.55f, 0.5f);
            Lit("M_GloveWhite", "#F6F3EA", 0.5f);
            Lit("M_FanBody", "#1FB5A8", 0.55f);
            Lit("M_MagnetRed", "#E8262C", 0.7f);
            Lit("M_Anvil", "#3A3F47", 0.65f, 0.7f);
            Lit("M_BumperCap", "#FF3D5A", 0.7f, 0f, "#FF3D5A", 1.2f);
            Lit("M_BumperRing", "#FFB000", 0.7f, 0f, "#FFB000", 2.5f);
            Lit("M_Gum", "#FF7EB6", 0.7f);
            Lit("M_GumHi", "#FFC3DD", 0.8f);
            Lit("M_Oil", "#121419", 0.94f, 0.4f);
            Lit("M_OilSheenA", "#6A4CFF", 0.95f);
            Lit("M_OilSheenB", "#19C6B0", 0.95f);
            Lit("M_DuckYellow", "#FFD21F", 0.7f);
            Lit("M_CannonIron", "#2B2E35", 0.6f, 0.6f);
            Lit("M_UfoHull", "#B9C3CF", 0.75f, 0.85f);
            Lit("M_UfoDome", "#5FE3FF", 0.95f);
            Lit("M_UfoLight", "#7CFF4F", 0.6f, 0f, "#7CFF4F", 4f);
            Lit("M_Alien", "#7CE05A", 0.6f);
            Lit("M_MalletWood", "#D9A066", 0.4f);
            // Шоу-события v6: Верити (смайлик) и автобус EL DRIFTO.
            Lit("M_VerityYellow", "#F6CB00", 0.62f);
            Lit("M_VerityFace", "#0B0B0D", 0.75f);
            Lit("M_BusRed", "#E5352A", 0.68f);
            Lit("M_BusWhite", "#F3F0E7", 0.6f);
            Lit("M_BusYellow", "#FFC21A", 0.65f);
            Lit("M_BusSeat", "#2A6FD6", 0.35f);
            Lit("M_BusSign", "#FF8A00", 0.6f, 0f, "#FF8A00", 2.5f);
            Lit("M_Skin", "#F0BE93", 0.4f);
            // Полигон испытаний: стены и крыша ангара, лампы прожекторов.
            Lit("M_HangarWall", "#6E9BD1", 0.45f);
            Lit("M_HangarRoof", "#E3EAF2", 0.5f);
            Lit("M_Floodlamp", "#FFF6DC", 0.6f, 0f, "#FFF6DC", 4f);
            // Бетон испытательной площадки: светло-серый, но не белый под солнцем.
            Lit("M_ArenaConcrete", "#B4B0A7", 0.25f);
            // ГИПЕРГАМИЯ (v7): золотая рама, розовая подложка, сердечки, светящиеся звёздочки, картинка пользователя.
            Lit("M_HypergamyFrame", "#FFC83D", 0.7f, 0.55f);
            Lit("M_HypergamyBack", "#FF4F9A", 0.55f);
            Lit("M_HypergamyHeart", "#FF2D55", 0.7f);
            Lit("M_HypergamyStar", "#FFF1A8", 0.5f, 0f, "#FFE45C", 1.5f);
            HypergamyAssets.PictureMaterial();
            Glow("M_UfoBeam", "#7CFF4F", 0.12f, true);
            // Бонусы: ящик «?», ракеты, пулемёт, пузырь щита, трассеры и молнии.
            Lit("M_ItemBoxA", "#FF4BAA", 0.7f, 0f, "#FF4BAA", 0.6f);
            Lit("M_ItemBoxB", "#2DD4F0", 0.7f, 0f, "#2DD4F0", 0.6f);
            Lit("M_ItemBoxC", "#FFC21A", 0.7f, 0f, "#FFC21A", 0.6f);
            Lit("M_ItemMark", "#FFFFFF", 0.7f, 0f, "#FFFFFF", 1.6f);
            Lit("M_LeaderBlue", "#2F6BFF", 0.7f, 0.2f);
            Lit("M_AmmoOlive", "#6B7A3A", 0.4f);
            Glow("M_ShieldGlow", "#40FFE0", 0.3f, true);
            Glow("M_Tracer", "#FFD86B", 1f, true);
            Glow("M_Bolt", "#8FE9FF", 1f, true);
            Glow("M_Tornado", "#8C8274", 0.42f, false);

            // Трасса
            Lit("M_Asphalt", "#FFFFFF", 0.28f, 0f, null, 0f, asphalt);
            Lit("M_Line", "#F2F0E8", 0.3f);
            Lit("M_Grass", "#FFFFFF", 0.08f, 0f, null, 0f, grass);
            Lit("M_Gravel", "#FFFFFF", 0.05f, 0f, null, 0f, gravel);

            CarMaterials.CreateAll();
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

        /// Прозрачный светящийся или дымчатый материал без освещения (луч НЛО, воронка смерча), видно с обеих сторон.
        public static Material Glow(string name, string hex, float alpha, bool additive)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            var color = Hex(hex);
            color.a = alpha;
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", null);
            FxFactory.Transparent(material, additive);
            material.SetFloat("_Cull", 0f);
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

        /// Бесшовная плитка: ряды камней со швами (брусчатка — со смещением рядов и скруглением, плитка — ровная сетка).
        static Texture2D TileTexture(string name, int size, int columns, int rows, Color stoneA, Color stoneB, Color mortar,
                                     float joint, bool stagger, int seed)
        {
            string path = $"{TexturesDir}/{name}.png";
            var random = new System.Random(seed);
            var shade = new float[columns * 2, rows];
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns * 2; x++)
                    shade[x, y] = (float)random.NextDouble();
            var pixels = new Color[size * size];
            for (int py = 0; py < size; py++)
            {
                float v = (float)py / size * rows;
                int row = Mathf.FloorToInt(v);
                float fy = v - row;
                for (int px = 0; px < size; px++)
                {
                    float u = (float)px / size * columns + (stagger && row % 2 == 1 ? 0.5f : 0f);
                    int col = Mathf.FloorToInt(u);
                    float fx = u - col;
                    // Расстояние до шва; у брусчатки углы скруглены.
                    float dx = Mathf.Min(fx, 1f - fx), dy = Mathf.Min(fy, 1f - fy) * columns / rows;
                    float d = stagger ? Mathf.Min(dx, dy) - Mathf.Max(0f, 0.08f - Mathf.Sqrt(dx * dx + dy * dy)) : Mathf.Min(dx, dy);
                    float s = shade[((col % (columns * 2)) + columns * 2) % (columns * 2), row % rows];
                    Color stone = Color.Lerp(stoneA, stoneB, s);
                    float grain = (float)random.NextDouble() * 0.06f - 0.03f;
                    stone = new Color(stone.r + grain, stone.g + grain, stone.b + grain);
                    float edge = Mathf.SmoothStep(joint * 0.6f, joint * 1.6f, d);
                    pixels[py * size + px] = Color.Lerp(mortar, stone, edge);
                }
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

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
