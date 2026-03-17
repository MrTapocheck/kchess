using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Collections.ObjectModel; // Важно для ObservableCollection
using kchess;
using System.Collections.Generic;

namespace kchess
{
    /// <summary>
    /// Модель одного элемента в списке истории ходов.
    /// </summary>
    public class MoveDisplayItem
    {
        public int MoveNumber { get; set; } // Номер хода (1, 2, 3...)
        public string WhiteMove { get; set; } = ""; // Ход белых (например, "e2-e4")
        public string BlackMove { get; set; } = ""; // Ход черных (пусто, если ход еще не сделан)
        
        // Свойства для иконок (можно парсить строку, но пока оставим текст)
        // В будущем тут можно добавить пути к картинкам
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ChessEngine _engine;
        
        // Коллекция для истории ходов в UI
        public ObservableCollection<MoveDisplayItem> MoveHistoryList { get; }

        public MainViewModel()
        {
            _engine = new ChessEngine();
            MoveHistoryList = new ObservableCollection<MoveDisplayItem>();
        }

        public Piece?[,] Board => _engine.Board;
        public string StatusMessage => _engine.LastStatus;
        public string CurrentTurnText => 
            _engine.IsGameOver ? "Игра окончена" : 
            (_engine.CurrentTurn == PieceColor.White ? "Ход белых" : "Ход черных");

        // Для фантомчиков
        public List<(int x, int y)> GetLegalMoves(int fromX, int fromY)
        {
            var legalMoves = new List<(int x, int y)>();
            var engine = _engine; 
            
            if (engine == null || engine.Board[fromY, fromX] == null) return legalMoves;

            var piece = engine.Board[fromY, fromX];
            if (piece == null || piece.Color != engine.CurrentTurn) 
                return legalMoves;
                
            // 1. Получаем геометрические ходы (пешки, кони, слоны и т.д.)
            var pseudoMoves = piece.GetLegalMoves(engine.Board, new Position(fromX, fromY));
            
            // 2. Фильтруем их через проверку на шах
            foreach (var move in pseudoMoves)
            {
                int toX = move.X;
                int toY = move.Y;
                
                // --- ПЕСОЧНИЦА ---
                var captured = engine.Board[toY, toX];
                
                // Делаем ход временно
                engine.Board[toY, toX] = piece;
                engine.Board[fromY, fromX] = null;
                
                // Проверяем шах
                bool isCheck = engine.IsKingInCheck(piece.Color); 
                
                // Откатываем ход
                engine.Board[fromY, fromX] = piece;
                engine.Board[toY, toX] = captured;
                
                if (!isCheck)
                {
                    legalMoves.Add((toX, toY));
                }
            }
            
            // 3. === ОТДЕЛЬНАЯ ПРОВЕРКА НА ВЗЯТИЕ НА ПРОХОДЕ (EN PASSANT) ===
            // Геометрические ходы пешки не включают диагональный ход на пустую клетку.
            // Поэтому мы проверяем его вручную здесь.
            if (piece.Type == PieceType.Pawn)
            {
                if (engine._enPassantTarget.HasValue)
                {
                    int epX = engine._enPassantTarget.Value.X;
                    int epY = engine._enPassantTarget.Value.Y;

                    // Проверяем, бьет ли наша пешка эту цель
                    // Условие: целевая клетка должна быть по диагонали спереди от пешки
                    int direction = (piece.Color == PieceColor.White) ? -1 : 1;
                    
                    if (epY == fromY + direction && Math.Abs(epX - fromX) == 1)
                    {
                        // Это кандидат на взятие! Теперь проверим, не под шахом ли мы окажемся
                        // Для этого симулируем ход:
                        // 1. Ставим пешку на target (epX, epY)
                        // 2. Убираем вражескую пешку, которая стоит рядом (fromY, epX)
                        
                        var capturedPawn = engine.Board[fromY, epX];
                        
                        // Если там нет вражеской пешки — странно, но пропускаем
                        if (capturedPawn != null && capturedPawn.Color != piece.Color && capturedPawn.Type == PieceType.Pawn)
                        {
                            // Симмуляция
                            engine.Board[epY, epX] = piece;       // Наша пешка пришла
                            engine.Board[fromY, fromX] = null;    // Старая позиция пуста
                            engine.Board[fromY, epX] = null;      // Вражеская пешка убита

                            bool isCheckAfterEp = engine.IsKingInCheck(piece.Color);

                            // Откат симуляции
                            engine.Board[fromY, fromX] = piece;
                            engine.Board[epY, epX] = null;
                            engine.Board[fromY, epX] = capturedPawn;

                            if (!isCheckAfterEp)
                            {
                                // ВСЁ ЧИСТО! Добавляем координату взятия в список
                                legalMoves.Add((epX, epY));
                            }
                        }
                    }
                }
            }
            // ================================================================

            // 4. === ДОБАВЛЯЕМ РОКИРОВКУ ===
            if (piece.Type == PieceType.King && !engine.IsKingInCheck(piece.Color))
            {
                int y = fromY;
                bool isWhite = piece.Color == PieceColor.White;
                
                // --- КОРОТКАЯ РОКИРОВКА (O-O) ---
                bool canCastleKingside = isWhite ? !engine._whiteKingMoved && !engine._whiteRookKingsideMoved 
                                                 : !engine._blackKingMoved && !engine._blackRookKingsideMoved;
                
                if (canCastleKingside)
                {
                    if (engine.Board[y, 5] == null && engine.Board[y, 6] == null)
                    {
                        if (!engine.IsSquareAttacked(5, y, isWhite ? PieceColor.Black : PieceColor.White) &&
                            !engine.IsSquareAttacked(6, y, isWhite ? PieceColor.Black : PieceColor.White))
                        {
                            var k = engine.Board[y, 4]; engine.Board[y, 4] = null; engine.Board[y, 6] = k;
                            if (!engine.IsKingInCheck(piece.Color)) legalMoves.Add((6, y));
                            engine.Board[y, 6] = null; engine.Board[y, 4] = k;
                        }
                    }
                }

                // --- ДЛИННАЯ РОКИРОВКА (O-O-O) ---
                bool canCastleQueenside = isWhite ? !engine._whiteKingMoved && !engine._whiteRookQueensideMoved 
                                                  : !engine._blackKingMoved && !engine._blackRookQueensideMoved;
                
                if (canCastleQueenside)
                {
                    if (engine.Board[y, 1] == null && engine.Board[y, 2] == null && engine.Board[y, 3] == null)
                    {
                        if (!engine.IsSquareAttacked(3, y, isWhite ? PieceColor.Black : PieceColor.White) &&
                            !engine.IsSquareAttacked(2, y, isWhite ? PieceColor.Black : PieceColor.White))
                        {
                            var k = engine.Board[y, 4]; engine.Board[y, 4] = null; engine.Board[y, 2] = k;
                            if (!engine.IsKingInCheck(piece.Color)) legalMoves.Add((2, y));
                            engine.Board[y, 2] = null; engine.Board[y, 4] = k;
                        }
                    }
                }
            }
            
            return legalMoves;
        }

        // Метод обновления истории (вызывать после каждого хода)
        private void UpdateMoveHistory()
        {
            var moves = _engine.MoveHistory;
            
            // Очищаем и заполняем заново (простой способ, для 100 ходов быстро)
            MoveHistoryList.Clear();
            
            for (int i = 0; i < moves.Count; i += 2)
            {
                int moveNum = (i / 2) + 1;
                string white = moves[i];
                string black = (i + 1 < moves.Count) ? moves[i + 1] : "";

                MoveHistoryList.Add(new MoveDisplayItem
                {
                    MoveNumber = moveNum,
                    WhiteMove = white,
                    BlackMove = black
                });
            }
        }

        public void TryMakeMove(int fromX, int fromY, int toX, int toY, PieceType promotionType = PieceType.Queen)
        {
            try
            {
                // Пытаемся сделать ход через движок.                
                bool success = _engine.TryMove(fromX, fromY, toX, toY, promotionType);
                
                if (success)
                {
                    OnPropertyChanged(nameof(Board));
                    UpdateMoveHistory();
                    RefreshProperties();
                }
                else
                {
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }
            catch (Exception ex)
            {
                _engine.SetStatus($"Ошибка: {ex.Message}");
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        // Метод для новой игры
        public void NewGame()
        {
            // ПОКА ЗАГЛУШКА:
            _engine.InitializeBoard(); // Вызываем полную перезагрузку
            MoveHistoryList.Clear();   // Очищаем UI список
            RefreshProperties();       // Обновляем статусы
            OnPropertyChanged(nameof(Board)); // Сообщаем UI, что доска изменилась
        }
        
        // Нужно сделать InitializeBoard публичным в ChessEngine или добавить Reset()
        // Давай в следующем шаге поправим ChessEngine.

        private void RefreshProperties()
        {
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(CurrentTurnText));
            if (_engine.IsGameOver) OnPropertyChanged(nameof(CurrentTurnText));
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