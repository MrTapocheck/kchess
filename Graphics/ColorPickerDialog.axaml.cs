using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace kchess.Graphics
{
    public partial class ColorPickerDialog : UserControl
    {
        public event EventHandler<Color>? ColorSelected;

        public ColorPickerDialog()
        {
            InitializeComponent();
            
            HueSlider.PropertyChanged += (s, e) => UpdateColorFromSliders();
            LightnessSlider.PropertyChanged += (s, e) => UpdateColorFromSliders();
            
            UpdateColorFromSliders();
        }

        public void SetInitialColor(Color color)
        {
            HexInput.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            // Примечание: слайдеры не синхронизируются обратно без сложной математики RGB->HSL,
            // но превью и HEX обновятся корректно при движении слайдеров.
        }

        private void UpdateColorFromSliders()
        {
            double h = HueSlider.Value;
            double l = LightnessSlider.Value / 100.0;
            double s = 1.0; 

            var color = HslToRgb(h, s, l);
            
            if (PreviewBox != null)
                PreviewBox.Background = new SolidColorBrush(color);
            
            if (HexInput != null)
                HexInput.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private void ApplyClick(object? sender, RoutedEventArgs e)
        {
            if (Color.TryParse(HexInput.Text, out var color))
            {
                ColorSelected?.Invoke(this, color);
            }
            ForceClose(); // Закрываем ВСЕГДА при нажатии кнопки
        }

        private void CancelClick(object? sender, RoutedEventArgs e)
        {
            ForceClose(); // Закрываем ВСЕГДА при отмене
        }

        // Универсальный метод уничтожения диалога
        private void ForceClose()
        {
            if (this.Parent is Panel parent)
            {
                parent.Children.Remove(this);
            }
            else if (this.Parent is Grid grid)
            {
                grid.Children.Remove(this);
            }
        }

        private Color HslToRgb(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = l - c / 2;
            double r = 0, g = 0, b = 0;

            if (h >= 0 && h < 60) { r = c; g = x; b = 0; }
            else if (h >= 60 && h < 120) { r = x; g = c; b = 0; }
            else if (h >= 120 && h < 180) { r = 0; g = c; b = x; }
            else if (h >= 180 && h < 240) { r = 0; g = x; b = c; }
            else if (h >= 240 && h < 300) { r = x; g = 0; b = c; }
            else if (h >= 300 && h < 360) { r = c; g = 0; b = x; }

            return new Color(255, 
                (byte)((r + m) * 255), 
                (byte)((g + m) * 255), 
                (byte)((b + m) * 255));
        }
    }
}