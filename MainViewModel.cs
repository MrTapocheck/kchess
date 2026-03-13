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
            // Если мы в режиме выбора фигуры, обычные ходы блокируются
            if (_isWaitingForPromotion)
            {
                // Можно добавить звук ошибки или игнорировать
                return;
            }

            try
            {
                // Пытаемся сделать ход в движке
                bool success = _engine.TryMove(fromX, fromY, toX, toY);
                
                if (success)
                {
                    // Ход успешен, обновляем интерфейс
                    RefreshProperties();
                }
                else
                {
                    // Ход недопустим (движок вернул false), обновляем только статус с ошибкой
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }
            catch (PawnPromotionRequiredException ex)
            {
                // Движок выбросил исключение: пешка дошла до края!
                _isWaitingForPromotion = true;
                _promotionPosition = new Position(ex.X, ex.Y);
                
                OnPropertyChanged(nameof(IsWaitingForPromotion));
                OnPropertyChanged(nameof(PromotionPosition));
                OnPropertyChanged(nameof(StatusMessage));
            }
            catch (Exception ex)
            {
                // Этот блок никогда не выполнится, так как первый catch(Exception) перехватит всё.
                // Нужно убрать дубликат или сделать более специфичным первый блок.
                // Но пока оставим как есть, просто исправим вызов:
                _engine.SetStatus($"Ошибка: {ex.Message}");
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        /// <summary>
        /// Выбор фигуры для превращения пешки.
        /// Вызывается когда пользователь кликнул на иконку (Ферзь, Ладья и т.д.) в UI.
        /// </summary>
        public void SelectPromotionPiece(PieceType type)
        {
            if (!_isWaitingForPromotion)
            {
                throw new InvalidOperationException("Сейчас не требуется выбор фигуры.");
            }

            try
            {
                // Передаем выбор в движок
                _engine.CompletePromotion(type);
                
                // Превращение успешно, выходим из режима ожидания
                _isWaitingForPromotion = false;
                _promotionPosition = null;
                
                RefreshProperties();
            }
            catch (ArgumentException ex)
            {
                // Пользователь выбрал пешку или короля (недопустимо)
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