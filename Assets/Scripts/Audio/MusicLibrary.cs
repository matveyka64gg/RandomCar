using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Audio
{
    /// Музыкальная композиция: клип и человекочитаемые название и исполнитель.
    [System.Serializable]
    public sealed class MusicTrack
    {
        /// Постоянный ключ трека (GUID клипа): по нему запоминается последний трек плейлиста.
        public string Id;
        public AudioClip Clip;
        public string Title;
        public string Artist;
        /// Плейлист: «Main» для корня папки музыки, иначе имя подпапки (например, «TelAviv»).
        public string Playlist;
        /// Выравнивание громкости между треками (по измеренной громкости, только ослабление).
        [Range(0.05f, 1f)] public float Gain = 1f;
        /// Измеренная громкость файла, LUFS (0 — не измерялась). Заполняет редактор.
        public float Loudness;

        /// Свой трек игрока (UserContent): файл на диске, клип загружается перед воспроизведением. Null — встроенный трек.
        [System.NonSerialized] public string FilePath;
        [System.NonSerialized] public AudioType FileType;
        /// Файл не прочитался (повреждён или пропал): трек пропускается.
        [System.NonSerialized] public bool Broken;

        public bool IsUser => FilePath != null;
    }

    /// Какую музыку играть: всю вместе, только встроенную или только свою (UserContent).
    public enum MusicSource { All, Game, Mine }

    /// Все музыкальные треки игры. Собирается редактором из папки Assets/Art/Audio/Music (меню RandomCar/Audio):
    /// корень папки — общий плейлист, каждая подпапка — отдельный плейлист (например, плейлист карты).
    /// Название и исполнителя можно поправить вручную прямо в этом ассете — пересборка их не затирает.
    [CreateAssetMenu(menuName = "RandomCar/Music Library")]
    public sealed class MusicLibrary : ScriptableObject
    {
        public const string MainPlaylist = "Main";

        public List<MusicTrack> Tracks = new List<MusicTrack>();

        /// Треки плейлиста в порядке библиотеки (по имени файла). Треки без клипа пропускаются.
        public List<MusicTrack> Playlist(string key)
        {
            if (string.IsNullOrEmpty(key)) key = MainPlaylist;
            var result = new List<MusicTrack>();
            foreach (var t in Tracks)
                if (t != null && t.Clip != null && t.Playlist == key) result.Add(t);
            return result;
        }
    }
}
