using System.Linq;
using RandomCar.Track;
using UnityEditor;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Общие помощники сборщиков сцен: экземпляры моделей, группы, меши, статичность.
    public static class BuilderKit
    {
        public const string ModelsDir = "Assets/Art/Models";
        public const string GeneratedDir = "Assets/Art/Generated";

        public static Vector3 AtDistance(TrackPath path, float distance, float lateral, out Vector3 right)
        {
            float p = TrackPath.Wrap(distance / path.Length);
            right = path.SideAt(p);
            return path.PointAt(p) + right * lateral;
        }

        /// Экземпляр модели из Assets/Art/Models (папка по префиксу имени или явная).
        public static GameObject Instance(string model, GameObject parent, Vector3 position, Quaternion rotation, string folder = null)
        {
            folder ??= model.StartsWith("Car_") ? "Cars" : "Environment";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsDir}/{folder}/{model}.fbx");
            if (asset == null)
            {
                Debug.LogError($"[RandomCar] нет модели {folder}/{model}");
                return new GameObject(model + "_MISSING");
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            if (parent != null) go.transform.SetParent(parent.transform, false);
            go.transform.SetPositionAndRotation(position, rotation);
            return go;
        }

        public static GameObject Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        public static GameObject Place(Mesh mesh, string name, Transform parent, params string[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.Select(ArtLibrary.Get).ToArray();
            return go;
        }

        /// Меш сохраняется ассетом в Generated/<папка>. Ассет создаётся заново: копирование в существующий меш
        /// оставляло на GPU старый буфер индексов (часть подсетки не рисовалась до перезагрузки).
        public static Mesh SaveMesh(Mesh mesh, string folder = "Track")
        {
            string dir = $"{GeneratedDir}/{folder}";
            ArtLibrary.EnsureFolder(dir);
            string path = $"{dir}/{mesh.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        public static void SetStatic(GameObject root)
        {
            const StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.OccludeeStatic;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }

        public static Transform Find(Transform root, string name)
        {
            if (root.name == name || root.name.StartsWith(name + ".") || root.name.StartsWith(name + "_")) return root;
            foreach (Transform child in root)
            {
                var found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
