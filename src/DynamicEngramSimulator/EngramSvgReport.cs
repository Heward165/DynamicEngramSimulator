using System.Globalization;
using System.Net;
using System.Text;

namespace DynamicEngramSimulator;

/// <summary>Creates small dependency-free SVG summaries for experiment galleries.</summary>
public static class EngramSvgReport
{
    /// <summary>Plots population identity and behavioral stability for every seed.</summary>
    public static string Stability(EngramExperimentReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return LineChart("Identity and behavioral stability",
            report.Runs.Select((run, index) => new ChartPoint(index, run.PopulationIdentity)).ToArray(),
            report.Runs.Select((run, index) => new ChartPoint(index, run.BehavioralStability)).ToArray(),
            "population identity", "behavioral stability");
    }

    /// <summary>Plots paired selectivity changes for every disabled mechanism.</summary>
    public static string Ablations(EngramAblationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return BarChart("Selectivity change after ablation",
            report.Effects.Select(effect => (effect.Ablation.ToString(), effect.MeanSelectivityChange)).ToArray());
    }

    /// <summary>Plots mean population overlap against inter-event delay.</summary>
    public static string TemporalLinking(TemporalLinkingReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ChartPoint[] points = report.Delays
            .Select(summary => new ChartPoint(summary.DelayHours, summary.PopulationOverlap.Mean))
            .ToArray();
        return LineChart("Engram overlap by inter-event delay", points, [], "population overlap", null);
    }

    /// <summary>Plots paired selectivity effects for factorial conditions.</summary>
    public static string Factorial(EngramFactorialReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return BarChart("Factorial selectivity effects", report.Conditions
            .Select(condition => (condition.Name, condition.FinalSelectivityInference.MeanDifference)).ToArray());
    }

    /// <summary>Plots paired selectivity effects for scientific null controls.</summary>
    public static string NullControls(EngramNullControlReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return BarChart("Allocation null controls", report.Conditions
            .Select(condition => (condition.Control.ToString(), condition.FinalSelectivityInference.MeanDifference)).ToArray());
    }

    /// <summary>Plots total-order or Morris input influence.</summary>
    public static string Sensitivity(EngramSensitivityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return BarChart("Global parameter sensitivity", report.Indices
            .Select(index => (index.Parameter.ToString(),
                index.TotalOrder ?? index.MorrisMeanAbsoluteEffect ?? 0)).ToArray());
    }

    private static string LineChart(
        string title,
        IReadOnlyList<ChartPoint> first,
        IReadOnlyList<ChartPoint> second,
        string firstLabel,
        string? secondLabel)
    {
        const int width = 900;
        const int height = 460;
        const int left = 75;
        const int top = 55;
        const int plotWidth = 780;
        const int plotHeight = 330;
        double maxX = Math.Max(1, first.Concat(second).Select(point => point.X).DefaultIfEmpty(1).Max());
        double minY = Math.Min(0, first.Concat(second).Select(point => point.Y).DefaultIfEmpty(0).Min());
        double maxY = Math.Max(1, first.Concat(second).Select(point => point.Y).DefaultIfEmpty(1).Max());
        double rangeY = Math.Max(1e-9, maxY - minY);

        string Points(IReadOnlyList<ChartPoint> series) => string.Join(' ', series.Select(point =>
            $"{Format(left + (point.X / maxX * plotWidth))},{Format(top + ((maxY - point.Y) / rangeY * plotHeight))}"));

        var svg = Header(width, height, title);
        svg.Append($"<line x1=\"{left}\" y1=\"{top}\" x2=\"{left}\" y2=\"{top + plotHeight}\" class=\"axis\"/>");
        svg.Append($"<line x1=\"{left}\" y1=\"{top + plotHeight}\" x2=\"{left + plotWidth}\" y2=\"{top + plotHeight}\" class=\"axis\"/>");
        svg.Append($"<polyline points=\"{Points(first)}\" class=\"series-a\"/>");
        if (second.Count > 0)
            svg.Append($"<polyline points=\"{Points(second)}\" class=\"series-b\"/>");
        svg.Append($"<text x=\"{left}\" y=\"425\" class=\"legend a\">{WebUtility.HtmlEncode(firstLabel)}</text>");
        if (secondLabel is not null)
            svg.Append($"<text x=\"{left + 260}\" y=\"425\" class=\"legend b\">{WebUtility.HtmlEncode(secondLabel)}</text>");
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string BarChart(string title, IReadOnlyList<(string Label, double Value)> values)
    {
        const int width = 900;
        const int height = 500;
        const double baseline = 235;
        double maximum = Math.Max(0.01, values.Select(value => Math.Abs(value.Value)).DefaultIfEmpty(1).Max());
        var svg = Header(width, height, title);
        svg.Append($"<line x1=\"55\" y1=\"{baseline}\" x2=\"870\" y2=\"{baseline}\" class=\"axis\"/>");
        for (int index = 0; index < values.Count; index++)
        {
            (string label, double value) = values[index];
            double x = 70 + (index * (790.0 / Math.Max(values.Count, 1)));
            double barWidth = Math.Max(16, (760.0 / Math.Max(values.Count, 1)) - 15);
            double magnitude = Math.Abs(value) / maximum * 160;
            double y = value >= 0 ? baseline - magnitude : baseline;
            svg.Append($"<rect x=\"{Format(x)}\" y=\"{Format(y)}\" width=\"{Format(barWidth)}\" height=\"{Format(magnitude)}\" class=\"bar\"/>");
            svg.Append($"<text x=\"{Format(x)}\" y=\"430\" class=\"bar-label\" transform=\"rotate(35 {Format(x)} 430)\">{WebUtility.HtmlEncode(label)}</text>");
        }
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static StringBuilder Header(int width, int height, string title) => new StringBuilder()
        .Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">")
        .Append("<style>text{font-family:system-ui,sans-serif;fill:#202124}.title{font-size:22px;font-weight:600}.axis{stroke:#555;stroke-width:1}.series-a,.series-b{fill:none;stroke-width:3}.series-a{stroke:#1565c0}.series-b{stroke:#c62828}.legend{font-size:14px}.legend.a{fill:#1565c0}.legend.b{fill:#c62828}.bar{fill:#1565c0}.bar-label{font-size:12px}</style>")
        .Append($"<text x=\"55\" y=\"32\" class=\"title\">{WebUtility.HtmlEncode(title)}</text>");

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private readonly record struct ChartPoint(double X, double Y);
}
