using System;
using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.PowerUps
{
    /// Бонус, который можно подобрать в ящике «?» на трассе и применить клавишей (по умолчанию E).
    /// Новый вид: значение здесь, описание в PowerUpSettings.DefaultItems и ветка в PowerUpDirector.Execute.
    public enum PowerUpKind : byte
    {
        None,
        /// Самонаводящаяся ракета: ближайший соперник впереди (или рядом).
        HomingRocket,
        /// Ракета на лидера: летит над трассой к первому месту и пикирует.
        LeaderRocket,
        /// Пулемёт на капоте: очереди вперёд, каждое попадание толкает.
        MachineGun,
        /// Турбо: мощный разгон на пару секунд.
        Turbo,
        /// Суперпрыжок: машину резко подбрасывает (перепрыгнуть ловушку).
        SuperJump,
        /// Электроразряд: молнии в соседей — их трясёт, руль «пьяный», скорость падает.
        Shock,
        /// Щит: несколько секунд ничего не действует, соседи отскакивают от пузыря.
        Shield,
        /// Мина: оставить ловушку позади.
        Mine,
        /// Суперклаксон: звуковая волна расшвыривает всех вокруг.
        Horn,
    }

    /// Описание бонуса: тексты для интерфейса, частота выпадения в зависимости от места в гонке, цвет.
    [Serializable]
    public sealed class PowerUpDefinition
    {
        public PowerUpKind Kind;
        public string Title;
        /// Как применить и что будет (подсказка рядом со значком).
        public string Hint;
        /// Вес выпадения у лидера и у последнего (между ними — по месту): отстающим чаще достаются ускорения и ракеты.
        public float FrontWeight = 1f;
        public float BackWeight = 1f;
        public Color Color = Color.white;

        public float WeightFor(float rankFraction) => Mathf.Max(0f, Mathf.Lerp(FrontWeight, BackWeight, Mathf.Clamp01(rankFraction)));
    }

    /// Единственное место для настроек бонусов: виды и веса, расстановка ящиков, сила и длительность эффектов.
    [CreateAssetMenu(menuName = "RandomCar/Power-Up Settings")]
    public sealed class PowerUpSettings : ScriptableObject
    {
        public List<PowerUpDefinition> Items = DefaultItems();

        [Header("Ящики на трассе")]
        /// Ряд ящиков через каждые столько метров трассы (место ряда сдвигается к прямой или ко входу в поворот).
        public float RowSpacing = 120f;
        /// Ящик снова появляется через, с.
        public float RespawnSeconds = 6f;
        /// Радиус подбора, м (машина проезжает сквозь ящик).
        public float PickupRadius = 2.6f;

        [Header("Ракеты")]
        public float RocketSpeed = 58f;
        public float RocketLifetime = 6f;
        public float RocketRange = 200f;
        public float LeaderRocketSpeed = 80f;
        public float LeaderRocketLifetime = 16f;

        [Header("Пулемёт")]
        public float GunSeconds = 4f;
        public int GunRounds = 48;
        public float BulletSpeed = 170f;
        public float BulletRange = 120f;
        /// Толчок от одного попадания, м/с: очередь сбивает машину с траектории.
        public float BulletPush = 2.4f;

        [Header("Усилители")]
        public float TurboSeconds = 2.5f;
        public float ShieldSeconds = 6f;
        public float ShockRadius = 16f;
        public float HornRadius = 18f;
        public float MineLifetime = 30f;

        public PowerUpDefinition Get(PowerUpKind kind) => Items.Find(i => i.Kind == kind);

        public static List<PowerUpDefinition> DefaultItems() => new List<PowerUpDefinition>
        {
            Def(PowerUpKind.HomingRocket, "Самонаводящаяся ракета", "летит в ближайшего соперника впереди", 1.0f, 1.2f, "#FF4B3A"),
            Def(PowerUpKind.LeaderRocket, "Ракета на лидера", "летит над трассой к первому месту", 0f, 0.35f, "#3A8BFF"),
            Def(PowerUpKind.MachineGun, "Пулемёт", "4 с очередей вперёд, попадания толкают", 0.9f, 1.0f, "#FFC21A"),
            Def(PowerUpKind.Turbo, "Турбо", "мощный разгон на 2.5 с", 0.6f, 1.4f, "#FF8A1F"),
            Def(PowerUpKind.SuperJump, "Суперпрыжок", "подбрасывает вверх — перепрыгни ловушку", 0.8f, 0.7f, "#A6F23A"),
            Def(PowerUpKind.Shock, "Электроразряд", "молнии в соседей рядом: трясёт и тормозит", 0.6f, 0.9f, "#5FE3FF"),
            Def(PowerUpKind.Shield, "Щит", "6 с ничего не берёт, соседи отскакивают", 1.1f, 0.5f, "#24E0C8"),
            Def(PowerUpKind.Mine, "Мина", "оставить ловушку позади", 1.2f, 0.5f, "#D7263D"),
            Def(PowerUpKind.Horn, "Суперклаксон", "звуковая волна расшвыривает всех вокруг", 0.8f, 0.8f, "#FF5FA2"),
        };

        static PowerUpDefinition Def(PowerUpKind kind, string title, string hint, float front, float back, string hex) => new PowerUpDefinition
        {
            Kind = kind, Title = title, Hint = hint, FrontWeight = front, BackWeight = back,
            Color = ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white,
        };

        /// Бонус из ящика для участника на месте rank (0 — лидер) из count: веса по месту, случайность — от вызывающего.
        public PowerUpKind Roll(int rank, int count, double roll)
        {
            float fraction = count > 1 ? rank / (float)(count - 1) : 0.5f;
            float total = 0f;
            foreach (var i in Items) total += i.WeightFor(fraction);
            if (total <= 0f) return PowerUpKind.HomingRocket;
            float pick = (float)roll * total;
            foreach (var i in Items)
            {
                float w = i.WeightFor(fraction);
                if (pick < w) return i.Kind;
                pick -= w;
            }
            return Items[Items.Count - 1].Kind;
        }
    }
}
