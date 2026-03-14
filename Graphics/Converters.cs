using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;
using kchess;

namespace kchess.Graphics
{
    /// <summary>
    /// Превращает объект фигуры в букву (P, N, K...).
    /// </summary>
    public class PieceToImagePathConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Piece piece)
            {
                string figCode = piece.Type switch
                {
                    PieceType.Pawn => "p",
                    PieceType.Knight => "n",
                    PieceType.Bishop => "b",
                    PieceType.Rook => "r",
                    PieceType.Queen => "q",
                    PieceType.King => "k",
                    _ => ""
                };

                if (string.IsNullOrEmpty(figCode))
                    return null;

                // l = light (white), d = dark (black)
                string colorCode = (piece.Color == PieceColor.White) ? "l" : "d";

                // ИСПРАВЛЕНО: убираем подчеркивание перед t60
                // Было: chess_{fig}{color}_t60.png
                // Стало: Chess_{fig}{color}t60.png (обрати внимание на большую C и отсутствие _)
                string fileName = $"Chess_{figCode}{colorCode}t60.png";

                return new Uri($"avares://kchess/Graphics/Assets/{fileName}");
            }
            return null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Превращает цвет фигуры в кисть для отрисовки текста.
    /// Делаем контраст: Белые - почти белые, Черные - почти черные.
    /// Чтобы белые были видны на светлом фоне, в MainWindow.axaml.cs мы добавим им тень.
    /// </summary>
    public class PieceColorToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Piece piece)
            {
                // Белые фигуры: Чисто белый (#FFFFFF)
                // Черные фигуры: Глубокий черный (#1A1A1A)
                return piece.Color == PieceColor.White 
                    ? new SolidColorBrush(Color.Parse("#FFFFFF")) 
                    : new SolidColorBrush(Color.Parse("#1A1A1A"));
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}