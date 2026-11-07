using RandomCar.Audio;
using UnityEngine;

namespace RandomCar.Events
{
    /// Наковальня: на дороге растёт тень, сверху свистит — и с неба падает тонна железа туда, где окажется машина.
    /// Попал — «ДЗАНГ!»: машину сплющивает в лепёшку, она вязнет и подпрыгивает; рядом — пыль и ударная волна.
    /// Наковальня остаётся лежать на дороге препятствием, потом проваливается сквозь асфальт.
    public sealed class AnvilGag : GagEvent
    {
        const float Height = 38f;
        const float HitRadius = 2.9f;

        Transform anvil;
        Collider shape;
        TelegraphMarker marker;
        Vector3 ground;
        float whistleAt;

        void Awake()
        {
            anvil = Child("Anvil");
            shape = anvil.GetComponent<Collider>();
            anvil.gameObject.SetActive(false);
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnGagTelegraph()
        {
            ground = Ground(Point.Position);
            anvil.SetPositionAndRotation(ground + Vector3.up * Height, Quaternion.LookRotation(Quaternion.Euler(0f, RandomRange(-40f, 40f), 0f) * Point.Forward));
            anvil.gameObject.SetActive(true);
            if (shape != null) shape.enabled = false;
            marker.Show(ground, Point.Forward, 1.2f, false, false);
            whistleAt = 0f;
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            // Тень растёт, наковальня падает с ускорением и чуть кувыркается.
            marker.Show(ground, Point.Forward, Mathf.Lerp(0.6f, 2.4f, progress), false, false);
            marker.Pulse(progress);
            anvil.position = ground + Vector3.up * Height * (1f - progress * progress);
            if (whistleAt == 0f)
            {
                whistleAt = Time.time;
                ChaosEffects?.Sound(SoundBank.Whistle, ground + Vector3.up * 10f, 0.9f, 0.55f);
            }
        }

        protected override void OnActivate()
        {
            marker.Hide();
            anvil.position = ground;
            var fx = ChaosEffects;
            fx?.Sound(SoundBank.Anvil, ground, 1f, Random.Range(0.95f, 1.05f));
            fx?.Blast(ground, BlastStyle.ColorPuff, 1.4f, new Color(0.75f, 0.7f, 0.6f, 0.8f));
            fx?.Blast(ground + Vector3.up * 1.2f, BlastStyle.Spark, 1.2f);
            fx?.Scorch(ground, 4f, true);
            fx?.Pop(ground + Vector3.up * 3f, "ДЗАНГ!", new Color(0.85f, 0.9f, 1f), 1.7f);
            ShakeCameraNear(ground, 0.7f, 40f);
            foreach (var racer in SimulatedNear(ground, HitRadius))
            {
                var car = racer.Controller;
                car.ApplySquash(new Vector3(1.4f, 0.28f, 1.35f), 2.2f, "сплющен наковальней");
                car.ApplySlow(0.25f, 1.5f);
                car.ApplyLaunch(Vector3.up * 5f + FlatDir(racer.Car.position - ground, Point.Forward) * 6f, Vector3.up * 4f, "сплющен наковальней");
                fx?.Pop(racer.Car.position + Vector3.up * 1.4f, "ПЛЮЩ!", new Color(1f, 0.55f, 0.55f), 1.5f);
            }
            fx?.Shockwave(ground, 8f, 11f, 1);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            // После удара наковальня подпрыгивает и замирает.
            float bounce = time < 0.5f ? Mathf.Abs(Mathf.Sin(time * 12f)) * Mathf.Exp(-time * 8f) * 0.8f : 0f;
            anvil.position = ground + Vector3.up * bounce;
            // Препятствием становится, когда сплющенная машина выбралась (иначе её выбросит из-под наковальни).
            if (shape != null && !shape.enabled && time > 0.8f && !AnySimulatedNear(ground, 1.8f)) shape.enabled = true;
        }

        protected override void OnEnd()
        {
            if (shape != null) shape.enabled = false;
        }

        protected override void OnEndUpdate(float progress) => anvil.position = ground - Vector3.up * 2f * progress;

        protected override void Cleanup()
        {
            marker.Hide();
            anvil.gameObject.SetActive(false);
        }
    }
}
