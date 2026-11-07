using System;
using System.Collections.Generic;
using RandomCar.Car;
using UnityEngine;
using UnityEngine.UIElements;
using static RandomCar.UI.UiKit;

namespace RandomCar.UI
{
    /// Выбор цвета: палитра готовых цветов и ползунки тона, насыщенности и яркости.
    /// Изменение сразу видно на машине (onChange), сохранение делает владелец.
    public sealed class ColorPicker
    {
        readonly Action<Color> onChange;
        readonly VisualElement preview;
        readonly Slider hue, sat, val;
        readonly VisualElement hueTrack;
        readonly List<(VisualElement swatch, Color color)> swatches = new List<(VisualElement, Color)>();
        bool silent;

        public VisualElement Root { get; }

        public ColorPicker(VisualElement parent, string title, IReadOnlyList<PaletteColor> palette, Color current, Action<Color> changed, bool compact = false)
        {
            onChange = changed;
            Root = El("picker", parent);
            var head = El("picker-head", Root);
            preview = El("picker-preview", head);
            Text(title, "picker-title", head);
            var grid = El(compact ? "picker-grid compact" : "picker-grid", Root);
            foreach (var p in palette)
            {
                var c = p.Color;
                var s = new Button(() => Set(c, true)) { tooltip = p.Name };
                s.AddToClassList("swatch");
                s.style.backgroundColor = c;
                grid.Add(s);
                swatches.Add((s, c));
            }
            hue = PickerSlider("Тон", 0f, 1f, out hueTrack);
            sat = PickerSlider("Насыщенность", 0f, 1f, out _);
            val = PickerSlider("Яркость", 0.02f, 1f, out _);
            hueTrack.AddToClassList("hue-track");
            hueTrack.style.backgroundImage = new StyleBackground(HueTexture());
            foreach (var sl in new[] { hue, sat, val }) sl.RegisterValueChangedCallback(_ => FromSliders());
            Set(current, false);
        }

        static Texture2D hueTexture;

        /// Радуга для дорожки ползунка тона (создаётся один раз).
        static Texture2D HueTexture()
        {
            if (hueTexture != null) return hueTexture;
            hueTexture = new Texture2D(128, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HueStrip" };
            for (int i = 0; i < 128; i++) hueTexture.SetPixel(i, 0, Color.HSVToRGB(i / 127f, 0.85f, 1f));
            hueTexture.Apply();
            return hueTexture;
        }

        Slider PickerSlider(string label, float min, float max, out VisualElement tracker)
        {
            var row = El("picker-row", Root);
            Text(label, "picker-label", row);
            var slider = new Slider(min, max);
            slider.AddToClassList("volume-slider");
            slider.AddToClassList("picker-slider");
            row.Add(slider);
            tracker = slider.Q(className: "unity-base-slider__tracker");
            return slider;
        }

        void FromSliders()
        {
            if (silent) return;
            var c = Color.HSVToRGB(hue.value, sat.value, val.value);
            Show(c);
            onChange?.Invoke(c);
        }

        public void Set(Color c, bool notify)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            silent = true;
            hue.SetValueWithoutNotify(h);
            sat.SetValueWithoutNotify(s);
            val.SetValueWithoutNotify(Mathf.Max(0.02f, v));
            silent = false;
            Show(c);
            if (notify) onChange?.Invoke(c);
        }

        void Show(Color c)
        {
            preview.style.backgroundColor = c;
            foreach (var (swatch, color) in swatches)
                swatch.EnableInClassList("selected", Mathf.Abs(color.r - c.r) + Mathf.Abs(color.g - c.g) + Mathf.Abs(color.b - c.b) < 0.02f);
        }
    }
}
