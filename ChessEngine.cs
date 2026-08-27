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
        PromotionNeeded
    }

    public class ChessEngine
    {
        private static readonly int[] KnightDx = { 1, 2, 2, 1, -1, -2, -2, -1 };
        private static readonly int[] KnightDy = { 2, 1, -1, -2, -2, -1, 1, 2 };
        private static readonly int[] RookDx = { 1, -1, 0, 0 };
        private static readonly int[] RookDy = { 0, 0, 1, -1 };
        private static readonly int[] BishopDx = { 1, 1, -1, -1 };
        private static readonly int[] BishopDy = { 1, -1, 1, -1 };

        public Piece?[,] Board { get; private set; }
        public PieceColor CurrentTurn { get; private set; } = PieceColor.White;
        public bool IsGameOver { get; private set; } = false;
        public string LastStatus { get; private set; } = "Игра началась";

        public List<string> MoveHistory { get; private set; } = new List<string>();
        public (int fromX, int fromY, int toX, int toY)? LastMove { get; private set; } = null;

        private int _halfMoveClock = 0;
        public int HalfMoveClock => _halfMoveClock;

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
                moves.Add((pos.X, pos.Y));
            return moves;
        }

        public void InitializeBoard()
        {
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    Board[y, x] = null;

            MoveHistory.Clear();
            LastMove = null;
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

        private Piece CreatePiece(PieceColor color, PieceType type) => type switch
        {
            PieceType.Pawn => new Pawn(color),
            PieceType.Knight => new Knight(color),
            PieceType.Bishop => new Bishop(color),
            PieceType.Rook => new Rook(color),
            PieceType.Queen => new Queen(color),
            PieceType.King => new King(color),
            _ => throw new ArgumentException($"Неизвестный тип фигуры: {type}")
        };

        // Публичный хеш-идентификатор позиции. Теперь включает право хода —
        // по правилам ФИДЕ повторением считается только позиция с одинаковым sideToMove.
        public string GetPositionHash() => GetPositionHash(CurrentTurn);

        public string GetPositionHash(PieceColor sideToMove)
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
                        char c = p.Type switch
                        {
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
            sb.Append(sideToMove == PieceColor.White ? 'W' : 'B');
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

        // Интеграционные точки для поиска AI
        public int GetRepetitionCount() => GetRepetitionCount(GetPositionHash());

        public int GetRepetitionCount(string hash) =>
            _positionHistory.TryGetValue(hash, out var count) ? count : 0;

        public bool IsSquareAttacked(int x, int y, PieceColor attackerColor)
        {
            int pawnFromY = attackerColor == PieceColor.White ? y + 1 : y - 1;
            if (IsValidCoordinate(x - 1, pawnFromY) && Board[pawnFromY, x - 1] is Piece p1 &&
                p1.Color == attackerColor && p1.Type == PieceType.Pawn)
                return true;
            if (IsValidCoordinate(x + 1, pawnFromY) && Board[pawnFromY, x + 1] is Piece p2 &&
                p2.Color == attackerColor && p2.Type == PieceType.Pawn)
                return true;

            for (int i = 0; i < 8; i++)
            {
                int ax = x + KnightDx[i];
                int ay = y + KnightDy[i];
                if (!IsValidCoordinate(ax, ay)) continue;
                var attacker = Board[ay, ax];
                if (attacker != null && attacker.Color == attackerColor && attacker.Type == PieceType.Knight)
                    return true;
            }

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
                        break;
                    }
                    ax += dx;
                    ay += dy;
                }
            }

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

        public bool CheckInsufficientMaterial()
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
            if (whitePieces.Count == 1 && blackPieces.Count == 2 &&
                blackPieces.Any(p => p.Type == PieceType.Knight)) return true;
            if (blackPieces.Count == 1 && whitePieces.Count == 2 &&
                whitePieces.Any(p => p.Type == PieceType.Knight)) return true;
            if (whitePieces.Count == 1 && blackPieces.Count == 2 &&
                blackPieces.Any(p => p.Type == PieceType.Bishop)) return true;
            if (blackPieces.Count == 1 && whitePieces.Count == 2 &&
                whitePieces.Any(p => p.Type == PieceType.Bishop)) return true;
            if (whitePieces.Count == 2 && blackPieces.Count == 2)
            {
                var wBishop = whitePieces.FirstOrDefault(p => p.Type == PieceType.Bishop);
                var bBishop = blackPieces.FirstOrDefault(p => p.Type == PieceType.Bishop);
                if (wBishop != null && bBishop != null)
                {
                    int wSq = -1, bSq = -1;
                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 8; x++)
                        {
                            if (Board[y, x] == wBishop) wSq = (x + y) % 2;
                            if (Board[y, x] == bBishop) bSq = (x + y) % 2;
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
            if (isCastlingAttempt)
            {
                bool kingside = toX > fromX;
                int rookFromX = kingside ? 7 : 0;
                int rookToX = kingside ? 5 : 3;
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
                int startCheckX = kingside ? fromX + 1 : rookFromX + 1;
                int endCheckX = kingside ? toX : fromX - 1;
                for (int x = startCheckX; x <= endCheckX; x++)
                {
                    if (Board[fromY, x] != null)
                    { LastStatus = $"Путь заблокирован на {(char)('a' + x)}{8 - fromY}"; return false; }
                    bool isKingPath = !(!kingside && x == rookFromX + 1);
                    if (isKingPath && IsSquareAttacked(x, fromY, CurrentTurn == PieceColor.White ? PieceColor.Black : PieceColor.White))
                    { LastStatus = $"Поле {(char)('a' + x)}{8 - fromY} под ударом"; return false; }
                }
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
                Board[epCapturedPos.Value.Y, epCapturedPos.Value.X] = epRemovedPiece;
            if (inCheck) { LastStatus = "Нельзя ходить под шах!"; return false; }

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
            if (isCaptureOrPawnMove) _halfMoveClock = 0;
            else _halfMoveClock++;

            bool isPromotionNeeded = (movingPiece != null && movingPiece.Type == PieceType.Pawn) &&
                                     ((movingPiece.Color == PieceColor.White && toY == 0) ||
                                     (movingPiece.Color == PieceColor.Black && toY == 7));
            string files = "abcdefgh";
            string moveNotation = $"{files[fromX]}{8 - fromY}-{files[toX]}{8 - toY}";
            if (isCastlingAttempt) moveNotation = (toX > fromX) ? "O-O" : "O-O-O";
            if (isEnPassantCapture) moveNotation += " e.p.";
            if (isPromotionNeeded && movingPiece != null)
            {
                Board[toY, toX] = CreatePiece(movingPiece.Color, promotionType);
                string pieceCode = promotionType switch
                {
                    PieceType.Queen => "Q", PieceType.Rook => "R",
                    PieceType.Bishop => "B", PieceType.Knight => "N", _ => "?"
                };
                moveNotation += "=" + pieceCode;
                LastStatus = $"Превращение пешки в {promotionType}!";
            }
            MoveHistory.Add(moveNotation);
            LastMove = (fromX, fromY, toX, toY);
            PieceColor previousTurn = CurrentTurn;
            CurrentTurn = (CurrentTurn == PieceColor.White) ? PieceColor.Black : PieceColor.White;
            RecordPosition();
            bool opponentInCheck = IsKingInCheck(CurrentTurn);
            bool opponentHasMoves = HasLegalMoves(CurrentTurn);
            bool gameOverReasonFound = false;
            if (!opponentHasMoves)
            {
                IsGameOver = true;
                if (opponentInCheck) LastStatus = $"МАТ! Победили {(previousTurn == PieceColor.White ? "белые" : "черные")}!";
                else LastStatus = "ПАТ! Ничья.";
                gameOverReasonFound = true;
            }
            if (!gameOverReasonFound && _halfMoveClock >= 100)
            {
                IsGameOver = true;
                LastStatus = "Ничья по правилу 50 ходов!";
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