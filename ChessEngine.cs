using System;
using System.Collections.Generic;

namespace kchess
{
    /// <summary>
    /// Исключение, сигнализирующее о необходимости выбора фигуры для превращения пешки.
    /// Содержит координаты, где произошло событие.
    /// </summary>
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

    /// <summary>
    /// Основной класс движка. Управляет состоянием доски, очередностью ходов и валидацией.
    /// Строго отделен от логики отображения.
    /// </summary>
    public class ChessEngine
    {
        public Piece?[,] Board { get; private set; }
        public PieceColor CurrentTurn { get; private set; } = PieceColor.White;
        public bool IsGameOver { get; private set; } = false;
        public string LastStatus { get; private set; } = "Игра_началась"; 

        // Временное хранение состояния для превращения пешки, если ход прерван
        private Position? _pendingPromotionPos = null;
        private Position? _pendingFromPos = null;

        public ChessEngine()
        {
            Board = new Piece?[8, 8];
            InitializeBoard();
        }

        /// <summary>
        /// Позволяет внешнему коду (например, ViewModel) установить сообщение об ошибке,
        /// если произошла ситуация, которую движок не обработал сам.
        /// </summary>
        public void SetStatus(string message)
        {
            LastStatus = message;
        }

        private void InitializeBoard()
        {
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
        /// Первая попытка хода. 
        /// Если ход требует превращения пешки, бросает PawnPromotionRequiredException.
        /// Если ход успешен, возвращает true.
        /// </summary>
        public bool TryMove(int fromX, int fromY, int toX, int toY)
        {
            // Если мы ждем завершения превращения, этот метод блокируем или игнорируем
            if (_pendingPromotionPos.HasValue)
            {
                LastStatus = "Ожидается выбор фигуры для превращения. Используйте CompletePromotion.";
                return false;
            }

            if (IsGameOver)
            {
                LastStatus = "Игра окончена";
                return false;
            }

            if (!IsValidCoordinate(fromX, fromY) || !IsValidCoordinate(toX, toY))
            {
                LastStatus = "Координаты вне доски";
                return false;
            }

            var piece = Board[fromY, fromX];
            
            if (piece == null)
            {
                LastStatus = "В этой клетке нет фигуры";
                return false;
            }
            if (piece.Color != CurrentTurn)
            {
                LastStatus = $"Сейчас ход {(CurrentTurn == PieceColor.White ? "белых" : "черных")}";
                return false;
            }

            var legalMoves = piece.GetLegalMoves(Board, new Position(fromX, fromY));
            var targetPos = new Position(toX, toY);
            
            if (!legalMoves.Contains(targetPos))
            {
                LastStatus = "Недопустимый ход для этой фигуры";
                return false;
            }

            // --- Логика выполнения хода ---
            
            // Проверка на взятие короля (для упрощенного окончания игры)
            var captured = Board[toY, toX];
            if (captured != null && captured.Type == PieceType.King)
            {
                Board[toY, toX] = piece;
                Board[fromY, fromX] = null;
                IsGameOver = true;
                LastStatus = $"Мат! Победили {(CurrentTurn == PieceColor.White ? "белые" : "черные")}";
                return true;
            }

            // Перемещение фигуры (пока временно)
            Board[toY, toX] = piece;
            Board[fromY, fromX] = null;

            // --- Проверка на превращение пешки ---
            bool isPromotionNeeded = false;
            if (piece.Type == PieceType.Pawn)
            {
                if ((piece.Color == PieceColor.White && toY == 0) || 
                    (piece.Color == PieceColor.Black && toY == 7))
                {
                    isPromotionNeeded = true;
                }
            }

            if (isPromotionNeeded)
            {
                // Откатываем визуальное перемещение пешки, так как она должна исчезнуть и стать новой фигурой
                // Но формально ход уже сделан, пешка стоит на последней горизонтали.
                // Мы просто блокируем смену хода и требуем выбора.
                _pendingPromotionPos = new Position(toX, toY);
                _pendingFromPos = new Position(fromX, fromY); // На случай если нужно будет откатить совсем
                
                LastStatus = "Требуется выбор фигуры для превращения пешки!";
                
                // ВАЖНО: Мы НЕ меняем CurrentTurn и выбрасываем исключение, чтобы UI перехватил его
                // и открыл диалог выбора. 
                // Возвращаем false, так как ход еще не завершен окончательно.
                // Но лучше всё-таки кинуть исключение, как ты и просил, чтобы нельзя было проигнорировать.
                
                // Отменяем перемещение в памяти до выбора? Нет, пешка уже там. 
                // Просто помечаем состояние.
                
                throw new PawnPromotionRequiredException(toX, toY, piece.Color);
            }

            // Если превращения не нужно, ход завершен
            FinishTurn();
            return true;
        }

        /// <summary>
        /// Завершает ход с выбранной фигурой для превращения.
        /// Вызывается только после того, как пользователь выбрал фигуру.
        /// </summary>
        public void CompletePromotion(PieceType newType)
        {
            if (!_pendingPromotionPos.HasValue)
            {
                throw new InvalidOperationException("Нет активного процесса превращения пешки.");
            }

            if (newType == PieceType.Pawn || newType == PieceType.King)
            {
                throw new ArgumentException("Нельзя превратить пешку в пешку или короля.");
            }

            int x = _pendingPromotionPos.Value.X;
            int y = _pendingPromotionPos.Value.Y;
            var color = CurrentTurn; // Цвет текущей фигуры (пешки)

            // Заменяем пешку на выбранную фигуру
            Board[y, x] = CreatePiece(color, newType);
            
            LastStatus = $"Пешка превратилась в {(newType == PieceType.Queen ? "ферзя" : newType.ToString().ToLower())}";

            // Сбрасываем состояние ожидания
            _pendingPromotionPos = null;
            _pendingFromPos = null;

            // Теперь завершаем ход
            FinishTurn();
        }

        private void FinishTurn()
        {
            if (!IsGameOver)
            {
                CurrentTurn = (CurrentTurn == PieceColor.White) ? PieceColor.Black : PieceColor.White;
                LastStatus = $"Ход выполнен. Теперь {(CurrentTurn == PieceColor.White ? "белых" : "черных")}.";
            }
        }

        private bool IsValidCoordinate(int x, int y)
        {
            return x >= 0 && x < 8 && y >= 0 && y < 8;
        }
        
        // Метод для отмены хода (если нужно будет реализовать UI отмены)
        public void CancelPromotion()
        {
             if (!_pendingPromotionPos.HasValue) return;
             
             // Тут нужна логика отката, но пока просто сбросим флаг
             // В полной версии нужно вернуть пешку назад и восстановить съеденную фигуру
             // Для этого нужно хранить состояние "до хода". 
             // Пока оставим заглушку с исключением, что отмена невозможна без истории.
             throw new NotImplementedException("Отмена превращения требует реализации истории ходов (Move History).");
        }
    }
}