using System; 
using System.Collections.Generic; 
using Avalonia; 
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging; // <--- ДОБАВЛЕНО: Нужно для Bitmap
using Avalonia.Data; 
using Avalonia.Threading; 
using Avalonia.Input; 
using kchess; 
using kchess.Graphics;
using Avalonia.Platform; // для ресурсов

namespace kchess.Graphics
{
    public partial class MainWindow : Window
    {
        // Старые конвертеры удалены, так как мы грузим картинки вручную
        
        private readonly List<Border> _cells = new List<Border>();
        // Список картинок можно не хранить отдельно, если мы берем их из cell.Child, но оставим для удобства
        private readonly List<Image> _images = new List<Image>(); 

        private int? _selectedX;
        private int? _selectedY;

        public MainWindow()
        {
            InitializeComponent();
            this.Opened += (s, e) => BuildChessBoard();
        }

        private void BuildChessBoard()
        {
            var grid = this.FindControl<Grid>("ChessBoardGrid");
            if (grid == null) return;

            grid.Children.Clear();
            _cells.Clear();
            _images.Clear();

            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int currentX = x;
                    int currentY = y;

                    var cellBorder = new Border
                    {
                        [Grid.ColumnProperty] = currentX,
                        [Grid.RowProperty] = currentY,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        Padding = new Thickness(0),
                        Tag = $"{currentX},{currentY}"
                    };

                    bool isDark = (currentX + currentY) % 2 == 1;
                    var cellColor = isDark ? Color.Parse("#769656") : Color.Parse("#F0D9B5");
                    cellBorder.Background = new SolidColorBrush(cellColor);

                    // Создаем Image вместо TextBlock
                    var pieceImage = new Image
                    {
                        Name = $"PieceImage_{x}_{y}",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Stretch = Stretch.Uniform,
                        MaxWidth = 50,
                        MaxHeight = 50
                    };

                    // ВАЖНО: МЫ УБРАЛИ ПРИВЯЗКУ (BINDING) ЧЕРЕЗ КОНВЕРТЕРЫ, ТАК КАК ОНИ УДАЛЕНЫ ИЛИ НЕ НУЖНЫ.
                    // МЫ БУДЕМ ОБНОВЛЯТЬ КАРТИНКИ ВРУЧНУЮ ЧЕРЕЗ UpdateBoardVisuals.
                    // Если хочешь оставить привязку для начальной отрисовки, нужно создать новый конвертер, 
                    // но ручное обновление надежнее для массивов.
                    // Поэтому просто оставляем pieceImage.Source = null (по умолчанию).

                    cellBorder.Child = pieceImage;
                    
                    cellBorder.PointerReleased += (s, e) => 
                    {
                        if (e.InitialPressMouseButton == MouseButton.Left)
                            OnCellClicked(currentX, currentY);
                    };
                    cellBorder.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);

                    grid.Children.Add(cellBorder);
                    _cells.Add(cellBorder);
                    _images.Add(pieceImage);
                }
            }
            
            // Первоначальная отрисовка фигур после создания доски
            UpdateBoardVisuals();
        }

        private void UpdateBoardVisuals()
        {
            var vm = this.DataContext as MainViewModel;
            if (vm == null) return;

            foreach (var cell in _cells)
            {
                var tag = cell.Tag?.ToString()?.Split(',');
                if (tag != null && tag.Length == 2)
                {
                    int x = int.Parse(tag[0]);
                    int y = int.Parse(tag[1]);
                    
                    var piece = vm.Board[y, x];
                    var image = cell.Child as Image;
                    
                    if (image != null)
                    {
                        if (piece == null)
                        {
                            image.Source = null;
                        }
                        else
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

                            string colorCode = (piece.Color == PieceColor.White) ? "l" : "d";

                            if (!string.IsNullOrEmpty(figCode))
                            {
                                string fileName = $"Chess_{figCode}{colorCode}t60.png";
                                
                                try 
                                {
                                    // Формируем относительный путь внутри сборки
                                    // Путь должен начинаться со слэша и указывать на папку Assets
                                    string assetPath = $"/Graphics/Assets/{fileName}";
                                    
                                    // Создаем URI для ресурса
                                    var uri = new Uri($"avares://kchess{assetPath}");
                                    
                                    // Открываем поток через AssetLoader
                                    using var stream = AssetLoader.Open(uri);
                                    
                                    // Загружаем Bitmap из потока
                                    image.Source = new Bitmap(stream);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Ошибка загрузки картинки {fileName}: {ex.Message}");
                                    image.Source = null;
                                }
                            }
                            else
                            {
                                image.Source = null;
                            }
                        }
                    }
                }
            }
        }

        private void OnCellClicked(int x, int y)
        {
            var vm = this.DataContext as MainViewModel;
            if (vm == null) return;

            if (_selectedX.HasValue && _selectedY.HasValue)
            {
                if (_selectedX == x && _selectedY == y)
                {
                    ClearSelection();
                    return;
                }

                vm.TryMakeMove(_selectedX.Value, _selectedY.Value, x, y);
                
                UpdateBoardVisuals();

                if (vm.IsWaitingForPromotion)
                {
                    ShowPromotionDialog(vm, x, y);
                }

                ClearSelection();
                return;
            }

            var piece = vm.Board[y, x];
            bool isWhiteTurn = vm.CurrentTurnText.Contains("белых");
            PieceColor currentColor = isWhiteTurn ? PieceColor.White : PieceColor.Black;

            if (piece != null && piece.Color == currentColor)
            {
                _selectedX = x;
                _selectedY = y;
                vm.SetStatus($"Выбрана {piece.Type}. Куда ходим?");
            }
            else
            {
                vm.SetStatus(piece == null ? "Выберите фигуру." : "Это фигура противника.");
            }
        }

        private void ShowPromotionDialog(MainViewModel vm, int x, int y)
        {
            System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ => 
            {
                Dispatcher.UIThread.Post(() => 
                {
                    try 
                    {
                        vm.SelectPromotionPiece(PieceType.Queen);
                        UpdateBoardVisuals();
                    }
                    catch (Exception ex)
                    {
                        vm.SetStatus($"Ошибка: {ex.Message}");
                    }
                });
            });
            vm.SetStatus("Превращение в Ферзя...");
        }

        private void ClearSelection()
        {
            _selectedX = null;
            _selectedY = null;
        }
    }
}