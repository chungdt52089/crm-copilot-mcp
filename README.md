# CRM Copilot MVP — Bộ tài liệu điều hành dự án

Trạng thái: **Planning baseline đã được phê duyệt**  
Ngày chốt baseline: **2026-08-18**  
Mục tiêu thời gian: **MVP demo được trong 7–9 ngày**

## 1. Mục tiêu

Xây dựng AI CRM Co-Pilot tiếng Việt cho Relationship Manager (RM), chứng minh rõ:

1. **MCP thật** để AI Host gọi các công cụ CRM qua một MCP Server.
2. **RAG thật** trên product knowledge và email templates lưu trong Chroma.
3. **Hội thoại nhiều lượt** với trạng thái khách hàng đang được đề cập.
4. **Email draft có nguồn** và không gửi PII thô tới Gemini.

P0 chỉ cần chạy ổn định luồng:

> Tìm khách hàng → xem tương tác → hỏi tiếp “khách hàng này” → truy xuất knowledge bằng RAG → tạo email draft → hiển thị nguồn và tool trace.

## 2. Quyết định đã khóa

| Hạng mục | Quyết định P0 |
| --- | --- |
| Backend/UI | .NET 10, ASP.NET Core Minimal API, Razor Pages + JavaScript tối thiểu |
| MCP | Official C# SDK; một MCP Server; Streamable HTTP |
| CRM | Dataset JSON tổng hợp + Mock CRM API; adapter qua `ICrmGateway` |
| MCP tools bắt buộc | `get_customer`, `get_interactions`, `search_product_knowledge`, `generate_email` |
| LLM | `gemini-3.5-flash-lite` |
| Embedding | `gemini-embedding-001`, 768 chiều, L2 normalize |
| Vector store | Chroma chạy client/server; .NET gọi HTTP |
| Conversation state | In-memory trong AI Host, theo `sessionId`; MCP Server stateless |
| An toàn | Dữ liệu tổng hợp; PII masking trước Gemini/log; email chỉ là draft |
| Kiểm thử | 8 scenario nội bộ; đạt tối thiểu 7/8 = 87,5% |
| Ngoài P0 | HubSpot, Campaign, gửi email thật, auth production, long-term memory |

Chi tiết và lý do nằm trong `docs/01_PROJECT_DECISIONS.md`.

## 3. Vai trò làm việc

| Vai trò | Trách nhiệm |
| --- | --- |
| Người dùng/Product Owner | Chốt phạm vi, truyền plan/feedback giữa ChatGPT và Claude, quyết định commit |
| ChatGPT/Reviewer | Lập kế hoạch, review và phê duyệt plan của Claude, kiểm tra evidence sau checkpoint, quản lý scope |
| Claude/Implementer | Khảo sát repo, đề xuất plan, chờ phê duyệt, implement đúng một checkpoint, chạy kiểm tra và báo cáo evidence |

ChatGPT không thể tự biết thay đổi trong phiên Claude nếu chưa được cung cấp repository/diff/log. Sau mỗi checkpoint, gửi cho ChatGPT gói evidence định nghĩa trong `docs/09_CLAUDE_WORKFLOW_GUIDE.md`.

## 4. Thứ tự đọc

1. `CLAUDE.md`
2. `docs/01_PROJECT_DECISIONS.md`
3. `docs/02_ARCHITECTURE.md`
4. `docs/03_ACCEPTANCE_CRITERIA.md`
5. `docs/04_P0_CHECKPOINTS.md`
6. `docs/05_IMPLEMENTATION_PLAN_9_DAYS.md`
7. Các đặc tả dữ liệu, MCP, RAG/PII
8. Workflow và prompt
9. Demo runbook
10. `docs/CHECKPOINT_STATUS.md`

## 5. Bản đồ tài liệu

| File | Mục đích |
| --- | --- |
| `CLAUDE.md` | Luật bắt buộc Claude phải tuân theo trong repo |
| `01_PROJECT_DECISIONS.md` | Các quyết định kiến trúc/phạm vi đã khóa |
| `02_ARCHITECTURE.md` | Thành phần, luồng dữ liệu, ownership và trust boundary |
| `03_ACCEPTANCE_CRITERIA.md` | Tiêu chí pass/fail của MVP và 8 test scenario |
| `04_P0_CHECKPOINTS.md` | Chia P0 thành checkpoint nhỏ có gate |
| `05_IMPLEMENTATION_PLAN_9_DAYS.md` | Lịch triển khai 9 ngày và phương án rút xuống 7 ngày |
| `06_DATA_AND_MOCK_API_SPEC.md` | Dataset tổng hợp, schema, endpoint và adapter |
| `07_MCP_TOOL_CONTRACTS.md` | Contract tool P0 và lỗi chuẩn hóa |
| `08_RAG_EMAIL_AND_PII_SPEC.md` | Ingestion, retrieval, email generation và masking |
| `09_CLAUDE_WORKFLOW_GUIDE.md` | Quy trình ChatGPT ↔ người dùng ↔ Claude |
| `10_CLAUDE_PROMPTS.md` | Prompt mẫu theo checkpoint và prompt review |
| `11_DEMO_RUNBOOK.md` | Kịch bản demo, fallback và checklist |
| `12_POST_MVP_AND_INTEGRATION.md` | Opportunity, call script, HubSpot, Docker/cloud |
| `13_REFERENCE_SOURCES.md` | Nguồn chính thức và ngày kiểm chứng |
| `14_ACCEPTANCE_SCENARIO_REPORT.md` | Kết quả bộ 8 scenario T01–T08 theo từng lớp evidence (D/L/B) |
| `CHECKPOINT_STATUS.md` | Sổ trạng thái, evidence, blocker và quyết định review |

## 6. Quy tắc sử dụng

- Không giao Claude một prompt triển khai toàn bộ dự án.
- Mỗi lần chỉ mở **một checkpoint**.
- Claude phải gửi plan trước; ChatGPT review; người dùng xác nhận phê duyệt; Claude mới sửa code.
- Không đánh dấu checkpoint hoàn tất nếu thiếu lệnh kiểm tra và output thực tế.
- Nếu thay model embedding hoặc số chiều, phải xóa/re-index collection tương ứng.
- Nếu P0 chưa ổn định, mọi đề xuất HubSpot/cloud/UI nâng cao chuyển vào backlog.

## 7. Trạng thái ban đầu

- P0-00 — Documentation Baseline: **DONE**
- P0-01 — Repository & Solution Scaffold: **DONE**
- P0-02 — Synthetic Data & Mock CRM API: **DONE**
- P0-03 — Gemini Embedding & Chroma RAG: **DONE** (PASS; merged to `develop` via PR #7; xem mục 8 "Gemini Embedding & Chroma RAG (P0-03)" và `docs/CHECKPOINT_STATUS.md`)
- P0-04 — MCP Server Core Tools: **DONE** (PASS; merged to `develop` via PR #8; xem mục 8 "MCP Server Core Tools (P0-04)" và `docs/CHECKPOINT_STATUS.md`)
- P0-05 — AI Host + MCP Client: **DONE** (PASS; merged to `develop` via PR #9; xem mục 8 "AI Host + MCP Client (P0-05)" và `docs/CHECKPOINT_STATUS.md`)
- P0-06 — Conversation State: **DONE** (PASS; live acceptance confirmed 2026-08-24; merged to `develop` via PR #11, `042f65b` → merge `340b678`; xem mục 8 "Conversation State (P0-06)" và `docs/CHECKPOINT_STATUS.md`)
- P0-07 — RAG Email Draft + PII: **DONE** (PASS; live acceptance confirmed 2026-08-24; merged to `develop` via PR #12, `b463742` → merge `ba4c2ba`; xem `docs/CHECKPOINT_STATUS.md`)
- P0-08 — Web UI + Trace + Sources: **DONE (merged)** (merged to `develop` via PR #13, `cf2ae3e` → merge `a91c041`; verdict chưa được ghi trong `docs/CHECKPOINT_STATUS.md` tại thời điểm merge — xem blocker B-04)
- P0-09 — Acceptance, Hardening & Demo (Pha A): **IN PROGRESS** trên `feature/p0-09-deployment-readiness` (xem mục 8 "Acceptance & Demo (P0-09)")

`develop` chưa được merge vào `main` — `main` vẫn ở `afa47fd` (docs baseline).

Chi tiết evidence/verdict từng checkpoint xem `docs/CHECKPOINT_STATUS.md`.

## 8. Build & Run (P0-01 scaffold)

Yêu cầu: .NET SDK khớp `global.json` (`10.0.400`, band `10.0.4xx`, `rollForward: latestPatch`).

### Restore, build, test

```powershell
dotnet restore CrmCopilot.slnx
dotnet build CrmCopilot.slnx --no-restore
dotnet test tests/CrmCopilot.Tests/CrmCopilot.Tests.csproj --no-build --no-restore
```

### Chạy từng service (chỉ có `/health` ở P0-01, chưa có business logic)

| Service | Lệnh chạy | Health URL |
| --- | --- | --- |
| `CrmCopilot.Web` | `dotnet run --project src/CrmCopilot.Web --launch-profile http --no-build` | http://localhost:5081/health |
| `CrmCopilot.McpServer` | `dotnet run --project src/CrmCopilot.McpServer --launch-profile http --no-build` | http://localhost:5090/health |
| `CrmCopilot.MockCrmApi` | `dotnet run --project src/CrmCopilot.MockCrmApi --launch-profile http --no-build` | http://localhost:5100/health |

Chạy `dotnet build CrmCopilot.slnx` trước khi dùng `--no-build`. Mỗi service dùng port cố định qua `Properties/launchSettings.json` (profile `http`), không hard-code port trong `Program.cs`.

`CrmCopilot.McpServer` yêu cầu biến môi trường `MOCKCRM_API_BASE_URL` (absolute URL, ví dụ `http://localhost:5100`) để khởi động — không có giá trị mặc định nào trong `appsettings.json`. Thiếu hoặc sai giá trị sẽ làm host fail fast ngay khi start thay vì âm thầm fallback. Xem mục "Secret hygiene" bên dưới để biết cách set giá trị này cho `dotnet run` cục bộ.

Kể từ P0-03, chạy đầy đủ web host của `CrmCopilot.McpServer` (không dùng CLI verb `--ingest-knowledge`/`--query-knowledge`) còn yêu cầu thêm `GEMINI_API_KEY` và `CHROMA_BASE_URL` — cùng cơ chế fail-fast qua `ValidateOnStart()`. Hai CLI verb ở mục "Gemini Embedding & Chroma RAG (P0-03)" bên dưới **không** cần `MOCKCRM_API_BASE_URL`.

### Mock CRM API (P0-02)

Khi `CrmCopilot.MockCrmApi` đang chạy (`http://localhost:5100`), các endpoint đọc-only sau đã sẵn sàng, đọc từ `data/crm/customers.json` và `data/crm/interactions.json` (dataset tổng hợp, `synthetic: true`):

| Method | Path | Mô tả |
| --- | --- | --- |
| GET | `/api/customers/{customerId}` | Tra cứu chính xác theo ID; `404 NOT_FOUND` nếu không thấy |
| GET | `/api/customers?query={nameOrId}` | Tìm theo ID hoặc tên chuẩn hoá; `409` (body `ApiEnvelope<CustomerCandidateDto[]>`) nếu trùng tên |
| GET | `/api/customers/{customerId}/interactions?limit=5` | Interaction mới nhất trước, `limit` 1–20; `404 NOT_FOUND` nếu customer không tồn tại |

Ví dụ nhanh (canonical customer `CUS-0001`, xem `docs/06_DATA_AND_MOCK_API_SPEC.md` §3):

```powershell
curl http://localhost:5100/api/customers/CUS-0001
curl http://localhost:5100/api/customers/CUS-0001/interactions
```

### Regenerate the synthetic dataset

Dataset được sinh deterministic từ seed cố định (`SyntheticDatasetGenerator`), không chỉnh tay file JSON lớn. Không chạy lệnh dưới đây với `--customers` lớn và commit kết quả nếu chưa được Product Owner phê duyệt kích thước dataset mới.

```powershell
dotnet run --project src/CrmCopilot.MockCrmApi --no-build -- --generate-dataset [--customers N] [--seed N] [--output <dir>]
```

Không tham số sẽ tái tạo đúng dataset đã checked-in (12 customers / ~26 interactions) vào `data/crm/`, độc lập với current working directory.

### Gemini Embedding & Chroma RAG (P0-03)

RAG code sống trong `CrmCopilot.McpServer` (`Knowledge/`), theo đúng phân vai kiến trúc — MCP Server sở hữu RAG orchestration (docs/02_ARCHITECTURE.md §3). Nguồn dữ liệu là `data/knowledge/products.json` (6 sản phẩm) và `data/knowledge/email-templates.json` (8 template), tổng 14 record, `synthetic: true`, `language: "vi"`; Chroma chỉ là index có thể xoá và tái tạo lại từ hai file này.

**Chạy Chroma cục bộ** (image đã pin, volume đặt tên để dữ liệu tồn tại qua việc xoá container — chưa thêm `compose.yaml`, xem docs/02 §10):

```powershell
docker run -d --name crm-copilot-chroma -p 8000:8000 -v crm-copilot-chroma-data:/data chromadb/chroma:1.5.9
curl http://localhost:8000/api/v2/heartbeat
```

**Ingest / query CLI** (dev-time only, không khởi động Kestrel, không cần `MOCKCRM_API_BASE_URL`):

```powershell
$env:GEMINI_API_KEY = "<giá trị thật, chỉ cục bộ, không commit>"
$env:CHROMA_BASE_URL = "http://localhost:8000"

dotnet run --project src/CrmCopilot.McpServer --no-build -- --ingest-knowledge
dotnet run --project src/CrmCopilot.McpServer --no-build -- --query-knowledge "<câu truy vấn>"
```

`--ingest-knowledge` in ra số document/embedded/unchanged và số record trong collection sau khi ingest; chạy lại lần hai trên dữ liệu không đổi phải cho `0 embedded` (idempotent — không gọi lại Gemini). `--query-knowledge` in ra L2 norm của query embedding (kỳ vọng ~1.0) và top-3 `sourceId`/`distance` — dùng để hiệu chỉnh `KnowledgeRetrievalOptions.MaxDistance` (mặc định `1.2`, xem `src/CrmCopilot.McpServer/Knowledge/KnowledgeRetrievalOptions.cs`).

**Mandatory live acceptance run** — bắt buộc trước khi báo P0-03 PASS (không chỉ là smoke test tuỳ chọn), tách biệt khỏi bộ test mặc định (`dotnet test` chạy offline, xem `LiveRagAcceptanceTests`, `[Fact(SkipUnless=...)]`, opt-in qua chính hai biến môi trường trên):

```powershell
$env:CHROMA_COLLECTION_NAME = "crm-copilot-knowledge-livetest"   # tuỳ chọn — cô lập khỏi collection dev mặc định
dotnet run --project src/CrmCopilot.McpServer --no-build -- --ingest-knowledge   # lần 1: kỳ vọng 14 embedded, count=14
dotnet run --project src/CrmCopilot.McpServer --no-build -- --ingest-knowledge   # lần 2: kỳ vọng 0 embedded/14 unchanged, count vẫn=14
dotnet run --project src/CrmCopilot.McpServer --no-build -- --query-knowledge "Khách hàng quan tâm gửi tiết kiệm an toàn kỳ hạn 6 tháng, cần liên hệ lại."
# kỳ vọng PRD-SAV-006M nằm trong top-3, L2 norm ~1.0
```

Nếu không có `GEMINI_API_KEY` thật hoặc không chạy được Chroma container tại thời điểm implement, đây là stop condition theo CLAUDE.md §9 — báo Product Owner thay vì chỉ dựa vào evidence offline.

Để chỉ chạy `LiveRagAcceptanceTests` qua `dotnet test` (thay vì CLI ở trên), dùng đúng lệnh đã verify sau — dự án dùng Microsoft.Testing.Platform (`global.json`), không phải VSTest, nên các option filter là của chính `dotnet test`, truyền thẳng, **không** qua dấu `--`:

```powershell
dotnet test tests/CrmCopilot.Tests/CrmCopilot.Tests.csproj --no-build --no-restore --filter-class "CrmCopilot.Tests.Knowledge.LiveRagAcceptanceTests"
```

`--filter-class`/`--filter-method`/`--filter-namespace`/`--filter-query`/`--filter` (cú pháp VSTest) đều là option gốc của `dotnet test` cho project này. `--filter-query` cần đúng 4 segment `/assemblyName/namespace/class/method` — một pattern 3 segment như `*/LiveRagAcceptanceTests/*` sẽ không khớp gì và chạy "Zero tests ran" (exit 5), không phải lỗi cấu hình.

**Rollback**: `git revert` cho code (không `reset --hard`); named volume `crm-copilot-chroma-data` không bao giờ bị xoá; nếu cần dọn collection test, chỉ xoá đúng `crm-copilot-knowledge-livetest` qua Chroma delete-collection endpoint — không đụng tới collection dev mặc định `crm-copilot-knowledge`.

### MCP Server Core Tools (P0-04)

`CrmCopilot.McpServer` chạy MCP Server thật (official C# SDK `ModelContextProtocol.AspNetCore`, Streamable HTTP, `HttpServerSessionMode.Stateless`) tại `http://localhost:5090/mcp`, expose 3 tool read-only: `get_customer`, `get_interactions` (qua `ICrmGateway`/`MockCrmGateway`, P0-02), `search_product_knowledge` (qua `IKnowledgeRetriever`/`KnowledgeRetriever`, P0-03). Không có tool nào ghi dữ liệu; `generate_email` là P0-07.

Chạy đầy đủ web host (không phải hai CLI verb `--ingest-knowledge`/`--query-knowledge`) cần cả ba biến môi trường bắt buộc — `MOCKCRM_API_BASE_URL`, `GEMINI_API_KEY`, `CHROMA_BASE_URL` — cùng cơ chế fail-fast `ValidateOnStart()` đã có từ P0-02/P0-03. `search_product_knowledge` gọi Gemini embedding thật khi được invoke; hai tool còn lại không cần Gemini nhưng vẫn cần `GEMINI_API_KEY` hợp lệ (không rỗng) để host khởi động được, vì `ValidateOnStart` áp dụng cho toàn bộ host, không riêng từng tool.

```powershell
# Terminal 1
dotnet run --project src/CrmCopilot.MockCrmApi --launch-profile http --no-build

# Terminal 2 (Chroma đã chạy theo hướng dẫn P0-03 ở trên)
$env:MOCKCRM_API_BASE_URL = "http://localhost:5100"
$env:GEMINI_API_KEY = "<giá trị thật, chỉ cục bộ, không commit>"
$env:CHROMA_BASE_URL = "http://localhost:8000"
dotnet run --project src/CrmCopilot.McpServer --launch-profile http --no-build

curl http://localhost:5090/health
```

Xác minh tool discovery/tools-call bằng một MCP client thật (khuyến nghị `McpClient`/`HttpClientTransport` từ package `ModelContextProtocol`, cùng API dùng trong `tests/CrmCopilot.Tests/Mcp/McpToolProtocolTests.cs`, hoặc MCP Inspector) — không dùng `curl` thô cho JSON-RPC Streamable HTTP (cần đúng `Accept` header và session framing). Mọi response, kể cả lỗi, đều là một tool result JSON thường (`IsError=false` ở tầng MCP) theo đúng envelope `{status, traceId, sourceIds, data, error}` (docs/07_MCP_TOOL_CONTRACTS.md §3) — không dùng MCP-level `isError` cho lỗi nghiệp vụ.

### AI Host + MCP Client (P0-05)

`CrmCopilot.Web` là AI Host thật: Gemini chat (`gemini-3.5-flash-lite`) chọn tool qua function calling, Host xác thực rồi gọi qua **MCP Client thật** (`ModelContextProtocol` package) tới `CrmCopilot.McpServer` — Web không bao giờ gọi trực tiếp `CrmCopilot.MockCrmApi` hoặc đọc JSON CRM (không có package/config nào cho phép việc đó). Vòng lặp tool bị giới hạn **tối đa 3 lần gọi MCP tool** cho mỗi lượt (Gemini có thể được gọi thêm một lần nữa, lần thứ 4, chỉ để xem kết quả tool thứ 3 và quyết định đã xong hay chưa — không có lần gọi MCP thứ 4). Tool ngoài danh sách 3 tool đã duyệt (`get_customer`, `get_interactions`, `search_product_knowledge`) không bao giờ được đưa vào schema gửi cho Gemini, kể cả khi MCP Server có expose thêm tool khác.

**PII trước khi gọi Gemini:** tin nhắn tiếng Việt thô bị từ chối thẳng (không gọi Gemini/MCP) nếu chứa email, số điện thoại, số tài khoản/CCCD hoặc địa chỉ dạng free-text; tin nhắn có ý định tra cứu khách hàng (chứa từ khóa CRM hoặc một cụm tên viết hoa liên tiếp kiểu "Nguyễn Minh Anh") mà không kèm mã khách hàng hợp lệ (`CUS-####`) cũng bị từ chối, yêu cầu nhập đúng mã khách hàng. Đây là cơ chế **reject-gate best-effort** (không phải masking có placeholder/restore — đó là phạm vi P0-07); ưu tiên từ chối nhầm hơn là để lọt PII. Kết quả tool trả về cho Gemini cũng được tối giản: `get_customer`/`get_interactions` chỉ gửi lại `{status, sourceIds}` (không bao giờ gửi `CustomerDto`/`InteractionDto` — có PII), còn `search_product_knowledge` gửi lại toàn bộ nội dung khớp vì không chứa PII khách hàng.

Chạy đầy đủ 4 process (McpServer/MockCrmApi/Chroma như P0-03/P0-04, cộng Web):

```powershell
# Terminal 1 (P0-02)
dotnet run --project src/CrmCopilot.MockCrmApi --launch-profile http --no-build

# Terminal 2: Chroma đã chạy theo P0-03 (ingest vào collection mặc định crm-copilot-knowledge
# trước khi test luồng RAG — xem "Mandatory live acceptance run" bên dưới)

# Terminal 3 (P0-04)
$env:MOCKCRM_API_BASE_URL = "http://localhost:5100"
$env:GEMINI_API_KEY = "<giá trị thật, chỉ cục bộ, không commit>"
$env:CHROMA_BASE_URL = "http://localhost:8000"
dotnet run --project src/CrmCopilot.McpServer --launch-profile http --no-build

# Terminal 4 (P0-05) — Web cần McpServer đã sẵn sàng: handshake MCP là "lazy" (chỉ xảy ra ở
# request /api/chat đầu tiên, không phải lúc Web start), nên Web start được ngay cả khi McpServer
# chưa lên — nhưng request /api/chat đầu tiên sẽ lỗi MCP_UNAVAILABLE nếu McpServer chưa sẵn sàng.
$env:MCPSERVER_BASE_URL = "http://localhost:5090"
$env:GEMINI_API_KEY = "<giá trị thật, chỉ cục bộ, không commit>"
dotnet run --project src/CrmCopilot.Web --launch-profile http --no-build

curl http://localhost:5081/health
```

Gọi thử `/api/chat` (từ P0-06, `sessionId` là bắt buộc — xem mục "Conversation State (P0-06)" bên dưới):

```powershell
Invoke-RestMethod -Method Post http://localhost:5081/api/chat `
  -Body (@{ message = "Tìm khách hàng CUS-0001"; sessionId = [guid]::NewGuid().ToString() } | ConvertTo-Json) -ContentType "application/json"
```

Response luôn là JSON `{reply, status, sourceIds, toolTrace, data, error}` — status HTTP tương ứng: `200` (success), `404` (not_found), `409` (ambiguous, hoặc đã đạt giới hạn tool-loop), `400` (input/tool-selection/sessionId không hợp lệ — PII_REJECTED, CUSTOMER_ID_REQUIRED, UNKNOWN_TOOL, INVALID_ARGUMENT, v.v.), `503` (MCP/CRM/RAG upstream unavailable), `502` (Gemini/MCP protocol lỗi hoặc lỗi nội bộ).

**Mandatory live acceptance gate** — bắt buộc trước khi báo P0-05 PASS (giống P0-03), tách biệt khỏi test suite mặc định (`dotnet test` chạy offline với `FakeGeminiChatClient` + MCP protocol thật qua fakes, không cần key thật):

```powershell
# 1) Ingest collection mặc định (không set CHROMA_COLLECTION_NAME → mặc định crm-copilot-knowledge)
$env:GEMINI_API_KEY = "<giá trị thật>"
$env:CHROMA_BASE_URL = "http://localhost:8000"
dotnet run --project src/CrmCopilot.McpServer --no-build -- --ingest-knowledge

# Từ P0-06, sessionId là bắt buộc trên mỗi request — dùng cùng một sessionId cho các bước 2-4 để
# minh hoạ conversation state (bước 3/4 có thể bỏ "CUS-0001" và dùng "khách hàng này" thay thế).
$sessionId = [guid]::NewGuid().ToString()

# 2) get_customer thật
Invoke-RestMethod -Method Post http://localhost:5081/api/chat -Body (@{message="Tìm khách hàng CUS-0001"; sessionId=$sessionId} | ConvertTo-Json) -ContentType "application/json"
# kỳ vọng: toolTrace có get_customer/success, data.customer.id == "CUS-0001"

# 3) get_interactions thật
Invoke-RestMethod -Method Post http://localhost:5081/api/chat -Body (@{message="Xem các tương tác gần đây của CUS-0001"; sessionId=$sessionId} | ConvertTo-Json) -ContentType "application/json"
# kỳ vọng: toolTrace có get_interactions/success, data.interactions không rỗng

# 4) search_product_knowledge thật, dùng đúng collection vừa ingest
Invoke-RestMethod -Method Post http://localhost:5081/api/chat -Body (@{message="Khách hàng CUS-0001 quan tâm gửi tiết kiệm, gợi ý sản phẩm phù hợp"; sessionId=$sessionId} | ConvertTo-Json) -ContentType "application/json"
# kỳ vọng: toolTrace có search_product_knowledge/success, sourceIds chứa kb:product:PRD-SAV-006M
```

Ghi lại `toolTrace` đầy đủ (tool name/status/traceId/durationMs) của cả 3 bước làm evidence — không dùng `curl` thô cho `/api/chat` (JSON body cần đúng `Content-Type`).

**Giới hạn đã biết:** heuristic CRM-intent (từ khóa + cụm tên viết hoa liên tiếp) là best-effort — có thể từ chối nhầm câu hỏi hợp lệ (chấp nhận được) và có thể bỏ sót một số cách diễn đạt tên hiếm gặp không kèm từ khóa/không viết hoa liên tiếp; loại bỏ hoàn toàn rủi ro này cần hạ tầng masking/NER thật của P0-07. RM nên tham chiếu khách hàng bằng mã (`CUS-0001`) trong chat, không gõ tên đầy đủ.

### Conversation State (P0-06)

Từ P0-06, `sessionId` (một chuỗi GUID hợp lệ) là **bắt buộc** trên mỗi request `/api/chat` — trình duyệt tự sinh một lần và gửi lại cho mọi lượt của cùng một phiên hội thoại; `sessionId` không bao giờ được server sinh ra. Thiếu, rỗng hoặc không phải GUID hợp lệ → `400 INVALID_ARGUMENT`.

`CrmCopilot.Web` giữ một `IConversationStateStore` in-memory (`ConcurrentDictionary`, docs/02_ARCHITECTURE.md §6), theo đúng `sessionId`, ghi nhớ `CurrentCustomerId` cùng vài trường ngắn hạn khác (không phải transcript). Nhờ đó, sau khi một lượt đã tra cứu thành công một khách hàng (`get_customer`/`get_interactions`), lượt tiếp theo trong cùng phiên có thể dùng cụm như "khách hàng này" mà không cần lặp lại mã khách hàng — Host tự điền `customerId` đã lưu vào tool call trước khi gọi MCP (MCP Server vẫn stateless, luôn nhận `customerId` explicit, không đổi so với P0-04/P0-05). Nếu chưa có khách hàng nào được xác lập trong phiên, một câu hỏi kiểu "khách hàng này" sẽ bị từ chối với `400 CUSTOMER_ID_REQUIRED` thay vì đoán mò.

Reset một phiên (xoá `CurrentCustomerId` và lịch sử ngắn hạn đã lưu, dùng cho tính năng "New conversation" ở P0-08 sau này):

```powershell
Invoke-RestMethod -Method Delete "http://localhost:5081/api/chat/sessions/$sessionId"
```

Luôn trả `204 No Content` (idempotent) nếu `sessionId` là GUID hợp lệ, kể cả khi phiên đó chưa từng tồn tại; `400 INVALID_ARGUMENT` nếu `sessionId` không phải GUID hợp lệ.

**Giới hạn đã biết:**
- Restart `CrmCopilot.Web` sẽ mất toàn bộ conversation state (in-memory, chấp nhận được ở MVP — xem docs/02 §6, docs/11 Q&A).
- Không có TTL/idle-expiry cho một session; một `sessionId` hợp lệ dù bị từ chối ngay từ `InputGuard` (vd. do PII) vẫn tạo một entry rỗng trong store cho đến khi restart hoặc bị `DELETE` tường minh.
- Gọi `DELETE /api/chat/sessions/{sessionId}` đồng thời với một request `/api/chat` đang chạy cho cùng `sessionId` là một race chấp nhận được ở P0-06 (không có khoá đồng bộ) — UI P0-08 cần tự vô hiệu hoá nút Reset/"New conversation" trong khi đang có request chat cho phiên đó.
- `RecentSanitizedMessages` chỉ chống được ba dạng PII cơ học mà `InputGuard` đã nhận diện (email/số điện thoại kiểu VN/chuỗi 9+ chữ số) trước khi lưu — đây là lớp phòng thủ bổ sung (defense-in-depth), không phải bộ phát hiện PII toàn diện.

### Web UI + Trace + Sources (P0-08)

`CrmCopilot.Web` phục vụ một trang Razor tối thiểu tại `http://localhost:5081/` (`Pages/Index.cshtml` + `wwwroot/js/app.js` + `wwwroot/css/app.css`, vanilla JS, không framework frontend). Trang gồm: ô nhập tiếng Việt, khung chat, customer card, danh sách candidate khi trùng tên, danh sách interaction, panel email nháp (subject/body/nhãn cần duyệt/source chips), accordion "Tool trace & sources", và nút "New conversation".

Trình duyệt tự sinh `sessionId` (GUID) một lần và gửi lại ở mọi lượt; nút "New conversation" gọi `DELETE /api/chat/sessions/{sessionId}` rồi sinh `sessionId` mới.

Hai control an toàn của UI:

- **Không dùng `innerHTML` với bất kỳ giá trị nội suy nào** — toàn bộ nội dung do model/CRM sinh ra được gán qua `textContent` (`wwwroot/js/app.js:53`), nên output của model không thể trở thành HTML thực thi được.
- **Khoá thao tác khi đang xử lý** — nút Gửi, nút "New conversation" và ô nhập đều bị `disabled` trong lúc có request đang chạy (`wwwroot/js/app.js:115-117`), chặn double-submit và chặn race giữa reset và một lượt chat đang bay (giới hạn mà P0-06 đã nêu).

Từ P0-08, một tool "kết thúc lượt" (`get_customer`/`get_interactions`/`generate_email`) thành công sẽ kết thúc lượt ngay và reply do Host render deterministic — không hỏi Gemini thêm một lần nữa để viết câu trả lời.

### Acceptance & Demo (P0-09)

Bộ 8 scenario nội bộ của `docs/03_ACCEPTANCE_CRITERIA.md` §6 nằm ở `tests/CrmCopilot.Tests/Acceptance/`.

```powershell
# Lớp D (deterministic, offline) — chạy cả 8 scenario và sinh report
dotnet test tests/CrmCopilot.Tests/CrmCopilot.Tests.csproj --no-build --no-restore `
  --filter-class "CrmCopilot.Tests.Acceptance.AcceptanceScenarioTests"
```

Kết quả ghi ra `TestResults/acceptance-scenarios-offline.md` (đã bị `.gitignore` chặn): bảng 8 dòng kèm `ScenarioAccuracy X/8`, dòng `Quality target 8/8: MET | NOT MET`, và toàn bộ check của từng scenario. Bản tường thuật được commit là `docs/14_ACCEPTANCE_SCENARIO_REPORT.md`.

Vài điểm về thiết kế của bộ này, cần biết trước khi đọc kết quả:

- **Ngưỡng pass là `≥7/8`**, đúng như `docs/03` §6 đã khóa — không phải 8/8. `8/8` được in ra như quality target, không phải pass condition.
- **`Fail` khác `Error`.** `Fail` = scenario đã được đánh giá và có check không đạt (tính vào `X/8`). `Error` = không đánh giá được (harness/transport hỏng) và làm test fail **độc lập** với ngưỡng 7/8 — một scenario không đo được thì con số `X/8` không còn ý nghĩa.
- **T02/T03 được đánh giá ở MCP tool boundary**, không qua `/api/chat`. `InputGuard` (quyết định D7 của P0-05, `src/CrmCopilot.Web/Chat/InputGuard.cs`) **cố ý** từ chối message chứa cụm tên viết hoa liên tiếp mà không kèm `CUS-####`. Chạy hai scenario tra cứu-theo-tên qua chat sẽ fail *đúng theo thiết kế*, nên chúng được đo ở tầng tool — không nới `InputGuard` để "cho pass".
- Scenario dùng `DatasetCrmGateway`: dataset thật đã checked-in + logic search thật của P0-02, nên T02/T03/T05 kiểm chứng hành vi hệ thống chứ không kiểm chứng setup của chính test.

```powershell
# Lớp L (live gate) — cần GEMINI_API_KEY thật + Chroma + MockCrmApi đang chạy
dotnet test tests/CrmCopilot.Tests/CrmCopilot.Tests.csproj --no-build --no-restore `
  --filter-class "CrmCopilot.Tests.Acceptance.LiveAcceptanceScenarioTests"
```

Live gate dùng collection cô lập `crm-copilot-knowledge-livetest`. Thiếu credential thì test báo **Skipped** — và Skipped **không bao giờ** là PASS: theo công thức verdict của P0-09, một live gate chưa chạy giới hạn checkpoint ở mức PARTIAL, và kết quả offline không được mượn để thay thế.

### Secret hygiene

- `.env.example` chỉ là **configuration template** liệt kê tên biến môi trường cần thiết theo từng checkpoint (P0-02/P0-03/P0-05). Repo hiện **không có code hoặc package nào tự động đọc file `.env`** — copy `.env.example` thành `.env` (đã bị `.gitignore` chặn) chỉ tạo một bản ghi chú giá trị cục bộ cho riêng bạn; `dotnet run` sẽ **không** tự đọc được các giá trị đó.
- Để `dotnet run` cục bộ thấy được giá trị, phải export thành environment variable thật trong session PowerShell trước khi chạy, ví dụ:

  ```powershell
  $env:MOCKCRM_API_BASE_URL = "http://localhost:5100"
  dotnet run --project src/CrmCopilot.McpServer --launch-profile http --no-build
  ```

- `.env` sẽ chỉ thực sự được một cơ chế nào đó nạp khi repo bổ sung Docker Compose (`--env-file .env`) hoặc một dotenv loader thật (chưa có ở P0-02) — README sẽ cập nhật lại mục này khi đó.
- Không commit `.env`, `secrets.json`, `appsettings.Development.json`.

