using System.Text.Json;

namespace FlowBloxTest.FlowBlocks.AI;

internal sealed class SentencePieceModelFixture : IDisposable
{
    internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "flowblox-tokenizer-" + Guid.NewGuid().ToString("N"));

    internal SentencePieceModelFixture()
    {
        Directory.CreateDirectory(DirectoryPath);
        // Synthetic Unigram model: native UNK=0, BOS=1, EOS=2, whitespace=3, a=4, b=5.
        const string model = "Cg4KBTx1bms+FQAAgL8YAgoMCgM8cz4VAACAvxgDCg0KBDwvcz4VAACAvxgDCgwKA+KWgRUAAIC/GAEKCgoBYRUAAIC/GAEKCgoBYhUAAIC/GAESAhgBGhAKCGlkZW50aXR5GAEgASgB";
        File.WriteAllBytes(Path.Combine(DirectoryPath, "sentencepiece.bpe.model"), Convert.FromBase64String(model));
        File.WriteAllText(Path.Combine(DirectoryPath, "config.json"), """{"model_type":"xlm-roberta","max_position_embeddings":32}""");
        File.WriteAllText(Path.Combine(DirectoryPath, "tokenizer_config.json"), """{"cls_token":"<s>","sep_token":"</s>","unk_token":"<unk>","model_max_length":32}""");
        File.WriteAllText(Path.Combine(DirectoryPath, "sentence_bert_config.json"), """{"max_seq_length":32}""");
        var tokens = new[] { "<s>", "<pad>", "</s>", "<unk>", "\u2581", "a", "b" };
        var vocabulary = tokens.Select(token => new object[] { token, -1 }).ToArray();
        File.WriteAllText(Path.Combine(DirectoryPath, "tokenizer.json"), JsonSerializer.Serialize(new
        {
            model = new { type = "Unigram", unk_id = 3, vocab = vocabulary },
            added_tokens = tokens.Take(4).Select((token, index) => new { content = token, id = index }).ToArray()
        }));
    }

    public void Dispose() => Directory.Delete(DirectoryPath, true);
}
