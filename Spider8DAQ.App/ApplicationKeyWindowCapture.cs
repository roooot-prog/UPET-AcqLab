using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Spider8DAQ.App;

/// <summary>JPEG of the AcqLab window (not the full desktop). Max ~200 KB.</summary>
internal static class ApplicationKeyWindowCapture
{
    public static (byte[]? full, byte[]? thumb) Capture(int maxFullBytes, int maxThumbBytes)
    {
        var app = Application.Current;
        if (app is null) return (null, null);
        if (app.Dispatcher.CheckAccess())
            return CaptureCore(maxFullBytes, maxThumbBytes);
        try
        {
            return app.Dispatcher.Invoke(
                () => CaptureCore(maxFullBytes, maxThumbBytes),
                System.Windows.Threading.DispatcherPriority.Background);
        }
        catch
        {
            return (null, null);
        }
    }

    private static (byte[]? full, byte[]? thumb) CaptureCore(int maxFullBytes, int maxThumbBytes)
    {
        var app = Application.Current;
        if (app is null) return (null, null);

        Window? w = app.Windows.OfType<MainWindow>().FirstOrDefault(x => x.IsVisible)
                    ?? app.Windows.OfType<ApplicationKeyPendingWindow>().FirstOrDefault(x => x.IsVisible)
                    ?? app.MainWindow
                    ?? app.Windows.OfType<Window>().FirstOrDefault(x => x.IsVisible);
        if (w is null || w.ActualWidth < 16 || w.ActualHeight < 16)
            return (null, null);

        try
        {
            var full = RenderJpeg(w, maxWidth: 960, maxBytes: maxFullBytes, minQuality: 32);
            var thumb = RenderJpeg(w, maxWidth: 160, maxBytes: maxThumbBytes, minQuality: 40);
            return (full, thumb);
        }
        catch
        {
            return (null, null);
        }
    }

    private static byte[]? RenderJpeg(Window w, int maxWidth, int maxBytes, int minQuality)
    {
        var scale = Math.Min(1.0, maxWidth / Math.Max(1.0, w.ActualWidth));
        var pxW = Math.Max(8, (int)(w.ActualWidth * scale));
        var pxH = Math.Max(8, (int)(w.ActualHeight * scale));

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
            {
                pxW = Math.Max(8, pxW * 3 / 4);
                pxH = Math.Max(8, pxH * 3 / 4);
            }

            var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var brush = new VisualBrush(w) { Stretch = Stretch.Fill };
                dc.DrawRectangle(brush, null, new Rect(0, 0, pxW, pxH));
            }
            rtb.Render(dv);

            for (var q = 70; q >= minQuality; q -= 12)
            {
                var enc = new JpegBitmapEncoder { QualityLevel = q };
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var ms = new MemoryStream();
                enc.Save(ms);
                if (ms.Length <= maxBytes)
                    return ms.ToArray();
                if (q <= minQuality && attempt == 2)
                    return ms.Length <= maxBytes * 2 ? ms.ToArray() : null;
            }
        }

        return null;
    }
}
