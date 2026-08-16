using Multiplatform_Downloader.Services;

namespace Multiplatform_Downloader.Tests.Services;

public class ProtocolRegistrarTests
{
    [Fact]
    public void should_register_and_unregister_idempotently()
    {
        var scheme = "mpdltest" + Guid.NewGuid().ToString("N")[..8];
        var sut = new ProtocolRegistrar(scheme);

        try
        {
            Assert.False(sut.IsRegistered());

            sut.Register();
            Assert.True(sut.IsRegistered());

            sut.Register(); // 멱등 — 재등록해도 문제 없음
            Assert.True(sut.IsRegistered());
        }
        finally
        {
            sut.Unregister();
        }

        Assert.False(sut.IsRegistered());
    }

    [Fact]
    public void should_be_safe_to_unregister_when_not_registered()
    {
        var scheme = "mpdltest" + Guid.NewGuid().ToString("N")[..8];
        var sut = new ProtocolRegistrar(scheme);

        sut.Unregister(); // 예외 없이 통과

        Assert.False(sut.IsRegistered());
    }
}
