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
- P0-03 — Gemini Embedding & Chroma RAG: **IN PROGRESS** (implementation trên `feature/p0-03-rag-chroma` chưa commit; xem mục 8 "Gemini Embedding & Chroma RAG (P0-03)" và `docs/CHECKPOINT_STATUS.md`)

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

### Secret hygiene

- `.env.example` chỉ là **configuration template** liệt kê tên biến môi trường cần thiết theo từng checkpoint (P0-02/P0-03/P0-05). Repo hiện **không có code hoặc package nào tự động đọc file `.env`** — copy `.env.example` thành `.env` (đã bị `.gitignore` chặn) chỉ tạo một bản ghi chú giá trị cục bộ cho riêng bạn; `dotnet run` sẽ **không** tự đọc được các giá trị đó.
- Để `dotnet run` cục bộ thấy được giá trị, phải export thành environment variable thật trong session PowerShell trước khi chạy, ví dụ:

  ```powershell
  $env:MOCKCRM_API_BASE_URL = "http://localhost:5100"
  dotnet run --project src/CrmCopilot.McpServer --launch-profile http --no-build
  ```

- `.env` sẽ chỉ thực sự được một cơ chế nào đó nạp khi repo bổ sung Docker Compose (`--env-file .env`) hoặc một dotenv loader thật (chưa có ở P0-02) — README sẽ cập nhật lại mục này khi đó.
- Không commit `.env`, `secrets.json`, `appsettings.Development.json`.

