// Purpose: Selects the best safe round from check results.

namespace Company.ABC.Vesta.Core.Checks;

public static class RoundChooser
{
    public static int? ChooseBestSafeRound(IDictionary<int, CheckResult> roundResults)
    {
        var safeRounds = roundResults.Values
            .Where(r => r.Safe)
            .OrderBy(r => r.Problems.Count)
            .ThenByDescending(r => r.Round)
            .ToList();

        return safeRounds.FirstOrDefault()?.Round;
    }
}