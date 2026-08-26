using Bunit;
using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Web.Components.Chat;
using EnterpriseAIPlatform.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests.Web;

/// <summary>Spec 024 US1 (real screen, not placeholder) and US2 (send-disabled-while-streaming, interrupted marker).</summary>
public class ChatComposerComponentTests : BunitContext
{
    private static ChatComposerState CreateState(UserModel? currentUser)
    {
        var currentUserAccessor = Substitute.For<ICurrentUserAccessor>();
        currentUserAccessor.GetCurrentUser().Returns(
            currentUser is null
                ? ServerActionResponse<UserModel>.Unauthorized()
                : ServerActionResponse<UserModel>.Ok(currentUser));

        return new ChatComposerState(
            currentUserAccessor,
            Substitute.For<IIdentityHasher>(),
            Substitute.For<IChatThreadStore>(),
            Substitute.For<IChatMessageStore>(),
            Substitute.For<IChatPipeline>(),
            Substitute.For<IModelAccessService>());
    }

    [Fact]
    public void ChatHome_AuthenticatedUser_ShowsIdentity_AndEmptyComposer_NotPlaceholder()
    {
        var currentUserAccessor = Substitute.For<ICurrentUserAccessor>();
        currentUserAccessor.GetCurrentUser().Returns(
            ServerActionResponse<UserModel>.Ok(new UserModel { Name = "Alice", Email = "alice@contoso.com" }));
        var state = CreateState(new UserModel { Name = "Alice", Email = "alice@contoso.com" });
        Services.AddSingleton(state);
        // ChatHome refreshes the sidebar after a send (spec 024 US3) — needs a ConversationListState too.
        Services.AddSingleton(new ConversationListState(
            currentUserAccessor, Substitute.For<IIdentityHasher>(), Substitute.For<IChatThreadStore>()));

        // bUnit 2.9.0's Render<T>(Action<ComponentParameterCollectionBuilder<T>>) does not mount
        // the component when the builder adds zero parameters (ChatHome takes none) — the
        // RenderFragment-based overload with an explicit OpenComponent/CloseComponent pair is the
        // reliable way to render a no-parameter component directly in this version.
        var cut = Render(builder =>
        {
            builder.OpenComponent<ChatHome>(0);
            builder.CloseComponent();
        });

        Assert.Contains("Alice", cut.Markup);
        Assert.Contains("alice@contoso.com", cut.Markup);
        Assert.DoesNotContain("Hello, world!", cut.Markup);
        Assert.False(cut.Find("textarea.chat-composer-input").HasAttribute("disabled"));
    }

    [Fact]
    public void ChatComposer_WhileStreaming_DisablesInputAndSendButton()
    {
        var cut = Render<ChatComposer>(parameters => parameters
            .Add(p => p.ComposerText, "hello")
            .Add(p => p.IsStreaming, true)
            .Add(p => p.OnSend, EventCallback.Factory.Create(this, () => Task.CompletedTask)));

        Assert.True(cut.Find("textarea").HasAttribute("disabled"));
        Assert.True(cut.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void ChatComposer_NotStreaming_WithText_EnablesSendButton()
    {
        var cut = Render<ChatComposer>(parameters => parameters
            .Add(p => p.ComposerText, "hello")
            .Add(p => p.IsStreaming, false)
            .Add(p => p.OnSend, EventCallback.Factory.Create(this, () => Task.CompletedTask)));

        Assert.False(cut.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void ChatTranscript_InterruptedMessage_RendersDistinctMarker()
    {
        var messages = new List<ChatMessageViewState>
        {
            new() { Role = "assistant", Content = "partial reply", IsComplete = true, IsInterrupted = true },
        };

        var cut = Render<ChatTranscript>(parameters => parameters
            .Add(p => p.Messages, messages));

        Assert.Contains("chat-message-interrupted", cut.Markup);
        Assert.Contains("Interrupted", cut.Markup);
    }

    [Fact]
    public void ChatTranscript_InProgressMessage_RendersStreamingMarker_NotInterrupted()
    {
        var messages = new List<ChatMessageViewState>
        {
            new() { Role = "assistant", Content = "partial", IsComplete = false },
        };

        var cut = Render<ChatTranscript>(parameters => parameters
            .Add(p => p.Messages, messages));

        Assert.Contains("chat-message-streaming", cut.Markup);
        Assert.DoesNotContain("chat-message-interrupted", cut.Markup);
    }
}
