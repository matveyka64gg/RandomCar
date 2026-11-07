using System.Collections.Generic;
using RandomCar.Events;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Пул эффектов хаоса в сцене Game: многослойные взрывы, фейерверки и вспышки света. Создаётся заранее,
    /// в игре только запускается (ChaosFx).
    public static class ChaosFxBuilder
    {
        /// В «безумии» взрывов несколько в секунду — пул вдвое больше, чем в v4.
        public const int Blasts = 24;
        public const int Scorches = 28;
        public const int Fireworks = 10;
        public const int Lights = 3;

        public static ChaosFx Build(Transform parent)
        {
            var root = new GameObject("ChaosFx");
            root.transform.SetParent(parent, false);
            var fx = root.AddComponent<ChaosFx>();

            var blasts = new List<ChaosFx.BlastRig>();
            for (int i = 0; i < Blasts; i++)
            {
                var rig = new GameObject("Blast_" + i).transform;
                rig.SetParent(root.transform, false);
                blasts.Add(new ChaosFx.BlastRig
                {
                    Root = rig,
                    Flash = FxFactory.Create(FxFactory.Kind.Flash, rig, "Flash"),
                    Fireball = FxFactory.Create(FxFactory.Kind.Fireball, rig, "Fireball"),
                    Smoke = FxFactory.Create(FxFactory.Kind.Smoke, rig, "Smoke"),
                    Debris = FxFactory.Create(FxFactory.Kind.Debris, rig, "Debris"),
                    Sparks = FxFactory.Create(FxFactory.Kind.Sparks, rig, "Sparks"),
                    Ring = FxFactory.Create(FxFactory.Kind.Ring, rig, "Ring"),
                    Confetti = FxFactory.Create(FxFactory.Kind.Confetti, rig, "Confetti"),
                    Stars = FxFactory.Create(FxFactory.Kind.Stars, rig, "Stars"),
                });
            }

            var fireworks = new List<ChaosFx.FireworkRig>();
            for (int i = 0; i < Fireworks; i++)
            {
                var rig = new GameObject("Firework_" + i).transform;
                rig.SetParent(root.transform, false);
                fireworks.Add(new ChaosFx.FireworkRig
                {
                    Root = rig,
                    Trail = FxFactory.Create(FxFactory.Kind.FireworkTrail, rig, "Trail"),
                    Burst = FxFactory.Create(FxFactory.Kind.FireworkBurst, rig, "Burst"),
                    Glitter = FxFactory.Create(FxFactory.Kind.Glitter, rig, "Glitter"),
                });
            }

            var lights = new List<Light>();
            for (int i = 0; i < Lights; i++)
            {
                var go = new GameObject("Flash_" + i);
                go.transform.SetParent(root.transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.range = 30f;
                l.intensity = 0f;
                l.enabled = false;
                lights.Add(l);
            }
            // Следы на дороге: копоть и трещины (один пул, материал меняется при использовании).
            DecalFactory.EnsureAssets();
            var scorches = new List<Renderer>();
            for (int i = 0; i < Scorches; i++)
            {
                var decal = DecalFactory.Flat("Scorch_" + i, root.transform, DecalFactory.Disc, DecalFactory.Crater, Vector3.zero, Vector3.one);
                decal.SetActive(false);
                scorches.Add(decal.GetComponent<Renderer>());
            }
            fx.Configure(blasts, fireworks, lights, scorches, DecalFactory.Crater, DecalFactory.Cracks);
            return fx;
        }
    }
}
