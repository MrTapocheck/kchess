using System;
using System.Collections.Generic;
using kchess;

namespace kchess.Services
{
    public class ChessAI
    {
        private readonly NeuralEvaluator _evaluator;
        private readonly int _depth;
        public const int EasyDepth = 2;
        public const int MediumDepth = 4;
        public const int HardDepth = 5;
        private const int MaxTtEntries = 1_000_000;
        private const int BatchChunkSize = 16;

        private enum TtFlag : byte { Exact = 0, LowerBound = 1, UpperBound = 2 }

        private readonly struct TtEntry
        {
            public readonly int Depth;
            public readonly float Score;
            public readonly TtFlag Flag;
            public TtEntry(int depth, float score, TtFlag flag)
            {
                Depth = depth; Score = score; Flag = flag;
            }
        }

        private readonly Dictionary<ulong, TtEntry> _tt = new Dictionary<ulong, TtEntry>(1 << 16);
        private static readonly int[] PieceValues = { 100, 320, 330, 500, 900, 20000 };
        private static readonly ulong[,,] ZobristPieces;
        private static readonly ulong ZobristWhiteToMove;
        private static readonly ulong ZobristAiIsWhite;

        static ChessAI()
        {
            ZobristPieces = new ulong[2, 6, 64];
            var rng = new Random(1337);
            for (int color = 0; color < 2; color++)
                for (int p = 0; p < 6; p++)
                    for (int sq = 0; sq < 64; sq++)
                        ZobristPieces[color, p, sq] = RandU64(rng);
            ZobristWhiteToMove = RandU64(rng);
            ZobristAiIsWhite = RandU64(rng);
        }

        private static ulong RandU64(Random rng)
        {
            var bytes = new byte[8];
            rng.NextBytes(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }

        private static int GetPieceValue(PieceType type) => PieceValues[(int)type];

        private static ulong ComputeHash(ChessEngine engine, PieceColor sideToMove, PieceColor aiColor)
        {
            ulong h = 0;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    var piece = engine.Board[y, x];
                    if (piece == null) continue;
                    int colorIndex = piece.Color == PieceColor.White ? 0 : 1;
                    int pieceIndex = (int)piece.Type;
                    int sq = y * 8 + x;
                    h ^= ZobristPieces[colorIndex, pieceIndex, sq];
                }
            if (sideToMove == PieceColor.White) h ^= ZobristWhiteToMove;
            if (aiColor == PieceColor.White) h ^= ZobristAiIsWhite;
            return h;
        }

        private static PieceColor ToggleColor(PieceColor c) =>
            c == PieceColor.White ? PieceColor.Black : PieceColor.White;

        private static float ToAiPerspective(float whiteMinusBlack, PieceColor aiColor) =>
            aiColor == PieceColor.White ? whiteMinusBlack : -whiteMinusBlack;

        private readonly struct SearchUndo
        {
            public readonly Piece? MovingPiece;
            public readonly Piece? CapturedOnTarget;
            public readonly Piece? CapturedEnPassantPawn;
            public readonly bool IsEnPassant;
            public readonly bool IsCastling;
            public readonly int RookFromX;
            public readonly int RookToX;
            public readonly int RookY;
            public readonly Position? EnPassantTargetBefore;
            public readonly bool WhiteKingMovedBefore;
            public readonly bool BlackKingMovedBefore;
            public readonly bool WhiteRookKingsideMovedBefore;
            public readonly bool WhiteRookQueensideMovedBefore;
            public readonly bool BlackRookKingsideMovedBefore;
            public readonly bool BlackRookQueensideMovedBefore;

            public SearchUndo(
                Piece? movingPiece, Piece? capturedOnTarget, Piece? capturedEnPassantPawn,
                bool isEnPassant, bool isCastling, int rookFromX, int rookToX, int rookY,
                Position? enPassantTargetBefore,
                bool whiteKingMovedBefore, bool blackKingMovedBefore,
                bool whiteRookKingsideMovedBefore, bool whiteRookQueensideMovedBefore,
                bool blackRookKingsideMovedBefore, bool blackRookQueensideMovedBefore)
            {
                MovingPiece = movingPiece; CapturedOnTarget = capturedOnTarget;
                CapturedEnPassantPawn = capturedEnPassantPawn;
                IsEnPassant = isEnPassant; IsCastling = isCastling;
                RookFromX = rookFromX; RookToX = rookToX; RookY = rookY;
                EnPassantTargetBefore = enPassantTargetBefore;
                WhiteKingMovedBefore = whiteKingMovedBefore; BlackKingMovedBefore = blackKingMovedBefore;
                WhiteRookKingsideMovedBefore = whiteRookKingsideMovedBefore;
                WhiteRookQueensideMovedBefore = whiteRookQueensideMovedBefore;
                BlackRookKingsideMovedBefore = blackRookKingsideMovedBefore;
                BlackRookQueensideMovedBefore = blackRookQueensideMovedBefore;
            }
        }

        private static bool IsEnPassantCapture(ChessEngine engine, (int fromX, int fromY, int toX, int toY) move, Piece movingPiece)
        {
            if (movingPiece.Type != PieceType.Pawn) return false;
            if (move.fromX == move.toX) return false;
            if (engine.Board[move.toY, move.toX] != null) return false;
            if (!engine._enPassantTarget.HasValue) return false;
            var ep = engine._enPassantTarget.Value;
            return ep.X == move.toX && ep.Y == move.toY;
        }

        private int GetMoveOrderingScore(ChessEngine engine, (int fromX, int fromY, int toX, int toY) move)
        {
            var attacker = engine.Board[move.fromY, move.fromX];
            var captured = engine.Board[move.toY, move.toX];
            if (attacker == null) return int.MinValue;
            if (captured == null && IsEnPassantCapture(engine, move, attacker))
                return GetPieceValue(PieceType.Pawn) * 10 - GetPieceValue(attacker.Type);
            if (captured == null) return 0;
            return GetPieceValue(captured.Type) * 10 - GetPieceValue(attacker.Type);
        }

        private List<(int fromX, int fromY, int toX, int toY)> OrderMoves(ChessEngine engine, List<(int fromX, int fromY, int toX, int toY)> moves, bool isMaximizing)
        {
            if (moves.Count <= 1) return moves;
            var captures = new List<(int fromX, int fromY, int toX, int toY)>(moves.Count);
            var quiet = new List<(int fromX, int fromY, int toX, int toY)>(moves.Count);
            foreach (var m in moves)
            {
                var captured = engine.Board[m.toY, m.toX];
                if (captured != null) captures.Add(m);
                else quiet.Add(m);
            }
            captures.Sort((a, b) =>
            {
                int sa = GetMoveOrderingScore(engine, a);
                int sb = GetMoveOrderingScore(engine, b);
                return isMaximizing ? sb.CompareTo(sa) : sa.CompareTo(sb);
            });
            captures.AddRange(quiet);
            return captures;
        }

        public ChessAI(NeuralEvaluator evaluator, int depth = 5)
        {
            _evaluator = evaluator;
            _depth = Math.Max(1, depth);
        }

        public int Depth => _depth;

        public (int fromX, int fromY, int toX, int toY)? GetBestMove(
            ChessEngine engine,
            List<(int fromX, int fromY, int toX, int toY)> candidates)
        {
            if (candidates.Count == 0 || _evaluator == null)
                return null;

            candidates.Sort((a, b) => GetMoveOrderingScore(engine, b).CompareTo(GetMoveOrderingScore(engine, a)));

            var firstPiece = engine.Board[candidates[0].fromY, candidates[0].fromX];
            if (firstPiece == null) return null;
            PieceColor aiColor = firstPiece.Color;

            int initialHalfMoveClock = engine.HalfMoveClock;
            var repetitions = new Dictionary<ulong, int>();
            ulong startHash = ComputeHash(engine, engine.CurrentTurn, aiColor);
            int realReps = engine.GetRepetitionCount();
            repetitions[startHash] = realReps;

            var bestMove = candidates[0];
            float bestScore = float.MinValue;
            float alpha = float.MinValue;
            float beta = float.MaxValue;

            foreach (var move in candidates)
            {
                ApplyMoveForSearch(engine, move, out var undo);

                bool resetClock = undo.CapturedOnTarget != null || undo.IsEnPassant ||
                                   (undo.MovingPiece != null && undo.MovingPiece.Type == PieceType.Pawn);
                int nextClock = resetClock ? 0 : initialHalfMoveClock + 1;

                PieceColor nextSideToMove = ToggleColor(aiColor);
                ulong nextHash = ComputeHash(engine, nextSideToMove, aiColor);
                repetitions.TryGetValue(nextHash, out int prevReps);
                repetitions[nextHash] = prevReps + 1;

                float score;
                if (nextClock >= 100 || repetitions[nextHash] >= 3 || engine.CheckInsufficientMaterial())
                    score = 0f;
                else
                    score = Minimax(engine, _depth - 1, alpha, beta, aiColor, nextSideToMove, nextClock, repetitions);

                if (prevReps == 0) repetitions.Remove(nextHash);
                else repetitions[nextHash] = prevReps;

                UnmakeMoveForSearch(engine, move, undo);

                if (score > bestScore) { bestScore = score; bestMove = move; }
                alpha = Math.Max(alpha, score);
                if (beta <= alpha) break;
            }
            return bestMove;
        }

        private float Minimax(
            ChessEngine engine, int depth, float alpha, float beta,
            PieceColor aiColor, PieceColor sideToMove,
            int halfMoveClock, Dictionary<ulong, int> repetitions)
        {
            bool isMaximizing = sideToMove == aiColor;

            ulong currentHash = ComputeHash(engine, sideToMove, aiColor);

            if (halfMoveClock >= 100 || engine.CheckInsufficientMaterial())
                return 0f;
            if (repetitions.TryGetValue(currentHash, out int currentReps) && currentReps >= 3)
                return 0f;

            if (depth == 0)
                return ToAiPerspective(_evaluator.Evaluate(engine.Board), aiColor);

            if (_tt.TryGetValue(currentHash, out var ttEntry) && ttEntry.Depth >= depth)
            {
                if (ttEntry.Flag == TtFlag.Exact)
                    return ttEntry.Score;
            }

            var moves = GenerateAllLegalMovesFast(engine, sideToMove);
            if (moves.Count == 0)
            {
                if (engine.IsKingInCheck(sideToMove))
                    return isMaximizing ? -100000f : 100000f;
                return 0f;
            }

            moves = OrderMoves(engine, moves, isMaximizing);
            float bestScore = isMaximizing ? float.MinValue : float.MaxValue;
            bool cutoffOccurred = false;

            if (depth == 1 && moves.Count > 0)
            {
                for (int start = 0; start < moves.Count; start += BatchChunkSize)
                {
                    int batchCount = Math.Min(BatchChunkSize, moves.Count - start);
                    float[] batchInput = new float[batchCount * NeuralEvaluator.InputSize];
                    bool[] isDraw = new bool[batchCount];

                    for (int i = 0; i < batchCount; i++)
                    {
                        var move = moves[start + i];
                        ApplyMoveForSearch(engine, move, out var undo);

                        bool resetClock = undo.CapturedOnTarget != null || undo.IsEnPassant ||
                                           (undo.MovingPiece != null && undo.MovingPiece.Type == PieceType.Pawn);
                        int nextClock = resetClock ? 0 : halfMoveClock + 1;

                        PieceColor nextSideToMove = ToggleColor(sideToMove);
                        ulong nextHash = ComputeHash(engine, nextSideToMove, aiColor);
                        repetitions.TryGetValue(nextHash, out int prevReps);
                        repetitions[nextHash] = prevReps + 1;

                        if (nextClock >= 100 || repetitions[nextHash] >= 3 || engine.CheckInsufficientMaterial())
                        {
                            isDraw[i] = true;
                        }
                        else
                        {
                            _evaluator.EncodeBoard(engine.Board, batchInput, i);
                        }

                        if (prevReps == 0) repetitions.Remove(nextHash);
                        else repetitions[nextHash] = prevReps;

                        UnmakeMoveForSearch(engine, move, undo);
                    }

                    var evals = _evaluator.EvaluateBatch(batchInput, batchCount);

                    for (int i = 0; i < batchCount; i++)
                    {
                        float score = isDraw[i] ? 0f : ToAiPerspective(evals[i], aiColor);
                        if (isMaximizing)
                        {
                            if (score > bestScore) bestScore = score;
                            alpha = Math.Max(alpha, score);
                        }
                        else
                        {
                            if (score < bestScore) bestScore = score;
                            beta = Math.Min(beta, score);
                        }
                        if (beta <= alpha) { cutoffOccurred = true; break; }
                    }
                    if (cutoffOccurred) break;
                }

                if (_tt.Count > MaxTtEntries) _tt.Clear();
                var flag = cutoffOccurred
                    ? (isMaximizing ? TtFlag.LowerBound : TtFlag.UpperBound)
                    : TtFlag.Exact;
                float storeScore = cutoffOccurred ? (isMaximizing ? alpha : beta) : bestScore;
                _tt[currentHash] = new TtEntry(depth, storeScore, flag);
                return bestScore;
            }

            foreach (var move in moves)
            {
                ApplyMoveForSearch(engine, move, out var undo);

                bool resetClock = undo.CapturedOnTarget != null || undo.IsEnPassant ||
                                   (undo.MovingPiece != null && undo.MovingPiece.Type == PieceType.Pawn);
                int nextClock = resetClock ? 0 : halfMoveClock + 1;

                PieceColor nextSideToMove = ToggleColor(sideToMove);
                ulong nextHash = ComputeHash(engine, nextSideToMove, aiColor);
                repetitions.TryGetValue(nextHash, out int prevReps);
                repetitions[nextHash] = prevReps + 1;

                float score;
                if (nextClock >= 100 || repetitions[nextHash] >= 3 || engine.CheckInsufficientMaterial())
                    score = 0f;
                else
                    score = Minimax(engine, depth - 1, alpha, beta, aiColor, nextSideToMove, nextClock, repetitions);

                if (prevReps == 0) repetitions.Remove(nextHash);
                else repetitions[nextHash] = prevReps;

                UnmakeMoveForSearch(engine, move, undo);

                if (isMaximizing)
                {
                    if (score > bestScore) bestScore = score;
                    alpha = Math.Max(alpha, score);
                }
                else
                {
                    if (score < bestScore) bestScore = score;
                    beta = Math.Min(beta, score);
                }
                if (beta <= alpha) { cutoffOccurred = true; break; }
            }

            if (_tt.Count > MaxTtEntries) _tt.Clear();
            var ttFlag = cutoffOccurred
                ? (isMaximizing ? TtFlag.LowerBound : TtFlag.UpperBound)
                : TtFlag.Exact;
            float ttScore = cutoffOccurred ? (isMaximizing ? alpha : beta) : bestScore;
            _tt[currentHash] = new TtEntry(depth, ttScore, ttFlag);
            return bestScore;
        }

        private List<(int fromX, int fromY, int toX, int toY)> GenerateAllLegalMovesFast(ChessEngine engine, PieceColor color)
        {
            var moves = new List<(int fromX, int fromY, int toX, int toY)>();
            var board = engine.Board;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var piece = board[y, x];
                    if (piece != null && piece.Color == color)
                    {
                        var pseudoMoves = piece.GetLegalMoves(board, new Position(x, y));
                        foreach (var target in pseudoMoves)
                        {
                            var m = (x, y, target.X, target.Y);
                            if (IsMoveLegalFast(engine, m, color))
                                moves.Add(m);
                        }
                        if (piece.Type == PieceType.Pawn && engine._enPassantTarget.HasValue)
                        {
                            var ep = engine._enPassantTarget;
                            if (ep.HasValue)
                            {
                                int dir = piece.Color == PieceColor.White ? -1 : 1;
                                if (ep.Value.Y == y + dir && Math.Abs(ep.Value.X - x) == 1)
                                {
                                    var m = (x, y, ep.Value.X, ep.Value.Y);
                                    if (IsMoveLegalFast(engine, m, color))
                                        moves.Add(m);
                                }
                            }
                        }
                        if (piece.Type == PieceType.King)
                            AddCastlingMovesFast(engine, moves, x, y, color);
                    }
                }
            }
            return moves;
        }

        private bool IsMoveLegalFast(ChessEngine engine, (int fromX, int fromY, int toX, int toY) move, PieceColor movingColor)
        {
            var piece = engine.Board[move.fromY, move.fromX];
            if (piece == null) return false;
            ApplyMoveForSearch(engine, move, out var undo);
            bool isCheck = engine.IsKingInCheck(movingColor);
            UnmakeMoveForSearch(engine, move, undo);
            return !isCheck;
        }

        private void AddCastlingMovesFast(
            ChessEngine engine, List<(int fromX, int fromY, int toX, int toY)> moves,
            int kingX, int kingY, PieceColor color)
        {
            if (engine.IsKingInCheck(color)) return;
            bool isWhite = color == PieceColor.White;
            if (isWhite ? engine._whiteKingMoved : engine._blackKingMoved) return;
            if (!(isWhite ? engine._whiteRookKingsideMoved : engine._blackRookKingsideMoved) &&
                engine.Board[kingY, 5] == null && engine.Board[kingY, 6] == null &&
                !engine.IsSquareAttacked(5, kingY, ToggleColor(color)) &&
                !engine.IsSquareAttacked(6, kingY, ToggleColor(color)))
            {
                var rook = engine.Board[kingY, 7];
                if (rook != null && rook.Type == PieceType.Rook && rook.Color == color)
                {
                    var m = (kingX, kingY, 6, kingY);
                    if (IsMoveLegalFast(engine, m, color)) moves.Add(m);
                }
            }
            if (!(isWhite ? engine._whiteRookQueensideMoved : engine._blackRookQueensideMoved) &&
                engine.Board[kingY, 1] == null && engine.Board[kingY, 2] == null && engine.Board[kingY, 3] == null &&
                !engine.IsSquareAttacked(3, kingY, ToggleColor(color)) &&
                !engine.IsSquareAttacked(2, kingY, ToggleColor(color)))
            {
                var rook = engine.Board[kingY, 0];
                if (rook != null && rook.Type == PieceType.Rook && rook.Color == color)
                {
                    var m = (kingX, kingY, 2, kingY);
                    if (IsMoveLegalFast(engine, m, color)) moves.Add(m);
                }
            }
        }

        private void ApplyMoveForSearch(ChessEngine engine, (int fromX, int fromY, int toX, int toY) move, out SearchUndo undo)
        {
            var movingPiece = engine.Board[move.fromY, move.fromX];
            var capturedOnTarget = engine.Board[move.toY, move.toX];
            var enPassantBefore = engine._enPassantTarget;
            bool wk = engine._whiteKingMoved, bk = engine._blackKingMoved;
            bool wrk = engine._whiteRookKingsideMoved, wrq = engine._whiteRookQueensideMoved;
            bool brk = engine._blackRookKingsideMoved, brq = engine._blackRookQueensideMoved;

            bool isCastling = movingPiece != null && movingPiece.Type == PieceType.King &&
                              Math.Abs(move.toX - move.fromX) == 2 && move.toY == move.fromY;
            bool isEnPassant = movingPiece != null && IsEnPassantCapture(engine, move, movingPiece);
            Piece? capturedEpPawn = null;
            int rookFromX = -1, rookToX = -1, rookY = -1;

            if (isEnPassant && movingPiece != null)
            {
                int capturedPawnY = move.fromY;
                capturedEpPawn = engine.Board[capturedPawnY, move.toX];
                engine.Board[capturedPawnY, move.toX] = null;
            }
            engine.Board[move.toY, move.toX] = movingPiece;
            engine.Board[move.fromY, move.fromX] = null;

            if (isCastling)
            {
                bool kingside = move.toX > move.fromX;
                rookFromX = kingside ? 7 : 0;
                rookToX = kingside ? 5 : 3;
                rookY = move.fromY;
                var rook = engine.Board[rookY, rookFromX];
                engine.Board[rookY, rookToX] = rook;
                engine.Board[rookY, rookFromX] = null;
            }
            engine._enPassantTarget = null;
            if (movingPiece != null && movingPiece.Type == PieceType.Pawn && Math.Abs(move.toY - move.fromY) == 2)
            {
                int epY = (move.fromY + move.toY) / 2;
                engine._enPassantTarget = new Position(move.toX, epY);
            }
            if (movingPiece != null)
            {
                if (movingPiece.Type == PieceType.King)
                {
                    if (movingPiece.Color == PieceColor.White) engine._whiteKingMoved = true;
                    else engine._blackKingMoved = true;
                }
                else if (movingPiece.Type == PieceType.Rook)
                {
                    if (movingPiece.Color == PieceColor.White)
                    {
                        if (move.fromX == 0 && move.fromY == 7) engine._whiteRookQueensideMoved = true;
                        if (move.fromX == 7 && move.fromY == 7) engine._whiteRookKingsideMoved = true;
                    }
                    else
                    {
                        if (move.fromX == 0 && move.fromY == 0) engine._blackRookQueensideMoved = true;
                        if (move.fromX == 7 && move.fromY == 0) engine._blackRookKingsideMoved = true;
                    }
                }
            }
            if (capturedOnTarget != null && capturedOnTarget.Type == PieceType.Rook)
            {
                if (capturedOnTarget.Color == PieceColor.White)
                {
                    if (move.toX == 0 && move.toY == 7) engine._whiteRookQueensideMoved = true;
                    if (move.toX == 7 && move.toY == 7) engine._whiteRookKingsideMoved = true;
                }
                else
                {
                    if (move.toX == 0 && move.toY == 0) engine._blackRookQueensideMoved = true;
                    if (move.toX == 7 && move.toY == 0) engine._blackRookKingsideMoved = true;
                }
            }
            undo = new SearchUndo(
                movingPiece, capturedOnTarget, capturedEpPawn, isEnPassant, isCastling,
                rookFromX, rookToX, rookY, enPassantBefore,
                wk, bk, wrk, wrq, brk, brq);
        }

        private void UnmakeMoveForSearch(ChessEngine engine, (int fromX, int fromY, int toX, int toY) move, SearchUndo undo)
        {
            if (undo.IsCastling)
            {
                var rook = engine.Board[undo.RookY, undo.RookToX];
                engine.Board[undo.RookY, undo.RookFromX] = rook;
                engine.Board[undo.RookY, undo.RookToX] = null;
            }
            engine.Board[move.fromY, move.fromX] = undo.MovingPiece;
            engine.Board[move.toY, move.toX] = undo.CapturedOnTarget;
            if (undo.IsEnPassant && undo.CapturedEnPassantPawn != null)
            {
                int capturedPawnY = move.fromY;
                engine.Board[capturedPawnY, move.toX] = undo.CapturedEnPassantPawn;
            }
            engine._enPassantTarget = undo.EnPassantTargetBefore;
            engine._whiteKingMoved = undo.WhiteKingMovedBefore;
            engine._blackKingMoved = undo.BlackKingMovedBefore;
            engine._whiteRookKingsideMoved = undo.WhiteRookKingsideMovedBefore;
            engine._whiteRookQueensideMoved = undo.WhiteRookQueensideMovedBefore;
            engine._blackRookKingsideMoved = undo.BlackRookKingsideMovedBefore;
            engine._blackRookQueensideMoved = undo.BlackRookQueensideMovedBefore;
        }
    }
}