using System;
using System.IO;
using System.Linq;
using System.Collections.Generic; // <--- Добавлено
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace kchess.Services
{
    public class NeuralEvaluator : IDisposable
    {
        // Модель принимает: [batch, 12, 8, 8]
        public const int InputChannels = 12;
        public const int InputHeight = 8;
        public const int InputWidth = 8;
        public const int InputSize = InputChannels * InputHeight * InputWidth; // 12*8*8 = 768

        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly string _outputName;
        private readonly float[] _inputBuffer;
        private readonly DenseTensor<float> _inputTensor;

        public NeuralEvaluator(string modelPath)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"Модель не найдена: {modelPath}");

            // Пытаемся выполнить инференс на GPU (CUDA). Если не получилось — фолбэчим на CPU.
            // Это позволяет поддержать и GTX 1070, и более новые карты.
            SessionOptions sessionOptions;
            try
            {
                sessionOptions = SessionOptions.MakeSessionOptionWithCudaProvider(deviceId: 0);
            }
            catch
            {
                sessionOptions = new SessionOptions();
            }

            _session = new InferenceSession(modelPath, sessionOptions);
            _inputName = _session.InputMetadata.Keys.First();
            _outputName = _session.OutputMetadata.Keys.First();

            // 1 x 12 x 8 x 8
            _inputBuffer = new float[1 * 12 * 8 * 8];
            _inputTensor = new DenseTensor<float>(_inputBuffer, new[] { 1, 12, 8, 8 });
        }

        public float Evaluate(Piece?[,] board)
        {
            // Полностью обновляем вход, чтобы избежать "хвостов" от предыдущих позиций.
            Array.Clear(_inputBuffer, 0, _inputBuffer.Length);

            var tensor = _inputTensor;

            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var piece = board[y, x];
                    if (piece != null)
                    {
                        int channel = GetChannel(piece);
                        int row = y; 
                        int col = x;
                        tensor[0, channel, row, col] = 1.0f;
                    }
                }
            }

            var inputs = new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };

            using (var results = _session.Run(inputs))
            {
                var outputTensor = results.First().AsTensor<float>();
                var scores = outputTensor.ToArray();

                // Модель обучалась с nn.CrossEntropyLoss(), значит выход — это логиты.
                // В ONNX softmax внутри loss не выполняется, поэтому делаем softmax здесь.
                // Порядок классов: 0 => "1-0" (white), 1 => "1/2-1/2" (draw), 2 => "0-1" (black).
                float l0 = scores[0];
                float l1 = scores[1];
                float l2 = scores[2];

                float maxLogit = MathF.Max(l0, MathF.Max(l1, l2));
                float e0 = MathF.Exp(l0 - maxLogit);
                float e1 = MathF.Exp(l1 - maxLogit);
                float e2 = MathF.Exp(l2 - maxLogit);
                float denom = e0 + e1 + e2;

                float pWhite = e0 / denom;
                float pBlack = e2 / denom;

                return pWhite - pBlack;
            }
        }

        // Заполняет encoding одной позиции в уже выделенный batchInput.
        // batchInput при этом должен иметь длину: batchSize * InputSize.
        public void EncodeBoard(Piece?[,] board, float[] batchInput, int batchIndex)
        {
            int baseOffset = batchIndex * InputSize; // в элементах float

        // Очищаем только текущий сегмент (768 флоатов) перед заполнением
        Array.Clear(batchInput, baseOffset, InputSize); 
        
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var piece = board[y, x];
                    if (piece == null) continue;

                    int channel = GetChannel(piece); // 0..11
                    // layout плоского массива при DenseTensor [batch, ch, y, x]:
                    // offset = ((batch*12 + ch)*8 + y)*8 + x = batch*768 + ch*64 + y*8 + x
                    int offset = baseOffset + channel * 64 + y * 8 + x;
                    batchInput[offset] = 1.0f;
                }
            }
        }

        // Прогоняет модель сразу для batchSize позиций.
        // Возвращает: whiteWinProb - blackWinProb для каждой позиции.
        public float[] EvaluateBatch(float[] batchInput, int batchSize)
        {
            var tensor = new DenseTensor<float>(
                batchInput,
                new[] { batchSize, InputChannels, InputHeight, InputWidth }
            );

            var inputs = new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };

            using (var results = _session.Run(inputs))
            {
                var outputTensor = results.First().AsTensor<float>();
                var flat = outputTensor.ToArray();

                // Ожидаем [batch, 3] или flatten [batch*3]
                if (flat.Length != batchSize * 3)
                {
                    // Допускаем частный случай [3] при batchSize==1
                    if (!(batchSize == 1 && flat.Length == 3))
                        throw new InvalidOperationException($"Unexpected model output size: {flat.Length}. Expected {batchSize * 3}.");
                }

                var result = new float[batchSize];
                for (int i = 0; i < batchSize; i++)
                {
                    // Модель выдает логиты [batch,3], применяем softmax к каждому набору из 3.
                    float l0 = flat[i * 3 + 0];
                    float l1 = flat[i * 3 + 1];
                    float l2 = flat[i * 3 + 2];

                    float maxLogit = MathF.Max(l0, MathF.Max(l1, l2));
                    float e0 = MathF.Exp(l0 - maxLogit);
                    float e1 = MathF.Exp(l1 - maxLogit);
                    float e2 = MathF.Exp(l2 - maxLogit);
                    float denom = e0 + e1 + e2;

                    float pWhite = e0 / denom;
                    float pBlack = e2 / denom;

                    result[i] = pWhite - pBlack;
                }

                return result;
            }
        }

        private int GetChannel(Piece piece)
        {
            int offset = (piece.Color == PieceColor.White) ? 0 : 6;
            return piece.Type switch
            {
                PieceType.Pawn => 0 + offset,
                PieceType.Knight => 1 + offset,
                PieceType.Bishop => 2 + offset,
                PieceType.Rook => 3 + offset,
                PieceType.Queen => 4 + offset,
                PieceType.King => 5 + offset,
                _ => throw new ArgumentException($"Неизвестная фигура: {piece.Type}")
            };
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }
}