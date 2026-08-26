using Bunit;
using Bunit.TestDoubles;
using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Web.Components.Chat;
using EnterpriseAIPlatform.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests.Web;

/// <summary>Spec 024 US3: sidebar empty state, listing, inline rename, and the not-found block.</summary>
public class ConversationListComponentTests : BunitContext
{
    private const string PartitionKey = "hashed-owner";

    private readonly ICurrentUserAccessor _currentUserAccessor = Substitute.For<ICurrentUserAccessor>();
    private readonly IIdentityHasher _identityHasher = Substitute.For<IIdentityHasher>();
    private readonly IChatThreadStore _threadStore = Substitute.For<IChatThreadStore>();
    private readonly UserModel _caller = new() { Name = "Alice", Email = "alice@contoso.com" };

    public ConversationListComponentTests()
    {
        _currentUserAccessor.GetCurrentUser().Returns(ServerActionResponse<UserModel>.Ok(_caller));
        _identityHasher.ForEmail(_caller.Email).Returns(new StoragePartitionKey(PartitionKey));
    }

    private static ChatThreadModel Thread(string id, string displayName) => new()
    {
        Id = id, PartitionKey = PartitionKey, OwnerUserId = "alice@contoso.com", ModelId = "azure-foundry:gpt-5",
        DisplayName = displayName,
    };

    private void RegisterStates(ChatComposerState composerState, ConversationListState listState)
    {
        Services.AddSingleton(composerState);
        Services.AddSingleton(listState);
    }

    private ChatComposerState CreateComposerState() => new(
        _currentUserAccessor,
        _identityHasher,
        _threadStore,
        Substitute.For<IChatMessageStore>(),
        Substitute.For<IChatPipeline>(),
        Substitute.For<IModelAccessService>());

    [Fact]
    public void ConversationList_NoConversations_RendersEmptyState()
    {
        _threadStore.ListByOwnerAsync(PartitionKey, Arg.Any<CancellationToken>())
            .Returns(new List<ChatThreadModel>());
        RegisterStates(CreateComposerState(), new ConversationListState(_currentUserAccessor, _identityHasher, _threadStore));

        var cut = Render<ConversationList>(builder =>
        {
            builder.OpenComponent<ConversationList>(0);
            builder.CloseComponent();
        });

        Assert.Contains("conversation-list-empty", cut.Markup);
    }

    [Fact]
    public void ConversationList_WithConversations_RendersEachName()
    {
        _threadStore.ListByOwnerAsync(PartitionKey, Arg.Any<CancellationToken>())
            .Returns(new List<ChatThreadModel> { Thread("t1", "Trip planning"), Thread("t2", "Recipe ideas") });
        var listState = new ConversationListState(_currentUserAccessor, _identityHasher, _threadStore);
        RegisterStates(CreateComposerState(), listState);

        var cut = Render<ConversationList>(builder =>
        {
            builder.OpenComponent<ConversationList>(0);
            builder.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("Trip planning"));

        Assert.Contains("Trip planning", cut.Markup);
        Assert.Contains("Recipe ideas", cut.Markup);
    }

    [Fact]
    public void ConversationList_SelectingAConversation_NavigatesToItsRoute()
    {
        _threadStore.ListByOwnerAsync(PartitionKey, Arg.Any<CancellationToken>())
            .Returns(new List<ChatThreadModel> { Thread("t1", "Trip planning") });
        RegisterStates(CreateComposerState(), new ConversationListState(_currentUserAccessor, _identityHasher, _threadStore));

        var cut = Render<ConversationList>(builder =>
        {
            builder.OpenComponent<ConversationList>(0);
            builder.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("Trip planning"));

        cut.Find("button.conversation-list-item-name").Click();

        var navigationManager = Services.GetRequiredService<BunitNavigationManager>();
        Assert.EndsWith("/chat/t1", navigationManager.Uri);
    }

    [Fact]
    public async Task ConversationList_InlineRename_RoundTrip_UpdatesDisplayName()
    {
        _threadStore.ListByOwnerAsync(PartitionKey, Arg.Any<CancellationToken>())
            .Returns(new List<ChatThreadModel> { Thread("t1", "Untitled") });
        _threadStore.RenameAsync("t1", PartitionKey, "Trip planning", Arg.Any<CancellationToken>())
            .Returns(ServerActionResponse<ChatThreadModel>.Ok(Thread("t1", "Trip planning")));
        RegisterStates(CreateComposerState(), new ConversationListState(_currentUserAccessor, _identityHasher, _threadStore));

        var cut = Render<ConversationList>(builder =>
        {
            builder.OpenComponent<ConversationList>(0);
            builder.CloseComponent();
        });
        cut.WaitForState(() => cut.Markup.Contains("Untitled"));

        cut.Find("button.conversation-list-item-rename").Click();
        var input = cut.Find("input.conversation-list-rename-input");
        await input.InputAsync("Trip planning");
        await input.KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        cut.WaitForState(() => cut.Markup.Contains("Trip planning") && !cut.Markup.Contains("Untitled"));
        Assert.Contains("Trip planning", cut.Markup);
    }

    [Fact]
    public void ChatShell_ForeignOrNonexistentThread_RendersNotFoundBlock()
    {
        _threadStore.ListByOwnerAsync(PartitionKey, Arg.Any<CancellationToken>())
            .Returns(new List<ChatThreadModel>());
        _threadStore.GetAsync("does-not-exist", PartitionKey, Arg.Any<CancellationToken>())
            .Returns((ChatThreadModel?)null);
        RegisterStates(CreateComposerState(), new ConversationListState(_currentUserAccessor, _identityHasher, _threadStore));

        var cut = Render<ChatShell>(parameters => parameters.Add(p => p.ThreadId, "does-not-exist"));
        cut.WaitForState(() => cut.Markup.Contains("chat-shell-not-found"));

        Assert.Contains("could not be found", cut.Markup);
    }
}
