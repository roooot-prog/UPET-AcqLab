using System.Runtime.InteropServices;

namespace Spider8DAQ.Core.Licensing;

/// <summary>Windows default timer is ~15.6 ms; timeBeginPeriod(1) lets 1 ms live ticks fire.</summary>
public static class NativeTimerResolution
{
    private static int _refs;

    public static void Request1Ms()
    {
        if (Interlocked.Increment(ref _refs) == 1)
            TimeBeginPeriod(1);
    }

    public static void Release()
    {
        if (Interlocked.Decrement(ref _refs) == 0)
            TimeEndPeriod(1);
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}
