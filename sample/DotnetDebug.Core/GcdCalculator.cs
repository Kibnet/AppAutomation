using System;
using System.Collections.Generic;

namespace DotnetDebug;

public static class GcdCalculator
{
    public static long ComputeGcd(IEnumerable<long> numbers)
    {
        return ComputeGcdWithSteps(numbers).Result;
    }

    public static long ComputeGcd(long a, long b)
    {
        return ComputeGcdPair(a, b).Result;
    }

    public static GcdComputationResult ComputeGcdWithSteps(IEnumerable<long> numbers)
    {
        if (numbers is null)
        {
            throw new ArgumentNullException(nameof(numbers));
        }

        var computations = new List<GcdPairComputation>();
        using var enumerator = numbers.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new ArgumentException("At least one number is required.", nameof(numbers));
        }

        var firstNumber = enumerator.Current;
        var hasNonZero = firstNumber != 0;
        var gcd = Math.Abs(firstNumber);
        computations.Add(new GcdPairComputation(firstNumber, 0, gcd, Array.Empty<GcdDivisionStep>()));

        while (enumerator.MoveNext())
        {
            var n = enumerator.Current;
            hasNonZero |= n != 0;

            var pair = ComputeGcdPair(gcd, n);
            computations.Add(pair);
            gcd = pair.Result;
        }

        if (!hasNonZero)
        {
            throw new ArgumentException("GCD is undefined for all zeros.", nameof(numbers));
        }

        return new GcdComputationResult(gcd, computations);
    }

    private static GcdPairComputation ComputeGcdPair(long a, long b)
    {
        var originalA = a;
        var originalB = b;
        a = Math.Abs(a);
        b = Math.Abs(b);

        var steps = new List<GcdDivisionStep>();
        while (b != 0)
        {
            var quotient = a / b;
            var remainder = a % b;
            steps.Add(new GcdDivisionStep(a, b, quotient, remainder));
            a = b;
            b = remainder;
        }

        return new GcdPairComputation(originalA, originalB, a, steps);
    }
}

public sealed record GcdComputationResult(long Result, IReadOnlyList<GcdPairComputation> PairComputations);

public sealed record GcdPairComputation(
    long Left,
    long Right,
    long Result,
    IReadOnlyList<GcdDivisionStep> Steps);

public sealed record GcdDivisionStep(long Dividend, long Divisor, long Quotient, long Remainder);
