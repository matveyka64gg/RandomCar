using System;
using RandomCar.Car;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace RandomCar.Garage
{
    /// Ракурс камеры гаража под раздел: общий вид, колесо, корма, борт, крыша.
    public enum GarageView { Overview, Front, Wheel, Rear, LeftSide, RightSide, Top, Side }

    /// Студия гаража: подиум с поворотным кругом вдали от трассы, свет и задник. Показывает машину без физики
    /// и управляет камерой: вращение мышью (зажатая кнопка) или правым стиком, приближение колесом,
    /// плавный переход к ракурсу раздела. Машина в кадре смещена влево — справа панель интерфейса.
    public sealed class GarageStudio : MonoBehaviour
    {
        [SerializeField] Transform turntable;
        [SerializeField] Camera viewCamera;
        [SerializeField] float minDistance = 3.2f;
        [SerializeField] float maxDistance = 11f;

        GameObject preview;
        float yaw = 215f, pitch = 12f, distance = 7.2f, height = 0.75f;
        float targetYaw = 215f, targetPitch = 12f, targetDistance = 7.2f, targetHeight = 0.75f;
        float autoSpin = 6f;
        float idleFor;
        bool userControl;

        public bool Active { get; private set; }
        public CarCosmetics Preview { get; private set; }

        /// Курсор над интерфейсом: вращение и приближение не начинаются (задаёт GameUI).
        public Func<Vector2, bool> PointerOverUi;

        /// Смещение машины в кадре влево, доля расстояния (справа — панель гаража).
        public float FrameOffset = 0.16f;

        public void Configure(Transform table, Camera cam)
        {
            turntable = table;
            viewCamera = cam;
        }

        /// Свой свет студии: без него гараж наследует освещение карты (ночью «Тель-Авива» машина почти не видна).
        /// Солнце карты на время гаража становится ключевым светом студии, рассеянный свет — нейтральным.
        [SerializeField] Color studioSky = new Color(0.56f, 0.58f, 0.64f);
        [SerializeField] Color studioEquator = new Color(0.42f, 0.42f, 0.46f);
        [SerializeField] Color studioGround = new Color(0.2f, 0.2f, 0.22f);
        [SerializeField] Color studioSun = new Color(1f, 0.96f, 0.9f);
        [SerializeField] float studioSunIntensity = 1.35f;
        [SerializeField] Vector3 studioSunEuler = new Vector3(52f, 150f, 0f);

        struct SceneLighting
        {
            public AmbientMode Mode;
            public Color Sky, Equator, Ground;
            public bool Fog;
            public Light Sun;
            public Color SunColor;
            public float SunIntensity;
            public Quaternion SunRotation;
        }

        SceneLighting saved;
        bool lightingOverridden;

        public void Enter()
        {
            Active = true;
            enabled = true;
            UseStudioLighting();
        }

        void UseStudioLighting()
        {
            if (lightingOverridden) return;
            saved = new SceneLighting
            {
                Mode = RenderSettings.ambientMode,
                Sky = RenderSettings.ambientSkyColor,
                Equator = RenderSettings.ambientEquatorColor,
                Ground = RenderSettings.ambientGroundColor,
                Fog = RenderSettings.fog,
                Sun = RenderSettings.sun,
            };
            if (saved.Sun != null)
            {
                saved.SunColor = saved.Sun.color;
                saved.SunIntensity = saved.Sun.intensity;
                saved.SunRotation = saved.Sun.transform.rotation;
                saved.Sun.color = studioSun;
                saved.Sun.intensity = studioSunIntensity;
                saved.Sun.transform.rotation = Quaternion.Euler(studioSunEuler);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = studioSky;
            RenderSettings.ambientEquatorColor = studioEquator;
            RenderSettings.ambientGroundColor = studioGround;
            RenderSettings.fog = false;
            lightingOverridden = true;
        }

        void RestoreSceneLighting()
        {
            if (!lightingOverridden) return;
            RenderSettings.ambientMode = saved.Mode;
            RenderSettings.ambientSkyColor = saved.Sky;
            RenderSettings.ambientEquatorColor = saved.Equator;
            RenderSettings.ambientGroundColor = saved.Ground;
            RenderSettings.fog = saved.Fog;
            if (saved.Sun != null)
            {
                saved.Sun.color = saved.SunColor;
                saved.Sun.intensity = saved.SunIntensity;
                saved.Sun.transform.rotation = saved.SunRotation;
            }
            saved = default;
            lightingOverridden = false;
        }

        void OnDisable() => RestoreSceneLighting();

        public void Exit()
        {
            Active = false;
            RestoreSceneLighting();
            if (preview != null) Destroy(preview);
            preview = null;
            Preview = null;
            previewKey = null;
        }

        string previewKey;

        /// Показать машину со сборкой. Машина кинематическая: колёса стоят в покое, физики нет.
        public void Show(CarEntry entry, CarBuild build, CarCatalog catalog)
        {
            if (preview != null) Destroy(preview);
            preview = Instantiate(entry.Prefab, turntable.position, turntable.rotation, turntable);
            preview.name = "Preview_" + entry.Key;
            var physics = preview.GetComponent<CarPhysics>();
            physics.SetSimulated(false);
            physics.Locked = true;
            // Звук мотора в гараже не нужен.
            foreach (var source in preview.GetComponentsInChildren<AudioSource>(true)) source.enabled = false;
            Preview = preview.GetComponent<CarCosmetics>();
            if (Preview != null) Preview.Apply(build, catalog);
            previewKey = entry.Key;
        }

        /// Та же машина — только перекрасить/переставить детали (без пересоздания).
        public void Refresh(CarEntry entry, CarBuild build, CarCatalog catalog)
        {
            if (preview == null || previewKey != entry.Key) Show(entry, build, catalog);
            else if (Preview != null) Preview.Apply(build, catalog);
        }

        /// Плавно перевести камеру к ракурсу (подиум при этом не крутится сам, пока игрок не отпустит).
        public void SetView(GarageView view)
        {
            userControl = false;
            idleFor = 0f;
            switch (view)
            {
                case GarageView.Front: Target(200f, 10f, 6.4f, 0.65f); break;
                case GarageView.Wheel: Target(245f, 4f, 4.2f, 0.45f); break;
                case GarageView.Rear: Target(35f, 16f, 6.6f, 0.8f); break;
                case GarageView.LeftSide: Target(90f, 6f, 6.8f, 0.7f); break;
                case GarageView.RightSide: Target(270f, 6f, 6.8f, 0.7f); break;
                case GarageView.Side: Target(270f, 4f, 7.2f, 0.7f); break;
                case GarageView.Top: Target(215f, 42f, 7.4f, 0.6f); break;
                default: Target(215f, 12f, 7.2f, 0.75f); break;
            }
        }

        void Target(float y, float p, float d, float h)
        {
            // Ближайший по кругу угол: камера не делает лишний оборот.
            targetYaw = yaw + Mathf.DeltaAngle(yaw, y);
            targetPitch = p;
            targetDistance = d;
            targetHeight = h;
        }

        void LateUpdate()
        {
            if (!Active || viewCamera == null) return;
            float dt = Time.unscaledDeltaTime;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            // Указатель — мышь или палец (телефон): машину вращают перетаскиванием там, где нет панелей интерфейса.
            var pointer = Pointer.current;
            bool overUi = pointer != null && PointerOverUi != null && PointerOverUi(pointer.position.ReadValue());
            bool pressed = pointer != null && pointer.press.isPressed || mouse != null && mouse.rightButton.isPressed;
            bool dragging = pressed && !overUi;
            Vector2 look = Vector2.zero;
            if (dragging && pointer != null) look += pointer.delta.ReadValue() * (pointer is Touchscreen ? 0.18f : 0.25f);
            if (pad != null) look += pad.rightStick.ReadValue() * 120f * dt;
            if (look.sqrMagnitude > 0.0001f)
            {
                idleFor = 0f;
                userControl = true;
                targetYaw += look.x;
                targetPitch = Mathf.Clamp(targetPitch - look.y, 0f, 60f);
            }
            else idleFor += dt;
            // Без участия игрока подиум медленно поворачивается.
            if (idleFor > 6f && userControl) targetYaw += autoSpin * dt;
            if (mouse != null && !overUi)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) targetDistance = Mathf.Clamp(targetDistance - Mathf.Sign(scroll) * 0.45f, minDistance, maxDistance);
            }

            float k = 1f - Mathf.Exp(-6f * dt);
            yaw = Mathf.Lerp(yaw, targetYaw, k);
            pitch = Mathf.Lerp(pitch, targetPitch, k);
            distance = Mathf.Lerp(distance, targetDistance, k);
            height = Mathf.Lerp(height, targetHeight, k);

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focus = turntable.position + Vector3.up * height;
            // Машина левее центра: фокус сдвинут вправо относительно камеры.
            focus += rotation * Vector3.right * (distance * FrameOffset);
            viewCamera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
            viewCamera.fieldOfView = Mathf.Lerp(viewCamera.fieldOfView, 38f, k);
        }
    }
}
