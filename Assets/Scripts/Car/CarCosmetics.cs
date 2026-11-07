using System;
using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Car
{
    /// Роль слота материала в окраске.
    public enum PaintRole { Body, Roof, Hood, Lower, Rim, Caliper, Glass }

    /// Внешний вид машины по сборке (CarBuild): включает вариант обвеса в каждом слоте, ставит диски и шины,
    /// красит кузов, акцентные зоны, обвес, диски и суппорты (MaterialPropertyBlock — без копий материалов),
    /// тонирует стёкла и строит наклейки (CarVinyls). Детали — только картинка: без коллайдеров и массы.
    [RequireComponent(typeof(CarPhysics))]
    public sealed class CarCosmetics : MonoBehaviour
    {
        [Serializable]
        public sealed class PaintSlot
        {
            public Renderer Renderer;
            public int Slot;
            public PaintRole Role;
            /// Слот принадлежит детали обвеса (цвет по PartsColor).
            public bool Kit;
        }

        [Serializable]
        public sealed class KitVariant
        {
            public PartSlot Slot;
            public string Variant;
            public GameObject Object;
        }

        [Serializable]
        public sealed class WheelLook
        {
            public Transform Wheel;
            /// Шина по умолчанию — рендер самого колеса.
            public Renderer StockTire;
            public Renderer StockRim;
            public List<Renderer> TireVariants = new List<Renderer>();
        }

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");
        static readonly int ClearCoat = Shader.PropertyToID("_ClearCoatMask");

        /// Радиус посадки шины для общих моделей дисков (Tools/Blender, диски строятся под 0.24 м).
        public const float PartRimRadius = 0.24f;

        [SerializeField] List<PaintSlot> paintSlots = new List<PaintSlot>();
        [SerializeField] List<KitVariant> kits = new List<KitVariant>();
        [SerializeField] List<WheelLook> wheels = new List<WheelLook>();
        [SerializeField] float rimRadius = 0.24f;
        [SerializeField] float tireWidth = 0.24f;

        readonly List<GameObject> spawnedRims = new List<GameObject>();
        MaterialPropertyBlock block;

        public CarBuild Build { get; private set; }
        public Color PaintColor { get; private set; } = Color.white;
        public IReadOnlyList<KitVariant> Kits => kits;

        public void Configure(List<PaintSlot> slots, List<KitVariant> kitVariants, List<WheelLook> wheelLooks, float stockRimRadius, float width)
        {
            paintSlots = slots;
            kits = kitVariants;
            wheels = wheelLooks;
            rimRadius = stockRimRadius;
            tireWidth = width;
        }

        public bool HasKit(PartSlot slot, string variant) => kits.Exists(k => k.Slot == slot && k.Variant == variant);

        public bool HasTire(string variant)
        {
            if (variant == CarBuild.Stock) return true;
            foreach (var w in wheels)
                foreach (var t in w.TireVariants)
                    if (t != null && t.name.StartsWith($"Tire_{variant}_", StringComparison.Ordinal)) return true;
            return false;
        }

        /// Слоты, в которых у этой машины есть варианты обвеса.
        public IEnumerable<PartSlot> KitSlots()
        {
            var seen = new HashSet<PartSlot>();
            foreach (var k in kits)
                if (k.Variant != CarBuild.Stock && seen.Add(k.Slot)) yield return k.Slot;
        }

        public void Apply(CarBuild build, CarCatalog catalog)
        {
            Build = build;
            ApplyKits(build);
            ApplyWheels(build, catalog);
            ApplyPaint(build.Paint);
            var vinyls = GetComponent<CarVinyls>();
            if (vinyls != null) vinyls.Apply(build.Decals, build.Paint);
        }

        void ApplyKits(CarBuild build)
        {
            // В каждом слоте включён выбранный вариант; если его нет у машины — заводской.
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
            {
                string want = build.Get(slot);
                bool exists = HasKit(slot, want);
                foreach (var k in kits)
                {
                    if (k.Slot != slot || k.Object == null) continue;
                    k.Object.SetActive(exists ? k.Variant == want : k.Variant == CarBuild.Stock);
                }
            }
        }

        void ApplyWheels(CarBuild build, CarCatalog catalog)
        {
            foreach (var go in spawnedRims) if (go != null) Destroy(go);
            spawnedRims.Clear();

            string tire = build.Get(PartSlot.Tires);
            float ratio = TireRimRatio(tire);
            string rimVariant = build.Get(PartSlot.Rims);
            var rimPart = rimVariant != CarBuild.Stock && catalog != null ? catalog.Part(PartSlot.Rims, rimVariant) : null;
            foreach (var w in wheels)
            {
                if (w.Wheel == null) continue;
                bool stockTire = tire == CarBuild.Stock || !HasTire(tire);
                if (w.StockTire != null) w.StockTire.enabled = stockTire;
                foreach (var t in w.TireVariants)
                    if (t != null) t.gameObject.SetActive(!stockTire && t.name.StartsWith($"Tire_{tire}_", StringComparison.Ordinal));
                float fit = stockTire ? 1f : ratio;
                if (w.StockRim != null)
                {
                    w.StockRim.enabled = rimPart == null || rimPart.Prefab == null;
                    w.StockRim.transform.localScale = Vector3.one * fit;
                }
                if (rimPart != null && rimPart.Prefab != null)
                {
                    var go = Instantiate(rimPart.Prefab, w.Wheel, false);
                    go.name = "Rim_" + rimPart.Variant;
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    // Общий диск под 0.24 м: масштаб по радиусу посадки шины, ширина — по ширине колеса.
                    float s = rimRadius * fit / PartRimRadius;
                    go.transform.localScale = new Vector3(tireWidth / PartRimRadius, s, s);
                    foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
                    spawnedRims.Add(go);
                }
            }
        }

        /// Радиус посадки шины относительно заводского (спортивная — низкий профиль, внедорожная — высокий).
        public static float TireRimRatio(string tire) => tire switch
        {
            "Sport" => 1.08f,
            "Offroad" => 0.94f,
            _ => 1f,
        };

        void ApplyPaint(PaintJob paint)
        {
            block ??= new MaterialPropertyBlock();
            PaintColor = paint.Body;
            foreach (var p in paintSlots)
            {
                if (p.Renderer == null) continue;
                Color color;
                PaintFinish finish;
                switch (p.Role)
                {
                    case PaintRole.Glass:
                        p.Renderer.GetPropertyBlock(block, p.Slot);
                        block.SetColor(BaseColor, new Color(0.05f, 0.09f, 0.15f, Mathf.Lerp(0.22f, 0.9f, paint.WindowTint)));
                        p.Renderer.SetPropertyBlock(block, p.Slot);
                        continue;
                    case PaintRole.Rim:
                        color = paint.Rims;
                        finish = paint.RimFinish;
                        break;
                    case PaintRole.Caliper:
                        color = paint.Calipers;
                        finish = PaintFinish.Gloss;
                        break;
                    default:
                        bool accent = UsesAccent(p.Role, paint.Scheme);
                        if (p.Kit && paint.Parts == PartsColor.Black)
                        {
                            color = new Color(0.04f, 0.04f, 0.045f);
                            finish = PaintFinish.Gloss;
                        }
                        else if (accent || (p.Kit && paint.Parts == PartsColor.Accent))
                        {
                            color = paint.Accent;
                            finish = paint.AccentFinish;
                        }
                        else
                        {
                            color = paint.Body;
                            finish = paint.BodyFinish;
                        }
                        break;
                }
                p.Renderer.GetPropertyBlock(block, p.Slot);
                SetFinish(block, color, finish);
                p.Renderer.SetPropertyBlock(block, p.Slot);
            }
            // Детали, появившиеся после настройки (общие диски), красятся по их материалам.
            foreach (var go in spawnedRims) Tint(go, paint);
        }

        static bool UsesAccent(PaintRole role, PaintScheme scheme) => role switch
        {
            PaintRole.Roof => scheme is PaintScheme.Roof or PaintScheme.RoofAndHood or PaintScheme.LowerAndRoof,
            PaintRole.Hood => scheme is PaintScheme.Hood or PaintScheme.RoofAndHood,
            PaintRole.Lower => scheme is PaintScheme.Lower or PaintScheme.LowerAndRoof,
            _ => false,
        };

        /// Покрытие: металличность, гладкость базы и лак.
        public static void SetFinish(MaterialPropertyBlock block, Color color, PaintFinish finish)
        {
            float metallic, smooth, coat;
            switch (finish)
            {
                case PaintFinish.Gloss: metallic = 0.05f; smooth = 0.62f; coat = 1f; break;
                case PaintFinish.Pearl: metallic = 0.45f; smooth = 0.78f; coat = 1f; break;
                case PaintFinish.Satin: metallic = 0.3f; smooth = 0.42f; coat = 0.35f; break;
                case PaintFinish.Matte: metallic = 0.08f; smooth = 0.16f; coat = 0f; break;
                case PaintFinish.Chrome: metallic = 1f; smooth = 0.96f; coat = 0f; break;
                default: metallic = 0.7f; smooth = 0.52f; coat = 1f; break;
            }
            block.SetColor(BaseColor, color);
            block.SetFloat(Metallic, metallic);
            block.SetFloat(Smoothness, smooth);
            block.SetFloat(ClearCoat, coat);
        }

        void Tint(GameObject go, PaintJob paint)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || mats[i].name != "M_Rim") continue;
                    r.GetPropertyBlock(block, i);
                    SetFinish(block, paint.Rims, paint.RimFinish);
                    r.SetPropertyBlock(block, i);
                }
            }
        }
    }
}
