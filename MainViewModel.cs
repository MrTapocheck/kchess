using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Collections.ObjectModel; // Важно для ObservableCollection
using kchess;

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
        private bool _isWaitingForPromotion;
        private Position? _promotionPosition;
        
        // Коллекция для истории ходов в UI
        public ObservableCollection<MoveDisplayItem> MoveHistoryList { get; }

        public MainViewModel()
        {
            _engine = new ChessEngine();
            _isWaitingForPromotion = false;
            MoveHistoryList = new ObservableCollection<MoveDisplayItem>();
        }

        public Piece?[,] Board => _engine.Board;
        public string StatusMessage => _engine.LastStatus;
        public string CurrentTurnText => 
            _engine.IsGameOver ? "Игра окончена" : 
            (_engine.CurrentTurn == PieceColor.White ? "Ход белых" : "Ход черных");
        
        public bool IsWaitingForPromotion => _isWaitingForPromotion;
        public Position? PromotionPosition => _promotionPosition;

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

        public void TryMakeMove(int fromX, int fromY, int toX, int toY)
        {
            if (_isWaitingForPromotion) return;

            try
            {
                bool success = _engine.TryMove(fromX, fromY, toX, toY);
                
                if (success)
                {
                    OnPropertyChanged(nameof(Board));
                    UpdateMoveHistory(); // Обновляем список ходов
                    RefreshProperties();
                }
                else
                {
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }
            catch (PawnPromotionRequiredException ex)
            {
                _isWaitingForPromotion = true;
                _promotionPosition = new Position(ex.X, ex.Y);
                OnPropertyChanged(nameof(IsWaitingForPromotion));
                OnPropertyChanged(nameof(PromotionPosition));
                OnPropertyChanged(nameof(StatusMessage));
            }
            catch (Exception ex)
            {
                _engine.SetStatus($"Ошибка: {ex.Message}");
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public void SelectPromotionPiece(PieceType type)
        {
            if (!_isWaitingForPromotion) throw new InvalidOperationException("Нет превращения.");

            try
            {
                _engine.CompletePromotion(type);
                _isWaitingForPromotion = false;
                _promotionPosition = null;
                
                OnPropertyChanged(nameof(Board));
                UpdateMoveHistory(); // Обновляем список после превращения
                RefreshProperties();
            }
            catch (ArgumentException ex)
            {
                _engine.SetStatus($"Ошибка: {ex.Message}");
                OnPropertyChanged(nameof(StatusMessage));
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
            // Пересоздаем движок или инициируем заново
            // Проще создать новый экземпляр ChessEngine, но тогда нужно сбросить всё
            // В ChessEngine нет метода Reset, давай создадим новый
            // Но лучше добавить метод Reset в ChessEngine. Пока сделаем так:
            
            // Хак: создаем новый движок (в реальном проекте лучше сделать метод Reset)
            // Так как поле _engine readonly, нам нужно немного хитрости или рефакторинг.
            // Давай просто создадим новый ViewModel? Нет, это сложно для UI.
            
            // Добавим метод Reset в ChessEngine позже. А пока:
            // Временное решение: перезапуск приложения? Нет.
            // Давайте добавим метод в ChessEngine прямо сейчас.
            
            // НО ТАК КАК МЫ НЕ МОЖЕМ МЕНЯТЬ ChessEngine ПРЯМО СЕЙЧАС БЕЗ РИСКА,
            // давай предположим, что ты добавишь простой метод Clear() в ChessEngine.
            // ИЛИ: мы просто создадим новый экземпляр через рефлексию? Нет.
            
            // ЛУЧШЕ: Добавь в ChessEngine метод public void Reset() { ... }
            // И вызови его здесь.
            
            // ПОКА ЗАГЛУШКА:
            _engine.InitializeBoard(); // Если сделать этот метод публичным
            MoveHistoryList.Clear();
            RefreshProperties();
            OnPropertyChanged(nameof(Board));
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