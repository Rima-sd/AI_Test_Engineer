// Purpose: Identifies the Vesta document contract used during JSON validation.
// Contract kinds map to the corresponding embedded JSON Schema resources.

namespace Company.ABC.Vesta.Core.Contracts;

/// <summary>
/// Identifies the JSON contract used to validate a Vesta document.
/// </summary>
public enum ContractKind
{
    Job,
    StepMessage,
    AuditEntry,
    AgentRequest,
    AgentReply
}