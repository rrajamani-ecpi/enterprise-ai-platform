using EnterpriseAIPlatform.Application.Chat;
using EnterpriseAIPlatform.Application.Common;
using EnterpriseAIPlatform.Application.Identity;
using EnterpriseAIPlatform.Application.ModelAccess;
using EnterpriseAIPlatform.Domain.Chat;
using EnterpriseAIPlatform.Domain.Identity;
using EnterpriseAIPlatform.Domain.ModelAccess;
using EnterpriseAIPlatform.Infrastructure.Chat;
using NSubstitute;

namespace EnterpriseAIPlatform.UnitTests;

/// <summary>
/// Spec 004 US1 (FR-001–FR-006, SC-001/002/003) and US5 R1 subset (FR-020/021, SC-007). Every
/// dependency is faked so gate ordering and the "no store write before every gate passes"
/// guarantee are directly observable via NSubstitute call verification.
/// </summary>
public class ChatPipelineTests
{
    private const string PartitionKey = "hashed-owner";
    private const string ThreadId = "thread-1";
    private const string DefaultModelId = "azure-foundry:gpt-5";
    private const string FallbackModelId = "azure-foundry:fallback";

    private readonly IChatThreadStore _threadStore = Substitute.For<IChatThreadStore>();
    private readonly IChatMessageStore _messageStore = Substitute.For<IChatMessageStore>();
    private readonly IIdentityHasher _identityHasher = Substitute.For<IIdentityHasher>();
    private readonly IMessageLimitConfigService _messageLimitConfigService = Substitute.For<IMessageLimitConfigService>();
    private readonly IDailyMessageCounter _dailyMessageCounter = Substitute.For<IDailyMessageCounter>();
    private readonly IContentSafetyGuard _contentSafetyGuard = Substitute.For<IContentSafetyGuard>();
    private readonly IPiiRedactor _piiRedactor = Substitute.For<IPiiRedactor>();
    private readonly IModelAccessService _modelAccessService = Substitute.For<IModelAccessService>();
    private readonly IModelCatalogService _modelCatalogService = Substitute.For<IModelCatalogService>();
    private readonly ISystemModelConfigCache _systemModelConfigCache = Substitute.For<ISystemModelConfigCache>();
    private readonly IChatCompletionClient _chatCompletionClient = Substitute.For<IChatCompletionClient>();
    private readonly UserModel _caller = new() { Name = "Alice", Email = "alice@contoso.com" };

    public ChatPipelineTests()
    {
        _identityHasher.ForEmail(_caller.Email).Returns(new StoragePartitionKey(PartitionKey));
        _dailyMessageCounter.NextResetUtc().Returns(DateTimeOffset.UtcNow.AddHours(1));

        // Defaults for the "happy path" — individual tests override as needed.
        _threadStore.GetAsync(ThreadId, PartitionKey, Arg.Any<CancellationToken>())
            .Returns(new ChatThreadModel
            {
                Id = ThreadId, PartitionKey = PartitionKey, OwnerUserId = _caller.Email,
                Version = "v3", ModelId = DefaultModelId,
            });
        _messageLimitConfigService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ServerActionResponse<MessageLimitConfig>.Ok(new MessageLimitConfig()));
        _contentSafetyGuard.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyVerdict.Allowed());
        _piiRedactor.Redact(Arg.Any<string>()).Returns(call => new PiiRedactionResult(call.ArgAt<string>(0), 0));
        _modelAccessService.GetAvailableModelsAsync(Arg.Any<UserModel>(), Arg.Any<CancellationToken>())
            .Returns(ServerActionResponse<IReadOnlyList<ModelConfigDocument>>.Ok(new List<ModelConfigDocument> { Model(DefaultModelId) }));
        _modelCatalogService.GetAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => ServerActionResponse<ModelConfigDocument>.Ok(Model(call.ArgAt<string>(0))));
        _systemModelConfigCache.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new SystemModelConfig { FallbackModelId = FallbackModelId });
        _chatCompletionClient.Provider.Returns("azure-foundry");
        _chatCompletionClient.StreamCompletionAsync(Arg.Any<ChatRequest>(), Arg.Any<ModelConfigDocument>(), Arg.Any<CancellationToken>())
            .Returns(EmptyChunks());
    }

    private static ModelConfigDocument Model(string id) => new()
    {
        Id = id, DisplayName = "Test", Provider = "azure-foundry", IsEnabled = true,
        SupportsToolCalling = true, SupportsVision = true, SupportsReasoning = true,
        AccessTier = ModelAccessTier.Standard,
    };

    private static async IAsyncEnumerable<string> EmptyChunks()
    {
        await Task.CompletedTask;
        yield break;
    }

    private ChatPipeline BuildPipeline() => new(
        _threadStore, _messageStore, _identityHasher, _messageLimitConfigService, _dailyMessageCounter,
        _contentSafetyGuard, _piiRedactor, _modelAccessService, _modelCatalogService, _systemModelConfigCache,
        new[] { _chatCompletionClient });

    [Fact]
    public async Task NonV3Thread_IsRejected_WithNoFurtherGateOrStoreCallsMade()
    {
        _threadStore.GetAsync(ThreadId, PartitionKey, Arg.Any<CancellationToken>())
            .Returns(new ChatThreadModel
            {
                Id = ThreadId, PartitionKey = PartitionKey, OwnerUserId = _caller.Email, Version = "v2", ModelId = DefaultModelId,
            });

        var result = await BuildPipeline().SendMessageAsync(_caller, ThreadId, "hello", DefaultModelId);

        var rejected = Assert.IsType<ChatSendResult.Rejected>(result);
        Assert.Equal(PreflightRejectionCode.ThreadReadOnly, rejected.Code);
        await _messageLimitConfigService.DidNotReceive().GetAsync(Arg.Any<CancellationToken>());
        await _messageStore.DidNotReceive().AppendAsync(Arg.Any<ChatMessageModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingThread_IsRejected_AsThreadReadOnly()
    {
        _threadStore.GetAsync(ThreadId, PartitionKey, Arg.Any<CancellationToken>()).Returns((ChatThreadModel?)null);

        var result = await BuildPipeline().SendMessageAsync(_caller, ThreadId, "hello", DefaultModelId);

        Assert.IsType<ChatSendResult.Rejected>(result);
    }

    [Fact]
    public async Task PerMessageCap_Exceeded_IsRejected_WithNoStoreWrite()
    {
        _messageLimitConfigService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ServerActionResponse<MessageLimitConfig>.Ok(new MessageLimitConfig { PerMessageCharacterCap = 5 }));

        var result = await BuildPipeline().SendMessageAsync(_caller, ThreadId, "this is way more than five characters", DefaultModelId);

        var rejected = Assert.IsType<ChatSendResult.Rejected>(result);
        Assert.Equal(PreflightRejectionCode.MessageTooLong, rejected.Code);
        await _messageStore.DidNotReceive().AppendAsync(Arg.Any<ChatMessageModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DailyCap_Exceeded_IsRejected_WithResetsAt_AndNoStoreWrite()
    {
        _messageLimitConfigService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ServerActionResponse<MessageLimitConfig>.Ok(new MessageLimitConfig { DailyMessageCap = 10 }));
        _dailyMessageCounter.GetCountAsync(PartitionKey, Arg.Any<CancellationToken>()).Returns(10);

        var result = await BuildPipeline().SendMessageAsync(_caller, ThreadId, "hello", DefaultModelId);

        var rejected = Assert.IsType<ChatSendResult.Rejected>(result);
        Assert.Equal(PreflightRejectionCode.DailyLimitExceeded, rejected.Code);
        Assert.NotNull(rejected.ResetsAtUtc);
        await _messageStore.DidNotReceive().AppendAsync(Arg.Any<ChatMessageModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MessageLimitConfigUnreadable_FailsOpen_MessageIsAllowed()
    {
        _messageLimitConfigService.GetAsync(Arg.Any<CancellationToken>())
            .Returns<Task<ServerActionResponse<MessageLimitConfig>>>(_ => throw new InvalidOperationException("config store unreachable"));

        var result = await BuildPipeline().SendMessageAsync(_caller, ThreadId, "hello", DefaultModelId);

        Assert.IsType<ChatSendResult.Streaming>(result);
    }

    [Fact]
    public async Task DailyCounterUnreadable_FailsOpen_MessageIsAllowed()
    {
        _messageLimitConfigService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ServerActionResponse<MessageLimitConfig>.Ok(new MessageLimitConfig { DailyMessageCap = 10 }));
        _dailyMessageCounter.GetCountAsync(PartitionKey, Arg.Any<CancellationToken>()).Returns((int?)null);

        var result = await BuildPipeline().SendMessageAsync(_caller, ThreadId, "hello", DefaultModelId);

        Assert.IsType<ChatSendResult.Streaming>(result);
    }

    [Fact]
    public async Task ContentSafetyBlock_PreventsModelCall_AndStoreWrite()
    {
        _contentSafetyGuard.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyVerdict.Blocked("hate"));

        var result = await BuildPipeline().SendMessageAsync(_caller, ThreadId, "hello", DefaultModelId);

        var blocked = Assert.IsType<ChatSendResult.ContentBlocked>(result);
        Assert.Equal("hate", blocked.Category);
        _chatCompletionClient.DidNotReceive().StreamCompletionAsync(
            Arg.Any<ChatRequest>(), Arg.Any<ModelConfigDocument>(), Arg.Any<CancellationToken>());
        await _messageStore.DidNotReceive().AppendAsync(Arg.Any<ChatMessageModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestedModel_OutsideAllowList_IsSubstitutedWithFallback()
    {
        const string disallowedModel = "azure-foundry:not-allowed";

        var result = await BuildPipeline().SendMessageAsync(_caller, ThreadId, "hello", disallowedModel);

        Assert.IsType<ChatSendResult.Streaming>(result);
        await _modelCatalogService.Received().GetAsync(FallbackModelId, Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _modelCatalogService.DidNotReceive().GetAsync(disallowedModel, Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestedModel_WithinAllowList_IsUsedAsRequested()
    {
        var result = await BuildPipeline().SendMessageAsync(_caller, ThreadId, "hello", DefaultModelId);

        Assert.IsType<ChatSendResult.Streaming>(result);
        await _modelCatalogService.Received().GetAsync(DefaultModelId, Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SendMessageAsync_HasNoDataProductsParameter_ClientCanNeverSupplyOne()
    {
        // FR-006 (SC-003): the server-authoritative override is structural in R1 — the client
        // literally cannot pass a dataProducts value because the method signature has no such parameter.
        var parameters = typeof(IChatPipeline).GetMethod(nameof(IChatPipeline.SendMessageAsync))!.GetParameters();

        Assert.DoesNotContain(parameters, p => p.Name!.Contains("dataProduct", StringComparison.OrdinalIgnoreCase));
    }
}
