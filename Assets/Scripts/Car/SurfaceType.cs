using UnityEngine;

namespace RandomCar.Car
{
    public enum SurfaceKind { Asphalt, Kerb, Gravel, Grass, Sand, Dirt, Cobble }

    /// Тип покрытия: сцепление, сопротивление, неровности, эффекты. Ассеты лежат в Assets/Settings/Surfaces.
    [CreateAssetMenu(menuName = "RandomCar/Surface Type")]
    public sealed class SurfaceType : ScriptableObject
    {
        public string DisplayName = "Асфальт";
        public SurfaceKind Kind = SurfaceKind.Asphalt;
        [Tooltip("Множитель сцепления шин")] public float Grip = 1f;
        [Tooltip("Дополнительное сопротивление качению, доля нагрузки")] public float RollingResistance;
        [Tooltip("Амплитуда неровностей, м")] public float Bumpiness;
        [Tooltip("Длина волны неровностей, м")] public float BumpWavelength = 1.2f;
        [Tooltip("Оставляет следы шин")] public bool SkidMarks = true;
        [Tooltip("Цвет следов")] public Color SkidColor = new Color(0.08f, 0.08f, 0.09f, 0.75f);
        [Tooltip("Пылит при движении")] public bool Dusty;
        public Color DustColor = new Color(0.89f, 0.77f, 0.54f, 1f);
    }
}
