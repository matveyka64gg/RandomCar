using RandomCar.Audio;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Events
{
    /// Безумный руль: машине, ради которой трюк, на несколько секунд достаётся нелепое управление —
    /// руль наоборот, икота (машина подпрыгивает), чих (рывки вперёд), балерина (крутится на месте)
    /// или «пьяный» руль. Над машиной — надпись и звёздочки. Игрок и ИИ получают это одинаково.
    public sealed class CrazyGag : GagEvent
    {
        static readonly (ControlGag gag, float seconds, string text)[] Variants =
        {
            (ControlGag.Inverted, 3f, "РУЛЬ НАОБОРОТ!"),
            (ControlGag.Hiccups, 3.3f, "ИКОТА!"),
            (ControlGag.Sneeze, 3f, "АП-ПА-ПА…"),
            (ControlGag.Ballet, 1.6f, "БАЛЕРИНА!"),
            (ControlGag.Wobbly, 3f, "ПЬЯНЫЙ РУЛЬ!"),
        };

        ParticleSystem stars;
        int variant;
        float starsAt;

        void Awake() => stars = Fx("FxStars");

        protected override void OnGagTelegraph()
        {
            variant = Mathf.Clamp(Mathf.FloorToInt(RandomRange(0f, Variants.Length)), 0, Variants.Length - 1);
            var target = Target;
            if (target == null) return;
            ChaosEffects?.Sound(SoundBank.Warp, target.Car.position, 0.7f, 1.5f);
            ChaosEffects?.Pop(target.Car.position + Vector3.up * 2.6f, "?!", new Color(0.7f, 0.5f, 1f), 1.2f);
        }

        protected override void OnActivate()
        {
            var target = Target;
            if (target == null) return;
            var v = Variants[variant];
            if (Simulates(target)) target.Controller.ApplyControlGag(v.gag, v.seconds);
            ChaosEffects?.Pop(target.Car.position + Vector3.up * 2.8f, v.text, new Color(0.75f, 0.55f, 1f), 1.35f);
            ChaosEffects?.Sound(v.gag == ControlGag.Ballet ? SoundBank.Ding : SoundBank.Warp, target.Car.position, 0.8f, 0.8f);
        }

        protected override void OnActiveUpdate(float progress, float time)
        {
            var target = Target;
            if (target == null || Time.time < starsAt) return;
            // Звёздочки над головой, пока действует.
            starsAt = Time.time + 0.6f;
            Play(stars, target.Car.position + Vector3.up * 1.8f);
            Center = target.Car.position;
        }

        protected override void Cleanup() => Stop(stars);
    }
}
