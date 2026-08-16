using Multiplatform_Downloader.Core.Ipc;

namespace Multiplatform_Downloader.Tests.Ipc;

public class SingleInstanceGuardTests
{
    [Fact]
    public void should_be_primary_when_first_instance()
    {
        var name = "mpdl-test-" + Guid.NewGuid().ToString("N");
        using var guard = new SingleInstanceGuard(name);

        Assert.True(guard.IsPrimaryInstance);
    }

    [Fact]
    public void should_not_be_primary_when_second_instance()
    {
        var name = "mpdl-test-" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        Assert.True(first.IsPrimaryInstance);
        Assert.False(second.IsPrimaryInstance);
    }
}
