using System;
using System.Collections.Generic;
using System.Linq;
using RandomCar.Audio;
using RandomCar.Core;
using RandomCar.UserContent;
using UnityEngine;
using UnityEngine.UIElements;
using static RandomCar.UI.UiKit;

namespace RandomCar.UI
{
    /// Экран «Моя музыка»: свои треки игрока (UserMusicLibrary) — список, включить трек, убрать из библиотеки,
    /// «Добавить музыку» (Windows — системное окно выбора файлов, Android — системный выбор файлов), папка UserContent/Music
    /// (Windows: сканируется при открытии), источник музыки (вся / встроенная / своя).
    public sealed class MusicLibraryScreen
    {
        readonly Action back;
        readonly Action<string> notice;
        readonly ScrollView list;
        readonly Label status, sourceValue, count, folderNote;
        readonly Button addButton;
        string pendingDelete;
        bool busy;

        public VisualElement Root { get; }

        public MusicLibraryScreen(VisualElement parent, Action onBack, Action<string> showNotice)
        {
            back = onBack;
            notice = showNotice;
            Root = El("screen center dim music-library", parent);
            var panel = El("panel music-library-panel", Root);
            Text("МОЯ МУЗЫКА", "heading", panel);
            folderNote = Text("", "settings-note music-library-note", panel);

            var actions = El("music-library-actions", panel);
            addButton = MenuButton("ДОБАВИТЬ МУЗЫКУ", AddMusic, actions);
            if (UserContentPaths.HasDropFolders)
            {
                MenuButton("ОБНОВИТЬ ИЗ ПАПКИ", () => Scan(true), actions, "secondary");
                MenuButton("ОТКРЫТЬ ПАПКУ", () => UserContentPaths.Reveal(UserContentPaths.MusicDropFolder), actions, "secondary");
            }
            ChoiceButton("ИСТОЧНИК", CycleSource, actions, out sourceValue);

            count = Text("", "settings-section", panel);
            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("music-library-list");
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.mouseWheelScrollSize = 60f;
            panel.Add(list);
            status = Text("", "music-library-status", panel);
            MenuButton("НАЗАД", () => back(), panel, "settings-back");

            UserMusicLibrary.Changed += () => { if (!Root.ClassListContains("hidden")) Refresh(); };
        }

        public void Open()
        {
            pendingDelete = null;
            status.text = "";
            folderNote.text = UserContentPaths.HasDropFolders
                ? $"Положите файлы OGG, MP3 или WAV в папку игры UserContent/Music — они появятся здесь. Или нажмите «Добавить музыку».\n{UserContentPaths.MusicDropFolder}"
                : "Нажмите «Добавить музыку» и выберите треки в памяти телефона (OGG, MP3, WAV). Игра сохранит копию у себя — трек останется после перезапуска.";
            Refresh();
            Scan(false);
        }

        async void Scan(bool manual)
        {
            if (!UserContentPaths.HasDropFolders || busy) return;
            busy = true;
            try
            {
                if (manual) status.text = "Проверяю папку…";
                string result = await UserMusicLibrary.ScanDropFolderAsync();
                status.text = result ?? (manual ? "Новых треков в папке нет." : status.text);
                Refresh();
            }
            finally
            {
                busy = false;
            }
        }

        void AddMusic()
        {
            if (busy) return;
            if (AndroidFilePicker.Supported)
            {
                busy = true;
                status.text = "Выберите треки…";
                AndroidFilePicker.PickAudio((files, error) => Import(files.Select(f => (f.Path, f.Name, true)).ToList(), error));
                return;
            }
            if (!FileDialog.Supported)
            {
                status.text = "Добавление файлов на этой платформе недоступно.";
                return;
            }
            var paths = FileDialog.OpenAudio("Своя музыка для RandomCar (OGG, MP3, WAV)");
            if (paths.Count == 0) return;
            busy = true;
            Import(paths.Select(p => (p, (string)null, false)).ToList(), null);
        }

        async void Import(List<(string path, string name, bool temp)> files, string pickerError)
        {
            try
            {
                if (files.Count == 0)
                {
                    status.text = pickerError ?? "";
                    return;
                }
                var messages = new List<string>();
                int added = 0;
                for (int i = 0; i < files.Count; i++)
                {
                    status.text = files.Count > 1 ? $"Добавляю {i + 1} из {files.Count}…" : "Добавляю…";
                    var r = await UserMusicLibrary.ImportAsync(files[i].path, files[i].name, files[i].temp);
                    if (r.Added) added++;
                    messages.Add(r.Message);
                }
                if (pickerError != null) messages.Add(pickerError);
                status.text = files.Count == 1 ? messages[0] : $"Добавлено треков: {added} из {files.Count}." + (messages.Count > 0 && added < files.Count ? " " + messages.Last(m => m != null) : "");
                if (added > 0) notice?.Invoke(added == 1 ? "Трек добавлен в «Мою музыку»" : $"Добавлено треков: {added}");
                Refresh();
            }
            finally
            {
                busy = false;
            }
        }

        void CycleSource()
        {
            GameSettings.MusicSource = (MusicSource)(((int)GameSettings.MusicSource + 1) % 3);
            Refresh();
        }

        static string SourceName(MusicSource s) => s switch
        {
            MusicSource.Game => "ВСТРОЕННАЯ",
            MusicSource.Mine => "МОЯ",
            _ => "ВСЯ",
        };

        void Refresh()
        {
            sourceValue.text = SourceName(GameSettings.MusicSource);
            var tracks = UserMusicLibrary.Tracks;
            count.text = tracks.Count == 0 ? "СВОИХ ТРЕКОВ ПОКА НЕТ" : $"СВОИ ТРЕКИ: {tracks.Count}";
            list.Clear();
            var player = MusicPlayer.Instance;
            var current = player != null ? player.Current : null;
            foreach (var t in tracks)
            {
                var track = t;
                var row = El("music-library-row", list);
                var playing = player != null ? player.UserTrack(track.id) : null;
                bool isCurrent = playing != null && playing == current;
                row.EnableInClassList("current", isCurrent);
                var play = MenuButton(isCurrent ? "■" : "▶", () =>
                {
                    var mt = MusicPlayer.Instance?.UserTrack(track.id);
                    if (mt == null) return;
                    if (MusicPlayer.Instance.Current == mt) MusicPlayer.Instance.TogglePause();
                    else MusicPlayer.Instance.Play(mt);
                    Refresh();
                }, row, "music-library-play");
                play.tooltip = "Слушать";
                var texts = El("music-library-texts", row);
                Text(track.title, "music-library-title", texts);
                string origin = track.imported ? "добавлен в игру" : "из папки UserContent/Music";
                if (playing != null && playing.Broken) origin = "файл не читается";
                Text(string.IsNullOrEmpty(track.artist) ? origin : $"{track.artist} · {origin}", "music-library-artist", texts);
                bool confirm = pendingDelete == track.id;
                var del = MenuButton(confirm ? "УБРАТЬ?" : "×", () => Delete(track.id), row, confirm ? "music-library-delete confirm" : "music-library-delete");
                del.tooltip = track.imported ? "Удалить из библиотеки игры" : "Убрать из библиотеки (файл в папке останется)";
            }
        }

        void Delete(string id)
        {
            if (pendingDelete != id)
            {
                pendingDelete = id;
                Refresh();
                return;
            }
            pendingDelete = null;
            status.text = UserMusicLibrary.Remove(id);
            Refresh();
        }
    }
}
