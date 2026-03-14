using System;
using System.Collections.Generic;

namespace kchess
{
    public class PawnPromotionRequiredException : Exception
    {
        public int X { get; }
        public int Y { get; }
        public PieceColor Color { get; }

        public PawnPromotionRequiredException(int x, int y, PieceColor color) 
            : base($"Требуется выбор фигуры для превращения пешки на позиции ({x}, {y})")
        {
            X = x;
            Y = y;
            Color = color;
        }
    }

    public class ChessEngine
    {
        public Piece?[,] Board { get; private set; }
        public PieceColor CurrentTurn { get; private set; } = PieceColor.White;
        public bool IsGameOver { get; private set; } = false;
        public string LastStatus { get; private set; } = "Игра началась";

        // Для отслеживания права на взятие на проходе
        private Position? _enPassantTarget = null; 

        // Для рокировки (флаги: двигались ли король и ладьи)
        // true = еще не двигались, false = двигались или съедены
        private bool _whiteKingMoved = false;
        private bool _blackKingMoved = false;
        private bool _whiteRookKingsideMoved = false;
        private bool _whiteRookQueensideMoved = false;
        private bool _blackRookKingsideMoved = false;
        private bool _blackRookQueensideMoved = false;

        private Position? _pendingPromotionPos = null;

        public ChessEngine()
        {
            Board = new Piece?[8, 8];
            InitializeBoard();
        }

        private void InitializeBoard()
        {
            // Пешки
            for (int i = 0; i < 8; i++)
            {
                Board[1, i] = new Pawn(PieceColor.Black);
                Board[6, i] = new Pawn(PieceColor.White);
            }

            var backRowTypes = new PieceType[] 
            { 
                PieceType.Rook, PieceType.Knight, PieceType.Bishop, PieceType.Queen, 
                PieceType.King, PieceType.Bishop, PieceType.Knight, PieceType.Rook 
            };

            for (int i = 0; i < 8; i++)
            {
                Board[0, i] = CreatePiece(PieceColor.Black, backRowTypes[i]);
                Board[7, i] = CreatePiece(PieceColor.White, backRowTypes[i]);
            }
            
            // Сброс флагов
            _whiteKingMoved = _blackKingMoved = false;
            _whiteRookKingsideMoved = _whiteRookQueensideMoved = false;
            _blackRookKingsideMoved = _blackRookQueensideMoved = false;
            _enPassantTarget = null;
        }

        private Piece CreatePiece(PieceColor color, PieceType type)
        {
            return type switch
            {
                PieceType.Pawn => new Pawn(color),
                PieceType.Knight => new Knight(color),
                PieceType.Bishop => new Bishop(color),
                PieceType.Rook => new Rook(color),
                PieceType.Queen => new Queen(color),
                PieceType.King => new King(color),
                _ => throw new ArgumentException($"Неизвестный тип фигуры: {type}")
            };
        }

        /// <summary>
        /// Проверяет, атакована ли клетка (x, y) фигурами указанного цвета.
        /// Используется для проверки шаха.
        /// </summary>
        private bool IsSquareAttacked(int x, int y, PieceColor attackerColor)
        {
            // Проходим по всей доске и ищем фигуры атакующего цвета
            for (int bx = 0; bx < 8; bx++)
            {
                for (int by = 0; by < 8; by++)
                {
                    var piece = Board[by, bx];
                    if (piece != null && piece.Color == attackerColor)
                    {
                        // Получаем ходы этой фигуры
                        // Важно: для пешек атака отличается от движения (они бьют по диагонали)
                        // Но наш GetLegalMoves для пешки уже включает взятие по диагонали.
                        // Однако GetLegalMoves может проверять шах (рекурсия)! Чтобы избежать этого,
                        // нам нужна версия получения ходов БЕЗ проверки шаха.
                        // УПРОЩЕНИЕ: Для проверки шаха мы можем использовать "псевдо-легальные" ходы.
                        // Но так как у нас логика в фигурах простая, можно вызвать GetLegalMoves,
                        // ЕСЛИ мы гарантируем, что внутри фигур нет проверки IsKingInCheck.
                        // Сейчас в фигурах её нет. Значит безопасно.
                        
                        var moves = piece.GetLegalMoves(Board, new Position(bx, by));
                        foreach (var move in moves)
                        {
                            if (move.X == x && move.Y == y)
                                return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Проверяет, находится ли король указанного цвета под шахом.
        /// </summary>
        public bool IsKingInCheck(PieceColor color)
        {
            // Находим короля
            int kx = -1, ky = -1;
            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    var p = Board[y, x];
                    if (p != null && p.Type == PieceType.King && p.Color == color)
                    {
                        kx = x; ky = y;
                        break;
                    }
                }
                if (kx != -1) break;
            }

            if (kx == -1) return false; // Короля нет (съеден в упрощенной версии?)

            PieceColor enemyColor = (color == PieceColor.White) ? PieceColor.Black : PieceColor.White;
            return IsSquareAttacked(kx, ky, enemyColor);
        }

        /// <summary>
        /// Проверяет, есть ли у игрока хоть один легальный ход.
        /// Используется для определения Мата и Пата.
        /// </summary>
        public bool HasLegalMoves(PieceColor color)
        {
            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    var piece = Board[y, x];
                    if (piece != null && piece.Color == color)
                    {
                        // Получаем псевдо-ходы
                        var pseudoMoves = piece.GetLegalMoves(Board, new Position(x, y));
                        
                        foreach (var move in pseudoMoves)
                        {
                            // Симулируем ход
                            var savedTarget = Board[move.Y, move.X];
                            var savedSource = Board[y, x];
                            
                            Board[move.Y, move.X] = savedSource;
                            Board[y, x] = null;

                            // Если после хода король не под шахом - значит ход легален
                            bool inCheck = IsKingInCheck(color);

                            // Откатываем ход
                            Board[y, x] = savedSource;
                            Board[move.Y, move.X] = savedTarget;

                            if (!inCheck)
                                return true; // Нашли хоть один легальный ход
                        }
                    }
                }
            }
            return false; // Легальных ходов нет
        }

        public bool TryMove(int fromX, int fromY, int toX, int toY)
        {
            if (IsGameOver)
            {
                LastStatus = "Игра окончена";
                return false;
            }

            if (_pendingPromotionPos.HasValue)
            {
                LastStatus = "Ожидается выбор фигуры для превращения.";
                return false;
            }

            if (!IsValidCoordinate(fromX, fromY) || !IsValidCoordinate(toX, toY))
            {
                LastStatus = "Координаты вне доски";
                return false;
            }

            var piece = Board[fromY, fromX];
            if (piece == null || piece.Color != CurrentTurn)
            {
                LastStatus = piece == null ? "Здесь нет фигуры" : $"Сейчас ход {(CurrentTurn == PieceColor.White ? "белых" : "черных")}";
                return false;
            }

            // 1. Получаем псевдо-легальные ходы (геометрия фигуры)
            var pseudoMoves = piece.GetLegalMoves(Board, new Position(fromX, fromY));
            var targetPos = new Position(toX, toY);

            if (!pseudoMoves.Contains(targetPos))
            {
                // Проверка на рокировку (если геометрически ход не подходит, но это король на 2 клетки)
                if (piece.Type == PieceType.King && Math.Abs(toX - fromX) == 2 && toY == fromY)
                {
                    // Попытка рокировки будет обработана ниже, если геометрия разрешает
                }
                else
                {
                    LastStatus = "Недопустимый ход для этой фигуры";
                    return false;
                }
            }

            // --- СИМУЛЯЦИЯ ХОДА ДЛЯ ПРОВЕРКИ НА ШАХ ---
            
            // Сохраняем состояние для отката
            var capturedPiece = Board[toY, toX];
            var movingPiece = Board[fromY, fromX];
            
            // Особая логика для взятия на проходе (симуляция)
            bool isEnPassantCapture = false;
            if (movingPiece.Type == PieceType.Pawn && toX != fromX && capturedPiece == null)
            {
                // Это взятие на проходе?
                int direction = (movingPiece.Color == PieceColor.White) ? 1 : -1; // Пешка пришла с противоположной стороны
                // Пешка стоит на toY, съеденная должна быть на toY + direction
                if (IsValidCoordinate(toX, toY + direction))
                {
                    var epPawn = Board[toY + direction, toX];
                    if (epPawn != null && epPawn.Type == PieceType.Pawn && epPawn.Color != movingPiece.Color)
                    {
                        isEnPassantCapture = true;
                        capturedPiece = epPawn; // Запоминаем, кого будем "съедать" виртуально
                    }
                }
            }

            // Делаем виртуальный ход
            Board[toY, toX] = movingPiece;
            Board[fromY, fromX] = null;
            if (isEnPassantCapture)
            {
                // Виртуально убираем съеденную пешку
                int epPawnY = toY + ((movingPiece.Color == PieceColor.White) ? 1 : -1);
                Board[epPawnY, toX] = null; 
            }

            // Проверяем: остался ли мой король под ударом?
            bool inCheck = IsKingInCheck(CurrentTurn);

            // ОТКАТ виртуального хода
            Board[fromY, fromX] = movingPiece;
            Board[toY, toX] = capturedPiece; // Возвращаем жертву или пустоту
            if (isEnPassantCapture)
            {
                int epPawnY = toY + ((movingPiece.Color == PieceColor.White) ? 1 : -1);
                Board[epPawnY, toX] = capturedPiece; // Возвращаем пешку на место
            }

            if (inCheck)
            {
                LastStatus = "Нельзя ходить под шах!";
                return false;
            }

            // --- ЕСЛИ ПРОШЛИ ПРОВЕРКУ, ДЕЛАЕМ РЕАЛЬНЫЙ ХОД ---
            
            // Сброс флага en passant перед новым ходом
            _enPassantTarget = null;

            // Реальное перемещение
            Board[toY, toX] = movingPiece;
            Board[fromY, fromX] = null;

            // Обработка взятия на проходе (реальная)
            if (isEnPassantCapture)
            {
                int epPawnY = toY + ((movingPiece.Color == PieceColor.White) ? 1 : -1);
                Board[epPawnY, toX] = null;
                LastStatus = "Взятие на проходе!";
            }

            // Установка цели для взятия на проходе (если пешка прыгнула на 2 клетки)
            if (movingPiece.Type == PieceType.Pawn && Math.Abs(toY - fromY) == 2)
            {
                int epY = (fromY + toY) / 2;
                _enPassantTarget = new Position(toX, epY);
            }

            // Обработка рокировки (перемещение ладьи)
            if (movingPiece.Type == PieceType.King && Math.Abs(toX - fromX) == 2)
            {
                bool isKingside = toX > fromX;
                int rookFromX = isKingside ? 7 : 0;
                int rookToX = isKingside ? 5 : 3;
                int rookY = fromY;

                var rook = Board[rookY, rookFromX];
                
                // Явная проверка на null перед доступом к свойствам
                if (rook != null && rook.Type == PieceType.Rook)
                {
                    Board[rookY, rookToX] = rook;
                    Board[rookY, rookFromX] = null;
                }
                
                // Помечаем, что король ходил
                if (CurrentTurn == PieceColor.White) _whiteKingMoved = true;
                else _blackKingMoved = true;
            }

            // Обновление флагов рокировки при движении ладей
            if (movingPiece.Type == PieceType.Rook)
            {
                if (CurrentTurn == PieceColor.White)
                {
                    if (fromX == 0 && fromY == 7) _whiteRookQueensideMoved = true;
                    if (fromX == 7 && fromY == 7) _whiteRookKingsideMoved = true;
                }
                else
                {
                    if (fromX == 0 && fromY == 0) _blackRookQueensideMoved = true;
                    if (fromX == 7 && fromY == 0) _blackRookKingsideMoved = true;
                }
            }

            // Превращение пешки
            bool isPromotionNeeded = false;
            if (movingPiece.Type == PieceType.Pawn)
            {
                if ((movingPiece.Color == PieceColor.White && toY == 0) || 
                    (movingPiece.Color == PieceColor.Black && toY == 7))
                {
                    isPromotionNeeded = true;
                }
            }

            if (isPromotionNeeded)
            {
                _pendingPromotionPos = new Position(toX, toY);
                LastStatus = "Требуется выбор фигуры для превращения пешки!";
                throw new PawnPromotionRequiredException(toX, toY, movingPiece.Color);
            }

            // Смена хода
            PieceColor previousTurn = CurrentTurn;
            CurrentTurn = (CurrentTurn == PieceColor.White) ? PieceColor.Black : PieceColor.White;

            // Проверка на Мат или Пат
            bool opponentInCheck = IsKingInCheck(CurrentTurn);
            bool opponentHasMoves = HasLegalMoves(CurrentTurn);

            if (!opponentHasMoves)
            {
                IsGameOver = true;
                if (opponentInCheck)
                {
                    LastStatus = $"МАТ! Победили {(previousTurn == PieceColor.White ? "белые" : "черные")}!";
                }
                else
                {
                    LastStatus = "ПАТ! Ничья.";
                }
            }
            else
            {
                if (opponentInCheck)
                    LastStatus = $"Шах! Ход {(CurrentTurn == PieceColor.White ? "белых" : "черных")}.";
                else
                    LastStatus = $"Ход выполнен. Теперь {(CurrentTurn == PieceColor.White ? "белых" : "черных")}.";
            }

            return true;
        }

        public void CompletePromotion(PieceType newType)
        {
            if (!_pendingPromotionPos.HasValue)
                throw new InvalidOperationException("Нет активного процесса превращения.");

            if (newType == PieceType.Pawn || newType == PieceType.King)
                throw new ArgumentException("Нельзя превратить в пешку или короля.");

            int x = _pendingPromotionPos.Value.X;
            int y = _pendingPromotionPos.Value.Y;
            
            // ИСПРАВЛЕНИЕ: Проверка на null
            var currentPiece = Board[y, x];
            if (currentPiece == null) 
                throw new InvalidOperationException("Фигура для превращения не найдена.");

            var color = currentPiece.Color;

            Board[y, x] = CreatePiece(color, newType);
            
            _pendingPromotionPos = null;
            
            bool opponentInCheck = IsKingInCheck(CurrentTurn);
            bool opponentHasMoves = HasLegalMoves(CurrentTurn);
            
            if (!opponentHasMoves)
            {
                IsGameOver = true;
                // Исправлена логика победителя (победил тот, чей ход БЫЛ, т.е. противоположный CurrentTurn)
                var winner = (CurrentTurn == PieceColor.White) ? "черные" : "белые";
                LastStatus = opponentInCheck ? $"МАТ! Победили {winner}!" : "ПАТ! Ничья.";
            }
            else
            {
                LastStatus = opponentInCheck ? $"Шах! Ход {(CurrentTurn == PieceColor.White ? "белых" : "черных")}." : LastStatus;
            }
        }

        private bool IsValidCoordinate(int x, int y) => x >= 0 && x < 8 && y >= 0 && y < 8;
        
        public void SetStatus(string msg) => LastStatus = msg;
    }
}