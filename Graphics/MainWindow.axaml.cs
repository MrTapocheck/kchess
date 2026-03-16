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
using Avalonia.Interactivity;
using System.Collections.Generic; // Для List<>
using System.Linq;                // Для FirstOrDefault()

namespace kchess.Graphics
{
    public partial class MainWindow : Window
    {
        private bool _isWhitePerspective = true;
        private readonly List<Border> _cells = new List<Border>();
        private readonly List<Image> _images = new List<Image>(); 
        private int? _selectedX;
        private int? _selectedY;

        //для подсветки
        private List<(int x, int y)> _possibleMoves = new List<(int, int)>(); 
        public Color HighlightColor { get; set; } = Color.Parse("#FFFF00");

        // Таймер для проверки состояния мыши (Polling)
        private Timer? _hoverTimer;
        private Button? _settingsBtn;
        private Popup? _settingsPopup;

        // Методы навигации
        private void ShowMainMenu()
        {
            MainMenuPanel.IsVisible = true;
            SetupPanel.IsVisible = false;
            GamePanel.IsVisible = false;
        }

        // Обработчик кнопки "Выход" в главном меню
        private void ExitApp_Click(object? sender, RoutedEventArgs e)
        {
            // Закрываем главное окно, что завершает работу приложения
            this.Close();
            
            // Альтернативный способ (чтобы прям принудительно закрыть):
            // Application.Current!.ApplicationLifetime?.Shutdown();
        }   

        private void ShowSetup()
        {
            MainMenuPanel.IsVisible = false;
            SetupPanel.IsVisible = true;
            GamePanel.IsVisible = false;
        }

        private void StartGame(bool isWhite)
        {
            _isWhitePerspective = isWhite;
            
            // Инициализируем новую игру в ViewModel
            var vm = this.DataContext as MainViewModel;
            vm?.NewGame();

            // Перерисовываем доску с учетом перспективы
            BuildChessBoard(_isWhitePerspective);

            // Переключаем интерфейс
            SetupPanel.IsVisible = false;
            GamePanel.IsVisible = true;
        }
        // Обработчик кнопки "В главное меню" из игры
        private void BackToMenuFromGame_Click(object? sender, RoutedEventArgs e)
        {
            // Спрашиваем подтверждение? Пока просто выходим.
            // Можно добавить диалог: "Вы уверены? Партия будет потеряна."
            
            ShowMainMenu();
            
            // Опционально: сбросить игру
            var vm = this.DataContext as MainViewModel;
            vm?.NewGame();
        }

        // Обработчики кнопок
        private void StartLocalFriend_Click(object? sender, RoutedEventArgs e) => ShowSetup();
        
        private void ChooseWhite_Click(object? sender, RoutedEventArgs e) => StartGame(true);
        private void ChooseBlack_Click(object? sender, RoutedEventArgs e) => StartGame(false);
        
        private void BackToMenu_Click(object? sender, RoutedEventArgs e) => ShowMainMenu();
        
        private void StartVsAi_Click(object? sender, RoutedEventArgs e) 
        { 
            // Пока просто запускаем за белых, потом добавим выбор сложности
            StartGame(true); 
        }

        private void ShowNetworkMenu_Click(object? sender, RoutedEventArgs e)
        {
            // Тут позже откроем окно ввода IP
            System.Console.WriteLine("Функция сети в разработке!");
        }
        
        // ВАЖНО: В конструкторе сразу покажи меню
        public MainWindow()
        {
            InitializeComponent();
            ShowMainMenu(); // Скрываем всё, показываем меню
            // Убираем this.Opened += BuildChessBoard, так как теперь рисуем доску только при старте игры
        }

        // Метод открытия  универсального диалога
        private void OpenHighlightColorPicker_Click(object? sender, RoutedEventArgs e)
        {
            if (SettingsPopup != null) SettingsPopup.IsOpen = false;

            var picker = new ColorPickerDialog();
            picker.SetInitialColor(HighlightColor);

            // Просто слушаем цвет. Диалог сам закроется внутри себя при нажатии кнопок.
            picker.ColorSelected += (s, color) =>
            {
                HighlightColor = color;
                UpdateSelectionBorderColor();
            };

            if (this.Content is Grid mainGrid)
            {
                mainGrid.Children.Add(picker);
            }
        } 

        // Метод обновления цвета уже существующих рамок на доске
        private void UpdateSelectionBorderColor()
        {
            foreach (var cell in _cells)
            {
                if (cell.Child is Grid gridContainer)
                {
                    // Ищем бордер рамки внутри клетки
                    var border = gridContainer.Children.FirstOrDefault(c => c is Border b && b.Name == "SelectionBorder") as Border;
                    if (border != null)
                    {
                        border.BorderBrush = new SolidColorBrush(HighlightColor);
                    }
                }
            }
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

        // Добавляем параметр isWhitePerspective
        private void BuildChessBoard(bool isWhitePerspective = true)
        {
            var grid = this.FindControl<Grid>("ChessBoardGrid");
            if (grid == null) return;

            grid.Children.Clear();
            _cells.Clear();
            _images.Clear();

            const int BoardSize = 8;
            var DarkCoordColor = Color.Parse("#F0D9B5"); 
            var LightCoordColor = Color.Parse("#769656"); 
            
            string[] files = { "a", "b", "c", "d", "e", "f", "g", "h" };

            // ... (очистка Column/Row Definitions остается той же) ...
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            for (int i = 0; i < BoardSize; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            }

            for (int y = 0; y < BoardSize; y++)
            {
                for (int x = 0; x < BoardSize; x++)
                {
                    // === ЛОГИКА ПЕРЕВОРОТА ===
                    // Если играем за белых: x=0..7, y=0..7 (стандарт)
                    // Если за черных: нам нужно инвертировать координаты отрисовки
                    int boardX = isWhitePerspective ? x : (BoardSize - 1 - x);
                    int boardY = isWhitePerspective ? y : (BoardSize - 1 - y);
                    
                    // Но логика шахматного движка всегда работает от 0 до 7 относительно массива Board[8,8]
                    // Где Board[0,0] - это a8 (черные), Board[7,7] - h1 (белые).
                    // Нам нужно мапить визуальные координаты (x,y в цикле) на логические (logicX, logicY).
                    
                    int logicX = isWhitePerspective ? x : (BoardSize - 1 - x);
                    int logicY = isWhitePerspective ? y : (BoardSize - 1 - y);

                    var cellBorder = new Border
                    {
                        [Grid.ColumnProperty] = x, // Визуальная позиция в Grid
                        [Grid.RowProperty] = y,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        Tag = $"{logicX},{logicY}" // Сохраняем ЛОГИЧЕСКИЕ координаты для кликов!
                    };

                    bool isDark = (logicX + logicY) % 2 == 1; // Цвет клетки зависит от логики
                    var cellColor = isDark ? Color.Parse("#769656") : Color.Parse("#F0D9B5");
                    cellBorder.Background = new SolidColorBrush(cellColor);

                    var contentGrid = new Grid();
                    cellBorder.Child = contentGrid;

                    var coordColor = isDark ? DarkCoordColor : LightCoordColor;
                    var brush = new SolidColorBrush(coordColor);

                    // === ОТРИСОВКА КООРДИНАТ ===
                    // Буквы: должны быть снизу относительно игрока.
                    // Если белые: снизу это y=7. Если черные: снизу это y=7 (визуально), но логически это 0-я горизонталь.
                    // Проще: рисуем буквы на последнем визуальном ряду (y == 7) и цифры на последнем визуальном столбце (x == 7).
                    
                    if (y == BoardSize - 1) 
                    {
                        // Какую букву писать?
                        // Если белые: x=0 -> 'a'. Если черные: x=0 (визуально h1) -> 'h'.
                        char fileChar = isWhitePerspective ? files[logicX][0] : files[logicX][0]; 
                        // Стоп, logicX уже инвертирован. 
                        // Если x=0 (слева визуально для черных), то logicX=7 ('h'). Значит files[7] = 'h'. Всё верно!
                        
                        var fileLabel = new TextBlock
                        {
                            Text = files[logicX].ToString(), // Используем logicX для выбора буквы
                            FontSize = 12, FontWeight = FontWeight.Bold, Foreground = brush,
                            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                            Margin = new Thickness(2, 0, 0, 2), IsHitTestVisible = false
                        };
                        contentGrid.Children.Add(fileLabel);
                    }

                    if (x == BoardSize - 1)
                    {
                        // Какую цифру писать?
                        // Если белые: y=0 -> 8. Если черные: y=0 (визуально верх) -> 1.
                        // logicY уже инвертирован. Rank = 8 - logicY.
                        int rankNumber = BoardSize - logicY;
                        
                        var rankLabel = new TextBlock
                        {
                            Text = rankNumber.ToString(),
                            FontSize = 12, FontWeight = FontWeight.Bold, Foreground = brush,
                            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(0, 2, 2, 0), IsHitTestVisible = false
                        };
                        contentGrid.Children.Add(rankLabel);
                    }
                    
                    var pieceImage = new Image
                    {
                        Name = $"PieceImage_{x}_{y}", // Имя зависит от визуальных координат, чтобы находить в UpdateVisuals
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        Stretch = Stretch.Uniform, IsHitTestVisible = false
                    };
                    contentGrid.Children.Add(pieceImage);

                    cellBorder.PointerReleased += (s, e) =>
                    {
                        if (e.InitialPressMouseButton == MouseButton.Left)
                            OnCellClicked(logicX, logicY); // Передаем ЛОГИЧЕСКИЕ координаты в движок!
                    };
                    cellBorder.Cursor = new Cursor(StandardCursorType.Hand);

                    grid.Children.Add(cellBorder);
                    _cells.Add(cellBorder);
                    _images.Add(pieceImage);
                }
            }

            UpdateBoardVisuals();
        }
     
        private void UpdateBoardVisuals()
        {
            var vm = this.DataContext as MainViewModel;
            if (vm == null) return;

            foreach (var cell in _cells)
            {
                var tag = cell.Tag?.ToString()?.Split(',');
                if (tag == null || tag.Length != 2) continue;

                int x = int.Parse(tag[0]);
                int y = int.Parse(tag[1]);
                var piece = vm.Board[y, x];

                // Находим Grid внутри клетки
                if (cell.Child is not Grid gridContainer) continue;

                // --- 1. УПРАВЛЕНИЕ КОНТУРОМ (Желтая рамка) ---
                Border? selectionBorder = gridContainer.Children.FirstOrDefault(c => c is Border b && b.Name == "SelectionBorder") as Border;
                
                if (selectionBorder == null)
                {
                    // Создаем, если нет
                    selectionBorder = new Border
                    {
                        Name = "SelectionBorder",
                        BorderThickness = new Thickness(4),
                        BorderBrush = new SolidColorBrush(HighlightColor), // переменная цвета
                        IsHitTestVisible = false, // Чтобы не мешал кликам
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };
                    // Вставляем самым первым (поверх фона клетки, но под фигурами)
                    gridContainer.Children.Insert(0, selectionBorder);
                }

                // Показываем только если эта клетка выбрана
                selectionBorder.IsVisible = (_selectedX == x && _selectedY == y);


                // --- 2. УПРАВЛЕНИЕ ПРИЗРАКОМ (Полупрозрачная фигура) ---
                Image? ghostImage = gridContainer.Children.FirstOrDefault(c => c is Image i && i.Name.StartsWith("Ghost")) as Image;

                bool isPossibleMove = _possibleMoves.Any(m => m.x == x && m.y == y);

                if (isPossibleMove)
                {
                    if (ghostImage == null)
                    {
                        // Создаем призрака
                        ghostImage = new Image
                        {
                            Name = $"Ghost_{x}_{y}",
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Stretch = Stretch.Uniform,
                            IsHitTestVisible = false,
                            Opacity = 0.5 // Полупрозрачность 50%
                        };
                        gridContainer.Children.Add(ghostImage);
                    }

                    // Берем тип фигуры ИЗ ВЫБРАННОЙ КЛЕТКИ (той, которой ходим)
                    var selectedPiece = vm.Board[_selectedY!.Value, _selectedX!.Value];
                    if (selectedPiece != null)
                    {
                        LoadPieceImage(ghostImage, selectedPiece);
                        ghostImage.IsVisible = true;
                        
                        // Если на целевой клетке стоит СВОЯ фигура, скрываем призрака (туда нельзя ходить)
                        if (piece != null && piece.Color == selectedPiece.Color)
                        {
                            ghostImage.IsVisible = false;
                        }
                        // Если там враг — призрак виден (сигнал атаки)
                    }
                    else
                    {
                        ghostImage.IsVisible = false;
                    }
                }
                else
                {
                    if (ghostImage != null) ghostImage.IsVisible = false;
                }


                // --- 3. УПРАВЛЕНИЕ РЕАЛЬНОЙ ФИГУРОЙ ---
                Image? realImage = gridContainer.Children.FirstOrDefault(c => c is Image i && i.Name.StartsWith("PieceImage_")) as Image;
                
                if (realImage != null)
                {
                    if (piece != null)
                    {
                        LoadPieceImage(realImage, piece);
                        realImage.IsVisible = true;
                        
                        // Опционально: можно скрывать реальную фигуру, если на ней стоит призрак (для красоты), 
                        // но лучше оставить как есть.
                    }
                    else
                    {
                        realImage.IsVisible = false;
                        realImage.Source = null;
                    }
                }
            }
        }

        // Вспомогательный метод загрузки картинки (чтобы не дублировать код)
        private void LoadPieceImage(Image image, Piece piece)
        {
            if (piece == null) { image.Source = null; return; }

            string figCode = piece.Type switch
            {
                PieceType.Pawn => "p", PieceType.Knight => "n", PieceType.Bishop => "b",
                PieceType.Rook => "r", PieceType.Queen => "q", PieceType.King => "k", _ => ""
            };
            string colorCode = (piece.Color == PieceColor.White) ? "l" : "d";
            string fileName = $"Chess_{figCode}{colorCode}t60.png";

            try
            {
                string assetPath = $"/Graphics/Assets/{fileName}";
                var uri = new Uri($"avares://kchess{assetPath}");
                using var stream = AssetLoader.Open(uri);
                image.Source = new Bitmap(stream);
            }
            catch { image.Source = null; }
        }

        private void OnCellClicked(int x, int y)
        {
            var vm = this.DataContext as MainViewModel;
            if (vm == null) return;

            // 1. Если уже есть выбор и мы кликнули на возможный ход -> ХОДИМ
            if (_selectedX.HasValue && _selectedY.HasValue)
            {
                // Проверяем, есть ли клик в списке возможных ходов
                bool isMoveValid = _possibleMoves.Any(m => m.x == x && m.y == y);

                if (isMoveValid)
                {
                    // Делаем ход
                    vm.TryMakeMove(_selectedX.Value, _selectedY.Value, x, y);
                    
                    // Сбрасываем выделение
                    ClearSelection();
                    UpdateBoardVisuals(); // Перерисовываем доску (фигуры переместились)
                    
                    // Проверка на превращение пешки (если нужно)
                    if (vm.IsWaitingForPromotion) 
                    {
                        ShowPromotionDialog(vm, x, y);
                    }
                    return;
                }

                // Если кликнули не на ход, а на другую свою фигуру -> МЕНЯЕМ ВЫБОР
                var piece = vm.Board[y, x];
                bool isMyTurn = vm.CurrentTurnText.Contains("белых"); // Или лучше через Enum
                PieceColor myColor = isMyTurn ? PieceColor.White : PieceColor.Black;

                if (piece != null && piece.Color == myColor)
                {
                    _selectedX = x;
                    _selectedY = y;
                    _possibleMoves = vm.GetLegalMoves(x, y); // Запрашиваем ходы у движка
                    UpdateBoardVisuals(); // Рисуем призраков
                    return;
                }

                // Кликнули в пустоту или на врага (но это не ход) -> СБРОС
                ClearSelection();
                UpdateBoardVisuals();
                return;
            }

            // 2. Если ничего не выбрано -> ПЫТАЕМСЯ ВЫБРАТЬ
            var currentPiece = vm.Board[y, x];
            bool isCurrentMyTurn = vm.CurrentTurnText.Contains("белых");
            PieceColor currentMyColor = isCurrentMyTurn ? PieceColor.White : PieceColor.Black;

            if (currentPiece != null && currentPiece.Color == currentMyColor)
            {
                _selectedX = x;
                _selectedY = y;
                _possibleMoves = vm.GetLegalMoves(x, y);
                
                vm.SetStatus($"Выбрана {currentPiece.Type}. Куда ходим?");
                UpdateBoardVisuals();
            }
            else
            {
                vm.SetStatus(currentPiece == null ? "Выберите фигуру." : "Это фигура противника.");
            }
        }

        private void ClearSelection()
        {
            _selectedX = null;
            _selectedY = null;
            _possibleMoves.Clear();
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
    }
}