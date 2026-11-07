using System.Collections;
using System.IO;
using UnityEngine;

namespace RandomCar.Core
{
    /// Запись прохождения для проверки: кадр Game view с интерфейсом раз в Interval секунд реального времени
    /// в папку (потом ffmpeg собирает видео). Игрой не используется — вешается на время проверки и снимается.
    /// Кадры — JPEG (быстрее PNG: запись почти не тормозит игру и видео идёт в реальном темпе).
    public sealed class DevFrameRecorder : MonoBehaviour
    {
        public string Folder;
        public float Interval = 0.2f;
        public int MaxFrames = 3000;
        /// true — PNG через ScreenCapture (как раньше), false — JPEG 90%.
        public bool Png;

        int frame;
        float next;
        bool busy;

        public int Frames => frame;

        void Update()
        {
            if (string.IsNullOrEmpty(Folder) || frame >= MaxFrames || Time.unscaledTime < next || busy) return;
            next = Time.unscaledTime + Interval;
            Directory.CreateDirectory(Folder);
            if (Png)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(Folder, $"f_{frame:D5}.png"));
                frame++;
                return;
            }
            StartCoroutine(Capture(Path.Combine(Folder, $"f_{frame:D5}.jpg")));
            frame++;
        }

        IEnumerator Capture(string path)
        {
            busy = true;
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            byte[] jpg = texture.EncodeToJPG(90);
            Destroy(texture);
            File.WriteAllBytes(path, jpg);
            busy = false;
        }
    }
}
