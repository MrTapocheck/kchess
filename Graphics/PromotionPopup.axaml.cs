using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using kchess.Models;

namespace kchess.Graphics
{
    public partial class PromotionPopup : UserControl
    {
        // Событие, которое сообщит главному окну, какую фигуру выбрали
        public event EventHandler<PieceType>? PieceSelected;

        private PieceColor _currentColor;

        public PromotionPopup()
        {
            InitializeComponent();
        }

        // Метод для настройки цвета фигур (Белые или Черные)
        public void Setup(PieceColor color)
        {
            _currentColor = color;
            string colorCode = (color == PieceColor.White) ? "l" : "d"; // l=light, d=dark

            // Обновляем картинки кнопок в зависимости от цвета
            UpdateImage(BtnQueen, $"Chess_q{colorCode}t60.png");
            UpdateImage(BtnRook, $"Chess_r{colorCode}t60.png");
            UpdateImage(BtnBishop, $"Chess_b{colorCode}t60.png");
            UpdateImage(BtnKnight, $"Chess_n{colorCode}t60.png");
        }

        private void UpdateImage(Button btn, string fileName)
        {
            if (btn.Content is Image img)
            {
                try
                {
                    var uri = new Uri($"avares://kchess/Graphics/Assets/{fileName}");
                    using var stream = Avalonia.Platform.AssetLoader.Open(uri);
                    img.Source = new Avalonia.Media.Imaging.Bitmap(stream);
                }
                catch { /* Игнорируем, если картинка не найдена */ }
            }
        }

        private void OnPieceSelected(object? sender, RoutedEventArgs e)
        {
            PieceType chosenType = PieceType.Pawn; // Заглушка

            if (sender == BtnQueen) chosenType = PieceType.Queen;
            else if (sender == BtnRook) chosenType = PieceType.Rook;
            else if (sender == BtnBishop) chosenType = PieceType.Bishop;
            else if (sender == BtnKnight) chosenType = PieceType.Knight;

            PieceSelected?.Invoke(this, chosenType);
        }
    }
}