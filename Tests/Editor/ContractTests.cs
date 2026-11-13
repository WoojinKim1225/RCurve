using System;
using System.Reflection;
using NUnit.Framework;
using RCurve;
using RCurve.Tests;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// The promises 1.0 keeps: the baked format is versioned, baking is deterministic, the error bounds hold against
// the exact source and against Unity's AnimationCurve, and failures are the documented exceptions.
public sealed class ContractTests
{
    [Test]
    public void BuilderVersionIsThePackageVersion()
    {
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(RationalCurve).Assembly);
        Assert.That(package, Is.Not.Null);
        Assert.That(RationalCurve.BuilderVersion, Is.EqualTo(package.version));
    }

    [Test]
    public void BakeRecordsItsFormatAndANewerFormatIsRefused()
    {
        var gameObject = new GameObject("format host");
        try
        {
            var host = gameObject.AddComponent<CurveTestHost>();
            host.Curve = new RationalCurve(AnimationCurve.EaseInOut(0, 0, 1, 1));
            host.Curve.Bake();
            using (var serialized = new SerializedObject(host))
            {
                Assert.That(serialized.FindProperty("Curve._baked.Format").intValue, Is.EqualTo(RationalCurve.FormatVersion));
                Assert.That(serialized.FindProperty("Curve._baked.Builder").stringValue, Is.EqualTo(RationalCurve.BuilderVersion));
                serialized.FindProperty("Curve._baked.Format").intValue = RationalCurve.FormatVersion + 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(() => host.Curve.Evaluate(0.5), Throws.InvalidOperationException.With.Message.Contains("format"));
            using (var serialized = new SerializedObject(host))
            {
                // data written before the field existed reads as format 0: the same layout, still evaluated
                serialized.FindProperty("Curve._baked.Format").intValue = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(host.Curve.Evaluate(0.5), Is.EqualTo(0.5).Within(1e-3));
        }
        finally { Object.DestroyImmediate(gameObject); }
    }

    // The input hash decides whether a bake still matches its source; 1.x never changes it for an existing input
    // (a changed hash would make every existing bake look stale). These are pinned values: if this fails, the
    // change breaks every asset baked before it.
    [Test]
    public void InputHashOfAnExistingInputNeverChanges()
    {
        string linear = new RationalCurve(AnimationCurve.Linear(0, 0, 1, 1)).InputHash;
        string weighted = new RationalCurve(new AnimationCurve(
                new Keyframe(0, 0, 0, 3, 0, 0.15f) { weightedMode = WeightedMode.Out },
                new Keyframe(1, 1, -0.7f, 0, 0.65f, 0) { weightedMode = WeightedMode.In }),
            new CurveFitSettings(CurveFitObjective.LeastSquares, CurveFitQuality.Medium, 1e-4, 1e-3, 6, CurveLookupMode.Magic)).InputHash;
        string step = new RationalCurve(new AnimationCurve(new Keyframe(0, 2, 0, float.PositiveInfinity), new Keyframe(1, 5))
            { preWrapMode = WrapMode.PingPong, postWrapMode = WrapMode.Loop }).InputHash;
        Assert.That($"{linear} {weighted} {step}", Is.EqualTo(Pinned));
    }

    // linear, weighted with non-default settings, step with wrap modes
    private const string Pinned =
        "F843A5F81B2DB32CE31D592AA1C595B626346329B825D9C94BE02146D747EBF7 " +
        "6FE46F73269C6E78241DE3EA9B2BD36C12AD50407D25557C83E9D82F93E2EEA4 " +
        "988DE569386048B8B5123778DF31369530430E913A28D73E6CE9D1DE215E7972";

    [Test]
    public void BakingIsDeterministic()
    {
        var random = new System.Random(7);
        for (int c = 0; c < 40; c++)
        {
            AnimationCurve source = RandomCurve(random, allowSteps: true);
            BakedRationalCurve first, second;
            try
            {
                first = RationalCurveBuilder.Build(source);
                second = RationalCurveBuilder.Build(source);
            }
            catch (InvalidOperationException) { continue; }
            Assert.That(second.KeyTimes.ToArray(), Is.EqualTo(first.KeyTimes.ToArray()));
            Assert.That(second.KeyValues.ToArray(), Is.EqualTo(first.KeyValues.ToArray()));
            for (int i = 0; i < first.SegmentCount; i++)
            {
                RationalCurveSegment a = first.Segments[i], b = second.Segments[i];
                Assert.That(b.IsStep, Is.EqualTo(a.IsStep));
                Assert.That(b.NumeratorCoefficients.ToArray(), Is.EqualTo(a.NumeratorCoefficients.ToArray()), $"curve {c} segment {i}");
                Assert.That(b.DenominatorCoefficients.ToArray(), Is.EqualTo(a.DenominatorCoefficients.ToArray()), $"curve {c} segment {i}");
            }
        }
    }

    // Differential test over random curves (weighted per side or not, mixed In/Out, steps): the baked curve stays
    // within the settings' bounds of the exact source (dense in the Bezier parameter, in double). Against Unity's own
    // AnimationCurve.Evaluate (float) it stays within the value bound plus a float allowance on unweighted segments.
    // On weighted segments Unity is not an exact reference: its solver was seen to miss its own Bezier by 0.02 (not
    // even returning the key's value at the key's time) on 1 of these 500 curves, so there disagreements are counted,
    // not asserted. A curve that cannot be baked fails only with InvalidOperationException (the fit's limits).
    [Test]
    public void RandomCurvesStayWithinTheirBoundsOfTheSourceAndOfUnity()
    {
        var random = new System.Random(20261113);
        var settings = new CurveFitSettings();
        const double unityAllowance = 1e-4;
        int baked = 0, unbakeable = 0, weightedUnityMisses = 0;
        double worstValue = 0, worstSlope = 0, worstUnity = 0;
        for (int c = 0; c < 500; c++)
        {
            AnimationCurve source = RandomCurve(random, allowSteps: true);
            BakedRationalCurve curve;
            try { curve = RationalCurveBuilder.Build(source, settings); }
            catch (InvalidOperationException) { unbakeable++; continue; }
            baked++;
            Keyframe[] keys = source.keys;
            for (int k = 0; k + 1 < keys.Length; k++)
            {
                var segment = new AnimationCurveBezierSegment(keys[k], keys[k + 1]);
                bool weighted = (keys[k].weightedMode & WeightedMode.Out) != 0 || (keys[k + 1].weightedMode & WeightedMode.In) != 0;
                bool missed = false;
                double length = segment.EndTime - segment.StartTime;
                for (int i = 1; i < 400; i++)
                {
                    double s = i / 400.0;
                    var point = segment.Evaluate(s);
                    double time = segment.StartTime + length * point.x;
                    if (!(time > segment.StartTime && time < segment.EndTime)) continue;
                    double value = curve.Evaluate(time);
                    double valueError = Math.Abs(point.y - value);
                    worstValue = Math.Max(worstValue, valueError / settings.MaxValueError);
                    Assert.That(valueError, Is.LessThanOrEqualTo(settings.MaxValueError * (1 + 1e-9) + 1e-12),
                        $"curve {c} segment {k} at {time}: value");
                    if (!segment.IsStep)
                    {
                        var derivative = segment.EvaluateDerivative(s);
                        double slopeError = Math.Abs(derivative.y / (derivative.x * length) - curve.EvaluateSlope(time));
                        worstSlope = Math.Max(worstSlope, slopeError / settings.MaxSlopeError);
                        Assert.That(slopeError, Is.LessThanOrEqualTo(settings.MaxSlopeError * (1 + 1e-9) + 1e-12),
                            $"curve {c} segment {k} at {time}: slope");
                    }
                    double unityError = Math.Abs(source.Evaluate((float)time) - value);
                    if (weighted)
                    {
                        missed |= unityError > settings.MaxValueError + unityAllowance;
                        continue;
                    }
                    worstUnity = Math.Max(worstUnity, unityError);
                    Assert.That(unityError, Is.LessThanOrEqualTo(settings.MaxValueError + unityAllowance),
                        $"curve {c} segment {k} at {time}: against Unity");
                }
                if (missed) weightedUnityMisses++;
            }
        }
        TestContext.WriteLine($"baked {baked}, unbakeable {unbakeable}; worst value {worstValue:0.###} and slope " +
                              $"{worstSlope:0.###} of their bounds, worst against Unity (unweighted) {worstUnity:E3}; " +
                              $"weighted segments where Unity disagrees {weightedUnityMisses}");
        Assert.That(baked, Is.GreaterThan(450));
    }

    // EvaluateSlope never returns NaN for a finite time: at every key, wrap seam and turnaround of random curves (with
    // corners and steps) it is the owning segment's slope times the wrap direction.
    [Test]
    public void SlopeIsNeverNaNForAFiniteTime()
    {
        var random = new System.Random(31);
        for (int c = 0; c < 200; c++)
        {
            AnimationCurve source = RandomCurve(random, allowSteps: true);
            source.preWrapMode = (WrapMode)new[] { WrapMode.Loop, WrapMode.PingPong, WrapMode.ClampForever }[random.Next(3)];
            source.postWrapMode = (WrapMode)new[] { WrapMode.Loop, WrapMode.PingPong, WrapMode.ClampForever }[random.Next(3)];
            BakedRationalCurve curve;
            try { curve = RationalCurveBuilder.Build(source); }
            catch (InvalidOperationException) { continue; }
            double[] times = curve.KeyTimes.ToArray();
            double first = times[0], span = times[times.Length - 1] - first;
            for (int i = 0; i < times.Length; i++)
            {
                double slope = curve.EvaluateSlope(times[i]);
                Assert.That(double.IsNaN(slope), Is.False, $"curve {c} key {i}");
                double owner = i < curve.SegmentCount
                    ? curve.Segments[i].EvaluateSlope(times[i])
                    : curve.Segments[i - 1].EvaluateSlope(times[i]);
                if (!(i == times.Length - 1 && (source.postWrapMode == WrapMode.Loop)))
                    Assert.That(slope, Is.EqualTo(owner), $"curve {c} key {i}: the owning segment's slope");
            }
            for (int k = -3; k <= 3; k++)
                Assert.That(double.IsNaN(curve.EvaluateSlope(first + k * span)), Is.False, $"curve {c} at {k} spans");
        }
    }

    // Magic's one-step correction is proven at bake time, so the managed lookup's miss path (which throws) is never
    // reached: query every key, its neighbouring doubles, interval midpoints, random times and wrapped times, and
    // compare with the same curve baked for brute-force lookup.
    [Test]
    public void MagicLookupNeverMissesAfterItsOneCorrection()
    {
        var random = new System.Random(4242);
        int magicCurves = 0;
        for (int c = 0; c < 300; c++)
        {
            AnimationCurve source = RandomCurve(random, allowSteps: true);
            source.preWrapMode = random.Next(2) == 0 ? WrapMode.Loop : WrapMode.ClampForever;
            source.postWrapMode = random.Next(2) == 0 ? WrapMode.PingPong : WrapMode.Loop;
            BakedRationalCurve magic, brute;
            try
            {
                magic = RationalCurveBuilder.Build(source, new CurveFitSettings(lookupMode: CurveLookupMode.Magic));
                brute = RationalCurveBuilder.Build(source, new CurveFitSettings(lookupMode: CurveLookupMode.BruteForce));
            }
            catch (InvalidOperationException) { continue; }
            if (magic.ManagedLookup != CurveLookupStrategy.Magic) continue;
            magicCurves++;
            double[] times = magic.KeyTimes.ToArray();
            double first = times[0], span = times[times.Length - 1] - first;
            var queries = new System.Collections.Generic.List<double>();
            for (int i = 0; i < times.Length; i++)
            {
                queries.Add(times[i]);
                queries.Add(Neighbour(times[i], -1));
                queries.Add(Neighbour(times[i], +1));
                if (i + 1 < times.Length) queries.Add(0.5 * (times[i] + times[i + 1]));
            }
            for (int i = 0; i < 200; i++) queries.Add(first - span + random.NextDouble() * 3 * span);
            foreach (double time in queries)
            {
                double expected = brute.Evaluate(time);
                double actual = magic.Evaluate(time);
                Assert.That(actual.Equals(expected), Is.True, $"curve {c} at {time:R}: {actual:R} vs {expected:R}");
            }
        }
        TestContext.WriteLine($"curves using Magic: {magicCurves}");
        Assert.That(magicCurves, Is.GreaterThan(100));
    }

    private static double Neighbour(double value, int direction)
    {
        if (value == 0) return direction * double.Epsilon;
        long bits = BitConverter.DoubleToInt64Bits(value);
        return BitConverter.Int64BitsToDouble(value > 0 ? bits + direction : bits - direction);
    }

    [Test]
    public void EdgeCasesBakeLikeUnityOrFailWithTheDocumentedException()
    {
        // single key: its value everywhere; no keys: 0, as Unity
        var single = new AnimationCurve(new Keyframe(0.3f, 2.5f));
        BakedRationalCurve singleBaked = RationalCurveBuilder.Build(single);
        foreach (float t in new[] { -1f, 0.3f, 2f })
        {
            Assert.That(singleBaked.Evaluate(t), Is.EqualTo(2.5).Within(1e-12));
            Assert.That(single.Evaluate(t), Is.EqualTo(2.5f).Within(1e-6));
        }
        var empty = new AnimationCurve();
        Assert.That(RationalCurveBuilder.Build(empty).Evaluate(0.5), Is.EqualTo(0));
        Assert.That(empty.Evaluate(0.5f), Is.EqualTo(0));

        // a zero-length handle, and both handles full length (dx/ds = 0 at s = 1/2): not representable
        Assert.Throws<NotSupportedException>(() => RationalCurveBuilder.Build(new AnimationCurve(
            new Keyframe(0, 0, 0, 1, 0, 0) { weightedMode = WeightedMode.Out }, new Keyframe(1, 1))));
        Assert.Throws<NotSupportedException>(() => RationalCurveBuilder.Build(new AnimationCurve(
            new Keyframe(0, 0, 0, 1, 0, 1) { weightedMode = WeightedMode.Out },
            new Keyframe(1, 1, 1, 0, 1, 0) { weightedMode = WeightedMode.In })));

        // a weight out of [0,1]: refused, never clamped (unless Unity already clamped it)
        var heavy = new AnimationCurve(new Keyframe(0, 0, 0, 1, 0, 1.5f) { weightedMode = WeightedMode.Out }, new Keyframe(1, 1));
        if (heavy.keys[0].outWeight > 1) Assert.Throws<ArgumentOutOfRangeException>(() => RationalCurveBuilder.Build(heavy));
        else MatchesUnity(heavy);

        // a step: the held value, as Unity
        MatchesUnity(new AnimationCurve(new Keyframe(0, 1, 0, float.PositiveInfinity), new Keyframe(1, 3), new Keyframe(2, 0)));

        // two keys at one time, and keys given in reverse: as Unity keeps them, or refused as not increasing
        foreach (AnimationCurve odd in new[]
                 {
                     new AnimationCurve(new Keyframe(0, 0), new Keyframe(0, 1), new Keyframe(1, 2)),
                     new AnimationCurve(new Keyframe(2, 0), new Keyframe(1, 3), new Keyframe(0, 1))
                 })
        {
            Keyframe[] keys = odd.keys;
            bool increasing = true;
            for (int i = 1; i < keys.Length; i++) increasing &= keys[i].time > keys[i - 1].time;
            if (increasing) MatchesUnity(odd);
            else Assert.Throws<ArgumentException>(() => RationalCurveBuilder.Build(odd));
        }
    }

    private static void MatchesUnity(AnimationCurve source)
    {
        BakedRationalCurve curve = RationalCurveBuilder.Build(source);
        Keyframe[] keys = source.keys;
        for (int k = 0; k + 1 < keys.Length; k++)
            for (int i = 1; i < 50; i++)
            {
                float time = Mathf.Lerp(keys[k].time, keys[k + 1].time, i / 50f);
                Assert.That(curve.Evaluate(time), Is.EqualTo(source.Evaluate(time)).Within(new CurveFitSettings().MaxValueError + 1e-4));
            }
    }

    // 2 to 5 keys one unit or so apart; each side weighted or not on its own; now and then a step
    private static AnimationCurve RandomCurve(System.Random random, bool allowSteps)
    {
        int count = 2 + random.Next(4);
        var keys = new Keyframe[count];
        float time = 0;
        for (int i = 0; i < count; i++)
        {
            float outTangent = allowSteps && random.Next(12) == 0 ? float.PositiveInfinity : Range(random, -5, 5);
            var key = new Keyframe(time, Range(random, -2, 2), Range(random, -5, 5), outTangent);
            WeightedMode mode = WeightedMode.None;
            if (random.Next(2) == 0) mode |= WeightedMode.In;
            if (random.Next(2) == 0) mode |= WeightedMode.Out;
            key.weightedMode = mode;
            key.inWeight = Range(random, 0.05f, 0.95f);
            key.outWeight = Range(random, 0.05f, 0.95f);
            keys[i] = key;
            time += Range(random, 0.2f, 2f);
        }
        return new AnimationCurve(keys);
    }

    private static float Range(System.Random random, float min, float max) => (float)(min + random.NextDouble() * (max - min));
}
