using CrmCopilot.Contracts.Knowledge;

namespace CrmCopilot.McpServer.Knowledge;

internal sealed record VectorRecord(string Id, double[] Embedding, string Document, KnowledgeSourceMetadata Metadata);
