using System;
using System.Collections.Generic;
using System.IO;
using RandomCar.Audio;
using RandomCar.Car;
using RandomCar.Events;
using UnityEngine;

namespace RandomCar.Core
{
    /// Настройки игрока: Хаос, громкость, качество. Хранятся локально в JSON рядом с таблицей лучших.
    public static class GameSettings
    {
        [Serializable]
        sealed class Data
        {
            public int chaos = (int)ChaosLevel.Mayhem;
            /// Версия правил Хаоса, при которой выбран уровень: v5 сделал «безумие» стандартом.
            public int chaosRev;
            public float volume = 0.8f;
            /// -1 — по устройству: телефон — «экономная» графика (Mobile), ПК — «высокая» (PC).
            public int quality = -1;
            public bool showFps;
            public float steerSensitivity = 1f;
            public int assists = (int)AssistLevel.Standard;
            public int aiDifficulty = 1;
            public string car = "Iskra";
            public string map = "Hills";
            public string playerName = "Игрок";
            public string lastAddress = "";
            public int port = 7777;
            public bool introSeen;
            public float musicVolume = 0.6f;
            public float sfxVolume = 1f;
            public int musicRepeat;
            public bool musicShuffle;
            public bool musicPaused;
            public List<KeyValue> lastTracks = new List<KeyValue>();
            public int rocketShow = 3;
            public int effectsQuality = 1;
            public bool airSiren = true;
            public float mouseSensitivity = 1f;
            public int cameraMode;
            public string cameraKey = "";
            public string itemKey = "";
            /// Источник музыки: 0 — вся (встроенная и своя), 1 — встроенная, 2 — своя (UserContent).
            public int musicSource;
            /// Экранные кнопки управления: -1 — по устройству (есть сенсорный экран), 0 — выкл, 1 — вкл.
            public int touchControls = -1;
        }

        [Serializable]
        sealed class KeyValue
        {
            public string key;
            public string value;
        }

        static Data data;

        public static event Action Changed;

        static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        static Data Current
        {
            get
            {
                if (data != null) return data;
                data = new Data();
                try
                {
                    if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), data);
                    // Хаос v5 («MAXIMUM CHAOS»): «безумие» — новый стандарт, старый выбор один раз заменяется на него.
                    if (data.chaosRev < ChaosRevision)
                    {
                        data.chaos = (int)ChaosLevel.Mayhem;
                        data.chaosRev = ChaosRevision;
                    }
                }
                catch (Exception error)
                {
                    Debug.LogWarning($"Настройки не прочитаны, используются стандартные: {error.Message}");
                }
                return data;
            }
        }

        const int ChaosRevision = 5;

        public static ChaosLevel Chaos
        {
            get => (ChaosLevel)Mathf.Clamp(Current.chaos, 0, (int)ChaosLevel.Absurd);
            set
            {
                Current.chaos = (int)value;
                Current.chaosRev = ChaosRevision;
                Save();
            }
        }

        public static float Volume
        {
            get => Mathf.Clamp01(Current.volume);
            set { Current.volume = Mathf.Clamp01(value); Save(); }
        }

        /// Громкость музыки, 0..1 (умножается на общую).
        public static float MusicVolume
        {
            get => Mathf.Clamp01(Current.musicVolume);
            set { Current.musicVolume = Mathf.Clamp01(value); Save(); }
        }

        /// Громкость звуковых эффектов (мотор, удары, события, интерфейс), 0..1 (умножается на общую).
        public static float SfxVolume
        {
            get => Mathf.Clamp01(Current.sfxVolume);
            set { Current.sfxVolume = Mathf.Clamp01(value); Save(); }
        }

        public static MusicSource MusicSource
        {
            get => (MusicSource)Mathf.Clamp(Current.musicSource, 0, 2);
            set
            {
                Current.musicSource = (int)value;
                Save();
                MusicPlayer.Instance?.RefreshPlaylist();
            }
        }

        /// Показывать ли экранные кнопки (руль, газ, тормоз, бонус, пауза). По умолчанию — на устройствах с сенсорным экраном.
        public static bool TouchControls
        {
            get => Current.touchControls < 0 ? Application.isMobilePlatform || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-rc-mobile-ui") >= 0 : Current.touchControls == 1;
            set { Current.touchControls = value ? 1 : 0; Save(); }
        }

        public static MusicRepeat MusicRepeat
        {
            get => (MusicRepeat)Mathf.Clamp(Current.musicRepeat, 0, 2);
            set { Current.musicRepeat = (int)value; Save(); }
        }

        public static bool MusicShuffle
        {
            get => Current.musicShuffle;
            set { Current.musicShuffle = value; Save(); }
        }

        /// Музыку поставили на паузу кнопкой: после перезапуска игры она не включится сама.
        public static bool MusicPaused
        {
            get => Current.musicPaused;
            set { if (Current.musicPaused == value) return; Current.musicPaused = value; Save(); }
        }

        /// Последний трек плейлиста (Id), чтобы продолжить с него после перезапуска.
        public static string LastTrack(string playlist)
        {
            var e = Current.lastTracks.Find(x => x.key == playlist);
            return e != null ? e.value : null;
        }

        public static void SetLastTrack(string playlist, string trackId)
        {
            var e = Current.lastTracks.Find(x => x.key == playlist);
            if (e == null) Current.lastTracks.Add(e = new KeyValue { key = playlist });
            if (e.value == trackId) return;
            e.value = trackId;
            Save();
        }

        /// «Ракетный дождь» над «Тель-Авивом»: 0 — выключен, 1..4 — от редкого до максимального.
        public static int RocketShow
        {
            get => Mathf.Clamp(Current.rocketShow, 0, 4);
            set { Current.rocketShow = Mathf.Clamp(value, 0, 4); Save(); }
        }

        /// Качество эффектов шоу: 0 — облегчённые (без света и частиц), 1 — полные.
        public static int EffectsQuality
        {
            get => Mathf.Clamp(Current.effectsQuality, 0, 1);
            set { Current.effectsQuality = Mathf.Clamp(value, 0, 1); Save(); }
        }

        /// Сирена воздушной тревоги на карте «Тель-Авив».
        public static bool AirSiren
        {
            get => Current.airSiren;
            set { Current.airSiren = value; Save(); }
        }

        /// Чувствительность обзора мышью (ПКМ), 0.4..2.5.
        public static float MouseSensitivity
        {
            get => Mathf.Clamp(Current.mouseSensitivity, 0.4f, 2.5f);
            set { Current.mouseSensitivity = Mathf.Clamp(value, 0.4f, 2.5f); Save(); }
        }

        /// Вид в гонке: 0 — сзади (третье лицо), 1 — из салона.
        public static int CameraMode
        {
            get => Mathf.Clamp(Current.cameraMode, 0, 1);
            set { Current.cameraMode = Mathf.Clamp(value, 0, 1); Save(); }
        }

        /// Переназначенная клавиша смены вида (путь Input System, например "<Keyboard>/v"); пусто — C.
        /// Клавиша применения бонуса (пусто — E).
        public static string ItemKey
        {
            get => Current.itemKey ?? "";
            set { Current.itemKey = value ?? ""; Save(); }
        }

        public static string CameraKey
        {
            get => Current.cameraKey ?? "";
            set { Current.cameraKey = value ?? ""; Save(); }
        }

        /// 0 — экономная графика (Mobile), 1 — высокая (PC). Пока игрок не выбирал — по устройству.
        public static int Quality
        {
            get => Current.quality < 0 ? (Application.isMobilePlatform ? 0 : 1) : Current.quality;
            set { Current.quality = value; Save(); }
        }

        public static bool ShowFps
        {
            get => Current.showFps;
            set { Current.showFps = value; Save(); }
        }

        /// Скорость набора руля с клавиатуры, 0.5..1.6.
        public static float SteerSensitivity
        {
            get => Mathf.Clamp(Current.steerSensitivity, 0.5f, 1.6f);
            set { Current.steerSensitivity = Mathf.Clamp(value, 0.5f, 1.6f); Save(); }
        }

        public static AssistLevel Assists
        {
            get => (AssistLevel)Mathf.Clamp(Current.assists, 0, 2);
            set { Current.assists = (int)value; Save(); }
        }

        /// Сложность ИИ: 0 — лёгкий, 1 — средний, 2 — сложный.
        public static int AiDifficulty
        {
            get => Mathf.Clamp(Current.aiDifficulty, 0, 2);
            set { Current.aiDifficulty = Mathf.Clamp(value, 0, 2); Save(); }
        }

        /// Ключ выбранной машины (CarCatalog).
        public static string Car
        {
            get => string.IsNullOrEmpty(Current.car) ? "Iskra" : Current.car;
            set { Current.car = value; Save(); }
        }

        /// Ключ выбранной карты.
        public static string Map
        {
            get => string.IsNullOrEmpty(Current.map) ? "Hills" : Current.map;
            set { Current.map = value; Save(); }
        }

        public static string PlayerName
        {
            get => string.IsNullOrWhiteSpace(Current.playerName) ? "Игрок" : Current.playerName;
            set { Current.playerName = string.IsNullOrWhiteSpace(value) ? "Игрок" : value.Trim(); Save(); }
        }

        public static string LastAddress
        {
            get => Current.lastAddress ?? "";
            set { Current.lastAddress = value ?? ""; Save(); }
        }

        public static int Port
        {
            get => Current.port is > 1024 and < 65536 ? Current.port : 7777;
            set { Current.port = value; Save(); }
        }

        /// Вступительная заставка уже показана хотя бы раз: дальше её можно пропускать сразу.
        public static bool IntroSeen
        {
            get => Current.introSeen;
            set { if (Current.introSeen == value) return; Current.introSeen = value; Save(); }
        }

        static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true));
            }
            catch (Exception error)
            {
                Debug.LogWarning($"Настройки не сохранены: {error.Message}");
            }
            Changed?.Invoke();
        }
    }
}
