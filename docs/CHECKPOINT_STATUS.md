# Checkpoint Status

Baseline date: 2026-08-18  
Current phase: P0-03 implementation in progress on working tree (uncommitted); not yet built/tested/reviewed this session  
Last synced: 2026-08-21  
Current checkpoint to open: **P0-03**

## Git branch control

| Item                   | Current value                 |
| ---------------------- | ----------------------------- |
| Branch strategy        | `main → develop → feature/**` |
| Stable/release branch  | `main`                        |
| Integration branch     | `develop`                     |
| Expected active branch | `feature/p0-03-rag-chroma`    |
| Actual active branch   | `feature/p0-03-rag-chroma`    |
| Base commit            | `efa9fdd`                     |
| Merge target           | `develop`                     |
| Current merge status   | NOT MERGED                    |

## 1. Status board

| Checkpoint                           | Status      | Reviewer verdict | Evidence summary                                                                                                                                                                 | Next action                       |
| ------------------------------------ | ----------- | ---------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------- |
| P0-00 Documentation Baseline         | DONE        | PASS             | 16-file Markdown planning kit cross-checked                                                                                                                                      | Open P0-01 planning               |
| P0-01 Repository & Solution Scaffold | DONE        | PASS             | restore PASS; build PASS with 0 warnings/errors; tests 3/3 PASS using Microsoft.Testing.Platform; three Kestrel health endpoints returned HTTP 200; secret hygiene PASS. | Open P0-02 planning                |
| P0-02 Synthetic Data & Mock CRM API  | DONE        | PASS             | Dataset 12 customers / 26 interactions (seed 20260818), deterministic + golden-tested against checked-in JSON; restore/build PASS with 0 warnings/errors; tests 50/50 PASS; customer lookup/search (incl. 409 AMBIGUOUS_MATCH data-envelope contract) and interaction endpoints verified by test + manual curl; MockCrmGateway full error mapping and ValidateOnStart fail-fast verified; README `.env` wording corrected in final audit. | Open P0-03 planning               |
| P0-03 Gemini Embedding & Chroma RAG  | REVIEW      | —                | Implementation present on `feature/p0-03-rag-chroma` working tree (uncommitted): embedding client, Chroma HTTP adapter, idempotent ingestion, retriever, unit tests + opt-in `LiveRagAcceptanceTests`. 2026-08-21 offline: `dotnet restore` PASS; `dotnet build CrmCopilot.slnx --no-restore` PASS, 0 warnings/0 errors; `dotnet test` 121/122 passed, 1 skipped (`LiveRagAcceptanceTests`, no live creds — expected). 2026-08-21 **mandatory live acceptance run** (real Gemini `gemini-embedding-001` + Chroma `1.5.9` local container, isolated collection `crm-copilot-knowledge-livetest`, dev collection `crm-copilot-knowledge` untouched): heartbeat OK; ingest run 1 (fresh collection) → 14 embedded, count=14; ingest run 2 → 0 embedded/14 unchanged (idempotent confirmed); query embedding L2 norm=1.000000; canonical query top-3 = `PRD-SAV-006M` (distance 0.472959, rank 1), `PRD-SAV-012M`, `TPL-EMAIL-MATURITY-01`; `dotnet test --filter-class LiveRagAcceptanceTests` 1/1 PASS. No API key or payload logged. Not yet: committed, reviewer verdict. | Request Product Owner/ChatGPT review verdict; do not start P0-04 |
| P0-04 MCP Server Core Tools          | NOT STARTED | —                | —                                                                                                                                                                                | Blocked by P0-02/P0-03            |
| P0-05 AI Host + MCP Client           | NOT STARTED | —                | —                                                                                                                                                                                | Blocked by P0-04                  |
| P0-06 Conversation State             | NOT STARTED | —                | —                                                                                                                                                                                | Blocked by P0-05                  |
| P0-07 RAG Email Draft + PII          | NOT STARTED | —                | —                                                                                                                                                                                | Blocked by P0-03/P0-06            |
| P0-08 Web UI + Trace + Sources       | NOT STARTED | —                | —                                                                                                                                                                                | Blocked by P0-07                  |
| P0-09 Acceptance & Demo              | NOT STARTED | —                | —                                                                                                                                                                                | Blocked by P0-08                  |

Allowed status values: `NOT STARTED`, `PLANNING`, `APPROVED`, `IN PROGRESS`, `REVIEW`, `REWORK`, `BLOCKED`, `DONE`.

## 2. Frozen baseline

| Item                 | Value actually implemented                                                    | Evidence/version date                   |
| -------------------- | ----------------------------------------------------------------------------- | --------------------------------------- |
| .NET SDK/target      | `10.0.400` (`net10.0`), pinned qua `global.json` (`rollForward: latestPatch`) | 2026-08-19                              |
| MCP packages         | Chưa thêm — hoãn tới P0-04 theo quyết định đã duyệt                           | 2026-08-19 (NuGet baseline vẫn `2.2.0`) |
| MCP transport        | Streamable HTTP                                                               | Decision baseline                       |
| Gemini chat          | `gemini-3.5-flash-lite`                                                       | Verify at P0-01/P0-05                   |
| Gemini embedding     | `gemini-embedding-001`                                                        | Verified live 2026-08-21 (real API call) |
| Embedding dimension  | 768 + L2 normalized (query norm=1.000000 observed live)                      | Verified live 2026-08-21                |
| Chroma version/image | `chromadb/chroma:1.5.9`, distance metric `l2`, HTTP client (no official .NET package) | Verified live 2026-08-21          |
| Dataset version/hash | seed=20260818, 12 customers / 26 interactions; SHA-256 customers.json=`14c500f9…6c4b2e9d8`, interactions.json=`52af5586…3a0a52da558` | 2026-08-19 |

## 3. Review log

| Date       | Checkpoint | Artifact reviewed      | Verdict | Required follow-up            |
| ---------- | ---------- | ---------------------- | ------- | ----------------------------- |
| 2026-08-18 | P0-00      | Documentation baseline | PASS    | Begin P0-01 plan; no code yet |
| 2026-08-19 | P0-02      | Synthetic dataset + Mock CRM API implementation, plus final audit corrections (409 contract, MockCrmGateway error mapping, ValidateOnStart fail-fast, README `.env` wording) | PASS | Open P0-03 planning |

## 4. Blocker log

| ID  | Opened | Checkpoint | Blocker           | Owner | Status/decision |
| --- | ------ | ---------- | ----------------- | ----- | --------------- |
| —   | —      | —          | No active blocker | —     | —               |

## 5. Checkpoint evidence template

```markdown
### P0-XX Review Evidence — YYYY-MM-DD

- Approved plan reference:
- Changed files:
- Packages/config changes:
- Commands actually run:
- Automated test result:
- Manual verification:
- Acceptance criteria mapping:
- Security/PII check:
- Known limitations:
- Git diff/status:
- Claude report:
- ChatGPT verdict:
- Required follow-up:
- Next checkpoint allowed: yes/no
```

## 6. Cập nhật file này

- Claude đề xuất update trong completion report nhưng không tự đánh dấu `DONE` trước review.
- ChatGPT đưa verdict; Product Owner/Claude cập nhật verdict và evidence sau khi được xác nhận.
- Không xóa lịch sử review/blocker; thêm dòng mới.
- Mọi version/model/dataset hash thực tế phải điền khi được triển khai.
