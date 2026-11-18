using FlowBlox.Core.Runner.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.Core.Runner.Serialization;

/// <summary>
/// Protects launcher snapshots for the current Windows user and reads older plaintext entries.
/// </summary>
public sealed class ProtectedRunnerRequestConverter : JsonConverter<RunnerRequest>
{
    public override void WriteJson(JsonWriter writer, RunnerRequest? value, JsonSerializer serializer)
    {
        serializer.Serialize(writer, RunnerRequestProtection.ForStorage(value ?? new RunnerRequest()));
    }

    public override RunnerRequest? ReadJson(
        JsonReader reader,
        Type objectType,
        RunnerRequest? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return new RunnerRequest();

        return RunnerRequestProtection.FromStorage(JObject.Load(reader).ToObject<RunnerRequest>(serializer)!);
    }
}
