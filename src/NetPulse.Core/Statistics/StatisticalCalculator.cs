using System;
using System.Collections.Generic;
using System.Linq;

namespace NetPulse.Core.Statistics;

public static class StatisticalCalculator
{
    /// <summary>
    /// Computes RFC 3550 standard interarrival jitter:
    /// J = J + (|D(i-1, i)| - J) / 16
    /// </summary>
    public static double UpdateRfc3550Jitter(double currentJitter, double currentLatency, double previousLatency)
    {
        double diff = Math.Abs(currentLatency - previousLatency);
        if (currentJitter <= 0)
        {
            return diff;
        }
        return currentJitter + (diff - currentJitter) / 16.0;
    }

    /// <summary>
    /// Computes rolling mean of the provided sequence.
    /// </summary>
    public static double CalculateMean(IReadOnlyList<double> values)
    {
        if (values == null || values.Count == 0) return 0;
        double sum = 0;
        for (int i = 0; i < values.Count; i++)
        {
            sum += values[i];
        }
        return sum / values.Count;
    }

    /// <summary>
    /// Computes the median of the provided sequence.
    /// </summary>
    public static double CalculateMedian(IReadOnlyList<double> values)
    {
        if (values == null || values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        int count = sorted.Count;
        if (count % 2 == 1)
        {
            return sorted[count / 2];
        }
        return (sorted[(count / 2) - 1] + sorted[count / 2]) / 2.0;
    }

    /// <summary>
    /// Computes the specified percentile (e.g. 0.95 for P95).
    /// </summary>
    public static double CalculatePercentile(IReadOnlyList<double> values, double percentile)
    {
        if (values == null || values.Count == 0) return 0;
        if (percentile <= 0) return values.Min();
        if (percentile >= 1.0) return values.Max();

        var sorted = values.OrderBy(v => v).ToList();
        double rank = percentile * (sorted.Count - 1);
        int low = (int)Math.Floor(rank);
        int high = (int)Math.Ceiling(rank);
        double weight = rank - low;

        if (low == high) return sorted[low];
        return sorted[low] * (1.0 - weight) + sorted[high] * weight;
    }

    /// <summary>
    /// Computes packet loss percentage (0.0 to 100.0).
    /// </summary>
    public static double CalculatePacketLoss(int failedCount, int totalCount)
    {
        if (totalCount <= 0) return 0;
        return Math.Clamp((double)failedCount / totalCount * 100.0, 0.0, 100.0);
    }
}
