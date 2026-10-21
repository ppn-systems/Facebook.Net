// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;

namespace Facebook.Net.Protocol;

/// <summary>
/// Generates humanized Box-Muller Gaussian (normal distribution) delays with sub-second millisecond micro-jitter
/// so automated campaign intervals mimic natural human cognitive/reading cadence rather than flat integer-second timers.
/// </summary>
public static class HumanJitter
{
    /// <summary>
    /// Samples a truncated Gaussian delay bounded within <c>[minSeconds, maxSeconds]</c> with millisecond resolution.
    /// </summary>
    public static TimeSpan NextGaussianDelay(int minSeconds, int maxSeconds)
    {
        int clampedMin = Math.Max(0, minSeconds);
        int clampedMax = Math.Max(clampedMin, maxSeconds);
        if (clampedMax <= 0)
        {
            return TimeSpan.Zero;
        }

        double minMs = clampedMin * 1000.0;
        double maxMs = clampedMax * 1000.0;
        if (Math.Abs(maxMs - minMs) < 1.0)
        {
            return TimeSpan.FromMilliseconds(minMs);
        }

        double mean = (minMs + maxMs) / 2.0;
        double stdDev = Math.Max(150.0, (maxMs - minMs) / 4.0);

        // Box-Muller transform for standard normal Z ~ N(0, 1)
        double u1 = 1.0 - Random.Shared.NextDouble();
        double u2 = 1.0 - Random.Shared.NextDouble();
        double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

        double sampledMs = mean + (stdDev * randStdNormal);
        double boundedMs = Math.Clamp(sampledMs, minMs, maxMs);
        return TimeSpan.FromMilliseconds(Math.Round(boundedMs));
    }
}
