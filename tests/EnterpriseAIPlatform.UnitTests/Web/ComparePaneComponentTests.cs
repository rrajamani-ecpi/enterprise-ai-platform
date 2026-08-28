using Bunit;
using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Domain.ModelAccess;
using EnterpriseAIPlatform.Infrastructure.Chat;
using EnterpriseAIPlatform.Web.Components.Chat;
using EnterpriseAIPlatform.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests.Web;

/// <summary>Spec 024 US4: the "no model assigned" indicator, per-pane error isolation, and the reused ChatTranscript rendering a pane's own messages.</summary>
public class ComparePaneComponentTests : BunitContext
{
    private const string PartitionKey = "hashed-owner";

    private readonly ICurrentUserAccessor _currentUserAccessor = Substitute.For<ICurrentUserAccessor>();
    private readonly IIdentityHasher _identityHasher = Substitute.For<IIdentityHasher>();
    private readonly IMultiChatSessionStore _sessionStore = Substitute.For<IMultiChatSessionStore>();
    private readonly IChatThreadStore _threadStore = Substitute.For<IChatThreadStore>();
    private readonly IChatPipeline _chatPipeline = Substitute.For<IChatPipeline>();
    private readonly IModelAccessService _modelAccessService = Substitute.For<IModelAccessService>();
    private readonly IChatMessageStore _messageStore = Substitute.For<IChatMessageStore>();
    private readonly UserModel _caller = new() { Name = "Alice", Email = "alice@contoso.com" };

    public ComparePaneComponentTests()
    {
        _currentUserAccessor.GetCurrentUser().Returns(ServerActionResponse<UserModel>.Ok(_caller));
        _identityHasher.ForEmail(_caller.Email).Returns(new StoragePartitionKey(PartitionKey));
        _modelAccessService.GetAvailableModelsAsync(_caller, Arg.Any<CancellationToken>())
            .Returns(ServerActionResponse<IReadOnlyList<ModelConfigDocument>>.Ok(new List<ModelConfigDocument>()));
    }

    private CompareSessionState CreateState() => new(
        _currentUserAccessor,
        _identityHasher,
        _sessionStore,
        new MultiChatDispatcher(_sessionStore, _threadStore, _chatPipeline),
        _modelAccessService,
        _messageStore);

    [Fact]
    public void ComparePane_NoModelAssigned_RendersNothingToSendToIndicator()
    {
        Services.AddSingleton(CreateState());
        var pane = new ComparePaneViewState { Position = 0 };

        var cut = Render<ComparePane>(parameters => parameters.Add(p => p.Pane, pane));

        Assert.Contains("compare-pane-empty", cut.Markup);
    }

    [Fact]
    public void ComparePane_WithModelAssigned_RendersTranscript_NotTheEmptyIndicator()
    {
        Services.AddSingleton(CreateState());
        var pane = new ComparePaneViewState { Position = 0, ModelId = "azure-foundry:gpt-5" };
        pane.Messages.Add(new ChatMessageViewState { Role = "assistant", Content = "hello there", IsComplete = true });

        var cut = Render<ComparePane>(parameters => parameters.Add(p => p.Pane, pane));

        Assert.DoesNotContain("compare-pane-empty", cut.Markup);
        Assert.Contains("hello there", cut.Markup);
    }

    [Fact]
    public void ComparePane_WithError_RendersErrorMessage_AlongsideItsOwnMessages()
    {
        Services.AddSingleton(CreateState());
        var pane = new ComparePaneViewState { Position = 0, ModelId = "azure-foundry:gpt-5", ErrorMessage = "SEND_FAILED" };
        pane.Messages.Add(new ChatMessageViewState { Role = "user", Content = "hi", IsComplete = true });

        var cut = Render<ComparePane>(parameters => parameters.Add(p => p.Pane, pane));

        Assert.Contains("SEND_FAILED", cut.Markup);
        Assert.Contains("hi", cut.Markup);
    }
}
