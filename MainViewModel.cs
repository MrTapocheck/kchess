using System;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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
        private readonly ChessAI? _ai;
        private readonly NeuralEvaluator? _evaluator;

        public GameMode CurrentMode { get; private set; } = GameMode.LocalPvP;
        private bool _aiPlaysWhite = false; // Поле вместо свойства, чтобы было проще

        public ObservableCollection<MoveDisplayItem> MoveHistoryList { get; }

        public MainViewModel()
        {
            _engine = new ChessEngine();
            MoveHistoryList = new ObservableCollection<MoveDisplayItem>();

            try 
            {
                string modelPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chess_model.onnx");
                
                // Добавим проверку существования файла ПЕРЕД созданием объекта
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
                // Теперь мы увидим точную причину ошибки
                string errorMsg = $"❌ Ошибка загрузки ИИ: {ex.Message}";
                Console.WriteLine(errorMsg);
                
                // Сохраним ошибку в статус, чтобы видеть её в интерфейсе при старте
                _engine.SetStatus(errorMsg); 
                
                _ai = null;
                _evaluator = null;
            }
        }

        public Piece?[,] Board => _engine.Board;
        public string StatusMessage => _engine.LastStatus;
        public string CurrentTurnText => 
            _engine.IsGameOver ? "Игра окончена" : 
            (_engine.CurrentTurn == PieceColor.White ? "Ход белых" : "Ход черных");

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

        public void StartGameVsAI(bool playerIsWhite)
        {
            Console.WriteLine(">>> StartGameVsAI ВЫЗВАН! PlayerIsWhite: " + playerIsWhite); // Лог для отладки (если вдруг увидим)
            
            NewGame();
            CurrentMode = GameMode.PvAI;
            _aiPlaysWhite = !playerIsWhite;
            
            SetStatus(playerIsWhite ? "Вы играете белыми против ИИ" : "Вы играете черными против ИИ");

            // ЕСЛИ БОТ ИГРАЕТ БЕЛЫМИ — ОН ДОЛЖЕН ПОЙТИ ПРЯМО СЕЙЧАС
            if (_aiPlaysWhite)
            {
                Console.WriteLine(">>> БОТ ИГРАЕТ БЕЛЫМИ. ЗАПУСК ХОДА НЕМЕДЛЕННО.");
                
                // Проверка на null перед вызовом
                if (_ai == null)
                {
                    SetStatus("ОШИБКА: Модуль ИИ не загружен!");
                    return;
                }
                
                // Вызываем напрямую, без Task.Delay
                MakeAiMove();
            }
        }

        public void StartLocalGame()
        {
            NewGame();
            CurrentMode = GameMode.LocalPvP;
            SetStatus("Локальная игра: Ход белых");
        }

        public void NewGame()
        {
            _engine.InitializeBoard();
            MoveHistoryList.Clear();
            RefreshProperties();
            OnPropertyChanged(nameof(Board));
        }

        // --- Логика ИИ ---

        public void MakeAiMove()
        {
            Console.WriteLine(">>> [MakeAiMove] ЗАПУСК! Генерация ходов...");
            
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

            SetStatus("🤖 ИИ думает...");
            SetStatus("🤖 ИИ думает...");

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

            // Запуск расчета (можно обернуть в Task.Run если будет фризить UI)
            var best = _ai.GetBestMove(_engine, candidates);

            if (best.HasValue)
            {
                TryMakeMove(best.Value.fromX, best.Value.fromY, best.Value.toX, best.Value.toY);
            }
        }

        // --- Обработка ходов ---

        public void TryMakeMove(int fromX, int fromY, int toX, int toY, PieceType promotionType = PieceType.Queen)
        {
            try
            {
                bool success = _engine.TryMove(fromX, fromY, toX, toY, promotionType);
                
                if (success)
                {
                    OnPropertyChanged(nameof(Board));
                    UpdateMoveHistory();
                    RefreshProperties();

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
                
            // 1. Получаем геометрические ходы
            var pseudoMoves = piece.GetLegalMoves(_engine.Board, new Position(fromX, fromY));
            
            // 2. Фильтруем их через проверку на шах
            foreach (var move in pseudoMoves)
            {
                int toX = move.X;
                int toY = move.Y;
                
                var captured = _engine.Board[toY, toX];
                
                // Делаем ход временно
                _engine.Board[toY, toX] = piece;
                _engine.Board[fromY, fromX] = null;
                
                bool isCheck = _engine.IsKingInCheck(piece.Color); 
                
                // Откатываем ход
                _engine.Board[fromY, fromX] = piece;
                _engine.Board[toY, toX] = captured;
                
                if (!isCheck)
                {
                    legalMoves.Add((toX, toY));
                }
            }
            
            // 3. ВЗЯТИЕ НА ПРОХОДЕ (En Passant)
            if (piece.Type == PieceType.Pawn && _engine._enPassantTarget.HasValue)
            {
                int epX = _engine._enPassantTarget.Value.X;
                int epY = _engine._enPassantTarget.Value.Y;

                int direction = (piece.Color == PieceColor.White) ? -1 : 1;
                
                // Проверяем, бьет ли пешка эту цель по диагонали
                if (epY == fromY + direction && Math.Abs(epX - fromX) == 1)
                {
                    // Симмулируем взятие
                    var capturedPawn = _engine.Board[fromY, epX];
                    
                    if (capturedPawn != null && capturedPawn.Color != piece.Color && capturedPawn.Type == PieceType.Pawn)
                    {
                        // Ставим пешку на целевую клетку
                        _engine.Board[epY, epX] = piece;       
                        _engine.Board[fromY, fromX] = null;    
                        // Убираем взятую пешку
                        _engine.Board[fromY, epX] = null;      

                        bool isCheckAfterEp = _engine.IsKingInCheck(piece.Color);

                        // Откат
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

            // 4. РОКИРОВКА
            if (piece.Type == PieceType.King && !_engine.IsKingInCheck(piece.Color))
            {
                int y = fromY;
                bool isWhite = piece.Color == PieceColor.White;
                
                // КОРОТКАЯ (O-O)
                bool canCastleKingside = isWhite 
                    ? !_engine._whiteKingMoved && !_engine._whiteRookKingsideMoved 
                    : !_engine._blackKingMoved && !_engine._blackRookKingsideMoved;
                
                if (canCastleKingside)
                {
                    if (_engine.Board[y, 5] == null && _engine.Board[y, 6] == null)
                    {
                        if (!_engine.IsSquareAttacked(5, y, isWhite ? PieceColor.Black : PieceColor.White) &&
                            !_engine.IsSquareAttacked(6, y, isWhite ? PieceColor.Black : PieceColor.White))
                        {
                            // Симмуляция
                            var k = _engine.Board[y, 4]; 
                            _engine.Board[y, 4] = null; 
                            _engine.Board[y, 6] = k;
                            
                            if (!_engine.IsKingInCheck(piece.Color)) 
                                legalMoves.Add((6, y));
                            
                            // Откат
                            _engine.Board[y, 6] = null; 
                            _engine.Board[y, 4] = k;
                        }
                    }
                }

                // ДЛИННАЯ (O-O-O)
                bool canCastleQueenside = isWhite 
                    ? !_engine._whiteKingMoved && !_engine._whiteRookQueensideMoved 
                    : !_engine._blackKingMoved && !_engine._blackRookQueensideMoved;
                
                if (canCastleQueenside)
                {
                    if (_engine.Board[y, 1] == null && _engine.Board[y, 2] == null && _engine.Board[y, 3] == null)
                    {
                        if (!_engine.IsSquareAttacked(3, y, isWhite ? PieceColor.Black : PieceColor.White) &&
                            !_engine.IsSquareAttacked(2, y, isWhite ? PieceColor.Black : PieceColor.White))
                        {
                            // Симмуляция
                            var k = _engine.Board[y, 4]; 
                            _engine.Board[y, 4] = null; 
                            _engine.Board[y, 2] = k;
                            
                            if (!_engine.IsKingInCheck(piece.Color)) 
                                legalMoves.Add((2, y));
                            
                            // Откат
                            _engine.Board[y, 2] = null; 
                            _engine.Board[y, 4] = k;
                        }
                    }
                }
            }
            
            return legalMoves;
        }
        private bool IsMoveLegal(int fromX, int fromY, int toX, int toY)
        {
            var piece = _engine.Board[fromY, fromX];
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
            MoveHistoryList.Clear();
            for (int i = 0; i < moves.Count; i += 2)
            {
                int moveNum = (i / 2) + 1;
                string white = moves[i];
                string black = (i + 1 < moves.Count) ? moves[i + 1] : "";
                MoveHistoryList.Add(new MoveDisplayItem { MoveNumber = moveNum, WhiteMove = white, BlackMove = black });
            }
        }

        private void RefreshProperties()
        {
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(CurrentTurnText));
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