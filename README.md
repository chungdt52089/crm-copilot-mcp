# CRM Copilot

**AI CRM Co-Pilot tiếng Việt cho Relationship Manager (RM) ngân hàng.**

RM gõ một câu tiếng Việt bình thường — *"Tìm hồ sơ khách hàng CUS-0001"*, *"Khách hàng này có cơ hội
bán nào?"*, *"Soạn email follow-up về gói tiết kiệm 6 tháng"*. Hệ thống tự chọn đúng công cụ CRM và
gọi chúng qua **MCP (Model Context Protocol)**, truy xuất kiến thức sản phẩm bằng **RAG**, rồi trả về
kết quả có cấu trúc kèm **nguồn trích dẫn** và **dấu vết từng lần gọi tool**.

Email và kịch bản gọi điện chỉ là **bản nháp**: hệ thống không gửi email, không thực hiện cuộc gọi, và
luôn gắn cờ `requiresHumanApproval = true` để RM duyệt.

RM **đăng nhập** bằng tài khoản có vai trò (RM / Admin / Auditor). Quyền gọi tool được kiểm **tại biên
MCP** bằng JWT — không phải ở giao diện — nên một MCP client độc lập cũng không đi vòng được. RM cũng
có thể **giữ nút mic để đọc yêu cầu** thay vì gõ: transcript được điền vào ô nhập để RM đọc lại và
sửa, rồi mới bấm Gửi.

---

## 1. Bối cảnh — RM đang mất thời gian vào đâu

Một Relationship Manager (RM) ngân hàng bán lẻ phụ trách **50–80 khách hàng mỗi ngày**. Phần lớn thời
gian của họ không nằm ở tư vấn hay bán hàng, mà ở thao tác tra cứu và soạn thảo lặp đi lặp lại:
khoảng **2–3 giờ mỗi ca**, tương đương **25–37%** thời gian làm việc.

Để chuẩn bị cho **một** cuộc gọi hay **một** email, RM phải tự đi hết chuỗi thao tác này:

| Bước thủ công | RM phải tự làm gì |
| --- | --- |
| Mở hồ sơ khách hàng → mở lịch sử tương tác → mở danh sách cơ hội bán | Chuyển qua lại giữa những màn hình rời rạc |
| Ghép các mẩu thông tin vừa đọc lại với nhau | Tự tổng hợp trong đầu, không công cụ nào làm hộ |
| Viết email hoặc kịch bản gọi | Bắt đầu từ trang trắng, mỗi lần một kiểu |

Gốc rễ không nằm ở kỹ năng của RM hay tốc độ của CRM. CRM được thiết kế để **lưu trữ** dữ liệu chuẩn
xác (*system of record*) và nó làm tốt việc đó — nhưng công việc hằng ngày của RM lại cần một thứ
khác: một hệ thống biết **nối các mẩu dữ liệu thành hành động** (*system of intelligence*). Phần
"nối" đó hiện do con người gánh, và nó không co giãn được khi số khách hàng tăng lên.

Hướng giải quyết vì thế không phải là thay CRM, mà là đặt thêm **một lớp co-pilot** phía trên: RM hỏi
bằng tiếng Việt như nói với đồng nghiệp, hệ thống tự chọn đúng công cụ CRM để gọi, tự tổng hợp, tự
soạn bản nháp — quyền quyết định cuối cùng vẫn thuộc về RM.

---

## 2. Dự án này demo phần nào của bài toán

Bài toán đầy đủ rộng hơn nhiều những gì repo này làm. Đây là **một lát cắt được thu hẹp có chủ đích**,
và trọng tâm của nó là **MCP (Model Context Protocol)**: làm sao để một AI Host **khám phá** được
những công cụ CRM đang có (`tools/list`), **chọn đúng** công cụ cho câu hỏi của người dùng, **gọi** nó
qua một giao thức chuẩn (`tools/call`), và **giữ ngữ cảnh** qua nhiều lượt hội thoại.

RAG, PII masking và sinh nội dung có mặt ở đây để MCP có việc thật để làm trên dữ liệu thật — chúng là
bối cảnh xung quanh, không phải thứ được trình diễn riêng.

Cụ thể, với một RM, bản demo này làm được bốn việc:

1. **Tra cứu 360° một khách hàng** — hồ sơ, lịch sử tương tác, cơ hội bán, chiến dịch.
2. **Hỏi tiếp bằng "khách hàng này"** mà không phải nhắc lại mã khách hàng ở mỗi lượt.
3. **Tra kiến thức sản phẩm** bằng semantic search thay vì lật tài liệu.
4. **Soạn nháp email và kịch bản gọi** có trích nguồn, và luôn cần RM duyệt.

Ranh giới in-scope/out-of-scope đầy đủ nằm ở mục 3; những gì cố tình chưa làm nằm ở mục 13.

---

## 3. Dự án này chứng minh điều gì

| # | Điều được chứng minh | Cách chứng minh trong code |
| --- | --- | --- |
| 1 | **MCP thật**, không phải mô phỏng | AI Host là MCP Client thật (`ModelContextProtocol`), gọi một MCP Server thật (`ModelContextProtocol.AspNetCore`) qua Streamable HTTP tại `/mcp` |
| 2 | **RAG thật** | 21 tài liệu tiếng Việt được embed bằng `gemini-embedding-001` (768 chiều, L2-normalize) và lưu trong Chroma; truy xuất theo khoảng cách `l2` |
| 3 | **Hội thoại nhiều lượt** | AI Host giữ `CurrentCustomerId` theo `sessionId`; "khách hàng này" ở lượt sau được phân giải xác định, không phụ thuộc model đoán đúng |
| 4 | **Draft có nguồn + không rò PII** | Tên khách hàng được thay bằng placeholder trước khi gửi Gemini và khôi phục ở máy cục bộ; email/điện thoại/số tài khoản **không bao giờ** được đưa vào prompt |
| 5 | **Phân quyền ở đúng tầng** | `ToolPolicy` + request filter `tools/call` trong MCP Server; Host chỉ là UX. Cùng một lời gọi, đổi JWT là đổi kết quả — chứng minh được bằng MCP Inspector |
| 6 | **Giọng nói không phải cửa sau** | Transcript chỉ điền vào ô nhập, **không tự gửi** — nên vẫn đi qua `InputGuard` y như gõ tay |

### Phạm vi

| In-scope | Out-of-scope |
| --- | --- |
| 8 MCP tool: 7 read-only + `delete_customer` (xoá mềm, chỉ Admin) | Kết nối CRM thật (HubSpot, Salesforce…) |
| RAG trên product knowledge / email template / call-script playbook | Gửi email thật, thực hiện cuộc gọi thật |
| Đăng nhập cookie + 3 vai trò; phân quyền tool tại biên MCP bằng JWT | Authentication/authorization mức production (OIDC, refresh token, quản trị người dùng) |
| Chat tiếng Việt nhiều lượt, state trong bộ nhớ | Lưu hội thoại vào database, khôi phục sau khi restart |
| Sinh email nháp + kịch bản gọi nháp, có trích nguồn | Long-term memory, personalization qua nhiều phiên |
| PII masking trước khi gọi LLM và trước khi ghi log | DLP/compliance review đạt chuẩn ngân hàng |
| UI tối thiểu có tool trace và source chip | Triển khai cloud, Kubernetes (**chưa có**) |
| Nhập liệu bằng giọng nói tiếng Việt (push-to-talk, transcribe phía server) | Streaming/realtime STT, wake word, đọc kết quả bằng giọng |
| Docker Compose chạy cả 4 service ở local | CI/CD, TLS termination, secret manager |

Toàn bộ dữ liệu là **dữ liệu tổng hợp** (`synthetic: true`), sinh xác định từ seed cố định. Không có
dữ liệu khách hàng thật ở bất kỳ đâu trong repo.

---

## 4. Công nghệ sử dụng

| Lớp | Lựa chọn | Ghi chú |
| --- | --- | --- |
| Runtime / backend | **.NET 10** (`net10.0`), ASP.NET Core **Minimal API** | SDK ghim `10.0.400` qua `global.json`, `rollForward: latestPatch` |
| Frontend | **Razor Pages** một trang + **vanilla JavaScript** + CSS thuần | Không React/Vue/Angular, không bundler, không npm |
| LLM sinh câu trả lời | **`gemini-3.5-flash-lite`** | Dùng cho tool-calling ở AI Host, sinh email và sinh kịch bản gọi |
| Nhận dạng giọng nói | **`gemini-3.5-flash`** — biến riêng `SPEECH_MODEL_ID` | Bản `-lite` trả về nội dung **bịa** khi nhận audio; đã loại bằng spike trước khi code |
| Đăng nhập | **Cookie authentication** + `PasswordHasher<T>` | Tài khoản demo trong `data/auth/users.json`, không cần RDBMS, 0 package thêm |
| Phân quyền tool | **JWT HS256** — Web ký, McpServer xác thực | Kiểm tại `tools/call` bằng request filter của MCP SDK, không rải check vào từng tool |
| SDK gọi Gemini | **`Google.GenAI` 1.19.0** | Ghim version tập trung ở `Directory.Packages.props` |
| Embedding | **`gemini-embedding-001`**, **768 chiều**, L2-normalize | Phân biệt task type `RETRIEVAL_DOCUMENT` / `RETRIEVAL_QUERY` |
| Vector store | **Chroma `chromadb/chroma:1.5.9`**, metric `l2` | Gọi qua HTTP API v2 (không có package .NET chính thức) |
| Giao thức tool | **MCP** — `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` **2.2.0** | Streamable HTTP, `HttpServerSessionMode.Stateless` |
| Cơ sở dữ liệu | **Không có RDBMS/NoSQL** | Dữ liệu CRM là **file JSON tĩnh** trong `data/crm/`; conversation state là **in-memory** (`ConcurrentDictionary`); Chroma chỉ chứa vector knowledge |
| Đóng gói | **Docker Compose** (`compose.yaml`) — hoặc `dotnet run` như trước | Cả 4 service chạy container: multi-stage Dockerfile, non-root, secret inject lúc runtime. Hai lối chạy loại trừ nhau vì dùng chung port |
| Kiểm thử | **xUnit v3 4.0.0** trên **Microsoft.Testing.Platform** | Bộ mặc định chạy **offline** hoàn toàn; test gọi Gemini/Chroma thật là opt-in |
| Quản lý secret | **.NET User Secrets** (khuyến nghị) hoặc environment variable | Không có dotenv loader trong repo |

---

## 5. Kiến trúc

Bốn tiến trình chạy song song trên máy local, cộng Gemini API ở ngoài:

```text
                    ┌───────────────────────────────────────────────┐
                    │  Trình duyệt  http://localhost:5081           │
                    │  Razor Page + vanilla JS, tự sinh sessionId   │
                    └───────────────────┬───────────────────────────┘
                                        │ POST /api/chat  {message, sessionId}
                                        ▼
     ┌──────────────────────────────────────────────────────────────────────┐
     │  CrmCopilot.Web  :5081   — AI HOST + MCP CLIENT + UI                 │
     │  InputGuard → ConversationState → Gemini tool-calling → MCP Client   │
     │  Sở hữu DUY NHẤT conversation state. KHÔNG bao giờ gọi thẳng CRM.    │
     └────────────┬──────────────────────────────────┬──────────────────────┘
                  │ MCP / Streamable HTTP + JWT      │ HTTPS
                  │ POST :5090/mcp                   ▼
                  │                        ┌────────────────────┐
                  ▼                        │  Gemini API        │
     ┌────────────────────────────────┐    │  chat + embedding  │
     │  CrmCopilot.McpServer  :5090   │───▶│  (bên ngoài)       │
     │  MCP SERVER — 8 tool           │    └────────────────────┘
     │  STATELESS: mọi tool nhận      │
     │  customerId tường minh         │
     └────┬──────────────────────┬────┘
          │ HTTP                 │ HTTP (Chroma API v2)
          ▼                      ▼
 ┌────────────────────┐   ┌──────────────────────────────┐
 │ MockCrmApi  :5100  │   │ Chroma  :8000                │
 │ REST đọc-only      │   │ collection                   │
 │ đọc data/crm/*.json│   │ crm-copilot-knowledge        │
 │ 12 KH / 26 tương   │   │ 21 vector (product /         │
 │ tác / 8 cơ hội /   │   │ email-template / call-script)│
 │ 3 chiến dịch       │   │ KHÔNG chứa customer/chat     │
 └────────────────────┘   └──────────────────────────────┘
```

### Ai sở hữu cái gì

| Thành phần | Sở hữu | Không bao giờ làm |
| --- | --- | --- |
| `CrmCopilot.Web` | UI, **đăng nhập**, **phát hành JWT** cho MCP, conversation state, vòng lặp tool-calling, gate PII đầu vào, **endpoint transcribe** | Gọi trực tiếp `MockCrmApi`, đọc file JSON CRM |
| `CrmCopilot.McpServer` | 8 MCP tool, **kiểm quyền theo vai tại `tools/call`**, RAG orchestration, masking trước khi sinh nội dung | Lưu conversation state, lưu lịch sử chat |
| `CrmCopilot.MockCrmApi` | Dataset CRM tổng hợp + 5 endpoint đọc + 1 endpoint `DELETE` (xoá mềm **chỉ trong RAM**) | Ghi vào file JSON, gọi LLM |
| `CrmCopilot.Contracts` | DTO, interface, mã lỗi, quy tắc định dạng ID dùng chung | Chứa logic hạ tầng |
| Chroma | Vector của knowledge document | Chứa customer, interaction, chat, audit log |

### Ranh giới tin cậy

- Nội dung truy xuất từ RAG được coi là **dữ liệu không tin cậy**, không phải chỉ thị cho model.
- UI render mọi nội dung do model/CRM sinh ra bằng `textContent`, **không dùng `innerHTML`** với giá
  trị nội suy — output của model không thể trở thành HTML thực thi được.
- Mọi lỗi trả về đều có cấu trúc; không có stack trace hay thông điệp nội bộ nào lộ ra UI.

---

## 6. Luồng làm việc của ứng dụng

### 6.1 Một lượt chat đi qua đâu

```text
Trình duyệt: POST /api/chat { message, sessionId }
   │
   1. Kiểm tra sessionId phải là GUID hợp lệ          → sai: 400 INVALID_ARGUMENT
   │
   2. InputGuard (chạy TRƯỚC mọi lời gọi Gemini/MCP)
   │     • có email / SĐT / chuỗi 9+ chữ số / địa chỉ → 400 PII_REJECTED
   │     • có mã hình dạng sai (vd. CS-0002)          → 400 CUSTOMER_ID_INVALID
   │     • có cụm tên viết hoa liên tiếp, không có mã → 400 CUSTOMER_ID_REQUIRED
   │     • có từ khoá CRM, không mã, phiên chưa có KH → 400 CUSTOMER_ID_REQUIRED
   │
   3. Nạp ConversationState theo sessionId (CurrentCustomerId, LastIntent…)
   │
   4. MCP handshake → tools/list                      → lỗi: 503 MCP_UNAVAILABLE
   │     Gemini CHỈ nhìn thấy giao của (allowlist 8 tool) ∩ (tools/list)
   │     tools/list KHÔNG lọc theo vai — mọi vai đều thấy đủ 8 tool
   │     → tool ngoài allowlist là vô hình về mặt cấu trúc, không phải lọc sau
   │
   5. Gọi Gemini (temperature 0.1) kèm system instruction tiếng Việt
   │     Nếu phiên đang có khách hàng, mã đó được nhắc thẳng trong system instruction
   │
   6. Model trả về function call → Host xử lý TRƯỚC khi gửi đi:
   │     a. Thiếu customerId? → tự điền từ CurrentCustomerId
   │        (không đè khi model đã đưa query — hai tham số đó loại trừ nhau)
   │     b. productCode / opportunityId sai định dạng? → BỎ, không sửa,
   │        rồi gấp cụm chữ đó vào objective để RAG tự phân giải
   │     c. Gọi trùng hệt lượt trước?  → 400 DUPLICATE_TOOL_CALL
   │     d. Gọi nhiều tool song song?  → 400 MULTIPLE_FUNCTION_CALLS_NOT_SUPPORTED
   │        (ngoại lệ duy nhất: generate_email kèm search_product_knowledge thừa
   │         được gộp lại thành một lời gọi generate_email)
   │     e. Đã đủ 3 lần gọi MCP?       → 409 TOOL_LOOP_LIMIT_EXCEEDED
   │
   7. Gọi MCP tool thật, kèm JWT mang {sub, role} của người đang đăng nhập
   │     McpServer kiểm quyền TRƯỚC khi chạm gateway → 403 FORBIDDEN + dòng DENIED trong log
   │     ghi {toolName, status, traceId, durationMs} vào toolTrace
   │
   8. Quy tắc "tool kết thúc lượt":
   │     7 tool CRM có cấu trúc thành công  → KẾT THÚC LƯỢT NGAY.
   │       Host tự soạn câu trả lời xác định từ dữ kiện không-PII (mã KH + số lượng);
   │       kết quả KHÔNG được gửi ngược lại cho Gemini.
   │     search_product_knowledge          → nội dung (không chứa PII) được gửi lại
   │       cho Gemini để model viết câu trả lời có căn cứ. Quay lại bước 5.
   │
   9. Trả về { reply, status, sourceIds, toolTrace, data, error }
   │
  10. UI đổ data vào đúng panel và vẽ source chip + accordion tool trace
```

**Vì sao 7 tool kết thúc lượt ngay:** payload gửi lại Gemini đã bị lược sạch mọi trường ngữ nghĩa (tên
khách hàng, nội dung tương tác). Nếu vẫn bắt model viết prose về dữ liệu nó không nhìn thấy, model sẽ
bịa. Nên Host tự soạn câu trả lời, còn dữ liệu thật hiển thị ở panel có cấu trúc.
`search_product_knowledge` là ngoại lệ vì nội dung của nó không chứa PII và được gửi nguyên vẹn.

### 6.2 Chín kịch bản người dùng

| Người dùng hỏi | Tool được chọn | Tool tự làm gì bên trong | `sourceIds` trả về | Panel hiển thị |
| --- | --- | --- | --- | --- |
| "Tìm hồ sơ khách hàng **CUS-0001**" | `get_customer` | Gọi `GET /api/customers/CUS-0001` | `crm:customer:CUS-0001` | Khách hàng |
| "Khách hàng này có những **tương tác** gần nhất nào?" | `get_interactions` | Lấy tương tác mới nhất trước, mặc định 5 | `crm:interaction:INT-…` | Tương tác gần đây |
| "**Khách hàng này** …" (lượt sau) | tool tương ứng | Host tự điền `customerId` từ state — model không cần đoán | như trên | như trên |
| "Khách hàng này đang có **cơ hội bán** nào?" | `get_opportunities` | Lọc theo `status` (Open/Won/Lost/Closed) rồi mới cắt `limit` | `crm:opportunity:OPP-…` | Cơ hội bán |
| "Khách hàng này thuộc **chiến dịch** nào?" | `get_campaigns` | Tra theo `eligibleCustomerIds` tường minh, không suy từ segment | `crm:campaign:CMP-…` | Chiến dịch |
| "Có **sản phẩm tiết kiệm** 6 tháng nào phù hợp?" | `search_product_knowledge` | Embed câu hỏi → truy vấn Chroma → lọc theo ngưỡng khoảng cách | `kb:product:…`, `kb:email-template:…` | Tool trace + source chip |
| "**Soạn email** follow-up về gói tiết kiệm 6 tháng" | `generate_email` | **Tự** lấy tương tác gần nhất + **tự** truy xuất product và email template → mask PII → gọi Gemini → khôi phục tên ở máy cục bộ | `kb:product:…`, `kb:email-template:…`, `crm:interaction:…` | Email nháp |
| "**Xoá khách hàng CUS-0001**" — *chỉ Admin* | `delete_customer` | Gọi `DELETE /api/customers/CUS-0001`; Mock CRM đánh dấu xoá **chỉ trong RAM** | `crm:customer:CUS-0001` | Không có panel — câu trả lời xác định |
| "**Soạn kịch bản gọi** cho khách hàng này" | `generate_call_script` | **Tự** lấy tương tác + cơ hội bán + **tự** truy xuất call-script playbook và product | `kb:call-script:…`, `kb:product:…`, `crm:opportunity:…` | Kịch bản gọi |

> Hai tool sinh nội dung **tự truy xuất lấy bằng chứng của mình**. Không cần — và không nên — gọi
> `search_product_knowledge` trước chúng: kết quả của lần gọi ngoài đó không bao giờ đến được bản nháp.

### 6.3 Tám MCP tool

Bảy tool đầu là `ReadOnly = true`, `Destructive = false`. `delete_customer` là **ngoại lệ duy nhất và
có chủ đích** — tool ghi dữ liệu đầu tiên của dự án, `ReadOnly = false`, `Destructive = true`.

| Tool | Tham số | Nguồn dữ liệu |
| --- | --- | --- |
| `get_customer` | `customerId?` **hoặc** `query?` (đúng một trong hai) | Mock CRM API |
| `get_interactions` | `customerId`, `limit` (1–20, mặc định 5) | Mock CRM API |
| `get_opportunities` | `customerId`, `status?` (Open/Won/Lost/Closed), `limit` (1–20, mặc định 5) | Mock CRM API |
| `get_campaigns` | `customerId`, `limit` (1–20, mặc định 5) | Mock CRM API |
| `search_product_knowledge` | `query` (≤1000 ký tự), `topK` (1–5, mặc định 3), `documentTypes?` (`product`, `email_template`) | Chroma + Gemini embedding |
| `generate_email` | `customerId`, `objective` (≤500 ký tự), `tone` (`professional` \| `professional_warm` \| `concise`), `productCode?` | CRM + Chroma + Gemini |
| `generate_call_script` | `customerId`, `objective?` (≤500 ký tự), `opportunityId?`, `productCode?` | CRM + Chroma + Gemini |
| `delete_customer` | `customerId` | Mock CRM API — xoá mềm trong RAM |

Mọi tool trả về **cùng một envelope**:

```jsonc
{
  "status":    "success | not_found | ambiguous | error",
  "traceId":   "…",
  "sourceIds": ["crm:customer:CUS-0001"],
  "data":      { /* khác nhau theo tool và theo status */ },
  "error":     null            // hoặc { code, message, retryable }
}
```

**Vai nào gọi được tool nào** — `ToolPolicy` là allowlist tường minh, tool không nằm trong danh sách
là bị từ chối theo thiết kế:

| Vai | Tool được phép |
| --- | --- |
| **Admin** | Cả 8 |
| **RM** | 7 tool read-only / sinh nháp |
| **Auditor** | `get_customer`, `get_interactions` |

Thêm một tool mới mà không khai báo quyền thì mặc định **không ai ngoài Admin gọi được** — deny by
default, không phải blocklist.

Lỗi nghiệp vụ **không** dùng cờ `isError` của tầng MCP — chúng là tool result bình thường mang `status`
tương ứng. `ambiguous` (trùng tên khách hàng) là **kết quả có thể hành động**, không phải lỗi.

`search_product_knowledge` **không** tìm được call-script playbook: chúng nằm chung collection nhưng
nằm ngoài contract của tool này, chỉ `generate_call_script` mới truy xuất tới.

### 6.4 Mã lỗi và HTTP status của `/api/chat`

| HTTP | Mã lỗi | Nghĩa |
| --- | --- | --- |
| `200` | — | Thành công |
| `404` | `NOT_FOUND` | Mã hợp lệ nhưng không tồn tại trong dataset |
| `409` | — | Trùng tên khách hàng, cần chọn mã cụ thể |
| `409` | `TOOL_LOOP_LIMIT_EXCEEDED` | Đã dùng hết 3 lần gọi tool của lượt |
| `403` | `FORBIDDEN` | Vai hiện tại không được phép gọi tool đó — chặn tại MCP Server |
| `400` | `INVALID_ARGUMENT` | `sessionId` sai, tin nhắn rỗng hoặc quá 2000 ký tự |
| `400` | `PII_REJECTED` | Tin nhắn chứa email / SĐT / số tài khoản / địa chỉ |
| `400` | `CUSTOMER_ID_REQUIRED` | Cần mã khách hàng nhưng chưa có |
| `400` | `CUSTOMER_ID_INVALID` | Mã sai định dạng (ví dụ `CS-0002`) |
| `400` | `NAME_LOOKUP_NOT_SUPPORTED` | Tra cứu theo tên không hỗ trợ qua chat |
| `400` | `UNKNOWN_TOOL`, `DUPLICATE_TOOL_CALL`, `MULTIPLE_FUNCTION_CALLS_NOT_SUPPORTED` | Vi phạm chính sách chọn tool ở Host |
| `503` | `MCP_UNAVAILABLE`, `UPSTREAM_UNAVAILABLE`, `RAG_UNAVAILABLE` | Dịch vụ phụ thuộc không sẵn sàng |
| `502` | `MODEL_ERROR`, `MCP_PROTOCOL_ERROR`, `MCP_INVALID_RESPONSE`, `INTERNAL_ERROR` | Lỗi giao thức/model/nội bộ |

Thông điệp hiển thị cho RM luôn là tiếng Việt an toàn — không lộ regex, quy ước định dạng, tên
validator hay stack trace, kể cả khi mã lỗi nội bộ chi tiết hơn.

### 6.5 PII và an toàn

**Ba cơ chế masking** trước khi bất kỳ nội dung nào tới Gemini:

1. **Loại trừ về mặt cấu trúc** — `email`, `phone`, `accountReference` **không bao giờ được đọc vào**
   prompt hay câu truy vấn. Đây là bảo đảm vô điều kiện, không phụ thuộc regex có khớp hay không.
2. **Thay thế theo trường** — `fullName` được thay bằng `{{CUSTOMER_NAME}}` ở mọi vị trí; tên thật chỉ
   được khôi phục **ở máy cục bộ** sau khi model trả kết quả. Nếu model làm mất placeholder, hệ thống
   chèn lời chào trung tính "Kính gửi Anh/Chị,".
3. **Regex phòng vệ bổ sung** — email / điện thoại / chuỗi số dài / chuỗi giống secret token trong văn
   bản tự do vẫn bị che, kể cả khi không phải giá trị của chính khách hàng này.

Các bảo đảm khác:

- `requiresHumanApproval` do **server ép bằng `true`** — kể cả khi model trả về `false`.
- Không bịa lãi suất: corpus knowledge không chứa ký tự `%` nào, nên mọi con số phần trăm trong bản
  nháp đều bị coi là bịa và bị chặn.
- **Tool trace chỉ hiển thị tên trường đã ẩn** (`name`, `email`, `phone`, `accountReference`), không
  bao giờ hiển thị giá trị.
- Audit log chỉ ghi trường dẫn xuất an toàn — `customerId` được **băm**, không ghi nội dung email, tên,
  số điện thoại, API key hay exception thô.
- Trạng thái hội thoại lưu lại tin nhắn **đã được khử PII cơ học** trước khi ghi.

### 6.6 Đăng nhập, vai trò và phân quyền tool

Truy cập bất kỳ trang nào khi chưa đăng nhập sẽ bị chuyển về `/Login`. Đường dẫn dưới `/api` trả
**401** thay vì 302 — để `fetch` phía trình duyệt xử lý được.

Ba tài khoản demo nằm trong `data/auth/users.json`, mật khẩu lưu dạng hash bằng `PasswordHasher<T>`:

| Tài khoản | Vai | Dùng để demo |
| --- | --- | --- |
| `rm01` | RM | Luồng nghiệp vụ chính: tra cứu, soạn email, soạn kịch bản gọi |
| `admin01` | Admin | Tool phá huỷ `delete_customer` |
| `auditor01` | Auditor | Bị từ chối phần lớn tool — minh hoạ phân quyền |

**Phân quyền nằm ở đâu, và vì sao ở đó.** Ẩn nút trên giao diện chỉ là trải nghiệm, không phải bảo
mật: một MCP client bất kỳ trỏ thẳng vào `:5090/mcp` sẽ đi vòng qua toàn bộ giao diện. Nên quyền được
kiểm ở **biên MCP**:

```text
Web  ──ký JWT HS256 {sub, role}──▶  McpServer
                                     ToolAuthorizationFilter  (request filter của tools/call)
                                       ├─ được phép  → chạy tool
                                       └─ bị từ chối → FORBIDDEN + log:
                                          DENIED tool=… userId=… role=… reason=role_not_permitted
```

Filter chạy **trước** thân tool, nên trước cả `ICrmGateway` — bị từ chối nghĩa là chưa có gì bị chạm
tới. Vì `tools/list` không lọc theo vai, lời gọi vẫn xảy ra thật và **dòng `DENIED` mới tồn tại** để
kiểm toán. Ẩn tool khỏi discovery sẽ làm mất chính bằng chứng đó.

Kiểm chứng độc lập: mở MCP Inspector, dán JWT của hai vai khác nhau, gọi cùng một tool với cùng tham
số. Cùng server, cùng lời gọi — **chỉ khác token, khác kết quả**.

### 6.7 Nhập liệu bằng giọng nói

```text
Giữ nút mic ──▶ MediaRecorder (audio/webm;codecs=opus, tự dừng sau 15s)
             ──▶ POST /api/transcribe   [Authorize], tối đa 1 MB, không ghi ra đĩa
             ──▶ Gemini SPEECH_MODEL_ID (gemini-3.5-flash), temperature 0
             ──▶ chuẩn hoá mã: "C U S 0 0 0 1" / "cus 0001" → "CUS-0001"
             ──▶ ĐIỀN VÀO Ô NHẬP — DỪNG Ở ĐÂY, không tự gửi
             ──▶ RM đọc lại, sửa nếu cần, bấm Gửi → đi qua InputGuard như gõ tay
```

Ba ràng buộc quan trọng:

- **`GEMINI_API_KEY` không bao giờ xuống trình duyệt.** Transcribe chạy phía server, dùng lại đúng
  `Google.GenAI.Client` singleton đã có.
- **Không tự gửi.** Đây là điều kiện để `InputGuard` vẫn là cổng duy nhất. Nếu tự gửi, giọng nói trở
  thành đường vòng qua gate PII.
- **Không log nội dung transcript** — chỉ ghi `bytes`, `durationMs`, `textLength`.

Bước RM xác nhận không phải để cho tiện. Transcribe bằng LLM **có thể bịa** khi audio kém — đã gặp
thật với micro tích hợp của laptop. Đó chính là lý do transcript phải đi qua mắt người trước.

---

## 7. Cấu trúc thư mục

```text
CrmCopilot/
├── CLAUDE.md                     # Quy tắc bắt buộc khi làm việc trong repo  (xem §14)
├── README.md                     # File này
├── CrmCopilot.slnx               # Solution (định dạng slnx)
├── global.json                   # Ghim .NET SDK 10.0.400 + runner Microsoft.Testing.Platform
├── Directory.Packages.props      # Central Package Management — mọi version ghim ở đây
├── .env.example                  # Danh sách TÊN biến môi trường (xem §9 về khi nào được nạp)
├── compose.yaml                  # Stack Docker 4 service — xem §9B
├── .dockerignore                 # Build context = repo root, nên loại trừ phải tường minh
│
├── data/
│   ├── crm/                      # Dataset CRM tổng hợp, seed 20260818
│   │   ├── customers.json        #   12 khách hàng
│   │   ├── interactions.json     #   26 tương tác
│   │   ├── opportunities.json    #    8 cơ hội bán
│   │   └── campaigns.json        #    3 chiến dịch
│   ├── knowledge/                # Corpus RAG — 21 document, tất cả tiếng Việt
│   │   ├── products.json         #    6 sản phẩm
│   │   ├── email-templates.json  #    8 mẫu email
│   │   └── call-scripts.json     #    7 playbook kịch bản gọi
│   └── auth/
│       └── users.json            # 3 tài khoản demo, mật khẩu đã hash
│
├── src/
│   ├── CrmCopilot.Contracts/     # DTO + interface dùng chung, không có hạ tầng
│   │   ├── Api/                  #   Envelope REST
│   │   ├── Chat/                 #   ChatRequest/Response, mã lỗi, tool trace
│   │   ├── Crm/                  #   CustomerDto, InteractionDto, OpportunityDto, CampaignDto,
│   │   │                         #   ICrmGateway, CustomerIdFormat, ProductCodeFormat
│   │   ├── Knowledge/            #   IKnowledgeRetriever, KnowledgeMatch, KnowledgeDocumentType
│   │   ├── Mcp/                  #   McpToolResult + DTO data theo từng tool
│   │   └── Pii/                  #   PiiPatterns (dùng chung Web và McpServer)
│   │
│   ├── CrmCopilot.MockCrmApi/    # REST đọc-only trên dataset JSON  (:5100)
│   │   ├── Dockerfile            #   Multi-stage, non-root; build context là repo root
│   │   ├── Data/                 #   Loader, generator, SoftDeleteRegistry (xoá mềm, RAM)
│   │   ├── Endpoints/            #   5 endpoint đọc + 1 DELETE
│   │   └── Search/               #   Chuẩn hoá và tìm theo tên
│   │
│   ├── CrmCopilot.McpServer/     # MCP Server + RAG                 (:5090)
│   │   ├── Dockerfile            #   Multi-stage, non-root; cũng là image của job `ingest`
│   │   ├── Auth/                 #   ToolPolicy, ToolAuthorizationFilter — kiểm quyền tools/call
│   │   ├── Crm/                  #   CustomerTools, CustomerAdminTools, OpportunityTools,
│   │   │                         #   CampaignTools, MockCrmGateway
│   │   ├── Knowledge/            #   Chroma client, Gemini embedding, retriever, ingestion
│   │   ├── Email/                #   EmailTools, PiiMasker, GeminiEmailDraftGenerator
│   │   ├── CallScript/           #   CallScriptTools, template catalog, generator
│   │   └── Tools/                #   Helper dựng envelope tool result
│   │
│   └── CrmCopilot.Web/           # AI Host + MCP Client + UI        (:5081)
│       ├── Dockerfile            #   Multi-stage, non-root
│       ├── Auth/                 #   AuthEndpoints, UserStore, McpTokenIssuer (ký JWT)
│       ├── Chat/                 #   ChatOrchestrator, InputGuard, ApprovedMcpToolNames,
│       │                         #   GeminiChatClient, McpClientProvider, ConversationState
│       ├── Speech/               #   TranscribeEndpoints, GeminiTranscriber,
│       │                         #   TranscriptNormalizer, SpeechOptions
│       ├── Pages/Index.cshtml    #   Trang duy nhất
│       └── wwwroot/              #   app.js + app.css (vanilla)
│
├── tests/CrmCopilot.Tests/       # Một test project cho toàn solution
│   ├── Acceptance/               #   Bộ 8 scenario T01–T08 + runner + report writer
│   ├── Crm/  Knowledge/  Mcp/    #   Test theo tầng
│   ├── Email/  CallScript/  Web/
│   └── TestSupport/              #   Host in-memory và các fake dùng chung
│
└── docs/                         # Tài liệu điều hành và đặc tả          (xem §14)
```

---

## 8. Yêu cầu hệ thống

| Thành phần | Yêu cầu |
| --- | --- |
| .NET SDK | **`10.0.400`** (band `10.0.4xx`, `rollForward: latestPatch`) — kiểm tra bằng `dotnet --version`. Chỉ cần cho §9A và để chạy test |
| Docker | Lối §9A: chỉ cần cho container Chroma. Lối §9B: cần cho cả 4 service. Compose v2 (`docker compose`, không phải `docker-compose`) |
| Gemini API key | Bắt buộc. Cần quyền gọi cả model chat và model embedding |
| Hệ điều hành | Windows + PowerShell là môi trường đã kiểm chứng. Mã nguồn không phụ thuộc nền tảng |
| Port trống | `5081` (Web) · `5090` (MCP Server) · `5100` (Mock CRM) · `8000` (Chroma) — **giống nhau ở cả hai lối chạy** |
| Mạng | Cần ra Internet để gọi Gemini API |

---

## 9. Cài đặt và chạy

Có **hai lối chạy**, cho cùng một kết quả và cùng bộ port:

| | §9A — `dotnet run` | §9B — `docker compose` |
| --- | --- | --- |
| Cần .NET SDK trên máy | Có | Không |
| Số terminal phải mở | 3 | 0 |
| Secret lấy từ | .NET User Secrets | environment / `.env` |
| Hợp cho | phát triển, debug, chạy test | demo, kiểm tra đóng gói |

> **Hai lối này loại trừ nhau** — cùng chiếm `5081/5090/5100/8000`. Đang chạy lối này thì phải dừng hẳn
> trước khi chuyển sang lối kia. Xem "Chuyển qua lại giữa hai lối" ở cuối §9B.

---

## 9A. Chạy bằng `dotnet run`

### Bước 1 — Restore và build

```powershell
dotnet restore CrmCopilot.slnx
dotnet build CrmCopilot.slnx --no-restore
```

Kỳ vọng: **0 Warning, 0 Error**.

### Bước 2 — Chạy Chroma

Image ghim version; volume đặt tên để dữ liệu còn nguyên khi xoá container:

```powershell
docker run -d --name crm-copilot-chroma -p 8000:8000 -v crm-copilot-chroma-data:/data chromadb/chroma:1.5.9
curl http://localhost:8000/api/v2/heartbeat
```

### Bước 3 — Cấu hình

`GEMINI_API_KEY` và `MCP_JWT_SIGNING_KEY` là **secret** — dùng .NET User Secrets, không đưa vào file
trong repo và không gõ thẳng vào dòng lệnh (dòng lệnh đi vào lịch sử shell):

```powershell
dotnet user-secrets --project src/CrmCopilot.McpServer set GEMINI_API_KEY "<khoá của bạn>"
dotnet user-secrets --project src/CrmCopilot.Web        set GEMINI_API_KEY "<khoá của bạn>"

# JWT ký/xác thực quyền gọi tool — HMAC đối xứng nên HAI GIÁ TRỊ PHẢI GIỐNG HỆT NHAU,
# tối thiểu 32 byte. Thiếu hoặc lệch: cả hai host fail fast lúc khởi động.
dotnet user-secrets --project src/CrmCopilot.Web        set MCP_JWT_SIGNING_KEY "<chuỗi >=32 ký tự>"
dotnet user-secrets --project src/CrmCopilot.McpServer  set MCP_JWT_SIGNING_KEY "<cùng chuỗi đó>"
```

Các giá trị còn lại **không phải secret** — đặt vào `appsettings.Development.json` của từng project
(file này đã bị `.gitignore` chặn), hoặc export thành environment variable:

| Biến | Cần cho | Giá trị local |
| --- | --- | --- |
| `MOCKCRM_API_BASE_URL` | McpServer | `http://localhost:5100` |
| `CHROMA_BASE_URL` | McpServer | `http://localhost:8000` |
| `CHROMA_COLLECTION_NAME` | McpServer (tuỳ chọn) | mặc định `crm-copilot-knowledge` |
| `MCPSERVER_BASE_URL` | Web | `http://localhost:5090` |
| `GEMINI_API_KEY` | McpServer + Web | *(user secrets)* |
| `MCP_JWT_SIGNING_KEY` | McpServer + Web | *(user secrets)* — **phải giống hệt nhau**, ≥ 32 byte |
| `SPEECH_MODEL_ID` | Web (tuỳ chọn) | mặc định `gemini-3.5-flash` |

Cách thay thế bằng environment variable trong phiên PowerShell hiện tại:

```powershell
$env:MOCKCRM_API_BASE_URL = "http://localhost:5100"
$env:CHROMA_BASE_URL      = "http://localhost:8000"
$env:MCPSERVER_BASE_URL   = "http://localhost:5090"
$env:GEMINI_API_KEY = (Read-Host -AsSecureString "GEMINI_API_KEY" |
  ForEach-Object { [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
      [Runtime.InteropServices.Marshal]::SecureStringToBSTR($_)) })
```

> `.env.example` chỉ là **danh sách tên biến** cho người đọc. Repo **không có** loader nào đọc file
> `.env` — sao chép nó thành `.env` sẽ không có tác dụng gì với `dotnet run`.
>
> Điều này **chỉ đúng cho §9A**. Ở §9B, `docker compose` tự đọc `.env` ở repo root, nhưng chỉ để
> interpolate `${...}` bên trong `compose.yaml`, và chỉ với hai tên: `GEMINI_API_KEY` (bắt buộc) và
> `CHROMA_COLLECTION_NAME` (tuỳ chọn). Ba biến URL trong `.env` bị **bỏ qua** — `compose.yaml`
> hard-code chúng thành compose service name.

Thiếu biến bắt buộc, host **fail fast ngay lúc khởi động** (`ValidateOnStart`) thay vì âm thầm chạy sai.

### Bước 4 — Nạp knowledge vào Chroma

Hai lệnh CLI dưới đây chỉ chạy tác vụ rồi thoát, không khởi động web host và **không** cần
`MOCKCRM_API_BASE_URL`:

```powershell
dotnet run --project src/CrmCopilot.McpServer --no-build -- --ingest-knowledge
```

Lần đầu trên collection rỗng: **21 document, 21 embedded, count = 21**.
Chạy lại lần hai trên dữ liệu không đổi: **0 embedded / 21 unchanged** — đó là bằng chứng ingestion có
tính idempotent (không gọi lại Gemini).

Kiểm tra nhanh chất lượng truy xuất:

```powershell
dotnet run --project src/CrmCopilot.McpServer --no-build -- --query-knowledge "Khách hàng quan tâm gửi tiết kiệm an toàn kỳ hạn 6 tháng, cần liên hệ lại."
```

Kỳ vọng: L2 norm của query embedding ≈ `1.000000`, và `PRD-SAV-006M` nằm trong top-3.

### Bước 5 — Chạy ba service

Mỗi lệnh ở một terminal riêng. Profile `http` đặt `ASPNETCORE_ENVIRONMENT=Development`, nhờ đó user
secrets và `appsettings.Development.json` được nạp tự động.

```powershell
# Terminal 1 — Mock CRM API
dotnet run --project src/CrmCopilot.MockCrmApi --launch-profile http --no-build

# Terminal 2 — MCP Server
dotnet run --project src/CrmCopilot.McpServer --launch-profile http --no-build

# Terminal 3 — Web (AI Host + UI)
dotnet run --project src/CrmCopilot.Web --launch-profile http --no-build
```

MCP handshake diễn ra **lazy** — ở request `/api/chat` đầu tiên, không phải lúc Web khởi động. Web vẫn
start được khi McpServer chưa lên, nhưng lượt chat đầu tiên sẽ trả `MCP_UNAVAILABLE`.

### Bước 6 — Preflight

Cả bốn dòng phải trả `200`. Khác `200` thì dừng, đừng dùng tiếp:

```powershell
foreach ($u in @(
  "http://localhost:5100/health",
  "http://localhost:5090/health",
  "http://localhost:5081/health",
  "http://localhost:8000/api/v2/heartbeat")) {
  $r = Invoke-WebRequest -Uri $u -TimeoutSec 5 -UseBasicParsing
  Write-Output "$u -> $($r.StatusCode)"
}
```

Sau đó mở **<http://localhost:5081>**.

---

## 9B. Chạy bằng Docker Compose

Không cần .NET SDK trên máy — image tự build trong container. `compose.yaml` dựng bốn service:
`web`, `mcpserver`, `mockcrmapi`, `chroma`, cộng một job `ingest` chạy một lần theo profile.

### Bước 1 — Đặt `GEMINI_API_KEY` và `MCP_JWT_SIGNING_KEY`

Đây là **secret duy nhất**, và chỉ được inject lúc runtime — không bao giờ nằm trong image. Compose
lấy nó từ environment của shell, hoặc từ `.env` ở repo root (đã bị `.gitignore` chặn):

```powershell
# Cách 1 — file .env ở repo root (compose tự đọc)
#   GEMINI_API_KEY=<khoá của bạn>
#   MCP_JWT_SIGNING_KEY=<chuỗi >=32 ký tự>   # compose ép bắt buộc, thiếu là stack không lên

# Cách 2 — chỉ trong phiên PowerShell hiện tại, không đi vào lịch sử shell
$env:GEMINI_API_KEY = (Read-Host -AsSecureString "GEMINI_API_KEY" |
  ForEach-Object { [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
      [Runtime.InteropServices.Marshal]::SecureStringToBSTR($_)) })
```

Thiếu hoặc để rỗng thì compose **dừng ngay** với thông báo `GEMINI_API_KEY is required`, không dựng
container nào. Ba biến URL không cần đặt: `compose.yaml` hard-code chúng thành service name.

### Bước 2 — Dựng stack

Nếu bạn từng chạy Chroma thủ công theo §9A, hãy nhường port `8000` trước:

```powershell
docker stop crm-copilot-chroma   # chỉ stop; volume crm-copilot-chroma-data giữ nguyên
docker compose up -d --build
```

Compose chờ theo đúng thứ tự phụ thuộc: `chroma` + `mockcrmapi` healthy → `mcpserver` → healthy →
`web`. Kiểm tra:

```powershell
docker compose ps
```

Cả bốn dòng phải là `Up ... (healthy)`.

### Bước 3 — Nạp knowledge vào Chroma

Volume của compose là volume **mới và rỗng**, nên lần đầu bắt buộc phải ingest. Job này nằm sau
profile `ingest` nên `docker compose up` thường ngày **không** tốn Gemini API call:

```powershell
docker compose --profile ingest run --rm ingest
```

Lần đầu: **21 documents (21 embedded, 0 unchanged) … collection count after ingest=21**.
Chạy lại trên dữ liệu không đổi: **0 embedded, 21 unchanged** — bằng chứng ingestion idempotent.

Kiểm tra nhanh chất lượng truy xuất:

```powershell
docker compose --profile ingest run --rm --entrypoint dotnet ingest `
  CrmCopilot.McpServer.dll --query-knowledge "Khách hàng quan tâm gửi tiết kiệm an toàn kỳ hạn 6 tháng, cần liên hệ lại."
```

Kỳ vọng: L2 norm ≈ `1.000000`, và `PRD-SAV-006M` đứng đầu top-3.

### Bước 4 — Preflight

Dùng đúng block PowerShell ở **§9A Bước 6**, không sửa gì — port phía host giống hệt. Cả bốn phải
trả `200`. Sau đó mở **<http://localhost:5081>**.

### Vận hành hằng ngày

```powershell
docker compose logs -f mcpserver     # xem log một service
docker compose restart web           # restart một service (mất toàn bộ conversation state)
docker compose up -d --build         # build lại sau khi sửa code
docker compose down                  # dừng, GIỮ index Chroma
docker compose down -v               # dừng và XOÁ index Chroma của stack này
```

`docker compose down -v` chỉ xoá volume `crm-copilot_chroma-data` của stack này. Volume
`crm-copilot-chroma-data` mà lối §9A dùng là volume **khác** và không bị đụng tới. Sau `down -v`,
phải chạy lại Bước 3.

### Chuyển qua lại giữa hai lối

```powershell
# §9B  ->  §9A
docker compose down
docker start crm-copilot-chroma

# §9A  ->  §9B
#   dừng 3 terminal dotnet run (Ctrl+C), rồi:
docker stop crm-copilot-chroma
docker compose up -d
```

Hai lối dùng **hai index Chroma riêng biệt**. Index đã nạp ở lối này không xuất hiện ở lối kia; mỗi
lối cần bước ingest của riêng nó một lần.

---

## 9C. Tham khảo chung

### Sinh lại dataset CRM (tuỳ chọn)

Dataset sinh xác định từ seed cố định — đừng sửa tay các file JSON lớn:

```powershell
dotnet run --project src/CrmCopilot.MockCrmApi --no-build -- --generate-dataset [--customers N] [--seed N] [--output <dir>]
```

Không tham số sẽ tái tạo đúng dataset đang được commit.

### Endpoint của Mock CRM API

| Method | Path | Mô tả |
| --- | --- | --- |
| GET | `/api/customers/{customerId}` | Tra cứu chính xác theo mã; `404` nếu không có |
| GET | `/api/customers?query={nameOrId}` | Tìm theo mã hoặc tên chuẩn hoá; `409` nếu trùng tên |
| GET | `/api/customers/{customerId}/interactions?limit=5` | Tương tác mới nhất trước, `limit` 1–20 |
| GET | `/api/customers/{customerId}/opportunities?status=Open&limit=5` | Cơ hội bán, lọc trước khi cắt |
| GET | `/api/customers/{customerId}/campaigns?limit=5` | Chiến dịch khách hàng thuộc diện tham gia |
| DELETE | `/api/customers/{customerId}` | **Xoá mềm chỉ trong RAM** — `204`; `404` nếu không có hoặc đã xoá. Không bao giờ ghi vào `data/crm/customers.json` |

---

## 10. Hướng dẫn sử dụng

### Giao diện

Mở `http://localhost:5081` khi chưa đăng nhập sẽ bị chuyển về **`/Login`**. Đăng nhập bằng một trong
ba tài khoản ở §6.6; thanh trên cùng hiển thị **tên và vai** đang dùng, kèm nút **Đăng xuất**.

Sau khi đăng nhập là khung chat cùng các panel chỉ hiện khi lượt đó thực sự có dữ liệu:

| Panel | Hiện khi |
| --- | --- |
| **Khách hàng** | `get_customer` thành công |
| **Nhiều khách hàng trùng tên** | Kết quả `ambiguous` — chọn đúng mã rồi hỏi lại |
| **Tương tác gần đây** | `get_interactions` thành công |
| **Cơ hội bán** | `get_opportunities` thành công |
| **Chiến dịch** | `get_campaigns` thành công |
| **Kịch bản gọi** | `generate_call_script` thành công (mở đầu, câu hỏi khai thác, điểm trao đổi chính, xử lý từ chối, kết thúc) |
| **Email nháp** | `generate_email` thành công (tiêu đề, nội dung, nhãn cần duyệt, source chip) |
| **Tool trace & sources** | Luôn có sau mỗi lượt — accordion thu gọn |

Nút **New conversation** gọi `DELETE /api/chat/sessions/{sessionId}`, xoá state phía server rồi sinh
`sessionId` mới ở trình duyệt. Trong lúc một request đang chạy, ô nhập và các nút đều bị khoá để
tránh gửi trùng và tránh tranh chấp giữa reset với lượt chat đang bay.

Nút **Giữ để nói** ghi âm khi bạn giữ chuột, tự dừng sau **15 giây**, rồi điền transcript vào ô nhập.
Nó **không tự gửi** — hãy đọc lại, sửa nếu máy nghe sai, rồi mới bấm **Gửi**. Nói lại lần nữa sẽ **ghi
đè** nội dung cũ, vì cách sửa tự nhiên nhất khi nghe sai là đọc lại chứ không phải đọc thêm.

### Đọc tool trace và source chip

Mỗi dòng trong tool trace là **một lần gọi MCP thật**: tên tool, `status`, `traceId`, thời gian (ms).
Source chip là `sourceIds` của lượt đó — mỗi chip là một bằng chứng có thể lần ngược:

| Tiền tố | Nghĩa |
| --- | --- |
| `crm:customer:CUS-0001` | Hồ sơ khách hàng |
| `crm:interaction:INT-0001` | Một tương tác |
| `crm:opportunity:OPP-0002` | Một cơ hội bán |
| `crm:campaign:CMP-0001` | Một chiến dịch |
| `kb:product:PRD-SAV-006M` | Tài liệu sản phẩm |
| `kb:email-template:TPL-EMAIL-…` | Mẫu email |
| `kb:call-script:…` | Playbook kịch bản gọi |

Source chip của bản nháp **chỉ chứa nguồn mà bản nháp thật sự dùng** — không liệt kê mọi ứng viên đã
truy xuất.

### Quy tắc quan trọng khi dùng chat

**Luôn tham chiếu khách hàng bằng mã (`CUS-0001`), không gõ tên đầy đủ.** Đây là hành vi có chủ đích:
gõ tên thật vào chat là một đường rò PII, nên `InputGuard` từ chối trước khi bất kỳ dữ liệu nào rời máy.

### Gọi API trực tiếp

```powershell
$sessionId = [guid]::NewGuid().ToString()

Invoke-RestMethod -Method Post http://localhost:5081/api/chat `
  -Body (@{ message = "Tìm hồ sơ khách hàng CUS-0001."; sessionId = $sessionId } | ConvertTo-Json) `
  -ContentType "application/json"

# Reset phiên — luôn trả 204, kể cả khi phiên chưa từng tồn tại
Invoke-RestMethod -Method Delete "http://localhost:5081/api/chat/sessions/$sessionId"
```

`sessionId` là **bắt buộc** ở mọi request và do phía client sinh — server không bao giờ tự cấp. Dùng
lại cùng một `sessionId` để giữ ngữ cảnh qua nhiều lượt.

Không dùng `curl` thô cho `/mcp`: đó là JSON-RPC trên Streamable HTTP, cần đúng header `Accept`. Hãy
dùng một MCP client thật (`McpClient` + `HttpClientTransport`, hoặc MCP Inspector).

---

## 11. Ví dụ câu hỏi

### Câu hỏi chạy được

Dùng cùng một phiên, hỏi lần lượt từ trên xuống để thấy conversation state hoạt động:

| # | Câu hỏi | Tool | Kết quả |
| --- | --- | --- | --- |
| 1 | Tìm hồ sơ khách hàng CUS-0001. | `get_customer` | Panel Khách hàng |
| 2 | Khách hàng này có những tương tác gần nhất nào? | `get_interactions` | Tương tác, mới nhất trước |
| 3 | Khách hàng này đang có cơ hội bán nào? | `get_opportunities` | Panel Cơ hội bán |
| 4 | Chỉ lấy các cơ hội đang mở thôi. | `get_opportunities` | Lọc `status = Open` |
| 5 | Khách hàng này thuộc chiến dịch nào? | `get_campaigns` | Panel Chiến dịch |
| 6 | Có sản phẩm tiết kiệm kỳ hạn 6 tháng nào phù hợp không? | `search_product_knowledge` | Câu trả lời có căn cứ + source chip |
| 7 | Soạn email follow-up ngắn gọn, chuyên nghiệp và thân thiện về nhu cầu gửi tiết kiệm 6 tháng cho khách hàng này. | `generate_email` | Email nháp + nhãn cần RM duyệt |
| 8 | Soạn kịch bản gọi điện follow-up cho khách hàng này về gói tiết kiệm 6 tháng. | `generate_call_script` | Kịch bản gọi + nhãn cần RM duyệt |
| 9 | Tìm khách hàng CUS-9999. | `get_customer` | `404 NOT_FOUND` — không bịa hồ sơ |

Câu 7 và 8 minh hoạ việc Host **bỏ argument model bịa ra**: nếu Gemini điền `productCode` bằng cụm chữ
"gửi tiết kiệm 6 tháng" thay vì một mã sản phẩm, Host bỏ giá trị đó và gấp cụm chữ vào `objective` —
để chính bước RAG (thành phần thật sự biết danh mục sản phẩm) phân giải nó.

### Câu phụ thuộc vai đăng nhập

| Câu | Đăng nhập bằng | Kết quả |
| --- | --- | --- |
| "Xoá khách hàng CUS-0001" | `admin01` | Thành công — mọi đường đọc sau đó trả `NOT_FOUND` |
| "Xoá khách hàng CUS-0002" | `rm01` | **403 `FORBIDDEN`** + dòng `DENIED` trong console McpServer |
| "Soạn email cho khách hàng này" | `auditor01` | **403 `FORBIDDEN`** — Auditor chỉ được đọc |

Xoá là **xoá mềm chỉ trong RAM** của Mock CRM API. Restart tiến trình đó là khách hàng quay lại;
`data/crm/customers.json` không bao giờ bị ghi.

Khi soạn email, dùng đúng câu **"Soạn email cho khách hàng này"**. Thêm tính từ đánh giá kiểu *"soạn
email hợp lý"* dễ khiến model đi tra cơ hội bán trước — một lời gọi tool sai, và vì tool CRM kết thúc
lượt ngay nên lượt đó dừng ở đó.

### Câu bị từ chối — có chủ đích, không phải lỗi

| Câu hỏi | Kết quả | Vì sao |
| --- | --- | --- |
| `Gửi email tới minh.anh@example.test` | `400 PII_REJECTED` | Email trong chat là đường rò PII |
| `Số điện thoại khách là 0900000001, kiểm tra giúp` | `400 PII_REJECTED` | Số điện thoại, tương tự trên |
| `Tìm khách hàng Nguyễn Minh Anh` | `400 CUSTOMER_ID_REQUIRED` | Tên đầy đủ trong chat là đường rò PII — dùng mã |
| `Tìm khách hàng CS-0002` | `400 CUSTOMER_ID_INVALID` | Sai định dạng; hệ thống **không** tự sửa thành mã hợp lệ |
| `Khách hàng này có tương tác nào?` ở **phiên mới** | `400 CUSTOMER_ID_REQUIRED` | Chưa có khách hàng nào trong phiên — hỏi lại còn hơn đoán |

Tra cứu theo tên **vẫn hoạt động ở tầng MCP tool** (`get_customer` với tham số `query`, có xử lý trùng
tên bằng `ambiguous`). Chỉ khung chat mới chặn, và chặn có chủ đích.

---

## 12. Kiểm thử

Bộ test mặc định chạy **hoàn toàn offline** — Gemini và Chroma đều là fake, không cần API key.
Tổng hiện tại: **529 test**, 523 pass, 5 skip (live gate, opt-in), 1 fail duy nhất là KL-05 phụ thuộc
môi trường máy — xem §13.

```powershell
# Toàn bộ
dotnet test tests/CrmCopilot.Tests/CrmCopilot.Tests.csproj --no-build --no-restore

# Bộ 8 scenario nghiệm thu (sinh báo cáo vào TestResults/)
dotnet test tests/CrmCopilot.Tests/CrmCopilot.Tests.csproj --no-build --no-restore `
  --filter-class "CrmCopilot.Tests.Acceptance.AcceptanceScenarioTests"
```

Dự án dùng **Microsoft.Testing.Platform** (khai báo trong `global.json`), không phải VSTest — các option
`--filter-class` / `--filter-method` / `--filter-namespace` là option gốc của `dotnet test`, truyền
thẳng, **không** qua dấu `--`.

### Ba lớp evidence — không thay thế lẫn nhau

| Lớp | Là gì | Chứng minh được | Không chứng minh được |
| --- | --- | --- | --- |
| **D** — offline xác định | MCP protocol thật in-memory, Gemini/CRM là fake | Contract, mã lỗi, phân giải theo state, cô lập phiên, PII gate, thứ tự/limit | Bất cứ điều gì về model thật hoặc ranking truy xuất thật |
| **L** — live gate (opt-in) | Gemini thật + Chroma thật + Mock CRM thật | Grounding thật, PII thật không rời máy | Trải nghiệm UI |
| **B** — browser demo | Chạy tay qua `http://localhost:5081` | Luồng đầu-cuối, trace/source hiển thị, độ ổn định | Assertion máy móc |

Ngưỡng đã khoá của bộ 8 scenario là **≥ 7/8**. `8/8` là mục tiêu chất lượng, không phải điều kiện pass.

Hai scenario tra cứu theo tên được đo ở **tầng MCP tool**, không qua khung chat — vì `InputGuard` cố ý
từ chối tên đầy đủ trong chat. Đó là control đã được duyệt, không phải khiếm khuyết.

### Live gate

Live test **không bao giờ** chạy trong bộ mặc định — chúng dùng `[Fact(SkipUnless = …)]` và chỉ bật khi
có đủ `GEMINI_API_KEY` + `CHROMA_BASE_URL` (thêm `MOCKCRM_API_BASE_URL` cho test có gọi CRM).

| Class | Chứng minh |
| --- | --- |
| `Knowledge.LiveRagAcceptanceTests` | Heartbeat, ingest toàn corpus, idempotency, canonical retrieval |
| `Email.LiveEmailGenerationAcceptanceTests` | Email draft grounded thật, khôi phục placeholder |
| `CallScript.LiveCallScriptGenerationAcceptanceTests` | Kịch bản gọi grounded thật, tự seed collection |
| `Acceptance.LiveAcceptanceScenarioTests` | Scenario T07/T08 ở lớp L |

**`Skipped` không bao giờ được tính là PASS.** Một live gate chưa chạy thì kết quả offline không được
mượn để thay thế.

Mọi live test ghi vào collection riêng `crm-copilot-knowledge-livetest`, **không bao giờ** ghi vào
collection mặc định. `LiveRagAcceptanceTests` assert tên collection đã phân giải **trước khi** ghi bất
cứ gì. Nếu cần dọn, chỉ xoá đúng collection live-test — không đụng collection mặc định và không xoá
Chroma volume.

Kỳ vọng về corpus được viết dưới dạng **thuộc tính, không phải con số cứng**: nguồn không rỗng,
`sourceId` duy nhất, có đủ ba loại document, số vector khớp số document nguồn, lần ingest thứ hai
`Embedded == 0`. Nhờ vậy corpus lớn lên không làm test sai, trong khi document trùng vẫn bị bắt.

---

## 13. Giới hạn đã biết

- **Conversation state là in-memory.** Restart `CrmCopilot.Web` — kể cả bằng
  `docker compose restart web` — là mất toàn bộ phiên. Không có TTL, không có idle-expiry.
- **`InputGuard` là best-effort và cố ý thiên về từ chối nhầm.** Nó có thể chặn một câu hỏi hợp lệ; đó
  là hướng thất bại được chấp nhận. Loại bỏ hoàn toàn rủi ro cần hạ tầng NER thật.
- **Gọi trực tiếp MCP tool bỏ qua `InputGuard`.** Các tool ngoài `get_customer` khi nhận `customerId`
  sai định dạng qua MCP trực tiếp sẽ trả `NOT_FOUND` thay vì `CUSTOMER_ID_INVALID`. Luồng qua trình
  duyệt không bị ảnh hưởng vì Host đã chặn từ trước.
- **Docker Compose chỉ dành cho local.** Đăng nhập cookie + JWT trong bản demo **không phải**
  production-grade; chưa có TLS termination, secret manager, CI/CD hay cloud deploy. Port chỉ bind vào `127.0.0.1`, không expose
  ra mạng ngoài.
- **`/health` chỉ là liveness.** Nó trả `200` ngay khi process boot xong và **không** probe Chroma,
  Mock CRM hay Gemini. Vì vậy `depends_on: service_healthy` trong `compose.yaml` chứng minh tiến trình
  đã sống, chứ không chứng minh dependency đã sẵn sàng.
- **Một test phụ thuộc môi trường máy.** `MockCrmGatewayOptionsTests.Host_WithNoBaseUrlConfiguredAtAll_FailsToStart`
  dùng `WebApplicationFactory` trần, không có tombstone `null` như các factory anh em, nên nếu máy có
  file `src/CrmCopilot.McpServer/appsettings.Development.json` (bị `.gitignore` chặn, không có trong
  repo) thì `MOCKCRM_API_BASE_URL` rò vào và test này fail. Chạy với `ASPNETCORE_ENVIRONMENT=Production`
  thì bộ test trở lại 529/523/0/5. Đây là khiếm khuyết của test, chưa sửa.
- **Ngưỡng `MaxDistance` mặc định `1.2`** là điểm khởi đầu có lập luận; hiệu chỉnh bằng
  `--query-knowledge` trên khoảng cách thật.

- **Server không ép thời lượng audio.** Giới hạn 15 giây nằm ở client; server chỉ chặn ở **1 MB**
  (≈ 73 giây với `audio/webm;codecs=opus`). Client bị sửa vẫn gửi được audio dài hơn, nhưng luôn bị
  chặn bởi trần byte. Ép thời lượng thật cần parse container webm — ngoài phạm vi tối thiểu.
- **`/api/transcribe` tắt antiforgery.** Minimal API bind `IFormFile` bắt buộc như vậy khi không có
  `UseAntiforgery()`. Hệ quả: một POST cross-origin kèm cookie của nạn nhân có thể tiêu quota Gemini.
  Response không đọc được cross-origin và không có dữ liệu nào bị thay đổi.
- **Transcribe bằng LLM có thể bịa khi audio kém.** Model điền vào một câu nghe hợp lý thay vì trả về
  rỗng — đã gặp thật với micro tích hợp của laptop. Đây chính là lý do transcript **không tự gửi**:
  RM phải đọc lại trước khi nội dung đi tiếp.
- **Gemini có thể trả `503` khi model quá tải.** Hệ thống bắt `ServerError`, trả `502 MODEL_ERROR` kèm
  thông báo tiếng Việt, không lộ stack trace ra trình duyệt. Cách xử lý là thử lại sau vài giây.
- **`CLAUDE.md` và `docs/` tạm thời không nằm trong repo** — xem §14.

Verdict, blocker và verification debt chi tiết nằm ở `docs/CHECKPOINT_STATUS.md` (hiện đọc từ lịch sử
git, xem §14).

---

## 14. Bản đồ tài liệu

> **Lưu ý tạm thời.** `CLAUDE.md` và toàn bộ `docs/` đã bị gỡ khỏi repo ở PR #24 và sẽ được khôi phục.
> Nội dung vẫn nằm nguyên trong lịch sử git tại commit `441fa69`:
>
> ```powershell
> git show 441fa69:docs/07_MCP_TOOL_CONTRACTS.md      # đọc một file
> git checkout 441fa69 -- CLAUDE.md docs/             # khôi phục toàn bộ
> ```
>
> Mọi tham chiếu `docs/…` trong mã nguồn và trong file này đều trỏ tới bộ tài liệu đó.

| File | Nội dung |
| --- | --- |
| `CLAUDE.md` | Quy tắc bắt buộc khi làm việc trong repo |
| `docs/01_PROJECT_DECISIONS.md` | Các quyết định kiến trúc/phạm vi đã khoá |
| `docs/02_ARCHITECTURE.md` | Thành phần, luồng dữ liệu, ownership, trust boundary |
| `docs/03_ACCEPTANCE_CRITERIA.md` | Tiêu chí pass/fail và 8 test scenario |
| `docs/04_P0_CHECKPOINTS.md` | Chia P0 thành checkpoint có gate |
| `docs/05_IMPLEMENTATION_PLAN_9_DAYS.md` | Lịch triển khai |
| `docs/06_DATA_AND_MOCK_API_SPEC.md` | Dataset, schema, endpoint, adapter |
| `docs/07_MCP_TOOL_CONTRACTS.md` | Contract tool và lỗi chuẩn hoá |
| `docs/08_RAG_EMAIL_AND_PII_SPEC.md` | Ingestion, retrieval, sinh email, masking |
| `docs/09_WORKFLOW_GUIDE.md` | Quy trình làm việc |
| `docs/10_PROMPTS.md` | Prompt mẫu theo checkpoint |
| `docs/11_DEMO_RUNBOOK.md` | Kịch bản demo, fallback, checklist |
| `docs/12_POST_MVP_AND_INTEGRATION.md` | Hướng mở rộng sau MVP |
| `docs/13_REFERENCE_SOURCES.md` | Nguồn chính thức và ngày kiểm chứng |
| `docs/14_ACCEPTANCE_SCENARIO_REPORT.md` | Kết quả 8 scenario theo từng lớp evidence |
| `docs/15_PLAN_P0-12_TO_P0-15.md` | Kế hoạch + kết quả spike cho đăng nhập, phân quyền MCP, speech-to-text |
| `docs/CHECKPOINT_STATUS.md` | Sổ trạng thái, evidence, blocker, quyết định review |
