using System;
using System.Collections.Generic;

namespace kchess
{
    public enum PieceColor { White, Black }
    public enum PieceType { Pawn, Knight, Bishop, Rook, Queen, King }

    public struct Position
    {
        public int X { get; }
        public int Y { get; }

        public Position(int x, int y)
        {
            if (x < 0 || x > 7 || y < 0 || y > 7)
                throw new ArgumentOutOfRangeException($"Координаты ({x}, {y}) вне доски");
            X = x;
            Y = y;
        }

        public override bool Equals(object? obj) => obj is Position other && X == other.X && Y == other.Y;
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X}, {Y})";
    }

    public abstract class Piece
    {
        public PieceColor Color { get; }
        public PieceType Type { get; }

        protected Piece(PieceColor color, PieceType type)
        {
            Color = color;
            Type = type;
        }

        public abstract List<Position> GetLegalMoves(Piece?[,] board, Position currentPos);

        protected bool IsOnBoard(int x, int y) => x >= 0 && x < 8 && y >= 0 && y < 8;

        protected bool CanMoveTo(Piece?[,] board, int x, int y)
        {
            if (!IsOnBoard(x, y)) return false;
            var target = board[y, x];
            return target == null || target.Color != this.Color;
        }
    }

    public class Pawn : Piece
    {
        public Pawn(PieceColor color) : base(color, PieceType.Pawn) { }
        public override List<Position> GetLegalMoves(Piece?[,] board, Position currentPos)
        {
            var moves = new List<Position>();
            int direction = (Color == PieceColor.White) ? -1 : 1;
            int startRow = (Color == PieceColor.White) ? 6 : 1;

            // Ход вперед
            int nextY = currentPos.Y + direction;
            if (IsOnBoard(currentPos.X, nextY) && board[nextY, currentPos.X] == null)
            {
                moves.Add(new Position(currentPos.X, nextY));
                if (currentPos.Y == startRow)
                {
                    int doubleJumpY = currentPos.Y + (2 * direction);
                    if (board[doubleJumpY, currentPos.X] == null)
                        moves.Add(new Position(currentPos.X, doubleJumpY));
                }
            }

            // Взятие
            int[] dx = { -1, 1 };
            foreach (var offset in dx)
            {
                int newX = currentPos.X + offset;
                int newY = currentPos.Y + direction;
                if (IsOnBoard(newX, newY))
                {
                    var target = board[newY, newX];
                    if (target != null && target.Color != Color)
                        moves.Add(new Position(newX, newY));
                }
            }
            return moves;
        }
    }

    public class Knight : Piece
    {
        public Knight(PieceColor color) : base(color, PieceType.Knight) { }
        public override List<Position> GetLegalMoves(Piece?[,] board, Position currentPos)
        {
            var moves = new List<Position>();
            int[] dx = { 1, 2, 2, 1, -1, -2, -2, -1 };
            int[] dy = { 2, 1, -1, -2, -2, -1, 1, 2 };

            for (int i = 0; i < 8; i++)
            {
                int newX = currentPos.X + dx[i];
                int newY = currentPos.Y + dy[i];
                if (CanMoveTo(board, newX, newY))
                    moves.Add(new Position(newX, newY));
            }
            return moves;
        }
    }

    public class Rook : Piece
    {
        public Rook(PieceColor color) : base(color, PieceType.Rook) { }
        public override List<Position> GetLegalMoves(Piece?[,] board, Position currentPos)
        {
            var moves = new List<Position>();
            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { -1, 1, 0, 0 };

            for (int i = 0; i < 4; i++)
            {
                int step = 1;
                while (true)
                {
                    int newX = currentPos.X + (dx[i] * step);
                    int newY = currentPos.Y + (dy[i] * step);
                    if (!IsOnBoard(newX, newY)) break;

                    var target = board[newY, newX];
                    if (target == null)
                    {
                        moves.Add(new Position(newX, newY));
                    }
                    else
                    {
                        if (target.Color != Color) moves.Add(new Position(newX, newY));
                        break;
                    }
                    step++;
                }
            }
            return moves;
        }
    }

    public class Bishop : Piece
    {
        public Bishop(PieceColor color) : base(color, PieceType.Bishop) { }
        public override List<Position> GetLegalMoves(Piece?[,] board, Position currentPos)
        {
            var moves = new List<Position>();
            int[] dx = { 1, 1, -1, -1 };
            int[] dy = { -1, 1, 1, -1 };

            for (int i = 0; i < 4; i++)
            {
                int step = 1;
                while (true)
                {
                    int newX = currentPos.X + (dx[i] * step);
                    int newY = currentPos.Y + (dy[i] * step);
                    if (!IsOnBoard(newX, newY)) break;

                    var target = board[newY, newX];
                    if (target == null)
                    {
                        moves.Add(new Position(newX, newY));
                    }
                    else
                    {
                        if (target.Color != Color) moves.Add(new Position(newX, newY));
                        break;
                    }
                    step++;
                }
            }
            return moves;
        }
    }

    public class Queen : Piece
    {
        public Queen(PieceColor color) : base(color, PieceType.Queen) { }
        public override List<Position> GetLegalMoves(Piece?[,] board, Position currentPos)
        {
            var moves = new List<Position>();
            moves.AddRange(new Rook(Color).GetLegalMoves(board, currentPos));
            moves.AddRange(new Bishop(Color).GetLegalMoves(board, currentPos));
            return moves;
        }
    }

    public class King : Piece
    {
        public King(PieceColor color) : base(color, PieceType.King) { }
        public override List<Position> GetLegalMoves(Piece?[,] board, Position currentPos)
        {
            var moves = new List<Position>();
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int newX = currentPos.X + dx;
                    int newY = currentPos.Y + dy;
                    if (CanMoveTo(board, newX, newY))
                        moves.Add(new Position(newX, newY));
                }
            }
            return moves;
        }
    }
}