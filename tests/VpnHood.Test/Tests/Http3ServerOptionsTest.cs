using VpnHood.Net.Http3;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3ServerOptionsTest : TestBase
{
    // The connection checks its options as it is made. A timeout no timer takes must fail there, not when the
    // connection runs and arms its idle timer, before the cleanup that closes the connection is in place.
    [TestMethod]
    public void A_timeout_no_timer_takes_is_refused_with_the_options()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Http3ServerOptions { KeepAliveTimeout = TimeSpan.FromSeconds(-5) }.Validate());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Http3ServerOptions { HeadersTimeout = TimeSpan.Zero }.Validate());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Http3ServerOptions { CloseTimeout = TimeSpan.FromDays(60) }.Validate());

        // what a timer takes passes: no limit, and any time up to about 49 days
        new Http3ServerOptions {
            KeepAliveTimeout = Timeout.InfiniteTimeSpan,
            HeadersTimeout = TimeSpan.FromMilliseconds(1),
            CloseTimeout = TimeSpan.FromDays(49)
        }.Validate();
    }
}
