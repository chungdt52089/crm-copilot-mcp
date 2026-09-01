using CrmCopilot.Contracts.Knowledge;

namespace CrmCopilot.McpServer.Knowledge;

internal sealed record VectorMatch(string Id, string Document, KnowledgeSourceMetadata Metadata, double Distance);
