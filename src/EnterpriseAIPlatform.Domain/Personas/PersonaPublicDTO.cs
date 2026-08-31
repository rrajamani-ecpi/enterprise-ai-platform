namespace EnterpriseAIPlatform.Domain.Personas;

/// <summary>
/// The only shape returned to any Web-layer/Blazor-component-reachable code path (research.md D5).
/// Has no <c>ApiKey</c> property at all — absent by construction, not merely omitted by convention
/// (FR-009). Blazor Interactive Server never round-trips through JSON, so a
/// <c>[JsonIgnore]</c>-based strip on <see cref="PersonaModel"/> would not protect this boundary;
/// only a genuinely separate type does.
/// </summary>
public sealed record PersonaPublicDTO
{
    public required string Id { get; init; }

    public required string OwnerUserId { get; init; }

    public required string Model { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string PersonaMessage { get; init; } = string.Empty;

    public List<string> Extensions { get; init; } = new();

    public List<string> DataProducts { get; init; } = new();

    public List<string> CollaboratorPartitionKeys { get; init; } = new();

    public List<PersonaShareTarget> SharedWith { get; init; } = new();

    public bool A2aEnabled { get; init; }

    public bool IsLessonPersona { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public static PersonaPublicDTO FromModel(PersonaModel model) => new()
    {
        Id = model.Id,
        OwnerUserId = model.OwnerUserId,
        Model = model.Model,
        Name = model.Name,
        Description = model.Description,
        PersonaMessage = model.PersonaMessage,
        Extensions = model.Extensions,
        DataProducts = model.DataProducts,
        CollaboratorPartitionKeys = model.CollaboratorPartitionKeys,
        SharedWith = model.SharedWith,
        A2aEnabled = model.A2aEnabled,
        IsLessonPersona = model.IsLessonPersona,
        CreatedAtUtc = model.CreatedAtUtc,
        UpdatedAtUtc = model.UpdatedAtUtc,
    };
}
