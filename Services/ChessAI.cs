using System;
using System.Collections.Generic;
using System.Linq;
using kchess.Services;

namespace kchess
{
    public class ChessAI
    {
        private readonly NeuralEvaluator _evaluator;
        private const int DEPTH = 3;

        public ChessAI(NeuralEvaluator evaluator)
        {
            _evaluator = evaluator;
        }

        public (int fromX, int fromY, int toX, int toY)? GetBestMove(ChessEngine engine)
        {
            if (engine.IsGameOver || _evaluator == null) return null;

            var moves = GenerateAllLegalMovesFast(engine);
            if (moves.Count == 0) return null;

            var rng = new Random();
            moves = moves.OrderBy(m => rng.Next()).ToList();

            float bestScore = float.MinValue;
            var bestMove = moves[0];
            float alpha = float.MinValue;
            float beta = float.MaxValue;

            foreach (var move in moves)
            {
                var result = engine.TryMakeMove(move.fromX, move.fromY, move.toX, move.toY);
                
                if (!result.Success) continue;

                float score = -Minimax(engine, DEPTH - 1, -beta, -alpha);

                engine.UndoMove();

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = move;
                }

                alpha = Math.Max(alpha, score);
                if (beta <= alpha) break;
            }

            return bestMove;
        }

        private float Minimax(ChessEngine engine, int depth, float alpha, float beta)
        {
            if (depth == 0 || engine.IsGameOver)
            {
                float score = _evaluator.Evaluate(engine.Board);
                return score;
            }

            var moves = GenerateAllLegalMovesFast(engine);
            if (moves.Count == 0) return 0;

            float maxScore = float.MinValue;

            foreach (var move in moves)
            {
                var result = engine.TryMakeMove(move.fromX, move.fromY, move.toX, move.toY);
                if (!result.Success) continue;

                float score = -Minimax(engine, depth - 1, -beta, -alpha);
                engine.UndoMove();

                if (score > maxScore)
                {
                    maxScore = score;
                }

                alpha = Math.Max(alpha, score);
                if (beta <= alpha) break;
            }

            return maxScore;
        }

        private List<(int fromX, int fromY, int toX, int toY)> GenerateAllLegalMovesFast(ChessEngine engine)
        {
            var moves = new List<(int, int, int, int)>();
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var piece = engine.Board[y, x];
                    if (piece != null && piece.Color == engine.CurrentTurn)
                    {
                        var pseudoMoves = engine.GetPseudoMoves(x, y);
                        
                        foreach (var target in pseudoMoves)
                        {
                            int tx = target.x;
                            int ty = target.y;

                            var result = engine.TryMakeMove(x, y, tx, ty);
                            if (result.Success)
                            {
                                moves.Add((x, y, tx, ty));
                                engine.UndoMove();
                            }
                        }
                    }
                }
            }
            return moves;
        }
    }
}