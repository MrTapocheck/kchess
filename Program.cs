// тестовое
// позже переписать в тесты
// запускать с аргументом --selftest-castle
// проверяет, что рокировка работает корректно в разных ситуациях

using System;
using Avalonia;
using kchess.Graphics;

namespace kchess
{
    public class Program
    {
        // запуск авалонии
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest-castle")
            {
                RunCastleSelfTest();
                return;
            }

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        private static void RunCastleSelfTest()
        {
            var e = new ChessEngine();
            e.Board[7, 5] = null;
            e.Board[7, 6] = null;
            bool okK = e.TryMove(4, 7, 6, 7);
            Console.WriteLine($"Kingside empty-path TryMove={okK} status={e.LastStatus}");
            Console.WriteLine($"g1={e.Board[7, 6]?.Type} f1={e.Board[7, 5]?.Type} e1={e.Board[7, 4]?.Type} h1={e.Board[7, 7]?.Type}");

            e.InitializeBoard();
            e.Board[7, 1] = null;
            e.Board[7, 2] = null;
            e.Board[7, 3] = null;
            bool okQ = e.TryMove(4, 7, 2, 7);
            Console.WriteLine($"Queenside empty-path TryMove={okQ} status={e.LastStatus}");

            e.InitializeBoard();
            PrintMove(e, 4, 6, 4, 4, "e4");
            PrintMove(e, 4, 1, 4, 3, "e5");
            PrintMove(e, 6, 7, 5, 5, "Nf3");
            PrintMove(e, 1, 0, 2, 2, "Nc6");
            PrintMove(e, 5, 7, 2, 4, "Bc4");
            PrintMove(e, 5, 0, 2, 3, "Bc5");
            Console.WriteLine($"flags K={e._whiteKingMoved} rookK={e._whiteRookKingsideMoved} inCheck={e.IsKingInCheck(PieceColor.White)}");
            Console.WriteLine($"f1={e.Board[7,5]?.Type} g1={e.Board[7,6]?.Type} attf1={e.IsSquareAttacked(5,7,PieceColor.Black)} attg1={e.IsSquareAttacked(6,7,PieceColor.Black)}");
            bool castle = e.TryMove(4, 7, 6, 7);
            Console.WriteLine($"Italian O-O TryMove={castle} status={e.LastStatus}");
            Console.WriteLine($"g1={e.Board[7,6]?.Type} f1={e.Board[7,5]?.Type} e1={e.Board[7,4]?.Type} h1={e.Board[7,7]?.Type}");
        }

        private static void PrintMove(ChessEngine e, int fx, int fy, int tx, int ty, string name)
        {
            bool ok = e.TryMove(fx, fy, tx, ty);
            Console.WriteLine($"{name}: {ok} | {e.LastStatus}");
        }

        // Конфигуратор авалонии
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}