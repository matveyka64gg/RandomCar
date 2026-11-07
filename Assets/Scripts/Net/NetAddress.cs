using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RandomCar.Net
{
    /// Адреса для прямого подключения: разбор «IP:порт» с понятными ошибками и список адресов этого компьютера
    /// (локальная сеть, Radmin VPN, Hamachi) — хост сообщает их друзьям сам, без серверов и облака.
    public static class NetAddress
    {
        public readonly struct LocalAddress
        {
            public readonly string Ip;
            public readonly string Label;

            public LocalAddress(string ip, string label)
            {
                Ip = ip;
                Label = label;
            }
        }

        /// Разбор адреса хоста: «192.168.1.5», «26.12.3.4:7778», «localhost». error — сообщение для игрока.
        public static bool TryParse(string text, ushort defaultPort, out string ip, out ushort port, out string error)
        {
            ip = null;
            port = defaultPort;
            error = null;
            string value = (text ?? "").Trim();
            if (value.Length == 0)
            {
                error = "Введи адрес хоста, например 192.168.1.5 или 26.1.2.3:7777.";
                return false;
            }
            int colon = value.LastIndexOf(':');
            if (colon >= 0)
            {
                if (!TryParsePort(value.Substring(colon + 1), out port, out error)) return false;
                value = value.Substring(0, colon).Trim();
            }
            if (string.Equals(value, "localhost", System.StringComparison.OrdinalIgnoreCase)) value = "127.0.0.1";
            var parts = value.Split('.');
            if (parts.Length != 4 || !IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            {
                error = $"«{value}» — не IPv4-адрес. Нужны четыре числа через точку, например 192.168.1.5.";
                return false;
            }
            foreach (var part in parts)
            {
                if (part.Length == 0 || part.Length > 3 || !int.TryParse(part, out int octet) || octet > 255)
                {
                    error = $"«{value}» — не IPv4-адрес. Каждое число — от 0 до 255.";
                    return false;
                }
            }
            if (value == "0.0.0.0" || value == "255.255.255.255")
            {
                error = $"К адресу {value} подключиться нельзя — введи адрес компьютера хоста.";
                return false;
            }
            ip = value;
            return true;
        }

        /// Порт 1024–65535 (ниже — системные порты Windows).
        public static bool TryParsePort(string text, out ushort port, out string error)
        {
            port = 0;
            error = null;
            if (!int.TryParse((text ?? "").Trim(), out int value) || value < 1024 || value > 65535)
            {
                error = "Порт — число от 1024 до 65535 (обычно 7777).";
                return false;
            }
            port = (ushort)value;
            return true;
        }

        /// Подпись сети по адресу и имени адаптера.
        public static string Classify(string ip, string adapterName)
        {
            string name = (adapterName ?? "").ToLowerInvariant();
            if (name.Contains("radmin") || ip.StartsWith("26.")) return "Radmin VPN";
            if (name.Contains("hamachi") || ip.StartsWith("25.")) return "Hamachi";
            if (ip.StartsWith("192.168.") || ip.StartsWith("10.")) return "локальная сеть";
            if (ip.StartsWith("172."))
            {
                var parts = ip.Split('.');
                if (parts.Length > 1 && int.TryParse(parts[1], out int second) && second >= 16 && second <= 31) return "локальная сеть";
            }
            return "другая сеть";
        }

        /// IPv4-адреса работающих адаптеров этого компьютера (без 127.0.0.1): VPN первыми.
        public static List<LocalAddress> LocalAddresses()
        {
            var result = new List<LocalAddress>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = unicast.Address.ToString();
                    if (ip.StartsWith("127.") || ip.StartsWith("169.254.")) continue;
                    result.Add(new LocalAddress(ip, Classify(ip, adapter.Name + " " + adapter.Description)));
                }
            }
            result.Sort((a, b) => Rank(a.Label).CompareTo(Rank(b.Label)));
            return result;
        }

        static int Rank(string label) => label switch
        {
            "Radmin VPN" => 0,
            "Hamachi" => 1,
            "локальная сеть" => 2,
            _ => 3,
        };
    }
}
