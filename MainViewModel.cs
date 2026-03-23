using System;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using kchess.Services;
using Avalonia.Threading;

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
        public string WhiteMove { get; set; } = " ";
        public string BlackMove { get; set; } = " ";
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ChessEngine _engine;
        private readonly ChessAI? _ai;
        private readonly NeuralEvaluator? _evaluator;

        public GameMode CurrentMode { get; private set; } = GameMode.LocalPvP;
        private bool _aiPlaysWhite = false;
        private bool _isAiCalculating = false;
        
        public bool IsAiCalculating 
        { 
            get => _isAiCalculating; 
            private set 
            {
                _isAiCalculating = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Board));
            }
        }

        public ObservableCollection<MoveDisplayItem> MoveHistoryList { get; }

        public MainViewModel()
        {
            _engine = new ChessEngine();
            MoveHistoryList = new ObservableCollection<MoveDisplayItem>();

            try 
            {
                string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chess_model.onnx");
                
                if (!File.Exists(modelPath))
                    throw new FileNotFoundException($"Файл модели не найден: {modelPath}");

                Console.WriteLine($"✅ Путь к модели верный: {modelPath}");
                _evaluator = new NeuralEvaluator(modelPath);
                _ai = new ChessAI(_evaluator);
                Console.WriteLine("✅ AI загружен успешно.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка загрузки ИИ: {ex.Message}");
                _engine.SetStatus($"Ошибка ИИ: {ex.Message}");
                _ai = null;
                _evaluator = null;
            }
        }

        public Piece?[,] Board => _engine.Board;
        public string StatusMessage => _engine.LastStatus;
        
        public string CurrentTurnText => 
            _engine.IsGameOver ? "Игра окончена" : 
            (_engine.CurrentTurn == PieceColor.White ? "Ход белых" : "Ход черных");

        public void StartGameVsAI(bool playerIsWhite)
        {
            NewGame();
            CurrentMode = GameMode.PvAI;
            _aiPlaysWhite = !playerIsWhite;
            
            SetStatus(playerIsWhite ? "Вы играете белыми против ИИ" : "Вы играете черными против ИИ");

            if (_aiPlaysWhite && _ai != null)
            {
                Console.WriteLine(">>> БОТ ИГРАЕТ БЕЛЫМИ. ЗАПУСК ХОДА НЕМЕДЛЕННО.");
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

        public void MakeAiMove()
        {
            IsAiCalculating = true;             
            
            if (_ai == null || _evaluator == null || _engine.IsGameOver) return;

            try 
            {
                var candidates = new List<(int fromX, int fromY, int toX, int toY)>();
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        var piece = _engine.Board[y, x];
                        if (piece != null && piece.Color == _engine.CurrentTurn)
                        {
                            var moves = GetLegalMoves(x, y);
                            foreach (var move in moves)
                                candidates.Add((x, y, move.x, move.y));
                        }
                    }
                }

                if (candidates.Count == 0) 
                {
                    IsAiCalculating = false;
                    Dispatcher.UIThread.Post(() => SetStatus("Пат или Мат!"));
                    return; 
                }

                var bestMove = _ai.GetBestMove(_engine);

                Dispatcher.UIThread.Post(() =>
                {
                    if (bestMove.HasValue)
                    {
                        TryMakeMove(bestMove.Value.fromX, bestMove.Value.fromY, bestMove.Value.toX, bestMove.Value.toY);
                    }
                    else
                    {
                        SetStatus("ИИ сдался!");
                    }
                    
                    IsAiCalculating = false;
                    OnPropertyChanged(nameof(Board));
                });
            }
            catch (Exception ex)
            {
                IsAiCalculating = false;
                OnPropertyChanged(nameof(Board));
                Console.WriteLine($"[MakeAiMove] КРИТИЧЕСКАЯ ОШИБКА: {ex}");
                Dispatcher.UIThread.Post(() => SetStatus("Ошибка ИИ: " + ex.Message));
            }
        }

        public bool TryMakeMove(int fromX, int fromY, int toX, int toY, PieceType promotionType = PieceType.Queen)
        {
            var result = _engine.TryMakeMove(fromX, fromY, toX, toY, promotionType);

            if (result.Success)
            {
                UpdateMoveHistory();
                RefreshProperties();

                if (CurrentMode == GameMode.PvAI && !_engine.IsGameOver)
                {
                    bool isAiTurn = (_aiPlaysWhite && _engine.CurrentTurn == PieceColor.White) ||
                                    (!_aiPlaysWhite && _engine.CurrentTurn == PieceColor.Black);

                    if (isAiTurn && _ai != null)
                    {
                        SetStatus("🤖 ИИ думает...");
                        System.Threading.Tasks.Task.Run(() => MakeAiMove());
                    }
                    else
                    {
                        // ХОД ПЕРЕШЕЛ К ИГРОКУ, нужно обновить доску
                        OnPropertyChanged(nameof(Board));
                    }
                }
                
                return true;
            }
            
            RefreshProperties();
            return false;
        }

        // ВАЖНО: Этот метод теперь делает СИМУЛЯЦИЮ вручную, чтобы избежать рекурсии с TryMakeMove
        public List<(int x, int y)> GetLegalMoves(int fromX, int fromY)
        {
            var legalMoves = new List<(int x, int y)>();
            if (_engine.Board[fromY, fromX] == null) return legalMoves;

            var piece = _engine.Board[fromY, fromX];
            if (piece.Color != _engine.CurrentTurn) return legalMoves;
            
            var pseudoMoves = piece.GetLegalMoves(_engine.Board, new Position(fromX, fromY));
            
            foreach (var move in pseudoMoves)
            {
                int toX = move.X;
                int toY = move.Y;
                
                // Сохраняем состояние для отката
                var captured = _engine.Board[toY, toX];
                var sourcePiece = _engine.Board[fromY, fromX];
                
                // Особая логика для En Passant при симуляции
                bool isEpCapture = false;
                Piece? epCapturedPawn = null;
                int epPawnY = -1;

                if (piece.Type == PieceType.Pawn && toX != fromX && captured == null)
                {
                    if (_engine._enPassantTarget.HasValue && 
                        _engine._enPassantTarget.Value.X == toX && 
                        _engine._enPassantTarget.Value.Y == toY)
                    {
                        isEpCapture = true;
                        epPawnY = fromY; // Пешка противника стоит на ряду атакующей
                        epCapturedPawn = _engine.Board[epPawnY, toX];
                        if (epCapturedPawn != null)
                            _engine.Board[epPawnY, toX] = null; // Временно убираем
                    }
                }

                // Делаем ход на доске
                _engine.Board[toY, toX] = sourcePiece;
                _engine.Board[fromY, fromX] = null;

                // Проверяем шах
                bool inCheck = _engine.IsKingInCheck(piece.Color);

                // Откатываем ход
                _engine.Board[fromY, fromX] = sourcePiece;
                _engine.Board[toY, toX] = captured;
                
                // Восстанавливаем пешку при EP
                if (isEpCapture && epCapturedPawn != null)
                {
                    _engine.Board[epPawnY, toX] = epCapturedPawn;
                }

                if (!inCheck)
                {
                    legalMoves.Add((toX, toY));
                }
            }
            
            // Проверка рокировок (упрощенная, через прямую проверку условий)
            if (piece.Type == PieceType.King && !_engine.IsKingInCheck(piece.Color))
            {
                int y = fromY;
                bool isWhite = piece.Color == PieceColor.White;
                bool kingside = isWhite ? (!_engine._whiteKingMoved && !_engine._whiteRookKingsideMoved) 
                                        : (!_engine._blackKingMoved && !_engine._blackRookKingsideMoved);
                bool queenside = isWhite ? (!_engine._whiteKingMoved && !_engine._whiteRookQueensideMoved) 
                                         : (!_engine._blackKingMoved && !_engine._blackRookQueensideMoved);

                // Kingside
                if (kingside && _engine.Board[y, 5] == null && _engine.Board[y, 6] == null)
                {
                    if (!_engine.IsSquareAttacked(5, y, isWhite ? PieceColor.Black : PieceColor.White) &&
                        !_engine.IsSquareAttacked(6, y, isWhite ? PieceColor.Black : PieceColor.White))
                    {
                        legalMoves.Add((6, y));
                    }
                }
                // Queenside
                if (queenside && _engine.Board[y, 1] == null && _engine.Board[y, 2] == null && _engine.Board[y, 3] == null)
                {
                    if (!_engine.IsSquareAttacked(3, y, isWhite ? PieceColor.Black : PieceColor.White) &&
                        !_engine.IsSquareAttacked(2, y, isWhite ? PieceColor.Black : PieceColor.White))
                    {
                        legalMoves.Add((2, y));
                    }
                }
            }

            return legalMoves;
        }

        private void UpdateMoveHistory()
        {
            MoveHistoryList.Clear();
            var moves = _engine.MoveHistory;
            for (int i = 0; i < moves.Count; i += 2)
            {
                MoveHistoryList.Add(new MoveDisplayItem 
                { 
                    MoveNumber = (i / 2) + 1, 
                    WhiteMove = moves[i], 
                    BlackMove = (i + 1 < moves.Count) ? moves[i + 1] : " " 
                });
            }
        }

        private void RefreshProperties()
        {
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(CurrentTurnText));
            OnPropertyChanged(nameof(Board)); // Форсируем обновление доски
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