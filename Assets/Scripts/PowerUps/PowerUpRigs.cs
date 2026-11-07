using System;
using UnityEngine;

namespace RandomCar.PowerUps
{
    /// Объекты пула бонусов в сцене Game (собирает PowerUpBuilder): в игре они только включаются и выключаются.

    /// Ракета: две модели (самонаводящаяся и на лидера), дымный след, звук мотора.
    [Serializable]
    public sealed class RocketRig
    {
        public Transform Root;
        public GameObject Homing;
        public GameObject Leader;
        public ParticleSystem Trail;
        public AudioSource Engine;
    }

    /// Пулемёт на крыше машины: стволы (отдача), точки вылета, вспышка у ствола, свой звук очередей.
    [Serializable]
    public sealed class GunRig
    {
        public Transform Root;
        public Transform Barrels;
        public Transform MuzzleLeft;
        public Transform MuzzleRight;
        public ParticleSystem Flash;
        public AudioSource Audio;
    }

    /// Пузырь защиты и щита вокруг машины.
    [Serializable]
    public sealed class BubbleRig
    {
        public Transform Root;
        public Renderer Shell;
    }
}
