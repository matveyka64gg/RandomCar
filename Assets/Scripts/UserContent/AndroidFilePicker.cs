using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace RandomCar.UserContent
{
    /// Выбранные файлы: путь к копии в личной папке игры и исходное имя (для названия трека).
    public sealed class PickedFile
    {
        public string Path;
        public string Name;
    }

    /// Системное окно выбора файлов Android (Storage Access Framework) — плагин Assets/Plugins/Android/RandomCarFilePicker.java.
    /// Игра не запрашивает разрешений на хранилище: выбранные файлы сразу копируются в личную папку (кэш), оттуда — в библиотеку.
    /// Ответ приходит в главный поток Unity. На других платформах Supported = false.
    public static class AndroidFilePicker
    {
        public static bool Supported =>
#if UNITY_ANDROID && !UNITY_EDITOR
            true;
#else
            false;
#endif

        /// Открыто ли окно сейчас (второе не открываем).
        public static bool Busy { get; private set; }

        /// Выбрать аудиофайлы (несколько).
        public static void PickAudio(Action<List<PickedFile>, string> done) => Pick("audio/*", true, 0, done);

        /// Выбрать картинку PNG или JPEG; большие уменьшаются до maxSide ещё на стороне Android (память телефона).
        public static void PickImage(int maxSide, Action<List<PickedFile>, string> done) => Pick("image/png,image/jpeg", false, maxSide, done);

        /// done(файлы, ошибка): пустой список без ошибки — игрок закрыл окно.
        static void Pick(string mime, bool multiple, int maxImageSide, Action<List<PickedFile>, string> done)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Busy)
            {
                done(new List<PickedFile>(), "Окно выбора уже открыто.");
                return;
            }
            Busy = true;
            var context = SynchronizationContext.Current;
            string dest = System.IO.Path.Combine(Application.temporaryCachePath, "picked");
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var picker = new AndroidJavaClass("com.randomcar.content.RandomCarFilePicker");
                picker.CallStatic("pick", activity, mime, multiple, dest, maxImageSide, new Callback(context, (files, error) =>
                {
                    Busy = false;
                    done(files, error);
                }));
            }
            catch (Exception error)
            {
                Busy = false;
                Debug.LogError($"[RandomCar] Окно выбора файлов Android: {error}");
                done(new List<PickedFile>(), "Окно выбора файлов не открылось.");
            }
#else
            done(new List<PickedFile>(), "Системное окно выбора файлов есть только в Android-версии.");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        sealed class Callback : AndroidJavaProxy
        {
            readonly SynchronizationContext context;
            readonly Action<List<PickedFile>, string> done;

            public Callback(SynchronizationContext unityContext, Action<List<PickedFile>, string> onDone) : base("com.randomcar.content.RandomCarFilePicker$Callback")
            {
                context = unityContext;
                done = onDone;
            }

            // Вызывается из потока Java: переносим в главный поток Unity.
            public void onPicked(string paths, string names, string error, bool cancelled)
            {
                var files = new List<PickedFile>();
                if (!cancelled && !string.IsNullOrEmpty(paths))
                {
                    string[] p = paths.Split('\n'), n = (names ?? "").Split('\n');
                    for (int i = 0; i < p.Length; i++)
                        if (p[i].Length > 0) files.Add(new PickedFile { Path = p[i], Name = i < n.Length ? n[i] : System.IO.Path.GetFileName(p[i]) });
                }
                string message = string.IsNullOrEmpty(error) ? null : error;
                if (context != null) context.Post(_ => done(files, message), null);
                else done(files, message);
            }
        }
#endif

        /// Удалить временные копии (после импорта или при запуске).
        public static void CleanTemp()
        {
            string dir = Path.Combine(Application.temporaryCachePath, "picked");
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[RandomCar] Временные файлы выбора не удалены: {error.Message}");
            }
        }
    }
}
