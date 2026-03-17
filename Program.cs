using System;
using System.IO;
using System.Linq;
using Avalonia;
using kchess;
using kchess.Graphics; // Подключаем нашу папку с графикой

namespace kchess
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // 1. Парсим флаги
            int testIndex = Array.IndexOf(args, "--test");
            string? testFilePath = (testIndex != -1 && testIndex + 1 < args.Length) ? args[testIndex + 1] : null;

            // 2. Режим тестирования из файла (Приоритет №1)
            if (testFilePath != null)
            {
                RunTestSuite(testFilePath);
                return;
            }

            // 4. Режим GUI (По умолчанию, если нет флагов)
            // Запускаем Avalonia
            BuildAvaloniaApp(args).StartWithClassicDesktopLifetime(args);
        }


        // --- КОД ТЕСТОВ ---
        private static void RunTestSuite(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"ОШИБКА: Файл '{filePath}' не найден.");
                return;
            }

            Console.WriteLine($"=== Запуск тестов из файла: {filePath} ===");
            
            var viewModel = new MainViewModel();
            var lines = File.ReadAllLines(filePath);
            int moveNumber = 1;
            bool isWhiteTurn = true;

            foreach (var line in lines)
            {
                string cleanLine = line.Trim();
                if (string.IsNullOrEmpty(cleanLine) || cleanLine.StartsWith("#"))
                    continue;

                var parts = cleanLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                {
                    Console.WriteLine($"[FAIL] Ход #{moveNumber}: Неверный формат строки '{cleanLine}'");
                    return;
                }

                if (!TryParseTestMove(parts[0], out int fx, out int fy) || 
                    !TryParseTestMove(parts[1], out int tx, out int ty))
                {
                    Console.WriteLine($"[FAIL] Ход #{moveNumber}: Неверные координаты '{cleanLine}'");
                    return;
                }

                viewModel.TryMakeMove(fx, fy, tx, ty);

                bool hasError = viewModel.StatusMessage.Contains("Ошибка") || 
                                viewModel.StatusMessage.Contains("Недопустимый") ||
                                viewModel.StatusMessage.Contains("нет фигуры") ||
                                viewModel.StatusMessage.Contains("не твой ход");


                Console.WriteLine($"[OK] Ход #{moveNumber}: {cleanLine}");
                isWhiteTurn = !isWhiteTurn;
                moveNumber++;
            }

            Console.WriteLine("=== Все ходы выполнены успешно ===");
            Console.WriteLine($"Финальный статус: {viewModel.StatusMessage}");
            PrintBoard(viewModel);
        }

        private static bool TryParseTestMove(string coord, out int x, out int y)
        {
            x = -1; y = -1;
            if (string.IsNullOrEmpty(coord) || coord.Length < 2) return false;
            
            string files = "abcdefgh";
            char fileChar = char.ToLower(coord[0]);
            char rankChar = coord[1];

            int fileIndex = files.IndexOf(fileChar);
            if (fileIndex == -1) return false;
            x = fileIndex;

            if (!char.IsDigit(rankChar)) return false;
            int rank = rankChar - '0';
            if (rank < 1 || rank > 8) return false;

            y = 8 - rank;
            return true;
        }

        private static void PrintBoard(MainViewModel vm)
        {
            Console.WriteLine("\n--- Состояние доски ---");
            string files = "abcdefgh";
            Console.Write("   ");
            for (int i = 0; i < 8; i++) Console.Write($" {files[i]}  ");
            Console.WriteLine();
            
            for (int y = 0; y < 8; y++)
            {
                Console.Write($" {8-y} |");
                for (int x = 0; x < 8; x++)
                {
                    var p = vm.Board[y, x];
                    if (p == null) Console.Write(" .  ");
                    else
                    {
                        char s = p.Type switch { PieceType.King=>'K', PieceType.Queen=>'Q', PieceType.Rook=>'R', PieceType.Bishop=>'B', PieceType.Knight=>'N', PieceType.Pawn=>'P', _=>'?' };
                        Console.Write($" {(p.Color==PieceColor.White?char.ToUpper(s):char.ToLower(s))}  ");
                    }
                }
                Console.WriteLine("|");
            }
            Console.WriteLine("-----------------------\n");
        }

        // --- НОВАЯ ЧАСТЬ: ЗАПУСК AVALONIA ---
        public static AppBuilder BuildAvaloniaApp(string[] args)
            => AppBuilder.Configure<App>() // App теперь находится в namespace kchess.Graphics
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}