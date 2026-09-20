using System.Text.Json;
using AgentMeter.Core;

namespace AgentMeter.Tests;

public sealed class StrictJsonTests
{
    [Theory]
    [InlineData("""{"rateLimits":{},"rateLimits":{"primary":{"usedPercent":25}}}""")]
    [InlineData("""{"rateLimitsByLimitId":{"codex":{},"codex":{"primary":{"usedPercent":25}}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":25},"primary":{"usedPercent":75}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":25,"usedPercent":75}}}""")]
    public void DuplicateQuotaPropertiesAreAmbiguous(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = CodexParser.Parse(document.RootElement, DateTimeOffset.UtcNow);
        Assert.Equal(FailureKind.Malformed, result.Failure);
        Assert.Null(result.Snapshot);
    }
}
