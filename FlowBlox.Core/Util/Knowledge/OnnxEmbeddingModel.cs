using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FlowBlox.Core.Util.Knowledge;

internal sealed class OnnxEmbeddingModel : IDisposable
{
    private readonly InferenceSession _session;
    private readonly OnnxEmbeddingTokenizer _tokenizer;
    private static readonly string[] TokenizerFiles =
        ["config.json", "tokenizer.json", "tokenizer_config.json", "special_tokens_map.json", "sentence_bert_config.json"];

    internal OnnxEmbeddingModel(string folder)
    {
        _tokenizer = new OnnxEmbeddingTokenizer(folder);
        var modelPath = ResolveModelPath(folder);
        Profile = GetProfile(folder, modelPath);
        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4)
        };
        _session = new InferenceSession(modelPath, options);
        if (!_session.InputMetadata.ContainsKey("input_ids")
            || !_session.InputMetadata.ContainsKey("attention_mask")
            || !_session.OutputMetadata.ContainsKey("last_hidden_state"))
        {
            _session.Dispose();
            throw new InvalidDataException("Expected a transformer ONNX model with input_ids, attention_mask and last_hidden_state for mean pooling.");
        }
    }

    internal string Profile { get; }

    internal static void ValidateFiles(string folder)
    {
        _ = ResolveModelPath(folder);
        foreach (var file in TokenizerFiles)
        {
            if (!File.Exists(Path.Combine(folder, file)))
                throw new FileNotFoundException($"Embedding model file missing: {file}. Configure ModelFolder with a complete ONNX embedding model and matching tokenizer files.");
        }
    }

    internal IReadOnlyList<string> SplitText(string content, int size, int overlap) =>
        _tokenizer.SplitText(content, size, overlap);

    internal float[] Embed(string text, CancellationToken cancellationToken)
    {
        var ids = _tokenizer.Encode(text);
        var mask = Enumerable.Repeat(1L, ids.Length).ToArray();
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, [1, ids.Length])),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, [1, ids.Length]))
        };
        if (_session.InputMetadata.ContainsKey("token_type_ids"))
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>([1, ids.Length])));

        using var runOptions = new RunOptions();
        using var cancellation = cancellationToken.Register(() => runOptions.Terminate = true);
        cancellationToken.ThrowIfCancellationRequested();
        using var results = _session.Run(inputs, ["last_hidden_state"], runOptions);
        cancellationToken.ThrowIfCancellationRequested();
        return Pool(results.Single().AsTensor<float>(), mask);
    }

    internal static float[] Pool(Tensor<float> hiddenState, IReadOnlyList<long> mask)
    {
        if (hiddenState.Rank != 3 || hiddenState.Dimensions[0] != 1 || hiddenState.Dimensions[1] != mask.Count)
            throw new InvalidDataException("Expected token embeddings with shape [1, sequence, dimensions].");

        var vector = new float[hiddenState.Dimensions[2]];
        var count = 0L;
        for (var token = 0; token < mask.Count; token++)
        {
            if (mask[token] == 0)
                continue;
            count++;
            for (var dimension = 0; dimension < vector.Length; dimension++)
                vector[dimension] += hiddenState[0, token, dimension];
        }
        if (count == 0)
            throw new InvalidDataException("The embedding attention mask contains no text tokens.");

        for (var dimension = 0; dimension < vector.Length; dimension++)
            vector[dimension] /= count;
        VectorMath.Validate(vector);
        var norm = Math.Sqrt(vector.Sum(value => (double)value * value));
        for (var dimension = 0; dimension < vector.Length; dimension++)
            vector[dimension] = (float)(vector[dimension] / norm);
        return vector;
    }

    private static string ResolveModelPath(string folder)
    {
        foreach (var relativePath in new[] { "model.onnx", "onnx/model_quint8_avx2.onnx" })
        {
            var path = Path.Combine(folder, relativePath);
            if (File.Exists(path))
                return path;
        }
        throw new FileNotFoundException("ONNX embedding model missing. Configure ModelFolder with model.onnx or onnx/model_quint8_avx2.onnx and matching tokenizer files.");
    }

    private static string GetProfile(string folder, string modelPath)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var tokenizerPaths = TokenizerFiles.Concat(["sentencepiece.bpe.model", "vocab.txt"])
            .Select(file => Path.Combine(folder, file))
            .Where(File.Exists);
        foreach (var path in new[] { modelPath }.Concat(tokenizerPaths))
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer)) > 0)
                hash.AppendData(buffer.AsSpan(0, read));
        }
        return "onnx-mean-pooling-v1|" + Convert.ToHexString(hash.GetHashAndReset());
    }

    public void Dispose() => _session.Dispose();
}
