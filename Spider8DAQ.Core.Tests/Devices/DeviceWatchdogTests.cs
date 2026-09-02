using Spider8DAQ.Core.Devices;
using Xunit;

namespace Spider8DAQ.Core.Tests.Devices;

public class DeviceWatchdogTests
{
    [Fact]
    public async Task Failed_reconnect_does_not_bump_last_sample_utc()
    {
        var wd = new DeviceWatchdog(
            reconnectAsync: async () =>
            {
                await Task.Yield();
                return false;
            },
            timeout: TimeSpan.FromMilliseconds(40),
            pollInterval: TimeSpan.FromMilliseconds(15));
        wd.MaxReconnectAttempts = 8;
        wd.NotifySample();
        var stamped = wd.LastSampleUtc;
        wd.Start();
        await Task.Delay(180);
        Assert.Equal(stamped, wd.LastSampleUtc);
        await wd.DisposeAsync();
    }

    [Fact]
    public async Task Successful_reconnect_bumps_last_sample_utc()
    {
        var wd = new DeviceWatchdog(
            reconnectAsync: async () =>
            {
                await Task.Yield();
                return true;
            },
            timeout: TimeSpan.FromMilliseconds(40),
            pollInterval: TimeSpan.FromMilliseconds(15));
        wd.NotifySample();
        var stamped = wd.LastSampleUtc;
        wd.Start();
        await Task.Delay(150);
        Assert.True(wd.LastSampleUtc > stamped);
        await wd.DisposeAsync();
    }

    [Fact]
    public async Task Missing_device_raises_connection_lost_without_reconnect()
    {
        var reconnects = 0;
        var lost = new TaskCompletionSource();
        var wd = new DeviceWatchdog(
            reconnectAsync: () =>
            {
                Interlocked.Increment(ref reconnects);
                return Task.FromResult(true);
            },
            timeout: TimeSpan.FromSeconds(30),
            pollInterval: TimeSpan.FromMilliseconds(20),
            isDevicePresent: () => false);
        wd.NotifySample();
        var stamped = wd.LastSampleUtc;
        wd.ConnectionLost += (_, _) => lost.TrySetResult();
        wd.Start();
        await lost.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, reconnects);
        Assert.Equal(stamped, wd.LastSampleUtc);
        await wd.DisposeAsync();
    }

    [Fact]
    public async Task Max_failed_reconnects_raises_connection_lost()
    {
        var lost = new TaskCompletionSource();
        var wd = new DeviceWatchdog(
            reconnectAsync: () => Task.FromResult(false),
            timeout: TimeSpan.FromMilliseconds(25),
            pollInterval: TimeSpan.FromMilliseconds(15));
        wd.MaxReconnectAttempts = 2;
        wd.NotifySample();
        wd.ConnectionLost += (_, _) => lost.TrySetResult();
        wd.Start();
        await lost.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await wd.DisposeAsync();
    }

    [Fact]
    public async Task Monitor_samples_false_skips_reconnect_on_silence()
    {
        var reconnects = 0;
        var wd = new DeviceWatchdog(
            reconnectAsync: () =>
            {
                Interlocked.Increment(ref reconnects);
                return Task.FromResult(true);
            },
            timeout: TimeSpan.FromMilliseconds(20),
            pollInterval: TimeSpan.FromMilliseconds(15),
            monitorSamples: () => false);
        wd.Start();
        await Task.Delay(120);
        Assert.Equal(0, reconnects);
        await wd.DisposeAsync();
    }
}
