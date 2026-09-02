namespace Spider8DAQ.Core.Analysis;

/// <summary>Result of a continuous wavelet transform (scalogram).</summary>
public sealed class CwtResult
{
    /// <summary>Magnitude |W| indexed [scale, time].</summary>
    public required double[,] Magnitudes { get; init; }
    /// <summary>Center frequency [Hz] for each scale row.</summary>
    public required double[] FrequenciesHz { get; init; }
    /// <summary>Time axis [s] for each column (relative to window start).</summary>
    public required double[] TimesSec { get; init; }
    public double SampleRateHz { get; init; }
    public string WaveletName { get; init; } = "Morlet";
    public string ChannelName { get; init; } = "";
    public int InputSamples { get; init; }
    public int AnalyzedSamples { get; init; }
}

public sealed class CwtOptions
{
    /// <summary>Max samples after optional downsampling (time bins on scalogram).</summary>
    public int MaxSamples { get; set; } = 768;
    /// <summary>Number of log-spaced scales / frequency rows.</summary>
    public int ScaleCount { get; set; } = 64;
    /// <summary>Lowest analysis frequency [Hz]. 0 = auto (~2 / duration).</summary>
    public double MinFrequencyHz { get; set; }
    /// <summary>Highest analysis frequency [Hz]. 0 = auto (0.45·Nyquist).</summary>
    public double MaxFrequencyHz { get; set; }
    /// <summary>Morlet central frequency ω₀ / (2π) in cycles (typical 6).</summary>
    public double MorletOmega0 { get; set; } = 6.0;
    public string Wavelet { get; set; } = "Morlet";

    /// <summary>High-quality offline / report defaults (dense f×t grid; time spans full input after uniform downsample).</summary>
    public static CwtOptions ForExport() => new()
    {
        MaxSamples = 2048,
        ScaleCount = 96,
        Wavelet = "Morlet"
    };

    /// <summary>Live UI: dense enough for sharp Smooth heatmap, still ≤~20 Hz on 2 ch @ 50 Hz.</summary>
    public static CwtOptions ForLive() => new()
    {
        MaxSamples = 512,
        ScaleCount = 64,
        Wavelet = "Morlet"
    };
}

/// <summary>
/// Lab-friendly continuous wavelet transform (Morlet, FFT convolution) —
/// pure C#, no external wavelet package.
/// </summary>
public static class ContinuousWaveletTransform
{
    public static CwtResult Compute(
        IReadOnlyList<double> samples,
        double sampleRateHz,
        CwtOptions? options = null,
        string channelName = "")
    {
        options ??= new CwtOptions();
        var fs = Math.Max(1e-6, sampleRateHz);
        var cleaned = Sanitize(samples);
        if (cleaned.Length < 16)
        {
            return Empty(channelName, fs, options.Wavelet);
        }

        var (signal, effectiveFs) = Downsample(cleaned, fs, options.MaxSamples);
        var n = signal.Length;
        // Remove DC for clearer scalogram
        var mean = 0.0;
        for (var i = 0; i < n; i++) mean += signal[i];
        mean /= n;
        for (var i = 0; i < n; i++) signal[i] -= mean;

        var duration = n / effectiveFs;
        var fMin = options.MinFrequencyHz > 0
            ? options.MinFrequencyHz
            : Math.Max(0.5, 2.0 / Math.Max(duration, 1e-6));
        var fMax = options.MaxFrequencyHz > 0
            ? options.MaxFrequencyHz
            : Math.Max(fMin * 1.5, 0.45 * (effectiveFs * 0.5));
        if (fMax <= fMin) fMax = fMin * 2;

        var scales = options.ScaleCount <= 0 ? 64 : Math.Clamp(options.ScaleCount, 8, 160);
        var freqs = LogSpace(fMin, fMax, scales);
        // Morlet: f ≈ ω0 / (2π s)  →  s = ω0 / (2π f)
        var omega0 = Math.Max(3.0, options.MorletOmega0);
        var useMexican = string.Equals(options.Wavelet, "MexicanHat", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(options.Wavelet, "Mexican-hat", StringComparison.OrdinalIgnoreCase);

        var nfft = NextPow2(n * 2);
        var sigRe = new double[nfft];
        var sigIm = new double[nfft];
        for (var i = 0; i < n; i++) sigRe[i] = signal[i];
        FftInPlace(sigRe, sigIm);

        var mag = new double[scales, n];
        var waveletRe = new double[nfft];
        var waveletIm = new double[nfft];
        var workRe = new double[nfft];
        var workIm = new double[nfft];

        for (var si = 0; si < scales; si++)
        {
            var f = freqs[si];
            var scale = useMexican
                ? Math.Sqrt(2.5) / (2 * Math.PI * f) // Mexican-hat peak ≈ √2.5 /(2πs)
                : omega0 / (2 * Math.PI * f);
            scale = Math.Max(scale, 1.0 / effectiveFs);

            Array.Clear(waveletRe);
            Array.Clear(waveletIm);
            BuildWaveletTime(waveletRe, waveletIm, n, effectiveFs, scale, omega0, useMexican);
            FftInPlace(waveletRe, waveletIm);

            // Convolve: IFFT( FFT(x) * conj(FFT(ψ)) ) — ψ already built as analysis wavelet
            for (var k = 0; k < nfft; k++)
            {
                // multiply by conjugate of wavelet FFT for correlation
                var wr = waveletRe[k];
                var wi = -waveletIm[k];
                workRe[k] = sigRe[k] * wr - sigIm[k] * wi;
                workIm[k] = sigRe[k] * wi + sigIm[k] * wr;
            }
            IfftInPlace(workRe, workIm);

            var norm = 1.0 / Math.Sqrt(Math.Max(scale, 1e-12));
            for (var t = 0; t < n; t++)
            {
                var re = workRe[t] * norm;
                var im = workIm[t] * norm;
                mag[si, t] = Math.Sqrt(re * re + im * im);
            }
        }

        var times = new double[n];
        for (var i = 0; i < n; i++)
            times[i] = i / effectiveFs;

        return new CwtResult
        {
            Magnitudes = mag,
            FrequenciesHz = freqs,
            TimesSec = times,
            SampleRateHz = effectiveFs,
            WaveletName = useMexican ? "Mexican-hat" : "Morlet",
            ChannelName = channelName,
            InputSamples = cleaned.Length,
            AnalyzedSamples = n
        };
    }

    private static CwtResult Empty(string channelName, double fs, string wavelet) =>
        new()
        {
            Magnitudes = new double[0, 0],
            FrequenciesHz = [],
            TimesSec = [],
            SampleRateHz = fs,
            WaveletName = wavelet,
            ChannelName = channelName,
            InputSamples = 0,
            AnalyzedSamples = 0
        };

    private static double[] Sanitize(IReadOnlyList<double> samples)
    {
        var list = new List<double>(samples.Count);
        double last = 0;
        var has = false;
        for (var i = 0; i < samples.Count; i++)
        {
            var v = samples[i];
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                if (has) list.Add(last);
                continue;
            }
            last = v;
            has = true;
            list.Add(v);
        }
        return list.ToArray();
    }

    private static (double[] Signal, double Fs) Downsample(double[] signal, double fs, int maxSamples)
    {
        if (signal.Length <= maxSamples || maxSamples < 16)
            return (signal.ToArray(), fs);

        var step = (double)signal.Length / maxSamples;
        var dst = new double[maxSamples];
        for (var i = 0; i < maxSamples; i++)
        {
            var idx = (int)(i * step);
            if (idx >= signal.Length) idx = signal.Length - 1;
            dst[i] = signal[idx];
        }
        var newFs = fs * maxSamples / (double)signal.Length;
        return (dst, newFs);
    }

    private static double[] LogSpace(double fMin, double fMax, int count)
    {
        var r = new double[count];
        if (count == 1)
        {
            r[0] = fMin;
            return r;
        }
        var logMin = Math.Log(fMin);
        var logMax = Math.Log(fMax);
        for (var i = 0; i < count; i++)
            r[i] = Math.Exp(logMin + (logMax - logMin) * i / (count - 1));
        return r;
    }

    private static void BuildWaveletTime(
        double[] re, double[] im, int nSignal, double fs, double scale, double omega0, bool mexicanHat)
    {
        // Support ~±4·scale (or ±8σ for Mexican-hat)
        var half = mexicanHat ? 8.0 * scale : 4.0 * scale;
        var halfSamples = Math.Min(re.Length / 2 - 1, Math.Max(8, (int)Math.Ceiling(half * fs)));
        var norm = 1.0 / Math.Sqrt(scale);

        for (var k = -halfSamples; k <= halfSamples; k++)
        {
            var t = k / fs;
            var tau = t / scale;
            int idx = k >= 0 ? k : re.Length + k;
            if (idx < 0 || idx >= re.Length) continue;

            if (mexicanHat)
            {
                // ψ(τ) = (1-τ²)·exp(-τ²/2) · π^{-1/4} / √s   (real)
                var g = Math.Exp(-0.5 * tau * tau);
                re[idx] = norm * 0.867325 * (1 - tau * tau) * g; // π^{-1/4}≈0.751 + scale factor
                im[idx] = 0;
            }
            else
            {
                // Complex Morlet: π^{-1/4} exp(i ω0 τ) exp(-τ²/2) / √s
                var envelope = 0.75112554446 * Math.Exp(-0.5 * tau * tau) * norm;
                var phase = omega0 * tau;
                re[idx] = envelope * Math.Cos(phase);
                im[idx] = envelope * Math.Sin(phase);
            }
        }
    }

    private static int NextPow2(int n)
    {
        var p = 1;
        while (p < n) p <<= 1;
        return Math.Max(16, p);
    }

    private static void FftInPlace(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i >= j) continue;
            (re[i], re[j]) = (re[j], re[i]);
            (im[i], im[j]) = (im[j], im[i]);
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2 * Math.PI / len;
            var wlenRe = Math.Cos(ang);
            var wlenIm = Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                double wRe = 1, wIm = 0;
                for (var j = 0; j < len / 2; j++)
                {
                    var uRe = re[i + j];
                    var uIm = im[i + j];
                    var vRe = re[i + j + len / 2] * wRe - im[i + j + len / 2] * wIm;
                    var vIm = re[i + j + len / 2] * wIm + im[i + j + len / 2] * wRe;
                    re[i + j] = uRe + vRe;
                    im[i + j] = uIm + vIm;
                    re[i + j + len / 2] = uRe - vRe;
                    im[i + j + len / 2] = uIm - vIm;
                    var nWRe = wRe * wlenRe - wIm * wlenIm;
                    wIm = wRe * wlenIm + wIm * wlenRe;
                    wRe = nWRe;
                }
            }
        }
    }

    private static void IfftInPlace(double[] re, double[] im)
    {
        for (var i = 0; i < im.Length; i++) im[i] = -im[i];
        FftInPlace(re, im);
        var inv = 1.0 / re.Length;
        for (var i = 0; i < re.Length; i++)
        {
            re[i] *= inv;
            im[i] = -im[i] * inv;
        }
    }
}
