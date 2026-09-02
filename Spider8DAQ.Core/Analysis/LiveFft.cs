namespace Spider8DAQ.Core.Analysis;

/// <summary>Simple real FFT magnitude spectrum (radix-2 Cooley–Tukey).</summary>
public static class LiveFft
{
    public static double[] Magnitude(IReadOnlyList<double> samples)
    {
        var n = 1;
        while (n * 2 <= samples.Count) n *= 2;
        if (n < 8) return [];

        var re = new double[n];
        var im = new double[n];
        for (var i = 0; i < n; i++)
        {
            // Hann window
            var w = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)));
            re[i] = samples[i] * w;
        }

        FftInPlace(re, im);
        var half = n / 2;
        var mag = new double[half];
        var scale = 2.0 / n;
        for (var i = 0; i < half; i++)
            mag[i] = Math.Sqrt(re[i] * re[i] + im[i] * im[i]) * scale;
        mag[0] *= 0.5;
        return mag;
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
}
