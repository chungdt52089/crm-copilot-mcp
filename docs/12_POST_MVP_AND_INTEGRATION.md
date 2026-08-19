# 12 — Post-MVP and Integration

## 1. Backlog theo ưu tiên

### P1 — Chỉ làm nếu P0 pass sớm

1. `get_opportunities`.
2. `generate_call_script` dựa trên RAG.
3. Dockerfile + `compose.yaml` cho toàn bộ stack.
4. UI polish nhỏ và export/copy draft.

### P2 — Sau demo

1. `get_campaigns`.
2. `HubSpotCrmGateway`.
3. Cloud deployment.
4. Persistent conversation state (Redis/Postgres).
5. Auth/RBAC và tenant/user scoping.
6. Centralized audit store/observability.

### Production horizon

- Bank A real adapter và contract validation.
- Secret manager, encryption, TLS, network allowlist.
- Authorization theo RM/customer portfolio.
- Consent, retention, deletion, audit review.
- DLP/NER, threat modeling, red-team prompt injection.
- Evaluation dataset có chuyên gia nghiệp vụ.
- Human approval workflow và CRM write-back có kiểm soát.
- HA, backup, capacity, cost và incident response.

## 2. HubSpot integration pathway

### Điều kiện bắt đầu

- P0-02 đến P0-09 PASS.
- MCP tool contracts ổn định.
- Có HubSpot developer test account và token hợp lệ.
- Product Owner phê duyệt scope chỉ-read.

### Thiết kế

- Thêm `HubSpotCrmGateway : ICrmGateway`.
- Dùng neutral DTO hiện có; mapping vendor nằm trong adapter.
- Config chọn provider `Mock|HubSpot`.
- Không để MCP tool biết HubSpot endpoint/property name.
- Bắt đầu Contact + activity/association cần thiết; Deal sau.
- Dữ liệu HubSpot test vẫn nên là synthetic.

### Mapping dự kiến

| Domain | HubSpot |
| --- | --- |
| Customer | CRM Contact |
| Interaction | Calls, emails, notes, meetings và associations |
| Opportunity | Deal |
| Campaign | Marketing/campaign API tùy account và scope |

Chi tiết API/scopes cần được kiểm chứng tại thời điểm implement; không hard-code dựa trên tài liệu cũ.

## 3. Docker packaging

Sau P0 local:

- Multi-stage Dockerfile cho Web, MCP Server, Mock CRM API.
- Chroma official image + named volume.
- Healthchecks và dependency readiness.
- Non-root container khi khả thi.
- Env-based endpoints và secrets; không bake key vào image.
- `compose.yaml` có profile demo và volume rõ.

Acceptance bonus:

- `docker compose up --build` khởi động stack từ clean state.
- Seed idempotent.
- Health pass; canonical demo pass.
- Restart app không mất Chroma index volume; conversation state mất vẫn được ghi limitation.

## 4. Cloud deploy tối giản

Mục tiêu là chứng minh deployability, không production banking:

- Một managed container/web platform cho các .NET services.
- Chroma container có persistent volume hoặc dịch vụ vector phù hợp.
- HTTPS, environment secrets và health probes.
- CORS origin explicit.
- Budget/quota guard cho Gemini.
- Không public Mock CRM/MCP endpoint nếu không cần; Web là entry point.

Nếu platform không hỗ trợ persistent Chroma ổn định trong timebox, giữ local Docker demo thay vì đổi vector database phút cuối.

## 5. Opportunity và call script

P1 vertical slice:

> Customer → interactions → opportunities → product knowledge → call script draft.

Giữ cùng pattern:

- opportunity structured lookup qua MCP/CRM;
- script knowledge qua Chroma RAG;
- PII masking;
- structured output + sources;
- human approval;
- không thực hiện cuộc gọi.

## 6. Những thứ không nên thêm sớm

- Multi-agent orchestration.
- Fine-tuning.
- Knowledge graph.
- Voice/live calling.
- Salesforce song song HubSpot.
- Kubernetes/microservice platform.
- Autonomous next-best-action write-back.

Chỉ thêm khi có acceptance criterion cụ thể và P0 đã ổn định.

