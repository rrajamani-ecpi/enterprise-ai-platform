namespace EnterpriseAIPlatform.Domain.Personas;

/// <summary>The write operation being attempted, for <c>PersonaAccessEvaluator.CanWrite</c> (spec 009 FR-005).</summary>
public enum PersonaOperation
{
    Read,
    Edit,
    Delete,
}
