using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using ChatMessage = JustyBase.Common.Models.ChatMessage;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services.Chat;
using JustyBase.Services.Documents;
using JustyBase.ViewModels.Tools;
using Microsoft.Extensions.AI;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JustyBase.Services;

public interface ICopilotChatService
{
    bool IsConnected { get; }
    string? ConnectionError { get; }
    Task<bool> InitializeAsync();
    IAsyncEnumerable<string> SendMessageAsync(List<ChatMessage> messages, string? modelId = null, string? reasoningEffort = null, CancellationToken cancellationToken = default);
    Task<List<string>> GetAvailableModelsAsync();
    Task<List<string>> GetAvailableReasoningEffortsAsync(string? modelId = null);
    IReadOnlyList<(string Id, string DisplayName)> AvailableBackends { get; }
    string? ActiveBackendId { get; }
    Task<bool> SwitchBackendAsync(string backendId);
    void SetCurrentSqlProvider(Func<string?> currentSqlProvider);
    void SetActiveSqlContextProvider(Func<(string ConnectionName, string DatabaseName)?> activeSqlContextProvider);
    void SetSqlEditorContextProvider(Func<(string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)?> sqlEditorContextProvider);
    void SetSqlEditorBufferUpdater(Func<string, bool> sqlEditorBufferUpdater);
    void SetToolConfirmationHandler(Func<string, string, Task<bool>> handler);
    void SetMode(ChatMode mode);
    ChatMode GetCurrentMode();
    bool IsCodexAuthenticated { get; }
    CodexAccountInfo? CodexAccount { get; }
    Task<CodexAccountInfo?> ReadCodexAccountAsync(CancellationToken cancellationToken = default);
    Task<bool> StartCodexLoginAsync(CancellationToken cancellationToken = default);
    Task<bool> LogoutCodexAsync(CancellationToken cancellationToken = default);
    Task CancelCurrentRequestAsync();
    void SetCodexThreadId(string? threadId);
    string? GetCodexThreadId();
}

public sealed class LocalChatService : ICopilotChatService, IAsyncDisposable
{
    private readonly ISimpleLogger _logger;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly LocalToolExecutor _toolExecutor;
    private readonly LocalChatClientFactory _clientFactory;
    private readonly ILocalStateProvider _stateProvider;
    private readonly ILocalModelConfigurationService _modelConfiguration;
    private readonly ISystemPromptBuilder _promptBuilder;
    private readonly CodexAppServerClient _codexClient;

    private bool _isConnected;
    public bool IsConnected => _isConnected;
    public string? ConnectionError { get; private set; }

    private ILocalChatBackend? _activeBackend;
    private string? _activeBackendId;
    private ChatMode _currentMode = ChatMode.Expert;

    private Func<string, string, Task<bool>>? _toolConfirmationHandler;
    private int? _lastSqlHash;

    public LocalChatService(
        ISimpleLogger logger,
        IMessageForUserTools messageForUserTools,
        IGeneralApplicationData generalApplicationData,
        IDatabaseServiceResolver databaseServiceResolver,
        SqlDiagnosticsViewModel diagnosticsViewModel,
        LocalChatClientFactory clientFactory,
        ILocalStateProvider stateProvider,
        ILocalModelConfigurationService modelConfiguration,
        CodexAppServerClient codexClient,
        SqlExecutionErrorStore sqlExecutionErrorStore)
    {
        _logger = logger;
        _messageForUserTools = messageForUserTools;
        _generalApplicationData = generalApplicationData;
        _clientFactory = clientFactory;
        _stateProvider = stateProvider;
        _modelConfiguration = modelConfiguration;
        _codexClient = codexClient;
        _promptBuilder = new SystemPromptBuilder();
        _toolExecutor = new LocalToolExecutor(logger, generalApplicationData, databaseServiceResolver, diagnosticsViewModel, sqlExecutionErrorStore);
        _codexClient.SetToolHandler(ExecuteCodexToolAsync, ConfirmCodexToolAsync);
    }

    public void SetMode(ChatMode mode) => _currentMode = mode;
    public ChatMode GetCurrentMode() => _currentMode;

    public void SetCurrentSqlProvider(Func<string?> provider) => _toolExecutor.SetCurrentSqlProvider(provider);
    public void SetActiveSqlContextProvider(Func<(string ConnectionName, string DatabaseName)?> provider)
    {
        _stateProvider.SetActiveSqlContextProvider(provider);
        _toolExecutor.SetActiveSqlContextProvider(provider);
    }
    public void SetSqlEditorContextProvider(Func<(string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)?> provider)
    {
        _stateProvider.SetSqlEditorContextProvider(provider);
        _toolExecutor.SetSqlEditorContextProvider(provider);
    }
    public void SetSqlEditorBufferUpdater(Func<string, bool> updater) => _toolExecutor.SetSqlEditorBufferUpdater(updater);
    public void SetToolConfirmationHandler(Func<string, string, Task<bool>> handler) => _toolConfirmationHandler = handler;

    public IReadOnlyList<(string Id, string DisplayName)> AvailableBackends =>
        new[] { ("codex", "Codex (ChatGPT)") }
            .Concat(_clientFactory.Backends.Select(b => (b.Id, b.DisplayName)))
            .ToList();

    public string? ActiveBackendId => _activeBackendId ?? _activeBackend?.Id;

    public bool IsCodexAuthenticated => _codexClient.Account?.IsAuthenticated == true;
    public CodexAccountInfo? CodexAccount => _codexClient.Account;

    public async Task<CodexAccountInfo?> ReadCodexAccountAsync(CancellationToken cancellationToken = default)
    {
        var account = await _codexClient.ReadAccountAsync(cancellationToken).ConfigureAwait(false);
        if (account is null && !string.IsNullOrWhiteSpace(_codexClient.LastError))
            ConnectionError = _codexClient.LastError;
        return account;
    }

    public async Task<bool> StartCodexLoginAsync(CancellationToken cancellationToken = default)
    {
        var started = await _codexClient.StartChatGptLoginAsync(cancellationToken).ConfigureAwait(false);
        ConnectionError = started ? null : _codexClient.LastError ?? "Codex app-server is unavailable.";
        return started;
    }

    public Task<bool> LogoutCodexAsync(CancellationToken cancellationToken = default)
        => _codexClient.LogoutAsync(cancellationToken);

    public Task CancelCurrentRequestAsync()
        => string.Equals(_activeBackendId, "codex", StringComparison.OrdinalIgnoreCase)
            ? _codexClient.InterruptCurrentTurnAsync()
            : Task.CompletedTask;

    public void SetCodexThreadId(string? threadId) => _codexClient.SetThreadId(threadId);
    public string? GetCodexThreadId() => _codexClient.ThreadId;

    public async Task<bool> SwitchBackendAsync(string backendId)
    {
        var wasConnected = _isConnected;

        if (backendId.Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            var initialized = await _codexClient.InitializeAsync().ConfigureAwait(false);
            if (!initialized)
            {
                ConnectionError = _codexClient.LastError ?? "Codex app-server is unavailable.";
                _isConnected = wasConnected;
                return false;
            }

            await _codexClient.ReadAccountAsync().ConfigureAwait(false);
            if (!IsCodexAuthenticated)
            {
                ConnectionError = "Sign in with ChatGPT to use Codex.";
                _isConnected = wasConnected;
                return false;
            }

            _activeBackend = null;
            _activeBackendId = "codex";
            _isConnected = true;
            ConnectionError = null;
            return true;
        }

        var backend = _clientFactory.GetBackend(backendId);
        if (backend is null)
        {
            ConnectionError = $"Backend '{backendId}' not found";
            _isConnected = wasConnected;
            return false;
        }

        try
        {
            if (await backend.PingAsync())
            {
                _activeBackend = backend;
                _activeBackendId = backend.Id;
                _isConnected = true;
                ConnectionError = null;
                _logger?.TrackError(new Exception($"Switched to {backend.DisplayName} at {backend.Endpoint}"), isCrash: false);
                return true;
            }

            ConnectionError = $"Backend '{backend.DisplayName}' is not responding.";
            _isConnected = wasConnected;
            return false;
        }
        catch (Exception ex)
        {
            ConnectionError = $"Failed to connect to {backend.DisplayName}: {ex.Message}";
            _isConnected = wasConnected;
            return false;
        }
    }

    public async Task<bool> InitializeAsync()
    {
        _logger?.TrackError(new Exception("Initializing local chat backend..."), isCrash: false);
        ConnectionError = null;

        foreach (var backend in _clientFactory.Backends)
        {
            try
            {
                if (await backend.PingAsync())
                {
                    _activeBackend = backend;
                    _activeBackendId = backend.Id;
                    _isConnected = true;
                    _logger?.TrackError(new Exception($"Connected to {backend.DisplayName} at {backend.Endpoint}"), isCrash: false);
                    return true;
                }
            }
            catch { }
        }

        ConnectionError = "Could not connect to any local AI backend. Start Ollama or LM Studio and try again.";
        _isConnected = false;
        // No modal here — callers surface ConnectionError in StatusMessage / on explicit user action.
        return false;
    }

    public async Task<List<string>> GetAvailableModelsAsync()
    {
        if (string.Equals(_activeBackendId, "codex", StringComparison.OrdinalIgnoreCase))
            return await _codexClient.ListModelsAsync().ConfigureAwait(false);
        return await _modelConfiguration.GetAvailableModelsAsync(_activeBackend?.Id);
    }

    public async Task<List<string>> GetAvailableReasoningEffortsAsync(string? modelId = null)
    {
        if (string.Equals(_activeBackendId, "codex", StringComparison.OrdinalIgnoreCase))
            return await _codexClient.ListReasoningEffortsAsync(modelId).ConfigureAwait(false);

        return [];
    }

    public async IAsyncEnumerable<string> SendMessageAsync(
        List<ChatMessage> messages,
        string? modelId = null,
        string? reasoningEffort = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.Equals(_activeBackendId, "codex", StringComparison.OrdinalIgnoreCase))
        {
            var codexSqlFix = _currentMode == ChatMode.SqlFix;
            var codexCurrentSql = codexSqlFix
                ? await _toolExecutor.GetCurrentSql().ConfigureAwait(false)
                : string.Empty;
            var codexContext = await BuildCodexContextAsync(_currentMode).ConfigureAwait(false);
            var codexSystemPrompt = BuildSystemPrompt(_currentMode);
            var codexResponse = codexSqlFix ? new StringBuilder() : null;
            await foreach (var chunk in _codexClient.SendAsync(
                messages,
                modelId,
                reasoningEffort,
                _currentMode,
                codexSystemPrompt,
                codexContext,
                cancellationToken))
            {
                codexResponse?.Append(chunk);
                yield return chunk;
            }

            if (codexSqlFix && codexResponse is not null && !cancellationToken.IsCancellationRequested)
            {
                var lastUserMessage = FindLastUserMessage(messages);
                if (lastUserMessage is not null)
                {
                    var fallbackResult = await TryApplyDefaultSqlFixAsync(
                        codexCurrentSql,
                        codexResponse.ToString(),
                        lastUserMessage.Content).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(fallbackResult))
                        yield return $"\n\n{fallbackResult}";
                }
            }
            yield break;
        }

        var client = GetClient(modelId);
        if (client is null)
        {
            yield return "[Error: Local AI backend not initialized.]";
            yield break;
        }

        switch (_currentMode)
        {
            case ChatMode.SqlFix:
                await foreach (var chunk in SendMessageSqlFixAsync(client, messages, cancellationToken))
                    yield return chunk;
                break;

            case ChatMode.Simple:
                await foreach (var chunk in SendMessagePlainAsync(client, messages, cancellationToken))
                    yield return chunk;
                break;

            default:
                await foreach (var chunk in SendMessageWithToolsAsync(client, messages, modelId, cancellationToken))
                    yield return chunk;
                break;
        }
    }

    #region Mode Implementations

    private async IAsyncEnumerable<string> SendMessageWithToolsAsync(
        IChatClient client,
        List<ChatMessage> messages,
        string? modelId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lastUserMessage = FindLastUserMessage(messages);
        if (lastUserMessage is null) yield break;

        var (aiMessages, _) = await BuildAiMessagesAsync(messages, lastUserMessage);
        var options = CreateChatOptions(withTools: true);

        await foreach (var chunk in StreamWithRetryAsync(client, aiMessages, options, cancellationToken))
            yield return chunk;
    }

    private async IAsyncEnumerable<string> SendMessageSqlFixAsync(
        IChatClient client,
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lastUserMessage = FindLastUserMessage(messages);
        if (lastUserMessage is null) yield break;

        var currentSql = await _toolExecutor.GetCurrentSql();
        var diagnostics = await _toolExecutor.GetDiagnostics();
        var context = BuildContextSection(currentSql, diagnostics);

        var systemPrompt = BuildSystemPrompt(ChatMode.SqlFix);
        var prompt = $"""
            {systemPrompt}

            {context}
            """;

        var aiMessages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var history = messages.Where(m => m != lastUserMessage).TakeLast(6);
        foreach (var msg in history)
            aiMessages.Add(new(msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? ChatRole.User : ChatRole.Assistant, msg.Content));

        aiMessages.Add(new(ChatRole.User, lastUserMessage.Content));

        var options = CreateChatOptions(withTools: true);
        options.Tools = BuildSqlFixToolList().Select(f => (AITool)f).ToList();

        var textChunks = new List<string>();
        var functionCalls = new List<FunctionCallContent>();
        string? errorMessage = null;

        // Inner stream yields chunks; outer try-catch handles errors
        // (yield return can't be inside try-catch in C#)
        var innerEnumerator = InnerSqlFixStream(client, aiMessages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (await innerEnumerator.MoveNextAsync().ConfigureAwait(false))
            {
                var chunk = innerEnumerator.Current;
                if (chunk.Text is not null)
                    textChunks.Add(chunk.Text);
                if (chunk.Contents is not null)
                {
                    foreach (var content in chunk.Contents)
                    {
                        if (content is FunctionCallContent fcc)
                            functionCalls.Add(fcc);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            errorMessage = "\n[SqlFix timed out]";
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
            errorMessage = $"\n[Error: {ex.Message}]";
        }
        finally
        {
            await innerEnumerator.DisposeAsync();
        }

        // Stream collected text to UI
        foreach (var text in textChunks)
            yield return text;

        if (errorMessage is not null)
        {
            yield return errorMessage;
            yield break;
        }

        // Execute ApplySqlFix if the model called it
        foreach (var fc in functionCalls)
        {
            var argsJson = SerializeFunctionArguments(fc.Arguments);
            var result = await _toolExecutor.ExecuteToolAsync(fc.Name, argsJson);
            yield return $"\n\n[Tool {fc.Name} executed: {result}]";
        }

        // Fallback: if model didn't call ApplySqlFix but output SQL, auto-apply it
        var textBuilder = new StringBuilder();
        foreach (var t in textChunks) textBuilder.Append(t);
        if (functionCalls.Count == 0 && textBuilder.Length > 0)
        {
            var sql = ExtractSqlFromResponse(textBuilder.ToString());
            if (ShouldApplySqlFixByDefault(lastUserMessage.Content)
                && !string.IsNullOrWhiteSpace(sql)
                && !string.Equals(sql.Trim(), currentSql?.Trim(), StringComparison.Ordinal))
            {
                var approved = _toolConfirmationHandler is not null
                    && await _toolConfirmationHandler(
                        "ApplySqlFix",
                        System.Text.Json.JsonSerializer.Serialize(
                            new ApplySqlFixConfirmation { ProposedSql = sql },
                            ChatToolConfirmationJsonContext.Default.ApplySqlFixConfirmation));
                if (approved)
                {
                    var result = await _toolExecutor.ApplySqlFix(sql);
                    yield return $"\n\n[Applied after approval: {result}]";
                }
                else
                {
                    yield return "\n\n[SQL fix prepared but not applied: user approval was not granted.]";
                }
            }
        }
    }

    private static async IAsyncEnumerable<SqlFixChunk> InnerSqlFixStream(
        IChatClient client,
        List<Microsoft.Extensions.AI.ChatMessage> messages,
        ChatOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(120));
        await foreach (var update in client.GetStreamingResponseAsync(messages, options, cts.Token))
            yield return new SqlFixChunk(update.Text, update.Contents);
    }

    private sealed record SqlFixChunk(string? Text, IList<AIContent>? Contents);

    private async IAsyncEnumerable<string> SendMessagePlainAsync(
        IChatClient client,
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lastUserMessage = FindLastUserMessage(messages);
        if (lastUserMessage is null) yield break;

        var currentSql = await _toolExecutor.GetCurrentSql();
        var context = BuildContextSection(
            currentSql,
            string.Empty,
            includeDiagnostics: false,
            suppressUnchangedSql: false);

        var aiMessages = new List<Microsoft.Extensions.AI.ChatMessage>();
        aiMessages.Add(new(ChatRole.System, BuildSystemPrompt(ChatMode.Simple)));

        var history = messages.Where(m => m != lastUserMessage && !string.IsNullOrWhiteSpace(m.Content)).TakeLast(8);
        foreach (var msg in history)
            aiMessages.Add(new(msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? ChatRole.User : ChatRole.Assistant, msg.Content));

        var simplePrompt = string.IsNullOrWhiteSpace(context)
            ? lastUserMessage.Content
            : $"{context}\n\nUser: {lastUserMessage.Content}";

        aiMessages.Add(new(ChatRole.User, simplePrompt));

        var options = CreateChatOptions(withTools: false);

        await foreach (var chunk in StreamWithRetryAsync(client, aiMessages, options, cancellationToken))
            yield return chunk;
    }

    #endregion

    private static string SerializeFunctionArguments(IEnumerable<KeyValuePair<string, object?>> arguments)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var argument in arguments)
            {
                writer.WritePropertyName(argument.Key);
                WriteFunctionArgument(writer, argument.Value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteFunctionArgument(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonElement element:
                element.WriteTo(writer);
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                break;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                writer.WriteNumberValue(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
                break;
            case float number:
                writer.WriteNumberValue(number);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    #region Builders

    private List<AIFunction> BuildSqlFixToolList()
    {
        return
        [
            AIFunctionFactory.Create(_toolExecutor.GetDiagnostics),
            AIFunctionFactory.Create(_toolExecutor.GetCurrentSql),
            AIFunctionFactory.Create(_toolExecutor.GetCurrentSqlEditorContext),
            AIFunctionFactory.Create(ApplySqlFix),
            AIFunctionFactory.Create(_toolExecutor.BrowseSchemaObjects),
            AIFunctionFactory.Create(_toolExecutor.GetObjectColumns),
            AIFunctionFactory.Create(_toolExecutor.GetTableMetadata),
        ];
    }

    private async Task<string> ApplySqlFix(string proposedSql)
    {
        if (_toolConfirmationHandler is null
            || !await _toolConfirmationHandler(
                "ApplySqlFix",
                System.Text.Json.JsonSerializer.Serialize(
                    new ApplySqlFixConfirmation { ProposedSql = proposedSql },
                    ChatToolConfirmationJsonContext.Default.ApplySqlFixConfirmation)).ConfigureAwait(false))
        {
            return "[SQL change denied by the user.]";
        }

        return await _toolExecutor.ApplySqlFix(proposedSql).ConfigureAwait(false);
    }

    private async Task<string> ExecuteSql(string sql)
    {
        if (_toolConfirmationHandler is null
            || !await _toolConfirmationHandler(
                "ExecuteSql",
                System.Text.Json.JsonSerializer.Serialize(
                    new ExecuteSqlConfirmation { Sql = sql },
                    ChatToolConfirmationJsonContext.Default.ExecuteSqlConfirmation)).ConfigureAwait(false))
        {
            return "[SQL execution denied by the user.]";
        }

        return await _toolExecutor.ExecuteSql(sql).ConfigureAwait(false);
    }

    private ChatOptions CreateChatOptions(bool withTools = false)
    {
        var config = _generalApplicationData.Config;
        var options = new ChatOptions
        {
            MaxOutputTokens = config.AiChatMaxTokens > 0 ? config.AiChatMaxTokens : 4096,
            Temperature = (float)Math.Clamp(config.AiChatTemperature, 0.0, 2.0),
            AdditionalProperties = new() { ["think"] = false }
        };
        if (withTools)
        {
            var tools = _toolExecutor.BuildToolList().Select(f => (AITool)f).ToList();
            tools.Add(AIFunctionFactory.Create(ApplySqlFix));
            tools.Add(AIFunctionFactory.Create(ExecuteSql));
            options.Tools = tools;
        }
        return options;
    }

    private string BuildSystemPrompt(ChatMode mode)
    {
        var basePrompt = _promptBuilder.Build(mode);
        var overrideText = _generalApplicationData.Config.AiChatSystemPromptOverride;
        if (string.IsNullOrWhiteSpace(overrideText))
        {
            return basePrompt;
        }
        return string.IsNullOrWhiteSpace(basePrompt)
            ? overrideText.Trim()
            : $"{overrideText.Trim()}\n\n{basePrompt}";
    }

    private async Task<(List<Microsoft.Extensions.AI.ChatMessage> AiMessages, string CurrentPrompt)> BuildAiMessagesAsync(
        List<ChatMessage> messages,
        ChatMessage lastUserMessage)
    {
        var currentSql = await _toolExecutor.GetCurrentSql();
        var diagnostics = await _toolExecutor.GetDiagnostics();
        var context = BuildContextSection(currentSql, diagnostics);

        var prompt = BuildPromptWithActiveEditorContext(lastUserMessage.Content);
        var dbContext = await Task.Run(_stateProvider.BuildDatabaseContextSection);
        var attachmentMeta = _stateProvider.BuildAttachmentMetadataSection(lastUserMessage.Attachments);

        var combinedPrompt = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(context))
        {
            combinedPrompt.AppendLine(context);
            combinedPrompt.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(dbContext))
        {
            combinedPrompt.AppendLine(dbContext);
            combinedPrompt.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(attachmentMeta))
        {
            combinedPrompt.AppendLine(attachmentMeta);
            combinedPrompt.AppendLine();
        }
        combinedPrompt.Append(prompt);

        var systemMessage = BuildSystemPrompt(_currentMode);

        var aiMessages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.System, systemMessage),
            new(ChatRole.User, combinedPrompt.ToString())
        };

        return (aiMessages, combinedPrompt.ToString());
    }

    private async Task<string> BuildCodexContextAsync(ChatMode mode)
    {
        var currentSql = await _toolExecutor.GetCurrentSql().ConfigureAwait(false);
        var diagnostics = mode == ChatMode.Simple
            ? string.Empty
            : await _toolExecutor.GetDiagnostics().ConfigureAwait(false);
        var context = BuildContextSection(
            currentSql,
            diagnostics,
            includeDiagnostics: mode != ChatMode.Simple,
            suppressUnchangedSql: false);

        if (mode == ChatMode.Expert)
        {
            var databaseContext = await Task.Run(_stateProvider.BuildDatabaseContextSection).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(databaseContext))
                context = string.IsNullOrWhiteSpace(context)
                    ? databaseContext
                    : $"{context}\n\n{databaseContext}";
        }

        return context;
    }

    private string BuildContextSection(
        string currentSql,
        string diagnostics,
        bool includeDiagnostics = true,
        bool suppressUnchangedSql = true)
    {
        var sqlHash = currentSql?.GetHashCode();
        var sqlChanged = !_lastSqlHash.HasValue || _lastSqlHash.Value != sqlHash;
        _lastSqlHash = sqlHash;

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(currentSql) && !LocalSqlEditorContextFormatter.IsUnavailableSqlMessage(currentSql))
        {
            sb.AppendLine("CURRENT SQL:");
            if (!suppressUnchangedSql || sqlChanged)
            {
                var sql = currentSql.Length > 20000 ? currentSql[..20000] + "\n-- [truncated at 20k chars]" : currentSql;
                sb.AppendLine(sql);
            }
            else
            {
                sb.AppendLine("[same as previously]");
            }
        }

        if (includeDiagnostics
            && !string.IsNullOrWhiteSpace(diagnostics)
            && !diagnostics.Contains("No diagnostics", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine("DIAGNOSTICS (heuristic/static analysis; may be incomplete, stale, or incorrect — advisory only; verify before relying on it):");
            sb.AppendLine(diagnostics);
        }

        return sb.ToString().TrimEnd();
    }

    private string BuildPromptWithActiveEditorContext(string prompt)
    {
        var context = _stateProvider.GetSqlEditorContextSnapshot();
        return LocalContextBuilder.BuildPromptWithActiveEditorContext(prompt, context);
    }

    private static ChatMessage? FindLastUserMessage(IReadOnlyList<ChatMessage> messages)
    {
        return messages.LastOrDefault(static message => message.Role.Equals("user", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> TryApplyDefaultSqlFixAsync(
        string currentSql,
        string response,
        string userPrompt)
    {
        if (!ShouldApplySqlFixByDefault(userPrompt))
            return null;

        var currentAfterTurn = await _toolExecutor.GetCurrentSql().ConfigureAwait(false);
        if (!string.Equals(currentSql.Trim(), currentAfterTurn.Trim(), StringComparison.Ordinal))
            return null;

        var proposedSql = ExtractSqlFromResponse(response);
        if (string.IsNullOrWhiteSpace(proposedSql)
            || string.Equals(proposedSql.Trim(), currentSql.Trim(), StringComparison.Ordinal))
            return null;

        var confirmationPayload = System.Text.Json.JsonSerializer.Serialize(
            new ApplySqlFixConfirmation { ProposedSql = proposedSql },
            ChatToolConfirmationJsonContext.Default.ApplySqlFixConfirmation);
        var approved = _toolConfirmationHandler is not null
            && await _toolConfirmationHandler("ApplySqlFix", confirmationPayload).ConfigureAwait(false);
        if (!approved)
            return "[SQL fix prepared but not applied: user approval was not granted.]";

        var result = await _toolExecutor.ApplySqlFix(proposedSql).ConfigureAwait(false);
        return $"[Applied to the active SQL document after approval: {result}]";
    }

    private static bool ShouldApplySqlFixByDefault(string userPrompt)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
            return true;

        var normalized = userPrompt.Trim().ToLowerInvariant();
        string[] previewOnlyPhrases =
        [
            "only show", "show only", "just show", "show me the correction without applying",
            "do not apply", "don't apply", "do not change", "don't change", "preview only",
            "preview without applying", "show without applying",
            "show without changing", "explain without changing", "only provide the correction"
        ];

        return !previewOnlyPhrases.Any(normalized.Contains);
    }

    private static string ExtractSqlFromResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var trimmed = text.Trim();

        // Try to extract from ```sql ... ``` block
        var sqlBlockMatch = Regex.Match(trimmed, @"```sql\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
        if (sqlBlockMatch.Success)
        {
            var sql = sqlBlockMatch.Groups[1].Value.Trim();
            if (IsLikelySql(sql)) return sql;
        }

        // Try to extract from ``` ... ``` block
        var codeBlockMatch = Regex.Match(trimmed, @"```\s*([\s\S]*?)```");
        if (codeBlockMatch.Success)
        {
            var sql = codeBlockMatch.Groups[1].Value.Trim();
            if (IsLikelySql(sql)) return sql;
        }

        // If whole response looks like SQL, return it
        if (IsLikelySql(trimmed)) return trimmed;

        return string.Empty;
    }

    private static bool IsLikelySql(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var startsWithSqlKeyword = Regex.IsMatch(text.TrimStart(),
            @"^(SELECT|INSERT|UPDATE|DELETE|CREATE|DROP|ALTER|TRUNCATE|MERGE|WITH|EXPLAIN|CALL|SET)\b",
            RegexOptions.IgnoreCase);

        var hasSqlOperators = text.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                           || text.Contains("FROM", StringComparison.OrdinalIgnoreCase);

        return startsWithSqlKeyword || hasSqlOperators;
    }

    #endregion

    #region Client

    private IChatClient? GetClient(string? modelId)
    {
        var backend = _activeBackend ?? _clientFactory.Backends.FirstOrDefault();
        if (backend is null) return null;

        var effectiveModelId = modelId ?? "default";
        return backend.CreateChatClient(effectiveModelId);
    }

    private async Task<bool> ConfirmCodexToolAsync(string toolName, string arguments)
    {
        if (_toolConfirmationHandler is null)
            return false;
        return await _toolConfirmationHandler(toolName, arguments).ConfigureAwait(false);
    }

    private async Task<string> ExecuteCodexToolAsync(string toolName, string arguments)
    {
        var mappedName = toolName switch
        {
            "get_current_sql" => "GetCurrentSql",
            "get_sql_editor_context" => "GetCurrentSqlEditorContext",
            "get_active_database_context" => "GetActiveDatabaseContext",
            "list_schemas" => "ListSchemas",
            "browse_schema_objects" => "BrowseSchemaObjects",
            "search_schema_objects" => "SearchSchemaObjects",
            "get_object_definition" => "GetObjectDefinition",
            "get_object_columns" => "GetObjectColumns",
            "get_table_metadata" => "GetTableMetadata",
            "get_diagnostics" => "GetDiagnostics",
            "get_netezza_reference" => "GetNetezzaReference",
            "apply_sql_document_change" => "ApplySqlFix",
            "get_last_execution_error" => "GetLastExecutionError",
            "export_schema" => "ExportSchema",
            "execute_sql" => "ExecuteSql",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(mappedName))
            return $"[Blocked: tool '{toolName}' is not available in Codex mode.]";

        return await _toolExecutor.ExecuteToolAsync(mappedName, arguments).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<string> StreamWithRetryAsync(
        IChatClient client,
        List<Microsoft.Extensions.AI.ChatMessage> messages,
        ChatOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var config = _generalApplicationData.Config;
        var retryCount = Math.Clamp(config.AiChatMaxRetries, 0, 5);
        var timeout = config.AiChatRequestTimeoutMs > 0
            ? (int)Math.Ceiling(config.AiChatRequestTimeoutMs / 1000.0)
            : 120;
        var yieldedAny = false;

        for (int attempt = 1; attempt <= retryCount + 1; attempt++)
        {
            using var timeoutCts = new CancellationTokenSource();
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeout));

            var inner = InnerStreamAsync(client, messages, options, timeoutCts.Token);
            await using var enumerator = inner.GetAsyncEnumerator(CancellationToken.None);

            bool shouldRetry = false;
            string? errorMessage = null;

            while (true)
            {
                bool hasMore;
                try
                {
                    hasMore = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (attempt <= retryCount)
                    {
                        timeout += 30;
                        await Task.Delay(1000 * attempt, CancellationToken.None);
                        shouldRetry = true;
                    }
                    else
                    {
                        errorMessage = $"\n[Response timeout - no response within {timeout} seconds]";
                    }
                    break;
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
                catch (Exception ex)
                {
                    if (attempt <= retryCount)
                    {
                        await Task.Delay(1000 * attempt, CancellationToken.None);
                        shouldRetry = true;
                    }
                    else
                    {
                        _logger.TrackError(ex, isCrash: false);
                        if (!yieldedAny)
                        {
                            errorMessage = $"\n[Error: {ex.Message}]";
                        }
                    }
                    break;
                }

                if (!hasMore)
                {
                    yield break;
                }

                yieldedAny = true;
                yield return enumerator.Current;
            }

            if (errorMessage is not null)
            {
                yield return errorMessage;
                yield break;
            }

            if (shouldRetry)
            {
                yieldedAny = false;
                continue;
            }

            yield break;
        }
    }

    private static async IAsyncEnumerable<string> InnerStreamAsync(
        IChatClient client,
        List<Microsoft.Extensions.AI.ChatMessage> messages,
        ChatOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in client.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (update.Text is not null)
            {
                yield return update.Text;
            }
        }
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        await _codexClient.DisposeAsync().ConfigureAwait(false);
    }
}
