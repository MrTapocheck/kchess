using System;
using System.IO;
using System.Linq;
using kchess;

namespace kchess
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Парсим флаги
            bool consoleMode = args.Contains("--console");
            int testIndex = Array.IndexOf(args, "--test");
            string? testFilePath = (testIndex != -1 && testIndex + 1 < args.Length) ? args[testIndex + 1] : null;

            // 1. Режим тестирования из файла
            if (testFilePath != null)
            {
                RunTestSuite(testFilePath);
                return;
            }

            // 2. Режим консоли
            if (consoleMode)
            {
                var viewModel = new MainViewModel();
                var view = new ConsoleView(viewModel);
                
                Console.WriteLine("Запуск kchess в консольном режиме...");
                // Убрали Sleep для мгновенного старта
                // System.Threading.Thread.Sleep(1000); 

                bool running = true;
                while (running)
                {
                    view.Render();
                    var action = view.GetInput();

                    switch (action.Type)
                    {
                        case UserActionType.Exit:
                            running = false;
                            break;
                        case UserActionType.None:
                            break;
                        case UserActionType.Move:
                            viewModel.TryMakeMove(action.FromX, action.FromY, action.ToX, action.ToY);
                            break;
                        case UserActionType.Promotion:
                            if (action.PromoteTo.HasValue)
                            {
                                try { viewModel.SelectPromotionPiece(action.PromoteTo.Value); }
                                catch (Exception ex) { Console.WriteLine($"Ошибка: {ex.Message}"); System.Threading.Thread.Sleep(1000); }
                            }
                            break;
                    }
                }
                Console.WriteLine("Игра окончена.");
                return;
            }

            // 3. Если ничего не указано
            Console.WriteLine("Графический интерфейс пока отключен.");
            Console.WriteLine("Использование:");
            Console.WriteLine("  dotnet run -- --console             # Игра в консоли");
            Console.WriteLine("  dotnet run -- --test party.txt      # Прогон теста из файла");
        }

        private static void RunTestSuite(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"ОШИБКА: Файл '{filePath}' не найден.");
                return;
            }

            Console.WriteLine($"=== Запуск тестов из файла: {filePath} ===");
            
            var viewModel = new MainViewModel();
            // Создаем "невидимый" View для тестов, чтобы не рисовать в консоль лишнее
            // Но нам нужен доступ к логике VM, так что View не обязателен для самого теста, 
            // только если мы хотим видеть доску при ошибке.
            
            var lines = File.ReadAllLines(filePath);
            int moveNumber = 1;
            bool isWhiteTurn = true;

            foreach (var line in lines)
            {
                string cleanLine = line.Trim();
                
                // Пропускаем пустые строки и комментарии
                if (string.IsNullOrEmpty(cleanLine) || cleanLine.StartsWith("#"))
                    continue;

                // Парсим ход (формат: "e2 e4")
                var parts = cleanLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                {
                    Console.WriteLine($"[FAIL] Ход #{moveNumber}: Неверный формат строки '{cleanLine}'");
                    return;
                }

                // Используем тот же парсер, что и в ConsoleView, но тут придется продублировать или вынести в утилиту.
                // Для простоты продублируем логику парсинга прямо здесь или создадим статический хелпер.
                // Давайте создадим простой локальный парсер для теста.
                
                if (!TryParseTestMove(parts[0], out int fx, out int fy) || 
                    !TryParseTestMove(parts[1], out int tx, out int ty))
                {
                    Console.WriteLine($"[FAIL] Ход #{moveNumber}: Неверные координаты '{cleanLine}'");
                    return;
                }

                // Пытаемся сделать ход
                // Сохраняем состояние до хода, чтобы вывести доску при ошибке (опционально)
                string statusBefore = viewModel.StatusMessage;
                
                // Проверка очередности (для информативности)
                string expectedTurn = isWhiteTurn ? "белых" : "черных";
                if ((isWhiteTurn && viewModel.CurrentTurnText.Contains("черных")) || 
                    (!isWhiteTurn && viewModel.CurrentTurnText.Contains("белых")))
                {
                     // Очередность нарушена в файле теста? Или игра уже закончена?
                     if (viewModel.StatusMessage.Contains("Мат") || viewModel.StatusMessage.Contains("Пат"))
                     {
                         Console.WriteLine($"[INFO] Игра окончена на ходе #{moveNumber}. Статус: {viewModel.StatusMessage}");
                         // Можно прервать или продолжить, если файл длиннее (например, проверка истории)
                         // Прервем с успехом, если это конец партии
                         Console.WriteLine($"[PASS] Тест завершен успешно. Мат/Пат зафиксирован.");
                         return;
                     }
                }

                viewModel.TryMakeMove(fx, fy, tx, ty);

                // Проверяем, прошел ли ход успешно
                // Мы не можем напрямую узнать вернул ли TryMove true, но можем проверить StatusMessage
                // Если статус содержит "Ошибка", "Недопустимый", "Нет фигуры" - значит фейл.
                bool hasError = viewModel.StatusMessage.Contains("Ошибка") || 
                                viewModel.StatusMessage.Contains("Недопустимый") ||
                                viewModel.StatusMessage.Contains("нет фигуры") ||
                                viewModel.StatusMessage.Contains("не твой ход");

                // Специальный случай: превращение пешки требует доп. действия. 
                // В простом тесте мы не можем выбрать фигуру интерактивно.
                // Допущение: если требуется превращение, тест падает, если мы не предусмотрели механизм авто-выбора.
                // Улучшение: если IsWaitingForPromotion, автоматически выбираем Ферзя для теста.
                if (viewModel.IsWaitingForPromotion)
                {
                    Console.WriteLine($"[INFO] Ход #{moveNumber}: Требуется превращение. Авто-выбор: Ферзь.");
                    viewModel.SelectPromotionPiece(PieceType.Queen);
                    // Проверяем, не вызвало ли это ошибку
                    if (viewModel.StatusMessage.Contains("Ошибка"))
                    {
                         Console.WriteLine($"[FAIL] Ход #{moveNumber} ({cleanLine}): Ошибка при превращении: {viewModel.StatusMessage}");
                         PrintBoard(viewModel);
                         return;
                    }
                }

                if (hasError && !viewModel.IsWaitingForPromotion) // Игнорируем ошибку, если она была решена превращением
                {
                    Console.WriteLine($"[FAIL] Ход #{moveNumber} ({cleanLine}): {viewModel.StatusMessage}");
                    PrintBoard(viewModel);
                    return;
                }

                Console.WriteLine($"[OK] Ход #{moveNumber}: {cleanLine}");
                
                // Переключаем ожидаемый цвет для следующей итерации (грубая проверка)
                // На самом деле лучше смотреть на viewModel.CurrentTurn
                isWhiteTurn = !isWhiteTurn;
                moveNumber++;
            }

            Console.WriteLine("=== Все ходы выполнены успешно ===");
            Console.WriteLine($"Финальный статус: {viewModel.StatusMessage}");
            PrintBoard(viewModel);
        }

        // Дублируем логику парсинга из ConsoleView для автономности теста
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
    }
}