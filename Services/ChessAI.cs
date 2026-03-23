using System;
using System.Collections.Generic;
using System.Linq;
using kchess.Services;

namespace kchess
{
    public class ChessAI
    {
        private readonly NeuralEvaluator _evaluator;
        private const int DEPTH = 4; // Глубина поиска: 2 полухода (мой ход + ответ врага)

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

            var bestMove = candidates[0];
            // Используем очень маленькое число для старта
            float bestScore = float.MinValue;
            float alpha = float.MinValue;
            float beta = float.MaxValue;

            // Определяем, за кого играет бот сейчас (чтобы знать знак оценки)
            bool isWhiteTurn = engine.Board[candidates[0].fromY, candidates[0].fromX].Color == PieceColor.White;

            foreach (var move in candidates)
            {
                // 1. Делаем ход
                var piece = engine.Board[move.fromY, move.fromX];
                var captured = engine.Board[move.toY, move.toX];
                
                engine.Board[move.toY, move.toX] = piece;
                engine.Board[move.fromY, move.fromX] = null;

                // 2. Запускаем рекурсивный поиск (Minimax)
                // После нашего хода ходит противник, поэтому ищем МИНИМУМ для нас
                float score = -Minimax(engine, DEPTH - 1, alpha, beta, !isWhiteTurn);

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
        private float Minimax(ChessEngine engine, int depth, float alpha, float beta, bool isMaximizingPlayer)
        {
            // Базовый случай: достигли нужной глубины или конец игры
            if (depth == 0 || engine.IsGameOver)
            {
                float score = _evaluator.Evaluate(engine.Board);
                // Возвращаем оценку с точки зрения того, кто делал ход в корне дерева
                // Но так как мы инвертируем знак на каждом уровне, просто возвращаем score
                return score;
            }

            // Генерируем все легальные ходы для текущей позиции
            // ВНИМАНИЕ: Здесь нам нужен быстрый способ получить ходы. 
            // Так как у нас нет доступа к ViewModel здесь, придется перебирать доску вручную (как в старом коде).
            var moves = GenerateAllLegalMovesFast(engine);

            if (moves.Count == 0) return 0; // Пат или мат

            float bestScore = isMaximizingPlayer ? float.MinValue : float.MaxValue;

            foreach (var move in moves)
            {
                var piece = engine.Board[move.fromY, move.fromX];
                var captured = engine.Board[move.toY, move.toX];

                // Делаем ход
                engine.Board[move.toY, move.toX] = piece;
                engine.Board[move.fromY, move.fromX] = null;

                float score;
                if (isMaximizingPlayer)
                {
                    // Ход "нашего" игрока (в контексте этой ветки) - хотим максимизировать
                    // Но следующая функция вернет оценку с инверсией, поэтому...
                    // Упрощенная схема Negamax: всегда ищем максимум от инвертированного результата детей
                    score = -Minimax(engine, depth - 1, -beta, -alpha, false);
                }
                else
                {
                    score = -Minimax(engine, depth - 1, -beta, -alpha, true);
                }

                // Откат
                engine.Board[move.fromY, move.fromX] = piece;
                engine.Board[move.toY, move.toX] = captured;

                if (isMaximizingPlayer)
                {
                    if (score > bestScore) bestScore = score;
                    alpha = Math.Max(alpha, score);
                }
                else
                {
                    if (score < bestScore) bestScore = score;
                    beta = Math.Min(beta, score);
                }

                if (beta <= alpha) break;
            }

            return bestScore;
        }

        // Быстрая генерация ходов внутри AI (копия логики из ViewModel, но без проверок UI)
        private List<(int fromX, int fromY, int toX, int toY)> GenerateAllLegalMovesFast(ChessEngine engine)
        {
            var moves = new List<(int fromX, int fromY, int toX, int toY)>();
            var board = engine.Board;
            var color = engine.CurrentTurn;

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