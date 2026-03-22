using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

    public enum MoveResult
    {
        Success,
        InvalidMove,
        PromotionNeeded // статус выбрать во что превратить пешку
    }

    public class ChessEngine
    {
        public Piece?[,] Board { get; private set; }
        public PieceColor CurrentTurn { get; private set; } = PieceColor.White;
        public bool IsGameOver { get; private set; } = false;
        public string LastStatus { get; private set; } = "Игра началась";

        // История ходов в формате  "e2-e4", "Ng1-f3"
        public List<string> MoveHistory { get; private set; } = new List<string>();
        
        // Счетчик полуходов для правила 50 ходов (сбрасывается при ходе пешкой или взятии)
        private int _halfMoveClock = 0;
        
        // Список хешей позиций для правила троекратного повторения
        private readonly Dictionary<string, int> _positionHistory = new Dictionary<string, int>();

        // Взятие на проходе
        public Position? _enPassantTarget = null; 

        // Флаги рокировки
        public bool _whiteKingMoved = false;
        public bool _blackKingMoved = false;
        public bool _whiteRookKingsideMoved = false;
        public bool _whiteRookQueensideMoved = false;
        public bool _blackRookKingsideMoved = false;
        public bool _blackRookQueensideMoved = false;

        public ChessEngine()
        {
            Board = new Piece?[8, 8];
            InitializeBoard();
        }

        // для отрисовки фантомчиков
        public List<(int x, int y)> GetPseudoMoves(int fromX, int fromY)
        {
            var moves = new List<(int x, int y)>();
            if (!IsValidCoordinate(fromX, fromY)) return moves;
            
            var piece = Board[fromY, fromX];
            if (piece == null) return moves;

            // Используем встроенный метод фигуры
            var positions = piece.GetLegalMoves(Board, new Position(fromX, fromY));
            foreach (var pos in positions)
            {
                moves.Add((pos.X, pos.Y));
            }
            
            return moves;
        }        

        public void InitializeBoard()
        {
            // ПОЛНАЯ ОЧИСТКА ДОСКИ (null в каждую клетку)
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Board[y, x] = null;
                }
            }

            // Сброс всех флагов и истории
            MoveHistory.Clear();
            _halfMoveClock = 0;
            _positionHistory.Clear();
            IsGameOver = false;
            LastStatus = "Игра началась";
            CurrentTurn = PieceColor.White;
            _enPassantTarget = null;
            ResetCastlingFlags();

            // Расстановка пешек
            for (int i = 0; i < 8; i++)
            {
                Board[1, i] = new Pawn(PieceColor.Black);
                Board[6, i] = new Pawn(PieceColor.White);
            }

            // Расстановка фигур
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
            
            // Запись начальной позиции
            RecordPosition();
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
                _ => throw new ArgumentException($"Неизвестный тип фигуры: {type}")//на будущие модификации(импорт экспорт, добавление новых видов фигур)
            };
        }

        // Генерирует уникальный хеш текущей позиции (фигуры + рокировка + EP)
        // Используется для правила троекратного повторения
        private string GetPositionHash()
        {
            var sb = new StringBuilder();
            // Расстановка фигур
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var p = Board[y, x];
                    if (p == null) sb.Append('.');
                    else
                    {
                        char c = p.Type switch {
                            PieceType.King => 'K', PieceType.Queen => 'Q', PieceType.Rook => 'R',
                            PieceType.Bishop => 'B', PieceType.Knight => 'N', PieceType.Pawn => 'P', _ => '?'
                        };
                        sb.Append(p.Color == PieceColor.White ? char.ToUpper(c) : char.ToLower(c));
                    }
                }
            }
            // Флаги рокировки
            sb.Append(_whiteKingMoved ? '0' : '1');
            sb.Append(_blackKingMoved ? '0' : '1');
            sb.Append(_whiteRookKingsideMoved ? '0' : '1');
            sb.Append(_whiteRookQueensideMoved ? '0' : '1');
            sb.Append(_blackRookKingsideMoved ? '0' : '1');
            sb.Append(_blackRookQueensideMoved ? '0' : '1');
            
            // Цель для взятия на проходе (координаты или '-')
            if (_enPassantTarget.HasValue)
                sb.Append($"{_enPassantTarget.Value.X}{_enPassantTarget.Value.Y}");
            else
                sb.Append('-');

            return sb.ToString();
        }

        private void RecordPosition()
        {
            string hash = GetPositionHash();
            if (_positionHistory.ContainsKey(hash))
                _positionHistory[hash]++;
            else
                _positionHistory[hash] = 1;
        }

        public bool IsSquareAttacked(int x, int y, PieceColor attackerColor)
        {
            for (int bx = 0; bx < 8; bx++)
            {
                for (int by = 0; by < 8; by++)
                {
                    var piece = Board[by, bx];
                    if (piece != null && piece.Color == attackerColor)
                    {
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
                                        if (epPawnPos.HasValue) Board[epPawnPos.Value.Y, epPawnPos.Value.X] = null;
                                    }
                                }
                            }

                            Board[move.Y, move.X] = savedSource;
                            Board[y, x] = null;

                            bool inCheck = IsKingInCheck(color);

                            Board[y, x] = savedSource;
                            Board[move.Y, move.X] = savedTarget;
                            
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

        // Проверяет недостаточность материала для мата.
        // Правила: K vs K, K+N vs K, K+B vs K, K+B vs K+B (слоны одного цвета).
        private bool CheckInsufficientMaterial()
        {
            List<Piece> whitePieces = new List<Piece>();
            List<Piece> blackPieces = new List<Piece>();

            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    var p = Board[y, x];
                    if (p != null)
                    {
                        if (p.Color == PieceColor.White) whitePieces.Add(p);
                        else blackPieces.Add(p);
                    }
                }

            // Только короли
            if (whitePieces.Count == 1 && blackPieces.Count == 1) return true;

            // Король + Конь против Короля
            if (whitePieces.Count == 1 && blackPieces.Count == 2)
            {
                if (blackPieces.Any(p => p.Type == PieceType.Knight)) return true;
            }
            if (blackPieces.Count == 1 && whitePieces.Count == 2)
            {
                if (whitePieces.Any(p => p.Type == PieceType.Knight)) return true;
            }

            // Король + Слон против Короля
            if (whitePieces.Count == 1 && blackPieces.Count == 2)
            {
                if (blackPieces.Any(p => p.Type == PieceType.Bishop)) return true;
            }
            if (blackPieces.Count == 1 && whitePieces.Count == 2)
            {
                if (whitePieces.Any(p => p.Type == PieceType.Bishop)) return true;
            }

            // Король + Слон против Короля + Слон (оба слона одного цвета полей)
            if (whitePieces.Count == 2 && blackPieces.Count == 2)
            {
                var wBishop = whitePieces.FirstOrDefault(p => p.Type == PieceType.Bishop);
                var bBishop = blackPieces.FirstOrDefault(p => p.Type == PieceType.Bishop);
                
                if (wBishop != null && bBishop != null)
                {
                    // Проверка цвета поля слона: (x+y)%2 == 0 -> светлое, 1 -> темное
                    // нужно найти координаты слонов
                    int wSq = -1, bSq = -1;
                    for(int y=0; y<8; y++)
                        for(int x=0; x<8; x++)
                        {
                            if (Board[y,x] == wBishop) wSq = (x+y)%2;
                            if (Board[y,x] == bBishop) bSq = (x+y)%2;
                        }
                    
                    if (wSq != -1 && bSq != -1 && wSq == bSq) return true;
                }
            }

            return false;
        }

        public bool TryMove(int fromX, int fromY, int toX, int toY, PieceType promotionType = PieceType.Queen)
        {
            if (IsGameOver) { LastStatus = "Игра окончена"; return false; }
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
                isCastlingAttempt = true;
            else if (!pseudoMoves.Contains(targetPos))
            {
                bool isEpAttempt = (piece.Type == PieceType.Pawn && toX != fromX && Board[toY, toX] == null);
                if (!isEpAttempt) { LastStatus = "Недопустимый ход для этой фигуры"; return false; }
            }

            bool isEnPassantCapture = false;
            Position? epCapturedPos = null;
            
            if (piece.Type == PieceType.Pawn && toX != fromX && Board[toY, toX] == null)
            {
                if (_enPassantTarget.HasValue && _enPassantTarget.Value.X == toX && _enPassantTarget.Value.Y == toY)
                {
                    isEnPassantCapture = true;
                    int capturedPawnY = fromY;
                    epCapturedPos = new Position(toX, capturedPawnY);
                    var capturedPawn = Board[capturedPawnY, toX];

                    if (capturedPawn == null || capturedPawn.Type != PieceType.Pawn || capturedPawn.Color == piece.Color)
                    {
                        LastStatus = "Ошибка логики взятия на проходе"; 
                        return false;
                    }
                }
                else { LastStatus = "Недопустимый ход (взятие на проходе невозможно)"; return false; }
            }

            // ПРОВЕРКА НА РОКИРОВКУ 
            if (isCastlingAttempt)
            {
                bool kingside = toX > fromX;
                int rookFromX = kingside ? 7 : 0;
                int rookToX = kingside ? 5 : 3;

                // 1. Проверка флагов (король и ладья не ходили)
                if (CurrentTurn == PieceColor.White)
                {
                    if (_whiteKingMoved || (kingside && _whiteRookKingsideMoved) || (!kingside && _whiteRookQueensideMoved))
                        { LastStatus = "Король или ладья уже ходили"; return false; }
                }
                else
                {
                    if (_blackKingMoved || (kingside && _blackRookKingsideMoved) || (!kingside && _blackRookQueensideMoved))
                        { LastStatus = "Король или ладья уже ходили"; return false; }
                }

                if (IsKingInCheck(CurrentTurn)) 
                    { LastStatus = "Нельзя рокироваться под шахом"; return false; }

                // 2. Проверка пути (клетки между королем и ладьей должны быть пусты)
                int startCheckX = kingside ? fromX + 1 : rookFromX + 1;
                int endCheckX = kingside ? toX : fromX - 1;

                for (int x = startCheckX; x <= endCheckX; x++)
                {
                    if (Board[fromY, x] != null)
                        { LastStatus = $"Путь заблокирован на {(char)('a' + x)}{8-fromY}"; return false; }

                    // Король не может проходить через битые поля (b1 при длинной рокировке не проверяем)
                    bool isKingPath = !( !kingside && x == rookFromX + 1 );
                    
                    if (isKingPath && IsSquareAttacked(x, fromY, CurrentTurn == PieceColor.White ? PieceColor.Black : PieceColor.White))
                        { LastStatus = $"Поле {(char)('a' + x)}{8-fromY} под ударом"; return false; }
                }

                // 3. Финальная проверка ладьи
                var rook = Board[fromY, rookFromX];
                if (rook == null || rook.Type != PieceType.Rook || rook.Color != CurrentTurn)
                    { LastStatus = "Невозможная рокировка"; return false; }
            }
            
            var capturedPiece = Board[toY, toX];
            var movingPiece = Board[fromY, fromX];
            
            Board[toY, toX] = movingPiece;
            Board[fromY, fromX] = null;
            
            Piece? epRemovedPiece = null;
            if (isEnPassantCapture && epCapturedPos.HasValue)
            {
                epRemovedPiece = Board[epCapturedPos.Value.Y, epCapturedPos.Value.X];
                Board[epCapturedPos.Value.Y, epCapturedPos.Value.X] = null;
            }

            bool inCheck = IsKingInCheck(CurrentTurn);

            Board[fromY, fromX] = movingPiece;
            Board[toY, toX] = capturedPiece;
            if (isEnPassantCapture && epCapturedPos.HasValue)
            {
                Board[epCapturedPos.Value.Y, epCapturedPos.Value.X] = epRemovedPiece;
            }

            if (inCheck) { LastStatus = "Нельзя ходить под шах!"; return false; }

            // ВЫПОЛНЕНИЕ ХОДА 
            _enPassantTarget = null;
            Board[toY, toX] = movingPiece;
            Board[fromY, fromX] = null;

            bool isCaptureOrPawnMove = (capturedPiece != null || isEnPassantCapture || (movingPiece != null && movingPiece.Type == PieceType.Pawn));

            if (isEnPassantCapture && epCapturedPos.HasValue)
            {
                Board[epCapturedPos.Value.Y, epCapturedPos.Value.X] = null;
                LastStatus = "Взятие на проходе!";
            }

            if (movingPiece != null && movingPiece.Type == PieceType.Pawn && Math.Abs(toY - fromY) == 2)
            {
                int epY = (fromY + toY) / 2;
                _enPassantTarget = new Position(toX, epY);
            }

            if (isCastlingAttempt)
            {
                bool kingside = toX > fromX;
                int rookFromX = kingside ? 7 : 0;
                int rookToX = kingside ? 5 : 3;
                int rookY = fromY;
                var rook = Board[rookY, rookFromX];
                Board[rookY, rookToX] = rook;
                Board[rookY, rookFromX] = null;
                if (CurrentTurn == PieceColor.White) _whiteKingMoved = true;
                else _blackKingMoved = true;
            }

            if (movingPiece != null && movingPiece.Type == PieceType.Rook)
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
            
            if (movingPiece != null && movingPiece.Type == PieceType.King && !isCastlingAttempt)
            {
                if (CurrentTurn == PieceColor.White) _whiteKingMoved = true;
                else _blackKingMoved = true;
            }

            // Обновление счетчика 50 ходов
            if (isCaptureOrPawnMove)
                _halfMoveClock = 0;
            else
                _halfMoveClock++;

            bool isPromotionNeeded = (movingPiece != null && movingPiece.Type == PieceType.Pawn) && 
                                    ((movingPiece.Color == PieceColor.White && toY == 0) || 
                                    (movingPiece.Color == PieceColor.Black && toY == 7));

            // подготовка нотации хода 
            string files = "abcdefgh";
            string moveNotation = $"{files[fromX]}{8-fromY}-{files[toX]}{8-toY}";

            if (isCastlingAttempt) 
                moveNotation = (toX > fromX) ? "O-O" : "O-O-O";
            
            if (isEnPassantCapture) 
                moveNotation += " e.p.";

            // обработка превращения
            if (isPromotionNeeded && movingPiece != null)
            {
                Board[toY, toX] = CreatePiece(movingPiece.Color, promotionType);
                
                // Обновляем нотацию хода
                string pieceCode = promotionType switch
                {
                    PieceType.Queen => "Q",
                    PieceType.Rook => "R",
                    PieceType.Bishop => "B",
                    PieceType.Knight => "N",
                    _ => "?"
                };
                moveNotation += "=" + pieceCode;
                
                LastStatus = $"Превращение пешки в {promotionType}!";
            }

            // Если превращения нет, добавляем ход как есть
            MoveHistory.Add(moveNotation);

            // Смена хода
            PieceColor previousTurn = CurrentTurn;
            CurrentTurn = (CurrentTurn == PieceColor.White) ? PieceColor.Black : PieceColor.White;

            // Сначала записываем новую позицию в историю повторений!
            RecordPosition();

            // проверки условия окончания игры
            bool opponentInCheck = IsKingInCheck(CurrentTurn);
            bool opponentHasMoves = HasLegalMoves(CurrentTurn);
            bool gameOverReasonFound = false;

            // 1. Мат или Пат
            if (!opponentHasMoves)
            {
                IsGameOver = true;
                if (opponentInCheck)
                    LastStatus = $"МАТ! Победили {(previousTurn == PieceColor.White ? "белые" : "черные")}!";
                else
                    LastStatus = "ПАТ! Ничья.";
                gameOverReasonFound = true;
            }

            // 2. Правило 50 ходов (100 полуходов)
            if (!gameOverReasonFound && _halfMoveClock >= 100)
            {
                IsGameOver = true;
                LastStatus = "Ничья по правилу 50 ходов!";
                gameOverReasonFound = true;
            }

            // 3. Недостаточность материала
            if (!gameOverReasonFound && CheckInsufficientMaterial())
            {
                IsGameOver = true;
                LastStatus = "Ничья из-за недостаточности материала!";
                gameOverReasonFound = true;
            }

            // 4. Троекратное повторение
            if (!gameOverReasonFound)
            {
                string currentHash = GetPositionHash();
                if (_positionHistory.ContainsKey(currentHash) && _positionHistory[currentHash] >= 3)
                {
                    IsGameOver = true;
                    LastStatus = "Ничья из-за троекратного повторения позиции!";
                    gameOverReasonFound = true;
                }
            }

            // Если игра не закончилась, выводим обычный статус
            if (!gameOverReasonFound)
            {
                if (opponentInCheck)
                    LastStatus = $"Шах! Ход {(CurrentTurn == PieceColor.White ? "белых" : "черных")}.";
                else
                    LastStatus = $"Ход выполнен. Теперь {(CurrentTurn == PieceColor.White ? "белых" : "черных")}.";
            }

            return true;
        }

        private bool IsValidCoordinate(int x, int y) => x >= 0 && x < 8 && y >= 0 && y < 8;
        
        public void SetStatus(string msg) => LastStatus = msg;
    }
}