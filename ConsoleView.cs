using System;

namespace kchess
{
    public enum UserActionType
    {
        None,
        Exit,
        Move,
        Promotion
    }

    public class UserAction
    {
        public UserActionType Type { get; set; }
        
        // Координаты в формате движка (0-7)
        public int FromX { get; set; }
        public int FromY { get; set; }
        public int ToX { get; set; }
        public int ToY { get; set; }

        // Для превращения
        public PieceType? PromoteTo { get; set; }
    }

    public class ConsoleView
    {
        private readonly MainViewModel _viewModel;

        // Таблица перевода букв в индексы X
        private static readonly string Files = "abcdefgh";
        
        public ConsoleView(MainViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public void Render()
        {
            Console.Clear();
            Console.WriteLine("=== kchess (Standard Notation) ===");
            Console.WriteLine(_viewModel.CurrentTurnText);
            Console.WriteLine($"Статус: {_viewModel.StatusMessage}");
            
            if (_viewModel.IsWaitingForPromotion)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("*** ТРЕБУЕТСЯ ВЫБОР ФИГУРЫ (Q/R/B/N) ***");
                Console.ResetColor();
            }
            Console.WriteLine();

            var board = _viewModel.Board;
            
            // 1. Рисуем верхнюю линейку (для красоты, опционально)
            Console.Write("   ");
            for (int x = 0; x < 8; x++) Console.Write($" {Files[x]}  ");
            Console.WriteLine();

            // 2. Рисуем доску
            // В массиве Y=0 это верх (черные), Y=7 это низ (белые).
            // В шахматах ряд 8 - это верх, ряд 1 - низ.
            // Значит цикл идет от y=0 (ряд 8) до y=7 (ряд 1).
            for (int y = 0; y < 8; y++)
            {
                int rankNumber = 8 - y; // Преобразуем индекс массива в номер ряда (8..1)
                
                // Рисуем номер ряда слева
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write($" {rankNumber} ");
                Console.ResetColor();
                Console.Write("|");

                for (int x = 0; x < 8; x++)
                {
                    var piece = board[y, x];
                    
                    bool isPromotionCell = _viewModel.IsWaitingForPromotion && 
                                           _viewModel.PromotionPosition.HasValue &&
                                           _viewModel.PromotionPosition.Value.X == x && 
                                           _viewModel.PromotionPosition.Value.Y == y;

                    // Цвет клетки
                    if (isPromotionCell)
                        Console.BackgroundColor = ConsoleColor.DarkYellow;
                    else if ((x + y) % 2 == 0)
                        Console.BackgroundColor = ConsoleColor.DarkGray;
                    else
                        Console.BackgroundColor = ConsoleColor.Black;

                    // Содержимое клетки
                    Console.ForegroundColor = ConsoleColor.White;
                    if (piece == null)
                    {
                        Console.Write(" .  "); // 3 символа + пробел разделитель
                    }
                    else
                    {
                        char symbol = piece.Type switch
                        {
                            PieceType.King => 'K',
                            PieceType.Queen => 'Q',
                            PieceType.Rook => 'R',
                            PieceType.Bishop => 'B',
                            PieceType.Knight => 'N',
                            PieceType.Pawn => 'P',
                            _ => '?'
                        };

                        Console.ForegroundColor = (piece.Color == PieceColor.White) 
                            ? ConsoleColor.White 
                            : ConsoleColor.Red;
                        
                        // Форматируем: " P " или " N "
                        Console.Write($" {symbol}  ");
                    }
                    
                    Console.ResetColor();
                }
                Console.WriteLine("|");
            }

            // 3. Рисуем нижнюю линейку с буквами (a-h)
            Console.Write("   ");
            for (int x = 0; x < 8; x++) Console.Write($" {Files[x]}  ");
            Console.WriteLine();
            
            Console.WriteLine("\nПример хода: e2 e4");
        }

        /// <summary>
        /// Парсит ввод в формате "e2 e4" или "q"
        /// </summary>
        public UserAction GetInput()
        {
            if (_viewModel.IsWaitingForPromotion)
            {
                Console.Write("Выберите фигуру (Q/R/B/N): ");
                var key = Console.ReadKey(intercept: true);
                Console.WriteLine(key.KeyChar);

                PieceType? selected = key.KeyChar switch
                {
                    'q' or 'Q' => PieceType.Queen,
                    'r' or 'R' => PieceType.Rook,
                    'b' or 'B' => PieceType.Bishop,
                    'n' or 'N' => PieceType.Knight,
                    _ => null
                };

                if (selected.HasValue)
                {
                    return new UserAction { Type = UserActionType.Promotion, PromoteTo = selected.Value };
                }
                else
                {
                    Console.WriteLine("Неверный выбор.");
                    System.Threading.Thread.Sleep(1000);
                    return new UserAction { Type = UserActionType.None };
                }
            }

            Console.Write("Ход (например, e2 e4) или 'q': ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input) || input.ToLower() == "q")
                return new UserAction { Type = UserActionType.Exit };

            // Разбиваем строку на две части: "e2" и "e4"
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                Console.WriteLine("Ошибка: введите две клетки через пробел (напр: e2 e4).");
                System.Threading.Thread.Sleep(1500);
                return new UserAction { Type = UserActionType.None };
            }

            if (TryParseCoordinate(parts[0], out int fromX, out int fromY) &&
                TryParseCoordinate(parts[1], out int toX, out int toY))
            {
                return new UserAction 
                { 
                    Type = UserActionType.Move, 
                    FromX = fromX, FromY = fromY, ToX = toX, ToY = toY 
                };
            }

            Console.WriteLine("Ошибка: неверный формат клеток. Используйте a-h и 1-8.");
            System.Threading.Thread.Sleep(1500);
            return new UserAction { Type = UserActionType.None };
        }

        /// <summary>
        /// Преобразует "e2" в координаты X=4, Y=6.
        /// </summary>
        private bool TryParseCoordinate(string coord, out int x, out int y)
        {
            x = -1; 
            y = -1;

            if (string.IsNullOrEmpty(coord) || coord.Length < 2) return false;

            char fileChar = char.ToLower(coord[0]); // 'a'..'h'
            char rankChar = coord[1];               // '1'..'8'

            // Проверка буквы
            int fileIndex = Files.IndexOf(fileChar);
            if (fileIndex == -1) return false;
            x = fileIndex;

            // Проверка цифры
            if (!char.IsDigit(rankChar)) return false;
            int rank = rankChar - '0'; // Превращаем символ '2' в число 2
            if (rank < 1 || rank > 8) return false;

            // Преобразуем ранг (1..8) в индекс массива (7..0)
            // Ранг 1 -> индекс 7 (низ доски)
            // Ранг 8 -> индекс 0 (верх доски)
            y = 8 - rank;

            return true;
        }
    }
}