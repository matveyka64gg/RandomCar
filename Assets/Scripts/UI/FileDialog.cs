using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RandomCar.UI
{
    /// Системное окно выбора файлов Windows (comdlg32). В редакторе — окно редактора. На Android — недоступно
    /// (там системный выбор файлов — UserContent.AndroidFilePicker).
    /// Окно модальное: на время выбора игра стоит (гараж, меню; гонка не идёт). Файлы никуда не отправляются.
    public static class FileDialog
    {
        public static bool Supported =>
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
            true;
#else
            false;
#endif

        /// Путь к выбранной картинке или null (отмена).
        public static string OpenImage(string title)
        {
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanelWithFilters(title, "", new[] { "Картинки", "png,jpg,jpeg", "Все файлы", "*" });
            return string.IsNullOrEmpty(path) ? null : path;
#elif UNITY_STANDALONE_WIN
            var files = Open(title, "Картинки (PNG, JPEG)\0*.png;*.jpg;*.jpeg\0Все файлы\0*.*\0\0", false);
            return files.Count > 0 ? files[0] : null;
#else
            return null;
#endif
        }

        /// Выбрать одну или несколько композиций (OGG, MP3, WAV). Пустой список — отмена.
        public static List<string> OpenAudio(string title)
        {
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanelWithFilters(title, "", new[] { "Музыка", "ogg,mp3,wav", "Все файлы", "*" });
            return string.IsNullOrEmpty(path) ? new List<string>() : new List<string> { path };
#elif UNITY_STANDALONE_WIN
            return Open(title, "Музыка (OGG, MP3, WAV)\0*.ogg;*.mp3;*.wav\0Все файлы\0*.*\0\0", true);
#else
            return new List<string>();
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        const int OFN_ALLOWMULTISELECT = 0x00000200, OFN_EXPLORER = 0x00080000, OFN_FILEMUSTEXIST = 0x00001000,
                  OFN_PATHMUSTEXIST = 0x00000800, OFN_NOCHANGEDIR = 0x00000008;
        const int BufferChars = 32768;

        /// Окно открытия файла. Результат при нескольких файлах: «папка\0имя1\0имя2\0\0», при одном — полный путь.
        static List<string> Open(string title, string filter, bool multiple)
        {
            var result = new List<string>();
            IntPtr buffer = Marshal.AllocHGlobal(BufferChars * 2);
            try
            {
                for (int i = 0; i < BufferChars * 2; i++) Marshal.WriteByte(buffer, i, 0);
                var ofn = new OpenFileName();
                ofn.structSize = Marshal.SizeOf(ofn);
                ofn.filter = filter;
                ofn.file = buffer;
                ofn.maxFile = BufferChars;
                ofn.title = title;
                ofn.flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | (multiple ? OFN_ALLOWMULTISELECT : 0);
                ofn.dlgOwner = GetActiveWindow();
                if (!GetOpenFileName(ofn)) return result;
                string raw = Marshal.PtrToStringUni(buffer, BufferChars);
                var parts = new List<string>();
                foreach (var part in raw.Split('\0'))
                {
                    if (part.Length == 0) break;
                    parts.Add(part);
                }
                if (parts.Count == 1) result.Add(parts[0]);
                else for (int i = 1; i < parts.Count; i++) result.Add(System.IO.Path.Combine(parts[0], parts[i]));
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        sealed class OpenFileName
        {
            public int structSize;
            public IntPtr dlgOwner = IntPtr.Zero;
            public IntPtr instance = IntPtr.Zero;
            public string filter;
            public string customFilter;
            public int maxCustFilter;
            public int filterIndex;
            public IntPtr file;
            public int maxFile;
            public string fileTitle;
            public int maxFileTitle;
            public string initialDir;
            public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public string defExt;
            public IntPtr custData = IntPtr.Zero;
            public IntPtr hook = IntPtr.Zero;
            public string templateName;
            public IntPtr reservedPtr = IntPtr.Zero;
            public int reservedInt;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();
#endif
    }
}
