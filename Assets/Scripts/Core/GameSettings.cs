using System;
using System.IO;
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
            public int chaos = (int)ChaosLevel.Moderate;
            public float volume = 0.8f;
            public int quality = 1;
            public bool showFps;
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
                }
                catch (Exception error)
                {
                    Debug.LogWarning($"Настройки не прочитаны, используются стандартные: {error.Message}");
                }
                return data;
            }
        }

        public static ChaosLevel Chaos
        {
            get => (ChaosLevel)Mathf.Clamp(Current.chaos, 0, 2);
            set { Current.chaos = (int)value; Save(); }
        }

        public static float Volume
        {
            get => Mathf.Clamp01(Current.volume);
            set { Current.volume = Mathf.Clamp01(value); Save(); }
        }

        public static int Quality
        {
            get => Current.quality;
            set { Current.quality = value; Save(); }
        }

        public static bool ShowFps
        {
            get => Current.showFps;
            set { Current.showFps = value; Save(); }
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
