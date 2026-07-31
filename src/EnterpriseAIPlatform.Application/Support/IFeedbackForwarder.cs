namespace EnterpriseAIPlatform.Application.Support;

/// <summary>
/// Forwards feedback to the external ECPI API (spec 017 FR-009/010). The single implementation
/// never throws to its caller — it catches and logs internally; the boolean return is for
/// telemetry only, never used to decide what to tell the end user (who always sees success once
/// thread ownership has already passed, per FR-010).
/// </summary>
public interface IFeedbackForwarder
{
    Task<bool> ForwardAsync(string threadId, string content, CancellationToken cancellationToken = default);
}
