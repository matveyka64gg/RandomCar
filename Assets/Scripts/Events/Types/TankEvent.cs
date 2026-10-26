using UnityEngine;

namespace RandomCar.Events
{
    /// Танк: выезжает из-под земли за отбойником, наводит башню, красный луч 2 с.
    /// Выстрел оставляет кратер на 4 с, внутри кратера замедление.
    public sealed class TankEvent : RaceEvent
    {
        const float Sink = 3.2f;

        Transform tank;
        Transform turret;
        Transform barrel;
        Transform crater;
        LineRenderer laser;
        TelegraphMarker marker;
        ParticleSystem muzzle;
        ParticleSystem explosion;
        ParticleSystem smoke;
        Vector3 tankPosition;
        Quaternion barrelBase;

        void Awake()
        {
            tank = Child("Tank");
            turret = FindDeep(tank, "Turret");
            barrel = FindDeep(tank, "Barrel");
            crater = Child("Crater");
            laser = Child("Laser").GetComponent<LineRenderer>();
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            muzzle = Fx("FxMuzzle");
            explosion = Fx("FxExplosion");
            smoke = Fx("FxSmoke");
            barrelBase = barrel.localRotation;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name || t.name.StartsWith(name + "_")) return t;
            return null;
        }

        Vector3 MuzzlePoint => barrel.TransformPoint(new Vector3(0f, 0f, 2.75f));

        protected override void OnTelegraph()
        {
            Radius = 2.6f;
            var h = Predict(Target, Definition.WarningSeconds + 0.4f, Radius);
            Center = h.Position;
            float side = h.Lateral >= 0f ? 1f : -1f;
            var spot = At(h.Progress + 14f / Context.Track.Length, 0f);
            tankPosition = spot.Axis + spot.Right * side * 25f;
            Vector3 look = Center - tankPosition;
            look.y = 0f;
            tank.gameObject.SetActive(true);
            tank.SetPositionAndRotation(tankPosition - Vector3.up * Sink, Quaternion.LookRotation(look.normalized));
            turret.localRotation = Quaternion.Euler(0f, side * 70f, 0f) * Quaternion.identity;
            barrel.localRotation = barrelBase;
            crater.gameObject.SetActive(false);
            laser.enabled = false;
            marker.Show(Center, h.Forward, Radius, false, false);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            tank.position = tankPosition - Vector3.up * Sink * (1f - EaseOutBack(Mathf.Clamp01(time / 0.6f)));

            // Наведение башни на точку удара.
            Vector3 toTarget = Center - turret.position;
            toTarget.y = 0f;
            Quaternion aim = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            turret.rotation = Quaternion.Slerp(turret.rotation, aim, 1f - Mathf.Exp(-5f * Time.deltaTime));

            if (time > 0.7f)
            {
                float blink = Mathf.Lerp(6f, 22f, progress);
                laser.enabled = Mathf.Sin(time * blink) > -0.3f;
                laser.SetPosition(0, MuzzlePoint);
                laser.SetPosition(1, Center + Vector3.up * 0.05f);
            }
        }

        protected override void OnActivate()
        {
            laser.enabled = false;
            marker.Hide();
            Play(muzzle, MuzzlePoint);
            Play(explosion, Center + Vector3.up * 0.4f);
            Play(smoke, Center);
            crater.gameObject.SetActive(true);
            crater.SetPositionAndRotation(Center + Vector3.up * 0.02f, Quaternion.Euler(0f, Context.Random.Range(0f, 360f), 0f));
            crater.localScale = Vector3.one * Radius;
            ShakeCameraNear(Center, 0.5f, 50f);

            foreach (var racer in RacersNear(Center, Radius + 1f))
            {
                Vector3 away = racer.Car.position - Center;
                away.y = 0f;
                racer.Controller.ApplyImpulse(away.normalized * 5f + Vector3.up * 3f);
                racer.Controller.ApplySlow(0.65f, 1.0f);
            }
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            float recoil = time < 0.3f ? Mathf.Sin(time / 0.3f * Mathf.PI) : 0f;
            barrel.localRotation = barrelBase * Quaternion.Euler(-recoil * 8f, 0f, 0f);
            foreach (var racer in RacersNear(Center, Radius))
                racer.Controller.ApplySlow(0.6f, 1.0f);
        }

        protected override void OnEndUpdate(float progress)
        {
            crater.localScale = Vector3.one * Radius * (1f - progress);
            tank.position = tankPosition - Vector3.up * Sink * progress;
        }

        protected override void Cleanup()
        {
            laser.enabled = false;
            marker.Hide();
            crater.gameObject.SetActive(false);
            tank.gameObject.SetActive(false);
        }
    }
}
