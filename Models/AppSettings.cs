using Avalonia.Media;

namespace kchess.Models
{
    public class AppSettings
    {
        // Поле для хранения цвета в формате строки (например, "#FF0000")
        public string HighlightColorHex { get; set; } = "#FFFF00";
        public string Theme { get; set; } = "Dark";
        public string BoardTheme { get; set; } = "LichessGreen";
        public string PieceSkin { get; set; } = "Classic";
        public string Language { get; set; } = "RU";
        public bool ShowHints { get; set; } = true;

        public Color GetHighlightColor()
        {
            // Парсинг строки обратно в объект Color
            return Color.Parse(HighlightColorHex);
        }

        // Метод для сохранения в строку
        public string ToConfString()
        {
            return $"HighlightColorHex={HighlightColorHex}\n" +
                   $"Theme={Theme}\n" +
                   $"BoardTheme={BoardTheme}\n" +
                   $"PieceSkin={PieceSkin}\n" +
                   $"Language={Language}\n" +
                   $"ShowHints={ShowHints}";
        }

        // Метод для загрузки из строки
        public static AppSettings FromConfString(string content)
        {
            var settings = new AppSettings();
            var lines = content.Split('\n');

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    if (key == "HighlightColorHex")
                        settings.HighlightColorHex = value;
                    else if (key == "Theme")
                        settings.Theme = value;
                    else if (key == "BoardTheme")
                        settings.BoardTheme = value;
                    else if (key == "PieceSkin")
                        settings.PieceSkin = value;
                    else if (key == "Language")
                        settings.Language = value;
                    else if (key == "ShowHints" && bool.TryParse(value, out var showHints))
                        settings.ShowHints = showHints;
                }
            }
            return settings;
        }
    }
}