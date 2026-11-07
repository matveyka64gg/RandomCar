using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RandomCar.EditorTools
{
    /// Ассеты UI Toolkit: тема по умолчанию (шрифт с кириллицей), PanelSettings с масштабом под 1920×1080, стили игры.
    public static class UiAssets
    {
        const string Dir = "Assets/UI";
        const string ThemePath = Dir + "/RandomCarTheme.tss";
        const string PanelPath = Dir + "/RandomCarPanel.asset";
        const string StylePath = Dir + "/RandomCar.uss";

        public static PanelSettings PanelSettings()
        {
            ArtLibrary.EnsureFolder(Dir);
            if (!File.Exists(ThemePath))
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\nVisualElement {}\n");
                AssetDatabase.ImportAsset(ThemePath);
            }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            panel.themeStyleSheet = theme;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.clearColor = false;
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
            return panel;
        }

        const string OverlayPanelPath = Dir + "/RandomCarOverlayPanel.asset";

        /// Панель поверх всего интерфейса (полноэкранная картинка ГИПЕРГАМИИ): тот же масштаб, порядок выше.
        public static PanelSettings OverlayPanelSettings()
        {
            var main = PanelSettings();
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(OverlayPanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, OverlayPanelPath);
            }
            panel.themeStyleSheet = main.themeStyleSheet;
            panel.scaleMode = main.scaleMode;
            panel.referenceResolution = main.referenceResolution;
            panel.screenMatchMode = main.screenMatchMode;
            panel.match = main.match;
            panel.clearColor = false;
            panel.sortingOrder = main.sortingOrder + 100;
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
            return panel;
        }

        public static StyleSheet Style()
        {
            // Градиент затемнения меню: Clamp, иначе фильтрация подмешивает тёмный левый край к правому (видна линия).
            const string shade = Dir + "/ShadeGradient.png";
            if (AssetImporter.GetAtPath(shade) is TextureImporter importer && importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
        }
    }
}
