using UnityEngine;

namespace RandomCar.Events
{
    /// «Обломки» (карта «Тель-Авив»): после перехвата в небе на выбранное место трассы падает горящий обломок.
    /// Предупреждение — метка на дороге и падающий след в последнюю секунду. После удара на части дороги
    /// лежат куски (настоящие препятствия с коллайдерами) до конца события; проезд рядом всегда свободен.
    /// Правила те же, что у «Ракеты», вместо которой событие выпадает на этой карте (веса не меняются).
    public sealed class DebrisEvent : RaceEvent
    {
        const float FallSeconds = 1.1f;
        const float StartHeight = 220f;

        Transform fragment;
        TelegraphMarker marker;
        ParticleSystem explosion;
        ParticleSystem smoke;
        ParticleSystem sparks;
        ParticleSystem trail;
        Transform[] pieces;
        Vector3 right;
        Vector3 forward;

        void Awake()
        {
            fragment = Child("Fragment");
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            explosion = Fx("FxExplosion");
            smoke = Fx("FxSmoke");
            sparks = Fx("FxSparks");
            trail = fragment.Find("FxTrail")?.GetComponent<ParticleSystem>();
            var list = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in transform)
                if (t.name.StartsWith("Piece")) list.Add(t);
            pieces = list.ToArray();
        }

        protected override void OnTelegraph()
        {
            Radius = FairnessLimits.MaxHazardRadius;
            var h = Spot(Radius);
            Center = h.Position;
            right = h.Right;
            forward = h.Forward;
            marker.Show(Center, h.Forward, Radius, false, false);
            fragment.gameObject.SetActive(false);
            foreach (var p in pieces) p.gameObject.SetActive(false);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            float fallStart = WarningDuration - FallSeconds;
            if (time < fallStart) return;
            if (!fragment.gameObject.activeSelf)
            {
                fragment.gameObject.SetActive(true);
                if (trail != null) trail.Play(true);
            }
            // Обломок падает наискосок с высоты, кувыркаясь.
            float f = Mathf.Clamp01((time - fallStart) / FallSeconds);
            Vector3 from = Center + Vector3.up * StartHeight - forward * 60f + right * 25f;
            fragment.SetPositionAndRotation(Vector3.Lerp(from, Center, f * f), Quaternion.Euler(time * 300f, time * 170f, 0f));
        }

        protected override void OnActivate()
        {
            fragment.gameObject.SetActive(false);
            marker.Hide();
            Play(explosion, Center + Vector3.up * 0.5f);
            Play(smoke, Center);
            Play(sparks, Center + Vector3.up * 0.3f);
            ShakeCameraNear(Center, 0.5f, 60f);

            // Куски ложатся в пределах зоны (не шире допустимого радиуса опасности).
            for (int i = 0; i < pieces.Length; i++)
            {
                float a = i * 2.4f;
                float r = Radius * (0.25f + 0.55f * ((i * 37) % 10) / 10f);
                Vector3 pos = Center + right * Mathf.Cos(a) * r + forward * Mathf.Sin(a) * r * 1.4f;
                pieces[i].SetPositionAndRotation(pos, Quaternion.Euler(0f, i * 67f, (i % 2) * 12f));
                pieces[i].gameObject.SetActive(true);
            }

            const float reach = FairnessLimits.MaxHazardRadius + 1.2f;
            foreach (var racer in RacersNear(Center, reach))
            {
                Vector3 away = racer.Car.position - Center;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = right;
                racer.Controller.ApplyImpulse(away.normalized * 4f + Vector3.up * 2.5f);
                racer.Controller.ApplySlow(0.72f, 1.0f);
            }
        }

        protected override void Cleanup()
        {
            fragment.gameObject.SetActive(false);
            marker.Hide();
            foreach (var p in pieces) p.gameObject.SetActive(false);
        }
    }
}
