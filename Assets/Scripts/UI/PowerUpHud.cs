using RandomCar.Car;
using RandomCar.Core;
using RandomCar.PowerUps;
using RandomCar.Race;
using UnityEngine;
using UnityEngine.UIElements;

namespace RandomCar.UI
{
    /// Интерфейс бонусов в гонке (внизу слева, не закрывает дорогу): значок и название бонуса в запасе, подсказка
    /// с клавишей, полоска патронов или времени, вспышка при подборе и применении; рамка «ЦЕЛЬ» над машиной,
    /// в которую летит твоя ракета; предупреждение «РАКЕТА В ТЕБЯ!»; сообщение о попадании; полоска защиты
    /// после возврата на трассу и щита.
    public sealed class PowerUpHud
    {
        readonly RaceManager race;
        readonly VisualElement root;
        readonly VisualElement card, bar, barFill, protect, protectFill, marker, incoming, toast;
        readonly PowerUpIcon icon;
        readonly Label title, hint, protectLabel, markerLabel, incomingLabel, toastLabel;
        PowerUpDirector hooked;
        PowerUpKind shown = PowerUpKind.None;
        float pulseUntil, toastUntil, incomingUntil, refusedUntil;
        int markerTarget = -1;
        float markerUntil;
        string refusedText;

        public PowerUpHud(VisualElement hud, RaceManager raceManager)
        {
            race = raceManager;
            root = hud;
            card = El("item-card", hud);
            icon = new PowerUpIcon();
            var frame = El("item-frame", card);
            frame.Add(icon);
            var texts = El("item-texts", card);
            title = Text("НЕТ БОНУСА", "item-title", texts);
            hint = Text("проезжай через ящики «?»", "item-hint", texts);
            bar = El("item-bar", texts);
            barFill = El("item-bar-fill", bar);

            protect = El("protect-chip", hud);
            protectLabel = Text("ЗАЩИТА", "protect-label", protect);
            var protectBar = El("protect-bar", protect);
            protectFill = El("protect-fill", protectBar);

            marker = El("target-marker", hud);
            markerLabel = Text("ЦЕЛЬ", "target-label", marker);
            incoming = El("incoming", hud);
            incomingLabel = Text("", "", incoming);
            toast = El("hit-toast", hud);
            toastLabel = Text("", "", toast);
            foreach (var e in new[] { card, protect, marker, incoming, toast }) e.pickingMode = PickingMode.Ignore;
        }

        static VisualElement El(string cls, VisualElement parent)
        {
            var e = new VisualElement();
            if (!string.IsNullOrEmpty(cls)) e.AddToClassList(cls);
            e.pickingMode = PickingMode.Ignore;
            parent.Add(e);
            return e;
        }

        static Label Text(string text, string cls, VisualElement parent)
        {
            var l = new Label(text);
            if (!string.IsNullOrEmpty(cls)) l.AddToClassList(cls);
            l.pickingMode = PickingMode.Ignore;
            parent.Add(l);
            return l;
        }

        void Hook()
        {
            var director = PowerUpDirector.Current;
            if (director == hooked) return;
            Unhook();
            hooked = director;
            if (hooked == null) return;
            hooked.Granted += OnGranted;
            hooked.Used += OnUsed;
            hooked.HitLanded += OnHit;
            hooked.RocketLocked += OnLocked;
            hooked.PickupRefused += OnRefused;
            CarController.Blocked += OnBlocked;
        }

        public void Unhook()
        {
            if (hooked == null) return;
            hooked.Granted -= OnGranted;
            hooked.Used -= OnUsed;
            hooked.HitLanded -= OnHit;
            hooked.RocketLocked -= OnLocked;
            hooked.PickupRefused -= OnRefused;
            CarController.Blocked -= OnBlocked;
            hooked = null;
        }

        int MySlot => race.Player != null ? race.Player.Slot : -1;

        string NameOf(int slot) => race.BySlot(slot)?.Name ?? "?";

        void OnGranted(int slot, PowerUpKind kind, int box)
        {
            if (slot == MySlot) pulseUntil = Time.time + 0.5f;
        }

        void OnUsed(PowerUpUse use)
        {
            if (use.Slot == MySlot) pulseUntil = Time.time + 0.35f;
        }

        void OnLocked(PowerUpKind kind, int owner, int target)
        {
            if (owner == MySlot)
            {
                markerTarget = target;
                markerUntil = Time.time + (kind == PowerUpKind.LeaderRocket ? 16f : 6f);
            }
            if (target == MySlot)
            {
                incomingLabel.text = kind == PowerUpKind.LeaderRocket ? $"РАКЕТА НА ЛИДЕРА! ({NameOf(owner)})" : $"РАКЕТА В ТЕБЯ! ({NameOf(owner)})";
                incomingUntil = Time.time + (kind == PowerUpKind.LeaderRocket ? 6f : 3f);
                ChaosFxSound(Audio.SoundBank.LockOn);
            }
        }

        static void ChaosFxSound(AudioClip clip)
        {
            var cam = Camera.main;
            if (Events.ChaosFx.Instance != null && cam != null) Events.ChaosFx.Instance.Sound(clip, cam.transform.position, 0.8f, 1f);
        }

        void OnHit(PowerUpKind kind, int owner, int victim, bool absorbed)
        {
            int me = MySlot;
            if (owner != me && victim != me) return;
            if (owner == me && victim >= 0 && victim == markerTarget) markerUntil = 0f;
            string what = kind switch
            {
                PowerUpKind.HomingRocket => "РАКЕТА",
                PowerUpKind.LeaderRocket => "РАКЕТА НА ЛИДЕРА",
                PowerUpKind.MachineGun => "ПУЛЕМЁТ",
                PowerUpKind.Mine => "МИНА",
                PowerUpKind.Shock => "РАЗРЯД",
                PowerUpKind.Horn => "КЛАКСОН",
                _ => "БОНУС",
            };
            if (absorbed) toastLabel.text = victim == me ? $"{what}: ЗАЩИТА ВЫДЕРЖАЛА!" : $"{what}: {NameOf(victim)} под защитой";
            else if (victim == me) toastLabel.text = $"{what} ОТ {NameOf(owner).ToUpperInvariant()}!";
            else toastLabel.text = kind == PowerUpKind.LeaderRocket ? $"ЛИДЕР {NameOf(victim).ToUpperInvariant()} В ПОЛЁТЕ!" : $"{what}: ПОПАДАНИЕ — {NameOf(victim).ToUpperInvariant()}!";
            toast.EnableInClassList("mine", victim != me);
            toastUntil = Time.time + 2.2f;
        }

        void OnRefused(int slot)
        {
            if (slot != MySlot || hooked == null) return;
            var def = hooked.Settings.Get(hooked.HeldBy(slot));
            refusedText = def != null ? $"сначала используй: {def.Title}" : "сначала используй бонус";
            refusedUntil = Time.time + 2f;
        }

        void OnBlocked(CarController car, string what)
        {
            if (race.Player == null || car != race.Player.Controller) return;
            pulseUntil = Mathf.Max(pulseUntil, Time.time + 0.2f);
        }

        static string KeyName()
        {
            string path = string.IsNullOrEmpty(GameSettings.ItemKey) ? PlayerDriver.DefaultItemKey : GameSettings.ItemKey;
            return UnityEngine.InputSystem.InputControlPath.ToHumanReadableString(path, UnityEngine.InputSystem.InputControlPath.HumanReadableStringOptions.OmitDevice).ToUpperInvariant();
        }

        /// Полигон испытаний: бонусов нет — карточка, защита и метки скрыты.
        public bool Hidden { get; set; }

        public void Update(Racer player)
        {
            Hook();
            bool on = !Hidden && hooked != null && player != null && race.Phase != RacePhase.Idle;
            card.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on)
            {
                protect.style.display = marker.style.display = incoming.style.display = toast.style.display = DisplayStyle.None;
                return;
            }
            var kind = hooked.HeldBy(player.Slot);
            float gun = hooked.GunLeft(player.Slot);
            var c = player.Controller;
            if (kind != shown)
            {
                shown = kind;
                pulseUntil = Mathf.Max(pulseUntil, Time.time + 0.4f);
            }
            var def = hooked.Settings.Get(kind);
            // Значок — бонус в запасе; пусто — действующий эффект (пулемёт, щит).
            icon.Kind = kind != PowerUpKind.None ? kind : gun > 0f ? PowerUpKind.MachineGun : c.Shielded ? PowerUpKind.Shield : kind;
            var iconDef = hooked.Settings.Get(icon.Kind);
            icon.Accent = iconDef != null ? iconDef.Color : Color.white;
            card.EnableInClassList("held", kind != PowerUpKind.None);
            card.EnableInClassList("pulse", Time.time < pulseUntil);
            if (kind != PowerUpKind.None && def != null)
            {
                title.text = def.Title.ToUpperInvariant();
                hint.text = $"[{KeyName()}] — {def.Hint}";
            }
            else if (gun > 0f)
            {
                title.text = "ПУЛЕМЁТ";
                hint.text = "стреляет вперёд";
            }
            else
            {
                title.text = "НЕТ БОНУСА";
                hint.text = Time.time < refusedUntil ? refusedText : "проезжай через ящики «?»";
            }
            if (Time.time < refusedUntil && kind != PowerUpKind.None) hint.text = refusedText;
            // Полоска: патроны пулемёта, время турбо и щита.
            float fill = gun > 0f ? gun : c.Shielded ? c.ShieldFraction : 0f;
            bar.style.display = fill > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            barFill.style.width = Length.Percent(fill * 100f);

            // Защита после возврата на трассу.
            bool protectedNow = c.Protected;
            protect.style.display = protectedNow ? DisplayStyle.Flex : DisplayStyle.None;
            if (protectedNow)
            {
                protectLabel.text = $"ЗАЩИТА {c.ProtectionLeft:0.0} с";
                protectFill.style.width = Length.Percent(c.ProtectionFraction * 100f);
            }

            UpdateMarker();
            incoming.style.display = Time.time < incomingUntil ? DisplayStyle.Flex : DisplayStyle.None;
            incoming.EnableInClassList("blink", Mathf.Repeat(Time.time * 4f, 1f) < 0.5f);
            toast.style.display = Time.time < toastUntil ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// Рамка над машиной-целью твоей ракеты (пока ракета летит).
        void UpdateMarker()
        {
            var cam = Camera.main;
            var target = markerTarget >= 0 ? race.BySlot(markerTarget) : null;
            if (Time.time > markerUntil || target == null || target.Car == null || cam == null || root.panel == null || target.Finished)
            {
                marker.style.display = DisplayStyle.None;
                return;
            }
            Vector3 screen = cam.WorldToScreenPoint(target.Car.position + Vector3.up * 1.2f);
            if (screen.z <= 0.5f)
            {
                marker.style.display = DisplayStyle.None;
                return;
            }
            var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
            marker.style.display = DisplayStyle.Flex;
            marker.style.left = p.x;
            marker.style.top = p.y;
            float size = Mathf.Clamp(900f / Mathf.Max(screen.z, 1f), 46f, 150f);
            marker.style.width = size;
            marker.style.height = size;
            markerLabel.text = $"ЦЕЛЬ · {target.Name}";
            marker.style.rotate = new Rotate(Mathf.Sin(Time.time * 6f) * 6f);
        }
    }
}
