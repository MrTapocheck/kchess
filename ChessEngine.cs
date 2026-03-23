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
        
        // Оставляем для совместимости, но теперь основной статус возвращается из метода
        public string LastStatus { get; private set; } = "Игра началась";

        public List<string> MoveHistory { get; private set; } = new List<string>();
        
        private int _halfMoveClock = 0;
        private readonly Dictionary<string, int> _positionHistory = new Dictionary<string, int>();

        public Position? _enPassantTarget = null; 

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

        public List<(int x, int y)> GetPseudoMoves(int fromX, int fromY)
        {
            var moves = new List<(int x, int y)>();
            if (!IsValidCoordinate(fromX, fromY)) return moves;
            
            var piece = Board[fromY, fromX];
            if (piece == null) return moves;

            var positions = piece.GetLegalMoves(Board, new Position(fromX, fromY));
            foreach (var pos in positions)
            {
                moves.Add((pos.X, pos.Y));
            }
            
            return moves;
        }        

        public void InitializeBoard()
        {
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    Board[y, x] = null;

            MoveHistory.Clear();
            _halfMoveClock = 0;
            _positionHistory.Clear();
            IsGameOver = false;
            LastStatus = "Игра началась";
            CurrentTurn = PieceColor.White;
            _enPassantTarget = null;
            ResetCastlingFlags();

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

        private string GetPositionHash()
        {
            var sb = new StringBuilder();
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
            sb.Append(_whiteKingMoved ? '0' : '1');
            sb.Append(_blackKingMoved ? '0' : '1');
            sb.Append(_whiteRookKingsideMoved ? '0' : '1');
            sb.Append(_whiteRookQueensideMoved ? '0' : '1');
            sb.Append(_blackRookKingsideMoved ? '0' : '1');
            sb.Append(_blackRookQueensideMoved ? '0' : '1');
            
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
            // Геометрическая проверка атаки клетки:
            // - без генерации ходов и перебора всех фигур
            // - корректно для "атакованных" квадратов (в т.ч. для шаха/рокировки)

            // 1) Пешки
            // Пешка атакует вперед по направлению своего цвета на 1 клетку по диагонали.
            // В ваших координатах: White двигается direction=-1, Black direction=+1.
            int pawnFromY = attackerColor == PieceColor.White ? y + 1 : y - 1;
            if (IsValidCoordinate(x - 1, pawnFromY) && Board[pawnFromY, x - 1] is Piece p1 &&
                p1.Color == attackerColor && p1.Type == PieceType.Pawn)
                return true;
            if (IsValidCoordinate(x + 1, pawnFromY) && Board[pawnFromY, x + 1] is Piece p2 &&
                p2.Color == attackerColor && p2.Type == PieceType.Pawn)
                return true;

            // 2) Кони
            // Офсеты для L-образного движения.
            for (int i = 0; i < 8; i++)
            {
                int ax = x + KnightDx[i];
                int ay = y + KnightDy[i];
                if (!IsValidCoordinate(ax, ay)) continue;

                var attacker = Board[ay, ax];
                if (attacker != null && attacker.Color == attackerColor && attacker.Type == PieceType.Knight)
                    return true;
            }

            // 3) Король (соседние клетки)
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int ax = x + dx;
                    int ay = y + dy;
                    if (!IsValidCoordinate(ax, ay)) continue;

                    var attacker = Board[ay, ax];
                    if (attacker != null && attacker.Color == attackerColor && attacker.Type == PieceType.King)
                        return true;
                }
            }

            // 4) Ладьи и Ферзи (лучи по ортогонали)
            for (int i = 0; i < 4; i++)
            {
                int dx = RookDx[i];
                int dy = RookDy[i];
                int ax = x + dx;
                int ay = y + dy;
                while (IsValidCoordinate(ax, ay))
                {
                    var attacker = Board[ay, ax];
                    if (attacker != null)
                    {
                        if (attacker.Color == attackerColor &&
                            (attacker.Type == PieceType.Rook || attacker.Type == PieceType.Queen))
                            return true;
                        break; // Блокируется первой фигурой на луче
                    }
                    ax += dx;
                    ay += dy;
                }
            }

            // 5) Слоны и Ферзи (лучи по диагоналям)
            for (int i = 0; i < 4; i++)
            {
                int dx = BishopDx[i];
                int dy = BishopDy[i];
                int ax = x + dx;
                int ay = y + dy;
                while (IsValidCoordinate(ax, ay))
                {
                    var attacker = Board[ay, ax];
                    if (attacker != null)
                    {
                        if (attacker.Color == attackerColor &&
                            (attacker.Type == PieceType.Bishop || attacker.Type == PieceType.Queen))
                            return true;
                        break;
                    }
                    ax += dx;
                    ay += dy;
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

            if (whitePieces.Count == 1 && blackPieces.Count == 1) return true;

            if (whitePieces.Count == 1 && blackPieces.Count == 2)
            {
                if (blackPieces.Any(p => p.Type == PieceType.Knight)) return true;
            }
            if (blackPieces.Count == 1 && whitePieces.Count == 2)
            {
                if (whitePieces.Any(p => p.Type == PieceType.Knight)) return true;
            }

            if (whitePieces.Count == 1 && blackPieces.Count == 2)
            {
                if (blackPieces.Any(p => p.Type == PieceType.Bishop)) return true;
            }
            if (blackPieces.Count == 1 && whitePieces.Count == 2)
            {
                if (whitePieces.Any(p => p.Type == PieceType.Bishop)) return true;
            }

            if (whitePieces.Count == 2 && blackPieces.Count == 2)
            {
                var wBishop = whitePieces.FirstOrDefault(p => p.Type == PieceType.Bishop);
                var bBishop = blackPieces.FirstOrDefault(p => p.Type == PieceType.Bishop);
                
                if (wBishop != null && bBishop != null)
                {
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

        // === ГЛАВНОЕ ИЗМЕНЕНИЕ ЗДЕСЬ ===
        public (bool Success, string Status, bool NeedsPromotion, bool IsGameOver) TryMakeMove(
            int fromX, int fromY, int toX, int toY, PieceType promotionType = PieceType.Queen)
        {
            if (IsGameOver) 
                return (false, "Игра окончена", false, true);

            if (!IsValidCoordinate(fromX, fromY) || !IsValidCoordinate(toX, toY)) 
                return (false, "Координаты вне доски", false, false);

            var piece = Board[fromY, fromX];
            if (piece == null || piece.Color != CurrentTurn)
            {
                string msg = piece == null ? "Здесь нет фигуры" : $"Сейчас ход {(CurrentTurn == PieceColor.White ? "белых" : "черных")}";
                return (false, msg, false, false);
            }

            var pseudoMoves = piece.GetLegalMoves(Board, new Position(fromX, fromY));
            var targetPos = new Position(toX, toY);

            bool isCastlingAttempt = false;
            if (piece.Type == PieceType.King && Math.Abs(toX - fromX) == 2 && toY == fromY)
                isCastlingAttempt = true;
            else if (!pseudoMoves.Contains(targetPos))
            {
                bool isEpAttempt = (piece.Type == PieceType.Pawn && toX != fromX && Board[toY, toX] == null);
                if (!isEpAttempt) 
                    return (false, "Недопустимый ход для этой фигуры", false, false);
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
                        return (false, "Ошибка логики взятия на проходе", false, false);
                }
                else 
                    return (false, "Недопустимый ход (взятие на проходе невозможно)", false, false);
            }

            if (isCastlingAttempt)
            {
                bool kingside = toX > fromX;
                int rookFromX = kingside ? 7 : 0;

                if (CurrentTurn == PieceColor.White)
                {
                    if (_whiteKingMoved || (kingside && _whiteRookKingsideMoved) || (!kingside && _whiteRookQueensideMoved))
                        return (false, "Король или ладья уже ходили", false, false);
                }
                else
                {
                    if (_blackKingMoved || (kingside && _blackRookKingsideMoved) || (!kingside && _blackRookQueensideMoved))
                        return (false, "Король или ладья уже ходили", false, false);
                }

                if (IsKingInCheck(CurrentTurn)) 
                    return (false, "Нельзя рокироваться под шахом", false, false);

                int startCheckX = kingside ? fromX + 1 : rookFromX + 1;
                int endCheckX = kingside ? toX : fromX - 1;

                for (int x = startCheckX; x <= endCheckX; x++)
                {
                    if (Board[fromY, x] != null)
                        return (false, $"Путь заблокирован на {(char)('a' + x)}{8-fromY}", false, false);

                    bool isKingPath = !( !kingside && x == rookFromX + 1 );
                    
                    if (isKingPath && IsSquareAttacked(x, fromY, CurrentTurn == PieceColor.White ? PieceColor.Black : PieceColor.White))
                        return (false, $"Поле {(char)('a' + x)}{8-fromY} под ударом", false, false);
                }

                var rook = Board[fromY, rookFromX];
                if (rook == null || rook.Type != PieceType.Rook || rook.Color != CurrentTurn)
                    return (false, "Невозможная рокировка", false, false);
            }
            
            var capturedPiece = Board[toY, toX];
            var movingPiece = Board[fromY, fromX];
            
            // Симуляция для проверки шаха
            Board[toY, toX] = movingPiece;
            Board[fromY, fromX] = null;
            
            Piece? epRemovedPiece = null;
            if (isEnPassantCapture && epCapturedPos.HasValue)
            {
                epRemovedPiece = Board[epCapturedPos.Value.Y, epCapturedPos.Value.X];
                Board[epCapturedPos.Value.Y, epCapturedPos.Value.X] = null;
            }

            bool inCheck = IsKingInCheck(CurrentTurn);

            // Откат симуляции
            Board[fromY, fromX] = movingPiece;
            Board[toY, toX] = capturedPiece;
            if (isEnPassantCapture && epCapturedPos.HasValue)
            {
                Board[epCapturedPos.Value.Y, epCapturedPos.Value.X] = epRemovedPiece;
            }

            if (inCheck) 
                return (false, "Нельзя ходить под шах!", false, false);

            bool isPromotionNeeded = (movingPiece != null && movingPiece.Type == PieceType.Pawn) && 
                                    ((movingPiece.Color == PieceColor.White && toY == 0) || 
                                    (movingPiece.Color == PieceColor.Black && toY == 7));

            // 1. СОХРАНЯЕМ СТАРЫЕ ЗНАЧЕНИЯ ДЛЯ ОТКАТА
            var oldEpTarget = _enPassantTarget;
            var oldWhiteKingMoved = _whiteKingMoved;
            var oldBlackKingMoved = _blackKingMoved;
            var oldWhiteRookKingsideMoved = _whiteRookKingsideMoved;
            var oldWhiteRookQueensideMoved = _whiteRookQueensideMoved;
            var oldBlackRookKingsideMoved = _blackRookKingsideMoved;
            var oldBlackRookQueensideMoved = _blackRookQueensideMoved;
            var oldHalfMoveClock = _halfMoveClock;
            var oldIsGameOver = IsGameOver;
            var oldLastStatus = LastStatus;
            string? oldLastNotation = MoveHistory.Count > 0 ? MoveHistory[MoveHistory.Count - 1] : null;

            // 2. ВЫПОЛНЯЕМ ХОД
            _enPassantTarget = null;
            Board[toY, toX] = movingPiece;
            Board[fromY, fromX] = null;

            bool isCaptureOrPawnMove = (capturedPiece != null || isEnPassantCapture || (movingPiece != null && movingPiece.Type == PieceType.Pawn));

            if (isEnPassantCapture && epCapturedPos.HasValue)
            {
                Board[epCapturedPos.Value.Y, epCapturedPos.Value.X] = null;
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

            if (isCaptureOrPawnMove)
                _halfMoveClock = 0;
            else
                _halfMoveClock++;

            if (isPromotionNeeded && movingPiece != null)
            {
                Board[toY, toX] = CreatePiece(movingPiece.Color, promotionType);
            }

            // 3. ВЫЧИСЛЯЕМ ХЕШ ПОСЛЕ ВСЕХ ИЗМЕНЕНИЙ
            string hashAfter = GetPositionHash();

            // 4. СОЗДАЕМ И ПУШИМ СОСТОЯНИЕ
            var state = new MoveState
            {
                HashAfterMove = hashAfter,
                FromX = fromX, FromY = fromY, ToX = toX, ToY = toY,
                MovedPiece = movingPiece,
                CapturedPiece = capturedPiece,
                EpCapturedPiece = isEnPassantCapture ? epRemovedPiece : null,
                EpTargetBefore = oldEpTarget,
                CastlingMoved = isCastlingAttempt,
                RookFromX = isCastlingAttempt ? (toX > fromX ? 7 : 0) : -1,
                RookToX = isCastlingAttempt ? (toX > fromX ? 5 : 3) : -1,
                RookY = isCastlingAttempt ? fromY : -1,
                PromotionType = isPromotionNeeded ? promotionType : null,
                PromotedPieceBefore = isPromotionNeeded ? movingPiece : null,
                WhiteKingMoved = oldWhiteKingMoved,
                BlackKingMoved = oldBlackKingMoved,
                WhiteRookKingsideMoved = oldWhiteRookKingsideMoved,
                WhiteRookQueensideMoved = oldWhiteRookQueensideMoved,
                BlackRookKingsideMoved = oldBlackRookKingsideMoved,
                BlackRookQueensideMoved = oldBlackRookQueensideMoved,
                HalfMoveClockBefore = oldHalfMoveClock,
                IsGameOverBefore = oldIsGameOver,
                LastStatusBefore = oldLastStatus,
                LastMoveNotation = oldLastNotation
            };
            _moveHistoryStack.Push(state);

            // 5. ЗАПИСЬ В ИСТОРИЮ НОТАЦИИ И ПОЗИЦИЙ
            string moveNotation = "";
            string files = "abcdefgh";

            if (isPromotionNeeded && movingPiece != null)
            {
                moveNotation = $"{files[fromX]}{8-fromY}-{files[toX]}{8-toY}";
                string pieceCode = promotionType switch
                {
                    PieceType.Queen => "Q", PieceType.Rook => "R", PieceType.Bishop => "B", PieceType.Knight => "N", _ => "?"
                };
                moveNotation += "=" + pieceCode;
            }
            else
            {
                moveNotation = $"{files[fromX]}{8-fromY}-{files[toX]}{8-toY}";
                if (isCastlingAttempt) moveNotation = (toX > fromX) ? "O-O" : "O-O-O";
                if (isEnPassantCapture) moveNotation += " e.p.";
            }

            MoveHistory.Add(moveNotation);

            PieceColor previousTurn = CurrentTurn;
            CurrentTurn = (CurrentTurn == PieceColor.White) ? PieceColor.Black : PieceColor.White;

            RecordPosition();

            // 6. ПРОВЕРКА НА ОКОНЧАНИЕ ИГРЫ
            bool opponentInCheck = IsKingInCheck(CurrentTurn);
            bool opponentHasMoves = HasLegalMoves(CurrentTurn);
            bool gameOverReasonFound = false;
            string finalStatus = "";

            if (!opponentHasMoves)
            {
                IsGameOver = true;
                finalStatus = opponentInCheck ? $"МАТ! Победили {(previousTurn == PieceColor.White ? "белые" : "черные")}!" : "ПАТ! Ничья.";
                gameOverReasonFound = true;
            }
            else if (_halfMoveClock >= 100)
            {
                IsGameOver = true;
                finalStatus = "Ничья по правилу 50 ходов!";
                gameOverReasonFound = true;
            }
            else if (CheckInsufficientMaterial())
            {
                IsGameOver = true;
                finalStatus = "Ничья из-за недостаточности материала!";
                gameOverReasonFound = true;
            }
            else
            {
                if (_positionHistory.ContainsKey(hashAfter) && _positionHistory[hashAfter] >= 3)
                {
                    IsGameOver = true;
                    finalStatus = "Ничья из-за троекратного повторения позиции!";
                    gameOverReasonFound = true;
                }
            }

            if (!gameOverReasonFound)
            {
                finalStatus = opponentInCheck ? $"Шах! Ход {(CurrentTurn == PieceColor.White ? "белых" : "черных")}." : $"Ход выполнен. Теперь {(CurrentTurn == PieceColor.White ? "белых" : "черных")}.";
            }

            LastStatus = finalStatus;
            
            return (true, finalStatus, false, IsGameOver);
        }

        // Метод отката
        public void UndoMove()
        {
            if (_moveHistoryStack.Count == 0) return;

            var state = _moveHistoryStack.Pop();

            // 1. Откат фигур
            Board[state.FromY, state.FromX] = state.MovedPiece;
            Board[state.ToY, state.ToX] = state.CapturedPiece;

            if (state.EpCapturedPiece != null)
            {
                Board[state.FromY, state.ToX] = state.EpCapturedPiece;
            }

            if (state.CastlingMoved && state.RookY != -1)
            {
                var rook = Board[state.RookY, state.RookToX];
                if (rook != null && rook.Type == PieceType.Rook)
                {
                    Board[state.RookY, state.RookFromX] = rook;
                    Board[state.RookY, state.RookToX] = null;
                }
            }

            // 2. Откат флагов (восстанавливаем значения ДО хода)
            _enPassantTarget = state.EpTargetBefore;
            _whiteKingMoved = state.WhiteKingMoved;
            _blackKingMoved = state.BlackKingMoved;
            _whiteRookKingsideMoved = state.WhiteRookKingsideMoved;
            _whiteRookQueensideMoved = state.WhiteRookQueensideMoved;
            _blackRookKingsideMoved = state.BlackRookKingsideMoved;
            _blackRookQueensideMoved = state.BlackRookQueensideMoved;
            _halfMoveClock = state.HalfMoveClockBefore;
            IsGameOver = state.IsGameOverBefore;
            LastStatus = state.LastStatusBefore;

            // 3. УДАЛЕНИЕ ИЗ ИСТОРИИ ПОВТОРЕНИЙ (САМОЕ ВАЖНОЕ)
            if (!string.IsNullOrEmpty(state.HashAfterMove) && _positionHistory.ContainsKey(state.HashAfterMove))
            {
                _positionHistory[state.HashAfterMove]--;
                if (_positionHistory[state.HashAfterMove] <= 0)
                {
                    _positionHistory.Remove(state.HashAfterMove);
                }
            }

            // 4. Откат хода и истории нотации
            CurrentTurn = (CurrentTurn == PieceColor.White) ? PieceColor.Black : PieceColor.White;

            if (MoveHistory.Count > 0)
            {
                MoveHistory.RemoveAt(MoveHistory.Count - 1);
            }
        }

        private bool IsValidCoordinate(int x, int y) => x >= 0 && x < 8 && y >= 0 && y < 8;
        
        public void SetStatus(string msg) => LastStatus = msg;
    }
}