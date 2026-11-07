using System.Collections.Generic;
using System.Text;

namespace RandomCar.Audio
{
    /// Человекочитаемые название и исполнитель из имени файла «Исполнитель - Название».
    /// Убирает символы, которых нет в шрифте интерфейса (иероглифы, рамки, декоративные значки),
    /// пустые и слишком длинные приписки в скобках, лишние пробелы. Чистый C#.
    public static class MusicTitles
    {
        public const string Untitled = "Без названия";
        const int MaxBracketLength = 26;

        /// Декоративные символы, похожие на буквы, заменяются буквами; остальные лишние — пробелом или ничем.
        static readonly Dictionary<char, string> Lookalikes = new Dictionary<char, string>
        {
            ['α'] = "a", ['β'] = "b", ['ε'] = "e", ['ι'] = "i", ['κ'] = "k", ['ν'] = "v", ['ο'] = "o", ['ρ'] = "p",
            ['τ'] = "t", ['υ'] = "u", ['χ'] = "x", ['µ'] = "u", ['μ'] = "u", ['ø'] = "o", ['Ø'] = "O",
            ['┼'] = " ", ['│'] = " ", ['─'] = " ", ['_'] = " ", ['|'] = " ",
            ['–'] = "-", ['—'] = "-", ['’'] = "'", ['‘'] = "'", ['´'] = "'", ['`'] = "'",
        };

        public static (string artist, string title) FromFileName(string fileName)
        {
            string name = fileName ?? "";
            int dot = name.LastIndexOf('.');
            if (dot > 0 && name.Length - dot <= 5) name = name.Substring(0, dot);
            name = Clean(name);
            int split = name.IndexOf(" - ", System.StringComparison.Ordinal);
            string artist = split > 0 ? name.Substring(0, split) : "";
            string title = split > 0 ? name.Substring(split + 3) : name;
            artist = Tidy(artist);
            title = Tidy(title);
            if (title.Length == 0)
            {
                title = artist.Length > 0 ? artist : Untitled;
                artist = "";
            }
            return (artist, title);
        }

        /// Строка только из символов, которые есть в шрифте: латиница (с диакритикой), кириллица, цифры, пунктуация.
        public static string Clean(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (Lookalikes.TryGetValue(c, out var replacement)) sb.Append(replacement);
                else if (Renderable(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        public static bool Renderable(char c) =>
            (c >= ' ' && c <= '~') ||
            (c >= 'À' && c <= 'ÿ' && c != '×' && c != '÷') ||
            (c >= 'Ѐ' && c <= 'ӿ') ||
            c == '«' || c == '»' || c == '…' || c == '№';

        /// Скобки: пустые (после удаления символов) и слишком длинные приписки убираются; непарные кавычки — тоже.
        static string Tidy(string text)
        {
            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '(' || c == '[')
                {
                    char close = c == '(' ? ')' : ']';
                    int end = text.IndexOf(close, i + 1);
                    if (end > i)
                    {
                        string inner = text.Substring(i + 1, end - i - 1).Trim();
                        if (inner.Length > 0 && inner.Length <= MaxBracketLength) sb.Append(c).Append(inner).Append(close);
                        i = end + 1;
                        continue;
                    }
                }
                sb.Append(c);
                i++;
            }
            string s = sb.ToString();
            if (s.IndexOf('«') >= 0 != s.IndexOf('»') >= 0) s = s.Replace("«", "").Replace("»", "");
            // Пробелы: схлопнуть, убрать вокруг краёв и висящие разделители.
            var parts = s.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            s = string.Join(" ", parts);
            s = s.Replace(" - -", " -").Replace("- -", "-");
            s = s.Trim(' ', '-', ',', '.', ';', ':');
            while (s.Contains(" - - ")) s = s.Replace(" - - ", " - ");
            return s;
        }
    }
}
