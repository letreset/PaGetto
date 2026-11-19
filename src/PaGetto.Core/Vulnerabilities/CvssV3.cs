using System;
using System.Collections.Generic;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Vulnerabilities;

/// <summary>
/// The base score of a CVSS v3.0 or v3.1 vector, as the CVSS v3.1 specification computes it.
/// </summary>
public static class CvssV3
{
    /// <summary>
    /// Computes the base score of a vector such as <c>CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H</c>.
    /// </summary>
    /// <param name="vector">The vector.</param>
    /// <param name="score">The base score, from 0.0 to 10.0.</param>
    /// <returns>False when the vector is not a complete CVSS v3 vector.</returns>
    public static bool TryGetBaseScore(string vector, out double score)
    {
        score = 0;
        if (vector == null
            || !(vector.StartsWith("CVSS:3.0/", StringComparison.Ordinal) || vector.StartsWith("CVSS:3.1/", StringComparison.Ordinal)))
        {
            return false;
        }

        var metrics = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in vector.Substring("CVSS:3.x/".Length).Split('/'))
        {
            var separator = part.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                return false;
            }

            metrics[part.Substring(0, separator)] = part.Substring(separator + 1);
        }

        if (!metrics.TryGetValue("S", out var scope) || (scope != "U" && scope != "C"))
        {
            return false;
        }

        var changed = scope == "C";
        if (!TryGetWeight(metrics, "AV", new() { ["N"] = 0.85, ["A"] = 0.62, ["L"] = 0.55, ["P"] = 0.2 }, out var attackVector)
            || !TryGetWeight(metrics, "AC", new() { ["L"] = 0.77, ["H"] = 0.44 }, out var attackComplexity)
            || !TryGetWeight(metrics, "PR", changed
                ? new() { ["N"] = 0.85, ["L"] = 0.68, ["H"] = 0.5 }
                : new() { ["N"] = 0.85, ["L"] = 0.62, ["H"] = 0.27 }, out var privileges)
            || !TryGetWeight(metrics, "UI", new() { ["N"] = 0.85, ["R"] = 0.62 }, out var userInteraction)
            || !TryGetWeight(metrics, "C", ImpactWeights(), out var confidentiality)
            || !TryGetWeight(metrics, "I", ImpactWeights(), out var integrity)
            || !TryGetWeight(metrics, "A", ImpactWeights(), out var availability))
        {
            return false;
        }

        var impactSubScore = 1 - ((1 - confidentiality) * (1 - integrity) * (1 - availability));
        var impact = changed
            ? (7.52 * (impactSubScore - 0.029)) - (3.25 * Math.Pow(impactSubScore - 0.02, 15))
            : 6.42 * impactSubScore;
        var exploitability = 8.22 * attackVector * attackComplexity * privileges * userInteraction;

        if (impact <= 0)
        {
            score = 0;
        }
        else
        {
            score = changed
                ? RoundUp(Math.Min(1.08 * (impact + exploitability), 10))
                : RoundUp(Math.Min(impact + exploitability, 10));
        }

        return true;
    }

    /// <summary>
    /// The severity of a base score: the qualitative rating of the CVSS specification, with
    /// "None" (0.0) counted as low.
    /// </summary>
    public static VulnerabilitySeverity GetSeverity(double score)
    {
        return score switch
        {
            >= 9.0 => VulnerabilitySeverity.Critical,
            >= 7.0 => VulnerabilitySeverity.High,
            >= 4.0 => VulnerabilitySeverity.Moderate,
            _ => VulnerabilitySeverity.Low,
        };
    }

    private static Dictionary<string, double> ImpactWeights()
    {
        return new() { ["H"] = 0.56, ["L"] = 0.22, ["N"] = 0 };
    }

    private static bool TryGetWeight(Dictionary<string, string> metrics, string name, Dictionary<string, double> weights, out double weight)
    {
        weight = 0;
        return metrics.TryGetValue(name, out var value) && weights.TryGetValue(value, out weight);
    }

    // The Roundup function of CVSS v3.1 (Appendix A): the smallest number with one decimal that
    // is equal to or higher than its input, without floating point errors.
    private static double RoundUp(double value)
    {
        var integer = (long)Math.Round(value * 100000);
        return integer % 10000 == 0
            ? integer / 100000.0
            : (Math.Floor(integer / 10000.0) + 1) / 10.0;
    }
}
