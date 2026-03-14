using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace kchess
{

    /// <summary>
    /// ViewModel выступает посредником между логикой (ChessEngine) и интерфейсом.
    /// Она преобразует исключения движка в понятные состояния для UI.
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ChessEngine _engine;
        
        // Флаг: ждем ли мы выбора фигуры для превращения пешки?
        private bool _isWaitingForPromotion;
        
        // Координаты пешки, которую нужно превратить (чтобы подсветить их в UI)
        private Position? _promotionPosition;

        public MainViewModel()
        {
            _engine = new ChessEngine();
            _isWaitingForPromotion = false;
        }

            /// <summary>
            /// Публичный метод для установки статуса из View (например, из GUI).
            /// </summary>
            public void SetStatus(string message)
            {
            // Мы не можем напрямую изменить private set свойство LastStatus в движке,
            // но мы можем использовать существующий метод движка SetStatus, если он есть.
            // Если нет, то придется хитрить. 
            // В ChessEngine у нас есть public void SetStatus(string msg).
            _engine.SetStatus(message);
            OnPropertyChanged(nameof(StatusMessage));
            }

        // --- Данные для отображения (View будет читать это) ---

        /// <summary>
        /// Двумерный массив фигур. 
        /// View должен пробежаться по нему и отрисовать каждую клетку.
        /// </summary>
        public Piece?[,] Board => _engine.Board;

        /// <summary>
        /// Текстовый статус игры (ошибки, чей ход, мат).
        /// </summary>
        public string StatusMessage => _engine.LastStatus;

        /// <summary>
        /// Чей сейчас ход (для отображения в заголовке или панели).
        /// </summary>
        public string CurrentTurnText => 
            _engine.IsGameOver ? "Игра окончена" : 
            (_engine.CurrentTurn == PieceColor.White ? "Ход белых" : "Ход черных");

        /// <summary>
        /// Индикатор того, что UI должен показать окно выбора фигуры.
        /// </summary>
        public bool IsWaitingForPromotion => _isWaitingForPromotion;

        /// <summary>
        /// Позиция на доске, где требуется выбор фигуры.
        /// </summary>
        public Position? PromotionPosition => _promotionPosition;

        // --- Действия пользователя (View будет вызывать это) ---

        /// <summary>
        /// Обработка попытки хода.
        /// Вызывается из View при клике или вводе координат.
        /// </summary>
        public void TryMakeMove(int fromX, int fromY, int toX, int toY)
        {
            if (_isWaitingForPromotion)
            {
                return;
            }

            try
            {
                bool success = _engine.TryMove(fromX, fromY, toX, toY);
                
                if (success)
                {
                    // ВАЖНО: Сообщаем UI, что массив Board изменился!
                    OnPropertyChanged(nameof(Board));
                    
                    RefreshProperties(); // Обновляет статус и текст хода
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
                // Board менять не надо, фигура еще на месте до выбора превращения
            }
            catch (Exception ex)
            {
                _engine.SetStatus($"Критическая ошибка: {ex.Message}");
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public void SelectPromotionPiece(PieceType type)
        {
            if (!_isWaitingForPromotion)
            {
                throw new InvalidOperationException("Сейчас не требуется выбор фигуры.");
            }

            try
            {
                _engine.CompletePromotion(type);
                
                _isWaitingForPromotion = false;
                _promotionPosition = null;
                
                // ВАЖНО: После превращения доска изменилась (пешка стала ферзем)
                OnPropertyChanged(nameof(Board));
                
                RefreshProperties();
            }
            catch (ArgumentException ex)
            {
                _engine.SetStatus($"Ошибка превращения: {ex.Message}");
                OnPropertyChanged(nameof(StatusMessage));
            }
            catch (Exception ex)
            {
                _engine.SetStatus($"Критическая ошибка: {ex.Message}");
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
        /// <summary>
        /// Вспомогательный метод для уведомления об изменении всех основных свойств.
        /// </summary>
        private void RefreshProperties()
        {
            OnPropertyChanged(nameof(Board));
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(CurrentTurnText));
            // Проверяем, не закончилась ли игра вдруг
            if (_engine.IsGameOver)
            {
                OnPropertyChanged(nameof(CurrentTurnText));
            }
        }

        // Стандартная реализация INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}