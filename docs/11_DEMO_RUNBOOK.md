# 11 — Demo Runbook

## 1. Mục tiêu demo

Trong 5–7 phút, chứng minh bốn điểm:

1. RM hỏi bằng tiếng Việt.
2. Gemini/AI Host chọn và gọi tool qua MCP thật.
3. State giữ đúng customer qua nhiều lượt.
4. Email draft dùng RAG + PII masking + citations, và vẫn cần con người duyệt.

## 2. Preflight

Trước demo 15 phút:

- Repo ở commit/tag đã test; worktree sạch hoặc biết rõ diff.
- Gemini key có trong secret store/env, không hiển thị trên màn hình.
- Mock CRM health OK.
- MCP Server health/endpoint OK.
- Chroma heartbeat OK và collection có expected document count.
- Web health OK.
- Canonical customer/product/template tồn tại.
- Chạy smoke test T01, T05, T07.
- Mở sẵn tool trace panel nhưng thu gọn.
- Tắt notification và ẩn terminal chứa environment variables.
- Có fallback screenshot/video/log đã sanitized nếu network Gemini lỗi.

## 3. Kịch bản chính

### Bước 1 — Customer lookup

RM nhập:

> Tìm hồ sơ khách hàng CUS-0001.

Kỳ vọng:

- UI hiện `Nguyễn Minh Anh`, segment, city/status.
- Trace hiện `get_customer` qua MCP.
- Source hiện `crm:customer:CUS-0001`.

Thông điệp trình bày:

> Dữ liệu khách hàng là structured CRM data nên được lấy qua MCP tool, không qua vector search và không để model tự nhớ.

### Bước 2 — Multi-turn interaction

RM nhập:

> Khách hàng này có những tương tác gần nhất nào?

Kỳ vọng:

- Không hỏi lại ID.
- Trace `get_interactions` có argument `CUS-0001`.
- Interactions newest-first, có interaction nhu cầu gửi tiết kiệm 6 tháng.

Thông điệp:

> AI Host lưu entity ID trong conversation state; MCP Server vẫn stateless và nhận ID explicit.

### Bước 3 — Email draft dựa trên RAG

RM nhập:

> Soạn email follow-up ngắn gọn, chuyên nghiệp và thân thiện về nhu cầu gửi tiết kiệm 6 tháng cho khách hàng này.

Kỳ vọng:

- Trace `generate_email` và/hoặc nested RAG trace.
- Product `PRD-SAV-006M` nằm trong evidence.
- Template source hiện.
- Draft có subject/body tiếng Việt.
- Không bịa lãi suất/ưu đãi.
- Có nhãn “Cần RM kiểm tra và phê duyệt”.

Thông điệp:

> Hệ thống retrieve product/template bằng Gemini embedding + Chroma, mask PII trước Gemini, validate source và chỉ tạo draft — không gửi email.

### Bước 4 — Minh bạch

Mở tool trace/source panel để chỉ:

- tool name;
- duration/status;
- source IDs;
- `requiresHumanApproval`;
- masked field types (không hiển thị giá trị PII).

## 4. Negative path ngắn (nếu còn thời gian)

Nhập:

> Tìm khách hàng CUS-9999.

Kỳ vọng: “Không tìm thấy”, không tạo hồ sơ giả.

Hoặc dùng tên trùng để cho thấy Agent yêu cầu chọn ID thay vì đoán.

## 5. Cách nói về accuracy

Đúng:

> Nhóm tự xây dựng 8 scenario MVP bám vào luồng demo; hệ thống hiện đạt X/8. Đây là chỉ số nội bộ để kiểm soát chất lượng, chưa phải benchmark chính thức của Bank A.

Không nói:

> Hệ thống đạt 87,5% accuracy trong ngân hàng thực tế.

## 6. Cách nói về bảo mật

Đúng:

> MVP chỉ dùng dữ liệu tổng hợp và triển khai field-based masking cùng regex fallback trước Gemini/log. Đây là control minh họa cho kiến trúc, chưa thay thế DLP/compliance review production.

Không nói:

> Hệ thống đã tuân thủ đầy đủ mọi quy định ngân hàng.

## 7. Fallback plan

| Sự cố | Fallback |
| --- | --- |
| Gemini quota/network | Hiển thị sanitized captured successful run; giải thích live external dependency |
| Chroma lỗi | Không dùng draft giả; trình bày retrieval test/report và lỗi controlled |
| UI lỗi | Gọi API/host endpoint bằng prepared request và mở tool trace |
| MCP transport lỗi | Dùng MCP Inspector/test client để chứng minh server tools; không gọi CRM trực tiếp |
| Demo data sai | Không sửa live; chuyển negative/fallback artifact và ghi blocker |

Fallback artifact phải được tạo từ run thật trước demo và không chứa key/PII thô.

## 8. Checklist kết thúc

- [ ] Customer lookup đúng.
- [ ] Multi-turn đúng customer.
- [ ] Tool trace chứng minh MCP.
- [ ] RAG sources hiển thị.
- [ ] Email draft grounded.
- [ ] PII masking được giải thích bằng evidence an toàn.
- [ ] Human approval rõ.
- [ ] Limitation và post-MVP path được nói trung thực.

## 9. Q&A dự kiến

**Vì sao không dùng HubSpot ngay?**  
Adapter-first: mock loại bỏ blocker và chứng minh flow; sau khi P0 pass có thể thêm `HubSpotCrmGateway` mà giữ MCP contracts.

**MCP có phải nơi lưu hội thoại không?**  
Không. Host quản lý conversation state; MCP chuẩn hóa tool/context exchange với server.

**RAG nằm ở đâu?**  
Product knowledge/email templates được embed bằng Gemini và retrieve từ Chroma trước khi tạo email.

**Nếu restart thì sao?**  
P0 mất in-memory session; persistence/Redis là post-MVP.

**Email có gửi thật không?**  
Không. Chỉ tạo draft và bắt buộc RM phê duyệt.

**Có deploy cloud không?**  
Kiến trúc HTTP/container-ready; cloud là bonus sau khi core local pass để không ảnh hưởng 7–9 ngày.

