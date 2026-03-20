using System; 
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
using kchess.Models; 
using kchess.Services; 

namespace kchess.Graphics
{
    public partial class MainWindow : Window
    {
        private AppSettings _settings; 

        // Переменные состояния для игры с ботом
        private bool _isVsAi = false; // true если игра против ИИ
        private bool _isNetworkHost = false; // True если создаем сервер
        private string? _selectedDifficulty = null; // "Easy", "Medium", "Hard"
        private PieceColor _playerColorForAi = PieceColor.White;

        private readonly List<Border> _cells = new List<Border>();
        private readonly List<Image> _images = new List<Image>(); 
        private int? _selectedX;
        private int? _selectedY;

        // для подсветки
        private List<(int x, int y)> _possibleMoves = new List<(int, int)>(); 
        public Color HighlightColor { get; set; } = Color.Parse("#FFFF00");

        public MainWindow()
        {
            // Загрузка настроек
            _settings = SettingsService.Load();
            HighlightColor = _settings.GetHighlightColor();            
            InitializeComponent();
            
            // Строим доску сразу при запуске
            BuildChessBoard(); 
            // Показываем главное меню
            ShowMainMenu(); 
        }

        // ГЛАВНОЕ МЕНЮ: СЕТЬ 
        private void CreateNetworkGame_Click(object? sender, RoutedEventArgs e)
        {
            _isNetworkHost = true;
            // переход на выбор стороны
            ShowSideSelection("Режим: Онлайн (Хост)\nВыберите вашу сторону");
        }

        private void JoinNetworkGame_Click(object? sender, RoutedEventArgs e)
        {
            _isNetworkHost = false;
            // на экран ввода IP
            ShowJoinPanel();
        }

        // ПЕРЕХОДЫ 
        private void ShowJoinPanel()
        {
            MainMenuPanel.IsVisible = false;
            AiDifficultyPanel.IsVisible = false;
            HostSetupPanel.IsVisible = false;
            SetupPanel.IsVisible = false;
            GamePanel.IsVisible = false;
            
            JoinSetupPanel.IsVisible = true;
        }

        private void ShowHostSetupPanel()
        {
            MainMenuPanel.IsVisible = false;
            AiDifficultyPanel.IsVisible = false;
            SetupPanel.IsVisible = false;
            GamePanel.IsVisible = false;
            
            HostSetupPanel.IsVisible = true;
        }

        // Методы навигации
        private void ShowMainMenu()
        {
            MainMenuPanel.IsVisible = true;
            SetupPanel.IsVisible = false;
            GamePanel.IsVisible = false;

        }

        // МЕНЮ ВЫБОРА СЛОЖНОСТИ 
        private void SelectAiDifficulty_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string difficulty)
            {
                _selectedDifficulty = difficulty;
                TryStartAiGame();
            }
        }

        private void TryStartAiGame()
        {
            var vm = this.DataContext as MainViewModel;
            vm?.SetStatus($"Режим ИИ ({_selectedDifficulty}) в разработке!");
            /* на будущее
               StartGame(isVsAi: true, playerIsWhite: (_playerColorForAi == PieceColor.White));
            */
        }       

        // МЕНЮ ВЫБОРА СТОРОНЫ (УНИВЕРСАЛЬНОЕ)
        private void ShowSideSelection(string title)
        {
            SetupTitleText.Text = title;
            
            MainMenuPanel.IsVisible = false;
            AiDifficultyPanel.IsVisible = false;
            SetupPanel.IsVisible = true;
            GamePanel.IsVisible = false;
        }
        
        private void ChooseSide_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorTag)
            {
                bool playAsWhite = (colorTag == "White");
                _playerColorForAi = playAsWhite ? PieceColor.White : PieceColor.Black;

                if (_isNetworkHost)
                {
                    // Хост выбрал сторону -> идем на экран настройки сервера
                    ShowHostSetupPanel();
                }
                else if (_isVsAi)
                {
                    // ИИ -> идем на сложность
                    ShowAiDifficultySelection();
                }
                else
                {
                    // Локальная игра -> старт
                    StartGame(isVsAi: false, playerIsWhite: playAsWhite);
                }
            }
        }

        // Обработчик кнопки "Выход" в главном меню
        private void ExitApp_Click(object? sender, RoutedEventArgs e) => Close(); 

        private void ShowSetup()
        {
            MainMenuPanel.IsVisible = false;
            SetupPanel.IsVisible = true;
            GamePanel.IsVisible = false;
        }

        private void StartGame(bool isVsAi, bool playerIsWhite)
        {
            var vm = this.DataContext as MainViewModel;
            if (vm == null) return;

            // Сброс игры
            vm.NewGame();
            vm.SetStatus($"Игра началась! Режим: {(isVsAi ? "ИИ" : "Друг")}");

            // Жесткое переключение видимости
            MainMenuPanel.IsVisible = false;
            AiDifficultyPanel.IsVisible = false;
            SetupPanel.IsVisible = false;
            
            GamePanel.IsVisible = true;

            if (ChessBoardGrid == null)
            {
                vm.SetStatus("ОШИБКА: ChessBoardGrid не найден!");
                return;
            }

            // Принудительная перерисовка
            UpdateBoardVisuals();
            
            // Фокус на окно (иногда помогает)
            this.Activate();
        }
        
        // Обработчик кнопки "В главное меню" из игры
        private void BackToMenuFromGame_Click(object? sender, RoutedEventArgs e) => BackToMenu_Click(sender, e);

        // Обработчики кнопок
        private void StartLocalFriend_Click(object? sender, RoutedEventArgs e)
        {
            _isVsAi = false;
            ShowSideSelection("Режим: Игра с другом\nВыберите сторону");
        }
        
        private void BackToMenu_Click(object? sender, RoutedEventArgs e)
        {
            // СБРОС ВСЕХ ФЛАГОВ СОСТОЯНИЯ
            _isVsAi = false;
            _isNetworkHost = false;
            _selectedDifficulty = null;
            
            // СКРЫВАЕМ ВСЕ ПАНЕЛИ
            MainMenuPanel.IsVisible = false;
            AiDifficultyPanel.IsVisible = false;
            HostSetupPanel.IsVisible = false;
            SetupPanel.IsVisible = false;
            GamePanel.IsVisible = false;
            JoinSetupPanel.IsVisible = false;

            // ПОКАЗЫВАЕМ ГЛАВНОЕ МЕНЮ
            MainMenuPanel.IsVisible = true;
            
            var vm = this.DataContext as MainViewModel;
            vm?.SetStatus("Главное меню");
        }
        
        private void BackToSideSelection_Click(object? sender, RoutedEventArgs e)
        {
            // Скрываем лишние панели
            MainMenuPanel.IsVisible = false;
            AiDifficultyPanel.IsVisible = false;
            HostSetupPanel.IsVisible = false;
            JoinSetupPanel.IsVisible = false;
            GamePanel.IsVisible = false;

            // Показываем панель выбора стороны
            SetupPanel.IsVisible = true;
        }    

        // ЗАГЛУШКИ
        // Когда хост нажал "Создать сервер"
        private void StartHostServer_Click(object? sender, RoutedEventArgs e)
        {
            var vm = this.DataContext as MainViewModel;
            vm?.SetStatus("Создание сервера... (В разработке)");
            
            System.Threading.Thread.Sleep(500); // Имитация задержки
            vm?.SetStatus("Онлайн режим в разработке!");

            ShowHostSetupPanel(); 
        }

        private void StartJoinClient_Click(object? sender, RoutedEventArgs e)
        {
            // если Text вдруг null, берем пустую строку
            string ip = IpInputBox.Text ?? string.Empty;
            
            if (string.IsNullOrWhiteSpace(ip))
            {
                var vm = this.DataContext as MainViewModel;
                vm?.SetStatus("Введите IP адрес!");
                return;
            }

            var vm2 = this.DataContext as MainViewModel;
            vm2?.SetStatus($"Подключение к {ip}... (В разработке)");
            
            System.Threading.Thread.Sleep(500);
            vm2?.SetStatus("Онлайн режим в разработке!");
            
            ShowJoinPanel();
        }   

        private void StartVsAi_Click(object? sender, RoutedEventArgs e)
        {
            _isVsAi = true;
            _selectedDifficulty = null; 
            ShowSideSelection("Режим: Против ИИ\nВыберите сторону");
        }

        private void ShowAiDifficultySelection()
        {
            MainMenuPanel.IsVisible = false;
            AiDifficultyPanel.IsVisible = true;
            SetupPanel.IsVisible = false;
            GamePanel.IsVisible = false;
        }      

        private void ShowNetworkMenu_Click(object? sender, RoutedEventArgs e)
        {
            var vm = this.DataContext as MainViewModel;
            vm?.SetStatus("Онлайн режим в разработке...");
        }
        
        // пипетка
        private void OpenHighlightColorPicker_Click(object? sender, RoutedEventArgs e)
        {
            if (SettingsPopup != null) SettingsPopup.IsOpen = false;

            var picker = new ColorPickerDialog();
            picker.SetInitialColor(HighlightColor);

            picker.ColorSelected += (s, color) =>
            {
                HighlightColor = color;
                UpdateSelectionBorderColor();

                if (_settings != null) // Защита от null
                {
                    _settings.HighlightColorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                    SettingsService.Save(_settings);
                }
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
                    // Маппинг визуальных координат (x,y) на логические (logicX, logicY)
                    int logicX = isWhitePerspective ? x : (BoardSize - 1 - x);
                    int logicY = isWhitePerspective ? y : (BoardSize - 1 - y);

                    var cellBorder = new Border
                    {
                        [Grid.ColumnProperty] = x,
                        [Grid.RowProperty] = y,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        Tag = $"{logicX},{logicY}"
                    };

                    bool isDark = (logicX + logicY) % 2 == 1;
                    var cellColor = isDark ? Color.Parse("#769656") : Color.Parse("#F0D9B5");
                    cellBorder.Background = new SolidColorBrush(cellColor);

                    var contentGrid = new Grid();
                    cellBorder.Child = contentGrid;

                    var coordColor = isDark ? DarkCoordColor : LightCoordColor;
                    var brush = new SolidColorBrush(coordColor);

                    // Координаты: буквы снизу, цифры справа (относительно игрока)
                    if (y == BoardSize - 1)
                    {
                        contentGrid.Children.Add(new TextBlock
                        {
                            Text = files[logicX],
                            FontSize = 12, FontWeight = FontWeight.Bold, Foreground = brush,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Bottom,
                            Margin = new Thickness(2, 0, 0, 2),
                            IsHitTestVisible = false
                        });
                    }

                    if (x == BoardSize - 1)
                    {
                        contentGrid.Children.Add(new TextBlock
                        {
                            Text = (BoardSize - logicY).ToString(),
                            FontSize = 12, FontWeight = FontWeight.Bold, Foreground = brush,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(0, 2, 2, 0),
                            IsHitTestVisible = false
                        });
                    }

                    var pieceImage = new Image
                    {
                        Name = $"PieceImage_{x}_{y}",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Stretch = Stretch.Uniform,
                        IsHitTestVisible = false
                    };
                    contentGrid.Children.Add(pieceImage);

                    cellBorder.PointerReleased += (s, e) =>
                    {
                        if (e.InitialPressMouseButton == MouseButton.Left)
                            OnCellClicked(logicX, logicY);
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

                if (cell.Child is not Grid gridContainer) continue;

                // 1. Контур выделения
                var selectionBorder = gridContainer.Children
                    .FirstOrDefault(c => c is Border b && b.Name == "SelectionBorder") as Border;

                if (selectionBorder == null)
                {
                    selectionBorder = new Border
                    {
                        Name = "SelectionBorder",
                        BorderThickness = new Thickness(4),
                        BorderBrush = new SolidColorBrush(HighlightColor),
                        IsHitTestVisible = false,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };
                    gridContainer.Children.Insert(0, selectionBorder);
                }

                selectionBorder.IsVisible = (_selectedX == x && _selectedY == y);

                // 2. Призрак хода
                bool isPossibleMove = _possibleMoves.Any(m => m.x == x && m.y == y);
                var ghostImage = gridContainer.Children
                    .FirstOrDefault(c => c is Image i && i.Name != null && i.Name.StartsWith("Ghost")) as Image;

                if (isPossibleMove)
                {
                    if (ghostImage == null)
                    {
                        ghostImage = new Image
                        {
                            Name = $"Ghost_{x}_{y}",
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Stretch = Stretch.Uniform,
                            IsHitTestVisible = false,
                            Opacity = 0.5
                        };
                        gridContainer.Children.Add(ghostImage);
                    }

                    var selectedPiece = vm.Board[_selectedY!.Value, _selectedX!.Value];
                    if (selectedPiece != null)
                    {
                        LoadPieceImage(ghostImage!, selectedPiece);
                        // Скрываем призрака, если на клетке стоит своя фигура (атака)
                        ghostImage!.IsVisible = (piece == null || piece.Color != selectedPiece.Color);
                    }
                    else
                    {
                        ghostImage!.IsVisible = false;
                    }
                }
                else if (ghostImage != null)
                {
                    ghostImage.IsVisible = false;
                }

                // 3. Реальная фигура
                var realImage = gridContainer.Children
                    .FirstOrDefault(c => c is Image i && i.Name != null && i.Name.StartsWith("PieceImage_")) as Image;
                if (realImage != null)
                {
                    if (piece != null)
                    {
                        LoadPieceImage(realImage, piece);
                        realImage.IsVisible = true;
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

            // Если фигура уже выбрана
            if (_selectedX.HasValue && _selectedY.HasValue)
            {
                bool isMoveValid = _possibleMoves.Any(m => m.x == x && m.y == y);

                if (isMoveValid)
                {
                    var movingPiece = vm.Board[_selectedY.Value, _selectedX.Value];
                    
                    // Проверка на превращение пешки
                    if (movingPiece?.Type == PieceType.Pawn && (y == 0 || y == 7))
                    {
                        ShowPromotionSelection(_selectedX.Value, _selectedY.Value, x, y);
                        return;
                    }

                    // Обычный ход
                    vm.TryMakeMove(_selectedX.Value, _selectedY.Value, x, y);
                    ClearSelection();
                    UpdateBoardVisuals();
                    return;
                }

                // Клик на другую свою фигуру -> перевыбор
                var piece = vm.Board[y, x];
                var currentTurnColor = vm.CurrentTurnText.Contains("белых") ? PieceColor.White : PieceColor.Black;

                if (piece != null && piece.Color == currentTurnColor)
                {
                    _selectedX = x;
                    _selectedY = y;
                    _possibleMoves = vm.GetLegalMoves(x, y);
                    UpdateBoardVisuals();
                    return;
                }

                // Клик в пустоту или врага (не ход) -> сброс
                ClearSelection();
                UpdateBoardVisuals();
                return;
            }

            // Если ничего не выбрано -> попытка выбора
            var currentPiece = vm.Board[y, x];
            var turnColor = vm.CurrentTurnText.Contains("белых") ? PieceColor.White : PieceColor.Black;

            if (currentPiece != null && currentPiece.Color == turnColor)
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

        private void NewGame_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var vm = this.DataContext as MainViewModel;
            if (vm != null) { vm.NewGame(); UpdateBoardVisuals(); }
        }

                private void ShowPromotionSelection(int fromX, int fromY, int toX, int toY)
        {
            var vm = this.DataContext as MainViewModel;
            if (vm == null) return;

            // Создаем панель поверх доски
            var popupGrid = new Grid
            {
                Background = new SolidColorBrush(Color.Parse("#AA000000")), // Затемнение фона
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // Контейнер для кнопок (по центру)
            var contentBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#FF2D2D30")),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                BorderBrush = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(1)
            };

            var stackPanel = new StackPanel
            {
                Spacing = 15,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Заголовок
            stackPanel.Children.Add(new TextBlock
            {
                Text = "Превращение пешки!",
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Панель кнопок
            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var pieces = new[] { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight };
            var names = new[] { "Ферзь", "Ладья", "Слон", "Конь" };
            
            // Цвет пешки
            var pawnColor = vm.Board[fromY, fromX]?.Color ?? PieceColor.White;

            for (int i = 0; i < 4; i++)
            {
                var btn = new Button
                {
                    Content = names[i],
                    Width = 90,
                    Height = 90,
                    Tag = pieces[i], // Сохраняем тип фигуры
                    FontSize = 14,
                    FontWeight = FontWeight.Bold
                };

                // Обработчик клика
                btn.Click += (s, e) =>
                {
                    var selectedType = (PieceType)btn.Tag!;
                    
                    // 1. Делаем ход с выбранной фигурой
                    vm.TryMakeMove(fromX, fromY, toX, toY, selectedType);
                    
                    // 2. Удаляем окно
                    if (popupGrid.Parent is Grid parent)
                        parent.Children.Remove(popupGrid);

                    // 3. Сброс и перерисовка
                    ClearSelection();
                    UpdateBoardVisuals();
                };

                buttonsPanel.Children.Add(btn);
            }

            stackPanel.Children.Add(buttonsPanel);
            contentBorder.Child = stackPanel;
            popupGrid.Children.Add(contentBorder);

            // Добавляем на главный экран
            if (this.Content is Grid mainGrid)
            {
                mainGrid.Children.Add(popupGrid);
            }
        }
    }
}