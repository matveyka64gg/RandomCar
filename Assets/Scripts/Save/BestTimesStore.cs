using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RandomCar.Save
{
    /// Таблица лучших времён: топ-5, хранится в JSON в Application.persistentDataPath.
    public static class BestTimesStore
    {
        public const int Capacity = 5;

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "best_times.json");

        /// Вставляет время в отсортированный по возрастанию список (меньше — лучше).
        /// Возвращает место с нуля или -1, если время не попало в топ. Одинаковое время встаёт после старого.
        public static int Add(List<float> times, float time)
        {
            int rank = 0;
            while (rank < times.Count && times[rank] <= time) rank++;
            if (rank >= Capacity) return -1;

            times.Insert(rank, time);
            if (times.Count > Capacity) times.RemoveAt(times.Count - 1);
            return rank;
        }

        public static List<float> Load(string path)
        {
            var result = new List<float>();
            if (!File.Exists(path)) return result;

            try
            {
                var data = JsonUtility.FromJson<Data>(File.ReadAllText(path));
                if (data?.times != null)
                {
                    result.AddRange(data.times);
                    result.Sort();
                }
            }
            catch (ArgumentException error)
            {
                Debug.LogWarning($"Не удалось прочитать таблицу лучших времён, начинаем заново: {error.Message}");
                result.Clear();
            }

            if (result.Count > Capacity) result.RemoveRange(Capacity, result.Count - Capacity);
            return result;
        }

        public static void Save(string path, List<float> times)
        {
            var data = new Data { times = times };
            File.WriteAllText(path, JsonUtility.ToJson(data, true));
        }

        [Serializable]
        public sealed class Data
        {
            public List<float> times = new List<float>();
        }
    }
}
