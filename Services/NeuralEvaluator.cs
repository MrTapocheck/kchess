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
        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly string _outputName;

        public NeuralEvaluator(string modelPath)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"Модель не найдена: {modelPath}");

            var sessionOptions = new SessionOptions();
            _session = new InferenceSession(modelPath, sessionOptions);
            _inputName = _session.InputMetadata.Keys.First();
            _outputName = _session.OutputMetadata.Keys.First();
        }

        public float Evaluate(Piece?[,] board)
        {
            var tensor = new DenseTensor<float>(new[] { 1, 12, 8, 8 });

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

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputName, tensor)
            };

            using (var results = _session.Run(inputs))
            {
                var outputTensor = results.First().AsTensor<float>();
                var scores = outputTensor.ToArray();

                float whiteWinProb = scores[0];
                float blackWinProb = scores[2];

                return whiteWinProb - blackWinProb;
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