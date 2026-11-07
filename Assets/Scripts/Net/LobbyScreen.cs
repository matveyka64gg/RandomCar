using System.Linq;
using RandomCar.Core;
using RandomCar.UI;
using UnityEngine.UIElements;

namespace RandomCar.Net
{
    /// Экраны сетевой игры: подключение (имя, создать лобби, подключиться по адресу), лобби (адреса хоста,
    /// игроки с готовностью и пингом, трасса, ИИ и Хаос — у хоста, своя машина — у каждого) и ожидание.
    public sealed class LobbyScreen
    {
        readonly NetSession net;
        readonly GameUI ui;
        readonly VisualElement connect, lobby, wait;
        TextField nameField, portField, addressField;
        Label connectStatus, waitText, lobbySubtitle, addresses, lobbyStatus;
        Label mapValue, aiValue, chaosValue, carValue;
        VisualElement playerRows, addressBox;
        Button startButton, readyButton;

        public LobbyScreen(NetSession session)
        {
            net = session;
            ui = session.Flow.Ui;
            connect = BuildConnect();
            lobby = BuildLobby();
            wait = BuildWait();
            ui.AddScreen(connect);
            ui.AddScreen(lobby);
            ui.AddScreen(wait);
        }

        static VisualElement El(string cls, VisualElement parent = null) => UiKit.El(cls, parent);
        static Label Text(string text, string cls, VisualElement parent) => UiKit.Text(text, cls, parent);

        TextField Field(string title, string value, int maxLength, VisualElement parent)
        {
            var row = El("net-field-row", parent);
            Text(title, "net-field-title", row);
            var field = new TextField { value = value, maxLength = maxLength };
            field.AddToClassList("net-field");
            row.Add(field);
            return field;
        }

        VisualElement BuildConnect()
        {
            var screen = El("screen center dim", ui.Root);
            var panel = El("panel wide net-panel", screen);
            Text("СЕТЕВАЯ ИГРА", "heading", panel);
            Text("Напрямую по IP: в одной локальной сети, через Radmin VPN или Hamachi. Без серверов, аккаунтов и интернета.", "settings-note", panel);
            nameField = Field("ИМЯ", GameSettings.PlayerName, 16, panel);
            nameField.RegisterValueChangedCallback(e => GameSettings.PlayerName = NetProtocol.CleanName(e.newValue));

            Text("СОЗДАТЬ ЛОББИ", "settings-section", panel);
            portField = Field("ПОРТ", GameSettings.Port.ToString(), 5, panel);
            UiKit.MenuButton("СОЗДАТЬ ЛОББИ", () => Report(net.Host(portField.value)), panel);
            Text("При первом создании лобби Windows спросит разрешение брандмауэра — разреши доступ, иначе друзья не подключатся.", "settings-note", panel);

            Text("ПОДКЛЮЧИТЬСЯ К ДРУГУ", "settings-section", panel);
            addressField = Field("АДРЕС", GameSettings.LastAddress, 21, panel);
            addressField.textEdition.placeholder = "IP хоста, например 26.12.34.56 или 192.168.1.5:7777";
            UiKit.MenuButton("ПОДКЛЮЧИТЬСЯ", () => Report(net.Join(addressField.value)), panel);
            connectStatus = Text("", "net-status", panel);
            UiKit.MenuButton("НАЗАД", () => net.Flow.ShowMenu(), panel, "secondary");
            return screen;
        }

        VisualElement BuildLobby()
        {
            var screen = El("screen center dim", ui.Root);
            var panel = El("panel wide net-panel", screen);
            Text("ЛОББИ", "heading", panel);
            lobbySubtitle = Text("", "settings-note", panel);
            addressBox = El("net-addresses", panel);
            Text("Адреса для друзей:", "net-field-title", addressBox);
            addresses = Text("", "net-address-list", addressBox);

            playerRows = El("result-table", panel);

            var settings = El("net-settings", panel);
            mapValue = UiKit.Stepper("Трасса", d => net.CycleMap(d), settings);
            aiValue = UiKit.Stepper("Соперники-ИИ", d => net.CycleAi(d), settings);
            chaosValue = UiKit.Stepper("Хаос", d => net.CycleChaos(d), settings);
            carValue = UiKit.Stepper("Моя машина", d => net.CycleCar(d), settings);

            lobbyStatus = Text("", "net-status", panel);
            var row = El("buttons-row", panel);
            startButton = UiKit.MenuButton("СТАРТ", () => Report(net.StartRace()), row);
            readyButton = UiKit.MenuButton("ГОТОВ", ToggleReady, row);
            UiKit.MenuButton("ВЫЙТИ", () => net.Leave(null), row, "secondary");
            return screen;
        }

        VisualElement BuildWait()
        {
            var screen = El("screen center dim", ui.Root);
            var panel = El("panel net-panel", screen);
            Text("СЕТЕВАЯ ИГРА", "heading", panel);
            waitText = Text("", "net-wait", panel);
            UiKit.MenuButton("ВЫЙТИ", () => net.Leave(null), panel, "secondary");
            return screen;
        }

        void Report(string error)
        {
            if (error == null) return;
            if (connectStatus != null) connectStatus.text = error;
            if (lobbyStatus != null) lobbyStatus.text = error;
            ui.ShowNotice(error, 6f);
        }

        void ToggleReady()
        {
            var me = net.Me;
            net.SetReady(me == null || !me.Ready);
        }

        public void ShowConnect()
        {
            nameField.SetValueWithoutNotify(GameSettings.PlayerName);
            portField.SetValueWithoutNotify(GameSettings.Port.ToString());
            addressField.SetValueWithoutNotify(GameSettings.LastAddress);
            connectStatus.text = "";
            ui.ShowScreen(connect);
        }

        /// Подключение — строка на экране подключения; загрузка и ожидание старта — отдельный экран.
        public void ShowStatus(string text)
        {
            if (net.Mode == NetMode.Connecting)
            {
                connectStatus.text = text;
                return;
            }
            waitText.text = text;
            ui.ShowScreen(wait);
        }

        public void ShowLobby()
        {
            lobbyStatus.text = "";
            ui.ShowScreen(lobby);
            Refresh();
        }

        public void Refresh()
        {
            var state = net.Lobby;
            bool host = net.IsHost;
            var hostPlayer = state.Players.FirstOrDefault();
            lobbySubtitle.text = host
                ? "Ты хост: выбираешь трассу, соперников и Хаос. Старт — когда все готовы."
                : $"Хост: {(hostPlayer != null ? hostPlayer.Name : "—")}. Нажми «Готов», когда выберешь машину.";

            addressBox.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            if (host)
            {
                var list = NetAddress.LocalAddresses();
                addresses.text = list.Count == 0
                    ? "Сетевых адаптеров не найдено — проверь подключение к сети или VPN."
                    : string.Join("\n", list.Select(a => $"{a.Ip}:{GameSettings.Port}  —  {a.Label}"));
            }

            playerRows.Clear();
            foreach (var p in state.Players)
            {
                var row = El("result-row", playerRows);
                if (p.ClientId == net.LocalId) row.AddToClassList("me");
                Text(p.ClientId == 0 ? "★" : "", "result-row-pos", row);
                Text(p.Name, "result-row-name", row);
                var car = net.Flow.Cars.Get(p.CarKey);
                Text(string.IsNullOrEmpty(p.CarKey) ? "…" : car.DisplayName, "net-row-car", row);
                Text(p.Ready ? "готов" : "выбирает", p.Ready ? "net-row-ready on" : "net-row-ready", row);
                Text(p.ClientId == 0 ? "хост" : $"{p.PingMs} мс", "net-row-ping", row);
            }
            int ai = net.IsHost ? net.AiSlots : state.AiCount;
            for (int i = 0; i < ai; i++)
            {
                var row = El("result-row net-ai", playerRows);
                Text("", "result-row-pos", row);
                Text("Соперник-ИИ", "result-row-name", row);
                Text("", "net-row-car", row);
                Text("", "net-row-ready", row);
                Text("", "net-row-ping", row);
            }

            var map = net.Flow.Maps.Get(state.MapKey);
            mapValue.text = map != null ? map.DisplayName : "—";
            aiValue.text = ai.ToString();
            chaosValue.text = state.Chaos switch { 0 => "выключен", 2 => "много", 3 => "безумие", 4 => "АБСУРД", _ => "умеренно" };
            var me = net.Me;
            carValue.text = me != null && !string.IsNullOrEmpty(me.CarKey) ? net.Flow.Cars.Get(me.CarKey).DisplayName : net.Flow.Cars.Get(GameSettings.Car).DisplayName;
            foreach (var value in new[] { mapValue, aiValue, chaosValue })
                foreach (var arrow in value.parent.Query<Button>().ToList()) arrow.SetEnabled(host && !state.Racing);

            startButton.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            readyButton.style.display = host ? DisplayStyle.None : DisplayStyle.Flex;
            readyButton.text = me != null && me.Ready ? "НЕ ГОТОВ" : "ГОТОВ";
            if (state.Racing && !host) lobbyStatus.text = "Гонка идёт у других игроков — дождись, пока хост вернётся в лобби.";
        }
    }
}
