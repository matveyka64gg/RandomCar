using UnityEditor;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Настройки импорта моделей из Blender (Assets/Art/Models): без камер, света и анимации,
    /// а материалы заменяются общими из Assets/Art/Materials с тем же именем.
    public sealed class ModelImportRules : AssetPostprocessor
    {
        const string ModelsRoot = "Assets/Art/Models/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelsRoot)) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.addCollider = false;
        }

        /// Подменяет встроенные материалы модели общими из библиотеки через карту импортёра (AddRemap).
        /// Делается из главного процесса редактора: воркеры импорта не видят только что созданные материалы.
        public static int RemapMaterials(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            int remapped = 0;
            foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (embedded is not Material material) continue;
                string name = material.name;
                int dot = name.IndexOf('.');
                string libraryName = dot > 0 ? name.Substring(0, dot) : name;   // M_Glass.001 -> M_Glass
                var library = ArtLibrary.Get(libraryName);
                if (library == null)
                {
                    Debug.LogWarning($"[RandomCar] {path}: нет материала {libraryName} в библиотеке");
                    continue;
                }
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), library);
                remapped++;
            }
            if (remapped > 0) importer.SaveAndReimport();
            return remapped;
        }
    }
}
