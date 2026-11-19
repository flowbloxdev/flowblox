using Microsoft.Extensions.Internal;

namespace FlowBloxTest.FlowBlocks.Conditions;

internal sealed class LineSetCacheTestClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UnixEpoch;

    public void Advance(TimeSpan elapsed) => UtcNow += elapsed;
}
