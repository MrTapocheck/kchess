using System;
using System.Collections.Generic;

namespace kchess
{
    /// <summary>
    /// Основной класс движка. Управляет состоянием доски, очередностью ходов и валидацией.
    /// Не содержит графики или UI логики.
    /// </summary>
    public class ChessEngine
    {
        // Доска 8x8. null означает пустую клетку.
        public Piece?[,] Board { get; private set; }
        
        public PieceColor CurrentTurn { get; private set; } = PieceColor.White;
        
        public bool IsGameOver { get; private set; } = false;
        
        public string LastStatus { get; private set; } = "Игра началась";

        public ChessEngine()
        {
            Board = new Piece?[8, 8];
            InitializeBoard();
        }

        /// <summary>
        /// Расставляет фигуры на стартовые позиции.
        /// </summary>
        private void InitializeBoard()
        {
            // Пешки
            for (int i = 0; i < 8; i++)
            {
                Board[1, i] = new Pawn(PieceColor.Black);
                Board[6, i] = new Pawn(PieceColor.White);
            }

            // Массив фигур для первого и последнего ряда
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

        /// <summary>
        /// Фабричный метод для создания фигур по типу.
        /// </summary>
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
        /// Попытка сделать ход.
        /// Возвращает true если ход легален и выполнен, иначе false.
        /// </summary>
        public bool TryMove(int fromX, int fromY, int toX, int toY)
        {
            if (IsGameOver)
            {
                LastStatus = "Игра окончена";
                return false;
            }

            // Проверка границ
            if (fromX < 0 || fromX > 7 || fromY < 0 || fromY > 7 ||
                toX < 0 || toX > 7 || toY < 0 || toY > 7)
            {
                LastStatus = "Координаты вне доски";
                return false;
            }

            var piece = Board[fromY, fromX];
            
            // Проверка: есть ли фигура и твой ли ход
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

            // Запрос легальных ходов у самой фигуры
            var legalMoves = piece.GetLegalMoves(Board, new Position(fromX, fromY));
            
            // Проверка: есть ли целевая клетка в списке легальных
            var targetPos = new Position(toX, toY);
            if (!legalMoves.Contains(targetPos))
            {
                LastStatus = "Недопустимый ход для этой фигуры";
                return false;
            }

            // --- Выполнение хода ---
            // Взятие фигуры (если есть)
            var captured = Board[toY, toX];
            if (captured != null && captured.Type == PieceType.King)
            {
                IsGameOver = true;
                LastStatus = $"Мат! Победили {(CurrentTurn == PieceColor.White ? "белые" : "черные")}";
            }

            // Перемещение
            Board[toY, toX] = piece;
            Board[fromY, fromX] = null;

            // Превращение пешки (упрощенно: всегда в ферзя, если дошла до края)
            if (piece.Type == PieceType.Pawn)
            {
                if ((piece.Color == PieceColor.White && toY == 0) || 
                    (piece.Color == PieceColor.Black && toY == 7))
                {
                    Board[toY, toX] = new Queen(piece.Color);
                    LastStatus = "Пешка превратилась в ферзя!";
                }
            }

            // Смена хода (если игра не закончена)
            if (!IsGameOver)
            {
                CurrentTurn = (CurrentTurn == PieceColor.White) ? PieceColor.Black : PieceColor.White;
                LastStatus = $"Ход выполнен. Теперь {(CurrentTurn == PieceColor.White ? "белых" : "черных")}.";
            }

            return true;
        }

        /// <summary>
        /// Возвращает текстовое представление доски для отладки или консоли.
        /// </summary>
        public string GetBoardVisual()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("  a b c d e f g h");
            for (int y = 0; y < 8; y++)
            {
                sb.Append(8 - y).Append(" ");
                for (int x = 0; x < 8; x++)
                {
                    var p = Board[y, x];
                    if (p == null) sb.Append(". ");
                    else
                    {
                        char symbol = p.Type switch
                        {
                            PieceType.King => 'K',
                            PieceType.Queen => 'Q',
                            PieceType.Rook => 'R',
                            PieceType.Bishop => 'B',
                            PieceType.Knight => 'N',
                            PieceType.Pawn => 'P',
                            _ => '?'
                        };
                        sb.Append(p.Color == PieceColor.White ? char.ToUpper(symbol) : char.ToLower(symbol));
                        sb.Append(" ");
                    }
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}