using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Domain.Prompts;
using EnterpriseAIPlatform.Web.Services;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests.Chat;

/// <summary>
/// Spec 016 SC-008 / FR-012 — seeding the composer from a prompt copies the prompt text verbatim.
/// The prompt entity has no variable schema, so any substitution step would have to guess at
/// delimiters; these tests pin that no such step exists, including for text that merely looks like
/// it contains placeholders.
/// </summary>
public class ChatComposerStateSeedTests
{
    private readonly ICurrentUserAccessor _currentUserAccessor = Substitute.For<ICurrentUserAccessor>();
    private readonly IIdentityHasher _identityHasher = Substitute.For<IIdentityHasher>();
    private readonly IChatThreadStore _threadStore = Substitute.For<IChatThreadStore>();
    private readonly IChatMessageStore _messageStore = Substitute.For<IChatMessageStore>();
    private readonly IChatPipeline _chatPipeline = Substitute.For<IChatPipeline>();
    private readonly IModelAccessService _modelAccessService = Substitute.For<IModelAccessService>();
    private readonly UserModel _caller = new() { Name = "Alice", Email = "alice@contoso.com" };

    public ChatComposerStateSeedTests()
    {
        _currentUserAccessor.GetCurrentUser().Returns(ServerActionResponse<UserModel>.Ok(_caller));
        _identityHasher.ForEmail(_caller.Email).Returns(new StoragePartitionKey("hashed-owner"));
    }

    private ChatComposerState CreateState() =>
        new(_currentUserAccessor, _identityHasher, _threadStore, _messageStore, _chatPipeline, _modelAccessService);

    private static PromptModel Prompt(string description) => new()
    {
        Id = "p1",
        OwnerUserId = "alice@contoso.com",
        OwnerPartitionKey = "hashed-owner",
        Name = "Test prompt",
        Description = description,
    };

    [Theory]
    [InlineData("Summarize the following text.")]
    [InlineData("Summarize [the following] text.")]
    [InlineData("Rewrite {this} in a formal tone.")]
    [InlineData("Use {{double braces}} and [[double brackets]].")]
    [InlineData("Line one\nLine two\r\nLine three")]
    [InlineData("Mixed [brackets] and {braces}\nacross\nlines")]
    [InlineData("Costs $100 - 50% off; email a@b.com <tag> \"quoted\"")]
    [InlineData("   leading and trailing whitespace   ")]
    public void SeedFromPrompt_CopiesDescriptionVerbatim(string description)
    {
        var state = CreateState();

        state.SeedFromPrompt(Prompt(description));

        Assert.Equal(description, state.ComposerText);
    }

    [Fact]
    public void SeedFromPrompt_RaisesOnChanged_SoASubscribedComposerReRenders()
    {
        var state = CreateState();
        var notifications = 0;
        state.OnChanged += () => notifications++;

        state.SeedFromPrompt(Prompt("Summarize this."));

        Assert.Equal(1, notifications);
    }

    [Fact]
    public void SeedFromPrompt_ReplacesExistingComposerText()
    {
        var state = CreateState();
        state.ComposerText = "half-typed message";

        state.SeedFromPrompt(Prompt("Summarize this."));

        Assert.Equal("Summarize this.", state.ComposerText);
    }

    [Fact]
    public void SeedFromPrompt_DoesNotSend()
    {
        // FR-012: the user gets an editable composer, not a dispatched message. This also confirms
        // no second send path was introduced (Principle IV) — seeding touches neither the chat
        // pipeline nor the message store.
        var state = CreateState();

        state.SeedFromPrompt(Prompt("Summarize this."));

        Assert.Empty(state.Messages);
        Assert.False(state.IsStreaming);
        Assert.Empty(_chatPipeline.ReceivedCalls());
        Assert.Empty(_messageStore.ReceivedCalls());
    }

    [Fact]
    public void SeedFromPrompt_IsRepeatable()
    {
        var state = CreateState();

        state.SeedFromPrompt(Prompt("First."));
        state.SeedFromPrompt(Prompt("Second."));

        Assert.Equal("Second.", state.ComposerText);
    }
}
