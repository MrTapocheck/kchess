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

    public class ChessEngine
    {
        public Piece?[,] Board { get; private set; }
        public PieceColor CurrentTurn { get; private set; } = PieceColor.White;
        public bool IsGameOver { get; private set; } = false;
        public string LastStatus { get; private set; } = "Игра началась";

        // История ходов в формате SAN (например, "e2-e4", "Ng1-f3")
        public List<string> MoveHistory { get; private set; } = new List<string>();
        
        // Счетчик полуходов для правила 50 ходов (сбрасывается при ходе пешкой или взятии)
        private int _halfMoveClock = 0;
        
        // Список хешей позиций для правила троекратного повторения
        private readonly Dictionary<string, int> _positionHistory = new Dictionary<string, int>();

        // Взятие на проходе
        private Position? _enPassantTarget = null; 

        // Флаги рокировки
        public bool _whiteKingMoved = false;
        public bool _blackKingMoved = false;
        public bool _whiteRookKingsideMoved = false;
        public bool _whiteRookQueensideMoved = false;
        public bool _blackRookKingsideMoved = false;
        public bool _blackRookQueensideMoved = false;

        private Position? _pendingPromotionPos = null;

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
            
            //потом добавить генерацию рокировок
            return moves;
        }        

        public void InitializeBoard()
        {
            // 1. ПОЛНАЯ ОЧИСТКА ДОСКИ (Заполняем null каждую клетку)
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Board[y, x] = null;
                }
            }

            // 2. Сброс всех флагов и истории
            MoveHistory.Clear();
            _halfMoveClock = 0;
            _positionHistory.Clear();
            IsGameOver = false;
            LastStatus = "Игра началась";
            CurrentTurn = PieceColor.White;
            _pendingPromotionPos = null;
            _enPassantTarget = null;
            ResetCastlingFlags();

            // 3. Расстановка пешек
            for (int i = 0; i < 8; i++)
            {
                Board[1, i] = new Pawn(PieceColor.Black);
                Board[6, i] = new Pawn(PieceColor.White);
            }

            // 4. Расстановка фигур
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
            
            // 5. Запись начальной позиции
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
                _ => throw new ArgumentException($"Неизвестный тип фигуры: {type}")
            };
        }

        /// <summary>
        /// Генерирует уникальный хеш текущей позиции (фигуры + рокировка + EP).
        /// Используется для правила троекратного повторения.
        /// </summary>
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

        /// <summary>
        /// Проверяет недостаточность материала для мата.
        /// Правила: K vs K, K+N vs K, K+B vs K, K+B vs K+B (слоны одного цвета).
        /// </summary>
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
                    // Нам нужно найти координаты слонов. Упростим: переберем доску еще раз.
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
                    if (Board[capturedPawnY, toX] == null || Board[capturedPawnY, toX].Type != PieceType.Pawn || Board[capturedPawnY, toX].Color == piece.Color)
                    {
                        LastStatus = "Ошибка логики взятия на проходе"; return false;
                    }
                }
                else { LastStatus = "Недопустимый ход (взятие на проходе невозможно)"; return false; }
            }

            // --- ПРОВЕРКА НА РОКИРОВКУ ---
            if (isCastlingAttempt)
            {
                bool kingside = toX > fromX;
                
                // Проверка флагов
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
                
                // === ИСПРАВЛЕННАЯ ПРОВЕРКА ПУТИ ===
                // Проверяем клетки МЕЖДУ королем и финальной позицией короля + клетку под ладьей (для ферзевого фланга)
                // Для O-O: проверяем f1, g1.
                // Для O-O-O: проверяем d1, c1, b1 (важно проверить b1, так как ладья прыгает через неё, но король идет только до c1).
                // Стоп! Король идет на c1. Путь короля: d1, c1? Нет, король идет e1->c1. Клетки прохода: d1.
                // Но ладья идет a1->d1. Ей нужны свободные b1, c1, d1.
                // Правило: все клетки между королем и ладьей должны быть пусты.
                
                int startCheckX = kingside ? fromX + 1 : rookFromX + 1;
                int endCheckX = kingside ? toX : fromX - 1; 
                // Пояснение:
                // Kingside: от f1 (4+1) до g1 (6). Цикл: 5, 6.
                // Queenside: от b1 (0+1) до d1 (4-1). Цикл: 1, 2, 3. (b1, c1, d1). Это верно! Ладья должна пройти их все.

                for (int x = startCheckX; x <= endCheckX; x++) // Используем явный диапазон
                {
                    // Важно: направление цикла зависит от step, но проще идти от мин к макс если мы вычислили границы верно
                    // Но так как startCheckX < endCheckX всегда при нашей логике выше, идем просто ++
                    
                    if (Board[fromY, x] != null) 
                    { 
                        LastStatus = $"Путь заблокирован фигурой на {(char)('a' + x)}{8-fromY}"; 
                        return false; 
                    }
                    
                    // Проверка на атакованность поля (король не может проходить через шах)
                    // Примечание: для ладьи проверка шаха не нужна, только для путей короля.
                    // Путь короля:
                    // Kingside: f1, g1.
                    // Queenside: d1, c1. (b1 проверять на шах не нужно, там король не ступает).
                    
                    bool isKingPath = true;
                    if (!kingside && x == rookFromX + 1) isKingPath = false; // b1 не входит в путь короля

                    if (isKingPath && IsSquareAttacked(x, fromY, CurrentTurn == PieceColor.White ? PieceColor.Black : PieceColor.White))
                    {
                        LastStatus = $"Поле {(char)('a' + x)}{8-fromY} под ударом"; 
                        return false;
                    }
                }
                
                // Финальная проверка наличия ладьи
                if (Board[fromY, rookFromX] == null || Board[fromY, rookFromX].Type != PieceType.Rook)
                {
                    LastStatus = "Ладья отсутствует"; 
                    return false;
                }
                
                // Дополнительная проверка: ладья не должна была ходить (уже было выше, но на всякий случай)
                // И ладья должна быть своего цвета (косвенно проверяется флагами, но лучше явно)
                if (Board[fromY, rookFromX].Color != CurrentTurn)
                {
                     LastStatus = "Чужая ладья?"; return false;
                }
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

            // --- ВЫПОЛНЕНИЕ ХОДА ---
            _enPassantTarget = null;
            Board[toY, toX] = movingPiece;
            Board[fromY, fromX] = null;

            bool isCaptureOrPawnMove = (capturedPiece != null || isEnPassantCapture || movingPiece.Type == PieceType.Pawn);
            
            if (isEnPassantCapture && epCapturedPos.HasValue)
            {
                Board[epCapturedPos.Value.Y, epCapturedPos.Value.X] = null;
                LastStatus = "Взятие на проходе!";
            }

            if (movingPiece.Type == PieceType.Pawn && Math.Abs(toY - fromY) == 2)
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
            
            if (movingPiece.Type == PieceType.King && !isCastlingAttempt)
            {
                if (CurrentTurn == PieceColor.White) _whiteKingMoved = true;
                else _blackKingMoved = true;
            }

                        // ... (код движения фигур и рокировки выше остается без изменений) ...

            // Обновление счетчика 50 ходов
            if (isCaptureOrPawnMove)
                _halfMoveClock = 0;
            else
                _halfMoveClock++;

            bool isPromotionNeeded = (movingPiece.Type == PieceType.Pawn) && 
                                     ((movingPiece.Color == PieceColor.White && toY == 0) || 
                                      (movingPiece.Color == PieceColor.Black && toY == 7));

            // --- ПОДГОТОВКА НОТАЦИИ ХОДА (ОДИН РАЗ) ---
            string files = "abcdefgh";
            string moveNotation = $"{files[fromX]}{8-fromY}-{files[toX]}{8-toY}";

            if (isCastlingAttempt) 
                moveNotation = (toX > fromX) ? "O-O" : "O-O-O";
            
            if (isEnPassantCapture) 
                moveNotation += " e.p.";

            // --- ОБРАБОТКА ПРЕВРАЩЕНИЯ ---
            if (isPromotionNeeded)
            {
                _pendingPromotionPos = new Position(toX, toY);
                LastStatus = "Требуется выбор фигуры для превращения пешки!";
                
                // Добавляем ход в историю БЕЗ фигуры превращения (добавим позже в CompletePromotion)
                MoveHistory.Add(moveNotation);
                
                throw new PawnPromotionRequiredException(toX, toY, movingPiece.Color);
            }

            // Если превращения нет, добавляем ход как есть
            MoveHistory.Add(moveNotation);

            // Смена хода
            PieceColor previousTurn = CurrentTurn;
            CurrentTurn = (CurrentTurn == PieceColor.White) ? PieceColor.Black : PieceColor.White;

            // ВАЖНО: Сначала записываем новую позицию в историю повторений!
            RecordPosition();

            // Теперь проверяем условия окончания игры
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

            // 4. Троекратное повторение (ПРОВЕРЯЕМ ПОСЛЕ ЗАПИСИ!)
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
            
            // Обновляем нотацию последнего хода
            char promoChar = newType switch { PieceType.Queen=>'Q', PieceType.Rook=>'R', PieceType.Bishop=>'B', PieceType.Knight=>'N', _=>' ' };
            if (MoveHistory.Count > 0)
                MoveHistory[MoveHistory.Count - 1] += "=" + promoChar;

            _pendingPromotionPos = null;
            
            // ВАЖНО: Позиция изменилась (пешка стала фигурой). Нужно перезаписать хеш!
            // Удаляем старую запись этого хеша (которая была сделана в TryMove до превращения)
            // И записываем новый хеш с новой фигурой.
            // Проще всего: уменьшить счетчик последней записанной позиции (так как она еще не полная) 
            // и записать новую правильную.
            
            // Но так как GetPositionHash зависит от доски, а доска уже обновлена:
            // 1. Получаем хеш текущей (новой) позиции.
            string newHash = GetPositionHash();
            
            // 2. Так как в TryMove мы уже сделали RecordPosition() для позиции С ПЕШКОЙ,
            // нам нужно корректно учесть позицию С ФЕРЗЕМ.
            // Самый надежный способ: просто вызвать RecordPosition() еще раз.
            // Это увеличит счетчик для новой позиции. Если она совпадет с какой-то старой (маловероятно сразу), учтется.
            // Но важно: позиция с пешкой, которую мы записали ранее, теперь неактуальна.
            // Однако, поскольку пешка на 8-й горизонтали — это нелегальная позиция (она должна превратиться),
            // тот хеш больше никогда не повторится. Так что просто добавляем новый хеш.
            RecordPosition();

            bool opponentInCheck = IsKingInCheck(CurrentTurn);
            bool opponentHasMoves = HasLegalMoves(CurrentTurn);
            bool gameOverReasonFound = false;

            if (!opponentHasMoves)
            {
                IsGameOver = true;
                var winner = (CurrentTurn == PieceColor.White) ? "черные" : "белые";
                LastStatus = opponentInCheck ? $"МАТ! Победили {winner}!" : "ПАТ! Ничья.";
                gameOverReasonFound = true;
            }

            if (!gameOverReasonFound && CheckInsufficientMaterial())
            {
                 IsGameOver = true;
                 LastStatus = "Ничья из-за недостаточности материала!";
                 gameOverReasonFound = true;
            }

            if (!gameOverReasonFound)
            {
                // Проверка повторений уже с новой фигурой
                string currentHash = GetPositionHash();
                if (_positionHistory.ContainsKey(currentHash) && _positionHistory[currentHash] >= 3)
                {
                    IsGameOver = true;
                    LastStatus = "Ничья из-за троекратного повторения позиции!";
                    gameOverReasonFound = true;
                }
            }

            if (!gameOverReasonFound)
            {
                LastStatus = opponentInCheck ? $"Шах! Ход {(CurrentTurn == PieceColor.White ? "белых" : "черных")}." : LastStatus;
            }
        }

        private bool IsValidCoordinate(int x, int y) => x >= 0 && x < 8 && y >= 0 && y < 8;
        
        public void SetStatus(string msg) => LastStatus = msg;
    }
}