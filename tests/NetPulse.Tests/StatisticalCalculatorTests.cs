using System.Collections.Generic;
using NetPulse.Core.Statistics;
using Xunit;

namespace NetPulse.Tests;

public class StatisticalCalculatorTests
{
    [Fact]
    public void TestRfc3550JitterCalculation()
    {
        // Initial probe has no previous jitter
        double j0 = 0.0;
        double j1 = StatisticalCalculator.UpdateRfc3550Jitter(j0, 20.0, 10.0);
        Assert.Equal(10.0, j1);

        // Next probe: latency 22ms (diff = |22 - 20| = 2ms)
        // J_new = 10 + (2 - 10)/16 = 10 - 0.5 = 9.5
        double j2 = StatisticalCalculator.UpdateRfc3550Jitter(j1, 22.0, 20.0);
        Assert.Equal(9.5, j2);
    }

    [Fact]
    public void TestMedianCalculation()
    {
        // Odd count: [10, 20, 30] -> 20
        var odd = new List<double> { 30, 10, 20 };
        Assert.Equal(20.0, StatisticalCalculator.CalculateMedian(odd));

        // Even count: [10, 20, 30, 40] -> 25
        var even = new List<double> { 40, 10, 30, 20 };
        Assert.Equal(25.0, StatisticalCalculator.CalculateMedian(even));

        // Single item
        Assert.Equal(15.5, StatisticalCalculator.CalculateMedian(new List<double> { 15.5 }));

        // Empty
        Assert.Equal(0.0, StatisticalCalculator.CalculateMedian(new List<double>()));
    }

    [Fact]
    public void TestPercentileCalculation()
    {
        // 100 values from 1 to 100
        var values = new List<double>();
        for (int i = 1; i <= 100; i++) values.Add(i);

        double p95 = StatisticalCalculator.CalculatePercentile(values, 0.95);
        Assert.True(p95 >= 94.0 && p95 <= 96.0);

        double p50 = StatisticalCalculator.CalculatePercentile(values, 0.50);
        Assert.True(p50 >= 49.0 && p50 <= 51.5);
    }

    [Fact]
    public void TestPacketLossCalculation()
    {
        Assert.Equal(0.0, StatisticalCalculator.CalculatePacketLoss(0, 100));
        Assert.Equal(25.0, StatisticalCalculator.CalculatePacketLoss(25, 100));
        Assert.Equal(100.0, StatisticalCalculator.CalculatePacketLoss(10, 10));
        Assert.Equal(0.0, StatisticalCalculator.CalculatePacketLoss(0, 0));
    }
}
