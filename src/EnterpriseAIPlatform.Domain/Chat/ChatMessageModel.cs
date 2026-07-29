namespace EnterpriseAIPlatform.Domain.Chat;

public enum ChatMessageRole
{
    User,
    Assistant,
}

/// <summary>
/// A single chat message (spec 004 Key Entities) — a Cosmos document separate from
/// <see cref="ChatThreadModel"/>. <see cref="Content"/> is always the original, unredacted
/// user-authored text; PII redaction (FR-008) applies only to the model-bound copy, never here.
/// </summary>
public sealed class ChatMessageModel
{
    public required string Id { get; set; }

    /// <summary>Same hashed owner identity as the parent thread's <c>PartitionKey</c>.</summary>
    public required string PartitionKey { get; set; }

    public required string ThreadId { get; set; }

    public ChatMessageRole Role { get; set; }

    public required string Content { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
