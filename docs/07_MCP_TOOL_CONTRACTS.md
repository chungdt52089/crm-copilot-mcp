# 07 — MCP Tool Contracts

## 1. Nguyên tắc

- Một MCP Server P0.
- Tools có tên ổn định, mô tả rõ khi nào dùng/không dùng.
- Input nhỏ, explicit, không phụ thuộc hidden server session.
- Output structured JSON và có source IDs.
- P0 tools read-only hoặc generate draft không side effect.
- Host chịu conversation state; luôn resolve entity rồi truyền ID cho tool.

## 2. Tool list

Bốn tool đầu là baseline P0. Ba tool cuối được Product Owner phê duyệt kéo lên P0-10 (2026-08-26),
thay cho việc để lại backlog P1/P2 như §10 dự kiến ban đầu — xem §10.

| Tool | Mục đích | Phụ thuộc | Contract |
| --- | --- | --- | --- |
| `get_customer` | Tìm một hoặc nhiều candidate customer | `ICrmGateway` | §4 |
| `get_interactions` | Lấy lịch sử interaction mới nhất | `ICrmGateway` | §5 |
| `search_product_knowledge` | Semantic search product/template knowledge | Embedding + Chroma | §6 |
| `generate_email` | Tạo email draft grounded + masked | CRM gateway + RAG + Gemini | §7 |
| `get_opportunities` | Lấy cơ hội bán của một customer đã xác định | `ICrmGateway` | §11 |
| `get_campaigns` | Lấy chiến dịch mà một customer thuộc diện tham gia | `ICrmGateway` | §12 |
| `generate_call_script` | Tạo call-script draft grounded + masked | CRM gateway + RAG + Gemini | §13 |

**Cập nhật 2026-08-27 (P0-14):** thêm tool thứ tám `delete_customer` — xem §14.

`tools/list` phải trả về **đúng tám tool**, không thừa không thiếu. Bảy tool ở bảng trên giữ
`ReadOnly=true`, `Destructive=false`; riêng `delete_customer` là `ReadOnly=false`,
`Destructive=true`.

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

- Phải có đúng một trong `customerId` hoặc `query`. Contract này **không** được nới để chấp nhận cả hai.
- `customerId` exact lookup, và phải khớp `CustomerIdFormat` (`CUS-####`). Sai định dạng ⇒
  `INVALID_ARGUMENT`, **không** phải `NOT_FOUND` — một mã đúng định dạng nhưng không tồn tại mới là
  `NOT_FOUND`. Gộp hai trường hợp này khẳng định sai với người gọi rằng mã họ gõ là hợp lệ.
- `query` là **tên khách hàng**, không phải identifier — quy tắc định dạng trên không áp cho nó;
  natural-language query tiếp tục hoạt động bình thường.
- `query` có thể trả một hoặc nhiều candidate.

Phạm vi: quy tắc định dạng ở trên áp cho `get_customer` (P0-10). Các tool khác nhận `customerId`
hiện vẫn để gateway trả `NOT_FOUND` cho mã sai định dạng — cố ý giữ nguyên trong đợt sửa này để
không đụng các luồng email/call-script đã verified.

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

### 7.1 Cấu trúc `body` (P0-10)

`body` phải là **văn bản thuần**, gồm các đoạn theo thứ tự sau, mỗi đoạn cách nhau bằng một dòng
trống (`\n\n`):

1. lời chào;
2. đoạn dẫn nhập;
3. nội dung sản phẩm;
4. lời kêu gọi hành động;
5. lời kết và chữ ký.

Ràng buộc kiểm được ở server: tối thiểu **3 đoạn** cách nhau bằng dòng trống, và **không** chứa thẻ
HTML hay Markdown (`**`, `#`). Vi phạm đi qua đúng cơ chế retry một lần đã có. Dấu `-` đầu dòng
**không** bị coi là Markdown — đó là văn phong tiếng Việt bình thường.

Model **không** được bịa tên, chức danh, email hay số điện thoại của RM/ngân hàng trong chữ ký —
những dữ liệu đó không có trong evidence; dùng lời kết trung tính và để RM tự bổ sung.

UI phải bảo toàn newline (`white-space: pre-wrap`).

### 7.2 `sourceIds` (P0-10)

`sourceIds` chỉ chứa **nguồn draft thực sự trích dẫn** (`usedSourceIds ∩ evidence được phép`). Một
product được retrieve nhưng draft không dùng **không** được liệt kê. Call-script knowledge không bao
giờ xuất hiện ở đây: `generate_email` retrieve với filter `[product]`/`[email_template]` tường minh.

## 8. Tool annotations/safety

Bảy tool đầu ở §2 đều `ReadOnly=true`, `Destructive=false`: năm tool đọc thuần, và hai tool
generate (`generate_email`, `generate_call_script`) chỉ sinh draft, không ghi, không gửi, không gọi.

**Cập nhật 2026-08-27 (P0-14).** Câu cấm cũ *"không expose bất kỳ tool ghi dữ liệu nào trong P0"*
được override bởi PD-021: `delete_customer` là **ngoại lệ duy nhất**, `ReadOnly=false`,
`Destructive=true`, và được gác bằng authorization ở MCP boundary (§14). Vẫn **không** expose
`send_email` hay bất kỳ tool nào gây tác động ra ngoài hệ thống.

Host chỉ cho model thấy allowlist tool đã duyệt. Mọi tool call phải validate lại ở server; không tin
arguments do model tạo.

**Chuẩn hoá argument ở Host (P0-10).** Model không biết catalogue sản phẩm, nên nó có thể điền
`productCode` bằng ngôn ngữ tự nhiên (quan sát thật: `"gửi tiết kiệm 6 tháng"`). Host **bỏ** một
identifier sai định dạng trước khi dispatch — không sửa, không chuyển tiếp — và gấp nội dung đó vào
`objective` để chính RAG của tool phân giải ra mã đúng. Điều này **không** nới validator: một client
gọi thẳng MCP với cùng giá trị sai vẫn nhận `INVALID_ARGUMENT`.

**Chặn mã khách hàng sai định dạng ở Host (P0-10, browser-verified).** `InputGuard` từ chối message
chứa token dạng mã khách hàng nhưng không khớp `CUS-####` (ví dụ `CS-0002`) với
`CUSTOMER_ID_INVALID`, **trước khi** gọi Gemini và trước mọi tool call. Nếu để lọt, mỗi đường đi sai
một kiểu: model tự thay bằng khách hàng đang có trong phiên (báo thành công cho **nhầm** khách),
hoặc chuyển thành lookup rồi trả `NOT_FOUND` (ngụ ý mã đúng định dạng, chỉ là không tồn tại), hoặc
biến thành `query` rồi va chạm với `customerId` mà Host nhét vào từ session. Guard cố ý hẹp: các họ
identifier khác (`OPP-`, `INT-`, `CMP-`, `ACC-`, `RM-`, product/template/call-script code) không bị
ảnh hưởng.

Host **không bao giờ** ghép `customerId` từ session vào một call mà model đã cung cấp `query` —
đó chính là cách sinh ra call mang cả hai argument mà `get_customer` từ chối.

Turn bị từ chối ở `InputGuard` **không** thay đổi conversation state: khách hàng hợp lệ đang lưu
trong phiên vẫn nguyên vẹn cho follow-up sau đó.

## 9. Contract tests bắt buộc

- Tool discovery có đúng tên/schema.
- Required field thiếu → `INVALID_ARGUMENT`.
- Customer exact/ambiguous/not-found.
- Interaction limit/sort/customer isolation.
- Knowledge allowlist/topK/empty retrieval.
- Email success/no evidence/Gemini invalid schema/PII capture.
- Tool result không serialize exception hoặc secret.
- Opportunity: enum `status` hợp lệ/không hợp lệ, và **lọc-trước-limit** (§11).
- Campaign: chỉ trả campaign mà customer thuộc diện tham gia, không bao giờ trả toàn bộ (§12).
- Call script: chọn đúng một opportunity; `opportunityId` của khách khác ⇒ `not_found` và **không**
  gọi Gemini; `objective` vắng mặt ⇒ suy ra + warning; `sourceIds` **loại** retrieval candidate mà
  draft không dùng (§13.1).
- Host argument normalization: `productCode` ngôn ngữ tự nhiên bị bỏ và lượt chat vẫn success, trong
  khi direct MCP call với cùng giá trị vẫn `INVALID_ARGUMENT` (§8).
- Mã khách hàng sai định dạng: `CUSTOMER_ID_INVALID` ở cả phiên mới lẫn phiên đang giữ khách hàng;
  không gọi tool; không fallback sang khách hàng trong session; session giữ nguyên khách hàng hợp lệ;
  và ở MCP boundary `get_customer` trả `INVALID_ARGUMENT` chứ không `NOT_FOUND` (§4, §8).

## 10. Lịch sử P1/P2 tools

Bản gốc xếp `get_opportunities` + `generate_call_script` là P1 và `get_campaigns` là P2.

**Cập nhật 2026-08-26:** Product Owner phê duyệt kéo cả ba lên **P0-10**, override baseline "MCP
tools bắt buộc = 4" của `CLAUDE.md` §4. Contract của chúng nằm ở §11, §12, §13. Backlog tool còn lại
sau P0-10: không còn tool nào được lên kế hoạch.

Không thêm tool chỉ để đủ số lượng. Mỗi tool mới cần contract, test và demo value rõ.

## 11. `get_opportunities`

Mô tả tool:

> Lấy các cơ hội bán của một customer ID đã xác định. Dùng `status="Open"` khi người dùng hỏi về cơ
> hội đang mở. Không tìm customer theo tên.

Input:

```json
{ "customerId": "CUS-0001", "status": "Open", "limit": 5 }
```

Rules:

- `customerId` required.
- `status` optional; allowlist `Open`, `Won`, `Lost`, `Closed` (nhận case-insensitive, chuẩn hoá về
  đúng canonical). Giá trị khác ⇒ `INVALID_ARGUMENT` ở **cả** MCP boundary lẫn HTTP boundary.
- `limit` default 5, min 1, max 20.
- **Lọc theo `status` phải xảy ra TRƯỚC khi áp `limit`.** Nếu đảo thứ tự, một khách hàng có nhiều
  record `Won` đóng sớm sẽ chiếm hết trang và trả về rỗng cho truy vấn `status="Open"` dù khách hàng
  đó rõ ràng có cơ hội đang mở.
- Sắp xếp `ExpectedCloseDateUtc` tăng dần, tie-break `Id` tăng dần — nhờ đó "cơ hội Open đầu tiên"
  là một record xác định.

Output `sourceIds` chứa từng `crm:opportunity:<id>` được trả.

## 12. `get_campaigns`

Mô tả tool:

> Lấy các chiến dịch marketing mà một customer ID đã xác định thuộc diện tham gia. Luôn cần
> `customerId` — tool này không liệt kê toàn bộ chiến dịch.

Input:

```json
{ "customerId": "CUS-0001", "limit": 5 }
```

Rules:

- `customerId` **required**. P0-10 cố ý **không** có chế độ listing toàn cục, nên một câu hỏi về một
  khách hàng không bao giờ mở rộng thành cả bảng campaign.
- Quan hệ campaign↔customer là **deterministic qua `eligibleCustomerIds`**, không suy diễn từ
  `targetSegment`.
- `limit` default 5, min 1, max 20. Sắp xếp `StartDateUtc` giảm dần, tie-break `Id`.

Output `sourceIds` chứa từng `crm:campaign:<id>` được trả.

## 13. `generate_call_script`

Mô tả tool:

> Tạo bản nháp kịch bản gọi điện tiếng Việt cho một khách hàng đã xác định. Tool tự lấy interaction,
> cơ hội bán và tự retrieve playbook cùng product knowledge. Không thực hiện cuộc gọi. Luôn yêu cầu
> RM duyệt.

Input:

```json
{
  "customerId": "CUS-0001",
  "objective": "Trao đổi nhu cầu gửi tiết kiệm kỳ hạn 6 tháng",
  "opportunityId": "OPP-0002",
  "productCode": "PRD-SAV-006M"
}
```

Rules:

- `customerId` required. `objective` (≤500 ký tự), `opportunityId` (`^OPP-\d{4}$`), `productCode`
  (cùng format với §7) đều **optional**.
- Toàn bộ CRM lookup + RAG retrieval xảy ra **bên trong một lần gọi tool**, nên Host chỉ tốn một
  trong ba lượt gọi MCP của lượt chat.
- Call script trong knowledge base là **playbook làm evidence**, không phải nội dung trả thẳng cho
  RM — mỗi request sinh một draft mới, cá nhân hoá.
- Nếu `objective` vắng mặt, tool tự suy ra: ưu tiên `opportunityId`, rồi cơ hội `Open` khớp
  `productCode`, rồi cơ hội `Open` đầu tiên; không có cơ hội `Open` nào thì dùng mục tiêu chăm sóc
  định kỳ. Mọi trường hợp suy ra đều gắn warning `OBJECTIVE_INFERRED`, và trả `resolvedObjective` +
  `objectiveSource` + `selectedOpportunityId` trong output.
- `productCode` được chỉ định mà không có product evidence khớp ⇒ `not_found` ngay, **không** gọi
  Gemini. Playbook không được bù cho product evidence thiếu.
- PII mask trước Gemini. Opportunity chỉ vào prompt dưới dạng data tối giản: `sourceId`,
  `productCode`, `stage`, `status`, `expectedCloseDateUtc` và **`amountBand`** (khoảng giá trị) —
  **không** gửi số tiền chính xác, **không** gửi `customerId`.
- `requiresHumanApproval` luôn `true`, server ép, không đọc từ model.

Success data gồm `opening`, `discoveryQuestions[]`, `talkingPoints[]`,
`objectionHandling[{objection,response}]`, `closing`, `suggestedProductCode`,
`selectedOpportunityId`, `resolvedObjective`, `objectiveSource`, `sourceIds`,
`requiresHumanApproval`, `warnings[]`, `piiMaskSummary`.

### 13.1 `sourceIds` — chỉ nguồn thực sự hỗ trợ draft

Contract chốt 2026-08-26 sau browser verification:

```
sourceIds = dedupe(
    [ "crm:opportunity:<id>" — chỉ khi opportunity được giữ lại, xem bên dưới ]
  ++ [ usedSourceIds của model ∩ evidence được phép ]
)
```

- **Không** liệt kê retrieval candidate mà draft không dùng. Retrieval đưa 3 product + 2 playbook vào
  prompt; chỉ những cái được trích mới được báo cáo. `sourceIds` là provenance mà RM đọc như "draft
  dựa trên những nguồn này", nên liệt kê candidate vào đó là nói quá.
- Đây không phải "tin model": validator đã ép mọi id phải là subset của evidence, ép phải có ít nhất
  một nguồn `kb:product:`/`kb:call-script:`, và ép phải trích đúng sản phẩm khi request chỉ định
  `productCode`.
- **Chứng thực opportunity:** một opportunity **tự chọn** chỉ được giữ khi draft thực sự nói về sản
  phẩm của nó (`suggestedProductCode` khớp, hoặc draft đã trích nguồn sản phẩm đó). Không chứng thực
  được ⇒ loại khỏi cả `sourceIds` lẫn `selectedOpportunityId`.
- **Ngoại lệ:** `opportunityId` do caller truyền tường minh luôn được giữ — đó là ý định đã nêu rõ
  của caller, không phải phỏng đoán của tool.

Ràng buộc cấu trúc `body`/văn bản thuần của §7.1 áp dụng tương tự cho từng phần văn bản của call
script (không HTML/Markdown, tiếng Việt có dấu, không bịa số liệu ngoài evidence).

