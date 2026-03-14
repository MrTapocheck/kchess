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
    public class PieceToStringConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Piece piece)
            {
                char symbol = piece.Type switch
                {
                    PieceType.King => 'K',
                    PieceType.Queen => 'Q',
                    PieceType.Rook => 'R',
                    PieceType.Bishop => 'B',
                    PieceType.Knight => 'N',
                    PieceType.Pawn => 'P',
                    _ => '?'
                };
                return symbol.ToString();
            }
            return string.Empty;
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