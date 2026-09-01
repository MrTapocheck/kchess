using System;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using kchess;
using kchess.Services;

namespace kchess
{
    public enum GameMode
    {
        LocalPvP,
        PvAI,
        OnlineMultiplayer
    }

    public class MoveDisplayItem
    {
        public int MoveNumber { get; set; }
        public string WhiteMove { get; set; } = "";
        public string BlackMove { get; set; } = "";
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ChessEngine _engine;
        private ChessAI? _ai;
        private readonly NeuralEvaluator? _evaluator;

        public GameMode CurrentMode { get; private set; } = GameMode.LocalPvP;
        private bool _aiPlaysWhite = false;
        private bool _aiBusy;
        private int _aiSearchGeneration;

        public ObservableCollection<MoveDisplayItem> MoveHistoryList { get; }

        // Новое свойство для привязки UI (копия доски)
        private Piece?[,] _displayBoard = new Piece?[8, 8];
        public Piece?[,] DisplayBoard
        {
            get => _displayBoard;
            private set
            {
                _displayBoard = value;
                OnPropertyChanged();
            }
        }

        public MainViewModel()
        {
            _engine = new ChessEngine();
            MoveHistoryList = new ObservableCollection<MoveDisplayItem>();
            // Инициализируем отображаемую доску
            UpdateDisplayBoard();

            try 
            {
                string modelPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chess_model.onnx");
                
                if (!System.IO.File.Exists(modelPath))
                {
                    throw new FileNotFoundException($"Файл модели не найден по пути: {modelPath}");
                }

                Console.WriteLine($"✅ Путь к модели верный: {modelPath}");
                _evaluator = new NeuralEvaluator(modelPath);
                _ai = new ChessAI(_evaluator);
                Console.WriteLine("✅ AI загружен успешно.");
            }
            catch (Exception ex)
            {
                string errorMsg = $"❌ Ошибка загрузки ИИ: {ex.Message}";
                Console.WriteLine(errorMsg);
                _engine.SetStatus(errorMsg); 
                _ai = null;
                _evaluator = null;
            }
        }

        // Свойство для обратной совместимости (не используется в привязке)
        public Piece?[,] Board => _engine.Board;
        public PieceColor CurrentTurnColor => _engine.CurrentTurn;
        public string StatusMessage => _engine.LastStatus;
        public (int fromX, int fromY, int toX, int toY)? LastMove => _engine.LastMove;
        public string CurrentTurnText => 
            _engine.IsGameOver ? "Игра окончена" : 
            (_engine.CurrentTurn == PieceColor.White ? "Ход белых" : "Ход черных");

        public bool IsAiThinking => _aiBusy;

        public bool CanPlayerInteract
        {
            get
            {
                if (_engine.IsGameOver || _aiBusy) return false;
                if (CurrentMode != GameMode.PvAI) return true;
                bool aiTurn = (_aiPlaysWhite && _engine.CurrentTurn == PieceColor.White) ||
                              (!_aiPlaysWhite && _engine.CurrentTurn == PieceColor.Black);
                return !aiTurn;
            }
        }
        
        private bool _canUndo = false;
        public bool CanUndo
        {
            get => _canUndo;
            private set
            {
                if (_canUndo != value)
                {
                    _canUndo = value;
                    OnPropertyChanged(nameof(CanUndo));
                }
            }
        }

        private void HandlePostMoveLogic()
        {
            Console.WriteLine(">>> [HandlePostMoveLogic] Начало проверки...");
            Console.WriteLine($">>> [HandlePostMoveLogic] Текущий режим: {CurrentMode}");
            Console.WriteLine($">>> [HandlePostMoveLogic] Игра окончена? {_engine.IsGameOver}");
            Console.WriteLine($">>> [HandlePostMoveLogic] Чей ход? {_engine.CurrentTurn}");
            Console.WriteLine($">>> [HandlePostMoveLogic] Бот играет за белых? {_aiPlaysWhite}");

            if (_engine.IsGameOver) 
            {
                Console.WriteLine(">>> [HandlePostMoveLogic] Игра окончена, выход.");
                return; 
            }

            if (CurrentMode == GameMode.PvAI)
            {
                bool aiTurn = (_aiPlaysWhite && _engine.CurrentTurn == PieceColor.White) ||
                              (!_aiPlaysWhite && _engine.CurrentTurn == PieceColor.Black);
                
                Console.WriteLine($">>> [HandlePostMoveLogic] Сейчас ход бота? {aiTurn}");
                Console.WriteLine($">>> [HandlePostMoveLogic] Объект _ai null? {_ai == null}");

                if (aiTurn)
                {
                    if (_ai != null)
                    {
                        Console.WriteLine(">>> [HandlePostMoveLogic] ВЫЗОВ MakeAiMove()!");
                        MakeAiMove();
                    }
                    else
                    {
                        Console.WriteLine(">>> [HandlePostMoveLogic] ОШИБКА: Ход бота, но объект _ai = NULL!");
                    }
                }
                else
                {
                    Console.WriteLine(">>> [HandlePostMoveLogic] Сейчас ход игрока, бот молчит.");
                }
            }
            else
            {
                Console.WriteLine($">>> [HandlePostMoveLogic] Режим НЕ PvAI (текущий: {CurrentMode}), бот молчит.");
            }
        }

        // --- Методы управления игрой ---

        public void StartGameVsAI(bool playerIsWhite, int aiDepth)
        {
            Console.WriteLine(">>> StartGameVsAI ВЫЗВАН! PlayerIsWhite: " + playerIsWhite);
            
            NewGame();
            CurrentMode = GameMode.PvAI;
            _aiPlaysWhite = !playerIsWhite;
            if (_evaluator != null)
            {
                _ai = new ChessAI(_evaluator, aiDepth);
            }
            
            SetStatus(playerIsWhite ? "Вы играете белыми против ИИ" : "Вы играете черными против ИИ");
            CancelAiSearch();
            UpdateDisplayBoard();
        }

        public void StartLocalGame()
        {
            NewGame();
            CurrentMode = GameMode.LocalPvP;
            SetStatus("Локальная игра: Ход белых");
        }

        public void NewGame()
        {
            CancelAiSearch();
            _engine.InitializeBoard();
            MoveHistoryList.Clear();
            RefreshProperties();
            UpdateDisplayBoard();
            CanUndo = _engine.CanUndo;
        }

        public void NewGamePreserveMode()
        {
            GameMode previousMode = CurrentMode;
            bool previousAiPlaysWhite = _aiPlaysWhite;
            int aiDepth = _ai != null ? _ai.Depth : ChessAI.MediumDepth;

            CancelAiSearch();
            _engine.InitializeBoard();
            MoveHistoryList.Clear();
            RefreshProperties();
            UpdateDisplayBoard();
            CanUndo = _engine.CanUndo;
            
            CurrentMode = previousMode;
            _aiPlaysWhite = previousAiPlaysWhite;
            
            if (CurrentMode == GameMode.PvAI && _evaluator != null)
            {
                _ai = new ChessAI(_evaluator, aiDepth);
                SetStatus(previousAiPlaysWhite ? "Вы играете черными против ИИ" : "Вы играете белыми против ИИ");
                
                if (_aiPlaysWhite)
                {
                    MakeAiMove();
                }
            }
            else if (CurrentMode == GameMode.LocalPvP)
            {
                SetStatus("Локальная игра: Ход белых");
            }
            UpdateDisplayBoard();
        }

        public void UndoMove()
        {
            CancelAiSearch();
            if (_engine.UndoMove())
            {
                UpdateMoveHistory();
                RefreshProperties();
                UpdateDisplayBoard();
                CanUndo = _engine.CanUndo;
                SetStatus("Ход отменен");
            }
            else
            {
                SetStatus("Невозможно отменить ход");
            }
        }

        // --- Логика ИИ ---

        public void MakeAiMove()
        {
            Console.WriteLine(">>> [MakeAiMove] ЗАПУСК! Генерация ходов...");

            if (_aiBusy)
            {
                Console.WriteLine(">>> [MakeAiMove] Уже идёт поиск, повторный запуск пропущен.");
                return;
            }
            if (_ai == null || _evaluator == null) 
            {
                Console.WriteLine(">>> [MakeAiMove] ОШИБКА: _ai или _evaluator = NULL");
                return; 
            }
            if (_engine.IsGameOver) 
            {
                Console.WriteLine(">>> [MakeAiMove] Игра окончена, выход.");
                return; 
            }

            var candidates = new List<(int fromX, int fromY, int toX, int toY)>();

            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var moves = GetLegalMoves(x, y);
                    foreach (var move in moves)
                    {
                        candidates.Add((x, y, move.x, move.y));
                    }
                }
            }

            if (candidates.Count == 0) return;

            var searchEngine = _engine.CloneForSearch();
            int searchId = ++_aiSearchGeneration;
            SetAiBusy(true);
            SetStatus("🤖 ИИ думает...");

            var ai = _ai;
            var scheduler = SynchronizationContext.Current != null
                ? TaskScheduler.FromCurrentSynchronizationContext()
                : TaskScheduler.Default;

            Task.Run(() =>
            {
                var startTime = Stopwatch.StartNew();
                var best = ai.GetBestMove(searchEngine, candidates);
                startTime.Stop();

                if (startTime.ElapsedMilliseconds < 1000)
                {
                    int delay = 1000 - (int)startTime.ElapsedMilliseconds;
                    Console.WriteLine($">>> [MakeAiMove] Расчет был быстрым ({startTime.ElapsedMilliseconds}мс), добавляем задержку {delay}мс");
                    Thread.Sleep(delay);
                }

                return best;
            }).ContinueWith(task =>
            {
                try
                {
                    if (searchId != _aiSearchGeneration)
                    {
                        Console.WriteLine(">>> [MakeAiMove] Устаревший поиск отброшен.");
                        return;
                    }

                    if (task.IsFaulted)
                    {
                        Console.WriteLine($">>> [MakeAiMove] Ошибка поиска: {task.Exception?.GetBaseException().Message}");
                        SetStatus("Ошибка расчёта хода ИИ");
                        return;
                    }

                    Console.WriteLine($">>> [MakeAiMove] Результат: {task.Result.HasValue}");
                    if (task.Result.HasValue)
                    {
                        var move = task.Result.Value;
                        Console.WriteLine($">>> [MakeAiMove] Ход ИИ: {move.fromX},{move.fromY} -> {move.toX},{move.toY}");
                        SetStatus("ИИ делает ход...");
                        TryMakeMove(move.fromX, move.fromY, move.toX, move.toY);
                    }
                    else
                    {
                        SetStatus("ИИ не нашел ход");
                    }
                }
                finally
                {
                    if (searchId == _aiSearchGeneration)
                        SetAiBusy(false);
                }
            }, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        }

        private void CancelAiSearch()
        {
            _aiSearchGeneration++;
            SetAiBusy(false);
        }

        private void SetAiBusy(bool busy)
        {
            if (_aiBusy == busy) return;
            _aiBusy = busy;
            OnPropertyChanged(nameof(IsAiThinking));
            OnPropertyChanged(nameof(CanPlayerInteract));
        }

        // --- Обработка ходов ---

        public void TryMakeMove(int fromX, int fromY, int toX, int toY, PieceType promotionType = PieceType.Queen)
        {
            try
            {
                bool success = _engine.TryMove(fromX, fromY, toX, toY, promotionType);
                
                if (success)
                {
                    UpdateDisplayBoard(); // обновляем отображение доски
                    UpdateMoveHistory();
                    RefreshProperties();
                    CanUndo = _engine.CanUndo;

                    HandlePostMoveLogic();
                }
                else
                {
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Ошибка: {ex.Message}");
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public List<(int x, int y)> GetLegalMoves(int fromX, int fromY)
        {
            var legalMoves = new List<(int x, int y)>();
            if (_engine == null || _engine.Board[fromY, fromX] == null) return legalMoves;

            var piece = _engine.Board[fromY, fromX];
            if (piece == null || piece.Color != _engine.CurrentTurn) 
                return legalMoves;
                
            var pseudoMoves = piece.GetLegalMoves(_engine.Board, new Position(fromX, fromY));
            
            foreach (var move in pseudoMoves)
            {
                int toX = move.X;
                int toY = move.Y;
                
                var captured = _engine.Board[toY, toX];
                
                _engine.Board[toY, toX] = piece;
                _engine.Board[fromY, fromX] = null;
                
                bool isCheck = _engine.IsKingInCheck(piece.Color); 
                
                _engine.Board[fromY, fromX] = piece;
                _engine.Board[toY, toX] = captured;
                
                if (!isCheck)
                {
                    legalMoves.Add((toX, toY));
                }
            }
            
            // Взятие на проходе
            if (piece.Type == PieceType.Pawn && _engine._enPassantTarget.HasValue)
            {
                int epX = _engine._enPassantTarget.Value.X;
                int epY = _engine._enPassantTarget.Value.Y;

                int direction = (piece.Color == PieceColor.White) ? -1 : 1;
                
                if (epY == fromY + direction && Math.Abs(epX - fromX) == 1)
                {
                    var capturedPawn = _engine.Board[fromY, epX];
                    
                    if (capturedPawn != null && capturedPawn.Color != piece.Color && capturedPawn.Type == PieceType.Pawn)
                    {
                        _engine.Board[epY, epX] = piece;       
                        _engine.Board[fromY, fromX] = null;    
                        _engine.Board[fromY, epX] = null;      

                        bool isCheckAfterEp = _engine.IsKingInCheck(piece.Color);

                        _engine.Board[fromY, fromX] = piece;
                        _engine.Board[epY, epX] = null;
                        _engine.Board[fromY, epX] = capturedPawn;

                        if (!isCheckAfterEp)
                        {
                            legalMoves.Add((epX, epY));
                        }
                    }
                }
            }

            // Рокировка
            if (piece.Type == PieceType.King && !_engine.IsKingInCheck(piece.Color))
            {
                int y = fromY;
                bool isWhite = piece.Color == PieceColor.White;
                PieceColor enemyColor = isWhite ? PieceColor.Black : PieceColor.White;
                
                if (fromX != 4) 
                    return legalMoves;
                
                // Короткая рокировка
                bool canCastleKingside = isWhite 
                    ? !_engine._whiteKingMoved && !_engine._whiteRookKingsideMoved 
                    : !_engine._blackKingMoved && !_engine._blackRookKingsideMoved;
                
                if (canCastleKingside)
                {
                    var rook = _engine.Board[y, 7];
                    if (rook != null && rook.Type == PieceType.Rook && rook.Color == piece.Color)
                    {
                        if (_engine.Board[y, 5] == null && _engine.Board[y, 6] == null)
                        {
                            if (!_engine.IsSquareAttacked(5, y, enemyColor) &&
                                !_engine.IsSquareAttacked(6, y, enemyColor))
                            {
                                var k = _engine.Board[y, 4]; 
                                _engine.Board[y, 4] = null; 
                                _engine.Board[y, 6] = k;
                                
                                if (!_engine.IsKingInCheck(piece.Color)) 
                                    legalMoves.Add((6, y));
                                
                                _engine.Board[y, 6] = null; 
                                _engine.Board[y, 4] = k;
                            }
                        }
                    }
                }

                // Длинная рокировка
                bool canCastleQueenside = isWhite 
                    ? !_engine._whiteKingMoved && !_engine._whiteRookQueensideMoved 
                    : !_engine._blackKingMoved && !_engine._blackRookQueensideMoved;
                
                if (canCastleQueenside)
                {
                    var rook = _engine.Board[y, 0];
                    if (rook != null && rook.Type == PieceType.Rook && rook.Color == piece.Color)
                    {
                        if (_engine.Board[y, 1] == null && _engine.Board[y, 2] == null && _engine.Board[y, 3] == null)
                        {
                            if (!_engine.IsSquareAttacked(3, y, enemyColor) &&
                                !_engine.IsSquareAttacked(2, y, enemyColor))
                            {
                                var k = _engine.Board[y, 4]; 
                                _engine.Board[y, 4] = null; 
                                _engine.Board[y, 2] = k;
                                
                                if (!_engine.IsKingInCheck(piece.Color)) 
                                    legalMoves.Add((2, y));
                                
                                _engine.Board[y, 2] = null; 
                                _engine.Board[y, 4] = k;
                            }
                        }
                    }
                }
            }
            
            return legalMoves;
        }

        private bool IsMoveLegal(int fromX, int fromY, int toX, int toY)
        {
            var piece = _engine.Board[fromY, fromX];
            if (piece == null) return false;
            
            var captured = _engine.Board[toY, toX];
            
            _engine.Board[toY, toX] = piece;
            _engine.Board[fromY, fromX] = null;
            
            bool isCheck = _engine.IsKingInCheck(piece.Color);
            
            _engine.Board[fromY, fromX] = piece;
            _engine.Board[toY, toX] = captured;
            
            return !isCheck;
        }

        private void UpdateMoveHistory()
        {
            var moves = _engine.MoveHistory;
            int previousCount = MoveHistoryList.Count;
            MoveHistoryList.Clear();
            for (int i = 0; i < moves.Count; i += 2)
            {
                int moveNum = (i / 2) + 1;
                string white = moves[i];
                string black = (i + 1 < moves.Count) ? moves[i + 1] : "";
                MoveHistoryList.Add(new MoveDisplayItem { MoveNumber = moveNum, WhiteMove = white, BlackMove = black });
            }
            
            OnPropertyChanged(nameof(MoveHistoryList));
            
            if (MoveHistoryList.Count > previousCount)
            {
                MoveHistoryAdded?.Invoke(this, EventArgs.Empty);
            }
        }

        private void UpdateDisplayBoard()
        {
            var copy = new Piece?[8, 8];
            Array.Copy(_engine.Board, copy, _engine.Board.Length);
            DisplayBoard = copy;
        }
        
        public event EventHandler? MoveHistoryAdded;

        private void RefreshProperties()
        {
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(CurrentTurnText));
            OnPropertyChanged(nameof(LastMove));
            OnPropertyChanged(nameof(CanPlayerInteract));
        }

        public void SetStatus(string message)
        {
            _engine.SetStatus(message);
            OnPropertyChanged(nameof(StatusMessage));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}