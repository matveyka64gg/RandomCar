using UnityEngine;

namespace RandomCar.Events
{
    /// Гигантский мяч: тень и линия траектории поперёк дороги, затем мяч катится через дорогу и отбрасывает машину боком.
    public sealed class BallEvent : RaceEvent
    {
        const float BallRadius = 1.5f;
        /// Мяч выкатывается из-за обочины: столько метров за краем дороги.
        const float StartBeyondRoad = 5f;
        const float RollTime = 2.4f;

        Transform ball;
        TelegraphMarker marker;
        ParticleSystem dust;
        Vector3 axis;
        Vector3 right;
        Vector3 forward;
        float side;
        float spin;

        void Awake()
        {
            ball = Child("Ball");
            marker = Child("Marker").GetComponent<TelegraphMarker>();
            dust = Fx("FxDust");
        }

        protected override void OnTelegraph()
        {
            Radius = BallRadius;
            var h = Spot(1f);
            axis = h.Axis;
            right = h.Right;
            forward = h.Forward;
            side = RandomRange(0f, 1f) < 0.5f ? -1f : 1f;
            Center = axis + right * side * (RoadHalf + StartBeyondRoad);
            spin = 0f;
            ball.gameObject.SetActive(true);
            ball.localScale = Vector3.one;
            ball.SetPositionAndRotation(Center, Quaternion.identity);
            marker.Show(axis, -side * right, 1.6f, false, true, 6f);
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            float bounce = Mathf.Abs(Mathf.Sin(time * 4f)) * 0.6f * (1f - progress);
            ball.position = Center + Vector3.up * bounce;
        }

        protected override void OnActivate()
        {
            marker.Hide();
            Play(dust, Center);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            float travel = Mathf.Lerp(side * (RoadHalf + StartBeyondRoad), -side * (RoadHalf + StartBeyondRoad), time / RollTime);
            Vector3 previous = ball.position;
            Center = axis + right * travel;
            ball.position = Center + Vector3.up * Mathf.Abs(Mathf.Sin(time * 5f)) * 0.25f;
            float moved = Vector3.Distance(previous, ball.position);
            spin += moved / BallRadius * Mathf.Rad2Deg;
            ball.rotation = Quaternion.AngleAxis(spin * side, forward);
            if (dust != null) dust.transform.position = Center;

            foreach (var racer in RacersNear(Center, BallRadius + 1.1f))
            {
                if (!FirstHit(racer.Controller)) continue;
                racer.Controller.ApplyImpulse(-side * right * 8f + Vector3.up * 2f);
                ShakeCameraNear(Center, 0.3f, 20f);
            }
        }

        protected override void OnEndUpdate(float progress)
        {
            ball.localScale = Vector3.one * (1f - progress);
        }

        protected override void Cleanup()
        {
            marker.Hide();
            Stop(dust);
            ball.gameObject.SetActive(false);
        }
    }
}
