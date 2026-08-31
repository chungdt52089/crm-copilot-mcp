# 01 — Project Decisions

Ngày baseline: 2026-08-18. Trạng thái `Accepted` nghĩa là Claude không được tự thay đổi trong P0.

## 1. Decision register

| ID | Quyết định | Trạng thái | Lý do chính |
| --- | --- | --- | --- |
| PD-001 | Mục tiêu là MVP demo trong 7–9 ngày | Accepted | Ưu tiên luồng chạy được và trình bày rõ MCP + RAG |
| PD-002 | P0 gồm Customer, Interaction và Email Draft dựa trên RAG | Accepted | Trực tiếp giải quyết pain point chính của RM |
| PD-003 | Dùng dataset tổng hợp và Mock CRM API | Accepted | Không còn phụ thuộc CRM Sandbox/dataset Bank A chưa tồn tại |
| PD-004 | Thiết kế `ICrmGateway` để thay adapter sau này | Accepted | Giữ đường nâng cấp HubSpot/CRM thật nhưng không kéo rủi ro vào P0 |
| PD-005 | Dùng .NET 10 + ASP.NET Core Minimal API | Accepted | Phù hợp năng lực hiện có và .NET 10 là LTS tại thời điểm baseline |
| PD-006 | Một MCP Server, official C# SDK, Streamable HTTP | Accepted | MCP thật, dễ quan sát và thuận lợi cho Docker/cloud |
| PD-007 | AI Host quản lý hội thoại; MCP Server stateless | Accepted | Đúng phân vai MCP; tránh nhầm MCP với ContextManager |
| PD-008 | Gemini chat `gemini-3.5-flash-lite` | Accepted | Model GA, hỗ trợ function calling/structured output, có free tier tại baseline |
| PD-009 | Gemini embedding `gemini-embedding-001`, 768D, L2 normalize | Accepted | Text-only RAG đủ dùng, tiết kiệm storage và tái sử dụng kinh nghiệm dự án trước |
| PD-010 | Chroma client/server qua HTTP | Accepted | Dễ chạy Docker; tách vector store khỏi process .NET |
| PD-011 | RAG chỉ index product knowledge và email templates | Accepted | CRM structured data phải truy xuất bằng tool/API, không đưa vào vector DB |
| PD-012 | PII masking là P0 | Accepted | Giảm rủi ro khi dùng Gemini external API; tạo điểm trình bày bảo mật |
| PD-013 | Email chỉ là draft, luôn cần human approval | Accepted | Không có tác vụ gây tác động thật trong MVP |
| PD-014 | 8 test scenario tự xây dựng; pass tối thiểu 7/8 | Accepted | Tạo chỉ số nội bộ đo được; tương đương 87,5% và vượt ngưỡng 85% |
| PD-015 | Conversation state in-memory, ngắn hạn | Accepted | Đủ demo multi-turn; Redis/Postgres không mang lại giá trị tương xứng trong 9 ngày |
| PD-016 | Docker/cloud là bonus sau khi local P0 ổn định | Accepted | Không hy sinh core flow vì đóng gói/deploy |
| PD-017 | Opportunity + call script là P1; Campaign + HubSpot là P2 | Accepted | Bảo vệ tiến độ P0 |
| PD-018 | ChatGPT review plan/evidence; Claude implement một checkpoint/lần | Accepted | Tách planning/review khỏi implementation và kiểm soát scope |
| PD-019 | Test mặc định chạy offline với fake/captured clients; smoke test Gemini/Chroma là opt-in | Accepted | CI/dev không phụ thuộc quota/network nhưng vẫn có evidence integration riêng |
| PD-020 | Auth + role vào P0-12, override out-of-scope §2 | Accepted 2026-08-27 | Mentor yêu cầu bổ sung; auth minh hoạ trên dữ liệu tổng hợp |
| PD-021 | Thêm tool thứ 8 `delete_customer`, `Destructive=true`, override `docs/07 §8` | Accepted 2026-08-27 | Cần một tool nhạy cảm để chứng minh phân quyền có tác dụng |
| PD-022 | Authorization thực thi ở **MCP Server boundary** bằng JWT; Host **không** lọc tool | Accepted 2026-08-27 | Một điểm thực thi duy nhất; ẩn tool ở Host thì lời từ chối không xảy ra để ghi log |
| PD-023 | `delete_customer` xoá **mềm, in-memory**; restart về nguyên trạng | Accepted 2026-08-27 | Giữ golden test và SHA-256 của `data/crm/customers.json` nguyên vẹn |
| PD-024 | Ba role `RM` / `Auditor` / `Admin`; chỉ `Admin` được gọi `delete_customer` | Accepted 2026-08-27 | Đủ ba trường hợp: bình thường, bị hạn chế, có đặc quyền |
| PD-025 | Speech-to-text dùng **Gemini transcribe**; RM xác nhận text trước khi gửi | Accepted 2026-08-27 | Mentor gợi ý; bước xác nhận là control PII, không phải tiện ích UX |
| PD-026 | Log từ chối ghi ra **file log qua redirect console**, không dùng MCP logging notification | Accepted 2026-08-27 | 0 dòng code thêm; đồng thời trả nợ verification C8/B-03 |
| PD-027 | Transcribe dùng **`gemini-3.5-flash`** (không phải `gemini-3.5-flash-lite`), audio `audio/webm;codecs=opus` | Accepted 2026-08-27 | Spike A đo thật: `flash-lite` trả rác ("Hải Phòng", "vợ"); `gemini-3.5-flash` transcribe đúng. Chat/email/call-script **vẫn giữ** `flash-lite` — chỉ transcribe dùng model riêng |

## 2. P0 scope

### In scope

- Giao diện chat tiếng Việt tối thiểu.
- Tìm khách hàng theo ID hoặc tên.
- Xử lý trường hợp không tìm thấy và trùng tên.
- Xem các interaction gần nhất.
- Theo dõi `currentCustomerId` qua câu hỏi nhiều lượt.
- MCP client/server thật và tool trace có thể quan sát.
- RAG trên product knowledge/email template bằng Gemini embedding + Chroma.
- Tạo email draft có subject/body/source IDs.
- PII masking trước Gemini và log.
- Audit event tối thiểu không chứa dữ liệu nhạy cảm.
- 8 test scenario nội bộ, demo runbook và tài liệu kiến trúc.

### Out of scope

- Kết nối Bank A thật.
- HubSpot/Salesforce trong P0.
- Gửi email, gọi điện, cập nhật CRM hoặc hành động ghi dữ liệu.
- ~~Authentication/authorization production-grade.~~ → **Cập nhật 2026-08-27 (PD-020):** auth
  **minh hoạ** trên dữ liệu tổng hợp đã vào P0-12. Production-grade (SSO/OIDC, quản lý user,
  refresh token, revocation) vẫn out of scope.
- Long-term memory, Redis, PostgreSQL cho chat.
- Fine-tuning/training model.
- ML recommendation model, NER/DLP hoàn chỉnh.
- Campaign analytics, opportunity workflow hoàn chỉnh.
- Kubernetes, multi-region, autoscaling, HA.

## 3. Phân biệt ba loại context

| Loại | Nguồn/owner | P0 xử lý thế nào |
| --- | --- | --- |
| Protocol context | MCP client/server | Tool discovery và tool call theo MCP |
| Conversation state | AI Host | `sessionId`, entity IDs đang active, recent sanitized messages |
| Business knowledge | Chroma + knowledge files | Semantic retrieval cho product/template |

MCP **không phải** là nơi mặc định lưu toàn bộ hội thoại. MCP chuẩn hóa cách Host nhận/gọi capability từ Server; ứng dụng tự quyết định cách quản lý lịch sử và trạng thái.

## 4. Dữ liệu và nguồn chân lý

- Customer/Interaction: Mock CRM API là source of truth P0.
- Product/Email template: file knowledge gốc là source of truth; Chroma là index có thể tái tạo.
- Conversation: Host in-memory store là source of truth tạm thời.
- Tool/audit trace: structured application log đã mask.
- Không coi LLM output là source of truth.

## 5. Accuracy nội bộ

```text
Scenario Accuracy = số scenario pass hoàn toàn / 8 × 100%
```

- Mục tiêu: ít nhất 7/8 = 87,5%.
- Đây là **MVP internal accuracy**, không được mô tả là độ chính xác chính thức của Bank A.
- Scenario nào phụ thuộc Gemini phải có assertions về schema, source grounding và PII; không so exact wording.

## 6. Quy tắc thay đổi quyết định

Mọi thay đổi decision phải ghi:

1. Decision ID bị thay đổi.
2. Lý do và evidence.
3. Ảnh hưởng tới acceptance criteria, data, tests và lịch.
4. Kế hoạch migration/re-index nếu có.
5. Phê duyệt của Product Owner sau review của ChatGPT.
