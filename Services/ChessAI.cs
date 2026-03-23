using System;
using System.Collections.Generic;
using kchess;

namespace kchess.Services
{
    public class ChessAI
    {
        private readonly NeuralEvaluator _evaluator;
        private const int DEPTH = 5; // Глубина поиска: 2 полухода (мой ход + ответ врага)
        private const int MaxTtEntries = 1_000_000;
        private const int BatchChunkSize = 16; // depth==1: оцениваем ходы батчами по N

        private enum TtFlag : byte
        {
            Exact = 0,
            LowerBound = 1,
            UpperBound = 2
        }

        private readonly struct TtEntry
        {
            public readonly int Depth;
            public readonly float Score;
            public readonly TtFlag Flag;

            public TtEntry(int depth, float score, TtFlag flag)
            {
                Depth = depth;
                Score = score;
                Flag = flag;
            }
        }

        private readonly Dictionary<ulong, TtEntry> _tt = new Dictionary<ulong, TtEntry>(1 << 16);

        // pieceType enum: Pawn=0, Knight=1, Bishop=2, Rook=3, Queen=4, King=5
        private static readonly int[] PieceValues = { 100, 320, 330, 500, 900, 20000 };

        // Zobrist keys for [colorIndex (0 white / 1 black), pieceTypeIndex (0..5), squareIndex (0..63)]
        private static readonly ulong[,,] ZobristPieces;
        private static readonly ulong ZobristWhiteToMove;
        private static readonly ulong ZobristAiIsWhite;

        static ChessAI()
        {
            ZobristPieces = new ulong[2, 6, 64];
            var rng = new Random(1337);
            for (int color = 0; color < 2; color++)
            {
                for (int p = 0; p < 6; p++)
                {
                    for (int sq = 0; sq < 64; sq++)
                    {
                        ZobristPieces[color, p, sq] = RandU64(rng);
                    }
                }
            }
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
            {
                for (int x = 0; x < 8; x++)
                {
                    var piece = engine.Board[y, x];
                    if (piece == null) continue;

                    int colorIndex = piece.Color == PieceColor.White ? 0 : 1;
                    int pieceIndex = (int)piece.Type;
                    int sq = y * 8 + x;
                    h ^= ZobristPieces[colorIndex, pieceIndex, sq];
                }
            }

            if (sideToMove == PieceColor.White)
                h ^= ZobristWhiteToMove;
            if (aiColor == PieceColor.White)
                h ^= ZobristAiIsWhite;

            return h;
        }

        private static PieceColor ToggleColor(PieceColor c) =>
            c == PieceColor.White ? PieceColor.Black : PieceColor.White;

        private static float ToAiPerspective(float whiteMinusBlack, PieceColor aiColor) =>
            aiColor == PieceColor.White ? whiteMinusBlack : -whiteMinusBlack;

        private int GetMoveOrderingScore(ChessEngine engine, (int fromX, int fromY, int toX, int toY) move)
        {
            var attacker = engine.Board[move.fromY, move.fromX];
            var captured = engine.Board[move.toY, move.toX];
            if (attacker == null) return int.MinValue;
            if (captured == null) return 0;

            // MVV-LVA: victim value - attacker value (скорее всего даст приоритет взятиям)
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
                return isMaximizing ? (sb.CompareTo(sa)) : (sa.CompareTo(sb));
            });

            // Quiet оставим как есть (они уже легальны), порядок влияет только на отсечения.
            captures.AddRange(quiet);
            return captures;
        }

        public ChessAI(NeuralEvaluator evaluator)
        {
            _evaluator = evaluator;
        }

        public (int fromX, int fromY, int toX, int toY)? GetBestMove(
            ChessEngine engine,
            List<(int fromX, int fromY, int toX, int toY)> candidates)
        {
            if (candidates.Count == 0 || _evaluator == null)
                return null;

            // Быстрая сортировка кандидатов: сначала взятия (MVV-LVA).
            candidates.Sort((a, b) => GetMoveOrderingScore(engine, b).CompareTo(GetMoveOrderingScore(engine, a)));

            var bestMove = candidates[0];
            // Используем очень маленькое число для старта
            float bestScore = float.MinValue;
            float alpha = float.MinValue;
            float beta = float.MaxValue;

            var firstPiece = engine.Board[candidates[0].fromY, candidates[0].fromX];
            if (firstPiece == null)
                return null;

            PieceColor aiColor = firstPiece.Color;

            foreach (var move in candidates)
            {
                // 1. Делаем ход
                var piece = engine.Board[move.fromY, move.fromX];
                var captured = engine.Board[move.toY, move.toX];
                
                engine.Board[move.toY, move.toX] = piece;
                engine.Board[move.fromY, move.fromX] = null;

                // После хода ИИ ходит противник => классический Minimax в режиме minimizing.
                float score = Minimax(engine, DEPTH - 1, alpha, beta, aiColor, ToggleColor(aiColor));

                // 3. Откатываем ход
                engine.Board[move.fromY, move.fromX] = piece;
                engine.Board[move.toY, move.toX] = captured;

                // 4. Alpha-Beta логика
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;
                }

                alpha = Math.Max(alpha, score);
                if (beta <= alpha)
                {
                    break; // Отсечение ветки
                }
            }

            return bestMove;
        }

        // Рекурсивная функция Minimax
        private float Minimax(
            ChessEngine engine,
            int depth,
            float alpha,
            float beta,
            PieceColor aiColor,
            PieceColor sideToMove)
        {
            bool isMaximizing = sideToMove == aiColor;

            // Базовый случай: достигли нужной глубины или конец игры
            if (depth == 0 || engine.IsGameOver)
            {
                return ToAiPerspective(_evaluator.Evaluate(engine.Board), aiColor);
            }

            // Transposition table lookup (кэш позиций)
            ulong hash = ComputeHash(engine, sideToMove, aiColor);
            if (_tt.TryGetValue(hash, out var ttEntry) && ttEntry.Depth >= depth)
            {
                // Для надежности используем из кэша только EXACT значения.
                // (Lower/UpperBound могут быть неточными при неклассическом Negamax/Minimax в этом коде.)
                if (ttEntry.Flag == TtFlag.Exact)
                    return ttEntry.Score;
            }

            // Генерируем все легальные ходы для текущей позиции
            // ВНИМАНИЕ: Здесь нам нужен быстрый способ получить ходы. 
            // Так как у нас нет доступа к ViewModel здесь, придется перебирать доску вручную (как в старом коде).
            var moves = GenerateAllLegalMovesFast(engine, sideToMove);

            if (moves.Count == 0)
            {
                // Если ходов нет: мат/пат.
                if (engine.IsKingInCheck(sideToMove))
                    return isMaximizing ? -100000f : 100000f;
                return 0f;
            }

            moves = OrderMoves(engine, moves, isMaximizing);

            float bestScore = isMaximizing ? float.MinValue : float.MaxValue;
            bool cutoffOccurred = false;

            // depth==1: батчим оценки для всех ходов одним/несколькими вызовами GPU
            if (depth == 1 && moves.Count > 0)
            {
                for (int start = 0; start < moves.Count; start += BatchChunkSize)
                {
                    int batchCount = Math.Min(BatchChunkSize, moves.Count - start);
                    float[] batchInput = new float[batchCount * NeuralEvaluator.InputSize];

                    // Создаем батч входов: позиция после каждого из moves[start..start+batchCount)
                    for (int i = 0; i < batchCount; i++)
                    {
                        var move = moves[start + i];
                        var piece = engine.Board[move.fromY, move.fromX];
                        var captured = engine.Board[move.toY, move.toX];

                        engine.Board[move.toY, move.toX] = piece;
                        engine.Board[move.fromY, move.fromX] = null;

                        _evaluator.EncodeBoard(engine.Board, batchInput, i);

                        // Откат
                        engine.Board[move.fromY, move.fromX] = piece;
                        engine.Board[move.toY, move.toX] = captured;
                    }

                    var evals = _evaluator.EvaluateBatch(batchInput, batchCount); // white-black

                    // Затем обновляем alpha/beta как обычно, но без дополнительных GPU вызовов
                    for (int i = 0; i < batchCount; i++)
                    {
                        float score = ToAiPerspective(evals[i], aiColor);

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

                        if (beta <= alpha)
                        {
                            cutoffOccurred = true;
                            break;
                        }
                    }

                    if (cutoffOccurred)
                        break;
                }

                // Запись в TT
                if (_tt.Count > MaxTtEntries)
                    _tt.Clear();

                var flag = cutoffOccurred
                    ? (isMaximizing ? TtFlag.LowerBound : TtFlag.UpperBound)
                    : TtFlag.Exact;

                float storeScore = cutoffOccurred
                    ? (isMaximizing ? alpha : beta)
                    : bestScore;

                _tt[hash] = new TtEntry(depth, storeScore, flag);

                return bestScore;
            }

            foreach (var move in moves)
            {
                var piece = engine.Board[move.fromY, move.fromX];
                var captured = engine.Board[move.toY, move.toX];

                // Делаем ход
                engine.Board[move.toY, move.toX] = piece;
                engine.Board[move.fromY, move.fromX] = null;

                float score = Minimax(engine, depth - 1, alpha, beta, aiColor, ToggleColor(sideToMove));

                // Откат
                engine.Board[move.fromY, move.fromX] = piece;
                engine.Board[move.toY, move.toX] = captured;

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

                if (beta <= alpha)
                {
                    cutoffOccurred = true;
                    break;
                }
            }

            // Запись в TT
            if (_tt.Count > MaxTtEntries)
                _tt.Clear();

            var ttFlag = cutoffOccurred
                ? (isMaximizing ? TtFlag.LowerBound : TtFlag.UpperBound)
                : TtFlag.Exact;

            float ttScore = cutoffOccurred
                ? (isMaximizing ? alpha : beta)
                : bestScore;

            _tt[hash] = new TtEntry(depth, ttScore, ttFlag);

            return bestScore;
        }

        // Быстрая генерация ходов внутри AI (копия логики из ViewModel, но без проверок UI)
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
                            if (IsMoveLegalFast(engine, x, y, target.X, target.Y))
                            {
                                moves.Add((x, y, target.X, target.Y));
                            }
                        }
                        // Тут можно добавить рокировку и En Passant, если критично, 
                        // но для глубины 2 часто хватает базовой геометрии + шах.
                        // Для надежности лучше скопировать полную логику из ViewModel.IsMoveLegal + рокировки.
                        // Но чтобы код был компактным, оставим базу. Если бот не рокируется - не страшно пока.
                    }
                }
            }
            return moves;
        }

        private bool IsMoveLegalFast(ChessEngine engine, int fromX, int fromY, int toX, int toY)
        {
            var piece = engine.Board[fromY, fromX];
            if (piece == null) return false;
            var captured = engine.Board[toY, toX];

            engine.Board[toY, toX] = piece;
            engine.Board[fromY, fromX] = null;

            bool isCheck = engine.IsKingInCheck(piece.Color);

            engine.Board[fromY, fromX] = piece;
            engine.Board[toY, toX] = captured;

            return !isCheck;
        }
    }
}