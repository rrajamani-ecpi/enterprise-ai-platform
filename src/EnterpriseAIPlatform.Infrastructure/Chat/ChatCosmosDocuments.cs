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

    public List<string> DataProducts { get; set; } = new();

    public string Type => DocType;

    public static ChatThreadDocument FromModel(ChatThreadModel model) => new()
    {
        Id = model.Id,
        PartitionKey = model.PartitionKey,
        OwnerUserId = model.OwnerUserId,
        Version = model.Version,
        ModelId = model.ModelId,
        CreatedAtUtc = model.CreatedAtUtc,
        DataProducts = model.DataProducts,
    };

    public ChatThreadModel ToModel() => new()
    {
        Id = Id,
        PartitionKey = PartitionKey,
        OwnerUserId = OwnerUserId,
        Version = Version,
        ModelId = ModelId,
        CreatedAtUtc = CreatedAtUtc,
        DataProducts = DataProducts,
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
