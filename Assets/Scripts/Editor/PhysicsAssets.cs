using System.Collections.Generic;
using RandomCar.Car;
using UnityEditor;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Ассеты физики: типы покрытий, характеристики машин (CarSpec), материал кузова, шаг физики.
    /// Характеристики создаются один раз; дальше их правят в инспекторе. Сбросить к значениям из кода —
    /// меню RandomCar/Reset Car Specs.
    public static class PhysicsAssets
    {
        public const string SurfacesDir = "Assets/Settings/Surfaces";
        public const string SpecsDir = "Assets/Settings/Cars";
        const string BodyMaterialPath = "Assets/Settings/PM_CarBody.asset";

        /// Шаг физики 100 Гц: жёсткой подвеске на лучах нужен частый шаг.
        public const float FixedStep = 0.01f;

        public static void ConfigurePhysics()
        {
            // В редакторе запись в Time.fixedDeltaTime сохраняется в ProjectSettings/TimeManager.asset.
            Time.fixedDeltaTime = FixedStep;
            Time.maximumDeltaTime = 0.05f;
            AssetDatabase.SaveAssets();
        }

        public static PhysicsMaterial BodyMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BodyMaterialPath);
            if (m == null)
            {
                m = new PhysicsMaterial("PM_CarBody");
                AssetDatabase.CreateAsset(m, BodyMaterialPath);
            }
            // Кузов скользит вдоль стен и соседей, а не цепляется: аркадные столкновения.
            m.dynamicFriction = 0.15f;
            m.staticFriction = 0.15f;
            m.bounciness = 0.12f;
            m.frictionCombine = PhysicsMaterialCombine.Minimum;
            m.bounceCombine = PhysicsMaterialCombine.Average;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- покрытия ----------

        public static Dictionary<SurfaceKind, SurfaceType> EnsureSurfaces()
        {
            ArtLibrary.EnsureFolder(SurfacesDir);
            var result = new Dictionary<SurfaceKind, SurfaceType>
            {
                [SurfaceKind.Asphalt] = Surface("S_Asphalt", "Асфальт", SurfaceKind.Asphalt, 1f, 0f, 0f, 1f, true, Hex("#141416", 0.8f), false, Color.white),
                [SurfaceKind.Kerb] = Surface("S_Kerb", "Бордюр", SurfaceKind.Kerb, 0.92f, 0f, 0.004f, 0.6f, true, Hex("#141416", 0.7f), false, Color.white),
                [SurfaceKind.Gravel] = Surface("S_Gravel", "Гравий", SurfaceKind.Gravel, 0.62f, 0.07f, 0.03f, 0.9f, true, Hex("#8A6A43", 0.55f), true, Hex("#E3C48A", 0.8f)),
                [SurfaceKind.Grass] = Surface("S_Grass", "Трава", SurfaceKind.Grass, 0.55f, 0.05f, 0.022f, 1.6f, true, Hex("#3E5A2A", 0.5f), true, Hex("#B9A774", 0.55f)),
                [SurfaceKind.Sand] = Surface("S_Sand", "Песок", SurfaceKind.Sand, 0.58f, 0.09f, 0.025f, 1.1f, true, Hex("#B98A55", 0.5f), true, Hex("#F0C987", 0.85f)),
                [SurfaceKind.Dirt] = Surface("S_Dirt", "Грунт", SurfaceKind.Dirt, 0.78f, 0.03f, 0.015f, 1.3f, true, Hex("#6E4E33", 0.55f), true, Hex("#C9A273", 0.7f)),
                [SurfaceKind.Cobble] = Surface("S_Cobble", "Брусчатка", SurfaceKind.Cobble, 0.9f, 0.01f, 0.007f, 0.25f, true, Hex("#141416", 0.6f), false, Color.white),
            };
            AssetDatabase.SaveAssets();
            return result;
        }

        static SurfaceType Surface(string file, string title, SurfaceKind kind, float grip, float rolling, float bump, float wavelength,
                                   bool skids, Color skid, bool dusty, Color dust)
        {
            string path = $"{SurfacesDir}/{file}.asset";
            var s = AssetDatabase.LoadAssetAtPath<SurfaceType>(path);
            if (s == null)
            {
                s = ScriptableObject.CreateInstance<SurfaceType>();
                AssetDatabase.CreateAsset(s, path);
            }
            s.DisplayName = title;
            s.Kind = kind;
            s.Grip = grip;
            s.RollingResistance = rolling;
            s.Bumpiness = bump;
            s.BumpWavelength = wavelength;
            s.SkidMarks = skids;
            s.SkidColor = skid;
            s.Dusty = dusty;
            s.DustColor = dust;
            EditorUtility.SetDirty(s);
            return s;
        }

        static Color Hex(string hex, float alpha)
        {
            var c = ArtLibrary.Hex(hex);
            c.a = alpha;
            return c;
        }

        // ---------- машины ----------

        [MenuItem("RandomCar/Reset Car Specs")]
        public static void ResetCarSpecs() => EnsureCarSpecs(true);

        /// Характеристики машин. Разные по характеру (масса, руль, сцепление, занос, полёт, удары), близкие по времени круга.
        public static Dictionary<string, CarSpec> EnsureCarSpecs(bool overwrite = false)
        {
            ArtLibrary.EnsureFolder(SpecsDir);
            var result = new Dictionary<string, CarSpec>();
            foreach (string key in CarRoster.Keys)
            {
                string path = $"{SpecsDir}/Spec_{key}.asset";
                var spec = AssetDatabase.LoadAssetAtPath<CarSpec>(path);
                bool created = spec == null;
                if (created)
                {
                    spec = ScriptableObject.CreateInstance<CarSpec>();
                    AssetDatabase.CreateAsset(spec, path);
                }
                if (created || overwrite)
                {
                    Fill(spec, key);
                    EditorUtility.SetDirty(spec);
                }
                result[key] = spec;
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        public static void Fill(CarSpec s, string key)
        {
            s.Engine = new EngineSpec();
            s.Gearbox = new GearboxSpec();
            s.Turbo = new TurboSpec();
            s.Front = new AxleSpec();
            s.Rear = new AxleSpec { BrakeShare = 0.4f, PeakSlipAngle = 8f };
            switch (key)
            {
                case "Iskra":
                    s.DisplayName = "Искра";
                    s.Description = "Лёгкое купе с задним приводом. Маленькая быстрая турбина, острый руль, охотно идёт в занос.";
                    s.Mass = 1090f;
                    s.CenterOfMass = new Vector3(0f, 0.36f, 0.02f);
                    s.Drive = DriveType.RearWheel;
                    Engine(s, idle: 1000f, peakRpm: 4900f, max: 8000f, torque: 232f, low: 0.5f, high: 0.82f, braking: 62f, launch: 4100f);
                    Gears(s, 3.9f, 0.15f, 3.35f, 2.42f, 1.88f, 1.53f, 1.30f, 1.16f);
                    Turbo(s, boost: 0.7f, perBar: 0.55f, spoolStart: 2800f, full: 4500f, lag: 0.32f, decay: 0.38f);
                    Axles(s, frontGrip: 1.32f, rearGrip: 1.25f, frontPeak: 7f, rearPeak: 7.5f, slide: 0.70f, travel: 0.22f, frontHz: 2.0f, rearHz: 2.1f, antiRollF: 0.9f, antiRollR: 0.6f);
                    s.BrakeDecel = 1.25f;
                    s.HandbrakeForce = 1.0f;
                    s.MaxSteerAngle = 36f;
                    s.Drag = 0.62f;
                    s.Downforce = 0.9f;
                    s.TopSpeedKmh = 206f;
                    s.LooseSurfaceGrip = 1f;
                    Character(s, steer: 270f, airLevel: 5.5f, airDamp: 1.4f, airSteer: 3.0f, knock: 0.9f, bounce: 0.22f, friction: 0.15f, landing: 1.15f);
                    break;
                case "Kometa":
                    s.DisplayName = "Комета";
                    s.Description = "Тяжёлый GT с задним приводом и широкими шинами. Большая турбина с заметной задержкой, лучшая скорость на прямых.";
                    s.Mass = 1345f;
                    s.CenterOfMass = new Vector3(0f, 0.38f, -0.04f);
                    s.InertiaScale = new Vector3(1.15f, 1.2f, 1f);
                    s.Drive = DriveType.RearWheel;
                    Engine(s, idle: 850f, peakRpm: 4200f, max: 7000f, torque: 332f, low: 0.55f, high: 0.75f, braking: 85f, launch: 3500f);
                    Gears(s, 3.59f, 0.18f, 3.15f, 2.22f, 1.70f, 1.38f, 1.17f, 1.03f);
                    Turbo(s, boost: 1.0f, perBar: 0.52f, spoolStart: 3200f, full: 5100f, lag: 0.85f, decay: 0.5f);
                    Axles(s, frontGrip: 1.32f, rearGrip: 1.35f, frontPeak: 7f, rearPeak: 8f, slide: 0.74f, travel: 0.22f, frontHz: 1.8f, rearHz: 1.9f, antiRollF: 0.85f, antiRollR: 0.6f);
                    s.BrakeDecel = 1.15f;
                    s.MaxSteerAngle = 33f;
                    s.Drag = 0.5f;
                    s.Downforce = 1.15f;
                    s.TopSpeedKmh = 218f;
                    s.LooseSurfaceGrip = 0.95f;
                    Character(s, steer: 225f, airLevel: 6.5f, airDamp: 1.8f, airSteer: 2.4f, knock: 1.05f, bounce: 0.12f, friction: 0.14f, landing: 0.95f);
                    break;
                case "Pchelka":
                    s.DisplayName = "Пчёлка";
                    s.Description = "Раллийный хэтчбек с полным приводом. Ровный разгон, мягкая подвеска, лучше всех на гравии и песке.";
                    s.Mass = 1260f;
                    s.CenterOfMass = new Vector3(0f, 0.4f, 0.08f);
                    s.Drive = DriveType.AllWheel;
                    s.AwdFrontShare = 0.42f;
                    Engine(s, idle: 950f, peakRpm: 4000f, max: 7200f, torque: 296f, low: 0.6f, high: 0.76f, braking: 75f, launch: 4200f);
                    Gears(s, 3.89f, 0.14f, 3.25f, 2.25f, 1.72f, 1.40f, 1.19f, 1.06f);
                    Turbo(s, boost: 0.9f, perBar: 0.45f, spoolStart: 2400f, full: 4000f, lag: 0.55f, decay: 0.45f);
                    Axles(s, frontGrip: 1.30f, rearGrip: 1.32f, frontPeak: 8f, rearPeak: 9f, slide: 0.84f, travel: 0.3f, frontHz: 1.6f, rearHz: 1.7f, antiRollF: 0.7f, antiRollR: 0.6f);
                    s.BrakeDecel = 1.15f;
                    s.HandbrakeForce = 1.05f;
                    s.MaxSteerAngle = 35f;
                    s.Drag = 0.7f;
                    s.Downforce = 0.7f;
                    s.TopSpeedKmh = 196f;
                    s.LooseSurfaceGrip = 1.32f;
                    Character(s, steer: 260f, airLevel: 7.5f, airDamp: 1.9f, airSteer: 3.2f, knock: 1.0f, bounce: 0.15f, friction: 0.16f, landing: 0.8f);
                    break;
                case "Bastion":
                    s.DisplayName = "Бастион";
                    s.Description = "Роскошный рамный внедорожник: тяжёлый, высокий, с мощным битурбо-мотором. Не боится грунта и песка, но в поворотах кренится и не любит резких манёвров.";
                    s.Mass = 2450f;
                    s.CenterOfMass = new Vector3(0f, 0.62f, 0.05f);
                    s.InertiaScale = new Vector3(1.2f, 1.25f, 1.1f);
                    s.Drive = DriveType.AllWheel;
                    s.AwdFrontShare = 0.45f;
                    Engine(s, idle: 700f, peakRpm: 3200f, max: 6500f, torque: 560f, low: 0.62f, high: 0.72f, braking: 120f, launch: 3000f);
                    Gears(s, 3.45f, 0.20f, 4.40f, 2.86f, 1.92f, 1.37f, 1.0f, 0.82f, 0.70f);
                    Turbo(s, boost: 0.9f, perBar: 0.45f, spoolStart: 1800f, full: 3200f, lag: 0.6f, decay: 0.5f);
                    Axles(s, frontGrip: 1.14f, rearGrip: 1.17f, frontPeak: 8f, rearPeak: 9f, slide: 0.76f, travel: 0.32f, frontHz: 1.45f, rearHz: 1.55f, antiRollF: 1.1f, antiRollR: 0.85f);
                    s.Front.LoadSensitivity = 0.16f;
                    s.Rear.LoadSensitivity = 0.16f;
                    s.BrakeDecel = 1.02f;
                    s.MaxSteerAngle = 32f;
                    s.Drag = 0.95f;
                    s.Downforce = 0.2f;
                    s.TopSpeedKmh = 215f;
                    s.LooseSurfaceGrip = 1.2f;
                    Character(s, steer: 150f, airLevel: 8.5f, airDamp: 2.4f, airSteer: 1.6f, knock: 1.6f, bounce: 0.05f, friction: 0.12f, landing: 0.45f);
                    break;
                case "Bizon":
                    s.DisplayName = "Бизон";
                    s.Description = "Американский маслкар с большим атмосферным V8. Мощный и тяжёлый, задний привод легко срывается в занос; тормоза и сцепление скромнее, чем у спорткаров.";
                    s.Mass = 1720f;
                    s.CenterOfMass = new Vector3(0f, 0.42f, 0.10f);
                    s.InertiaScale = new Vector3(1.15f, 1.2f, 1.05f);
                    s.Drive = DriveType.RearWheel;
                    Engine(s, idle: 750f, peakRpm: 4500f, max: 6800f, torque: 620f, low: 0.66f, high: 0.78f, braking: 110f, launch: 3200f);
                    Gears(s, 3.55f, 0.18f, 3.0f, 2.05f, 1.55f, 1.22f, 1.0f, 0.82f);
                    NoTurbo(s);
                    Axles(s, frontGrip: 1.22f, rearGrip: 1.16f, frontPeak: 7f, rearPeak: 7.5f, slide: 0.66f, travel: 0.24f, frontHz: 1.7f, rearHz: 1.8f, antiRollF: 0.9f, antiRollR: 0.5f);
                    s.BrakeDecel = 1.05f;
                    s.HandbrakeForce = 1.2f;
                    s.MaxSteerAngle = 33f;
                    s.Drag = 0.82f;
                    s.Downforce = 0.6f;
                    s.TopSpeedKmh = 255f;
                    s.LooseSurfaceGrip = 0.95f;
                    Character(s, steer: 195f, airLevel: 5.5f, airDamp: 1.5f, airSteer: 2.2f, knock: 1.2f, bounce: 0.10f, friction: 0.14f, landing: 0.8f);
                    break;
                case "Taiga":
                    s.DisplayName = "Тайга";
                    s.Description = "Подготовленный пикап для бездорожья: длинноходная подвеска, грязевые шины, полный привод. На грунте и песке почти не теряет скорость, на асфальте тяжёлый и валкий.";
                    s.Mass = 2550f;
                    s.CenterOfMass = new Vector3(0f, 0.72f, 0.10f);
                    s.InertiaScale = new Vector3(1.25f, 1.3f, 1.15f);
                    s.Drive = DriveType.AllWheel;
                    s.AwdFrontShare = 0.48f;
                    Engine(s, idle: 700f, peakRpm: 3400f, max: 6000f, torque: 640f, low: 0.66f, high: 0.72f, braking: 130f, launch: 2800f);
                    Gears(s, 3.9f, 0.20f, 4.2f, 2.6f, 1.75f, 1.30f, 1.0f, 0.80f);
                    Turbo(s, boost: 0.8f, perBar: 0.42f, spoolStart: 1700f, full: 3000f, lag: 0.55f, decay: 0.5f);
                    Axles(s, frontGrip: 1.06f, rearGrip: 1.09f, frontPeak: 9f, rearPeak: 10f, slide: 0.80f, travel: 0.42f, frontHz: 1.2f, rearHz: 1.3f, antiRollF: 0.9f, antiRollR: 0.7f);
                    s.Front.LoadSensitivity = 0.18f;
                    s.Rear.LoadSensitivity = 0.18f;
                    s.BrakeDecel = 0.98f;
                    s.MaxSteerAngle = 31f;
                    s.Drag = 1.10f;
                    s.Downforce = 0.1f;
                    s.TopSpeedKmh = 190f;
                    s.LooseSurfaceGrip = 1.38f;
                    Character(s, steer: 140f, airLevel: 7.5f, airDamp: 2.1f, airSteer: 1.8f, knock: 1.7f, bounce: 0.07f, friction: 0.13f, landing: 0.5f);
                    break;
                case "Lastochka":
                    s.DisplayName = "Ласточка";
                    s.Description = "Редкое купе 60-х: тонкие шины с белыми боковинами, рядная шестёрка без наддува. Лёгкая и послушная, но держит дорогу хуже современных машин.";
                    s.Mass = 1150f;
                    s.CenterOfMass = new Vector3(0f, 0.42f, 0.05f);
                    s.Drive = DriveType.RearWheel;
                    Engine(s, idle: 850f, peakRpm: 4500f, max: 6800f, torque: 300f, low: 0.62f, high: 0.80f, braking: 70f, launch: 3500f);
                    Gears(s, 3.9f, 0.22f, 3.2f, 2.1f, 1.45f, 1.1f, 0.88f);
                    NoTurbo(s);
                    Axles(s, frontGrip: 1.12f, rearGrip: 1.10f, frontPeak: 7.5f, rearPeak: 8f, slide: 0.67f, travel: 0.26f, frontHz: 1.5f, rearHz: 1.6f, antiRollF: 0.7f, antiRollR: 0.4f);
                    s.BrakeDecel = 0.98f;
                    s.MaxSteerAngle = 34f;
                    s.Drag = 0.55f;
                    s.Downforce = 0.2f;
                    s.TopSpeedKmh = 205f;
                    s.LooseSurfaceGrip = 1.0f;
                    Character(s, steer: 175f, airLevel: 4.8f, airDamp: 1.2f, airSteer: 2.6f, knock: 0.85f, bounce: 0.25f, friction: 0.15f, landing: 1.2f);
                    break;
                case "Grom":
                    s.DisplayName = "Гром";
                    s.Description = "Среднемоторный суперкар с атмосферным V10 и полным приводом. Огромное сцепление и прижим, резкий отклик; на грунте широкие шины скользят.";
                    s.Mass = 1480f;
                    s.CenterOfMass = new Vector3(0f, 0.36f, -0.18f);
                    s.InertiaScale = new Vector3(1.0f, 1.0f, 1.0f);
                    s.Drive = DriveType.AllWheel;
                    s.AwdFrontShare = 0.35f;
                    Engine(s, idle: 1000f, peakRpm: 6500f, max: 8700f, torque: 560f, low: 0.55f, high: 0.86f, braking: 90f, launch: 5000f);
                    Gears(s, 3.7f, 0.10f, 3.1f, 2.2f, 1.7f, 1.37f, 1.14f, 0.97f, 0.84f);
                    NoTurbo(s);
                    Axles(s, frontGrip: 1.45f, rearGrip: 1.50f, frontPeak: 6.5f, rearPeak: 7f, slide: 0.74f, travel: 0.18f, frontHz: 2.3f, rearHz: 2.4f, antiRollF: 1.0f, antiRollR: 0.8f);
                    s.BrakeDecel = 1.35f;
                    s.MaxSteerAngle = 32f;
                    s.Drag = 0.62f;
                    s.Downforce = 2.0f;
                    s.TopSpeedKmh = 300f;
                    s.LooseSurfaceGrip = 0.85f;
                    Character(s, steer: 300f, airLevel: 6.5f, airDamp: 2.0f, airSteer: 2.8f, knock: 1.1f, bounce: 0.10f, friction: 0.14f, landing: 0.8f);
                    break;
                case "Sapsan":
                    s.DisplayName = "Сапсан";
                    s.Description = "Гиперкар с битурбо V8 и задним приводом: самый быстрый в гараже и самый строгий. Большая турбина подхватывает резко, на выходе из поворотов нужен аккуратный газ.";
                    s.Mass = 1400f;
                    s.CenterOfMass = new Vector3(0f, 0.35f, -0.20f);
                    s.Drive = DriveType.RearWheel;
                    Engine(s, idle: 1000f, peakRpm: 5500f, max: 8500f, torque: 560f, low: 0.5f, high: 0.84f, braking: 95f, launch: 4500f);
                    Gears(s, 3.5f, 0.09f, 3.0f, 2.15f, 1.68f, 1.36f, 1.14f, 0.97f, 0.84f);
                    Turbo(s, boost: 1.3f, perBar: 0.5f, spoolStart: 3000f, full: 5200f, lag: 0.7f, decay: 0.45f);
                    Axles(s, frontGrip: 1.48f, rearGrip: 1.55f, frontPeak: 6.5f, rearPeak: 7f, slide: 0.72f, travel: 0.17f, frontHz: 2.4f, rearHz: 2.5f, antiRollF: 1.0f, antiRollR: 0.75f);
                    s.BrakeDecel = 1.4f;
                    s.MaxSteerAngle = 31f;
                    s.Drag = 0.58f;
                    s.Downforce = 2.6f;
                    s.TopSpeedKmh = 330f;
                    s.LooseSurfaceGrip = 0.82f;
                    s.Rear.SlideGrip = 0.64f;
                    Character(s, steer: 290f, airLevel: 6.0f, airDamp: 1.7f, airSteer: 2.4f, knock: 1.05f, bounce: 0.10f, friction: 0.14f, landing: 0.75f);
                    break;
                default: // Myata
                    s.DisplayName = "Мята";
                    s.Description = "Ретро-родстер с передним приводом. Лёгкий, устойчивый и прощающий ошибки; маленькая отзывчивая турбина.";
                    s.Mass = 960f;
                    s.CenterOfMass = new Vector3(0f, 0.35f, 0.12f);
                    s.Drive = DriveType.FrontWheel;
                    Engine(s, idle: 1000f, peakRpm: 5200f, max: 7800f, torque: 214f, low: 0.52f, high: 0.8f, braking: 58f, launch: 4300f);
                    Gears(s, 3.85f, 0.15f, 3.3f, 2.35f, 1.82f, 1.48f, 1.25f, 1.11f);
                    Turbo(s, boost: 0.65f, perBar: 0.55f, spoolStart: 2600f, full: 4200f, lag: 0.3f, decay: 0.35f);
                    Axles(s, frontGrip: 1.30f, rearGrip: 1.34f, frontPeak: 7f, rearPeak: 8.5f, slide: 0.75f, travel: 0.22f, frontHz: 2.0f, rearHz: 2.0f, antiRollF: 0.95f, antiRollR: 0.5f);
                    s.BrakeDecel = 1.22f;
                    s.MaxSteerAngle = 36f;
                    s.Drag = 0.66f;
                    s.Downforce = 0.85f;
                    s.TopSpeedKmh = 198f;
                    s.LooseSurfaceGrip = 1f;
                    Character(s, steer: 250f, airLevel: 4.5f, airDamp: 1.1f, airSteer: 3.4f, knock: 0.75f, bounce: 0.32f, friction: 0.16f, landing: 1.4f);
                    break;
            }
        }

        /// Характер: скорость руля, поведение в воздухе, стойкость к ударам, упругость кузова, отскок при приземлении.
        static void Character(CarSpec s, float steer, float airLevel, float airDamp, float airSteer, float knock, float bounce, float friction, float landing)
        {
            s.SteerSpeed = steer;
            s.AirLeveling = airLevel;
            s.AirDamping = airDamp;
            s.AirSteer = airSteer;
            s.KnockResistance = knock;
            s.Bounciness = bounce;
            s.BodyFriction = friction;
            s.LandingBounce = landing;
        }

        static void Engine(CarSpec s, float idle, float peakRpm, float max, float torque, float low, float high, float braking, float launch)
        {
            var e = s.Engine;
            e.IdleRpm = idle;
            e.PeakTorqueRpm = peakRpm;
            e.MaxRpm = max;
            e.PeakTorque = torque;
            e.TorqueAtIdle = low;
            e.TorqueAtMax = high;
            e.EngineBraking = braking;
            e.LaunchRpm = launch;
        }

        static void Gears(CarSpec s, float final, float shiftTime, params float[] ratios)
        {
            s.Gearbox.FinalDrive = final;
            s.Gearbox.ShiftTime = shiftTime;
            s.Gearbox.Ratios = ratios;
        }

        /// Атмосферный двигатель: без турбины (давление 0, без свиста и клапана).
        static void NoTurbo(CarSpec s)
        {
            Turbo(s, boost: 0f, perBar: 0f, spoolStart: 2000f, full: 4000f, lag: 0.5f, decay: 0.5f);
        }

        static void Turbo(CarSpec s, float boost, float perBar, float spoolStart, float full, float lag, float decay)
        {
            var t = s.Turbo;
            t.MaxBoost = boost;
            t.TorquePerBar = perBar;
            t.SpoolStartRpm = spoolStart;
            t.FullBoostRpm = full;
            t.Lag = lag;
            t.Decay = decay;
            t.BlowOffThreshold = boost * 0.4f;
        }

        static void Axles(CarSpec s, float frontGrip, float rearGrip, float frontPeak, float rearPeak, float slide, float travel,
                          float frontHz, float rearHz, float antiRollF, float antiRollR)
        {
            s.Front.LongitudinalGrip = frontGrip * 0.97f;
            s.Front.LateralGrip = frontGrip;
            s.Front.PeakSlipAngle = frontPeak;
            s.Front.SlideGrip = slide;
            s.Front.Travel = travel;
            s.Front.SpringFrequency = frontHz;
            s.Front.AntiRoll = antiRollF;
            s.Front.BrakeShare = 0.62f;
            s.Rear.LongitudinalGrip = rearGrip * 0.97f;
            s.Rear.LateralGrip = rearGrip;
            s.Rear.PeakSlipAngle = rearPeak;
            s.Rear.SlideGrip = slide;
            s.Rear.Travel = travel;
            s.Rear.SpringFrequency = rearHz;
            s.Rear.AntiRoll = antiRollR;
            s.Rear.BrakeShare = 0.38f;
        }
    }
}
