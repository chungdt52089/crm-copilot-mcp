# 10 — Prompt Pack for Claude and ChatGPT

Các prompt dưới đây dùng theo checkpoint. Thay placeholder trong dấu `<...>`. Không dùng một prompt khổng lồ cho toàn dự án.

## 1. Prompt mở checkpoint — Claude chỉ lập plan

```text
You are the implementation engineer for the CRM Copilot MVP repository.

Current checkpoint: <P0-XX — NAME>.

Before proposing anything:
1. Read CLAUDE.md, README.md, docs/CHECKPOINT_STATUS.md.
2. Read the checkpoint and all directly relevant specs in docs/.
3. Inspect the current repository, git status, current branch, and relevant diffs.
4. Do not edit files, install packages, or run mutating commands yet.

Return:
- current repository state;
- decisions and acceptance criteria that constrain this checkpoint;
- in-scope and explicitly out-of-scope work;
- step-by-step implementation plan;
- exact files to create/modify and why;
- packages and pinned versions to add, if any, and why;
- commands/tests you will actually run;
- risks, assumptions, and blockers;
- any decision that needs approval.

Stop after the plan and wait for explicit approval. Do not implement the next checkpoint.
```

## 2. Prompt gửi ChatGPT để review plan Claude

```text
Bạn là reviewer/planner của dự án CRM Copilot MVP. Hãy review plan của Claude cho checkpoint <P0-XX> dựa trên CLAUDE.md, project decisions, architecture, acceptance criteria và checkpoint spec đã chốt.

Kiểm tra: scope, kiến trúc Host/MCP/CRM/RAG, MCP thật, PII/secrets, package/version, test evidence, rủi ro tiến độ và khả năng rollback.

Trả về:
1. Verdict: APPROVED / APPROVED WITH REQUIRED CHANGES / REVISION REQUIRED / BLOCKED.
2. Các vấn đề theo mức Critical/Major/Minor.
3. Plan đã chỉnh ở dạng hành động cụ thể.
4. Acceptance/evidence Claude phải cung cấp khi hoàn tất.
5. Nội dung ngắn gọn tôi có thể copy lại cho Claude.

Checkpoint spec:
<PASTE OR POINT TO SPEC>

Claude plan:
<PASTE CLAUDE PLAN>

Repository evidence hiện tại:
<GIT STATUS / TREE / RELEVANT NOTES>
```

## 3. Prompt cho Claude sau khi plan được duyệt

```text
Plan cho <P0-XX> đã được phê duyệt.

Required changes/conditions from the reviewer:
<PASTE EXACT CONDITIONS OR "None">

Implement only this approved checkpoint. Follow CLAUDE.md. Do not start any later checkpoint, do not commit or push, and do not change an approved architecture/model/schema decision.

Run the approved verification commands. If a blocker requires a material change, stop and report it instead of choosing a new architecture yourself.

When finished, provide the exact completion report required by CLAUDE.md, including changed files, acceptance mapping, commands actually run with results, manual checks, known limitations, and git diff summary. Then stop for review.
```

## 4. Prompt kiểm tra giữa checkpoint

```text
Pause implementation and provide a checkpoint progress report only.

Include:
- completed items mapped to the approved plan;
- current uncommitted changed files;
- commands/tests already run and exact results;
- current blocker or uncertainty;
- whether any decision/package/schema/contract has changed;
- remaining steps and estimate;
- smallest safe options to proceed.

Do not make additional changes until I respond.
```

## 5. Prompt gửi ChatGPT để audit checkpoint

```text
Hãy audit kết quả checkpoint <P0-XX> của CRM Copilot MVP.

Đối chiếu với plan đã duyệt, project decisions và acceptance criteria. Phân biệt rõ evidence đã chạy với claim chưa được chứng minh. Kiểm tra scope creep, boundary MCP/Host/RAG, PII/secrets, error handling và test coverage.

Trả về:
1. Verdict: PASS / PASS WITH FOLLOW-UP / REWORK / BLOCKED.
2. Bảng acceptance criterion → evidence → kết luận.
3. Findings theo Critical/Major/Minor với file/vị trí nếu có.
4. Required fixes trước khi đóng checkpoint.
5. Nếu PASS, checkpoint tiếp theo và điều kiện mở.
6. Dòng cập nhật đề xuất cho docs/CHECKPOINT_STATUS.md.

Approved plan:
<PASTE PLAN/APPROVAL>

Claude completion report:
<PASTE REPORT>

Git diff/status/test output or repository:
<ATTACH OR PASTE EVIDENCE>
```

## 6. Prompt rework cho Claude

```text
Checkpoint <P0-XX> was reviewed as REWORK.

Fix only these required findings:
<PASTE NUMBERED FINDINGS>

Constraints:
- preserve all unrelated user changes;
- do not refactor outside the affected files;
- do not add packages or change contracts unless explicitly listed;
- run the exact regression commands below;
- do not start the next checkpoint or commit/push.

Required verification:
<PASTE COMMANDS/ASSERTIONS>

After fixing, return a delta report: files changed since the previous report, each finding and its fix, commands/results, remaining risks, and diff summary. Then stop.
```

## 7. Prompt chẩn đoán blocker — không sửa code

```text
Diagnose this blocker for checkpoint <P0-XX> without changing files:
<ERROR / LOG / SYMPTOM>

Inspect relevant code/config and report:
- most likely root cause with evidence;
- alternative hypotheses;
- smallest safe fix options and trade-offs;
- files/packages/decisions each option affects;
- commands to validate the diagnosis;
- your recommendation.

Do not implement until a fix option is approved.
```

## 8. Prompt security/PII review cho Claude

```text
Perform a read-only security and PII review for checkpoint P0-07.

Trace data from UI/CRM through state, logs, MCP tool arguments/results, retrieval query, Gemini requests, model output, and UI rendering. Check the acceptance criteria in docs/08_RAG_EMAIL_AND_PII_SPEC.md.

Return findings with severity, evidence, exploit/leak scenario, smallest remediation, and tests that prove the fix. Pay special attention to raw PII in recent messages, logs, exception text, captured prompts, citations, placeholder restoration, and unsafe HTML rendering.

Do not edit files.
```

## 9. Prompt final demo readiness

```text
Prepare a read-only final readiness report for P0-09.

Use docs/03_ACCEPTANCE_CRITERIA.md and docs/11_DEMO_RUNBOOK.md. Run only the already-approved non-destructive build/test/health/demo verification commands.

Report:
- environment/config preflight;
- 8-scenario result table;
- three consecutive canonical demo run results;
- MCP tool discovery/trace evidence;
- RAG source/citation evidence;
- PII capture/log evidence;
- known limitations and honest demo wording;
- blockers ranked by demo risk.

Do not implement fixes, commit, push, or start post-MVP work.
```

## 10. Prompt dành riêng cho P0-01 lần đầu

```text
Plan P0-01 only. Do not scaffold yet.

In addition to the normal planning prompt, verify the locally installed .NET SDK and propose the exact official MCP C# packages needed for an HTTP-based MCP server/client. Pin versions; do not use wildcard versions. Explain why every project boundary exists and keep the solution to Web, McpServer, MockCrmApi, Contracts, and Tests.

Do not introduce PostgreSQL, Redis, a frontend framework, an agent framework, HubSpot, or Docker orchestration in this checkpoint.
```

## 11. Checkpoint-specific prompt — P0-02

You are the implementation engineer for the CRM Copilot MVP repository.

Current checkpoint: P0-02 — Synthetic Data & Mock CRM API.
Expected branch: feature/p0-02-mock-crm.
Base branch: develop.

Work in plan-only mode.

Before proposing the plan:

1. Read:
   - CLAUDE.md
   - README.md
   - docs/CHECKPOINT_STATUS.md
   - docs/01_PROJECT_DECISIONS.md
   - docs/02_ARCHITECTURE.md
   - docs/03_ACCEPTANCE_CRITERIA.md
   - docs/04_P0_CHECKPOINTS.md
   - docs/05_IMPLEMENTATION_PLAN_9_DAYS.md

2. Verify:
   - P0-01 is marked DONE.
   - Current branch is feature/p0-02-mock-crm.
   - The branch was created from the latest develop.
   - Working tree does not contain unrelated changes.
   - Existing solution still restores, builds, and tests successfully.

3. Inspect the existing Contracts and MockCrmApi projects.

Checkpoint goals:

- Define stable contracts for Customer and Interaction.
- Create deterministic synthetic CRM data owned by this repository.
- Implement read-only Mock CRM API endpoints for:
  - customer lookup;
  - customer search only if required by the checkpoint specification;
  - interaction history by customer.
- Customer and Interaction are mandatory.
- Preserve stable IDs and valid Customer–Interaction relationships.
- Return explicit validation, not-found, and success responses.
- Keep the data design extensible for Opportunity and Campaign without implementing them now.
- Propose a deterministic generation strategy that can scale from a small demo dataset to hundreds or thousands of records without manually maintaining large JSON files.
- Clearly propose the initial checked-in dataset size and the optional scale profile. Do not generate an unnecessarily large dataset without approval.

Required tests/evidence to plan:

- JSON/schema deserialization.
- Required fields and unique IDs.
- Every interaction references an existing customer.
- Deterministic generation from a fixed seed.
- Customer found/not-found.
- Interaction history found/empty/not-found behavior.
- Mock API health remains healthy.
- Offline and repeatable test execution.

Explicitly out of scope:

- HubSpot or any real CRM API.
- Database persistence.
- Gemini, embeddings, Chroma, or RAG.
- MCP SDK or MCP tools.
- Email generation or call scripts.
- UI and conversation state.
- Docker/cloud deployment.
- Opportunity and Campaign unless the checkpoint specification explicitly makes them mandatory.

Return:

- repository and Git state;
- decisions and acceptance criteria;
- exact data model and API contract proposal;
- proposed dataset size and deterministic scale strategy;
- in-scope and out-of-scope work;
- step-by-step implementation plan;
- exact files to create/modify;
- package/version changes, if any;
- verification commands and tests;
- risks, assumptions, blockers, and approval questions.

Do not edit files, install packages, generate data, commit, push, merge, or start P0-03.

Stop after the plan and wait for explicit approval.

## 12. Checkpoint-specific prompt — P0-03

You are the implementation engineer for the CRM Copilot MVP repository.

Current checkpoint: P0-03 — Gemini Embedding & Chroma RAG.
Expected branch: feature/p0-03-rag-chroma.
Base branch: develop.

Work in plan-only mode.

Before proposing the plan:

1. Read CLAUDE.md, README.md, docs/CHECKPOINT_STATUS.md and the relevant sections of:
   - docs/01_PROJECT_DECISIONS.md
   - docs/02_ARCHITECTURE.md
   - docs/03_ACCEPTANCE_CRITERIA.md
   - docs/04_P0_CHECKPOINTS.md
   - docs/05_IMPLEMENTATION_PLAN_9_DAYS.md
   - docs/08_RAG_EMAIL_AND_PII_SPEC.md
   - docs/13_REFERENCE_SOURCES.md

2. Verify:
   - P0-02 is DONE.
   - Current branch is feature/p0-03-rag-chroma.
   - It was created from the latest develop.
   - Working tree is clean.
   - Existing restore/build/test commands pass.
   - Synthetic CRM data and Mock CRM API remain intact.

Checkpoint goals:

- Implement the minimum viable RAG foundation using:
  - the Gemini embedding model locked in project decisions;
  - Chroma as the vector store;
  - repository-owned knowledge documents.
- Define ingestion, normalization, chunking, metadata, embedding, upsert, retrieval, and citation behavior.
- Keep customer PII and CRM transactional data out of the vector store unless explicitly permitted by the specification.
- Make ingestion idempotent.
- Store source metadata sufficient to produce verifiable citations.
- Provide a retrieval interface usable later by the AI Host and email-draft flow.
- Support a clear “no relevant evidence found” result.
- Use secrets/environment configuration; never place the Gemini API key in source, committed configuration, logs, tests, or documentation.
- Verify the exact Gemini SDK/model and Chroma client/API against official sources before pinning.

Required tests/evidence to plan:

- Deterministic chunking tests.
- Metadata/source preservation.
- Idempotent ingestion.
- Retrieval returns relevant chunks for representative queries.
- No-match behavior.
- Offline default tests using fakes/fixtures.
- Optional live Gemini/Chroma smoke test clearly separated from default tests.
- Secret hygiene verification.

Explicitly out of scope:

- Gemini chat completion and agent loop.
- MCP server/tools.
- Email draft generation.
- Conversation UI/state.
- PII placeholder restoration.
- HubSpot or real CRM integration.
- Full production Docker/cloud deployment.

If Chroma requires a local container for this checkpoint, propose only the smallest infrastructure required for RAG verification. Do not expand into the final P0-09 orchestration scope.

Return the exact architecture placement, interfaces, data flow, chunking proposal, metadata schema, collection strategy, packages with pinned versions, file changes, tests, commands, risks, and blocker decisions.

Do not edit files, install packages, access or print a real API key, commit, push, merge, or start P0-04.

Stop after the plan and wait for explicit approval.

## 13. Checkpoint-specific prompt — P0-04

You are the implementation engineer for the CRM Copilot MVP repository.

Current checkpoint: P0-04 — MCP Server Core Tools.
Expected branch: feature/p0-04-mcp-tools.
Base branch: develop.

Work in plan-only mode.

Before proposing the plan:

1. Read CLAUDE.md, README.md, docs/CHECKPOINT_STATUS.md and all MCP-related requirements in:
   - docs/01_PROJECT_DECISIONS.md
   - docs/02_ARCHITECTURE.md
   - docs/03_ACCEPTANCE_CRITERIA.md
   - docs/04_P0_CHECKPOINTS.md
   - docs/05_IMPLEMENTATION_PLAN_9_DAYS.md
   - docs/13_REFERENCE_SOURCES.md

2. Verify P0-02 and P0-03 are DONE, the branch is correct and based on the latest develop, the working tree is clean, and the existing solution passes restore/build/test.

Checkpoint goals:

- Implement a real MCP server using the official C# MCP SDK.
- Use Streamable HTTP transport as locked by project decisions.
- Verify and pin the current compatible official MCP package versions.
- Implement the minimum required read tools:
  - get_customer;
  - get_interactions;
  - search_product_knowledge (docs/04_P0_CHECKPOINTS.md, docs/07_MCP_TOOL_CONTRACTS.md and
    docs/05_IMPLEMENTATION_PLAN_9_DAYS.md's Day-4 row all include this as a required P0-04 tool;
    this list previously omitted it, which was a documentation gap, not an approved scope cut).
- Define clear tool names, descriptions, input schemas, result schemas, and error contracts.
- Make McpServer access CRM data through an HTTP gateway/client to MockCrmApi.
- Do not let McpServer read synthetic JSON files directly.
- Keep the gateway replaceable by a future real CRM adapter.
- Expose and verify MCP tool discovery.
- Handle invalid arguments, customer not found, upstream failure, timeout, and empty interactions honestly.
- Ensure tool output is bounded and does not leak internal exception details.

Required tests/evidence to plan:

- Tool discovery exposes the expected tools.
- Direct invocation of get_customer.
- Direct invocation of get_interactions.
- Direct invocation of search_product_knowledge.
- Invalid input and not-found behavior.
- Mock CRM upstream failure behavior.
- RAG/knowledge-retrieval upstream failure behavior.
- MCP HTTP endpoint/transport smoke test.
- Existing tests remain green.
- Offline default tests.

Explicitly out of scope:

- get_opportunities and get_campaigns.
- Generate-email and generate-call-script tools.
- Gemini orchestration.
- AI Host/MCP Client loop.
- UI and conversation state.
- HubSpot integration.
- PII masking beyond existing baseline safeguards.
- Docker/cloud deployment.

Return package/version evidence, tool contracts, gateway boundary, exact files, implementation sequence, tests, commands, risks, assumptions, and approval questions.

Do not edit files, install packages, commit, push, merge, or start P0-05.

Stop after the plan and wait for explicit approval.

## 14. Checkpoint-specific prompt — P0-05

You are the implementation engineer for the CRM Copilot MVP repository.

Current checkpoint: P0-05 — AI Host, MCP Client & Tool Loop.
Expected branch: feature/p0-05-ai-host.
Base branch: develop.

Work in plan-only mode.

Before proposing the plan:

1. Read CLAUDE.md, README.md, docs/CHECKPOINT_STATUS.md and the relevant Host/MCP/RAG requirements in:
   - docs/01_PROJECT_DECISIONS.md
   - docs/02_ARCHITECTURE.md
   - docs/03_ACCEPTANCE_CRITERIA.md
   - docs/04_P0_CHECKPOINTS.md
   - docs/05_IMPLEMENTATION_PLAN_9_DAYS.md
   - docs/08_RAG_EMAIL_AND_PII_SPEC.md
   - docs/13_REFERENCE_SOURCES.md

2. Verify all prerequisite checkpoints are DONE and existing Mock CRM, RAG, and MCP tests pass.

Checkpoint goals:

- Make CrmCopilot.Web the AI Host and MCP Client.
- Connect to the MCP server through the approved MCP transport.
- Use the Gemini chat model locked in project decisions.
- Discover tools through MCP rather than hard-coding direct CRM operations.
- Implement a bounded tool-calling loop:
  - model receives user intent;
  - model selects an allowed MCP tool;
  - Host validates and invokes it;
  - tool result returns to the model;
  - model produces a grounded Vietnamese response.
- Use RAG retrieval when knowledge evidence is required.
- Keep CRM access exclusively behind MCP; Web must not call MockCrmApi or read CRM JSON directly.
- Add iteration, timeout, cancellation, malformed-tool-call, and upstream-error controls.
- Respond “không tìm thấy” when required evidence is unavailable.
- Keep default tests offline with fake Gemini/MCP/RAG dependencies.
- Keep live Gemini verification opt-in and secret-safe.

Minimum demonstrated flows:

- Customer lookup through MCP.
- Interaction history lookup through MCP.
- At least one grounded response combining CRM context and relevant RAG evidence where appropriate.
- Unknown customer/no-data behavior.

Explicitly out of scope:

- Final email-draft workflow.
- Call-script generation.
- Persistent conversation state or Redis.
- Final UI.
- HubSpot.
- Final PII masking/restoration checkpoint.
- Docker/cloud deployment.

Return the orchestration sequence, interfaces, system-prompt responsibilities, allowed-tool policy, bounded-loop design, files, packages, tests, live/offline verification, risks, and approval questions.

Do not edit files, install packages, use or expose a real key during planning, commit, push, merge, or start P0-06.

Stop after the plan and wait for explicit approval.

## 15. Checkpoint-specific prompt — P0-06

You are the implementation engineer for the CRM Copilot MVP repository.

Current checkpoint: P0-06 — RAG-Grounded Email Draft.
Expected branch: feature/p0-06-rag-email-draft.
Base branch: develop.

Work in plan-only mode.

Before proposing the plan:

1. Read CLAUDE.md, README.md, docs/CHECKPOINT_STATUS.md and:
   - docs/01_PROJECT_DECISIONS.md
   - docs/02_ARCHITECTURE.md
   - docs/03_ACCEPTANCE_CRITERIA.md
   - docs/04_P0_CHECKPOINTS.md
   - docs/05_IMPLEMENTATION_PLAN_9_DAYS.md
   - docs/08_RAG_EMAIL_AND_PII_SPEC.md

2. Verify P0-05 is DONE and all existing tests pass.

Checkpoint goals:

- Add email-draft generation as a P0 capability.
- Build the draft from:
  - Customer context obtained through MCP;
  - Interaction history obtained through MCP;
  - relevant product/policy/template evidence retrieved through RAG;
  - Gemini generation through the AI Host.
- The result is a draft only; never send an email or call an external mail service.
- Produce a stable output contract containing, at minimum:
  - subject;
  - body;
  - evidence/source references;
  - explicit warning or no-result status when context is insufficient.
- Keep claims grounded in retrieved CRM and RAG evidence.
- Do not fabricate products, rates, eligibility, customer history, or commitments.
- Allow the user/RM to review and edit the draft.
- Keep the design ready for later PII protection in P0-07.

Required tests/evidence to plan:

- Successful grounded draft.
- Missing customer.
- Customer with no interactions.
- No relevant RAG evidence.
- Gemini failure/timeout.
- Draft contains citations/source identifiers.
- No external email is sent.
- Offline deterministic tests plus optional live smoke test.

Explicitly out of scope:

- Sending email.
- Call-script generation unless explicitly approved as a small optional follow-up.
- Opportunity/Campaign tools.
- Long-term conversation memory.
- HubSpot.
- Final UI.
- Docker/cloud deployment.

Return the end-to-end data flow, output contract, prompt grounding rules, files, tests, commands, limitations, and approval questions.

Do not edit files, install packages, send email, commit, push, merge, or start P0-07.

Stop after the plan and wait for explicit approval.

## 16. Checkpoint-specific prompt — P0-07

You are the implementation engineer for the CRM Copilot MVP repository.

Current checkpoint: P0-07 — PII Masking & Hallucination Controls.
Expected branch: feature/p0-07-pii-guardrails.
Base branch: develop.

Work in plan-only mode.

Before proposing the plan:

1. Read CLAUDE.md, README.md, docs/CHECKPOINT_STATUS.md and:
   - docs/01_PROJECT_DECISIONS.md
   - docs/02_ARCHITECTURE.md
   - docs/03_ACCEPTANCE_CRITERIA.md
   - docs/04_P0_CHECKPOINTS.md
   - docs/05_IMPLEMENTATION_PLAN_9_DAYS.md
   - docs/08_RAG_EMAIL_AND_PII_SPEC.md

2. Trace the current data flow from Mock CRM/MCP/RAG through Gemini and final output.
3. Verify P0-06 is DONE and all existing tests pass.

Checkpoint goals:

- Implement the minimum approved PII masking policy before data is sent to Gemini.
- Cover at least the PII fields required by the specification, such as customer name, email, phone, account/reference identifiers, or other explicitly classified fields.
- Use stable placeholders within one request/session where required.
- Restore placeholders only in approved final-output fields.
- Prevent raw PII from appearing in:
  - Gemini prompts/captured request payloads;
  - application logs;
  - MCP diagnostic logs;
  - exception messages;
  - test snapshots;
  - RAG queries or vector metadata when not explicitly allowed.
- Add hallucination controls:
  - no unsupported customer facts;
  - no unsupported products/policies;
  - explicit “không tìm thấy” or insufficient-evidence response;
  - citation/source validation where required.
- Keep logs useful through correlation IDs and non-sensitive structured fields.

Required tests/evidence to plan:

- Mask each required PII type.
- Stable placeholder mapping.
- Safe restoration.
- Unknown placeholder behavior.
- No raw PII in captured Gemini requests.
- No raw PII in logs/exceptions.
- Unsupported claim/no-evidence behavior.
- Malicious or malformed input handling.
- Regression tests for lookup, interactions, RAG, and email draft.

Explicitly out of scope:

- Enterprise DLP.
- Encryption/key-management platform.
- Authentication/authorization system.
- Compliance certification.
- Redis/PostgreSQL.
- HubSpot.
- Full penetration testing.
- Docker/cloud deployment.

Return the PII classification, trust boundaries, masking/restoration design, log policy, test matrix, files, risks, residual limitations, and decisions requiring approval.

Do not edit files, install packages, expose real customer data, commit, push, merge, or start P0-08.

Stop after the plan and wait for explicit approval.

## 17. Checkpoint-specific prompt — P0-08

You are the implementation engineer for the CRM Copilot MVP repository.

Current checkpoint: P0-08 — Minimal UI & Conversation State.
Expected branch: feature/p0-08-ui-conversation.
Base branch: develop.

Work in plan-only mode.

Before proposing the plan:

1. Read CLAUDE.md, README.md, docs/CHECKPOINT_STATUS.md and:
   - docs/01_PROJECT_DECISIONS.md
   - docs/02_ARCHITECTURE.md
   - docs/03_ACCEPTANCE_CRITERIA.md
   - docs/04_P0_CHECKPOINTS.md
   - docs/05_IMPLEMENTATION_PLAN_9_DAYS.md
   - docs/08_RAG_EMAIL_AND_PII_SPEC.md
   - docs/11_DEMO_RUNBOOK.md

2. Verify P0-07 is DONE and all existing tests pass.

Checkpoint goals:

- Add the smallest demo-ready web UI to CrmCopilot.Web using the UI approach locked in project decisions.
- Do not introduce a separate frontend framework unless explicitly approved.
- Support:
  - Vietnamese natural-language input;
  - customer-information display;
  - interaction-history display;
  - grounded email-draft display;
  - loading, error, not-found, and reset behavior;
  - source/citation visibility sufficient for the demo.
- Implement minimal in-memory/session-scoped conversation state.
- Conversation state means only the context needed for a short demo conversation, such as:
  - session ID;
  - currently selected customer reference;
  - recent bounded messages;
  - last intent/tool result or draft reference where required.
- Apply bounded retention and message limits.
- Do not implement long-term memory.
- Do not store Gemini keys or unnecessary raw PII in session state.
- HTML-encode untrusted/model-generated content and prevent unsafe rendering.
- Provide a clear “new conversation/reset” action.

Required tests/evidence to plan:

- Initial UI load.
- Customer lookup flow.
- Follow-up interaction question using session context.
- Email-draft display.
- Unknown customer/error state.
- Reset clears conversational context.
- Session isolation.
- Output encoding/XSS safety.
- Existing backend tests remain green.

Explicitly out of scope:

- React/Angular/Vue migration.
- Redis or distributed session.
- Login/RBAC.
- Long-term chat history.
- Email sending.
- HubSpot.
- Production-grade visual design.
- Cloud deployment.

Return the UI route/page structure, conversation-state schema and lifecycle, security controls, files, tests, manual demo steps, risks, and approval questions.

Do not edit files, install packages, commit, push, merge, or start P0-09.

Stop after the plan and wait for explicit approval.

## 18. Checkpoint-specific prompt — P0-09

You are the implementation engineer for the CRM Copilot MVP repository.

Current checkpoint: P0-09 — Docker, Demo & Deployment Readiness.
Expected branch: feature/p0-09-docker-demo.
Base branch: develop.

Work in plan-only mode.

Before proposing the plan:

1. Read all governing documentation, especially:
   - CLAUDE.md
   - README.md
   - docs/CHECKPOINT_STATUS.md
   - docs/01_PROJECT_DECISIONS.md
   - docs/02_ARCHITECTURE.md
   - docs/03_ACCEPTANCE_CRITERIA.md
   - docs/04_P0_CHECKPOINTS.md
   - docs/05_IMPLEMENTATION_PLAN_9_DAYS.md
   - docs/08_RAG_EMAIL_AND_PII_SPEC.md
   - docs/11_DEMO_RUNBOOK.md

2. Verify P0-08 is DONE and all tests pass.
3. Inspect local Docker availability and the current runtime configuration.
4. Do not access or print the real Gemini API key.

Checkpoint goals:

- Containerize only the services required by the approved MVP.
- Provide a minimal Docker Compose topology for:
  - CrmCopilot.Web;
  - CrmCopilot.McpServer;
  - CrmCopilot.MockCrmApi;
  - Chroma;
  - any additional service only if already required by the approved architecture.
- Use runtime environment injection for secrets.
- Do not bake Gemini keys or `.env` into images.
- Add health checks and dependency readiness behavior.
- Ensure service-to-service URLs use Compose service names rather than localhost.
- Preserve persistent Chroma data only if required by the checkpoint specification.
- Document local Docker startup, shutdown, ingestion, health verification, test execution, and demo reset.
- Validate the scenarios required by docs/11_DEMO_RUNBOOK.md.
- Run the canonical demo flow three consecutive times if required by acceptance criteria.
- Produce honest known-limitations and troubleshooting notes.
- Make the project cloud-ready at a basic level, but do not deploy to a paid/public cloud unless separately approved.

Required evidence to plan:

- Docker image builds.
- Compose configuration validation.
- All required containers become healthy.
- Web can reach MCP, MCP can reach Mock CRM, and Web/RAG can reach Chroma.
- Gemini secret is injected only at runtime.
- Automated tests remain green.
- Required demo scenarios pass.
- Three consecutive canonical demo runs.
- Clean startup from documented instructions.
- No secrets in image history, repository diff, or logs.

Explicitly out of scope:

- HubSpot production integration.
- Kubernetes.
- CI/CD platform implementation unless explicitly required.
- Paid cloud infrastructure.
- Enterprise monitoring.
- Production authentication.
- Opportunity/Campaign expansion.
- Unapproved new features.

Return the container topology, Dockerfile/Compose changes, environment variables, health/dependency strategy, verification matrix, demo plan, rollback/cleanup steps, risks, and approval questions.

Do not edit files, build images, start containers, commit, push, merge, deploy, or mark the project complete.

Stop after the plan and wait for explicit approval.
