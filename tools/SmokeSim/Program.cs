using Spider8DAQ.Core.Acquisition;
using Spider8DAQ.Hardware;

var outDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "UPETAcqLab", "recordings");
Directory.CreateDirectory(outDir);
var csv = Path.Combine(outDir, $"smoke_sim_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

await using var device = new SimulatedSpider8(8, 1) { SampleRateHz = 50 };
await using var engine = new AcquisitionEngine();

Console.WriteLine("CONNECT...");
await device.ConnectAsync();
if (device.State.ToString() != "Connected")
{
    Console.Error.WriteLine("FAIL Connect: " + device.State);
    return 1;
}

engine.Attach(device);
var headers = Enumerable.Range(1, 8).Select(i => $"CH{i}").ToArray();

Console.WriteLine("START...");
await device.StartStreamingAsync();
if (device.State.ToString() != "Streaming")
{
    Console.Error.WriteLine("FAIL Start: " + device.State);
    return 2;
}

Console.WriteLine("RECORD -> " + csv);
await engine.ArmRecordingAsync(csv, headers);
await Task.Delay(1500);
await engine.StopRecordingAsync();
await device.StopStreamingAsync();
await device.DisconnectAsync();

if (!File.Exists(csv))
{
    Console.Error.WriteLine("FAIL CSV missing");
    return 3;
}

var lines = await File.ReadAllLinesAsync(csv);
if (lines.Length < 3)
{
    Console.Error.WriteLine($"FAIL CSV too short ({lines.Length})");
    return 4;
}

var fi = new FileInfo(csv);
Console.WriteLine($"OK samples={lines.Length - 1} bytes={fi.Length} path={csv}");
Console.WriteLine($"engine.RecordedSamples={engine.RecordedSamples} frames={engine.FramesReceived}");
return 0;
