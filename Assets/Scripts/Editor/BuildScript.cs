using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Сборка Windows Standalone в build/RandomCar/RandomCar.exe.
    /// Из открытого редактора: меню RandomCar/Build Windows или unity command script eval.
    /// Из командной строки (редактор закрыт): Unity.exe -batchmode -quit -projectPath . -executeMethod RandomCar.EditorTools.BuildScript.BuildWindows
    public static class BuildScript
    {
        public const string OutputDir = "build/RandomCar";

        [MenuItem("RandomCar/Build Windows")]
        public static void BuildWindows()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string output = Path.Combine(projectRoot, OutputDir, "RandomCar.exe");

            PlayerSettings.productName = "RandomCar";
            PlayerSettings.bundleVersion = RandomCar.UI.GameUI.Version;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { WorldBuilder.ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[RandomCar] Сборка: {summary.result}, ошибок {summary.totalErrors}, предупреждений {summary.totalWarnings}, " +
                      $"размер {summary.totalSize / (1024f * 1024f):F1} МБ, время {summary.totalTime.TotalSeconds:F0} с → {output}");
            if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
