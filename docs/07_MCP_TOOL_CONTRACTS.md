# 07 — MCP Tool Contracts

## 1. Nguyên tắc

- Một MCP Server P0.
- Tools có tên ổn định, mô tả rõ khi nào dùng/không dùng.
- Input nhỏ, explicit, không phụ thuộc hidden server session.
- Output structured JSON và có source IDs.
- P0 tools read-only hoặc generate draft không side effect.
- Host chịu conversation state; luôn resolve entity rồi truyền ID cho tool.

## 2. Tool list P0

| Tool | Mục đích | Phụ thuộc |
| --- | --- | --- |
| `get_customer` | Tìm một hoặc nhiều candidate customer | `ICrmGateway` |
| `get_interactions` | Lấy lịch sử interaction mới nhất | `ICrmGateway` |
| `search_product_knowledge` | Semantic search product/template knowledge | Embedding + Chroma |
| `generate_email` | Tạo email draft grounded + masked | CRM gateway + RAG + Gemini |

## 3. Common result fields

Mọi tool result có:

```json
{
  "status": "success|not_found|ambiguous|error",
  "traceId": "...",
  "sourceIds": [],
  "data": {},
  "error": null
}
```

`error` khi có:

```json
{
  "code": "INVALID_ARGUMENT|NOT_FOUND|AMBIGUOUS_MATCH|UPSTREAM_UNAVAILABLE|RAG_UNAVAILABLE|MODEL_ERROR|INTERNAL_ERROR",
  "message": "Thông báo an toàn bằng tiếng Việt",
  "retryable": false
}
```

Không trả stack trace, URL chứa key, raw Gemini payload hoặc internal exception.

## 4. `get_customer`

Mô tả tool:

> Tìm hồ sơ khách hàng theo customer ID hoặc chuỗi tên. Dùng trước khi lấy interaction hoặc tạo draft nếu chưa có customer ID chính xác. Không tự chọn khi có nhiều candidate.

Input:

```json
{
  "customerId": "CUS-0001",
  "query": null
}
```

Rules:

- Phải có đúng một trong `customerId` hoặc `query`.
- `customerId` exact lookup.
- `query` có thể trả một hoặc nhiều candidate.

Success data:

```json
{
  "customer": {
    "id": "CUS-0001",
    "fullName": "Nguyễn Minh Anh",
    "segment": "Priority",
    "city": "Hà Nội",
    "status": "Active",
    "synthetic": true
  }
}
```

MCP result cho AI nên mặc định data-minimized; UI cần profile chi tiết có thể nhận phần synthetic contact qua trusted local response path, không đưa toàn bộ cho Gemini.

## 5. `get_interactions`

Mô tả tool:

> Lấy các tương tác gần nhất của một customer ID đã xác định. Không tìm customer theo tên.

Input:

```json
{
  "customerId": "CUS-0001",
  "limit": 5
}
```

Rules:

- `customerId` required.
- `limit` default 5, min 1, max 20.
- Sort `occurredAtUtc` descending.

Output `sourceIds` chứa từng `crm:interaction:<id>` được trả.

## 6. `search_product_knowledge`

Mô tả tool:

> Tìm product knowledge và email guidance bằng semantic search. Chỉ dùng cho kiến thức sản phẩm/template; không dùng để tìm customer hoặc interaction.

Input:

```json
{
  "query": "Khách hàng ưu tiên an toàn, muốn gửi tiết kiệm 6 tháng",
  "topK": 3,
  "documentTypes": ["product", "email_template"]
}
```

Rules:

- `query` required, tối đa 1000 ký tự (giới hạn cứng ở tool, không phải ví dụ minh hoạ — nhỏ hơn giới hạn phòng vệ 2000 ký tự nội bộ của `KnowledgeRetriever`).
- `topK` default 3, phạm vi 1-5 (nhỏ hơn giới hạn phòng vệ 1-20 nội bộ của `KnowledgeRetriever`).
- Filter chỉ nhận allowlisted document types (`product`, `email_template`).
- Không trả document nếu distance vượt quá threshold đã hiệu chỉnh ở P0-03 (`KnowledgeRetrievalOptions.MaxDistance`, không đổi ở P0-04); threshold là config và có test.

Output item (đã sửa ở P0-04 cho khớp đúng data model P0-03 — bản gốc ở trên có `title`/`score` không tồn tại trong `KnowledgeSourceMetadata`/`KnowledgeMatch` thật; P0-04 không tự bịa `title` và không tự quy đổi Chroma distance thành similarity score):

```json
{
  "sourceId": "kb:product:PRD-SAV-006M",
  "documentType": "product",
  "productCode": "PRD-SAV-006M",
  "content": "...",
  "distance": 0.47
}
```

`distance` là khoảng cách Chroma thô cho metric đã cấu hình (l2) — **lower is better** (càng nhỏ càng liên quan), không phải một "similarity score"/"accuracy" tự chế. Không có field `title` — `KnowledgeSourceMetadata` (P0-03) không có field hiển thị tên; nếu cần, đây là một thay đổi schema riêng, ngoài phạm vi P0-04.

Không hard-code interpretation score là “accuracy”. Score chỉ dùng ranking/threshold nội bộ.

## 7. `generate_email`

Mô tả tool:

> Tạo bản nháp email tiếng Việt cho một khách hàng đã xác định, dựa trên interactions và product/template knowledge được retrieve. Không gửi email. Luôn yêu cầu RM duyệt.

Input:

```json
{
  "customerId": "CUS-0001",
  "objective": "Follow-up nhu cầu gửi tiết kiệm kỳ hạn 6 tháng",
  "tone": "professional_warm",
  "productCode": null
}
```

Rules:

- `customerId` và `objective` required.
- `tone` allowlist: `professional`, `professional_warm`, `concise`.
- Nếu `productCode` có giá trị, vẫn phải retrieve và validate source tương ứng.
- Fetch customer + recent interactions.
- Minimize/mask context.
- Retrieve product/template top-k.
- Nếu không có evidence đủ: trả `not_found`/`RAG_UNAVAILABLE`, không sinh email quảng bá chung chung.
- Generate structured output và validate.

Success data:

```json
{
  "draft": {
    "subject": "Thông tin tham khảo về tiền gửi kỳ hạn 6 tháng",
    "body": "Kính gửi Anh/Chị Nguyễn Minh Anh, ...",
    "suggestedProductCode": "PRD-SAV-006M",
    "sourceIds": [
      "kb:product:PRD-SAV-006M",
      "kb:email-template:TPL-EMAIL-MATURITY-01",
      "crm:interaction:INT-0001"
    ],
    "requiresHumanApproval": true,
    "piiMaskSummary": {
      "maskedFieldTypes": ["name", "email", "phone", "accountReference"]
    }
  }
}
```

`body` được restore tên tổng hợp local sau model output; source IDs phải là subset của evidence thật.

## 8. Tool annotations/safety

Nếu SDK hỗ trợ annotation phù hợp, đánh dấu read-only cho ba tool đầu; `generate_email` là non-destructive draft. Không expose tool `send_email` trong P0.

Host chỉ cho model thấy allowlist tool P0. Mọi tool call phải validate lại ở server; không tin arguments do model tạo.

## 9. Contract tests bắt buộc

- Tool discovery có đúng tên/schema.
- Required field thiếu → `INVALID_ARGUMENT`.
- Customer exact/ambiguous/not-found.
- Interaction limit/sort/customer isolation.
- Knowledge allowlist/topK/empty retrieval.
- Email success/no evidence/Gemini invalid schema/PII capture.
- Tool result không serialize exception hoặc secret.

## 10. P1/P2 tools

- P1: `get_opportunities`, `generate_call_script`.
- P2: `get_campaigns`.

Không thêm tool chỉ để đủ số lượng. Mỗi tool mới cần contract, test và demo value rõ.

