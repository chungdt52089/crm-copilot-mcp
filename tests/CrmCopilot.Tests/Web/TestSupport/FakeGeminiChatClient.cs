using CrmCopilot.Web.Chat;
using Google.GenAI.Types;

namespace CrmCopilot.Tests.Web.TestSupport;

/// <summary>
/// Deterministic offline stand-in for IGeminiChatClient. Records every call's contents/config
/// (used to assert no raw PII is ever sent to Gemini, and that the tool allowlist sent is exactly
/// the P0-05-approved intersection) and the total call count (used for the MCP-call-bounded-loop
/// tests — geminiCallCount vs. mcpCallCount, plan §6).
/// </summary>
internal sealed class FakeGeminiChatClient : IGeminiChatClient
{
    private readonly Queue<GenerateContentResponse> _responses = new();

    public int CallCount { get; private set; }
    public List<IReadOnlyList<Content>> CapturedContents { get; } = [];
    public List<GenerateContentConfig> CapturedConfigs { get; } = [];
    public Exception? ThrowOnNextCall { get; set; }

    public void Enqueue(GenerateContentResponse response) => _responses.Enqueue(response);

    public Task<GenerateContentResponse> GenerateAsync(
        IReadOnlyList<Content> contents, GenerateContentConfig config, CancellationToken cancellationToken)
    {
        CallCount++;
        CapturedContents.Add(contents);
        CapturedConfigs.Add(config);

        if (ThrowOnNextCall is { } exception)
        {
            ThrowOnNextCall = null;
            throw exception;
        }

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"FakeGeminiChatClient: no more scripted responses (call #{CallCount}).");
        }

        return Task.FromResult(_responses.Dequeue());
    }

    public static GenerateContentResponse TextResponse(string text) => new()
    {
        Candidates = [new Candidate { Content = new Content { Role = "model", Parts = [Part.FromText(text)] } }],
    };

    public static GenerateContentResponse FunctionCallResponse(string name, Dictionary<string, object>? args = null) => new()
    {
        Candidates = [new Candidate { Content = new Content { Role = "model", Parts = [Part.FromFunctionCall(name, args ?? [])] } }],
    };

    public static GenerateContentResponse MultiFunctionCallResponse(params (string Name, Dictionary<string, object> Args)[] calls) => new()
    {
        Candidates =
        [
            new Candidate
            {
                Content = new Content
                {
                    Role = "model",
                    Parts = [.. calls.Select(call => Part.FromFunctionCall(call.Name, call.Args))],
                },
            },
        ],
    };
}
