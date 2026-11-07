using System.Collections.Generic;
using RandomCar.Car;
using RandomCar.Maps;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RandomCar.Net
{
    /// Протокол сетевой игры: версия, порт, имена сообщений и их содержимое. Хост — единственный источник
    /// лобби, состава гонки, старта, событий и результатов; машину считает её владелец и рассылает состояние.
    public static class NetProtocol
    {
        /// Версия протокола: игроки с разными версиями игры не соединяются (понятная ошибка вместо сбоя).
        // net6: шоу-события v6 (Верити, EL DRIFTO BUS); net7: ГИПЕРГАМИЯ и шоу на всех уровнях Хаоса —
        // у клиента должны быть те же события.
        public const string Version = UI.GameUI.Version + "/net7";
        public const ushort DefaultPort = 7777;
        public const int MaxPlayers = MapDefinition.MaxGrid;
        /// Частота рассылки состояния машин, раз в секунду.
        public const int StateRate = 30;
        /// Отставание показа чужих машин: интерполяция между снимками вместо рывков.
        public const double InterpolationDelay = 0.15;
        /// Сколько ждать загрузки карты у всех перед стартом, с.
        public const float LoadTimeout = 20f;
        /// Запас между командой «старт» и отсчётом: сообщение успевает дойти до всех.
        public const double StartLead = 1.2;

        public const string MsgLobby = "rc.lobby";
        public const string MsgProfile = "rc.profile";
        public const string MsgReady = "rc.ready";
        public const string MsgSetup = "rc.setup";
        public const string MsgLoaded = "rc.loaded";
        public const string MsgGo = "rc.go";
        public const string MsgStates = "rc.states";
        public const string MsgEvent = "rc.event";
        public const string MsgFinish = "rc.finish";
        public const string MsgLeft = "rc.left";
        public const string MsgBackToLobby = "rc.back";
        /// Сигнал события (мина задета, бочка взорвалась): номер розыгрыша, код, индекс, точка.
        public const string MsgSignal = "rc.signal";
        /// Физические объекты хаоса (шары, бочки): хост рассылает их состояние.
        public const string MsgBodies = "rc.bodies";
        /// Бонусы: просьбы клиента (взять ящик, применить), выдача и применение (хост), попадания (через хоста).
        public const string MsgItem = "rc.item";

        /// Частота рассылки физических объектов, раз в секунду.
        public const int BodyRate = 20;

        /// Данные запроса на подключение: версия и имя игрока.
        public static byte[] ConnectionPayload(string name)
        {
            using var w = NewWriter();
            w.WriteValueSafe(Version);
            w.WriteValueSafe(CleanName(name));
            return w.ToArray();
        }

        public static void ReadConnectionPayload(byte[] payload, out string version, out string name)
        {
            version = "";
            name = "Игрок";
            if (payload == null || payload.Length == 0) return;
            using var r = new FastBufferReader(payload, Allocator.Temp);
            r.ReadValueSafe(out version);
            r.ReadValueSafe(out name);
            name = CleanName(name);
        }

        /// Имя игрока: без управляющих символов, не длиннее 16, пустое — «Игрок».
        public static string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Игрок";
            var chars = new List<char>();
            foreach (char c in name.Trim())
                if (!char.IsControl(c) && chars.Count < 16) chars.Add(c);
            return chars.Count > 0 ? new string(chars.ToArray()) : "Игрок";
        }

        /// Буфер с ростом до 64 КБ (сборки машин с наклейками — несколько КБ).
        public static FastBufferWriter NewWriter(int size = 256) => new FastBufferWriter(size, Allocator.Temp, 64 * 1024);
    }

    /// Игрок в лобби.
    public sealed class LobbyPlayer
    {
        public ulong ClientId;
        public string Name = "Игрок";
        public string CarKey = "";
        /// Сборка машины (JSON). Хранится у хоста, в рассылку лобби не входит — только в состав гонки.
        public string BuildJson = "";
        public bool Ready;
        public int PingMs;
        public bool Loaded;
    }

    /// Состояние лобби, которое хост рассылает всем.
    public sealed class LobbyState
    {
        public string MapKey = "";
        public int AiCount = 2;
        public int Chaos = 3;
        public bool Racing;
        public readonly List<LobbyPlayer> Players = new List<LobbyPlayer>();

        public LobbyPlayer Find(ulong clientId) => Players.Find(p => p.ClientId == clientId);

        public void Write(FastBufferWriter w)
        {
            w.WriteValueSafe(MapKey);
            w.WriteValueSafe(AiCount);
            w.WriteValueSafe(Chaos);
            w.WriteValueSafe(Racing);
            w.WriteValueSafe(Players.Count);
            foreach (var p in Players)
            {
                w.WriteValueSafe(p.ClientId);
                w.WriteValueSafe(p.Name);
                w.WriteValueSafe(p.CarKey);
                w.WriteValueSafe(p.Ready);
                w.WriteValueSafe(p.PingMs);
            }
        }

        public static LobbyState Read(FastBufferReader r)
        {
            var s = new LobbyState();
            r.ReadValueSafe(out s.MapKey);
            r.ReadValueSafe(out s.AiCount);
            r.ReadValueSafe(out s.Chaos);
            r.ReadValueSafe(out s.Racing);
            r.ReadValueSafe(out int count);
            count = Mathf.Clamp(count, 0, NetProtocol.MaxPlayers);
            for (int i = 0; i < count; i++)
            {
                var p = new LobbyPlayer();
                r.ReadValueSafe(out p.ClientId);
                r.ReadValueSafe(out p.Name);
                r.ReadValueSafe(out p.CarKey);
                r.ReadValueSafe(out p.Ready);
                r.ReadValueSafe(out p.PingMs);
                s.Players.Add(p);
            }
            return s;
        }
    }

    /// Участник гонки в составе, который хост рассылает перед стартом. Порядок списка = места на решётке.
    public sealed class SetupEntry
    {
        public const ulong AiClient = ulong.MaxValue;

        public ulong ClientId = AiClient;
        public string Name = "";
        public string CarKey = "";
        public string BuildJson = "";

        public bool IsAi => ClientId == AiClient;
    }

    /// Состав гонки: карта, Хаос и участники.
    public sealed class RaceSetup
    {
        public string MapKey = "";
        public int Chaos = 3;
        public int Seed;
        public readonly List<SetupEntry> Entries = new List<SetupEntry>();

        public void Write(FastBufferWriter w)
        {
            w.WriteValueSafe(MapKey);
            w.WriteValueSafe(Chaos);
            w.WriteValueSafe(Seed);
            w.WriteValueSafe(Entries.Count);
            foreach (var e in Entries)
            {
                w.WriteValueSafe(e.ClientId);
                w.WriteValueSafe(e.Name);
                w.WriteValueSafe(e.CarKey);
                w.WriteValueSafe(e.BuildJson);
            }
        }

        public static RaceSetup Read(FastBufferReader r)
        {
            var s = new RaceSetup();
            r.ReadValueSafe(out s.MapKey);
            r.ReadValueSafe(out s.Chaos);
            r.ReadValueSafe(out s.Seed);
            r.ReadValueSafe(out int count);
            count = Mathf.Clamp(count, 0, MapDefinition.MaxGrid);
            for (int i = 0; i < count; i++)
            {
                var e = new SetupEntry();
                r.ReadValueSafe(out e.ClientId);
                r.ReadValueSafe(out e.Name);
                r.ReadValueSafe(out e.CarKey);
                r.ReadValueSafe(out e.BuildJson);
                s.Entries.Add(e);
            }
            return s;
        }
    }

    /// Снимок машины от её владельца: время сервера, положение, скорости и ввод (для колёс, звука, дыма).
    public struct CarState
    {
        public byte Slot;
        public double Time;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        public CarInput Input;
        public bool Locked;
        /// Защита после возврата на трассу и щит (бонус): у всех игроков машина видна защищённой.
        public bool Protected;
        public bool Shield;

        public void Write(FastBufferWriter w)
        {
            w.WriteValueSafe(Slot);
            w.WriteValueSafe(Time);
            w.WriteValueSafe(Position);
            w.WriteValueSafe(Rotation);
            w.WriteValueSafe(Velocity);
            w.WriteValueSafe(AngularVelocity);
            w.WriteValueSafe(Quantize(Input.Throttle, 0f));
            w.WriteValueSafe(Quantize(Input.Brake, 0f));
            w.WriteValueSafe(Quantize(Input.Steer, -1f));
            byte flags = (byte)((Input.Handbrake ? 1 : 0) | (Locked ? 2 : 0) | (Protected ? 4 : 0) | (Shield ? 8 : 0));
            w.WriteValueSafe(flags);
        }

        public static CarState Read(FastBufferReader r)
        {
            var s = new CarState();
            r.ReadValueSafe(out s.Slot);
            r.ReadValueSafe(out s.Time);
            r.ReadValueSafe(out s.Position);
            r.ReadValueSafe(out s.Rotation);
            r.ReadValueSafe(out s.Velocity);
            r.ReadValueSafe(out s.AngularVelocity);
            r.ReadValueSafe(out byte throttle);
            r.ReadValueSafe(out byte brake);
            r.ReadValueSafe(out byte steer);
            r.ReadValueSafe(out byte flags);
            s.Input = new CarInput
            {
                Throttle = Dequantize(throttle, 0f),
                Brake = Dequantize(brake, 0f),
                Steer = Dequantize(steer, -1f),
                Handbrake = (flags & 1) != 0,
            };
            s.Locked = (flags & 2) != 0;
            s.Protected = (flags & 4) != 0;
            s.Shield = (flags & 8) != 0;
            s.Rotation = Normalized(s.Rotation);
            return s;
        }

        /// Значение min..1 в байт (педали 0..1, руль -1..1).
        static byte Quantize(float value, float min) => (byte)Mathf.RoundToInt(Mathf.InverseLerp(min, 1f, value) * 255f);

        static float Dequantize(byte value, float min) => Mathf.Lerp(min, 1f, value / 255f);

        static Quaternion Normalized(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 1e-4f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }
    }
}
