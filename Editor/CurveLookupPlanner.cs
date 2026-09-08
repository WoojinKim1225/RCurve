using System;

namespace RCurve.Editor
{
    // Editor-only deterministic policy. Uniform mapped-time workload, no per-bake timing or runtime fitting.
    // Costs are relative lookup units calibrated by CurveLookupStrategyTests, not promised nanoseconds.
    public static class CurveLookupPlanner
    {
        public const int MaximumLutBytes = 1024;
        private const int _samples = 256;

        public static void Plan(double[] times, CurveIndexPrediction prediction, CurveLookupMode mode,
            out CurveLookupStrategy managed, out CurveLookupStrategy burst, out int[] table, out double scale)
        {
            if (times == null) throw new ArgumentNullException(nameof(times));
            if (mode < CurveLookupMode.Auto || mode > CurveLookupMode.Magic) throw new ArgumentOutOfRangeException(nameof(mode));
            int segments = Math.Max(0, times.Length - 1);
            managed = burst = CurveLookupStrategy.BinarySearch;
            table = Array.Empty<int>();
            scale = 0;
            if (segments == 0) return;
            if (mode == CurveLookupMode.BruteForce) { managed = burst = CurveLookupStrategy.BruteForce; return; }
            if (mode == CurveLookupMode.Magic)
            {
                if (prediction.IsEnabled) managed = burst = CurveLookupStrategy.Magic;
                return;
            }
            int cells = (int)Math.Min(MaximumLutBytes / sizeof(int) - 1, (long)segments * 4);
            double length = times[times.Length - 1] - times[0];
            scale = cells / length;
            if (scale > 0 && !double.IsInfinity(scale))
            {
                table = new int[cells + 1];
                for (int i = 0; i <= cells; i++)
                {
                    double time = i == cells ? times[times.Length - 1] : times[0] + length * (i / (double)cells);
                    int key = Array.BinarySearch(times, time);
                    table[i] = key >= 0 ? Math.Min(key, segments - 1) : Math.Max(0, ~key - 1);
                }
            }
            else scale = 0;
            if (mode == CurveLookupMode.LUT)
            {
                if (table.Length > 0) managed = burst = CurveLookupStrategy.LUT;
                return;
            }
            double comparisons = 0, corrections = 0, lutWork = 0;
            for (int i = 0; i < _samples; i++)
            {
                double time = times[0] + length * ((i + 0.5) / _samples);
                int key = Array.BinarySearch(times, time);
                int actual = key >= 0 ? Math.Min(key, segments - 1) : ~key - 1;
                comparisons += actual + 1;
                if (prediction.IsEnabled && (int)Math.Max(0, Math.Min(segments - 1, prediction.Evaluate(time - times[0]))) != actual)
                    corrections++;
                if (table.Length > 0)
                {
                    int cell = (int)Math.Max(0, Math.Min(cells - 1, (time - times[0]) * scale));
                    if (actual != table[cell]) lutWork += Math.Log(table[cell + 1] - table[cell] + 2, 2);
                }
            }
            comparisons /= _samples; corrections /= _samples; lutWork /= _samples;
            double binaryWork = Math.Log(times.Length, 2);
            managed = Select(3 + 4 * binaryWork, 3 + 1.8 * comparisons,
                table.Length > 0 ? 9 + 3 * lutWork : double.PositiveInfinity,
                prediction.IsEnabled ? 10 + 4 * corrections : double.PositiveInfinity);
            burst = Select(1 + 1.8 * binaryWork, 2 + 1.8 * comparisons,
                table.Length > 0 ? 5.5 + 3 * lutWork : double.PositiveInfinity,
                prediction.IsEnabled ? 7 + 2 * corrections : double.PositiveInfinity);
            if (managed != CurveLookupStrategy.LUT && burst != CurveLookupStrategy.LUT) { table = Array.Empty<int>(); scale = 0; }
        }

        private static CurveLookupStrategy Select(double binary, double bruteForce, double lut, double magic)
        {
            var strategy = CurveLookupStrategy.BinarySearch;
            double cost = binary;
            if (bruteForce < cost) { cost = bruteForce; strategy = CurveLookupStrategy.BruteForce; }
            if (lut < cost) { cost = lut; strategy = CurveLookupStrategy.LUT; }
            if (magic < cost) strategy = CurveLookupStrategy.Magic;
            return strategy;
        }
    }
}
