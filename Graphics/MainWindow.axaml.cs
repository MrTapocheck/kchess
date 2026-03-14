using System; 
using System.Collections.Generic; 
using Avalonia; 
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Data; 
using Avalonia.Threading; 
using Avalonia.Input; 
using kchess; 
using kchess.Graphics;
using Avalonia.Platform;
using System.Threading; // Для Timer

namespace kchess.Graphics
{
    public partial class MainWindow : Window
    {
        private readonly List<Border> _cells = new List<Border>();
        private readonly List<Image> _images = new List<Image>(); 
        private int? _selectedX;
        private int? _selectedY;

        // Таймер для проверки состояния мыши (Polling)
        private Timer? _hoverTimer;
        private Button? _settingsBtn;
        private Popup? _settingsPopup;

        public MainWindow()
        {
            InitializeComponent();
            this.Opened += (s, e) => BuildChessBoard();
            
            // Вся логика меню теперь в XAML через Binding!
            // Ничего добавлять сюда не нужно.
        }

        private void InitializeSettingsLogic()
        {
            _settingsBtn = this.FindControl<Button>("SettingsButton");
            _settingsPopup = this.FindControl<Popup>("SettingsPopup");

            if (_settingsBtn != null && _settingsPopup != null)
            {
                // Логика открытия (по событию - это быстро)
                _settingsBtn.PointerEntered += (s, e) => OpenMenu();
                _settingsPopup.PointerEntered += (s, e) => OpenMenu();

                // Запускаем фоновый таймер, который проверяет состояние мыши каждые 100мс
                _hoverTimer = new Timer(CheckHoverState, null, 100, 100);
            }
        }

        private void OpenMenu()
        {
            if (_settingsPopup != null)
                _settingsPopup.IsOpen = true;
        }

        private void CheckHoverState(object? state)
        {
            // Выполняем проверку в UI потоке
            Dispatcher.UIThread.Post(() => 
            {
                if (_settingsPopup == null || _settingsBtn == null) return;

                // 1. Если главное окно свернуто или не активно (опционально, но полезно)
                // if (!this.IsActive) { _settingsPopup.IsOpen = false; return; } 
                // ^ Закомментировал, так как IsActive может глючить при наведении, 
                // но если хочешь закрытие при сворачивании окна - раскомментируй.
                
                // Проверка на сворачивание окна (WindowState)
                if (this.WindowState == WindowState.Minimized)
                {
                    _settingsPopup.IsOpen = false;
                    return;
                }

                // 2. ГЛАВНАЯ ПРОВЕРКА:
                // Если меню открыто, проверяем, находится ли мышь НАД кнопкой ИЛИ НАД меню.
                if (_settingsPopup.IsOpen)
                {
                    bool isOverButton = _settingsBtn.IsPointerOver;
                    bool isOverPopup = _settingsPopup.IsPointerOver;

                    // Если мыши нет нигде -> ЗАКРЫВАЕМ
                    if (!isOverButton && !isOverPopup)
                    {
                        _settingsPopup.IsOpen = false;
                    }
                }
            });
        }

        private void BuildChessBoard()
        {
            // ... (твой старый код BuildChessBoard без изменений) ...
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
                    var pieceImage = new Image
                    {
                        Name = $"PieceImage_{x}_{y}",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Stretch = Stretch.Uniform,
                        MaxWidth = 50,
                        MaxHeight = 50
                    };
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
            UpdateBoardVisuals();
        }

        // ... (Остальные твои методы: UpdateBoardVisuals, OnCellClicked и т.д. остаются БЕЗ ИЗМЕНЕНИЙ) ...
        // Просто скопируй их из своего файла ниже сюда.
        
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
                        if (piece == null) { image.Source = null; }
                        else
                        {
                            string figCode = piece.Type switch
                            {
                                PieceType.Pawn => "p", PieceType.Knight => "n", PieceType.Bishop => "b",
                                PieceType.Rook => "r", PieceType.Queen => "q", PieceType.King => "k", _ => ""
                            };
                            string colorCode = (piece.Color == PieceColor.White) ? "l" : "d";
                            if (!string.IsNullOrEmpty(figCode))
                            {
                                string fileName = $"Chess_{figCode}{colorCode}t60.png";
                                try 
                                {
                                    string assetPath = $"/Graphics/Assets/{fileName}";
                                    var uri = new Uri($"avares://kchess{assetPath}");
                                    using var stream = AssetLoader.Open(uri);
                                    image.Source = new Bitmap(stream);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Ошибка загрузки картинки {fileName}: {ex.Message}");
                                    image.Source = null;
                                }
                            }
                            else { image.Source = null; }
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
                if (_selectedX == x && _selectedY == y) { ClearSelection(); return; }
                vm.TryMakeMove(_selectedX.Value, _selectedY.Value, x, y);
                UpdateBoardVisuals();
                if (vm.IsWaitingForPromotion) ShowPromotionDialog(vm, x, y);
                ClearSelection();
                return;
            }
            var piece = vm.Board[y, x];
            bool isWhiteTurn = vm.CurrentTurnText.Contains("белых");
            PieceColor currentColor = isWhiteTurn ? PieceColor.White : PieceColor.Black;
            if (piece != null && piece.Color == currentColor)
            {
                _selectedX = x; _selectedY = y;
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
                    try { vm.SelectPromotionPiece(PieceType.Queen); UpdateBoardVisuals(); }
                    catch (Exception ex) { vm.SetStatus($"Ошибка: {ex.Message}"); }
                });
            });
            vm.SetStatus("Превращение в Ферзя...");
        }

        private void NewGame_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var vm = this.DataContext as MainViewModel;
            if (vm != null) { vm.NewGame(); UpdateBoardVisuals(); }
        }

        private void ClearSelection() { _selectedX = null; _selectedY = null; }
    }
}