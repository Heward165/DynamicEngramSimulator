namespace DynamicEngramSimulator;

/// <summary>Paired effect estimate with a deterministic interval and randomization p-value.</summary>
public sealed record EngramPairedInference(
    int PairCount,
    double MeanDifference,
    double StandardizedEffect,
    double ConfidenceLower,
    double ConfidenceUpper,
    double TwoSidedPValue);

/// <summary>Named p-value before and after Benjamini-Hochberg correction.</summary>
public sealed record EngramAdjustedPValue(string Name, double RawPValue, double AdjustedPValue);

/// <summary>Dependency-free statistical inference for paired, seed-matched experiments.</summary>
public static class EngramInference
{
    /// <summary>
    /// Estimates a paired effect using bootstrap means and a sign-flip permutation test.
    /// Exact enumeration is used for at most twenty pairs; larger samples use deterministic Monte Carlo.
    /// </summary>
    public static EngramPairedInference Paired(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> treatment,
        ulong seed = 0x504149524544UL,
        int resamples = 10_000)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(treatment);
        if (baseline.Count == 0 || baseline.Count != treatment.Count || resamples < 100 ||
            baseline.Any(value => !double.IsFinite(value)) || treatment.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Paired inference requires equal, finite, non-empty samples.");

        double[] differences = treatment.Zip(baseline, (right, left) => right - left).ToArray();
        double mean = differences.Average();
        double deviation = differences.Length < 2
            ? 0
            : Math.Sqrt(differences.Sum(value => Math.Pow(value - mean, 2)) / (differences.Length - 1));
        double effect = deviation == 0 ? 0 : mean / deviation;
        var bootstrap = new double[resamples];
        ulong random = seed;
        for (int sample = 0; sample < bootstrap.Length; sample++)
        {
            double sum = 0;
            for (int index = 0; index < differences.Length; index++)
                sum += differences[(int)(Next(ref random) % (ulong)differences.Length)];
            bootstrap[sample] = sum / differences.Length;
        }

        Array.Sort(bootstrap);
        double p = differences.Length <= 20
            ? ExactSignFlipPValue(differences, Math.Abs(mean))
            : MonteCarloSignFlipPValue(differences, Math.Abs(mean), seed ^ 0xA11CEUL, resamples);
        return new EngramPairedInference(
            differences.Length,
            mean,
            effect,
            Quantile(bootstrap, 0.025),
            Quantile(bootstrap, 0.975),
            p);
    }

    /// <summary>Controls false discovery rate across a family of named hypotheses.</summary>
    public static IReadOnlyList<EngramAdjustedPValue> AdjustFalseDiscoveryRate(
        IEnumerable<KeyValuePair<string, double>> tests)
    {
        ArgumentNullException.ThrowIfNull(tests);
        var ordered = tests.OrderBy(test => test.Value).ToArray();
        if (ordered.Length == 0 || ordered.Any(test => string.IsNullOrWhiteSpace(test.Key) ||
                                                       !double.IsFinite(test.Value) || test.Value is < 0 or > 1))
            throw new ArgumentException("Tests require names and p-values in [0,1].", nameof(tests));

        var adjusted = new double[ordered.Length];
        double previous = 1;
        for (int index = ordered.Length - 1; index >= 0; index--)
        {
            double candidate = ordered[index].Value * ordered.Length / (index + 1d);
            previous = Math.Min(previous, candidate);
            adjusted[index] = Math.Clamp(previous, 0, 1);
        }

        return ordered.Select((test, index) =>
            new EngramAdjustedPValue(test.Key, test.Value, adjusted[index])).ToArray();
    }

    /// <summary>Approximate pairs required for a two-sided standardized mean-difference test.</summary>
    public static int EstimatePairedSampleSize(double standardizedEffect, double power = 0.8, double alpha = 0.05)
    {
        if (!double.IsFinite(standardizedEffect) || standardizedEffect == 0 ||
            !double.IsFinite(power) || power is <= 0.5 or >= 1 ||
            !double.IsFinite(alpha) || alpha is <= 0 or >= 0.5)
            throw new ArgumentOutOfRangeException(nameof(standardizedEffect));
        double zAlpha = InverseNormal(1 - (alpha / 2));
        double zPower = InverseNormal(power);
        return Math.Max(2, (int)Math.Ceiling(Math.Pow((zAlpha + zPower) / Math.Abs(standardizedEffect), 2)));
    }

    private static double ExactSignFlipPValue(double[] differences, double observed)
    {
        ulong combinations = 1UL << differences.Length;
        ulong extreme = 0;
        for (ulong mask = 0; mask < combinations; mask++)
        {
            double sum = 0;
            for (int index = 0; index < differences.Length; index++)
                sum += ((mask >> index) & 1) == 0 ? differences[index] : -differences[index];
            if (Math.Abs(sum / differences.Length) + 1e-15 >= observed) extreme++;
        }

        return extreme / (double)combinations;
    }

    private static double MonteCarloSignFlipPValue(double[] differences, double observed, ulong seed, int samples)
    {
        ulong random = seed;
        int extreme = 1;
        for (int sample = 0; sample < samples; sample++)
        {
            double sum = 0;
            for (int index = 0; index < differences.Length; index++)
                sum += (Next(ref random) & 1) == 0 ? differences[index] : -differences[index];
            if (Math.Abs(sum / differences.Length) + 1e-15 >= observed) extreme++;
        }

        return extreme / (double)(samples + 1);
    }

    private static double Quantile(double[] values, double probability)
    {
        double position = probability * (values.Length - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        return values[lower] + ((values[upper] - values[lower]) * (position - lower));
    }

    private static double InverseNormal(double probability)
    {
        double lower = -8;
        double upper = 8;
        for (int iteration = 0; iteration < 100; iteration++)
        {
            double middle = (lower + upper) / 2;
            if (NormalCdf(middle) < probability) lower = middle; else upper = middle;
        }

        return (lower + upper) / 2;
    }

    private static double NormalCdf(double value)
    {
        // Abramowitz-Stegun 7.1.26; adequate for power planning rather than inference.
        double sign = value < 0 ? -1 : 1;
        double x = Math.Abs(value) / Math.Sqrt(2);
        double t = 1 / (1 + (0.3275911 * x));
        double erf = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t
            - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return 0.5 * (1 + (sign * erf));
    }

    private static ulong Next(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong value = state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
