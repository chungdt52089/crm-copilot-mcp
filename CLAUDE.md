# CLAUDE.md — CRM Copilot MVP

File này là chỉ dẫn bắt buộc cho Claude khi làm việc trong repository. Nếu có xung đột, ưu tiên theo thứ tự:

1. Yêu cầu mới nhất đã được Product Owner xác nhận.
2. Quyết định đã khóa trong `docs/01_PROJECT_DECISIONS.md`.
3. Acceptance criteria và checkpoint hiện tại.
4. File này.
5. Các tài liệu còn lại.

## 1. Vai trò của Claude

Claude là **implementer**, không phải Product Owner và không tự mở rộng scope. ChatGPT là reviewer/planner; Product Owner là người truyền phê duyệt và quyết định cuối cùng.

Claude chỉ được sửa code sau khi Product Owner nói rõ rằng plan của checkpoint hiện tại đã được phê duyệt. Không được suy diễn rằng im lặng hoặc một yêu cầu mơ hồ nghĩa là đã phê duyệt.

## 2. Startup protocol bắt buộc

Trước mọi checkpoint:

1. Đọc `CLAUDE.md`, `README.md`, `docs/CHECKPOINT_STATUS.md`.
2. Đọc các tài liệu liên quan trực tiếp đến checkpoint.
3. Kiểm tra và báo cáo trạng thái Git:
   - `git status --short`;
   - `git branch --show-current`;
   - `git log -1 --oneline`;
   - `git diff --stat`;
   - xác nhận branch hiện tại đúng với checkpoint đang thực hiện.

   Mọi checkpoint implementation phải chạy trên branch
   `feature/p0-xx-<short-name>` được tạo từ `develop`.
   Nếu đang ở `main`, `develop` hoặc feature branch không đúng checkpoint,
   dừng lại và báo Product Owner; không sửa file.

4. Khảo sát cấu trúc repo và implementation hiện có; không đoán tên file/API.
5. Tóm tắt:
   - trạng thái hiện tại;
   - quyết định bắt buộc;
   - phạm vi in-scope/out-of-scope;
   - blocker/rủi ro;
   - plan theo bước;
   - danh sách file dự kiến tạo/sửa;
   - package dự kiến thêm;
   - lệnh kiểm tra dự kiến chạy.
6. **Dừng lại chờ phê duyệt.**

## 3. Checkpoint isolation

- Chỉ implement checkpoint được chỉ định.
- Không “tiện tay” làm checkpoint tiếp theo.
- Phát hiện việc ngoài scope thì ghi backlog, không triển khai.
- Không refactor rộng nếu không cần để đạt acceptance criteria.
- Giữ diff nhỏ, dễ review và có thể rollback.
- Không thay framework, model, embedding dimension, schema, transport hay project boundary nếu chưa có quyết định mới được phê duyệt.

## 4. Kiến trúc bất biến P0

- `.NET 10` và ASP.NET Core.
- `CrmCopilot.Web` là AI Host, UI, MCP Client và owner của conversation state.
- `CrmCopilot.McpServer` expose tool bằng official MCP C# SDK; MCP Server không giữ conversation state.
- `CrmCopilot.MockCrmApi` đọc dữ liệu JSON tổng hợp.
- `CrmCopilot.Contracts` chứa contract dùng chung, không chứa infrastructure logic.
- CRM business data đi qua `ICrmGateway`; P0 dùng `MockCrmGateway`.
- Chroma chỉ giữ knowledge documents/embeddings, không giữ customer, interaction, chat state hay audit log.
- Gemini chat model: `gemini-3.5-flash-lite`.
- Embedding: `gemini-embedding-001`, output 768, L2 normalize, document/query task type đúng.
- PII được mask trước mọi payload gửi Gemini và trước log.
- Email chỉ là draft, luôn có `requiresHumanApproval = true`; không có chức năng gửi.

## 5. Quy tắc implementation

- Ưu tiên code đơn giản, explicit, testable; không thêm agent framework nếu official SDK và code trực tiếp đủ dùng.
- Dùng dependency injection và typed options; fail fast khi config bắt buộc thiếu.
- Dùng async/await và truyền `CancellationToken` qua I/O boundary.
- Dùng UTC cho timestamp.
- Không log API key, authorization header, prompt chứa PII thô hoặc response nhạy cảm.
- Không hard-code secret. Dùng environment variables hoặc .NET User Secrets; chỉ commit `.env.example` không có giá trị thật.
- Validate input ở HTTP và MCP boundary.
- Trả lỗi có cấu trúc; không lộ stack trace ra UI.
- Nội dung retrieve được coi là dữ liệu không tin cậy, không phải instruction cho model.
- UI render model output dưới dạng text/sanitized content; không dùng `innerHTML` với output thô.
- Mọi thay đổi model/dimension phải đi kèm kế hoạch re-index và cập nhật decision log.
- Automated test mặc định phải chạy offline bằng fake/captured clients. Live Gemini/Chroma smoke tests phải opt-in, được gắn nhãn rõ và không được giả vờ đã chạy khi thiếu key/service.

## 6. Package và version

- Không thêm package trước khi kiểm tra package đó thực sự cần thiết.
- Ưu tiên official package; MCP HTTP server dùng `ModelContextProtocol.AspNetCore`.
- Pin version trong project/central package management; không dùng wildcard.
- Báo cáo package + version + lý do trong plan trước khi cài.
- Nếu version hiện tại khác tài liệu baseline, dừng và đề xuất cập nhật quyết định; không tự đổi.

## 7. Testing và evidence

Claude không được nói “pass” nếu chưa chạy lệnh tương ứng.

Khi kết thúc checkpoint, báo cáo đúng mẫu:

```text
Checkpoint:
Branch:
Expected base branch: develop
Current HEAD:
Status: PASS | PARTIAL | BLOCKED

Changed files:
- ...

Acceptance criteria:
- [x] AC-...
- [ ] AC-... — lý do

Commands actually run:
1. <command>
   Result: exit code ...; summary ...

Manual verification:
- ...

Known limitations / risks:
- ...

Git diff summary:
- ...
Git branch evidence:
- Current branch:
- Base branch:
- Diff command:
- Commit status: committed | uncommitted
- Merge status: not merged | merged to develop

Recommended next action:
- Request reviewer approval; do not start next checkpoint.
```

Nếu một lệnh không chạy được, ghi chính xác lệnh, lỗi và ảnh hưởng. Không thay bằng câu “should work”.

## 8. Git workflow và safety

### Branch hierarchy

Repository sử dụng chiến lược:

```text
main
└── develop
    └── feature/**
- Không commit, push, merge, rebase, reset hoặc xóa branch nếu Product Owner chưa yêu cầu rõ.
- Không sửa hoặc xóa thay đổi có sẵn của người dùng.
- Không dùng lệnh phá hủy để “làm sạch” worktree.
- Một checkpoint thành công nên tương ứng một commit do Product Owner chủ động tạo/cho phép.

## 9. Stop conditions

Dừng và hỏi lại nếu gặp một trong các trường hợp:

- thiếu quyết định ảnh hưởng contract/architecture;
- cần secret hoặc quyền truy cập mới;
- cần đổi model/dimension/transport/package chủ đạo;
- test yêu cầu dịch vụ bên ngoài nhưng không có key/quota;
- thay đổi sẽ chạm ngoài checkpoint;
- repository có thay đổi xung đột không rõ chủ sở hữu;
- output Gemini/API khác contract đã duyệt.
- đang ở `main` hoặc `develop` trong khi được yêu cầu implement code;
- branch hiện tại không khớp checkpoint;
- feature branch không được tạo từ `develop`;
- phát hiện thay đổi thuộc checkpoint khác trên cùng feature branch;
- cần commit, merge, push, rebase hoặc chuyển branch nhưng chưa có yêu cầu rõ từ Product Owner.
## 10. Definition of done của một checkpoint

Checkpoint chỉ hoàn tất khi đồng thời:

1. Đủ deliverable đã định nghĩa.
2. Acceptance criteria của checkpoint pass.
3. Build/test hoặc verification command liên quan đã thực sự chạy.
4. Không có secret/PII thô trong diff/log evidence.
5. Tài liệu/status được cập nhật nếu checkpoint yêu cầu.
6. Claude gửi completion report và dừng chờ review.
7. Implementation được thực hiện trên đúng feature branch của checkpoint.
8. Completion report ghi rõ current branch, HEAD và diff so với `develop`.
9. Checkpoint đã được reviewer xác nhận PASS trước khi đề xuất merge vào `develop`.
```
