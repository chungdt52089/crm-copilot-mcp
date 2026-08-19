# 13 — Reference Sources

Kiểm chứng ngày: **2026-08-18**. Khi bắt đầu checkpoint có package/model/API bên ngoài, Claude phải kiểm tra lại nguồn chính thức thay vì mặc định phiên bản trong file này vẫn mới nhất.

## MCP

- [MCP Architecture Overview](https://modelcontextprotocol.io/docs/2026-07-28/learn/architecture) — Host/Client/Server, data layer và transport layer. MCP không quy định cách ứng dụng quản lý LLM/context nội bộ.
- [Official MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) — official SDK cho MCP client/server .NET.
- [ModelContextProtocol NuGet](https://www.nuget.org/packages/ModelContextProtocol/) — main package; baseline quan sát: 2.2.0.
- [MCP C# SDK package guidance](https://github.com/modelcontextprotocol/csharp-sdk#packages) — HTTP-based server dùng `ModelContextProtocol.AspNetCore`.
- [MCP Security Best Practices](https://modelcontextprotocol.io/docs/2026-07-28/tutorials/security/security_best_practices) — threat/security considerations.

## .NET

- [.NET releases and support](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support) — .NET 10 là LTS tại baseline, support đến tháng 11/2028.
- [ASP.NET Core documentation](https://learn.microsoft.com/en-us/aspnet/core/) — Minimal APIs, configuration, health, security.

## Gemini

- [Gemini 3.5 Flash-Lite](https://ai.google.dev/gemini-api/docs/models/gemini-3.5-flash-lite) — model code, function calling và structured output.
- [Gemini Embeddings](https://ai.google.dev/gemini-api/docs/embeddings) — task types, dimensions, normalization và migration.
- [Gemini API pricing](https://ai.google.dev/gemini-api/docs/pricing) — free/paid tier và data-use note. Free tier có thể dùng dữ liệu để cải thiện sản phẩm; vì vậy P0 chỉ dùng synthetic data.

Baseline embedding decision:

- Model: `gemini-embedding-001`.
- Dimension: 768.
- Manual L2 normalization bắt buộc khi dùng non-default dimension.
- Index: `RETRIEVAL_DOCUMENT`.
- Query: `RETRIEVAL_QUERY`.
- Đổi sang embedding model khác bắt buộc re-embed/re-index vì vector spaces không tương thích.

## Chroma

- [Chroma Docker guide](https://docs.trychroma.com/guides/deploy/docker) — chạy Chroma server container và kết nối client/server.
- [Chroma client/server mode](https://docs.trychroma.com/guides/deploy/client-server-mode) — tách server và HTTP client.

Chroma docs chính thức hiện minh họa client cho Python/TypeScript/Rust rõ hơn .NET. P0 dùng một thin HTTP adapter .NET và contract tests; không phụ thuộc package cộng đồng nếu chưa review.

## Nguồn đề bài

- `Bank_Problem_Brief_AI_Agents_CRM_VI.docx` do người dùng cung cấp.
- Q&A mentor qua hai ảnh do người dùng cung cấp.
- Các file training/hackathon/references/repo structure do người dùng cung cấp.

Điểm hiệu chỉnh từ Q&A mentor:

- Chưa có CRM API thật; được tự mock.
- Không train model; ưu tiên RAG/semantic retrieval.
- Customer lookup, email draft, call script là tác vụ RM tốn thời gian.
- MCP tools tối thiểu theo hướng customer/interactions/opportunities/campaigns/generation.
- Conversation state in-memory đủ cho POC.
- Vector store không bị bắt buộc; dự án đã chốt Chroma.
- Mask PII trước external LLM.

## Quy tắc nguồn

- Với technical facts thay đổi theo thời gian, chỉ dựa vào documentation/repository chính thức.
- Pin package/model sau khi verify ngay tại checkpoint.
- Không tự nâng phiên bản giữa checkpoint.
- Ghi version thực tế vào `CHECKPOINT_STATUS.md` khi P0-01/P0-03 hoàn tất.

