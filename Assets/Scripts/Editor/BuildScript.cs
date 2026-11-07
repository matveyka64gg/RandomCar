using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Сборки бета-версии в папке Builds (корень проекта):
    ///   Builds/Windows/RandomCar.exe + RandomCar_Data + UserContent (папки для своей музыки и наклеек) + README_BETA.txt;
    ///   Builds/Android/RandomCar.apk + README_BETA.txt.
    /// Из открытого редактора: меню RandomCar/Build Windows (Beta), RandomCar/Build Android APK (Beta)
    /// или unity command script eval "RandomCar.EditorTools.BuildScript.BuildWindows(); return \"ok\";".
    /// Из командной строки (редактор закрыт): Unity.exe -batchmode -quit -projectPath . -executeMethod RandomCar.EditorTools.BuildScript.BuildWindows
    public static class BuildScript
    {
        public const string WindowsDir = "Builds/Windows";
        public const string AndroidDir = "Builds/Android";
        public const string AndroidId = "com.matveyka64gg.randomcar";
        /// Номер сборки Android (versionCode): растёт с каждой выкладкой, иначе телефон не обновит игру поверх старой.
        public const int AndroidVersionCode = 1;

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        static void CommonSettings()
        {
            PlayerSettings.productName = "RandomCar";
            PlayerSettings.bundleVersion = RandomCar.UI.GameUI.Version;
            // В сетевой игре хост и клиенты не должны замирать, когда окно без фокуса.
            PlayerSettings.runInBackground = true;
        }

        [MenuItem("RandomCar/Build Windows (Beta)")]
        public static void BuildWindows()
        {
            CommonSettings();
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            string dir = Path.Combine(ProjectRoot, WindowsDir);
            Directory.CreateDirectory(dir);
            var summary = Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, Path.Combine(dir, "RandomCar.exe"));
            if (summary.result != BuildResult.Succeeded) return;
            // Отладочные символы Burst не для раздачи игрокам.
            foreach (string service in new[] { "RandomCar_BurstDebugInformation_DoNotShip", "RandomCar_BackUpThisFolder_ButDontShipItWithYourGame" })
            {
                string folder = Path.Combine(dir, service);
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
            // Папки для своей музыки и наклеек — сразу в сборке, с подсказками.
            Directory.CreateDirectory(Path.Combine(dir, "UserContent", "Music"));
            Directory.CreateDirectory(Path.Combine(dir, "UserContent", "Decals"));
            File.WriteAllText(Path.Combine(dir, "UserContent", "Music", "README.txt"),
                "Положите сюда свою музыку (OGG, MP3, WAV) — RandomCar найдёт её при запуске и при открытии раздела «Моя музыка».\r\n" +
                "Удаление трека из библиотеки игры не удаляет файл из этой папки.\r\n");
            File.WriteAllText(Path.Combine(dir, "UserContent", "Decals", "README.txt"),
                "Положите сюда свои картинки для наклеек (PNG с прозрачностью или JPEG) — RandomCar добавит их в гараж при открытии гаража.\r\n" +
                "До 8 МБ и 4096×4096 точек, в игре картинка уменьшается до 1024 точек по большей стороне.\r\n");
            File.WriteAllText(Path.Combine(dir, "README_BETA.txt"), Readme("Windows"));
        }

        [MenuItem("RandomCar/Build Android APK (Beta)")]
        public static void BuildAndroid() => BuildAndroid(Path.Combine(ProjectRoot, AndroidDir, "RandomCar.apk"), AndroidArchitecture.ARM64, true);

        const string UrpGlobalSettings = "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset";

        static void BuildAndroid(string output, AndroidArchitecture architecture, bool release)
        {
            // URP при сборке под телефон выбрасывает из глобальных настроек ресурсы SSAO (на телефоне его нет) и сохраняет файл:
            // после этого Windows-сборка не может создать рендерер с SSAO. Файл сохраняется до сборки и возвращается после.
            string urpPath = Path.Combine(ProjectRoot, UrpGlobalSettings);
            string urpBefore = File.Exists(urpPath) ? File.ReadAllText(urpPath) : null;
            try
            {
                BuildAndroidCore(output, architecture, release);
            }
            finally
            {
                if (urpBefore != null && File.ReadAllText(urpPath) != urpBefore)
                {
                    File.WriteAllText(urpPath, urpBefore);
                    AssetDatabase.ImportAsset(UrpGlobalSettings, ImportAssetOptions.ForceUpdate);
                    Debug.Log("[RandomCar] Глобальные настройки URP возвращены после сборки Android (ресурсы SSAO для ПК).");
                }
            }
        }

        static void BuildAndroidCore(string output, AndroidArchitecture architecture, bool release)
        {
            CommonSettings();
            // Название на телефоне — с пометкой беты (на ПК название не меняется: от него зависит папка сохранений).
            PlayerSettings.productName = "RandomCar Beta";
            try
            {
                var android = NamedBuildTarget.Android;
                PlayerSettings.SetApplicationIdentifier(android, AndroidId);
                PlayerSettings.Android.bundleVersionCode = AndroidVersionCode;
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
                PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = architecture;
                // Сетевая игра по локальной сети (NGO) — нужен доступ к сети.
                PlayerSettings.Android.forceInternetPermission = true;
                PlayerSettings.Android.forceSDCardPermission = false;
                // Только альбомная ориентация: гонка и интерфейс сделаны под широкий экран.
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
                PlayerSettings.allowedAutorotateToPortrait = false;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
                PlayerSettings.allowedAutorotateToLandscapeLeft = true;
                PlayerSettings.allowedAutorotateToLandscapeRight = true;
                // Статическое объединение мешей на телефоне выключено: оно копирует геометрию карт в память (сотни МБ на «Городе»),
                // кадр держит SRP Batcher.
                PlayerSettings.SetStaticBatchingForPlatform(BuildTarget.Android, false);
                EditorUserBuildSettings.buildAppBundle = false;
                string dir = Path.GetDirectoryName(output);
                Directory.CreateDirectory(dir);
                var summary = Build(BuildTarget.Android, BuildTargetGroup.Android, output);
                // Символы IL2CPP (для разбора падений) — не для раздачи игрокам.
                string symbols = Path.Combine(dir, Path.GetFileNameWithoutExtension(output) + "_BackUpThisFolder_ButDontShipItWithYourGame");
                if (Directory.Exists(symbols)) Directory.Delete(symbols, true);
                if (release && summary.result == BuildResult.Succeeded) File.WriteAllText(Path.Combine(dir, "README_BETA.txt"), Readme("Android"));
            }
            finally
            {
                PlayerSettings.productName = "RandomCar";
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            }
        }

        static BuildSummary Build(BuildTarget target, BuildTargetGroup group, string output)
        {
            var options = new BuildPlayerOptions
            {
                scenes = Array.ConvertAll(EditorBuildSettings.scenes, s => s.path),
                locationPathName = output,
                target = target,
                targetGroup = group,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[RandomCar] Сборка {target}: {summary.result}, ошибок {summary.totalErrors}, предупреждений {summary.totalWarnings}, " +
                      $"размер {summary.totalSize / (1024f * 1024f):F1} МБ, время {summary.totalTime.TotalSeconds:F0} с → {output}");
            if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
            return summary;
        }

        static string Readme(string platform)
        {
            string common =
                $"RANDOMCAR — BETA VERSION ({RandomCar.UI.GameUI.VersionTitle}, версия {RandomCar.UI.GameUI.Version})\r\n" +
                "Это первая публичная бета: возможны ошибки, баланс и производительность ещё настраиваются.\r\n\r\n" +
                "Трассы: «Холмы Рандома», «Город», «Пустыня», «Тель-Авив». Машины, Хаос, бонусы, полигон испытаний, сетевая игра по локальной сети.\r\n\r\n";
            if (platform == "Windows")
                return common +
                       "ЗАПУСК: RandomCar.exe (папку RandomCar_Data не удалять и не отделять от exe).\r\n" +
                       "Управление: W/S/A/D или стрелки, пробел — ручник, E — бонус, C — вид, Esc — пауза. Геймпад: триггеры, левый стик, A — ручник, B — бонус, Y — вид, Start — пауза.\r\n\r\n" +
                       "СВОЯ МУЗЫКА: положите OGG/MP3/WAV в UserContent\\Music или нажмите «Моя музыка» → «Добавить музыку».\r\n" +
                       "СВОИ НАКЛЕЙКИ: положите PNG/JPEG в UserContent\\Decals или в гараже → «Добавить наклейку».\r\n" +
                       "Добавленные кнопкой файлы игра копирует к себе (%USERPROFILE%\\AppData\\LocalLow\\DefaultCompany\\RandomCar\\UserContent) — они сохранятся при обновлении игры.\r\n" +
                       "Файлы, положенные в папку UserContent рядом с игрой, при обновлении перенесите в новую папку игры.\r\n";
            return common +
                   "УСТАНОВКА: откройте RandomCar.apk на телефоне (Android 8.0 и новее, 64-битный процессор) и разрешите установку из этого источника.\r\n" +
                   "Управление — экранные кнопки: ◀ ▶ руль, ГАЗ, ТОРМОЗ, РУЧНИК, БОНУС, ВИД, II — пауза. Можно подключить геймпад.\r\n\r\n" +
                   "СВОЯ МУЗЫКА: «Моя музыка» → «Добавить музыку» → выберите треки (OGG, MP3, WAV) в системном окне.\r\n" +
                   "СВОИ НАКЛЕЙКИ: гараж → наклейки → библиотека → «Добавить наклейку» (PNG или JPEG).\r\n" +
                   "Игра не просит доступ ко всем файлам: выбранные файлы копируются в личную папку игры и остаются после перезапуска и обновления (но удаляются вместе с игрой).\r\n" +
                   "APK подписан отладочным ключом сборки (для тестовой раздачи), не для Google Play.\r\n";
        }
    }
}
