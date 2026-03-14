using System; // для Exception
using Avalonia; // Нужно для Thickness
using System.Collections.Generic; // Для List<T>
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Data; // Нужно для Bind
using Avalonia.Threading; //Нужно для Dispatch
using Avalonia.Input; // для маусботон
using kchess; 
using kchess.Graphics;

namespace kchess.Graphics
{
    public partial class MainWindow : Window
    {
        private readonly PieceToStringConverter _textConverter = new();
        private readonly PieceColorToBrushConverter _colorConverter = new();
        // Список всех клеток доски для ручного обновления
        private readonly List<Border> _cells = new List<Border>();

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
            _cells.Clear(); // Очищаем список

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
                        Tag = $"{currentX},{currentY}" // Сохраняем координаты в Tag
                    };

                    bool isDark = (currentX + currentY) % 2 == 1;
                    var cellColor = isDark ? Color.Parse("#769656") : Color.Parse("#F0D9B5");
                    cellBorder.Background = new SolidColorBrush(cellColor);

                    var pieceText = new TextBlock
                    {
                        Name = $"PieceText_{x}_{y}", // Дадим имя, чтобы найти потом
                        FontSize = 40,
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Background = Brushes.Transparent
                    };
                    
                    // Привязки оставляем для инициализации, но обновлять будем вручную
                    pieceText.Bind(TextBlock.TextProperty, new Binding($"Board[{currentY},{currentX}]") { Converter = _textConverter });
                    pieceText.Bind(TextBlock.ForegroundProperty, new Binding($"Board[{currentY},{currentX}]") { Converter = _colorConverter });

                    cellBorder.Child = pieceText;
                    cellBorder.PointerReleased += (s, e) => 
                    {
                        if (e.InitialPressMouseButton == MouseButton.Left)
                            OnCellClicked(currentX, currentY);
                    };
                    cellBorder.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);

                    grid.Children.Add(cellBorder);
                    _cells.Add(cellBorder); // СОХРАНЯЕМ ССЫЛКУ
                }
            }
        }

                // Метод для принудительного обновления содержимого клеток
        private void UpdateBoardVisuals()
        {
            var vm = this.DataContext as MainViewModel;
            if (vm == null) return;

            foreach (var cell in _cells)
            {
                // Получаем координаты из Tag
                var tag = cell.Tag?.ToString()?.Split(',');
                if (tag != null && tag.Length == 2)
                {
                    int x = int.Parse(tag[0]);
                    int y = int.Parse(tag[1]);
                    
                    var piece = vm.Board[y, x];
                    var textBlock = cell.Child as TextBlock;
                    
                    if (textBlock != null)
                    {
                        // Обновляем текст вручную
                        string text = "";
                        if (piece != null)
                        {
                            switch (piece.Type)
                            {
                                case PieceType.King: text = "K"; break;
                                case PieceType.Queen: text = "Q"; break;
                                case PieceType.Rook: text = "R"; break;
                                case PieceType.Bishop: text = "B"; break;
                                case PieceType.Knight: text = "N"; break;
                                case PieceType.Pawn: text = "P"; break;
                                default: text = "?"; break;
                            }
                        }
                        textBlock.Text = text;

                        // Обновляем цвет вручную
                        if (piece == null)
                        {
                            textBlock.Foreground = Brushes.Transparent;
                        }
                        else
                        {
                            textBlock.Foreground = (piece.Color == PieceColor.White) 
                                ? Brushes.White 
                                : Brushes.Black;
                        }
                    }
                }
            }
        }

        

        private void OnCellClicked(int x, int y)
        {
            var vm = this.DataContext as MainViewModel;
            if (vm == null) return;

            Console.WriteLine($"Клик по клетке: {x}, {y}. Выбрано ранее: {_selectedX}, {_selectedY}");

            // Если уже выбрана фигура, пытаемся сделать ход
            if (_selectedX.HasValue && _selectedY.HasValue)
            {
                // Если кликнули туда же - сброс
                if (_selectedX == x && _selectedY == y)
                {
                    Console.WriteLine("Сброс выбора (клик туда же).");
                    ClearSelection();
                    return;
                }

                Console.WriteLine($"Попытка хода из ({_selectedX}, {_selectedY}) в ({x}, {y})");
                
                // Пытаемся сделать ход
                vm.TryMakeMove(_selectedX.Value, _selectedY.Value, x, y);
                
                // ВАЖНО: Принудительно обновляем доску вручную!
                UpdateBoardVisuals(); 

                // Проверка на превращение
                if (vm.IsWaitingForPromotion)
                {
                    Console.WriteLine("Требуется превращение!");
                    ShowPromotionDialog(vm, x, y);
                    // Обновление внутри диалога произойдет позже
                }
                else
                {
                    // Если превращения нет, обновляем статус (хотя UpdateBoardVisuals уже сработал для фигур)
                    // Можно добавить подсветку выбранной клетки здесь, если нужно
                }

                // Всегда сбрасываем выделение после попытки хода
                ClearSelection();
                return;
            }

            // Если ничего не выбрано, выбираем фигуру
            var piece = vm.Board[y, x];
            
            if (piece == null)
            {
                Console.WriteLine("Клик по пустой клетке (ничего не выбрано). Игнорируем.");
                vm.SetStatus("Сначала выберите фигуру.");
                return;
            }

            // Проверка: свой ли цвет?
            bool isWhiteTurn = vm.CurrentTurnText.Contains("белых");
            PieceColor currentColor = isWhiteTurn ? PieceColor.White : PieceColor.Black;

            if (piece.Color == currentColor)
            {
                Console.WriteLine($"Выбрана своя фигура: {piece.Type}");
                _selectedX = x;
                _selectedY = y;
                vm.SetStatus($"Выбрана {piece.Type} ({x},{y}). Выберите клетку для хода.");
            }
            else
            {
                Console.WriteLine($"Клик по чужой фигуре: {piece.Type}. Игнорируем.");
                vm.SetStatus("Нельзя выбрать фигуру противника.");
            }
        }

        private void ShowPromotionDialog(MainViewModel vm, int x, int y)
        {
            // Авто-выбор ферзя через 1 секунду
            System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ => 
            {
                Dispatcher.UIThread.Post(() => 
                {
                    try 
                    {
                        vm.SelectPromotionPiece(PieceType.Queen);
                        
                        // ВАЖНО: Обновляем доску после превращения!
                        UpdateBoardVisuals(); 
                    }
                    catch (Exception ex)
                    {
                        vm.SetStatus($"Ошибка превращения: {ex.Message}");
                    }
                });
            });
            
            vm.SetStatus("Пешка превращается в Ферзя (авто)...");
        }

        private void ClearSelection()
        {
            _selectedX = null;
            _selectedY = null;
        }
    }
}