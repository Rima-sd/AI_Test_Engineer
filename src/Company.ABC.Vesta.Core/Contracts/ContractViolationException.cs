// Purpose: Represents a Vesta contract validation failure.
// The exception carries all validation problems found in a document.

namespace Company.ABC.Vesta.Core.Contracts;

/// <summary>
/// Thrown when a Vesta document does not satisfy its JSON Schema contract.
/// </summary>
public sealed class ContractViolationException : Exception
{
    /// <summary>
    /// Initializes the exception with all contract validation problems.
    /// </summary>
    /// <param name="contractKind">The contract that failed validation.</param>
    /// <param name="problems">All validation problems found.</param>
    public ContractViolationException(
        ContractKind contractKind,
        IEnumerable<string> problems)
        : base(CreateMessage(contractKind, problems, out var collectedProblems))
    {
        ContractKind = contractKind;
        Problems = collectedProblems;
    }

    /// <summary>
    /// Gets the contract kind that failed validation.
    /// </summary>
    public ContractKind ContractKind { get; }

    /// <summary>
    /// Gets all validation problems.
    /// </summary>
    public IReadOnlyList<string> Problems { get; }

    private static string CreateMessage(
        ContractKind contractKind,
        IEnumerable<string> problems,
        out IReadOnlyList<string> collectedProblems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        string[] items = problems.ToArray();

        collectedProblems = Array.AsReadOnly(items);

        return $"The {contractKind} contract validation failed: "
            + string.Join("; ", items);
    }
}