using System;
using System.Linq;
using kchess;

namespace kchess
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Парсим аргументы. 
            // dotnet run -- --console  -> args будет содержать "--console"
            bool consoleMode = args.Contains("--console");

            // Если передан флаг --console, запускаем только консоль.
            // Иначе (пока) ничего не делаем или можно вывести справку, 
            // так как GUI мы временно отключили по твоему запросу.
            
            if (!consoleMode)
            {
                Console.WriteLine("Графический интерфейс пока отключен.");
                Console.WriteLine("Запускайте с флагом --console для игры:");
                Console.WriteLine("dotnet run -- --console");
                return;
            }

            // 1. Создаем ViewModel (Логика + Состояние)
            var viewModel = new MainViewModel();

            // 2. Создаем View (Консоль)
            var view = new ConsoleView(viewModel);

            Console.WriteLine("Запуск kchess в консольном режиме...");
            System.Threading.Thread.Sleep(1000);

            bool running = true;
            while (running)
            {
                // 1. Отрисовка
                view.Render();

                // 2. Получение ввода
                var action = view.GetInput();

                // 3. Обработка действия
                switch (action.Type)
                {
                    case UserActionType.Exit:
                        running = false;
                        break;

                    case UserActionType.None:
                        // Ошибка ввода, цикл продолжится, Render очистит экран и покажет ошибку (если добавить её в статус)
                        // Сейчас ошибка уже выведена в GetInput, просто ждем следующего ввода
                        break;

                    case UserActionType.Move:
                        // Передаем ход в ViewModel
                        viewModel.TryMakeMove(action.FromX, action.FromY, action.ToX, action.ToY);
                        break;

                    case UserActionType.Promotion:
                        // Передаем выбор фигуры в ViewModel
                        if (action.PromoteTo.HasValue)
                        {
                            try
                            {
                                viewModel.SelectPromotionPiece(action.PromoteTo.Value);
                            }
                            catch (Exception ex)
                            {
                                // Если выбор неверный (например, выбрали короля), ловим и пишем в статус
                                // В текущей реализации VM кидает исключение, которое надо бы обработать красиво,
                                // но пока пусть падает в консоль или можно добавить обработку в VM.
                                // Для простоты: VM внутри SelectPromotionPiece уже должна быть безопасной или кидать.
                                // Давай добавим try-catch здесь для надежности.
                                Console.WriteLine($"Ошибка превращения: {ex.Message}");
                                System.Threading.Thread.Sleep(2000);
                            }
                        }
                        break;
                }
            }

            Console.WriteLine("Игра окончена. До свидания!");
        }
    }
}