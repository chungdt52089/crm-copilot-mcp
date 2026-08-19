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
- P0-01 — Repository & Solution Scaffold: **IN PROGRESS** (solution/health scaffold dựng xong; xem mục 8 và `docs/CHECKPOINT_STATUS.md`)

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

### Secret hygiene

- Copy `.env.example` thành `.env` (đã bị `.gitignore` chặn) và điền giá trị thật khi checkpoint tương ứng yêu cầu (P0-02/P0-03/P0-05).
- Không commit `.env`, `secrets.json`, `appsettings.Development.json`.

