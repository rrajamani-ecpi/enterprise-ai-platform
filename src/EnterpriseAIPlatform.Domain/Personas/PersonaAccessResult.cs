namespace EnterpriseAIPlatform.Domain.Personas;

/// <summary>The outcome of <c>PersonaAccessEvaluator.Evaluate</c> (spec 009 FR-003).</summary>
public enum PersonaAccessResult
{
    FullAccess,
    ReadOnly,
    Denied,
}
