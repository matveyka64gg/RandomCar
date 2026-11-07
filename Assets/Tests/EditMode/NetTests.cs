using NUnit.Framework;
using RandomCar.Car;
using RandomCar.Net;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RandomCar.Tests
{
    /// Сеть без соединения: формат сообщений, разбор адресов, интерполяция чужих машин.
    public sealed class NetTests
    {
        static FastBufferReader Roundtrip(System.Action<FastBufferWriter> write)
        {
            using var w = NetProtocol.NewWriter();
            write(w);
            return new FastBufferReader(w.ToArray(), Allocator.Temp);
        }

        [Test]
        public void CarStateSurvivesSerialization()
        {
            var s = new CarState
            {
                Slot = 3,
                Time = 123.456,
                Position = new Vector3(10f, 2f, -40f),
                Rotation = Quaternion.Euler(5f, 120f, -3f),
                Velocity = new Vector3(30f, 0.5f, -2f),
                AngularVelocity = new Vector3(0f, 1.2f, 0f),
                Input = new CarInput { Throttle = 0.8f, Brake = 0f, Steer = -0.5f, Handbrake = true },
                Locked = false,
            };
            var r = Roundtrip(w => s.Write(w));
            var back = CarState.Read(r);
            r.Dispose();
            Assert.AreEqual(s.Slot, back.Slot);
            Assert.AreEqual(s.Time, back.Time, 1e-9);
            Assert.That(Vector3.Distance(s.Position, back.Position), Is.LessThan(1e-4f));
            Assert.That(Quaternion.Angle(s.Rotation, back.Rotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(s.Velocity, back.Velocity), Is.LessThan(1e-4f));
            Assert.AreEqual(0.8f, back.Input.Throttle, 0.01f, "газ квантуется в байт");
            Assert.AreEqual(-0.5f, back.Input.Steer, 0.01f, "руль -1..1 квантуется в байт");
            Assert.IsTrue(back.Input.Handbrake);
            Assert.IsFalse(back.Locked);
        }

        [Test]
        public void LobbyAndSetupSurviveSerialization()
        {
            var lobby = new LobbyState { MapKey = "TelAviv", AiCount = 3, Chaos = 2, Racing = true };
            lobby.Players.Add(new LobbyPlayer { ClientId = 0, Name = "Хост", CarKey = "Grom", Ready = true, PingMs = 0 });
            lobby.Players.Add(new LobbyPlayer { ClientId = 5, Name = "Друг", CarKey = "Iskra", Ready = false, PingMs = 48, BuildJson = "секрет" });
            var r = Roundtrip(w => lobby.Write(w));
            var back = LobbyState.Read(r);
            r.Dispose();
            Assert.AreEqual("TelAviv", back.MapKey);
            Assert.AreEqual(3, back.AiCount);
            Assert.AreEqual(2, back.Chaos);
            Assert.IsTrue(back.Racing);
            Assert.AreEqual(2, back.Players.Count);
            Assert.AreEqual("Друг", back.Players[1].Name);
            Assert.AreEqual(48, back.Players[1].PingMs);
            Assert.AreEqual("", back.Players[1].BuildJson, "сборки в рассылку лобби не входят");

            var build = CarBuild.Default(null);
            var setup = new RaceSetup { MapKey = "City", Chaos = 1, Seed = 42 };
            setup.Entries.Add(new SetupEntry { ClientId = 0, Name = "Хост", CarKey = "Grom", BuildJson = build.ToJson() });
            setup.Entries.Add(new SetupEntry { Name = "Сапсан", CarKey = "Sapsan", BuildJson = "{}" });
            r = Roundtrip(w => setup.Write(w));
            var s2 = RaceSetup.Read(r);
            r.Dispose();
            Assert.AreEqual("City", s2.MapKey);
            Assert.AreEqual(42, s2.Seed);
            Assert.AreEqual(2, s2.Entries.Count);
            Assert.IsFalse(s2.Entries[0].IsAi);
            Assert.IsTrue(s2.Entries[1].IsAi);
            Assert.AreEqual(build.ToJson(), s2.Entries[0].BuildJson);
        }

        [Test]
        public void ConnectionPayloadCarriesVersionAndCleanName()
        {
            var payload = NetProtocol.ConnectionPayload("  Очень\tдлинное имя игрока для лобби  ");
            NetProtocol.ReadConnectionPayload(payload, out string version, out string name);
            Assert.AreEqual(NetProtocol.Version, version);
            Assert.That(name.Length, Is.LessThanOrEqualTo(16));
            Assert.IsFalse(name.Contains("\t"));
            Assert.AreEqual("Игрок", NetProtocol.CleanName("   "));
        }

        [TestCase("192.168.1.5", "192.168.1.5", 7777)]
        [TestCase(" 26.10.20.30:7780 ", "26.10.20.30", 7780)]
        [TestCase("localhost", "127.0.0.1", 7777)]
        [TestCase("localhost:9000", "127.0.0.1", 9000)]
        public void ParsesValidAddresses(string text, string ip, int port)
        {
            Assert.IsTrue(NetAddress.TryParse(text, 7777, out var parsedIp, out var parsedPort, out var error), error);
            Assert.AreEqual(ip, parsedIp);
            Assert.AreEqual(port, parsedPort);
        }

        [TestCase("")]
        [TestCase("192.168.1")]
        [TestCase("192.168.1.300")]
        [TestCase("abc.def.ghi.jkl")]
        [TestCase("192.168.1.5:80")]
        [TestCase("192.168.1.5:99999")]
        [TestCase("0.0.0.0")]
        public void RejectsBadAddressesWithRussianMessage(string text)
        {
            Assert.IsFalse(NetAddress.TryParse(text, 7777, out _, out _, out var error));
            Assert.IsFalse(string.IsNullOrEmpty(error));
            Assert.That(error, Does.Match("[а-яА-Я]"), "сообщение для игрока — по-русски");
        }

        [Test]
        public void ClassifiesVpnAndLocalNetworks()
        {
            Assert.AreEqual("Radmin VPN", NetAddress.Classify("26.1.2.3", "Ethernet 3"));
            Assert.AreEqual("Hamachi", NetAddress.Classify("25.4.5.6", "Ethernet 4"));
            Assert.AreEqual("локальная сеть", NetAddress.Classify("192.168.0.10", "Wi-Fi"));
            Assert.AreEqual("локальная сеть", NetAddress.Classify("172.20.1.1", "Ethernet"));
            Assert.AreEqual("другая сеть", NetAddress.Classify("8.8.8.8", "Ethernet"));
        }

        static CarState At(double time, Vector3 position, Vector3 velocity) =>
            new CarState { Time = time, Position = position, Rotation = Quaternion.identity, Velocity = velocity };

        [Test]
        public void SnapshotsInterpolateBetweenAndExtrapolateBriefly()
        {
            var buffer = new SnapshotBuffer();
            var v = new Vector3(0f, 0f, 30f);
            buffer.Push(At(1.0, Vector3.zero, v));
            buffer.Push(At(1.1, new Vector3(0f, 0f, 3f), v));
            Assert.IsFalse(buffer.Push(At(1.05, Vector3.one, v)), "старый пакет после нового отбрасывается");

            Assert.IsTrue(buffer.Sample(1.05, out var mid, out bool extrapolated));
            Assert.IsFalse(extrapolated);
            Assert.AreEqual(1.5f, mid.Position.z, 0.01f, "равномерное движение — середина пути");

            Assert.IsTrue(buffer.Sample(1.2, out var ahead, out extrapolated));
            Assert.IsTrue(extrapolated);
            Assert.AreEqual(6f, ahead.Position.z, 0.01f, "досчёт по скорости");

            buffer.Sample(5.0, out var far, out _);
            Assert.AreEqual(3f + 30f * (float)SnapshotBuffer.MaxExtrapolation, far.Position.z, 0.01f, "экстраполяция не дольше предела");
        }

        [Test]
        public void StandingsUseFinishTimesThenDistanceThenRetired()
        {
            var early = new RandomCar.Race.Racer { Name = "раньше", Finished = true, FinishTime = 274.7f, Laps = new RandomCar.Race.LapCounter(0f) };
            var late = new RandomCar.Race.Racer { Name = "позже", Finished = true, FinishTime = 278.6f, Laps = new RandomCar.Race.LapCounter(0f) };
            var driving = new RandomCar.Race.Racer { Name = "в пути", Laps = new RandomCar.Race.LapCounter(0f), Progress = 0.9f };
            var gone = new RandomCar.Race.Racer { Name = "сошёл", Retired = true, Laps = new RandomCar.Race.LapCounter(0f) };
            var list = new System.Collections.Generic.List<RandomCar.Race.Racer> { gone, driving, late, early };
            list.Sort(RandomCar.Race.RaceManager.CompareRacers);
            CollectionAssert.AreEqual(new[] { "раньше", "позже", "в пути", "сошёл" }, list.ConvertAll(r => r.Name));
        }

        [Test]
        public void SnapshotBufferKeepsNewestWhenFull()
        {
            var buffer = new SnapshotBuffer();
            for (int i = 0; i < SnapshotBuffer.Capacity + 10; i++) buffer.Push(At(i * 0.033, new Vector3(i, 0f, 0f), Vector3.zero));
            Assert.AreEqual(SnapshotBuffer.Capacity, buffer.Count);
            Assert.AreEqual(SnapshotBuffer.Capacity + 9, buffer.Latest.Position.x, 1e-4f);
        }
    }
}
