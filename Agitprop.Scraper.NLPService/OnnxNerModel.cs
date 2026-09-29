using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Agitprop.Scraper.NLPService;

internal interface INerModel : IDisposable
{
    float[] Predict(long[] inputIds);
}

internal sealed class OnnxNerModel : INerModel
{
    private readonly InferenceSession _session;
    private readonly string _inputIdsName;
    private readonly string _attentionMaskName;
    private readonly string? _tokenTypeIdsName;
    private readonly string _logitsName;
    private readonly int _labelCount;

    internal OnnxNerModel(string modelPath, int labelCount)
    {
        _labelCount = labelCount;
        var session = new InferenceSession(modelPath);
        try
        {
            _inputIdsName = FindRequiredName(session.InputMetadata.Keys, "input_ids");
            _attentionMaskName = FindRequiredName(session.InputMetadata.Keys, "attention_mask");
            _tokenTypeIdsName = session.InputMetadata.Keys
                .SingleOrDefault(name => string.Equals(name, "token_type_ids", StringComparison.Ordinal));
            _logitsName = session.OutputMetadata.Keys
                .SingleOrDefault(name => string.Equals(name, "logits", StringComparison.Ordinal))
                ?? throw new InvalidDataException("The ONNX NER model has no 'logits' output.");
            _session = session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public float[] Predict(long[] inputIds)
    {
        var sequenceLength = inputIds.Length;
        var shape = new[] { 1, sequenceLength };
        var attentionMask = Enumerable.Repeat(1L, sequenceLength).ToArray();
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_inputIdsName, new DenseTensor<long>(inputIds, shape)),
            NamedOnnxValue.CreateFromTensor(_attentionMaskName, new DenseTensor<long>(attentionMask, shape))
        };

        if (_tokenTypeIdsName is not null)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(
                _tokenTypeIdsName,
                new DenseTensor<long>(new long[sequenceLength], shape)));
        }

        using var outputs = _session.Run(inputs);
        var logits = outputs
            .Single(output => string.Equals(output.Name, _logitsName, StringComparison.Ordinal))
            .AsTensor<float>();
        var dimensions = logits.Dimensions.ToArray();

        if (dimensions.Length != 3
            || dimensions[0] != 1
            || dimensions[1] != sequenceLength
            || dimensions[2] != _labelCount)
        {
            throw new InvalidDataException(
                $"Unexpected NER model output shape [{string.Join(",", dimensions)}].");
        }

        return logits.ToArray();
    }

    public void Dispose() => _session.Dispose();

    private static string FindRequiredName(IEnumerable<string> names, string expected) =>
        names.SingleOrDefault(name => string.Equals(name, expected, StringComparison.Ordinal))
        ?? throw new InvalidDataException($"The ONNX NER model has no '{expected}' input.");
}
