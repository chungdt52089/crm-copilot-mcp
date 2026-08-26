# 14 — Acceptance Scenario Report (P0-09, cập nhật ở P0-10)

Bộ 8 scenario là deliverable của P0-09. Bản cập nhật gần nhất (**2026-08-26**) ghi kết quả chạy lại
trên `feature/p0-10-complete-mcp-tool-list` sau khi P0-10 nâng tool list từ 4 lên 7 — lớp L và lớp B
ở §4/§5 là evidence của đợt đó.

Bản tường thuật được commit của bộ 8 scenario nội bộ định nghĩa ở `docs/03_ACCEPTANCE_CRITERIA.md` §6.
Số liệu điền từ output thật của `AcceptanceScenarioTests` / `LiveAcceptanceScenarioTests`; báo cáo
máy sinh ra nằm ở `TestResults/acceptance-scenarios-offline.md` và `TestResults/acceptance-scenarios-live.md`
(thư mục `TestResults/` bị `.gitignore` chặn nên không commit).

```text
ScenarioAccuracy = PassedScenarios / 8 × 100%
Ngưỡng đã khóa   = 7/8 = 87.5%   (docs/03 §6)
Quality target   = 8/8            (mục tiêu, KHÔNG phải pass condition)
```

## 1. Ba lớp evidence — không thay thế lẫn nhau

| Lớp | Nguồn | Chứng minh được | **Không** chứng minh được |
| --- | --- | --- | --- |
| **D** — deterministic offline | `AcceptanceScenarioTests` — MCP protocol thật in-memory, Gemini/CRM/knowledge là fake | Contract, schema, error code, resolution theo state, session isolation, PII gate, thứ tự/limit | Bất cứ điều gì về model thật, embedding thật, retrieval ranking thật |
| **L** — live gate | `LiveAcceptanceScenarioTests` — Gemini thật + Chroma thật + MockCrmApi thật | Grounding thật, PII thật không rời máy | Trải nghiệm UI |
| **B** — browser demo | 3 lần chạy tay qua `http://localhost:5081` | Luồng đầu-cuối, trace/source hiển thị, tính ổn định | Assertion máy móc |

Quy tắc: một scenario chỉ được đánh dấu đạt ở lớp nào bằng đúng evidence của lớp đó. Live gate
báo **Skipped** thì ghi `NOT RUN`, **không** ghi PASS và **không** mượn kết quả lớp D thay thế.

## 2. Kết quả lớp D (deterministic offline)

- Ngày chạy gần nhất: **2026-08-26** trên `feature/p0-10-complete-mcp-tool-list` (base `c7bcbd6`,
  thay đổi chưa commit) — chạy lại sau khi P0-10 thêm 3 MCP tool, để chứng minh không regression.
  Lần chạy đầu tiên của bộ này là 2026-08-25 trên `feature/p0-09-deployment-readiness` (base
  `a91c041`), cũng cho 8/8.
- Lệnh: `dotnet test tests/CrmCopilot.Tests/CrmCopilot.Tests.csproj --no-build --no-restore --filter-class "CrmCopilot.Tests.Acceptance.AcceptanceScenarioTests"`
- **ScenarioAccuracy: 8/8** · Failed: 0 · Errored (không đánh giá được): 0 · Tổng số check: **75**
- Bối cảnh suite rộng hơn cùng đợt 2026-08-26: `dotnet build CrmCopilot.slnx` **succeeded**; offline
  đầy đủ **508 total / 503 succeeded / 0 failed / 5 skipped** (đúng 5 live opt-in, không phải
  regression); targeted **83 total / 82 succeeded / 0 failed / 1 skipped** (skip là
  `LiveRagAcceptanceTests` khi chưa cấp live credential); `tools/list` đúng **7 tool**;
  `git diff --check` exit 0.
- **Quality target 8/8: MET**

| ID | Scenario | Boundary | Lớp | Outcome | Checks | ms |
| --- | --- | --- | --- | --- | --- | --- |
| T01 | Lookup `CUS-0001` theo ID | `POST /api/chat` | D | PASS | 10 | 961 |
| T02 | Lookup theo tên duy nhất | MCP tool `get_customer` | D | PASS | 4 | 23 |
| T03 | Lookup theo tên trùng | MCP tool `get_customer` | D | PASS | 4 | 23 |
| T04 | Customer không tồn tại | `POST /api/chat` | D | PASS | 9 | 62 |
| T05 | Interactions của `CUS-0001` | `POST /api/chat` | D | PASS | 13 | 79 |
| T06 | Multi-turn "khách hàng này" | `POST /api/chat` | D | PASS | 10 | 56 |
| T07 | Email draft RAG | MCP tool `generate_email` | D | PASS | 13 | 37 |
| T08 | Safety / resilience | `POST /api/chat` + MCP | D | PASS | 12 | 194 |

### Vài check đáng chú ý

- **T05** không chỉ kiểm sort/limit: nó chốt đúng chuỗi `[INT-0001, INT-0002, INT-0003]` newest-first
  theo dataset, và chốt rằng phần tử **đầu tiên** là interaction tiết kiệm canonical (`INT-0001`,
  `type=Call`, `outcome=FollowUpRequired`, summary chứa cả "tiền gửi" lẫn "6 tháng") — chính là nhu
  cầu mà bước soạn email của demo dựa vào (`docs/03` §7).
- **T07** chứng minh `requiresHumanApproval` là **server ép**: fixture cố tình cho model trả
  `false`, kết quả cuối vẫn phải là `true`. Nếu tool chuyển sang tin field của model, check này fail.
- **T07** chốt "không bịa lãi suất" một cách tuyệt đối: corpus knowledge có **0 ký tự `%`**
  (`grep -c '%' data/knowledge/*.json` = 0) và mọi lần "Lãi suất" xuất hiện đều là ràng buộc *cấm tự
  suy diễn*, nên bất kỳ `%` nào trong draft đều là fabrication — không cần đánh giá mờ.
- **T08** chốt PII gate chặn **trước** Gemini bằng `CallCount == 0`, tức chứng minh Gemini chưa từng
  được gọi, chứ không chỉ "response bị từ chối".

## 3. Ghi chú kiến trúc — T02/T03 đo ở tool boundary (không phải defect)

T02 và T03 được đánh giá bằng `tools/call` tới `get_customer`, **không** qua `POST /api/chat`.

Lý do: `InputGuard` (quyết định D7 của P0-05, `src/CrmCopilot.Web/Chat/InputGuard.cs`) **cố ý** từ
chối message chứa cụm tên viết hoa liên tiếp mà không kèm mã `CUS-####`, trả `CUSTOMER_ID_REQUIRED`.
Đó là một control PII đã được duyệt, không phải lỗi. Chạy hai scenario tra-cứu-theo-tên qua chat sẽ
fail *đúng theo thiết kế*.

Cách xử lý đúng là đo chúng ở tầng mà nghiệp vụ tra cứu theo tên thực sự sống — MCP tool — và **không**
nới `InputGuard` để lấy điểm. RM vẫn nên tham chiếu khách hàng bằng mã trong chat.

## 4. Kết quả lớp L (live gate)

- Ngày chạy: **2026-08-26** (trên `feature/p0-10-complete-mcp-tool-list`, thay đổi chưa commit)
- Điều kiện: `GEMINI_API_KEY` thật (chỉ nằm trong terminal của Product Owner, không đọc/in/ghi log ở
  bất kỳ đâu), Chroma + MockCrmApi + McpServer + Web đang chạy
- Health preflight **4/4 HTTP 200**: `:5100/health`, `:5090/health`, `:5081/health`,
  `:8000/api/v2/heartbeat`

| ID | Scenario | Lớp | Outcome |
| --- | --- | --- | --- |
| T07 | Email draft RAG (live) | L | **PASS** |
| T08 | Safety / resilience (live) | L | **PASS** |

Toàn bộ live gate của repo, chạy cùng đợt:

| Live test class | Kết quả |
| --- | --- |
| `LiveRagAcceptanceTests` | 1/1 PASS (exit code 0) |
| `LiveCallScriptGenerationAcceptanceTests` | 2/2 PASS |
| `LiveAcceptanceScenarioTests` (T07/T08) | 1/1 PASS |
| `LiveEmailGenerationAcceptanceTests` | 1/1 PASS |
| **Tổng** | **5/5 PASS · 0 failed · 0 skipped** |

Không còn live test nào ở trạng thái Skipped trong đợt này — nên không có mục nào phải ghi `NOT RUN`.
Trong bộ **offline** mặc định, 5 test này vẫn báo Skipped đúng như thiết kế opt-in; đó là lý do lượt
offline có `5 skipped` mà không phải regression.

### 4.1 Contract greeting ở lớp L — hai nhánh đều hợp lệ

Live gate **không** yêu cầu email luôn chứa tên khách hàng. Theo contract P0-07
(`docs/08_RAG_EMAIL_AND_PII_SPEC.md` §6, hiện thực ở `EmailTools.cs:552-557`) có đúng hai nhánh
hợp lệ, và `LiveAcceptanceScenarioTests` assert dạng tuyển:

| Nhánh | Điều kiện | Kết quả bắt buộc trong body |
| --- | --- | --- |
| A | Model **giữ** `{{CUSTOMER_NAME}}` | Được restore ở local thành đúng `Nguyễn Minh Anh` |
| B | Model **làm mất/biến đổi** placeholder | `EmailTools` chèn lời chào trung tính `Kính gửi Anh/Chị,` |

Dù rơi vào nhánh nào, cả bốn điều kiện sau vẫn phải đúng: không còn placeholder thô trong
subject/body; subject và body là tiếng Việt **có dấu**; **không** giá trị PII thô nào rời máy tới
Gemini; không bịa lãi suất/số liệu ngoài evidence; và `requiresHumanApproval = true`.

Siết live gate thành "luôn phải có tên" sẽ biến một fallback đã được đặc tả và **đúng** thành
failure — nên không làm. Khẳng định chặt "placeholder vào ⇒ tên ra" được chứng minh ở lớp D, nơi
output của model do fixture kiểm soát.

## 5. Kết quả lớp B (browser demo) và verdict

- Ngày chạy: **2026-08-26** · **3 lượt liên tiếp, tất cả PASS**
- Mỗi lượt bắt đầu bằng **New conversation**; **không** build lại và **không** restart service giữa
  các lượt (điều kiện để chuỗi được tính là liên tiếp)

Xác nhận trong cả ba lượt:

| Hạng mục | Kết quả |
| --- | --- |
| Customer lookup | hoạt động |
| "khách hàng này" giữ đúng conversation state | đúng |
| Interaction history | hoạt động |
| Email generation | thành công |
| Call-script generation | thành công |
| RM approval hiển thị | có |
| Lỗi tool / internal message rò rỉ | không có |
| Thời gian generate | < 15 giây |
| Source chips | **không** chứa retrieval candidate mà draft không dùng |
| Tiếng Việt có dấu | đúng |
| Placeholder thô còn sót | không |
| Bịa lãi suất | không |
| Bịa danh tính/liên hệ RM | không |

### 5.1 PII và secret

Tool trace chỉ hiển thị **tên các trường** đã ẩn — `name`, `email`, `phone`, `accountReference` —
không có giá trị thô. Secret scan không phát hiện Gemini API key trong tracked corpus.

### 5.2 UI error contract

Internal error code giữ nguyên `CUSTOMER_ID_INVALID`. Public/UI message là:

```text
Mã khách hàng không hợp lệ. Vui lòng kiểm tra đúng định dạng và thử lại.
```

Message này **không** công khai format convention, regex, `INVALID_ARGUMENT`, tên validator, stack
trace hay bất kỳ chi tiết triển khai nào. Input sai định dạng (`CS-0002`, `CS-0003`, `CS-0004`) bị
từ chối sớm, **không** được tự sửa thành mã khách hàng hợp lệ, và **không** làm thay đổi
conversation state.

Cảnh báo `Đang hiển thị dữ liệu của khách hàng CUS-0002.` khi màn hình vẫn giữ dữ liệu hợp lệ trước
đó là **hành vi UI đúng**, không phải rò rỉ: hiển thị một ID khách hàng hợp lệ trong dữ liệu hoặc
trong cảnh báo dữ liệu cũ khác với việc công khai quy ước định dạng trong thông báo lỗi.

### 5.3 Verdict

Verdict cuối chỉ là **PASS** khi đồng thời: build sạch; offline suite `failed == 0` với đúng tập skip
đã liệt kê; 0 scenario `Error`; `ScenarioAccuracy ≥ 7/8`; mandatory live gate đã CHẠY và PASS;
3 browser run PASS liên tiếp; secret scan sạch; tài liệu đã đồng bộ.

Tính tới 2026-08-26, mọi điều kiện trên đã có evidence (xem row P0-10 ở `docs/CHECKPOINT_STATUS.md`
§1). Trạng thái checkpoint vì vậy là **READY_FOR_FINAL_REVIEW** — evidence đầy đủ, **chưa** có final
review độc lập. Tài liệu này **không** tự tuyên bố PASS; verdict thuộc về reviewer.
