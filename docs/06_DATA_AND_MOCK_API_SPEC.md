# 06 — Data and Mock CRM API Specification

## 1. Mục tiêu dữ liệu

Dataset phải nhỏ, deterministic, đủ branch để demo/test và hoàn toàn tổng hợp. Không dùng dữ liệu scrape hoặc export từ CRM thật.

## 2. Quy mô tối thiểu

| Loại | P0 bắt buộc | Ghi chú |
| --- | ---: | --- |
| Customers | 12 | Có tên unique, 2 khách trùng tên, 1 canonical customer |
| Interactions | 25–30 | Tối thiểu 2/customer cho customer chính; có timestamp khác nhau |
| Products | 6 | Tiết kiệm, thẻ, vay, bảo hiểm; đủ positive/negative retrieval |
| Email templates | 8 | Follow-up, đáo hạn, giới thiệu sản phẩm, cảm ơn |
| Opportunities | 6–8 | Có thể tạo sẵn nhưng không cần endpoint/tool trong core P0 |
| Campaigns | 2–3 | P2-ready, không block P0 |

## 3. Canonical scenario

Record chuẩn dùng xuyên tests/demo:

- Customer ID: `CUS-0001`
- Tên tổng hợp: `Nguyễn Minh Anh`
- Email: `minh.anh@example.test`
- Phone: `0900000001`
- Account reference: `000000000001`
- Segment: `Priority`
- Interaction gần nhất: quan tâm kênh gửi tiết kiệm an toàn, kỳ hạn 6 tháng, cần được liên hệ lại.
- Product expected trong top 3: `PRD-SAV-006M`.
- Email template expected candidate: `TPL-EMAIL-MATURITY-01` hoặc template follow-up tương đương đã đóng băng.

Tên, domain `.test`, phone/account chỉ phục vụ dữ liệu giả. File README/data manifest phải ghi rõ `synthetic: true`.

## 4. JSON schema tối thiểu

### Customer

```json
{
  "id": "CUS-0001",
  "fullName": "Nguyễn Minh Anh",
  "email": "minh.anh@example.test",
  "phone": "0900000001",
  "accountReference": "000000000001",
  "segment": "Priority",
  "city": "Hà Nội",
  "preferredLanguage": "vi",
  "relationshipManagerId": "RM-001",
  "status": "Active",
  "synthetic": true,
  "updatedAtUtc": "2026-08-15T09:00:00Z"
}
```

### Interaction

```json
{
  "id": "INT-0001",
  "customerId": "CUS-0001",
  "type": "Call",
  "occurredAtUtc": "2026-08-15T08:30:00Z",
  "summary": "Khách hàng quan tâm tiền gửi kỳ hạn 6 tháng và ưu tiên rủi ro thấp.",
  "outcome": "FollowUpRequired",
  "nextAction": "Gửi thông tin sản phẩm trước ngày 2026-08-20",
  "synthetic": true
}
```

### Product knowledge

```json
{
  "sourceId": "kb:product:PRD-SAV-006M",
  "productCode": "PRD-SAV-006M",
  "name": "Tiền gửi kỳ hạn 6 tháng",
  "category": "Savings",
  "summary": "Sản phẩm tiền gửi dành cho khách hàng ưu tiên an toàn và kỳ hạn trung bình.",
  "eligibility": ["Khách hàng cá nhân", "Có hồ sơ hợp lệ"],
  "benefits": ["Kỳ hạn 6 tháng", "Quản lý trên kênh số"],
  "constraints": ["Lãi suất minh họa không được tự suy diễn"],
  "language": "vi",
  "synthetic": true,
  "version": "1.0"
}
```

Không đưa con số lãi suất thật hoặc claim pháp lý chưa kiểm chứng vào dataset. Nếu có trường rate, ghi rõ là synthetic demo value và model chỉ được dùng đúng giá trị source.

### Email template

```json
{
  "sourceId": "kb:email-template:TPL-EMAIL-MATURITY-01",
  "templateId": "TPL-EMAIL-MATURITY-01",
  "intent": "SavingsFollowUp",
  "tone": "ProfessionalWarm",
  "subjectPattern": "Thông tin tham khảo về {{PRODUCT_NAME}}",
  "bodyGuidance": [
    "Nhắc lại nhu cầu đã trao đổi",
    "Chỉ nêu lợi ích có trong product source",
    "Mời khách hàng phản hồi thời gian phù hợp"
  ],
  "language": "vi",
  "synthetic": true,
  "version": "1.0"
}
```

## 5. Mock CRM endpoints P0

| Method | Path | Mô tả |
| --- | --- | --- |
| GET | `/health` | Health/readiness cơ bản |
| GET | `/api/customers/{customerId}` | Exact ID lookup |
| GET | `/api/customers?query={nameOrId}` | Case-insensitive normalized search |
| GET | `/api/customers/{customerId}/interactions?limit=5` | Newest-first, limit 1–20 |

P1/P2 có thể thêm `/opportunities` và `/campaigns`; không thêm trong P0-02 nếu làm chậm.

## 6. Response envelope

Success:

```json
{
  "data": {},
  "traceId": "...",
  "source": "mock-crm"
}
```

Error:

```json
{
  "error": {
    "code": "NOT_FOUND",
    "message": "Không tìm thấy khách hàng phù hợp.",
    "retryable": false
  },
  "traceId": "..."
}
```

Error codes P0: `INVALID_ARGUMENT`, `NOT_FOUND`, `AMBIGUOUS_MATCH`, `UPSTREAM_UNAVAILABLE`, `INTERNAL_ERROR`.

## 7. Search behavior

- Chuẩn hóa trim, case và khoảng trắng.
- Có thể hỗ trợ không dấu như enhancement nếu không làm mơ hồ; nếu có phải test.
- Exact ID ưu tiên cao nhất.
- Exact normalized full name trả unique nếu chỉ có một record.
- Nhiều record cùng normalized name trả candidates tối thiểu: `id`, `fullName`, `segment`, `city`; không tự chọn.
- Không fuzzy search phức tạp trong P0.

## 8. `ICrmGateway`

Contract định hướng:

```csharp
Task<CustomerLookupResult> FindCustomerAsync(
    CustomerLookupQuery query,
    CancellationToken cancellationToken);

Task<IReadOnlyList<InteractionDto>> GetInteractionsAsync(
    string customerId,
    int limit,
    CancellationToken cancellationToken);
```

- MCP tools chỉ phụ thuộc interface.
- `MockCrmGateway` gọi REST Mock CRM API bằng typed `HttpClient`.
- Không để MCP tool đọc JSON file trực tiếp.
- Adapter tương lai ánh xạ DTO vendor về neutral domain contract.

## 9. Dataset validation

P0-02 cần automated validation:

- ID unique và đúng prefix.
- Foreign key `interaction.customerId` tồn tại.
- Timestamps parse UTC.
- Tất cả record có `synthetic=true`.
- Không dùng domain email thật ngoài `.test`/`.example`.
- Canonical IDs tồn tại.
- Product/template `sourceId` unique.
- Không có secret/token/real account pattern trong file.

## 10. HubSpot mapping sau P0

| Neutral concept | HubSpot object |
| --- | --- |
| Customer | Contact |
| Interaction | Calls/Emails/Notes/Meetings hoặc engagements/associations tương ứng |
| Opportunity | Deal |
| Campaign | Marketing campaign/domain-specific integration |

`HubSpotCrmGateway` chỉ được thêm sau khi mock path pass. Không thay đổi MCP tool contract chỉ để phù hợp field name của HubSpot.

