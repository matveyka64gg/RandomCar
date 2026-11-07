using System;
using System.Collections.Generic;
using RandomCar.Maps;
using RandomCar.Save;
using UnityEngine;
using UnityEngine.UIElements;
using static RandomCar.UI.UiKit;

namespace RandomCar.UI
{
    /// Экран выбора карты: карточки с превью, описанием и рекордом трассы.
    public sealed class MapSelectScreen
    {
        readonly MapCatalog catalog;
        readonly Action<string> select;
        readonly List<(Button card, string key)> cards = new List<(Button, string)>();
        readonly List<Label> records = new List<Label>();

        public VisualElement Root { get; }

        public MapSelectScreen(VisualElement parent, MapCatalog mapCatalog, Action<string> onSelect, Action onBack)
        {
            catalog = mapCatalog;
            select = onSelect;
            Root = El("screen map-select dim", parent);
            Text("ВЫБОР ТРАССЫ", "heading map-heading", Root);
            var row = El("map-cards", Root);
            foreach (var map in catalog.RaceMaps)
            {
                var entry = map;
                var card = MenuButton("", () => select(entry.Key), row, "map-card");
                var image = El("map-preview", card);
                if (entry.Preview != null) image.style.backgroundImage = new StyleBackground(entry.Preview);
                var accent = El("map-accent", card);
                accent.style.backgroundColor = entry.Accent;
                Text(entry.DisplayName, "map-name", card);
                Text(entry.Description, "map-description", card);
                records.Add(Text("", "map-record", card));
                cards.Add((card, entry.Key));
            }
            var buttons = El("map-buttons", Root);
            MenuButton("НАЗАД", onBack, buttons, "secondary");
        }

        public void Open(string currentKey)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var (card, key) = cards[i];
                card.EnableInClassList("selected", key == currentKey);
                var times = BestTimesStore.Load(BestTimesStore.PathFor(key));
                records[i].text = times.Count > 0 ? $"Рекорд: {GameUI.FormatTime(times[0])}" : "Рекорда пока нет";
            }
            var focus = cards.Find(c => c.key == currentKey).card ?? (cards.Count > 0 ? cards[0].card : null);
            focus?.Focus();
        }
    }
}
