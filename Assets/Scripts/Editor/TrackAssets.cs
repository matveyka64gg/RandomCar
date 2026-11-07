using RandomCar.Track;
using UnityEditor;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Ассеты трасс: Assets/Settings/Tracks/Track_<карта>.asset.
    public static class TrackAssets
    {
        public const string Dir = "Assets/Settings/Tracks";

        public static TrackDefinition Ensure(string key)
        {
            ArtLibrary.EnsureFolder(Dir);
            string path = $"{Dir}/Track_{key}.asset";
            var def = AssetDatabase.LoadAssetAtPath<TrackDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<TrackDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }
            return def;
        }
    }
}
