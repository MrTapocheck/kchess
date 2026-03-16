using Avalonia.Media;

namespace kchess.Models
{
    public class AppSettings
    {
        // Поле для хранения цвета в формате строки (например, "#FF0000")
        public string HighlightColorHex { get; set; } = "#FFFF00";

        // === ВОТ ЭТОГО МЕТОДА НЕ ХВАТАЛО ===
        public Color GetHighlightColor()
        {
            // Парсим строку обратно в объект Color
            return Color.Parse(HighlightColorHex);
        }

        // Метод для сохранения в строку формата "ключ=значение"
        public string ToConfString()
        {
            return $"HighlightColorHex={HighlightColorHex}";
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
                }
            }
            return settings;
        }
    }
}