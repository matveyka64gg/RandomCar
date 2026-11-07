using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Car
{
    /// Огни машины по её состоянию: стоп-сигналы (и третий стоп) ярче при торможении и на стартовой решётке,
    /// фонари заднего хода — на задней передаче, аварийка мигает, пока машина кувыркается или лежит на боку,
    /// ночью (тёмная карта) ярче фары и включается луч ближнего света. Свечение меняется блоком свойств
    /// материала (без копий материалов) и только когда состояние изменилось. Сетевая копия — по присланному вводу.
    [RequireComponent(typeof(CarPhysics))]
    public sealed class CarLights : MonoBehaviour
    {
        enum Kind { Head, Drl, Tail, Reverse, Amber }

        struct Slot
        {
            public Renderer Renderer;
            public int Index;
            public Kind Kind;
            public Color Emission;
        }

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        /// Луч ближнего света (выключен днём).
        [SerializeField] Light beam;

        CarPhysics physics;
        CarController car;
        readonly List<Slot> slots = new List<Slot>();
        readonly float[] shown = new float[5];
        MaterialPropertyBlock block;
        float brakeHold;
        float night = -1f;
        float nightCheckAt;

        public void Configure(Light headlightBeam) => beam = headlightBeam;

        /// Сейчас горят стоп-сигналы / задний ход / аварийка (проверки).
        public bool BrakeOn { get; private set; }
        public bool ReverseOn { get; private set; }
        public bool HazardOn { get; private set; }

        /// Яркость стоп-сигнала относительно материала (проверки).
        public float TailLevel => shown[(int)Kind.Tail];

        void Awake()
        {
            physics = GetComponent<CarPhysics>();
            car = GetComponent<CarController>();
            block = new MaterialPropertyBlock();
            var model = transform.Find("Model");
            if (model == null) return;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty(EmissionColor)) continue;
                    Kind? kind = m.name switch
                    {
                        "M_LightHead" => Kind.Head,
                        "M_LightDrl" => Kind.Drl,
                        "M_LightTail" => Kind.Tail,
                        "M_LightReverse" => Kind.Reverse,
                        "M_LightAmber" => Kind.Amber,
                        _ => null,
                    };
                    if (kind.HasValue)
                        slots.Add(new Slot { Renderer = r, Index = i, Kind = kind.Value, Emission = m.GetColor(EmissionColor) });
                }
            }
            for (int i = 0; i < shown.Length; i++) shown[i] = -1f;
        }

        /// Темнота карты 0..1 по солнцу: «Тель-Авив» ночью — 1, дневные карты — 0, вечер — немного.
        static float Darkness()
        {
            var sun = RenderSettings.sun;
            if (sun == null || !sun.isActiveAndEnabled) return 0f;
            float byIntensity = Mathf.Clamp01((1.2f - sun.intensity) / 0.8f);
            float elevation = -sun.transform.forward.y;   // синус высоты солнца
            float byElevation = Mathf.Clamp01((0.42f - elevation) / 0.3f) * 0.4f;
            return Mathf.Max(byIntensity, byElevation);
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= nightCheckAt)
            {
                // Карта и её свет меняются редко (загрузка трассы, гараж) — проверка раз в секунду.
                nightCheckAt = Time.unscaledTime + 1f;
                night = Darkness();
                if (beam != null) beam.enabled = night > 0.5f && isActiveAndEnabled;
            }

            var input = physics.Input;
            bool live = physics.Simulated || physics.RemoteDriven;
            bool reversing = live && physics.Drivetrain.Reversing;
            // Педаль тормоза (на задней передаче газ и тормоз меняются местами), блокировка на старте.
            float pedal = reversing ? input.Throttle : input.Brake;
            bool braking = physics.Locked || (live && pedal > 0.12f && Mathf.Abs(physics.ForwardSpeed) > 0.3f) || (live && input.Handbrake);
            // Короткое «послесвечение»: тормоз по 1 кадру (ИИ, сеть) не мерцает.
            brakeHold = braking ? 0.12f : Mathf.Max(0f, brakeHold - Time.deltaTime);
            BrakeOn = brakeHold > 0f;
            ReverseOn = reversing && Mathf.Abs(physics.ForwardSpeed) < 25f;
            HazardOn = live && (physics.IsUpsideDown || (car != null && car.Tumbling));

            float dark = Mathf.Max(0f, night);
            float blink = HazardOn && Mathf.Repeat(Time.time * 1.6f, 1f) < 0.5f ? 1f : 0f;
            Set(Kind.Head, 1f + 2.4f * dark);
            Set(Kind.Drl, 1f + 0.6f * dark);
            // Габариты ночью ярче (видно соперника впереди), стоп-сигнал — заметно ярче габаритов.
            Set(Kind.Tail, BrakeOn ? 2.8f + 0.8f * dark : 0.55f + 0.9f * dark);
            Set(Kind.Reverse, ReverseOn ? 9f : 0.2f);
            Set(Kind.Amber, HazardOn ? (blink > 0f ? 2.4f : 0.12f) : 0.35f);
        }

        /// Яркость группы огней (множитель свечения материала); блоки свойств меняются только при изменении.
        void Set(Kind kind, float level)
        {
            int k = (int)kind;
            if (Mathf.Abs(shown[k] - level) < 0.01f) return;
            shown[k] = level;
            foreach (var s in slots)
            {
                if (s.Kind != kind || s.Renderer == null) continue;
                s.Renderer.GetPropertyBlock(block, s.Index);
                block.SetColor(EmissionColor, s.Emission * level);
                s.Renderer.SetPropertyBlock(block, s.Index);
            }
        }
    }
}
