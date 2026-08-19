# 03 — Acceptance Criteria

## 1. MVP Definition of Done

MVP được coi là demo-ready khi đồng thời:

- Local startup có hướng dẫn tái lập được.
- UI chat tiếng Việt chạy được luồng chuẩn đầu-cuối.
- MCP Host discover và gọi MCP tools thật; UI hoặc log demo được tool trace.
- Customer và Interaction đọc từ Mock CRM API qua `ICrmGateway`.
- Câu hỏi nhiều lượt dùng đúng `currentCustomerId`.
- Email draft dùng evidence retrieve từ Chroma.
- PII được mask trước Gemini và audit log.
- Email output có schema, source IDs và `requiresHumanApproval = true`.
- Không tìm thấy dữ liệu thì trả lời không tìm thấy, không bịa.
- Tối thiểu 7/8 scenario nội bộ pass.
- Không có secret trong repository.

## 2. Functional acceptance criteria

| ID | Tiêu chí | Evidence bắt buộc |
| --- | --- | --- |
| AC-F01 | Tìm đúng customer theo ID | HTTP/tool result + UI screenshot hoặc test output |
| AC-F02 | Tìm theo tên; xử lý không thấy/trùng tên | Integration tests hoặc recorded manual steps |
| AC-F03 | Lấy interactions đúng customer, sắp xếp mới nhất trước | Assertions trên ID và timestamp |
| AC-F04 | “Khách hàng này” dùng customer của turn trước | State test + tool args trace |
| AC-F05 | Host gọi tool qua MCP, không gọi CRM trực tiếp | MCP trace/log + code boundary review |
| AC-F06 | RAG trả product/template source trong top-k | Retrieval test + source IDs |
| AC-F07 | Email draft có subject/body và dựa trên source | Structured output + cited source IDs |
| AC-F08 | Email không tự gửi và luôn cần RM duyệt | `requiresHumanApproval=true`; không tồn tại send endpoint/tool |
| AC-F09 | UI hiển thị tool trace và sources ở mức dễ demo | Manual verification |
| AC-F10 | Khi thiếu dữ liệu, hệ thống nói không tìm thấy | Negative test; không có fabricated ID/product |

## 3. MCP acceptance criteria

| ID | Tiêu chí pass |
| --- | --- |
| AC-M01 | MCP Server khởi động bằng official C# SDK và expose đúng tool list P0 |
| AC-M02 | MCP Host tạo client, list/discover tools và gọi `tools/call` thành công |
| AC-M03 | Tool input có JSON schema và validate required fields |
| AC-M04 | Tool output có result/error code ổn định; không trả exception text thô |
| AC-M05 | MCP Server không giữ conversation state; tool nhận customer ID explicit |
| AC-M06 | Mỗi tool call có `traceId`, tool name, duration, success/error; log đã mask |

## 4. RAG acceptance criteria

| ID | Tiêu chí pass |
| --- | --- |
| AC-R01 | Ingestion dùng `RETRIEVAL_DOCUMENT`, query dùng `RETRIEVAL_QUERY` |
| AC-R02 | Vector 768 chiều được L2 normalize trước khi lưu/query |
| AC-R03 | Collection có metadata `sourceId`, `documentType`, `productCode`, `language`, `embeddingModel`, `embeddingDimension` |
| AC-R04 | Query canonical trả đúng knowledge cần thiết trong top 3 |
| AC-R05 | Email output chỉ cite source thực sự có trong retrieved context |
| AC-R06 | Khi Chroma/knowledge không sẵn sàng, hệ thống không giả vờ đã dùng RAG |

## 5. PII/security acceptance criteria

| ID | Tiêu chí pass |
| --- | --- |
| AC-S01 | Raw email, phone, account number, CCCD và address không xuất hiện trong captured Gemini request |
| AC-S02 | Raw PII không xuất hiện trong application logs/audit evidence |
| AC-S03 | Name được thay placeholder trước Gemini; có thể restore local cho UI với synthetic data |
| AC-S04 | API key chỉ lấy từ env/User Secrets và không có trong git diff |
| AC-S05 | Retrieved text không thể ghi đè system instruction trong test prompt-injection cơ bản |
| AC-S06 | Model output được parse/validate theo schema trước khi hiển thị |

## 6. 8 scenario đánh giá nội bộ

Accuracy tính theo scenario pass hoàn toàn, không tính từng assertion nhỏ.

| ID | Scenario | Điều kiện pass chính |
| --- | --- | --- |
| T01 | Lookup `CUS-0001` | Đúng profile và source `crm:customer:CUS-0001` |
| T02 | Lookup theo tên duy nhất | Trả đúng một customer |
| T03 | Lookup theo tên trùng | Không tự chọn; trả candidates và yêu cầu ID |
| T04 | Customer không tồn tại | Trả `NOT_FOUND`; không sinh thông tin |
| T05 | Interactions của `CUS-0001` | Chỉ đúng customer, newest-first, giới hạn đúng |
| T06 | Multi-turn “khách hàng này” | Tool call sau dùng `CUS-0001` từ state |
| T07 | Email draft RAG | Đúng schema; source IDs hợp lệ; grounded product/template; cần duyệt |
| T08 | Safety/resilience | PII không lọt Gemini/log và ít nhất một upstream failure được xử lý controlled |

```text
ScenarioAccuracy = PassedScenarios / 8 × 100%
Target = 7/8 = 87.5%
```

T01–T06 nên deterministic. T07/T08 cho phép wording khác nhau nhưng schema, source, PII và control behavior phải deterministic.

Test suite mặc định dùng fake/captured Gemini/embedding clients và test doubles phù hợp để không phụ thuộc quota/network. Live integration smoke tests là opt-in và phải báo riêng environment, thời điểm và kết quả.

## 7. Canonical demo assertions

Với `CUS-0001`:

- Có một interaction gần đây mô tả nhu cầu gửi tiết kiệm an toàn.
- Knowledge retrieval phải đưa `PRD-SAV-006M` vào top 3 cho objective chuẩn.
- Email draft không được đưa lãi suất/điều kiện ngoài knowledge source.
- Draft có placeholder/name được restore local, subject và body tiếng Việt.
- UI hiện ít nhất source product và template.

Các giá trị canonical được đóng băng khi hoàn tất P0-02.

## 8. Non-functional mục tiêu MVP

Đây là mục tiêu demo, không phải SLA production:

| Hạng mục | Mục tiêu |
| --- | --- |
| Customer/interaction tool không gọi Gemini | p95 local < 2 giây |
| Email generation | Hoàn tất < 15 giây trong điều kiện API bình thường |
| Startup | Một người khác chạy được theo README trong ≤ 15 phút |
| Stability | Chạy luồng demo chuẩn 3 lần liên tiếp không restart |
| Observability | Mỗi request có correlation/trace ID xuyên Host → MCP → CRM/RAG |

## 9. Checkpoint review verdict

ChatGPT dùng ba verdict:

- **PASS**: đủ acceptance + evidence, được mở checkpoint kế tiếp.
- **PASS WITH FOLLOW-UP**: không ảnh hưởng core; follow-up được ghi backlog rõ.
- **REWORK/BLOCKED**: thiếu evidence, sai decision hoặc core path chưa ổn; không mở checkpoint sau.
