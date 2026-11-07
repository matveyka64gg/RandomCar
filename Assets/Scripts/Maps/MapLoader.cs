using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RandomCar.Maps
{
    /// Загрузка сцены карты аддитивно к сцене Game. Предыдущая карта выгружается; сцена карты становится активной,
    /// чтобы небо, туман, окружающий свет и отражения брались из неё.
    public sealed class MapLoader : MonoBehaviour
    {
        [SerializeField] MapCatalog catalog;

        Scene loaded;

        public MapCatalog Catalog => catalog;
        public MapDefinition Current { get; private set; }
        public string CurrentKey => Current != null ? Current.Key : null;
        public bool Loading { get; private set; }

        public void Configure(MapCatalog mapCatalog) => catalog = mapCatalog;

        /// Загрузить карту (если ещё не загружена) и вызвать done с её описанием.
        public void Load(string key, Action<MapDefinition> done)
        {
            var entry = catalog.Get(key);
            if (Current != null && Current.Key == entry.Key)
            {
                done?.Invoke(Current);
                return;
            }
            StartCoroutine(LoadRoutine(entry, done));
        }

        IEnumerator LoadRoutine(MapEntry entry, Action<MapDefinition> done)
        {
            Loading = true;
            if (loaded.IsValid() && loaded.isLoaded)
            {
                Current = null;
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
            var op = SceneManager.LoadSceneAsync(entry.SceneName, LoadSceneMode.Additive);
            if (op == null)
            {
                Debug.LogError($"[RandomCar] Сцена карты {entry.SceneName} не добавлена в сборку");
                Loading = false;
                yield break;
            }
            yield return op;
            loaded = SceneManager.GetSceneByName(entry.SceneName);
            SceneManager.SetActiveScene(loaded);
            foreach (var root in loaded.GetRootGameObjects())
            {
                Current = root.GetComponentInChildren<MapDefinition>();
                if (Current != null) break;
            }
            if (Current == null) Debug.LogError($"[RandomCar] В сцене {entry.SceneName} нет MapDefinition");
            Loading = false;
            done?.Invoke(Current);
        }
    }
}
