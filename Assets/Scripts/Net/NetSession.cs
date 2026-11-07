using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RandomCar.Car;
using RandomCar.Core;
using RandomCar.Events;
using RandomCar.Race;
using RandomCar.Save;
using RandomCar.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace RandomCar.Net
{
    public enum NetMode { Off, Host, Connecting, Client }

    /// Сетевая игра Host + Clients по прямому IP (локальная сеть, Radmin VPN, Hamachi) без серверов и аккаунтов.
    /// Хост ведёт лобби, состав гонки, старт по общим часам, события и результаты; свою машину каждый считает сам
    /// и рассылает её состояние 30 раз в секунду (через хоста), чужие машины — интерполируемые копии (NetProxy).
    /// NetworkManager создаётся только при первом «Создать лобби» / «Подключиться»: одиночная игра сеть не запускает.
    public sealed class NetSession : MonoBehaviour
    {
        [SerializeField] GameFlow flow;

        NetworkManager manager;
        UnityTransport transport;
        LobbyScreen screen;
        RaceSetup setup;
        readonly Dictionary<int, NetProxy> proxies = new Dictionary<int, NetProxy>();
        readonly Dictionary<int, ulong> slotOwners = new Dictionary<int, ulong>();
        readonly Dictionary<int, CarState> relay = new Dictionary<int, CarState>();
        readonly Dictionary<ulong, string> pendingNames = new Dictionary<ulong, string>();
        readonly List<CarState> outgoing = new List<CarState>();
        double startAt = -1;
        bool waitingForLoad;
        float loadStarted;
        float nextStateAt;
        float nextLobbyAt;
        string connectTarget;

        /// Отладка двумя экземплярами без человека за рулём: свою машину ведёт ИИ (ключ -rc-autopilot).
        public bool Autopilot { get; set; }

        /// Имя только на эту сессию (ключ -rc-name), без записи в настройки.
        string nameOverride;
        string PlayerName => nameOverride ?? GameSettings.PlayerName;

        public NetMode Mode { get; private set; }
        public LobbyState Lobby { get; private set; } = new LobbyState();
        public bool IsHost => Mode == NetMode.Host;
        public ulong LocalId => manager != null ? manager.LocalClientId : 0;
        public LobbyPlayer Me => Lobby.Find(LocalId);
        public GameFlow Flow => flow;

        /// Время хоста, с: общие часы старта и снимков машин.
        public double ServerTime => manager != null && manager.IsListening ? manager.ServerTime.Time : Time.timeAsDouble;

        RaceManager Race => flow.Race;
        EventDirector Director => flow.Director;
        GameUI Ui => flow.Ui;

        public void Configure(GameFlow gameFlow) => flow = gameFlow;

        void Start()
        {
            flow.NetworkMenu = OpenNetwork;
            flow.NetworkLeave = () => Leave(null);
            flow.NetworkEscape = () => Leave(null);
            screen = new LobbyScreen(this);
            Race.RacerFinished += OnRacerFinished;
            Director.EventStarted += OnEventStarted;
            Director.SignalRaised += OnSignalRaised;
            HookPowerUps();
            StartCoroutine(CommandLine());
        }

        void OnDestroy()
        {
            if (flow != null && flow.Race != null) flow.Race.RacerFinished -= OnRacerFinished;
            if (flow != null && flow.Director != null)
            {
                flow.Director.EventStarted -= OnEventStarted;
                flow.Director.SignalRaised -= OnSignalRaised;
            }
            UnhookPowerUps();
            if (manager != null && manager.IsListening) manager.Shutdown();
        }

        // ---------- бонусы ----------

        enum ItemOp : byte { PickupRequest, Grant, UseRequest, Used, Hit }

        PowerUps.PowerUpDirector powerUps;
        int netItems;
        // Хост: просьбы клиентов (проверка в логе): подборы и применения — принято/всего, попадания от клиентов.
        int clientPickups, clientPickupsOk, clientUses, clientUsesOk, clientHits;

        void HookPowerUps()
        {
            powerUps = FindAnyObjectByType<PowerUps.PowerUpDirector>();
            if (powerUps == null) return;
            powerUps.PickupRequested += OnItemPickupRequested;
            powerUps.UseRequested += OnItemUseRequested;
            powerUps.Granted += OnItemGranted;
            powerUps.Used += OnItemUsed;
            powerUps.HitReported += OnItemHitReported;
        }

        void UnhookPowerUps()
        {
            if (powerUps == null) return;
            powerUps.PickupRequested -= OnItemPickupRequested;
            powerUps.UseRequested -= OnItemUseRequested;
            powerUps.Granted -= OnItemGranted;
            powerUps.Used -= OnItemUsed;
            powerUps.HitReported -= OnItemHitReported;
        }

        bool ItemNet => Mode != NetMode.Off && Lobby.Racing;

        /// Клиент: своя машина въехала в ящик — просьба хосту (он проверит и выдаст бонус всем).
        void OnItemPickupRequested(int slot, int box)
        {
            if (!ItemNet || IsHost) return;
            SendToHost(NetProtocol.MsgItem, w =>
            {
                w.WriteValueSafe((byte)ItemOp.PickupRequest);
                w.WriteValueSafe(slot);
                w.WriteValueSafe(box);
            });
        }

        void OnItemUseRequested(int slot)
        {
            if (!ItemNet || IsHost) return;
            SendToHost(NetProtocol.MsgItem, w =>
            {
                w.WriteValueSafe((byte)ItemOp.UseRequest);
                w.WriteValueSafe(slot);
            });
        }

        /// Хост: бонус выдан — всем.
        void OnItemGranted(int slot, PowerUps.PowerUpKind kind, int box)
        {
            if (!ItemNet || !IsHost) return;
            SendToClients(NetProtocol.MsgItem, w =>
            {
                w.WriteValueSafe((byte)ItemOp.Grant);
                w.WriteValueSafe(slot);
                w.WriteValueSafe((byte)kind);
                w.WriteValueSafe(box);
            });
        }

        /// Хост: бонус применён — всем (номер, цель, точка и направление).
        void OnItemUsed(PowerUps.PowerUpUse use)
        {
            if (!ItemNet || !IsHost) return;
            SendToClients(NetProtocol.MsgItem, w =>
            {
                w.WriteValueSafe((byte)ItemOp.Used);
                w.WriteValueSafe(use.Slot);
                w.WriteValueSafe((byte)use.Kind);
                w.WriteValueSafe(use.Serial);
                w.WriteValueSafe(use.Target);
                w.WriteValueSafe(use.Position);
                w.WriteValueSafe(use.Forward);
            });
        }

        /// Попадание решено здесь (своя машина): хосту или всем клиентам.
        void OnItemHitReported(int serial, int slot, Vector3 point)
        {
            if (!ItemNet) return;
            if (IsHost) SendToClients(NetProtocol.MsgItem, w => WriteItemHit(w, serial, slot, point));
            else SendToHost(NetProtocol.MsgItem, w => WriteItemHit(w, serial, slot, point));
        }

        static void WriteItemHit(FastBufferWriter w, int serial, int slot, Vector3 point)
        {
            w.WriteValueSafe((byte)ItemOp.Hit);
            w.WriteValueSafe(serial);
            w.WriteValueSafe(slot);
            w.WriteValueSafe(point);
        }

        void OnItem(ulong sender, FastBufferReader r)
        {
            if (powerUps == null) return;
            r.ReadValueSafe(out byte opByte);
            var op = (ItemOp)opByte;
            netItems++;
            switch (op)
            {
                case ItemOp.PickupRequest:
                {
                    r.ReadValueSafe(out int slot);
                    r.ReadValueSafe(out int box);
                    // Хост: только за свою машину и только по правилам (ящик есть, машина рядом, руки свободны).
                    if (IsHost && slotOwners.TryGetValue(slot, out var owner) && owner == sender)
                    {
                        clientPickups++;
                        if (powerUps.HandlePickupRequest(slot, box)) clientPickupsOk++;
                    }
                    break;
                }
                case ItemOp.UseRequest:
                {
                    r.ReadValueSafe(out int slot);
                    // Хост проверяет, что бонус действительно есть: бесконечных ракет у клиента не бывает.
                    if (IsHost && slotOwners.TryGetValue(slot, out var owner) && owner == sender)
                    {
                        clientUses++;
                        if (powerUps.Use(slot)) clientUsesOk++;
                    }
                    break;
                }
                case ItemOp.Grant:
                {
                    r.ReadValueSafe(out int slot);
                    r.ReadValueSafe(out byte kind);
                    r.ReadValueSafe(out int box);
                    if (FromHost(sender)) powerUps.ApplyGrant(slot, (PowerUps.PowerUpKind)kind, box);
                    break;
                }
                case ItemOp.Used:
                {
                    var use = new PowerUps.PowerUpUse();
                    r.ReadValueSafe(out use.Slot);
                    r.ReadValueSafe(out byte kind);
                    use.Kind = (PowerUps.PowerUpKind)kind;
                    r.ReadValueSafe(out use.Serial);
                    r.ReadValueSafe(out use.Target);
                    r.ReadValueSafe(out use.Position);
                    r.ReadValueSafe(out use.Forward);
                    if (FromHost(sender)) powerUps.Execute(use);
                    break;
                }
                case ItemOp.Hit:
                {
                    r.ReadValueSafe(out int serial);
                    r.ReadValueSafe(out int slot);
                    r.ReadValueSafe(out Vector3 point);
                    if (IsHost)
                    {
                        // От клиента: попадание в его машину (или взрыв мины от неё) — применить у себя и разослать.
                        if (slot >= 0 && (!slotOwners.TryGetValue(slot, out var owner) || owner != sender)) break;
                        clientHits++;
                        powerUps.ReceiveHit(serial, slot, point);
                        SendToClients(NetProtocol.MsgItem, w => WriteItemHit(w, serial, slot, point));
                    }
                    else if (FromHost(sender)) powerUps.ReceiveHit(serial, slot, point);
                    break;
                }
            }
        }

        // ---------- вход в сеть ----------

        /// Кнопка «Сетевая игра» в меню, «В лобби» после гонки.
        void OpenNetwork()
        {
            if (Mode == NetMode.Off)
            {
                screen.ShowConnect();
                return;
            }
            if (IsHost && Lobby.Racing) ReturnToLobby();
            else EnterLobbyLocal();
        }

        bool EnsureManager()
        {
            if (manager != null)
            {
                if (manager.ShutdownInProgress || manager.IsListening)
                {
                    Ui.ShowNotice("Предыдущее соединение ещё закрывается — попробуй через секунду.");
                    return false;
                }
                return true;
            }
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            manager = go.AddComponent<NetworkManager>();
            transport = go.AddComponent<UnityTransport>();
            transport.ConnectTimeoutMS = 1000;
            transport.MaxConnectAttempts = 10;
            transport.DisconnectTimeoutMS = 10000;
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                ConnectionApproval = true,
                EnableSceneManagement = false,
                TickRate = 30,
                ClientConnectionBufferTimeout = 10,
            };
            go.SetActive(true);
            manager.ConnectionApprovalCallback = Approve;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            manager.OnTransportFailure += OnTransportFailure;
            return true;
        }

        /// Создать лобби на порту. Ошибка — сообщение для игрока, иначе null.
        /// loopbackOnly — только этот компьютер (проверка двумя экземплярами без запроса брандмауэра).
        public string Host(string portText, bool loopbackOnly = false)
        {
            if (Mode != NetMode.Off) return "Сначала выйди из текущей сетевой игры.";
            if (!NetAddress.TryParsePort(portText, out ushort port, out string error)) return error;
            if (!EnsureManager()) return null;
            GameSettings.Port = port;
            transport.SetConnectionData("127.0.0.1", port, loopbackOnly ? "127.0.0.1" : "0.0.0.0");
            manager.NetworkConfig.ConnectionData = NetProtocol.ConnectionPayload(PlayerName);
            if (!manager.StartHost())
            {
                manager.Shutdown();
                return $"Не удалось создать лобби на порту {port}: порт занят другой программой (или второй копией игры). Выбери другой порт.";
            }
            Mode = NetMode.Host;
            flow.NetworkSession = true;
            RegisterHandlers();
            Lobby = new LobbyState { MapKey = flow.Maps.Get(GameSettings.Map).Key, AiCount = 2, Chaos = (int)GameSettings.Chaos };
            var me = new LobbyPlayer { ClientId = LocalId, Name = NetProtocol.CleanName(PlayerName), Ready = true };
            FillProfile(me);
            Lobby.Players.Add(me);
            Debug.Log($"[RandomCar] Сеть: лобби создано, порт {port}{(loopbackOnly ? " (только этот компьютер)" : "")}");
            EnterLobbyLocal();
            return null;
        }

        /// Подключиться к хосту «IP» или «IP:порт». Ошибка — сообщение для игрока, иначе null.
        public string Join(string address)
        {
            if (Mode != NetMode.Off) return "Сначала выйди из текущей сетевой игры.";
            if (!NetAddress.TryParse(address, (ushort)GameSettings.Port, out string ip, out ushort port, out string error)) return error;
            if (!EnsureManager()) return null;
            GameSettings.LastAddress = address.Trim();
            transport.SetConnectionData(ip, port);
            manager.NetworkConfig.ConnectionData = NetProtocol.ConnectionPayload(PlayerName);
            if (!manager.StartClient())
            {
                manager.Shutdown();
                return $"Не удалось начать подключение к {ip}:{port}.";
            }
            Mode = NetMode.Connecting;
            connectTarget = $"{ip}:{port}";
            RegisterHandlers();
            Debug.Log($"[RandomCar] Сеть: подключение к {connectTarget}");
            screen.ShowStatus($"Подключение к {connectTarget}…");
            return null;
        }

        /// Выйти из сетевой игры (хост закрывает лобби для всех).
        public void Leave(string message)
        {
            if (Mode == NetMode.Off) return;
            float delay = 0f;
            if (IsHost)
            {
                foreach (var id in manager.ConnectedClientsIds.ToList())
                {
                    if (id == LocalId) continue;
                    manager.DisconnectClient(id, "Хост закрыл лобби.");
                    // Сообщение о причине должно успеть уйти до остановки транспорта.
                    delay = 0.4f;
                }
            }
            EndSession(message, delay);
        }

        void EndSession(string message, float shutdownDelay = 0f)
        {
            Debug.Log($"[RandomCar] Сеть: сессия завершена{(message != null ? ": " + message : "")}");
            Mode = NetMode.Off;
            flow.NetworkSession = false;
            startAt = -1;
            waitingForLoad = false;
            Race.HoldCountdown = false;
            Director.Authoritative = true;
            if (powerUps != null) powerUps.Authoritative = true;
            Director.ChaosOverride = null;
            Director.CancelAll();
            proxies.Clear();
            slotOwners.Clear();
            relay.Clear();
            pendingNames.Clear();
            setup = null;
            Lobby = new LobbyState();
            if (manager != null && (manager.IsListening || manager.IsClient || manager.IsServer))
            {
                if (shutdownDelay > 0f) StartCoroutine(ShutdownLater(shutdownDelay));
                else manager.Shutdown();
            }
            flow.RestoreSinglePlayer();
            flow.ShowMenu();
            if (message != null) Ui.ShowNotice(message, 7f);
        }

        IEnumerator ShutdownLater(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (manager != null && Mode == NetMode.Off) manager.Shutdown();
        }

        // ---------- подключение ----------

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Pending = false;
            if (request.ClientNetworkId == NetworkManager.ServerClientId)
            {
                response.Approved = true;
                return;
            }
            string version, name;
            try
            {
                NetProtocol.ReadConnectionPayload(request.Payload, out version, out name);
            }
            catch (OverflowException)
            {
                Reject(response, "Не удалось прочитать запрос на подключение — это точно RandomCar?");
                return;
            }
            if (version != NetProtocol.Version)
                Reject(response, $"Разные версии игры: у хоста {NetProtocol.Version}, у тебя {(string.IsNullOrEmpty(version) ? "неизвестная" : version)}. Нужна одна и та же версия.");
            else if (Lobby.Racing)
                Reject(response, "Гонка уже идёт. Подключись, когда хост вернётся в лобби.");
            else if (Lobby.Players.Count + pendingNames.Count >= NetProtocol.MaxPlayers)
                Reject(response, $"Лобби заполнено: не больше {NetProtocol.MaxPlayers} игроков.");
            else
            {
                response.Approved = true;
                pendingNames[request.ClientNetworkId] = UniqueName(name);
            }
        }

        static void Reject(NetworkManager.ConnectionApprovalResponse response, string reason)
        {
            response.Approved = false;
            response.Reason = reason;
            Debug.Log($"[RandomCar] Сеть: подключение отклонено — {reason}");
        }

        string UniqueName(string name)
        {
            string result = name;
            for (int i = 2; Lobby.Players.Any(p => p.Name == result) || pendingNames.ContainsValue(result); i++) result = $"{name} {i}";
            return result;
        }

        void OnClientConnected(ulong id)
        {
            if (IsHost)
            {
                if (id == LocalId) return;
                pendingNames.TryGetValue(id, out string name);
                pendingNames.Remove(id);
                Lobby.Players.Add(new LobbyPlayer { ClientId = id, Name = name ?? "Игрок" });
                Debug.Log($"[RandomCar] Сеть: подключился {name} (id {id})");
                Ui.ShowNotice($"{name} в лобби");
                BroadcastLobby();
                return;
            }
            if (Mode == NetMode.Connecting && id == LocalId)
            {
                Mode = NetMode.Client;
                flow.NetworkSession = true;
                Debug.Log($"[RandomCar] Сеть: подключено к {connectTarget}, id {id}");
                SendProfile();
                EnterLobbyLocal();
                if (autoReady) SetReady(true);
            }
        }

        void OnClientDisconnected(ulong id)
        {
            if (Mode == NetMode.Off) return;
            if (IsHost)
            {
                if (id == LocalId) return;
                pendingNames.Remove(id);
                var player = Lobby.Find(id);
                if (player == null) return;
                Lobby.Players.Remove(player);
                Debug.Log($"[RandomCar] Сеть: отключился {player.Name}");
                Ui.ShowNotice($"{player.Name} отключился");
                foreach (var pair in slotOwners.Where(p => p.Value == id).ToList())
                {
                    Race.Retire(pair.Key);
                    proxies.Remove(pair.Key);
                    SendToClients(NetProtocol.MsgLeft, w => w.WriteValueSafe(pair.Key));
                }
                BroadcastLobby();
                screen.Refresh();
                return;
            }
            // Причину показываем, только если её написал хост (отказ в лобби, закрытие); служебные строки NGO
            // («[Disconnect Event]…», по-английски) заменяются понятным сообщением.
            string reason = manager.DisconnectReason;
            if (!string.IsNullOrEmpty(reason) && reason.StartsWith("[", StringComparison.Ordinal))
            {
                Debug.Log($"[RandomCar] Сеть: причина отключения от транспорта — {reason}");
                reason = null;
            }
            if (Mode == NetMode.Connecting)
                EndSession(!string.IsNullOrEmpty(reason) ? reason
                    : $"Хост {connectTarget} не отвечает. Проверь адрес и порт, что лобби создано, что вы в одной сети (Radmin VPN / Hamachi / локальная) и что брандмауэр Windows на компьютере хоста разрешил игру.");
            else
                EndSession(!string.IsNullOrEmpty(reason) ? reason : "Связь с хостом потеряна.");
        }

        void OnTransportFailure()
        {
            if (Mode == NetMode.Off) return;
            EndSession("Сетевая ошибка: соединение прервано.");
        }

        // ---------- лобби ----------

        void EnterLobbyLocal()
        {
            startAt = -1;
            waitingForLoad = false;
            Race.HoldCountdown = false;
            Race.Stop();
            Director.CancelAll();
            flow.EnterLobby();
            screen.ShowLobby();
        }

        /// Машина игрока в лобби: активная сборка выбранной машины (если она куплена).
        void FillProfile(LobbyPlayer player)
        {
            var entry = flow.Cars.Get(GameSettings.Car);
            if (!GarageStore.OwnsCar(entry)) entry = flow.Cars.Cars.Find(GarageStore.OwnsCar) ?? entry;
            var build = GarageStore.Active(entry, flow.Cars).Clone();
            // Свои картинки остаются на этом компьютере: по сети не передаются даже их идентификаторы.
            build.Decals.RemoveAll(d => d.Source != null && d.Source.StartsWith("custom:", StringComparison.Ordinal));
            player.CarKey = entry.Key;
            player.BuildJson = build.ToJson();
        }

        void SendProfile()
        {
            var me = new LobbyPlayer();
            FillProfile(me);
            SendToHost(NetProtocol.MsgProfile, w =>
            {
                w.WriteValueSafe(NetProtocol.CleanName(PlayerName));
                w.WriteValueSafe(me.CarKey);
                w.WriteValueSafe(me.BuildJson);
            });
        }

        /// Выбор машины в лобби: только купленные.
        public void CycleCar(int step)
        {
            var owned = flow.Cars.Cars.Where(GarageStore.OwnsCar).ToList();
            if (owned.Count == 0) return;
            int index = Mathf.Max(0, owned.FindIndex(c => c.Key == GameSettings.Car));
            GameSettings.Car = owned[(index + step + owned.Count) % owned.Count].Key;
            if (IsHost)
            {
                FillProfile(Me);
                BroadcastLobby();
            }
            else SendProfile();
            screen.Refresh();
        }

        public void SetReady(bool ready)
        {
            if (IsHost) return;
            SendToHost(NetProtocol.MsgReady, w => w.WriteValueSafe(ready));
        }

        public void CycleMap(int step)
        {
            if (!IsHost || Lobby.Racing) return;
            var maps = flow.Maps.RaceMaps;
            int index = Mathf.Max(0, maps.FindIndex(m => m.Key == Lobby.MapKey));
            Lobby.MapKey = maps[(index + step + maps.Count) % maps.Count].Key;
            BroadcastLobby();
        }

        public void CycleAi(int step)
        {
            if (!IsHost || Lobby.Racing) return;
            int max = Mathf.Max(0, NetProtocol.MaxPlayers - Lobby.Players.Count);
            Lobby.AiCount = (Lobby.AiCount + step + max + 1) % (max + 1);
            BroadcastLobby();
        }

        public void CycleChaos(int step)
        {
            if (!IsHost || Lobby.Racing) return;
            Lobby.Chaos = (Lobby.Chaos + step + 5) % 5;
            BroadcastLobby();
        }

        /// Соперников-ИИ не больше, чем свободных мест на решётке.
        public int AiSlots => Mathf.Min(Lobby.AiCount, Mathf.Max(0, NetProtocol.MaxPlayers - Lobby.Players.Count));

        void BroadcastLobby()
        {
            if (!IsHost) return;
            foreach (var p in Lobby.Players)
                p.PingMs = p.ClientId == LocalId ? 0 : (int)transport.GetCurrentRtt(p.ClientId);
            Lobby.AiCount = Mathf.Min(Lobby.AiCount, Mathf.Max(0, NetProtocol.MaxPlayers - Lobby.Players.Count));
            SendToClients(NetProtocol.MsgLobby, w => Lobby.Write(w));
            nextLobbyAt = Time.unscaledTime + 1f;
            screen.Refresh();
        }

        // ---------- гонка ----------

        /// Хост: старт, когда все готовы. Ошибка — сообщение для игрока, иначе null.
        public string StartRace()
        {
            if (!IsHost) return null;
            if (Lobby.Racing) return "Гонка уже идёт.";
            var notReady = Lobby.Players.Where(p => !p.Ready).Select(p => p.Name).ToList();
            if (notReady.Count > 0) return $"Не готовы: {string.Join(", ", notReady)}.";
            var s = new RaceSetup { MapKey = Lobby.MapKey, Chaos = Lobby.Chaos, Seed = Environment.TickCount };
            foreach (var p in Lobby.Players)
            {
                var entry = flow.Cars.Get(p.CarKey);
                s.Entries.Add(new SetupEntry { ClientId = p.ClientId, Name = p.Name, CarKey = entry.Key, BuildJson = p.BuildJson });
            }
            int ai = AiSlots;
            if (ai > 0)
            {
                // Соперники-ИИ близкой производительности к машине хоста, вид — случайный (как в одиночной игре).
                var host = Me;
                var rivals = RaceManager.SinglePlayer(flow.Cars, host.CarKey, CarBuild.FromJson(host.BuildJson), ai + 1, s.Seed)
                    .Where(x => x.Kind == ParticipantKind.Ai).Take(ai);
                foreach (var r in rivals)
                    s.Entries.Add(new SetupEntry { Name = r.Name, CarKey = r.CarKey, BuildJson = r.Build.ToJson() });
            }
            Lobby.Racing = true;
            foreach (var p in Lobby.Players) p.Loaded = false;
            BroadcastLobby();
            SendToClients(NetProtocol.MsgSetup, w => s.Write(w));
            ApplySetup(s);
            return null;
        }

        void ApplySetup(RaceSetup s)
        {
            setup = s;
            startAt = -1;
            proxies.Clear();
            slotOwners.Clear();
            relay.Clear();
            Director.CancelAll();
            screen.ShowStatus("Загрузка трассы…");
            flow.LoadMapForNetwork(s.MapKey, SpawnSetup);
        }

        void SpawnSetup()
        {
            if (setup == null || Mode == NetMode.Off) return;
            var list = new List<Participant>();
            for (int i = 0; i < setup.Entries.Count; i++)
            {
                var e = setup.Entries[i];
                var kind = !e.IsAi && e.ClientId == LocalId ? ParticipantKind.LocalPlayer
                    : e.IsAi && IsHost ? ParticipantKind.Ai : ParticipantKind.Remote;
                var build = CarBuild.FromJson(e.BuildJson) ?? CarBuild.Default(flow.Cars.Get(e.CarKey));
                list.Add(new Participant { Name = e.Name, CarKey = e.CarKey, Build = build, Kind = kind, ClientId = e.ClientId });
                slotOwners[i] = e.IsAi ? NetworkManager.ServerClientId : e.ClientId;
            }
            Race.Spawn(list);
            foreach (var racer in Race.Racers)
            {
                if (racer.Kind != ParticipantKind.Remote) continue;
                var proxy = racer.Car.gameObject.AddComponent<NetProxy>();
                proxy.Init(() => ServerTime);
                proxies[racer.Slot] = proxy;
            }
            Race.PrepareGrid();
            Director.Authoritative = IsHost;
            if (powerUps != null) powerUps.Authoritative = IsHost;
            Director.ChaosOverride = (ChaosLevel)Mathf.Clamp(setup.Chaos, 0, (int)ChaosLevel.Absurd);
            Debug.Log($"[RandomCar] Сеть: трасса {setup.MapKey} готова, участников {list.Count}");
            screen.ShowStatus("Ждём остальных игроков…");
            if (IsHost)
            {
                MarkLoaded(LocalId);
                waitingForLoad = true;
                loadStarted = Time.unscaledTime;
            }
            else SendToHost(NetProtocol.MsgLoaded, w => w.WriteValueSafe(true));
        }

        void MarkLoaded(ulong id)
        {
            var p = Lobby.Find(id);
            if (p != null) p.Loaded = true;
        }

        void Go()
        {
            waitingForLoad = false;
            double at = ServerTime + NetProtocol.StartLead + RaceManager.CountdownSeconds;
            SendToClients(NetProtocol.MsgGo, w => w.WriteValueSafe(at));
            BeginRace(at);
        }

        void BeginRace(double at)
        {
            startAt = at;
            flow.BeginRacing();
            Race.HoldCountdown = true;
            if (Autopilot) EnableAutopilot();
            Debug.Log($"[RandomCar] Сеть: старт через {at - ServerTime:F1} с");
        }

        /// Хост: вернуть всех в лобби (после гонки или досрочно).
        void ReturnToLobby()
        {
            Lobby.Racing = false;
            foreach (var p in Lobby.Players) p.Ready = p.ClientId == LocalId;
            BroadcastLobby();
            SendToClients(NetProtocol.MsgBackToLobby, w => w.WriteValueSafe(true));
            EnterLobbyLocal();
        }

        void EnableAutopilot()
        {
            var me = Race.Player;
            if (me == null || me.Kind != ParticipantKind.LocalPlayer) return;
            if (me.Car.TryGetComponent<PlayerDriver>(out var driver)) driver.enabled = false;
            if (!me.Car.TryGetComponent<AiDriver>(out var ai)) ai = me.Car.gameObject.AddComponent<AiDriver>();
            ai.Init(Race.Course, Race.Cars, AiSkill.ForDifficulty(1, 0.5f), (int)LocalId + 7);
        }

        void OnRacerFinished(Racer racer)
        {
            if (Mode == NetMode.Off || !(racer.Kind == ParticipantKind.LocalPlayer || (IsHost && racer.Kind == ParticipantKind.Ai))) return;
            if (IsHost) SendToClients(NetProtocol.MsgFinish, w => WriteFinish(w, racer.Slot, racer.FinishTime));
            else SendToHost(NetProtocol.MsgFinish, w => WriteFinish(w, racer.Slot, racer.FinishTime));
        }

        static void WriteFinish(FastBufferWriter w, int slot, float time)
        {
            w.WriteValueSafe(slot);
            w.WriteValueSafe(time);
        }

        void OnEventStarted(RaceEvent ev)
        {
            if (!IsHost || !Lobby.Racing) return;
            var spot = ev.Placement;
            SendToClients(NetProtocol.MsgEvent, w =>
            {
                w.WriteValueSafe(ev.Id);
                w.WriteValueSafe(spot.Progress);
                w.WriteValueSafe(spot.Lateral);
                w.WriteValueSafe(spot.Seed);
                w.WriteValueSafe(spot.FocusSlot);
                // Хаос v4: номер розыгрыша (сигналы и физические объекты), время запуска по часам хоста
                // (движущиеся конструкции у всех в одной фазе), название комбинации для плашки.
                w.WriteValueSafe(ev.Serial);
                w.WriteValueSafe(ServerTime);
                w.WriteValueSafe(ev.ComboTitle ?? "");
            });
        }

        /// Сигнал события: свой (машина этого компьютера задела мину) — хосту или всем клиентам.
        void OnSignalRaised(RaceEvent ev, int code, int index, Vector3 value)
        {
            if (Mode == NetMode.Off || !Lobby.Racing) return;
            if (IsHost) SendToClients(NetProtocol.MsgSignal, w => WriteSignal(w, ev.Serial, code, index, value));
            else SendToHost(NetProtocol.MsgSignal, w => WriteSignal(w, ev.Serial, code, index, value));
        }

        static void WriteSignal(FastBufferWriter w, int serial, int code, int index, Vector3 value)
        {
            w.WriteValueSafe(serial);
            w.WriteValueSafe(code);
            w.WriteValueSafe(index);
            w.WriteValueSafe(value);
        }

        void OnSignal(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int serial);
            r.ReadValueSafe(out int code);
            r.ReadValueSafe(out int index);
            r.ReadValueSafe(out Vector3 value);
            netSignals++;
            if (IsHost)
            {
                // От клиента: применить у себя и разослать остальным (повтор у отправителя безвреден).
                Director.ApplySignal(serial, code, index, value);
                SendToClients(NetProtocol.MsgSignal, w => WriteSignal(w, serial, code, index, value));
            }
            else if (FromHost(sender)) Director.ApplySignal(serial, code, index, value);
        }

        // ---------- физические объекты хаоса ----------

        readonly List<ChaosBody> outgoingBodies = new List<ChaosBody>();
        // Сводка хаоса в сети для лога (проверка: события, сигналы и физические объекты доходят).
        int netEvents, netSignals, netBodyStates, netBodyMisses;
        float nextChaosLogAt;
        readonly Dictionary<int, ChaosBody> remoteBodies = new Dictionary<int, ChaosBody>();
        float nextBodiesAt;

        /// Хост: состояние своих физических объектов (шары, бочки) клиентам, 20 раз в секунду.
        void SendBodies()
        {
            outgoingBodies.Clear();
            foreach (var b in ChaosBody.Active)
                if (b != null && b.Live && !b.Remote && b.NetId >= 0) outgoingBodies.Add(b);
            // Ненадёжное сообщение должно влезть в один пакет: в «безумии» тел до 70 — шлём пачками.
            for (int from = 0; from < outgoingBodies.Count; from += BodiesPerMessage)
            {
                int start = from;
                int count = Mathf.Min(BodiesPerMessage, outgoingBodies.Count - from);
                SendToClients(NetProtocol.MsgBodies, w =>
                {
                    w.WriteValueSafe((ushort)count);
                    for (int i = start; i < start + count; i++)
                    {
                        var b = outgoingBodies[i];
                        var body = b.Body;
                        w.WriteValueSafe(b.NetId);
                        w.WriteValueSafe(body.position);
                        w.WriteValueSafe(body.rotation);
                        w.WriteValueSafe(body.linearVelocity);
                    }
                }, NetworkDelivery.Unreliable);
            }
        }

        /// 24 тела × 44 байта ≈ 1.1 КБ — меньше одного пакета.
        const int BodiesPerMessage = 24;

        void OnBodies(ulong sender, FastBufferReader r)
        {
            if (!FromHost(sender)) return;
            remoteBodies.Clear();
            foreach (var b in ChaosBody.Active)
                if (b != null && b.Live && b.Remote && b.NetId >= 0) remoteBodies[b.NetId] = b;
            r.ReadValueSafe(out ushort count);
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out int id);
                r.ReadValueSafe(out Vector3 position);
                r.ReadValueSafe(out Quaternion rotation);
                r.ReadValueSafe(out Vector3 velocity);
                if (remoteBodies.TryGetValue(id, out var b))
                {
                    b.SetNetState(position, rotation, velocity);
                    netBodyStates++;
                }
                else netBodyMisses++;
            }
        }

        // ---------- кадр ----------

        void Update()
        {
            if (Mode == NetMode.Off || manager == null) return;

            if (IsHost && waitingForLoad)
            {
                bool all = Lobby.Players.All(p => p.Loaded);
                if (all || Time.unscaledTime - loadStarted > NetProtocol.LoadTimeout) Go();
            }

            if (startAt > 0 && Race.Phase == RacePhase.Countdown)
            {
                double left = startAt - ServerTime;
                Race.HoldCountdown = left > RaceManager.CountdownSeconds;
                Race.SetCountdown((float)Math.Min(left, RaceManager.CountdownSeconds));
            }

            bool racing = startAt > 0 && (Race.Phase == RacePhase.Countdown || Race.Phase == RacePhase.Running || Race.Phase == RacePhase.Finished);
            if (racing && Time.unscaledTime >= nextStateAt)
            {
                nextStateAt = Time.unscaledTime + 1f / NetProtocol.StateRate;
                SendStates();
            }

            if (racing && Race.Phase == RacePhase.Running && Time.unscaledTime >= nextChaosLogAt)
            {
                nextChaosLogAt = Time.unscaledTime + 10f;
                int live = 0, remote = 0;
                foreach (var b in ChaosBody.Active)
                    if (b != null && b.Live)
                    {
                        live++;
                        if (b.Remote) remote++;
                    }
                Debug.Log($"[RandomCar] Сеть: хаос — событий от хоста {netEvents}, сигналов {netSignals}, состояний объектов {netBodyStates} (без адресата {netBodyMisses}), объектов на трассе {live} (копий {remote}), идёт событий {Director.ActiveEvents.Count}; бонусы — сообщений {netItems}, подборов {powerUps?.Pickups}, применений {powerUps?.Uses}, попаданий ракет {powerUps?.RocketHits}, пуль {powerUps?.BulletHits}" +
                          (IsHost ? $"; от клиентов: подборов {clientPickupsOk}/{clientPickups}, применений {clientUsesOk}/{clientUses}, попаданий {clientHits}" : ""));
            }

            if (IsHost && racing && Time.unscaledTime >= nextBodiesAt)
            {
                nextBodiesAt = Time.unscaledTime + 1f / NetProtocol.BodyRate;
                SendBodies();
            }

            if (IsHost && Time.unscaledTime >= nextLobbyAt) BroadcastLobby();
        }

        void SendStates()
        {
            outgoing.Clear();
            foreach (var racer in Race.Racers)
            {
                if (racer.Retired) continue;
                bool mine = racer.Kind == ParticipantKind.LocalPlayer || (IsHost && racer.Kind == ParticipantKind.Ai);
                if (mine) outgoing.Add(Capture(racer));
            }
            if (IsHost)
            {
                outgoing.AddRange(relay.Values);
                relay.Clear();
                SendToClients(NetProtocol.MsgStates, WriteStates, NetworkDelivery.UnreliableSequenced);
            }
            else SendToHost(NetProtocol.MsgStates, WriteStates, NetworkDelivery.UnreliableSequenced);
        }

        void WriteStates(FastBufferWriter w)
        {
            w.WriteValueSafe((byte)outgoing.Count);
            foreach (var s in outgoing) s.Write(w);
        }

        CarState Capture(Racer racer)
        {
            var physics = racer.Controller.Physics;
            var body = physics.Body;
            return new CarState
            {
                Slot = (byte)racer.Slot,
                Time = ServerTime,
                Position = body.position,
                Rotation = body.rotation,
                Velocity = body.isKinematic ? Vector3.zero : body.linearVelocity,
                AngularVelocity = body.isKinematic ? Vector3.zero : body.angularVelocity,
                Input = physics.Input,
                Locked = physics.Locked,
                Protected = racer.Controller.Protected,
                Shield = racer.Controller.Shielded,
            };
        }

        // ---------- сообщения ----------

        void RegisterHandlers()
        {
            var m = manager.CustomMessagingManager;
            m.RegisterNamedMessageHandler(NetProtocol.MsgLobby, OnLobby);
            m.RegisterNamedMessageHandler(NetProtocol.MsgProfile, OnProfile);
            m.RegisterNamedMessageHandler(NetProtocol.MsgReady, OnReady);
            m.RegisterNamedMessageHandler(NetProtocol.MsgSetup, OnSetup);
            m.RegisterNamedMessageHandler(NetProtocol.MsgLoaded, OnLoaded);
            m.RegisterNamedMessageHandler(NetProtocol.MsgGo, OnGo);
            m.RegisterNamedMessageHandler(NetProtocol.MsgStates, OnStates);
            m.RegisterNamedMessageHandler(NetProtocol.MsgEvent, OnEvent);
            m.RegisterNamedMessageHandler(NetProtocol.MsgFinish, OnFinish);
            m.RegisterNamedMessageHandler(NetProtocol.MsgLeft, OnLeft);
            m.RegisterNamedMessageHandler(NetProtocol.MsgBackToLobby, OnBackToLobby);
            m.RegisterNamedMessageHandler(NetProtocol.MsgSignal, OnSignal);
            m.RegisterNamedMessageHandler(NetProtocol.MsgBodies, OnBodies);
            m.RegisterNamedMessageHandler(NetProtocol.MsgItem, OnItem);
        }

        void SendToHost(string message, Action<FastBufferWriter> write, NetworkDelivery delivery = NetworkDelivery.ReliableFragmentedSequenced)
        {
            if (manager == null || !manager.IsConnectedClient) return;
            using var w = NetProtocol.NewWriter();
            write(w);
            manager.CustomMessagingManager.SendNamedMessage(message, NetworkManager.ServerClientId, w, delivery);
        }

        void SendToClients(string message, Action<FastBufferWriter> write, NetworkDelivery delivery = NetworkDelivery.ReliableFragmentedSequenced)
        {
            if (manager == null || !manager.IsServer) return;
            var targets = manager.ConnectedClientsIds.Where(id => id != LocalId).ToList();
            if (targets.Count == 0) return;
            using var w = NetProtocol.NewWriter();
            write(w);
            manager.CustomMessagingManager.SendNamedMessage(message, targets, w, delivery);
        }

        bool FromHost(ulong sender) => !IsHost && sender == NetworkManager.ServerClientId;

        void OnLobby(ulong sender, FastBufferReader r)
        {
            if (!FromHost(sender)) return;
            Lobby = LobbyState.Read(r);
            screen.Refresh();
        }

        void OnProfile(ulong sender, FastBufferReader r)
        {
            if (!IsHost) return;
            r.ReadValueSafe(out string name);
            r.ReadValueSafe(out string carKey);
            r.ReadValueSafe(out string buildJson);
            var player = Lobby.Find(sender);
            if (player == null || Lobby.Racing) return;
            var entry = flow.Cars.Get(carKey);
            player.CarKey = entry.Key;
            // Сборку проверяет хост: неизвестные детали и лишние наклейки отбрасываются (CarBuild.Sanitize).
            var build = CarBuild.FromJson(buildJson) ?? CarBuild.Default(entry);
            build.Sanitize(entry, flow.Cars, false);
            player.BuildJson = build.ToJson();
            if (player.Name == "Игрок" && name != "Игрок") player.Name = UniqueName(NetProtocol.CleanName(name));
            BroadcastLobby();
        }

        void OnReady(ulong sender, FastBufferReader r)
        {
            if (!IsHost) return;
            r.ReadValueSafe(out bool ready);
            var player = Lobby.Find(sender);
            if (player == null || Lobby.Racing) return;
            player.Ready = ready;
            BroadcastLobby();
        }

        void OnSetup(ulong sender, FastBufferReader r)
        {
            if (!FromHost(sender)) return;
            ApplySetup(RaceSetup.Read(r));
        }

        void OnLoaded(ulong sender, FastBufferReader r)
        {
            if (!IsHost) return;
            MarkLoaded(sender);
        }

        void OnGo(ulong sender, FastBufferReader r)
        {
            if (!FromHost(sender) || setup == null) return;
            r.ReadValueSafe(out double at);
            BeginRace(at);
        }

        void OnStates(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out byte count);
            for (int i = 0; i < count; i++)
            {
                var s = CarState.Read(r);
                if (IsHost)
                {
                    // Хост принимает от игрока только его машину и только правдоподобное состояние.
                    if (!slotOwners.TryGetValue(s.Slot, out var owner) || owner != sender || !Plausible(s)) continue;
                    relay[s.Slot] = s;
                }
                else if (sender != NetworkManager.ServerClientId) continue;
                if (proxies.TryGetValue(s.Slot, out var proxy) && proxy != null) proxy.Push(s);
            }
        }

        static bool Plausible(CarState s) =>
            !float.IsNaN(s.Position.x) && !float.IsNaN(s.Position.y) && !float.IsNaN(s.Position.z) &&
            s.Velocity.sqrMagnitude < 150f * 150f && Mathf.Abs(s.Position.y) < 5000f;

        void OnEvent(ulong sender, FastBufferReader r)
        {
            if (!FromHost(sender)) return;
            r.ReadValueSafe(out string id);
            r.ReadValueSafe(out float progress);
            r.ReadValueSafe(out float lateral);
            r.ReadValueSafe(out int seed);
            r.ReadValueSafe(out int focus);
            r.ReadValueSafe(out int serial);
            r.ReadValueSafe(out double hostTime);
            r.ReadValueSafe(out string combo);
            var spot = new HazardSpot { Progress = progress, Lateral = lateral, Seed = seed, FocusSlot = focus };
            // Задержка сообщения: на столько хост запустил событие раньше (фаза молотов, стен и перчаток).
            float offset = Mathf.Clamp((float)(ServerTime - hostTime), 0f, 0.5f);
            netEvents++;
            if (!Director.LaunchRemote(id, spot, serial, offset, combo)) Debug.LogWarning($"[RandomCar] Сеть: событие {id} от хоста не запущено (стадия гонки здесь: {Race.Phase})");
        }

        void OnFinish(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int slot);
            r.ReadValueSafe(out float time);
            if (IsHost)
            {
                if (!slotOwners.TryGetValue(slot, out var owner) || owner != sender) return;
                Race.ReportFinish(slot, time);
                SendToClients(NetProtocol.MsgFinish, w => WriteFinish(w, slot, time));
            }
            else if (FromHost(sender) && Race.Player != null && Race.Player.Slot != slot) Race.ReportFinish(slot, time);
        }

        void OnLeft(ulong sender, FastBufferReader r)
        {
            if (!FromHost(sender)) return;
            r.ReadValueSafe(out int slot);
            var racer = Race.BySlot(slot);
            if (racer != null) Ui.ShowNotice($"{racer.Name} отключился");
            Race.Retire(slot);
            proxies.Remove(slot);
        }

        void OnBackToLobby(ulong sender, FastBufferReader r)
        {
            if (!FromHost(sender)) return;
            bool wasRacing = Race.Phase == RacePhase.Running && Race.Player != null && !Race.Player.Finished;
            EnterLobbyLocal();
            if (wasRacing) Ui.ShowNotice("Хост вернул всех в лобби.");
            if (autoReady) SetReady(true);
        }

        // ---------- запуск с ключами (проверка двумя экземплярами) ----------

        bool autoReady;

        /// -rc-name Имя, -rc-host [порт], -rc-loopback, -rc-join адрес, -rc-ready, -rc-autopilot, -rc-norecord. Выполняется, когда открыто меню.
        IEnumerator CommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string Value(string key)
            {
                int i = Array.IndexOf(args, key);
                return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal) ? args[i + 1] : null;
            }
            bool host = args.Contains("-rc-host");
            string join = Value("-rc-join");
            Autopilot = args.Contains("-rc-autopilot");
            autoReady = args.Contains("-rc-ready");
            string name = Value("-rc-name");
            if (name != null) nameOverride = NetProtocol.CleanName(name);
            // Два экземпляра на одном компьютере делят сохранения: проверочные гонки рекорды и кредиты не пишут.
            if (args.Contains("-rc-norecord")) Race.RecordBestTimes = false;
            if (!host && join == null) yield break;
            while (flow.State != FlowState.Menu) yield return null;
            string error = host ? Host(Value("-rc-host") ?? NetProtocol.DefaultPort.ToString(), args.Contains("-rc-loopback")) : Join(join);
            if (error != null)
            {
                Debug.LogWarning($"[RandomCar] Сеть: {error}");
                Ui.ShowNotice(error, 7f);
            }
        }
    }
}
