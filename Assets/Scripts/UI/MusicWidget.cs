using System;
using RandomCar.Audio;
using RandomCar.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static RandomCar.UI.UiKit;

namespace RandomCar.UI
{
    /// Панель «сейчас играет»: название и исполнитель, полоса прогресса, кнопки «назад», «пауза/продолжить»,
    /// «вперёд», а в полной версии — режим повтора и перемешивание. Работает с единственным MusicPlayer.
    public sealed class MusicWidget
    {
        readonly Label title, artist;
        readonly VisualElement progress;
        readonly IconElement playIcon, repeatIcon;
        readonly Button shuffle;
        readonly VisualElement[] bars = new VisualElement[4];
        readonly float[] barPhase = { 0.1f, 1.7f, 3.1f, 4.4f };
        MusicPlayer player;

        public VisualElement Root { get; }

        public MusicWidget(VisualElement parent, bool full)
        {
            Root = El(full ? "music music-full" : "music music-compact", parent);
            var art = El("music-art", Root);
            var eq = El("music-eq", art);
            for (int i = 0; i < bars.Length; i++) bars[i] = El("music-bar", eq);

            var texts = El("music-texts", Root);
            title = Text("", "music-title", texts);
            artist = Text("", "music-artist", texts);
            var track = El("music-progress", texts);
            progress = El("music-progress-fill", track);

            var buttons = El("music-buttons", Root);
            IconButton(Icon.Previous, () => player?.Previous(), buttons, "Предыдущий трек");
            playIcon = IconButton(Icon.Pause, () => player?.TogglePause(), buttons, "Пауза / продолжить", "music-play");
            IconButton(Icon.Next, () => player?.Next(), buttons, "Следующий трек");
            if (full)
            {
                repeatIcon = IconButton(Icon.RepeatAll, () => player?.CycleRepeat(), buttons, "Повтор", "music-mode");
                var shuffleIcon = IconButton(Icon.Shuffle, () => player?.SetShuffle(!GameSettings.MusicShuffle), buttons, "Перемешать", "music-mode");
                shuffle = (Button)shuffleIcon.parent;
            }

            Root.schedule.Execute(Tick).Every(60);
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Unbind());
        }

        static IconElement IconButton(Icon icon, Action action, VisualElement parent, string tooltip, string extra = "")
        {
            var b = MenuButton("", action, parent, "music-button " + extra);
            b.RemoveFromClassList("menu-button");
            b.tooltip = tooltip;
            var i = new IconElement(icon);
            b.Add(i);
            return i;
        }

        void Bind()
        {
            if (player == MusicPlayer.Instance) return;
            Unbind();
            player = MusicPlayer.Instance;
            if (player != null) player.Changed += Refresh;
            Refresh();
        }

        void Unbind()
        {
            if (player != null) player.Changed -= Refresh;
            player = null;
        }

        void Refresh()
        {
            var current = player != null ? player.Current : null;
            if (player == null || !player.HasTracks || current == null)
            {
                title.text = "Музыки пока нет";
                artist.text = "Добавьте свою: «Моя музыка»";
                Root.AddToClassList("empty");
            }
            else
            {
                Root.RemoveFromClassList("empty");
                title.text = current.Title;
                artist.text = string.IsNullOrEmpty(current.Artist) ? $"Трек {player.TrackNumber} из {player.TrackCount}" : current.Artist;
            }
            bool playing = player != null && player.IsPlaying;
            playIcon.Icon = playing ? Icon.Pause : Icon.Play;
            Root.EnableInClassList("playing", playing);
            if (repeatIcon != null)
            {
                repeatIcon.Icon = GameSettings.MusicRepeat switch
                {
                    MusicRepeat.One => Icon.RepeatOne,
                    MusicRepeat.Off => Icon.RepeatOff,
                    _ => Icon.RepeatAll,
                };
                ((Button)repeatIcon.parent).tooltip = GameSettings.MusicRepeat switch
                {
                    MusicRepeat.One => "Повтор: один трек",
                    MusicRepeat.Off => "Повтор выключен: остановка после последнего трека",
                    _ => "Повтор: весь плейлист по кругу",
                };
                ((Button)repeatIcon.parent).EnableInClassList("on", GameSettings.MusicRepeat != MusicRepeat.Off);
            }
            shuffle?.EnableInClassList("on", GameSettings.MusicShuffle);
        }

        void Tick()
        {
            Bind();
            if (player == null) return;
            progress.style.width = Length.Percent(player.Progress * 100f);
            // «Эквалайзер» на обложке: живой, пока музыка играет.
            float t = Time.unscaledTime;
            bool playing = player.IsPlaying;
            for (int i = 0; i < bars.Length; i++)
            {
                float h = playing ? 0.3f + 0.7f * Mathf.Abs(Mathf.Sin(t * (2.1f + i * 0.7f) + barPhase[i])) : 0.18f;
                bars[i].style.height = Length.Percent(h * 100f);
            }
        }
    }
}
