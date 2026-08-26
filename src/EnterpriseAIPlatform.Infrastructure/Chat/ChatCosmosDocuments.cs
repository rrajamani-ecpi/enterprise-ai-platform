using EnterpriseAIPlatform.Domain.Chat;
using Newtonsoft.Json;

namespace EnterpriseAIPlatform.Infrastructure.Chat;

/// <summary>
/// Cosmos-serializable projections of the Domain chat models, discriminated by <see cref="DocType"/>
/// so both document shapes can share one container (spec 004 D1) without leaking a persistence
/// concern into the Domain layer.
/// </summary>
internal sealed class ChatThreadDocument
{
    public const string DocType = "thread";

    [JsonProperty("id")]
    public required string Id { get; set; }

    public required string PartitionKey { get; set; }

    public required string OwnerUserId { get; set; }

    public string Version { get; set; } = "v3";

    public required string ModelId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Spec 024 US3 — creation-timestamp-based default, renamable thereafter.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Spec 024 US3 — drives the conversation list's most-recently-active-first order.</summary>
    public DateTimeOffset LastActivityAtUtc { get; set; }

    public List<string> DataProducts { get; set; } = new();

    /// <summary>Spec 006 D5 — null for an ordinary single-chat thread.</summary>
    public string? MultiChatSessionId { get; set; }

    public int? MultiChatPosition { get; set; }

    public string Type => DocType;

    public static ChatThreadDocument FromModel(ChatThreadModel model) => new()
    {
        Id = model.Id,
        PartitionKey = model.PartitionKey,
        OwnerUserId = model.OwnerUserId,
        Version = model.Version,
        ModelId = model.ModelId,
        CreatedAtUtc = model.CreatedAtUtc,
        DisplayName = model.DisplayName,
        LastActivityAtUtc = model.LastActivityAtUtc,
        DataProducts = model.DataProducts,
        MultiChatSessionId = model.MultiChatSessionId,
        MultiChatPosition = model.MultiChatPosition,
    };

    public ChatThreadModel ToModel() => new()
    {
        Id = Id,
        PartitionKey = PartitionKey,
        OwnerUserId = OwnerUserId,
        Version = Version,
        ModelId = ModelId,
        CreatedAtUtc = CreatedAtUtc,
        DisplayName = DisplayName,
        LastActivityAtUtc = LastActivityAtUtc,
        DataProducts = DataProducts,
        MultiChatSessionId = MultiChatSessionId,
        MultiChatPosition = MultiChatPosition,
    };
}

internal sealed class ChatMessageDocument
{
    public const string DocType = "message";

    [JsonProperty("id")]
    public required string Id { get; set; }

    public required string PartitionKey { get; set; }

    public required string ThreadId { get; set; }

    public ChatMessageRole Role { get; set; }

    public required string Content { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string Type => DocType;

    public static ChatMessageDocument FromModel(ChatMessageModel model) => new()
    {
        Id = model.Id,
        PartitionKey = model.PartitionKey,
        ThreadId = model.ThreadId,
        Role = model.Role,
        Content = model.Content,
        CreatedAtUtc = model.CreatedAtUtc,
    };

    public ChatMessageModel ToModel() => new()
    {
        Id = Id,
        PartitionKey = PartitionKey,
        ThreadId = ThreadId,
        Role = Role,
        Content = Content,
        CreatedAtUtc = CreatedAtUtc,
    };
}

/// <summary>Spec 006 D1 — the third document type sharing spec 004's <c>chat</c> container.</summary>
internal sealed class MultiChatSessionDocument
{
    public const string DocType = "multichat-session";

    [JsonProperty("id")]
    public required string Id { get; set; }

    public required string PartitionKey { get; set; }

    public required string OwnerUserId { get; set; }

    public List<MultiChatQuadrantDocument> Quadrants { get; set; } = new();

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string Type => DocType;

    public static MultiChatSessionDocument FromModel(MultiChatSession model) => new()
    {
        Id = model.Id,
        PartitionKey = model.PartitionKey,
        OwnerUserId = model.OwnerUserId,
        Quadrants = model.Quadrants.Select(MultiChatQuadrantDocument.FromModel).ToList(),
        UpdatedAtUtc = model.UpdatedAtUtc,
    };

    public MultiChatSession ToModel() => new()
    {
        Id = Id,
        PartitionKey = PartitionKey,
        OwnerUserId = OwnerUserId,
        Quadrants = Quadrants.Select(q => q.ToModel()).ToList(),
        UpdatedAtUtc = UpdatedAtUtc,
    };
}

internal sealed class MultiChatQuadrantDocument
{
    public required int Position { get; set; }

    public string? PersonaId { get; set; }

    public string? ModelId { get; set; }

    public string? ThreadId { get; set; }

    public static MultiChatQuadrantDocument FromModel(MultiChatQuadrant model) => new()
    {
        Position = model.Position,
        PersonaId = model.PersonaId,
        ModelId = model.ModelId,
        ThreadId = model.ThreadId,
    };

    public MultiChatQuadrant ToModel() => new()
    {
        Position = Position,
        PersonaId = PersonaId,
        ModelId = ModelId,
        ThreadId = ThreadId,
    };
}
