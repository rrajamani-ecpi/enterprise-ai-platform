namespace EnterpriseAIPlatform.Domain.Chat;

public enum QuadrantEventKind
{
    Chunk,
    Error,
    Done,
}

/// <summary>
/// One tagged unit written to the parallel-send fan-in channel (spec 006 D4) and serialized as
/// one SSE event. A quadrant's <see cref="QuadrantEventKind.Error"/>/<see cref="QuadrantEventKind.Done"/>
/// event never blocks or cancels another quadrant's still-in-flight stream (FR-011).
/// </summary>
public sealed record QuadrantEvent(int Position, QuadrantEventKind Kind, string? Content = null)
{
    public static QuadrantEvent Chunk(int position, string content) => new(position, QuadrantEventKind.Chunk, content);

    public static QuadrantEvent Error(int position, string content) => new(position, QuadrantEventKind.Error, content);

    public static QuadrantEvent Done(int position) => new(position, QuadrantEventKind.Done);
}
