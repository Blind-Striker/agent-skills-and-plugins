using BadgeKit;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<LabelBenchmarks>();

public class LabelBenchmarks
{
    [Benchmark]
    public string Legacy() => BadgeLabel.Format(1500, BadgeProfile.Legacy);

    [Benchmark]
    public string Current() => BadgeLabel.Format(1500, BadgeProfile.Current);
}
