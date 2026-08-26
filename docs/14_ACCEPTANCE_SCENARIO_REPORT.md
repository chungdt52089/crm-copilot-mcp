# 14 — Acceptance Scenario Report (P0-09)

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

- Ngày chạy: **2026-08-25**
- Commit gốc: `a91c041` (develop HEAD) + thay đổi P0-09 trên `feature/p0-09-deployment-readiness`
- Lệnh: `dotnet test tests/CrmCopilot.Tests/CrmCopilot.Tests.csproj --no-build --no-restore --filter-class "CrmCopilot.Tests.Acceptance.AcceptanceScenarioTests"`
- **ScenarioAccuracy: 8/8** · Failed: 0 · Errored (không đánh giá được): 0 · Tổng số check: **75**
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

> **NOT RUN** tại thời điểm viết. Cần `GEMINI_API_KEY` thật, Chroma và MockCrmApi đang chạy.
> Theo công thức verdict (§5), live gate chưa chạy ⇒ verdict tối đa là **PARTIAL**.

| ID | Scenario | Lớp | Outcome |
| --- | --- | --- | --- |
| T07 | Email draft RAG (live) | L | *(chưa chạy)* |
| T08 | Safety / resilience (live) | L | *(chưa chạy)* |

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

> **NOT RUN** tại thời điểm viết. Bảng evidence Run 1–3 và quy tắc reset chuỗi nằm ở §8 của plan P0-09.

Verdict cuối chỉ là **PASS** khi đồng thời: build sạch; offline suite `failed == 0` với đúng tập skip
đã liệt kê; 0 scenario `Error`; `ScenarioAccuracy ≥ 7/8`; **cả ba** mandatory live gate đã CHẠY và
PASS; 3 browser run PASS liên tiếp; secret scan và runtime-log PII scan sạch; tài liệu đã đồng bộ.
Chi tiết công thức ở §9 của plan P0-09 và evidence row P0-09 trong `docs/CHECKPOINT_STATUS.md`.
