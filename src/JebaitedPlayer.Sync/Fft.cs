namespace JebaitedPlayer.Sync;

/// <summary>Iterativni radix-2 FFT (kompleksni, na mjestu). Duljina mora biti potencija broja 2.</summary>
internal static class Fft
{
    public static int NextPowerOfTwo(int n)
    {
        int p = 1;
        while (p < n)
        {
            p <<= 1;
        }

        return p;
    }

    public static void Transform(double[] re, double[] im, bool inverse)
    {
        int n = re.Length;
        if (n != im.Length || (n & (n - 1)) != 0)
        {
            throw new ArgumentException("FFT: duljina mora biti potencija broja 2.");
        }

        // Permutacija obrnutih bitova.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = 2 * Math.PI / len * (inverse ? 1 : -1);
            double wRe = Math.Cos(angle), wIm = Math.Sin(angle);
            int half = len >> 1;
            for (int i = 0; i < n; i += len)
            {
                double cRe = 1, cIm = 0;
                for (int k = 0; k < half; k++)
                {
                    int a = i + k, b = a + half;
                    double tRe = re[b] * cRe - im[b] * cIm;
                    double tIm = re[b] * cIm + im[b] * cRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;

                    double nRe = cRe * wRe - cIm * wIm;
                    cIm = cRe * wIm + cIm * wRe;
                    cRe = nRe;
                }
            }
        }

        if (inverse)
        {
            for (int i = 0; i < n; i++)
            {
                re[i] /= n;
                im[i] /= n;
            }
        }
    }

    /// <summary>
    /// Unakrsna korelacija c[d] = Σ s[i]·a[i+d] za d u [-maxLag, maxLag]; rezultat[maxLag + d].
    /// Spektar od <paramref name="a"/> može se izračunati jednom i ponovno koristiti.
    /// </summary>
    public static double[] CrossCorrelate((double[] Re, double[] Im) spectrumA, double[] s, int maxLag)
    {
        int n = spectrumA.Re.Length;
        var sRe = new double[n];
        var sIm = new double[n];
        Array.Copy(s, sRe, Math.Min(s.Length, n));
        Transform(sRe, sIm, inverse: false);

        // A · conj(S)
        for (int i = 0; i < n; i++)
        {
            double aRe = spectrumA.Re[i], aIm = spectrumA.Im[i];
            double re = aRe * sRe[i] + aIm * sIm[i];
            double im = aIm * sRe[i] - aRe * sIm[i];
            sRe[i] = re;
            sIm[i] = im;
        }

        Transform(sRe, sIm, inverse: true);
        var result = new double[2 * maxLag + 1];
        for (int d = -maxLag; d <= maxLag; d++)
        {
            result[maxLag + d] = sRe[d >= 0 ? d : n + d];
        }

        return result;
    }

    public static (double[] Re, double[] Im) Spectrum(double[] a, int n)
    {
        var re = new double[n];
        var im = new double[n];
        Array.Copy(a, re, Math.Min(a.Length, n));
        Transform(re, im, inverse: false);
        return (re, im);
    }
}
