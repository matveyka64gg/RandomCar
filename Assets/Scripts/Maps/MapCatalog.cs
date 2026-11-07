using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RandomCar.Maps
{
    [System.Serializable]
    public sealed class MapEntry
    {
        public string Key;
        public string DisplayName;
        [TextArea] public string Description;
        public string SceneName;
        public Texture2D Preview;
        public Color Accent = Color.white;
        /// Плейлист музыки карты (подпапка Assets/Art/Audio/Music или «Main» — общий).
        public string Playlist = "Main";
        /// Не трасса для гонок (полигон испытаний): нет в выборе трасс и в лобби, грузится только своим режимом.
        public bool Hidden;
    }

    /// Список карт для экрана выбора. Порядок — порядок показа.
    [CreateAssetMenu(menuName = "RandomCar/Map Catalog")]
    public sealed class MapCatalog : ScriptableObject
    {
        public List<MapEntry> Maps = new List<MapEntry>();

        /// Карта по ключу; неизвестный ключ — первая гоночная (скрытые карты сюда не подставляются).
        public MapEntry Get(string key) => Maps.Find(m => m.Key == key) ?? RaceMaps.FirstOrDefault() ?? (Maps.Count > 0 ? Maps[0] : null);

        /// Трассы для гонок (без полигона испытаний и других скрытых карт).
        public List<MapEntry> RaceMaps => Maps.Where(m => !m.Hidden).ToList();

        /// Ключ полигона испытаний (скрытая карта), null — не собран.
        public string ArenaKey => Maps.Find(m => m.Hidden)?.Key;
    }
}
