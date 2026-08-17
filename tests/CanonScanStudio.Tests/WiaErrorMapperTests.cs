using CanonScanStudio.Scanning.Wia;
using FluentAssertions;

namespace CanonScanStudio.Tests;

public class WiaErrorMapperTests
{
    [Fact]
    public void Maps_missing_device_to_spanish_message()
    {
        var mapped = WiaErrorMapper.NotDetected("Canon PIXMA TS5151");
        mapped.UserMessage.Should().Contain("no detectado");
        mapped.UserMessage.Should().Contain("USB");
        mapped.UserMessage.Should().NotContain("0x80210015");
        mapped.CanRetry.Should().BeTrue();
    }
}
