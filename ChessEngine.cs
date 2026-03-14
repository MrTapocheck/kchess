using System;
using System.Collections.Generic;
using System.Linq;

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

        // Взятие на проходе: координаты клетки, которую можно "съесть"
        private Position? _enPassantTarget = null; 

        // Флаги рокировки: true = еще не двигались (можно рокироваться)
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
            
            ResetCastlingFlags();
            _enPassantTarget = null;
        }

        private void ResetCastlingFlags()
        {
            _whiteKingMoved = _blackKingMoved = false;
            _whiteRookKingsideMoved = _whiteRookQueensideMoved = false;
            _blackRookKingsideMoved = _blackRookQueensideMoved = false;
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

        private bool IsSquareAttacked(int x, int y, PieceColor attackerColor)
        {
            for (int bx = 0; bx < 8; bx++)
            {
                for (int by = 0; by < 8; by++)
                {
                    var piece = Board[by, bx];
                    if (piece != null && piece.Color == attackerColor)
                    {
                        // Для пешек атака только по диагонали
                        if (piece.Type == PieceType.Pawn)
                        {
                            int direction = (piece.Color == PieceColor.White) ? -1 : 1;
                            if (by + direction == y && Math.Abs(bx - x) == 1)
                                return true;
                            continue;
                        }

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

        public bool IsKingInCheck(PieceColor color)
        {
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

            if (kx == -1) return false; 

            PieceColor enemyColor = (color == PieceColor.White) ? PieceColor.Black : PieceColor.White;
            return IsSquareAttacked(kx, ky, enemyColor);
        }

        public bool HasLegalMoves(PieceColor color)
        {
            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    var piece = Board[y, x];
                    if (piece != null && piece.Color == color)
                    {
                        var pseudoMoves = piece.GetLegalMoves(Board, new Position(x, y));
                        
                        foreach (var move in pseudoMoves)
                        {
                            var savedTarget = Board[move.Y, move.X];
                            var savedSource = Board[y, x];
                            var capturedEpPawn = false;
                            Position? epPawnPos = null;

                            // Обработка EP в симуляции
                            if (piece.Type == PieceType.Pawn && move.X != x && savedTarget == null)
                            {
                                int dir = (piece.Color == PieceColor.White) ? 1 : -1;
                                if (IsValidCoordinate(move.X, move.Y + dir))
                                {
                                    var ep = Board[move.Y + dir, move.X];
                                    if (ep != null && ep.Type == PieceType.Pawn && ep.Color != piece.Color)
                                    {
                                        capturedEpPawn = true;
                                        epPawnPos = new Position(move.X, move.Y + dir);
                                        
                                        // Безопасное удаление пешки
                                        if (epPawnPos.HasValue)
                                        {
                                            Board[epPawnPos.Value.Y, epPawnPos.Value.X] = null;
                                        }
                                    }
                                }
                            }

                            Board[move.Y, move.X] = savedSource;
                            Board[y, x] = null;

                            bool inCheck = IsKingInCheck(color);

                            // Откат
                            Board[y, x] = savedSource;
                            Board[move.Y, move.X] = savedTarget;
                            
                            // Безопасное восстановление пешки
                            if (capturedEpPawn && epPawnPos.HasValue)
                            {
                                Board[epPawnPos.Value.Y, epPawnPos.Value.X] = new Pawn(piece.Color == PieceColor.White ? PieceColor.Black : PieceColor.White);
                            }

                            if (!inCheck)
                                return true;
                        }
                    }
                }
            }
            return false;
        }

        public bool TryMove(int fromX, int fromY, int toX, int toY)
        {
            if (IsGameOver) { LastStatus = "Игра окончена"; return false; }
            if (_pendingPromotionPos.HasValue) { LastStatus = "Ожидается выбор фигуры"; return false; }
            if (!IsValidCoordinate(fromX, fromY) || !IsValidCoordinate(toX, toY)) { LastStatus = "Координаты вне доски"; return false; }

            var piece = Board[fromY, fromX];
            if (piece == null || piece.Color != CurrentTurn)
            {
                LastStatus = piece == null ? "Здесь нет фигуры" : $"Сейчас ход {(CurrentTurn == PieceColor.White ? "белых" : "черных")}";
                return false;
            }

            var pseudoMoves = piece.GetLegalMoves(Board, new Position(fromX, fromY));
            var targetPos = new Position(toX, toY);

            bool isCastlingAttempt = false;
            if (piece.Type == PieceType.King && Math.Abs(toX - fromX) == 2 && toY == fromY)
            {
                isCastlingAttempt = true;
            }
            else if (!pseudoMoves.Contains(targetPos))
            {
                // Проверка на взятие на проходе (геометрически это ход по диагонали на пустую клетку)
                bool isEpAttempt = (piece.Type == PieceType.Pawn && toX != fromX && Board[toY, toX] == null);
                
                if (!isEpAttempt)
                {
                    LastStatus = "Недопустимый ход для этой фигуры";
                    return false;
                }
            }

            // --- ПРОВЕРКА НА ВЗЯТИЕ НА ПРОХОДЕ ---
            bool isEnPassantCapture = false;
            Position? epCapturedPos = null;
            
            // Проверяем: это пешка, ход по диагонали, клетка назначения пустая?
            if (piece.Type == PieceType.Pawn && toX != fromX && Board[toY, toX] == null)
            {
                // Есть ли активная цель для взятия на проходе?
                if (_enPassantTarget.HasValue)
                {
                    // Совпадают ли координаты цели с клеткой назначения?
                    if (_enPassantTarget.Value.X == toX && _enPassantTarget.Value.Y == toY)
                    {
                        isEnPassantCapture = true;
                        int capturedPawnY = fromY; // Пешка противника стоит на той же горизонтали, откуда мы пошли
                        epCapturedPos = new Position(toX, capturedPawnY);
                        
                        // Дополнительная проверка: действительно ли там вражеская пешка?
                        if (Board[capturedPawnY, toX] == null || 
                            Board[capturedPawnY, toX].Type != PieceType.Pawn || 
                            Board[capturedPawnY, toX].Color == piece.Color)
                        {
                            LastStatus = "Ошибка логики взятия на проходе";
                            return false;
                        }
                    }
                    else
                    {
                        // Цель есть, но мы пошли не туда
                        LastStatus = "Недопустимый ход (взятие на проходе невозможно здесь)";
                        return false;
                    }
                }
                else
                {
                    // Цели нет вообще
                    LastStatus = "Недопустимый ход (нет фигуры для взятия на проходе)";
                    return false;
                }
            }

            // --- ПРОВЕРКА НА РОКИРОВКУ ---
            if (isCastlingAttempt)
            {
                bool kingside = toX > fromX;
                if (CurrentTurn == PieceColor.White)
                {
                    if (_whiteKingMoved) { LastStatus = "Король уже ходил"; return false; }
                    if (kingside && _whiteRookKingsideMoved) { LastStatus = "Ладья уже ходила"; return false; }
                    if (!kingside && _whiteRookQueensideMoved) { LastStatus = "Ладья уже ходила"; return false; }
                }
                else
                {
                    if (_blackKingMoved) { LastStatus = "Король уже ходил"; return false; }
                    if (kingside && _blackRookKingsideMoved) { LastStatus = "Ладья уже ходила"; return false; }
                    if (!kingside && _blackRookQueensideMoved) { LastStatus = "Ладья уже ходила"; return false; }
                }

                if (IsKingInCheck(CurrentTurn)) { LastStatus = "Нельзя рокироваться под шахом"; return false; }

                int step = kingside ? 1 : -1;
                int rookFromX = kingside ? 7 : 0;
                int rookToX = kingside ? 5 : 3;
                
                // Проверка путей
                for (int x = fromX + step; x != (kingside ? toX + 1 : -1); x += step)
                {
                    if (Board[fromY, x] != null) { LastStatus = "Путь заблокирован"; return false; }
                    if (x != toX && IsSquareAttacked(x, fromY, CurrentTurn == PieceColor.White ? PieceColor.Black : PieceColor.White))
                    {
                        LastStatus = "Поле под ударом"; return false;
                    }
                }
                
                // Проверка ладьи
                if (Board[fromY, rookFromX] == null || Board[fromY, rookFromX].Type != PieceType.Rook)
                {
                    LastStatus = "Ладья отсутствует"; return false;
                }
            }

            // --- СИМУЛЯЦИЯ ХОДА ДЛЯ ПРОВЕРКИ НА ШАХ (для обычных ходов) ---
            // Для рокировки мы уже проверили шах на путях, но нужно проверить финальную позицию короля? 
            // Да, правило: король не может оказаться под шахом после хода.
            
            // Сохраняем состояние
            var capturedPiece = Board[toY, toX];
            var movingPiece = Board[fromY, fromX];
            
            // Делаем виртуальный ход
            Board[toY, toX] = movingPiece;
            Board[fromY, fromX] = null;
            
            Piece? epRemovedPiece = null;
            if (isEnPassantCapture && epCapturedPos.HasValue)
            {
                epRemovedPiece = Board[epCapturedPos.Value.Y, epCapturedPos.Value.X];
                Board[epCapturedPos.Value.Y, epCapturedPos.Value.X] = null;
            }

            bool inCheck = IsKingInCheck(CurrentTurn);

            // ОТКАТ
            Board[fromY, fromX] = movingPiece;
            Board[toY, toX] = capturedPiece;
            if (isEnPassantCapture && epCapturedPos.HasValue)
            {
                Board[epCapturedPos.Value.Y, epCapturedPos.Value.X] = epRemovedPiece;
            }

            if (inCheck)
            {
                LastStatus = "Нельзя ходить под шах!";
                return false;
            }

            // --- ВЫПОЛНЕНИЕ РЕАЛЬНОГО ХОДА ---
            
            // Сброс En Passant цели перед новым ходом
            _enPassantTarget = null;

            // Перемещение
            Board[toY, toX] = movingPiece;
            Board[fromY, fromX] = null;

            // Реализация взятия на проходе
            if (isEnPassantCapture && epCapturedPos.HasValue)
            {
                Board[epCapturedPos.Value.Y, epCapturedPos.Value.X] = null;
                LastStatus = "Взятие на проходе!";
            }

            // Установка цели для En Passant (если пешка прыгнула на 2)
            if (movingPiece.Type == PieceType.Pawn && Math.Abs(toY - fromY) == 2)
            {
                int epY = (fromY + toY) / 2;
                _enPassantTarget = new Position(toX, epY);
            }

            // Реализация рокировки (движение ладьи)
            if (isCastlingAttempt)
            {
                bool kingside = toX > fromX;
                int rookFromX = kingside ? 7 : 0;
                int rookToX = kingside ? 5 : 3;
                int rookY = fromY;

                var rook = Board[rookY, rookFromX];
                Board[rookY, rookToX] = rook;
                Board[rookY, rookFromX] = null;

                // Обновление флагов
                if (CurrentTurn == PieceColor.White) _whiteKingMoved = true;
                else _blackKingMoved = true;
            }

            // Обновление флагов при движении ладей (если просто пошли ладьей, а не рокировка)
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
            
            // Обновление флага короля при обычном ходе (не рокировке)
            if (movingPiece.Type == PieceType.King && !isCastlingAttempt)
            {
                if (CurrentTurn == PieceColor.White) _whiteKingMoved = true;
                else _blackKingMoved = true;
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

            // Проверка на Мат/Пат
            bool opponentInCheck = IsKingInCheck(CurrentTurn);
            bool opponentHasMoves = HasLegalMoves(CurrentTurn);

            if (!opponentHasMoves)
            {
                IsGameOver = true;
                if (opponentInCheck)
                    LastStatus = $"МАТ! Победили {(previousTurn == PieceColor.White ? "белые" : "черные")}!";
                else
                    LastStatus = "ПАТ! Ничья.";
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
            var currentPiece = Board[y, x];
            if (currentPiece == null) throw new InvalidOperationException("Фигура не найдена.");

            var color = currentPiece.Color;
            Board[y, x] = CreatePiece(color, newType);
            
            _pendingPromotionPos = null;
            
            bool opponentInCheck = IsKingInCheck(CurrentTurn);
            bool opponentHasMoves = HasLegalMoves(CurrentTurn);
            
            if (!opponentHasMoves)
            {
                IsGameOver = true;
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