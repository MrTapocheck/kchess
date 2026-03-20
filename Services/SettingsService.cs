using System;
using System.IO;
using kchess.Models;

namespace kchess.Services
{
    public static class SettingsService
    {
        private static readonly string FilePath = "kchess.conf";

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string content = File.ReadAllText(FilePath);
                    return AppSettings.FromConfString(content);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading config: {ex.Message}");
            }

            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {   
                //в конфиге предупреждение, чтобы не тыкали
                string content = "# kchess configuration file\n" + 
                                 "# Do not edit manually unless you know what you are doing\n" +
                                 settings.ToConfString() + "\n";
                
                File.WriteAllText(FilePath, content);
                Console.WriteLine("Config saved to kchess.conf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving config: {ex.Message}");
            }
        }
    }
}