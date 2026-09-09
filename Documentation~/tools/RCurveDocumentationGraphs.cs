// Copy into Assets/Editor of an isolated Unity host; see Documentation~/graphs.md.
using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using RCurve;
using UnityEngine;

public static class RCurveDocumentationGraphs
{
    private const int SampleCount = 513;
    private const string Blue = "#0369a1", Orange = "#c2410c", Grid = "#dbe3ed";

    [Serializable]
    private sealed class CurveReport
    {
        public AnimationCurve Source;
        public string Settings;
        public double[] Times, UnityValues, RationalValues, Errors, BakedBoundaries;
        public double SampledMaxError;
    }

    [Serializable]
    private sealed class LookupReport
    {
        public string ManagedLookup, BurstLookup;
        public CurveIndexPrediction Prediction;
        public double[] KeyTimes, KeyValues, Times, PredictedIndices;
        public double QueryTime, PredictedIndex, EvaluatedValue, MaxKeyIndexError;
        public int CandidateInterval, ResolvedInterval;
    }

    [Serializable]
    private sealed class Report
    {
        public string UnityVersion = Application.unityVersion;
        public string Source = "Current RCurve working tree; not a released-player benchmark.";
        public CurveReport Curve;
        public LookupReport Lookup;
    }

    public static void Generate()
    {
        string output = Environment.GetEnvironmentVariable("RCURVE_GRAPH_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("Set RCURVE_GRAPH_OUTPUT to the output directory.");
        var report = new Report { Curve = SampleCurve(), Lookup = SampleLookup() };
        string curveSvg = CurveGraph(report);
        string lookupSvg = LookupGraph(report);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "curve-accuracy.svg"), curveSvg);
        File.WriteAllText(Path.Combine(output, "magic-lookup.svg"), lookupSvg);
        File.WriteAllText(Path.Combine(output, "data.json"), JsonUtility.ToJson(report, true));
        Debug.Log($"RCurve documentation graphs generated: sampled max error {report.Curve.SampledMaxError:G6}; Magic query-trace checks passed.");
    }

    private static CurveReport SampleCurve()
    {
        var source = new AnimationCurve(
            new Keyframe(0, 0, 0, 3, 0, 0.15f) { weightedMode = WeightedMode.Out },
            new Keyframe(1, 1, -0.7f, 0, 0.65f, 0) { weightedMode = WeightedMode.In })
            { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
        var settings = new CurveFitSettings();
        var curve = new RationalCurve(source, settings);
        curve.Bake();
        var baked = curve.BakedCurve;
        var report = new CurveReport
        {
            Source = source,
            Settings = "Minimax / High (5/5), value tolerance 0.001, slope tolerance 0.01, Auto lookup",
            Times = new double[SampleCount], UnityValues = new double[SampleCount],
            RationalValues = new double[SampleCount], Errors = new double[SampleCount],
            BakedBoundaries = baked.KeyTimes.ToArray()
        };
        for (int i = 0; i < SampleCount; i++)
        {
            float time = i / (SampleCount - 1f); // Identical float input for both evaluators.
            report.Times[i] = time;
            report.UnityValues[i] = source.Evaluate(time);
            report.RationalValues[i] = curve.Evaluate(time);
            report.Errors[i] = report.RationalValues[i] - report.UnityValues[i];
            report.SampledMaxError = Math.Max(report.SampledMaxError, Math.Abs(report.Errors[i]));
        }
        Check(report.RationalValues[0] == source[0].value && report.RationalValues[SampleCount - 1] == source[1].value,
            $"Authored key values changed: {report.RationalValues[0]:R}, {report.RationalValues[SampleCount - 1]:R}.");
        Check(Math.Abs(baked.Segments[0].EvaluateSlope(0) - source[0].outTangent) < 1e-10, "Start tangent changed.");
        Check(Math.Abs(baked.Segments[baked.SegmentCount - 1].EvaluateSlope(1) - source[1].inTangent) < 1e-10, "End tangent changed.");
        Check(report.SampledMaxError <= settings.MaxValueError + 1e-5, "Graph fixture exceeded its sampled value tolerance.");
        return report;
    }

    private static LookupReport SampleLookup()
    {
        float[] times = { 0, 0.69f, 0.72f, 0.91f, 1 };
        float[] values = { 0, 0.8f, 0.2f, 1.3f, 1 };
        var keys = new Keyframe[times.Length];
        for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(times[i], values[i], 0, 0);
        var source = new AnimationCurve(keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
        var curve = RationalCurveBuilder.Build(source, new CurveFitSettings(lookupMode: CurveLookupMode.Magic));
        Check(curve.ManagedLookup == CurveLookupStrategy.Magic && curve.BurstLookup == CurveLookupStrategy.Magic, "Magic was not selected.");
        Check(curve.SegmentCount == 4 && curve.IndexPrediction.Cubic == 0 && curve.IndexPrediction.Quadratic == 0,
            "The worked example requires four intervals and a linear predictor.");
        var binary = new BakedRationalCurve(curve.KeyTimes.ToArray(), curve.KeyValues.ToArray(), curve.Segments.ToArray());
        var report = new LookupReport
        {
            ManagedLookup = curve.ManagedLookup.ToString(), BurstLookup = curve.BurstLookup.ToString(),
            Prediction = curve.IndexPrediction, KeyTimes = curve.KeyTimes.ToArray(), KeyValues = curve.KeyValues.ToArray(),
            Times = new double[SampleCount], PredictedIndices = new double[SampleCount], QueryTime = 0.95
        };
        for (int i = 0; i < SampleCount; i++)
        {
            double time = i / (SampleCount - 1.0);
            report.Times[i] = time;
            Check(curve.Evaluate(time) == binary.Evaluate(time), "Lookup differs from binary search.");
            report.PredictedIndices[i] = curve.IndexPrediction.Evaluate(time - report.KeyTimes[0]);
        }
        for (int i = 0; i < keys.Length; i++)
        {
            Check(curve.Evaluate(report.KeyTimes[i]) == values[i], "Key lookup changed.");
            report.MaxKeyIndexError = Math.Max(report.MaxKeyIndexError,
                Math.Abs(curve.IndexPrediction.Evaluate(report.KeyTimes[i] - report.KeyTimes[0]) - i));
        }
        Check(curve.IndexPrediction.IsValidFor(curve.KeyTimes) && report.MaxKeyIndexError < 1, "Predictor validation failed.");
        report.PredictedIndex = curve.IndexPrediction.Evaluate(report.QueryTime - report.KeyTimes[0]);
        report.CandidateInterval = (int)Math.Max(0, Math.Min(curve.SegmentCount - 1, report.PredictedIndex));
        int hint = -1; // Force lookup instead of letting a valid hint bypass Magic.
        report.EvaluatedValue = curve.Evaluate(report.QueryTime, ref hint);
        report.ResolvedInterval = hint;
        Check(report.CandidateInterval == 2 && hint == 3 && report.QueryTime >= report.KeyTimes[3], "Expected a one-interval right correction.");
        Check(report.QueryTime < report.KeyTimes[4] && report.EvaluatedValue == curve.Segments[hint].Evaluate(report.QueryTime),
            "Resolved interval/evaluated value mismatch.");
        return report;
    }

    private static string CurveGraph(Report report)
    {
        var data = report.Curve;
        var svg = Begin("AnimationCurve and RCurve", "A weighted source curve and its actual bake, with signed error on a separate axis.", 640);
        Text(svg, 40, 38, "Same keys and tangents. Approximation between keys.", 23, "#0f172a");
        Text(svg, 40, 65, data.Settings, 14);
        Line(svg, 68, 96, 101, 96, Blue, 3);
        Text(svg, 110, 101, "Unity AnimationCurve", 14);
        Line(svg, 320, 96, 353, 96, Orange, 2, "7 4");
        Text(svg, 362, 101, "RCurve bake (dashed)", 14);
        var valuePlot = new Rect(80, 137, 850, 230);
        Axes(svg, valuePlot, 0, 1.2, "Value");
        foreach (double boundary in data.BakedBoundaries)
            Line(svg, valuePlot.x + boundary * valuePlot.width, valuePlot.y,
                valuePlot.x + boundary * valuePlot.width, valuePlot.yMax, "#94a3b8", 1, "3 5");
        Curve(svg, valuePlot, data.Times, data.UnityValues, 0, 1.2, Blue, 3);
        Curve(svg, valuePlot, data.Times, data.RationalValues, 0, 1.2, Orange, 2, "7 4");
        Dot(svg, valuePlot, 0, 0, 0, 1.2);
        Dot(svg, valuePlot, 1, 1, 0, 1.2);
        Text(svg, 80, 407, $"Signed error: RCurve - Unity   |   Max absolute sampled error: {data.SampledMaxError:G4}", 15, "#0f172a");
        double limit = Math.Max(1e-8, data.SampledMaxError * 1.25);
        var errorPlot = new Rect(80, 444, 850, 108);
        Axes(svg, errorPlot, -limit, limit, "Error");
        Curve(svg, errorPlot, data.Times, data.Errors, -limit, limit, Orange, 2);
        Text(svg, 80, 596, $"{SampleCount} shared time samples; error axis auto-scaled. Dashed verticals: baked interval boundaries.", 14);
        Text(svg, 80, 619, $"Unity {report.UnityVersion} | Current working tree | Not a continuous error bound or a speed benchmark.", 13);
        return svg.Append("</svg>\n").ToString();
    }

    private static string LookupGraph(Report report)
    {
        var data = report.Lookup;
        const string Purple = "#7c3aed", Green = "#15803d";
        const double firstVisible = 0.60, lastVisible = 1.0;
        double X(double time) => 70 + (time - firstVisible) / (lastVisible - firstVisible) * 850;
        double Center(int interval) => X((data.KeyTimes[interval] + data.KeyTimes[interval + 1]) / 2);
        double candidateX = Center(data.CandidateInterval), resolvedX = Center(data.ResolvedInterval);
        Check(data.KeyTimes[data.CandidateInterval] >= firstVisible && data.QueryTime < lastVisible,
            "The query and full candidate interval must remain visible.");
        var svg = Begin("Magic: guess nearby, then check the time",
            "On a proportional time axis zoomed to 0.60–1.00, the orange guessed interval 2 ends at 0.91. The purple query at 0.95 lies in green interval 3. Move one interval right.", 515);
        Text(svg, 40, 40, "Magic: guess nearby, then check the time.", 26, "#0f172a", bold: true);
        Text(svg, 70, 88, "1. Predict a nearby interval", 20, "#0f172a", bold: true);
        Box(svg, new Rect(70, 105, 520, 80), "#f8fafc", Grid);
        Text(svg, 135, 130, "query time", 13, Purple, "middle");
        Text(svg, 135, 158, $"t = {F(data.QueryTime)}", 23, Purple, "middle");
        Arrow(svg, 213, 147, 243, 147, "#64748b");
        Text(svg, 330, 130, "predicted index", 13, "#475569", "middle");
        Text(svg, 330, 158, "≈ " + F(data.PredictedIndex), 23, "#0f172a", "middle");
        Arrow(svg, 418, 147, 461, 147, Orange);
        Text(svg, candidateX, 130, "floor", 13, Orange, "middle");
        Text(svg, candidateX, 158, "i = " + data.CandidateInterval, 23, Orange, "middle");
        Arrow(svg, candidateX, 190, candidateX, 266, Orange);

        Text(svg, 70, 230, "2. Check the actual time", 20, "#0f172a", bold: true);
        double queryX = X(data.QueryTime);
        Text(svg, queryX, 236, $"t = {F(data.QueryTime)}", 23, Purple, "middle");
        for (int i = 0; i < data.KeyTimes.Length - 1; i++)
        {
            double start = Math.Max(firstVisible, data.KeyTimes[i]);
            double end = Math.Min(lastVisible, data.KeyTimes[i + 1]);
            if (start >= end) continue;
            bool candidate = i == data.CandidateInterval, resolved = i == data.ResolvedInterval;
            double left = X(start), right = X(end), center = (left + right) / 2;
            // One shared, proportional time axis; the leftmost interval is visibly cropped.
            Box(svg, new Rect((float)left, 270, (float)(right - left), 100),
                resolved ? "#f0fdf4" : candidate ? "#fff7ed" : "#f8fafc",
                resolved ? Green : candidate ? Orange : Grid);
            if (candidate || resolved)
            {
                if (resolved) center = (queryX + right) / 2; // Keep labels clear of the query line.
                Text(svg, center, 303, candidate ? "GUESS" : "CONTAINS t", 14, candidate ? Orange : Green, "middle");
                Text(svg, center, 340, "interval " + i, resolved ? 20 : 24, candidate ? Orange : Green, "middle");
            }
            else Text(svg, center, 328, i.ToString(), 18, "#94a3b8", "middle");
        }
        Line(svg, 70, 390, 920, 390, "#64748b", 1);
        // Break mark and zoom label distinguish the cropped interval from a real boundary.
        Line(svg, 65, 386, 70, 394, "#64748b", 2);
        Line(svg, 72, 386, 77, 394, "#64748b", 2);
        Text(svg, 70, 416, F(firstVisible), 14, "#64748b", "middle");
        foreach (double boundary in data.KeyTimes)
        {
            if (boundary <= firstVisible || boundary > lastVisible) continue;
            Line(svg, X(boundary), 370, X(boundary), 396, "#64748b", 1);
            Text(svg, X(boundary), 416, F(boundary), 17, "#0f172a", "middle");
        }
        Line(svg, queryX, 245, queryX, 390, Purple, 2, "5 4");
        svg.Append($"<circle cx=\"{F(queryX)}\" cy=\"390\" r=\"5\" fill=\"{Purple}\"/>\n");
        Text(svg, 947, 416, "time", 13);

        Arrow(svg, candidateX, 455, resolvedX, 455, Green);
        Text(svg, (candidateX + resolvedX) / 2, 439,
            $"t = {F(data.QueryTime)} > times[{data.CandidateInterval + 1}] = {F(data.KeyTimes[data.CandidateInterval + 1])}", 18, Green, "middle");
        Text(svg, (candidateX + resolvedX) / 2, 483,
            $"{data.CandidateInterval} + 1 → {data.ResolvedInterval}", 20, Green, "middle");
        return svg.Append("</svg>\n").ToString();
    }

    private static void Arrow(StringBuilder svg, double x1, double y1, double x2, double y2, string color)
    {
        double length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        double dx = (x2 - x1) / length, dy = (y2 - y1) / length;
        Line(svg, x1, y1, x2, y2, color, 2);
        svg.Append($"<polygon points=\"{F(x2)},{F(y2)} {F(x2 - 9 * dx + 4 * dy)},{F(y2 - 9 * dy - 4 * dx)} {F(x2 - 9 * dx - 4 * dy)},{F(y2 - 9 * dy + 4 * dx)}\" fill=\"{color}\"/>\n");
    }

    private static void Box(StringBuilder svg, Rect rect, string fill, string stroke) =>
        svg.Append($"<rect x=\"{F(rect.x)}\" y=\"{F(rect.y)}\" width=\"{F(rect.width)}\" height=\"{F(rect.height)}\" rx=\"8\" fill=\"{fill}\" stroke=\"{stroke}\"/>\n");

    private static StringBuilder Begin(string title, string description, int height)
    {
        return new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1000 {height}\" role=\"img\" aria-labelledby=\"title desc\">\n<title id=\"title\">{Escape(title)}</title><desc id=\"desc\">{Escape(description)}</desc>\n<rect width=\"1000\" height=\"{height}\" rx=\"12\" fill=\"#ffffff\"/>\n");
    }

    private static void Axes(StringBuilder svg, Rect plot, double minimum, double maximum, string label)
    {
        Text(svg, plot.x, plot.y - 10, label, 13);
        for (int i = 0; i <= 4; i++)
        {
            double x = plot.x + plot.width * i / 4.0;
            Line(svg, x, plot.y, x, plot.yMax, Grid, 1);
            Text(svg, x, plot.yMax + 20, F(i / 4.0), 12, "#475569", "middle");
        }
        for (int i = 0; i <= 4; i++)
        {
            double y = plot.y + plot.height * i / 4.0;
            Line(svg, plot.x, y, plot.xMax, y, Grid, 1);
            Text(svg, plot.x - 9, y + 4, (maximum - (maximum - minimum) * i / 4).ToString("G3", CultureInfo.InvariantCulture), 12, "#475569", "end");
        }
        Text(svg, plot.xMax, plot.yMax + 37, "Time", 12, "#475569", "end");
    }

    private static void Curve(StringBuilder svg, Rect plot, double[] times, double[] values,
        double minimum, double maximum, string color, double width, string dash = "")
    {
        svg.Append($"<polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"{F(width)}\" stroke-dasharray=\"{dash}\" points=\"");
        for (int i = 0; i < times.Length; i++)
        {
            Check(!double.IsNaN(values[i]) && !double.IsInfinity(values[i]), "Nonfinite graph value.");
            svg.Append($"{F(plot.x + times[i] * plot.width)},{F(plot.yMax - (values[i] - minimum) / (maximum - minimum) * plot.height)} ");
        }
        svg.Append("\"/>\n");
    }

    private static void Dot(StringBuilder svg, Rect plot, double time, double value, double min, double max) =>
        svg.Append($"<circle cx=\"{F(plot.x + time * plot.width)}\" cy=\"{F(plot.yMax - (value - min) / (max - min) * plot.height)}\" r=\"3\" fill=\"{Blue}\"/>\n");
    private static void Text(StringBuilder svg, double x, double y, string text, int size = 14, string color = "#475569", string anchor = "start", bool bold = false) =>
        svg.Append($"<text x=\"{F(x)}\" y=\"{F(y)}\" font-family=\"Arial, sans-serif\" font-size=\"{size}\" font-weight=\"{(bold ? "700" : "400")}\" fill=\"{color}\" text-anchor=\"{anchor}\">{Escape(text)}</text>\n");
    private static void Line(StringBuilder svg, double x1, double y1, double x2, double y2, string color, double width, string dash = "") =>
        svg.Append($"<line x1=\"{F(x1)}\" y1=\"{F(y1)}\" x2=\"{F(x2)}\" y2=\"{F(y2)}\" stroke=\"{color}\" stroke-width=\"{F(width)}\" stroke-dasharray=\"{dash}\"/>\n");
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Escape(string text) => SecurityElement.Escape(text);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
