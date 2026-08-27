using System; 
using Avalonia; 
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Data; 
using Avalonia.Input; 
using kchess; 
using kchess.Graphics;
using Avalonia.Platform;
using Avalonia.Interactivity;
using Avalonia.Styling;
using System.Collections.Generic; // Для List<>
using System.Linq;                // Для FirstOrDefault()
using kchess.Models; 
using kchess.Services; 
using System.Collections.ObjectModel;

namespace kchess.Graphics
{
    public partial class MainWindow : Window
    {
        private AppSettings _settings; 

        //флаги
        private bool _isVsAi = false; // true если игра против ИИ
        private bool _isAiThinking = false;
        private bool _isNetworkHost = false; // True если создаётся сервер
        private string? _selectedDifficulty = null; // "Easy", "Medium", "Hard"
        private PieceColor _playerColorForAi = PieceColor.White;

        private readonly List<Border> _cells = new List<Border>();
        private readonly List<Image> _images = new List<Image>(); 
        private int? _selectedX;
        private int? _selectedY;
        private bool _isWhitePerspective = true;
        private Color _lightCellColor = Color.Parse("#F0D9B5");
        private Color _darkCellColor = Color.Parse("#769656");
        private Color _coordOnLightColor = Colors.Black;
        private Color _coordOnDarkColor = Colors.White;

        // для подсветки
        private List<(int x, int y)> _possibleMoves = new List<(int, int)>(); 
        public Color HighlightColor { get; set; } = Color.Parse("#FFFF00");
        public Color LastMoveHighlightColor { get; set; } = Color.Parse("#BACA44");

        public MainWindow()
        {
            // Загрузка настроек
            _settings = SettingsService.Load();
            HighlightColor = _settings.GetHighlightColor();            
            InitializeComponent();
            ApplyLoadedSettingsToUi();
            ApplyBoardTheme(_settings.BoardTheme);
            ApplyTheme(_settings.Theme);
            ApplyLanguage(_settings.Language);
            
            // Строим доску сразу при запуске
            BuildChessBoard(); 
            // Показываем главное меню
            ShowMainMenu(); 
        }

        private void ApplyLoadedSettingsToUi()
        {
            if (HintsToggleButton != null)
                HintsToggleButton.IsChecked = _settings.ShowHints;

            if (PieceSkinComboBox != null)
            {
                foreach (var item in PieceSkinComboBox.Items)
                {
                    if (item is ComboBoxItem cbItem &&
                        string.Equals(cbItem.Content?.ToString(), _settings.PieceSkin, StringComparison.OrdinalIgnoreCase))
                    {
                        PieceSkinComboBox.SelectedItem = cbItem;
                        break;
                    }
                }
            }
        }

        private static double RelativeLuminance(Color c)
            => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

        private static Color GetContrastColor(Color background)
            => RelativeLuminance(background) > 0.5 ? Colors.Black : Colors.White;

        private void SaveSettings()
        {
            SettingsService.Save(_settings);
        }

        private void ApplyLanguage(string? langCode)
        {
            bool en = string.Equals(langCode, "EN", StringComparison.OrdinalIgnoreCase);

            if (BtnMainVsAi != null) BtnMainVsAi.Content = en ? "Play vs AI" : "Игра против ИИ";
            if (BtnMainLocal != null) BtnMainLocal.Content = en ? "Play Local" : "Игра с другом";
            if (BtnMainCreateOnline != null) BtnMainCreateOnline.Content = en ? "Create Online Game" : "Создать онлайн игру";
            if (BtnMainJoinOnline != null) BtnMainJoinOnline.Content = en ? "Join Online Game" : "Присоединиться к игре";
            if (BtnMainExit != null) BtnMainExit.Content = en ? "Exit" : "Выход";

            if (TxtHostTitle != null) TxtHostTitle.Text = en ? "Server Setup" : "Настройки сервера";
            if (TxtHostSubtitle != null) TxtHostSubtitle.Text = en ? "You will play with selected color" : "Вы будете играть за выбранный цвет";
            if (BtnHostCreateServer != null) BtnHostCreateServer.Content = en ? "Create Server" : "Создать сервер";
            if (BtnHostBack != null) BtnHostBack.Content = en ? "Back" : "Назад";
            if (BtnHostMainMenu != null) BtnHostMainMenu.Content = en ? "Main Menu" : "В главное меню";

            if (TxtJoinTitle != null) TxtJoinTitle.Text = en ? "Join Game" : "Подключение к игре";
            if (TxtJoinSubtitle != null) TxtJoinSubtitle.Text = en ? "Enter server IP address" : "Введите IP адрес сервера";
            if (BtnJoinConnect != null) BtnJoinConnect.Content = en ? "Connect" : "Подключиться";
            if (BtnJoinMainMenu != null) BtnJoinMainMenu.Content = en ? "Main Menu" : "В главное меню";

            if (TxtDifficultyTitle != null) TxtDifficultyTitle.Text = en ? "Difficulty Level" : "Уровень сложности";
            if (BtnDifficultyEasy != null) BtnDifficultyEasy.Content = en ? "Easy" : "Легкий";
            if (BtnDifficultyMedium != null) BtnDifficultyMedium.Content = en ? "Medium" : "Средний";
            if (BtnDifficultyHard != null) BtnDifficultyHard.Content = en ? "Hard" : "Сложный";
            if (BtnDifficultyBack != null) BtnDifficultyBack.Content = en ? "Back" : "Назад";
            if (BtnDifficultyMainMenu != null) BtnDifficultyMainMenu.Content = en ? "Main Menu" : "В главное меню";

            if (BtnSideWhite != null) BtnSideWhite.Content = en ? "Play as White" : "Играть за Белых";
            if (BtnSideBlack != null) BtnSideBlack.Content = en ? "Play as Black" : "Играть за Черных";
            if (BtnSideMainMenu != null) BtnSideMainMenu.Content = en ? "Main Menu" : "В главное меню";

            if (BtnNewGame != null) BtnNewGame.Content = en ? "New Game" : "Новая игра";
            if (TxtMoveHistoryTitle != null) TxtMoveHistoryTitle.Text = en ? "Move History" : "История ходов";
            if (BtnGameMainMenu != null) BtnGameMainMenu.Content = en ? "Main Menu" : "В главное меню";

            if (SetupTitleText != null && string.IsNullOrWhiteSpace(SetupTitleText.Text))
                SetupTitleText.Text = en ? "Choose side" : "Выберите сторону";

            if (DataContext is MainViewModel vm && vm.MoveHistoryList is ObservableCollection<MoveDisplayItem>)
            {
                vm.SetStatus(en ? "Language switched to English" : "Язык переключен на русский");
            }
        }

        private void ApplyTheme(string? themeName)
        {
            var app = Application.Current;
            if (app == null) return;

            app.RequestedThemeVariant = themeName?.ToLowerInvariant() switch
            {
                "light" => ThemeVariant.Light,
                _ => ThemeVariant.Dark
            };
        }

        private void ApplyBoardTheme(string? boardThemeName)
        {
            (Color light, Color dark) = boardThemeName switch
            {
                "LichessBrown" => (Color.Parse("#F0D9B5"), Color.Parse("#B58863")),
                "LichessBlue" => (Color.Parse("#DEE3E6"), Color.Parse("#8CA2AD")),
                "LichessGray" => (Color.Parse("#E0E0E0"), Color.Parse("#8F8F8F")),
                "LichessPurple" => (Color.Parse("#F0E5FF"), Color.Parse("#8D6AAE")),
                "LichessOlive" => (Color.Parse("#EFECCA"), Color.Parse("#8FA66C")),
                _ => (Color.Parse("#F0D9B5"), Color.Parse("#769656"))
            };

            _lightCellColor = light;
            _darkCellColor = dark;
            _coordOnLightColor = GetContrastColor(light);
            _coordOnDarkColor = GetContrastColor(dark);
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
                Console.WriteLine($">>> КЛИК ПО СЛОЖНОСТИ: {difficulty}");
                _selectedDifficulty = difficulty;
                
                var vm = this.DataContext as MainViewModel;
                if (vm == null) return;

                // 1. Явно говорим VM: включаем режим ИИ
                bool playerIsWhite = (_playerColorForAi == PieceColor.White);
                vm.StartGameVsAI(playerIsWhite, GetDepthByDifficulty(difficulty));
                
                Console.WriteLine($">>> РЕЖИМ В VM: {vm.CurrentMode}");
                Console.WriteLine($">>> ИГРОК: {(playerIsWhite ? "Белые" : "Черные")}");

                // 2. Запускаем визуальную часть
                StartGame(playerIsWhite);
            }
        }

        private void TryStartAiGame()
        {
            Console.WriteLine(">>> TRY START AI GAME <<<");
            if (_selectedDifficulty == null) return;

            var vm = this.DataContext as MainViewModel;
            if (vm == null) return;

            bool playerIsWhite = (_playerColorForAi == PieceColor.White);
            
            // Вызываем метод, который ставит режим PvAI
            vm.StartGameVsAI(playerIsWhite, GetDepthByDifficulty(_selectedDifficulty)); 
            
            Console.WriteLine($">>> VM MODE SET TO: {vm.CurrentMode} <<<");

            StartGame(playerIsWhite);
        }

        // МЕНЮ ВЫБОРА СТОРОНЫ
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
                    ShowHostSetupPanel();
                }
                else if (_isVsAi)
                {
                    Console.WriteLine(">>> ЗАХОД В ВЕТКУ ИИ <<<");
                    ShowAiDifficultySelection();
                }
                else
                {
                    Console.WriteLine(">>> ЗАПУСК ЛОКАЛЬНОЙ ИГРЫ");
                    var vm = this.DataContext as MainViewModel;
                    if (vm == null) return;
                    vm.StartLocalGame(); // <--- ЯВНЫЙ ВЫЗОВ
                    StartGame(playAsWhite);
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

        private void StartGame(bool playerIsWhite)
        {
            var vm = this.DataContext as MainViewModel;
            if (vm == null) return;
            _isWhitePerspective = playerIsWhite;

            // Просто рисуем доску и показываем панель игры
            BuildChessBoard(playerIsWhite);            
            UpdateBoardVisuals();
            
            MainMenuPanel.IsVisible = false;
            AiDifficultyPanel.IsVisible = false;
            SetupPanel.IsVisible = false;
            GamePanel.IsVisible = true;
            
            this.Activate();

            // Гарантия первого хода ИИ, если игрок выбрал черных.
            // Если ИИ уже сходил на этапе запуска в VM, условие не выполнится.
            if (vm.CurrentMode == GameMode.PvAI)
            {
                var playerColor = playerIsWhite ? PieceColor.White : PieceColor.Black;
                if (vm.CurrentTurnColor != playerColor)
                {
                    vm.MakeAiMove();
                    UpdateBoardVisuals();
                }
            }
        }
        
        // Обработчик кнопки "В главное меню" из игры
        private void BackToMenuFromGame_Click(object? sender, RoutedEventArgs e) => BackToMenu_Click(sender, e);

        // Обработчики кнопок
        private void StartLocalFriend_Click(object? sender, RoutedEventArgs e)
        {
            _isVsAi = false; // Сбрасываем флаг ИИ
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

        // заглушки на функции оставленные на будущее
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

        private static int GetDepthByDifficulty(string? difficulty) => difficulty switch
        {
            "Easy" => ChessAI.EasyDepth,
            "Medium" => ChessAI.MediumDepth,
            "Hard" => ChessAI.HardDepth,
            _ => ChessAI.MediumDepth
        };

        private void ShowNetworkMenu_Click(object? sender, RoutedEventArgs e)
        {
            var vm = this.DataContext as MainViewModel;
            vm?.SetStatus("Онлайн режим в разработке...");
        }

        private void ThemeDark_Click(object? sender, RoutedEventArgs e)
        {
            _settings.Theme = "Dark";
            ApplyTheme(_settings.Theme);
            SaveSettings();
        }

        private void ThemeLight_Click(object? sender, RoutedEventArgs e)
        {
            _settings.Theme = "Light";
            ApplyTheme(_settings.Theme);
            SaveSettings();
        }

        private void LanguageRu_Click(object? sender, RoutedEventArgs e)
        {
            _settings.Language = "RU";
            ApplyLanguage(_settings.Language);
            SaveSettings();
        }

        private void LanguageEn_Click(object? sender, RoutedEventArgs e)
        {
            _settings.Language = "EN";
            ApplyLanguage(_settings.Language);
            SaveSettings();
        }

        private void BoardTheme_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string boardTheme) return;
            _settings.BoardTheme = boardTheme;
            ApplyBoardTheme(boardTheme);
            SaveSettings();
            BuildChessBoard(_isWhitePerspective);
        }

        private void PieceSkin_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (PieceSkinComboBox?.SelectedItem is ComboBoxItem item)
            {
                _settings.PieceSkin = item.Content?.ToString() ?? "Classic";
                SaveSettings();
                UpdateBoardVisuals();
            }
        }

        private void HintsToggle_Changed(object? sender, RoutedEventArgs e)
        {
            _settings.ShowHints = HintsToggleButton?.IsChecked ?? true;
            SaveSettings();
            UpdateBoardVisuals();
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
                    SaveSettings();
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
                    var cellColor = isDark ? _darkCellColor : _lightCellColor;
                    cellBorder.Background = new SolidColorBrush(cellColor);

                    var contentGrid = new Grid();
                    cellBorder.Child = contentGrid;

                    var coordColor = isDark ? _coordOnDarkColor : _coordOnLightColor;
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

                    var pieceSymbol = new TextBlock
                    {
                        Name = $"PieceSymbol_{x}_{y}",
                        FontSize = 46,
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        IsHitTestVisible = false,
                        IsVisible = false
                    };
                    contentGrid.Children.Add(pieceSymbol);

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
            bool symbolsSkin = string.Equals(_settings.PieceSkin, "Symbols", StringComparison.OrdinalIgnoreCase);
            bool showHints = _settings.ShowHints;

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

                // 1.5 Подсветка последнего хода
                bool isLastMoveCell = vm.LastMove.HasValue && 
                    ((vm.LastMove.Value.fromX == x && vm.LastMove.Value.fromY == y) ||
                     (vm.LastMove.Value.toX == x && vm.LastMove.Value.toY == y));
                
                var lastMoveHighlight = gridContainer.Children
                    .FirstOrDefault(c => c is Border b && b.Name == "LastMoveHighlight") as Border;

                if (isLastMoveCell)
                {
                    if (lastMoveHighlight == null)
                    {
                        lastMoveHighlight = new Border
                        {
                            Name = "LastMoveHighlight",
                            Background = new SolidColorBrush(LastMoveHighlightColor) { Opacity = 0.5 },
                            IsHitTestVisible = false,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch
                        };
                        gridContainer.Children.Insert(0, lastMoveHighlight);
                    }
                    lastMoveHighlight.IsVisible = true;
                }
                else
                {
                    if (lastMoveHighlight != null)
                        lastMoveHighlight.IsVisible = false;
                }

                // 2. Призрак хода
                bool isPossibleMove = showHints && _possibleMoves.Any(m => m.x == x && m.y == y);
                var ghostImage = gridContainer.Children
                    .FirstOrDefault(c => c is Image i && i.Name != null && i.Name.StartsWith("Ghost")) as Image;
                var ghostSymbol = gridContainer.Children
                    .FirstOrDefault(c => c is TextBlock t && t.Name != null && t.Name.StartsWith("GhostSymbol")) as TextBlock;

                if (isPossibleMove)
                {
                    if (symbolsSkin)
                    {
                        ghostImage?.IsVisible = false;
                        if (ghostSymbol == null)
                        {
                            ghostSymbol = new TextBlock
                            {
                                Name = $"GhostSymbol_{x}_{y}",
                                FontSize = 46,
                                FontWeight = FontWeight.Bold,
                                Opacity = 0.45,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                                IsHitTestVisible = false
                            };
                            gridContainer.Children.Add(ghostSymbol);
                        }

                        var selectedPiece = vm.Board[_selectedY!.Value, _selectedX!.Value];
                        if (selectedPiece != null)
                        {
                            ghostSymbol.Text = GetPieceSymbol(selectedPiece);
                            ghostSymbol.Foreground = new SolidColorBrush(selectedPiece.Color == PieceColor.White ? Colors.White : Colors.Black);
                            ghostSymbol.IsVisible = (piece == null || piece.Color != selectedPiece.Color);
                        }
                        else
                        {
                            ghostSymbol.IsVisible = false;
                        }
                    }
                    else
                    {
                        ghostSymbol?.IsVisible = false;
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
                }
                else
                {
                    if (ghostImage != null) ghostImage.IsVisible = false;
                    if (ghostSymbol != null) ghostSymbol.IsVisible = false;
                }

                // 3. Реальная фигура
                var realImage = gridContainer.Children
                    .FirstOrDefault(c => c is Image i && i.Name != null && i.Name.StartsWith("PieceImage_")) as Image;
                var realSymbol = gridContainer.Children
                    .FirstOrDefault(c => c is TextBlock t && t.Name != null && t.Name.StartsWith("PieceSymbol_")) as TextBlock;

                if (symbolsSkin)
                {
                    if (realImage != null)
                    {
                        realImage.IsVisible = false;
                        realImage.Source = null;
                    }

                    if (realSymbol == null) continue;
                    if (piece != null)
                    {
                        realSymbol.Text = GetPieceSymbol(piece);
                        realSymbol.Foreground = new SolidColorBrush(piece.Color == PieceColor.White ? Colors.White : Colors.Black);
                        realSymbol.IsVisible = true;
                    }
                    else
                    {
                        realSymbol.IsVisible = false;
                        realSymbol.Text = string.Empty;
                    }
                }
                else if (realImage != null)
                {
                    if (realSymbol != null)
                    {
                        realSymbol.IsVisible = false;
                        realSymbol.Text = string.Empty;
                    }

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

            var candidates = GetSkinAssetCandidates(_settings.PieceSkin, figCode, colorCode);
            foreach (var fileName in candidates)
            {
                try
                {
                    string assetPath = $"/Graphics/Assets/{fileName}";
                    var uri = new Uri($"avares://kchess{assetPath}");
                    using var stream = AssetLoader.Open(uri);
                    image.Source = new Bitmap(stream);
                    return;
                }
                catch
                {
                    // Пробуем следующий вариант в списке.
                }
            }

            image.Source = null;
        }

        private static IEnumerable<string> GetSkinAssetCandidates(string? skin, string figCode, string colorCode)
        {
            // Кастомные скины: просто добавьте файлы по этим шаблонам в Graphics/Assets.
            // Формат colorCode: l (white), d (black), figCode: p n b r q k.
            if (string.Equals(skin, "Neo", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"Neo_{figCode}{colorCode}.png";
                yield return $"Neo_Chess_{figCode}{colorCode}t60.png";
            }
            else if (string.Equals(skin, "Glass", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"Glass_{figCode}{colorCode}.png";
                yield return $"Glass_Chess_{figCode}{colorCode}t60.png";
            }
            else if (string.Equals(skin, "Minimal", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"Minimal_{figCode}{colorCode}.png";
                yield return $"Minimal_Chess_{figCode}{colorCode}t60.png";
            }

            yield return $"Chess_{figCode}{colorCode}t60.png";
        }

        private static string GetPieceSymbol(Piece piece)
        {
            return (piece.Color, piece.Type) switch
            {
                (PieceColor.White, PieceType.King) => "♔",
                (PieceColor.White, PieceType.Queen) => "♕",
                (PieceColor.White, PieceType.Rook) => "♖",
                (PieceColor.White, PieceType.Bishop) => "♗",
                (PieceColor.White, PieceType.Knight) => "♘",
                (PieceColor.White, PieceType.Pawn) => "♙",
                (PieceColor.Black, PieceType.King) => "♚",
                (PieceColor.Black, PieceType.Queen) => "♛",
                (PieceColor.Black, PieceType.Rook) => "♜",
                (PieceColor.Black, PieceType.Bishop) => "♝",
                (PieceColor.Black, PieceType.Knight) => "♞",
                (PieceColor.Black, PieceType.Pawn) => "♟",
                _ => string.Empty
            };
        }

        private void OnCellClicked(int x, int y)
        {
            if (_isAiThinking) return; 

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
                var currentTurnColor = vm.CurrentTurnColor;

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
            var turnColor = vm.CurrentTurnColor;

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