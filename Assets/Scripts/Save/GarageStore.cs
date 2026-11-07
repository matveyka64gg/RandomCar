using System;
using System.Collections.Generic;
using System.IO;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Save
{
    /// Гараж игрока: кредиты, купленные машины и детали, сборки каждой машины (несколько, одна активная),
    /// статистика для открытия машин. JSON в persistentDataPath/garage.json (версия 2; файл версии 1 —
    /// краска/диск/спойлер по индексам — переводится при чтении). Деньги только игровые, без реальных платежей.
    public static class GarageStore
    {
        /// Стартовый капитал новой игры: ноль. Деньги появляются только за гонки (см. Reward).
        public const int StartCredits = 0;
        public const int MaxBuilds = 6;

        [Serializable]
        sealed class CarSave
        {
            public string car;
            public int active;
            public List<CarBuild> builds = new List<CarBuild>();
            public List<string> ownedParts = new List<string>();
            // Версия 1: индексы краски, дисков и спойлера.
            public int paint = -1;
            public int rim = -1;
            public int spoiler = -1;
        }

        [Serializable]
        sealed class Data
        {
            public int version;
            public int credits = StartCredits;
            public List<string> ownedCars = new List<string>();
            public List<CarSave> cars = new List<CarSave>();
            public int races;
            public int wins;
            public List<string> mapsFinished = new List<string>();
        }

        static Data data;
        static string overridePath;

        public static event Action Changed;

        static string FilePath => overridePath ?? Path.Combine(Application.persistentDataPath, "garage.json");

        /// Для тестов: отдельный файл и чистое состояние.
        public static void UseFile(string path)
        {
            overridePath = path;
            data = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            data = null;
            overridePath = null;
            Changed = null;
        }

        static Data Current
        {
            get
            {
                if (data != null) return data;
                data = new Data();
                try
                {
                    if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), data);
                }
                catch (Exception error) when (error is IOException || error is ArgumentException || error is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"[RandomCar] Гараж не прочитан, начинаем заново: {error.Message}");
                    data = new Data();
                }
                if (data.version < 2)
                {
                    // Старый файл: деньги и машины по умолчанию, вид переводится в сборку при первом обращении.
                    data.version = 2;
                    data.credits = StartCredits;
                }
                return data;
            }
        }

        static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[RandomCar] Гараж не сохранён: {error.Message}");
            }
            Changed?.Invoke();
        }

        // ---------- деньги и машины ----------

        public static int Credits => Current.credits;
        public static int Races => Current.races;
        public static int Wins => Current.wins;

        public static void AddCredits(int amount)
        {
            if (amount <= 0) return;
            Current.credits = (int)Math.Min(int.MaxValue, (long)Current.credits + amount);
            Save();
        }

        /// Списать, если хватает. Вызывается только после подтверждения покупки игроком.
        public static bool TrySpend(int amount)
        {
            if (amount < 0 || Current.credits < amount) return false;
            Current.credits -= amount;
            Save();
            return true;
        }

        public static bool OwnsCar(CarEntry car) => car != null && (car.Price <= 0 || Current.ownedCars.Contains(car.Key));

        /// Выполнено ли условие открытия машины (без учёта цены).
        public static bool Unlocked(CarEntry car, out string requirement)
        {
            requirement = "";
            if (car == null || string.IsNullOrEmpty(car.Unlock)) return true;
            var parts = car.Unlock.Split(':');
            if (parts.Length != 2) return true;
            switch (parts[0])
            {
                case "wins":
                    int wins = int.TryParse(parts[1], out var w) ? w : 0;
                    requirement = $"Победы: {Math.Min(Current.wins, wins)} из {wins}";
                    return Current.wins >= wins;
                case "races":
                    int races = int.TryParse(parts[1], out var r) ? r : 0;
                    requirement = $"Гонки: {Math.Min(Current.races, races)} из {races}";
                    return Current.races >= races;
                case "map":
                    requirement = "Финишировать на карте «" + MapName(parts[1]) + "»";
                    return Current.mapsFinished.Contains(parts[1]);
                default:
                    return true;
            }
        }

        static string MapName(string key) => key switch
        {
            "TelAviv" => "Тель-Авив",
            "Desert" => "Пустыня",
            "City" => "Город",
            _ => "Холмы Рандома",
        };

        /// Покупка машины после подтверждения. false — не хватает кредитов или машина закрыта.
        public static bool BuyCar(CarEntry car)
        {
            if (car == null || OwnsCar(car) || !Unlocked(car, out _)) return false;
            if (!TrySpend(car.Price)) return false;
            Current.ownedCars.Add(car.Key);
            Save();
            return true;
        }

        // ---------- детали ----------

        static CarSave Car(string key)
        {
            var e = Current.cars.Find(x => x.car == key);
            if (e == null) Current.cars.Add(e = new CarSave { car = key });
            e.builds ??= new List<CarBuild>();
            e.ownedParts ??= new List<string>();
            return e;
        }

        public static bool OwnsPart(CarEntry car, PartOption part) =>
            part == null || part.Price <= 0 || part.Variant == CarBuild.Stock || Car(car.Key).ownedParts.Contains(part.Id);

        public static bool BuyPart(CarEntry car, PartOption part)
        {
            if (OwnsPart(car, part)) return true;
            if (!TrySpend(part.Price)) return false;
            Car(car.Key).ownedParts.Add(part.Id);
            Save();
            return true;
        }

        // ---------- сборки ----------

        /// Сборки машины (копии). Если сборок нет — одна заводская (или переведённая из файла версии 1).
        public static List<CarBuild> Builds(CarEntry car, CarCatalog catalog)
        {
            var e = Car(car.Key);
            if (e.builds.Count == 0)
            {
                e.builds.Add(e.paint >= 0 ? Migrate(car, e) : CarBuild.Default(car));
                e.paint = e.rim = e.spoiler = -1;
            }
            var list = new List<CarBuild>();
            foreach (var b in e.builds)
            {
                var copy = b.Clone();
                copy.Sanitize(car, catalog);
                list.Add(copy);
            }
            return list;
        }

        public static int ActiveIndex(CarEntry car, CarCatalog catalog)
        {
            int count = Builds(car, catalog).Count;
            return Mathf.Clamp(Car(car.Key).active, 0, count - 1);
        }

        /// Активная сборка машины (копия, приведённая к допустимому).
        public static CarBuild Active(CarEntry car, CarCatalog catalog)
        {
            var builds = Builds(car, catalog);
            return builds[Mathf.Clamp(Car(car.Key).active, 0, builds.Count - 1)];
        }

        public static void SetActive(CarEntry car, int index)
        {
            var e = Car(car.Key);
            e.active = Mathf.Clamp(index, 0, Mathf.Max(0, e.builds.Count - 1));
            Save();
        }

        /// Сохранить сборку в слот index (или новую в конец, если index вне списка и есть место).
        public static int SaveBuild(CarEntry car, CarCatalog catalog, int index, CarBuild build)
        {
            Builds(car, catalog);
            var e = Car(car.Key);
            var copy = build.Clone();
            copy.Sanitize(car, catalog);
            if (index >= 0 && index < e.builds.Count) e.builds[index] = copy;
            else if (e.builds.Count < MaxBuilds)
            {
                e.builds.Add(copy);
                index = e.builds.Count - 1;
            }
            else return -1;
            Save();
            return index;
        }

        public static void DeleteBuild(CarEntry car, int index)
        {
            var e = Car(car.Key);
            if (e.builds.Count <= 1 || index < 0 || index >= e.builds.Count) return;
            e.builds.RemoveAt(index);
            if (e.active >= e.builds.Count) e.active = e.builds.Count - 1;
            Save();
        }

        // ---------- статистика ----------

        /// Итог гонки игрока: награда в кредитах. place — место (с 1), racers — число участников.
        public static int RecordRace(int place, int racers, string mapKey, bool record)
        {
            Current.races++;
            if (place == 1) Current.wins++;
            if (!string.IsNullOrEmpty(mapKey) && !Current.mapsFinished.Contains(mapKey)) Current.mapsFinished.Add(mapKey);
            int reward = Reward(place, racers, record);
            Current.credits += reward;
            Save();
            return reward;
        }

        public static int Reward(int place, int racers, bool record)
        {
            int beaten = Mathf.Max(0, racers - place);
            return 800 + beaten * 700 + (place == 1 ? 1000 : 0) + (record ? 500 : 0);
        }

        // ---------- перевод старого формата ----------

        static readonly string[] LegacyPaints =
        {
            "#FF4B2B", "#2F6BFF", "#FFC21A", "#2DD4A0", "#FF6A2A", "#B3123A",
            "#8E5BFF", "#A6F23A", "#24E0C8", "#4AA3E8", "#F2F0E8", "#2E3238",
        };
        static readonly string[] LegacyRims = { CarBuild.Stock, "Five", "Ten", "Six", "Dish", "Mesh" };
        static readonly string[] LegacySpoilers = { CarBuild.Stock, "Lip", "Ducktail", "Wing" };

        static CarBuild Migrate(CarEntry car, CarSave e)
        {
            var build = CarBuild.Default(car);
            build.Name = "Моя";
            if (e.paint >= 0 && e.paint < LegacyPaints.Length && ColorUtility.TryParseHtmlString(LegacyPaints[e.paint], out var c)) build.Paint.Body = c;
            // Детали, выбранные в старом гараже, считаются купленными.
            if (e.rim > 0 && e.rim < LegacyRims.Length)
            {
                build.Set(PartSlot.Rims, LegacyRims[e.rim]);
                e.ownedParts.Add($"{PartSlot.Rims}.{LegacyRims[e.rim]}");
            }
            if (e.spoiler > 0 && e.spoiler < LegacySpoilers.Length)
            {
                build.Set(PartSlot.Spoiler, LegacySpoilers[e.spoiler]);
                e.ownedParts.Add($"{PartSlot.Spoiler}.{LegacySpoilers[e.spoiler]}");
            }
            return build;
        }
    }
}
