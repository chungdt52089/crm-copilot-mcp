# 04 — P0 Checkpoints

## 1. Nguyên tắc gate

- Mỗi checkpoint có đầu vào, output và lệnh kiểm tra riêng.
- Claude plan → ChatGPT review → Product Owner phê duyệt → Claude implement → Claude evidence → ChatGPT verdict.
- Không mở checkpoint sau khi checkpoint hiện tại chưa PASS, trừ khi reviewer ghi rõ ngoại lệ.

## 2. Checkpoint map

| ID | Tên | Output có thể demo/kiểm chứng |
| --- | --- | --- |
| P0-00 | Documentation Baseline | Quyết định, kiến trúc, AC, workflow và prompt được khóa |
| P0-01 | Repository & Solution Scaffold | Solution build; project boundaries/config/health có khung |
| P0-02 | Synthetic Data & Mock CRM API | Customer/interaction endpoint deterministic + tests |
| P0-03 | Gemini Embedding & Chroma RAG | Ingest/query knowledge; canonical top-3 retrieval pass |
| P0-04 | MCP Server Core Tools | Tool discovery/call cho customer, interaction, search knowledge |
| P0-05 | AI Host + MCP Client + Gemini Tool Loop | Natural-language request gọi đúng tool qua MCP |
| P0-06 | Conversation State | Follow-up “khách hàng này” dùng đúng ID |
| P0-07 | RAG Email Draft + PII | `generate_email` grounded, structured và masked |
| P0-08 | Web UI + Trace + Sources | Luồng đầu-cuối dễ demo trong UI |
| P0-09 | Acceptance, Hardening & Demo | 7/8 tests, 3 lần demo liên tiếp, docs/runbook cập nhật |

## 3. Chi tiết checkpoint

### P0-00 — Documentation Baseline

**In scope:** toàn bộ bộ tài liệu này.  
**Pass:** các decision không mâu thuẫn, P0 rõ, MCP/state/RAG tách đúng, có workflow review.  
**Trạng thái baseline:** DONE.

### P0-01 — Repository & Solution Scaffold

**Tạo:** solution và bốn project + test project; config typed; health endpoints; `.gitignore`; `.env.example`; README chạy khung.

**Không làm:** business tools, Gemini thật, Chroma logic.

**Pass evidence:**

- `dotnet --info`
- `dotnet restore`
- `dotnet build --no-restore`
- `dotnet test --no-build` hoặc giải thích nếu chưa có test executable
- health của ba web process
- package/version list đã pin

### P0-02 — Synthetic Data & Mock CRM API

**Tạo:** dataset canonical; DTO; validation; endpoints customer/interactions; `ICrmGateway` + `MockCrmGateway`; tests success/not-found/duplicate/sort.

**Không làm:** HubSpot, Chroma, LLM.

**Pass evidence:**

- Dataset đủ tối thiểu theo data spec.
- `GET /customers/CUS-0001` trả đúng.
- Search tên unique/duplicate deterministic.
- Interactions lọc đúng và newest-first.
- Contract/error integration tests pass.

### P0-03 — Gemini Embedding & Chroma RAG

**Tạo:** embedding client, L2 normalization, Chroma HTTP adapter, ingestion idempotent, collection metadata, retrieval test.

**Không làm:** email generation, MCP tool wrapper.

**Pass evidence:**

- Chroma heartbeat.
- Ingest 6 products + 8 templates không tạo duplicate khi chạy lại.
- Embedding length 768 và norm gần 1.
- Query canonical đưa `PRD-SAV-006M` vào top 3.
- Không log key hoặc full payload nhạy cảm.

### P0-04 — MCP Server Core Tools

**Tạo:** official MCP server; `get_customer`, `get_interactions`, `search_product_knowledge`; structured error; trace.

**Không làm:** Web UI, conversation state, email tool.

**Pass evidence:**

- MCP Inspector hoặc test client list đúng tools.
- Mỗi tool gọi thành công với canonical input.
- Invalid input/not-found/upstream error được chuẩn hóa.
- MCP Server không có session/customer global mutable state.

### P0-05 — AI Host + MCP Client + Gemini Tool Loop

**Tạo:** MCP Client, tool discovery mapping cho Gemini function calling, bounded loop, natural-language endpoint.

**Không làm:** polished UI, email generation.

**Pass evidence:**

- Câu “Tìm khách hàng CUS-0001” sinh tool call đúng.
- Câu hỏi interaction với explicit ID gọi đúng tool.
- Host không bypass MCP để gọi Mock CRM.
- Loop dừng ở giới hạn; unknown tool bị reject.

### P0-06 — Conversation State

**Tạo:** `IConversationStateStore`, in-memory implementation, browser/session contract, resolution rule và reset.

**Không làm:** Redis/Postgres/long-term memory.

**Pass evidence:**

- Turn 1 lookup cập nhật `CurrentCustomerId`.
- Turn 2 “khách hàng này” gọi interactions bằng cùng ID.
- Hai session không lẫn state.
- Follow-up khi chưa có customer yêu cầu làm rõ.
- Stored message/state không chứa raw email/phone/account.

### P0-07 — RAG Email Draft + PII Masking

**Tạo:** masker, captured/test Gemini boundary, `generate_email`, RAG retrieval, structured schema, local placeholder restore.

**Không làm:** gửi email, call script.

**Pass evidence:**

- Canonical email grounded vào retrieved product/template.
- `sourceIds` là subset của retrieved evidence.
- `requiresHumanApproval=true`.
- Captured request/log không chứa raw PII.
- No-knowledge/Chroma down/Gemini invalid schema xử lý controlled.

### P0-08 — Web UI + Trace + Sources

**Tạo:** chat tối thiểu, customer card, interaction list, draft panel, source chips, tool trace accordion, loading/error/reset.

**Không làm:** design system, login, responsive perfection.

**Pass evidence:**

- Toàn bộ canonical flow chạy bằng browser.
- Output model không render bằng unsafe raw HTML.
- Loading không cho gửi double request.
- Reset session hoạt động.

### P0-09 — Acceptance, Hardening & Demo

**Tạo:** test runner/report 8 scenario, startup docs, final runbook, known limitations, optional Docker nếu không đe dọa core.

**Pass evidence:**

- ≥7/8 scenario pass.
- Demo flow chạy 3 lần liên tiếp.
- Clean build/test output.
- Secret scan/diff review.
- README từ clean environment hoặc người thứ hai chạy được.
- `CHECKPOINT_STATUS.md` cập nhật final verdict.

## 4. Scope cut order nếu chậm

Cắt theo đúng thứ tự sau:

1. Cloud deploy.
2. Docker hóa toàn bộ service (vẫn giữ Chroma Docker local).
3. UI polish.
4. Opportunity dataset/tool.
5. Call script.
6. Campaign.

Không cắt Customer, Interaction, MCP thật, conversation state, RAG email, PII masking hoặc test flow chuẩn.

