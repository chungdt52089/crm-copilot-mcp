# Checkpoint Status

Baseline date: 2026-08-18

Current phase: P0-03 DONE (Product Owner confirmed PASS 2026-08-21; merged to `develop` via PR #7); P0-04 DONE (Product Owner confirmed PASS 2026-08-21; merged to `develop` via PR #8, `137363b` → merge `dfddeae`); P0-05 REVIEW — implementation complete on `feature/p0-05-ai-host` (branched from `develop` @ `dfddeae`), offline suite green and mandatory real-Gemini live acceptance gate PASS 2026-08-22, awaiting reviewer/Product Owner PASS verdict before merge

Last synced: 2026-08-22

Current checkpoint to open: **P0-05** (implementation complete, awaiting reviewer PASS verdict — do not open P0-06 until confirmed)

## Git branch control

| Item                   | Current value                 |
| ---------------------- | ----------------------------- |
| Branch strategy        | `main → develop → feature/**` |
| Stable/release branch  | `main`                        |
| Integration branch     | `develop`                     |
| Expected active branch | `feature/p0-05-ai-host`       |
| Actual active branch   | `feature/p0-05-ai-host`       |
| Base commit            | `dfddeae` (develop HEAD, includes merged P0-03 + P0-04) |
| Merge target           | `develop`                     |
| Current merge status   | P0-03 MERGED to `develop` (PR #7: `c06cd02` → `37c0e4d`); P0-04 MERGED to `develop` (PR #8: `137363b` → `dfddeae`); P0-05 implementation complete + offline suite green + mandatory live gate PASS on `feature/p0-05-ai-host`, NOT MERGED — awaiting reviewer PASS verdict before merge |

## 1. Status board

| Checkpoint                           | Status      | Reviewer verdict | Evidence summary                                                                                                                                                                 | Next action                       |
| ------------------------------------ | ----------- | ---------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------- |
| P0-00 Documentation Baseline         | DONE        | PASS             | 16-file Markdown planning kit cross-checked                                                                                                                                      | Open P0-01 planning               |
| P0-01 Repository & Solution Scaffold | DONE        | PASS             | restore PASS; build PASS with 0 warnings/errors; tests 3/3 PASS using Microsoft.Testing.Platform; three Kestrel health endpoints returned HTTP 200; secret hygiene PASS. | Open P0-02 planning                |
| P0-02 Synthetic Data & Mock CRM API  | DONE        | PASS             | Dataset 12 customers / 26 interactions (seed 20260818), deterministic + golden-tested against checked-in JSON; restore/build PASS with 0 warnings/errors; tests 50/50 PASS; customer lookup/search (incl. 409 AMBIGUOUS_MATCH data-envelope contract) and interaction endpoints verified by test + manual curl; MockCrmGateway full error mapping and ValidateOnStart fail-fast verified; README `.env` wording corrected in final audit. | Open P0-03 planning               |
| P0-03 Gemini Embedding & Chroma RAG  | DONE        | PASS             | Implementation merged to `develop` via PR #7 (`c06cd02` "feat: add RAG with Chroma" → merge `37c0e4d`): embedding client, Chroma HTTP adapter, idempotent ingestion, retriever, unit tests + opt-in `LiveRagAcceptanceTests`. 2026-08-21 offline: `dotnet restore` PASS; `dotnet build CrmCopilot.slnx --no-restore` PASS, 0 warnings/0 errors; `dotnet test` 121/122 passed, 1 skipped (`LiveRagAcceptanceTests`, no live creds — expected). 2026-08-21 **mandatory live acceptance run** (real Gemini `gemini-embedding-001` + Chroma `1.5.9` local container, isolated collection `crm-copilot-knowledge-livetest`, dev collection `crm-copilot-knowledge` untouched): heartbeat OK; ingest run 1 (fresh collection) → 14 embedded, count=14; ingest run 2 → 0 embedded/14 unchanged (idempotent confirmed); query embedding L2 norm=1.000000; canonical query top-3 = `PRD-SAV-006M` (distance 0.472959, rank 1), `PRD-SAV-012M`, `TPL-EMAIL-MATURITY-01`; `dotnet test --filter-class LiveRagAcceptanceTests` 1/1 PASS. No API key or payload logged. Product Owner confirmed PASS 2026-08-21. | Open P0-04 planning               |
| P0-04 MCP Server Core Tools          | DONE        | PASS             | Implementation merged to `develop` via PR #8 (`137363b` "feat: add core MCP tools" → merge `dfddeae`): official SDK `ModelContextProtocol.AspNetCore` 2.2.0, Streamable HTTP (`HttpServerSessionMode.Stateless`), `/mcp` endpoint; 3 tools `get_customer`/`get_interactions`/`search_product_knowledge` via existing `ICrmGateway`/`IKnowledgeRetriever` (unchanged); shared tool-result envelope in `Contracts/Mcp/`. `dotnet build` PASS, 0 warnings/0 errors; `dotnet test` 155/156 passed, 1 skipped (`LiveRagAcceptanceTests`, unrelated opt-in); `git diff --check` PASS (exit 0). **Live MCP verification 2026-08-21** (real McpServer + MockCrmApi + Chroma, real `GEMINI_API_KEY` held only in Product Owner's own terminal, never read/printed/logged by Claude): `tools/list` → exactly 3 tools with correct schemas; all 3 canonical calls PASS live — `get_customer CUS-0001`, `get_interactions CUS-0001 limit=5`, and `search_product_knowledge` canonical query topK=3 using real Gemini embedding + Chroma (`crm-copilot-knowledge-livetest` collection); `PRD-SAV-006M` ranked first in top-3 (distance 0.47295862, lower-is-better, matches P0-03's recorded 0.472959), no fabricated `title`/`score`; invalid `topK=6` → `INVALID_ARGUMENT`; no API key or raw exception leaked in any response. Product Owner confirmed PASS 2026-08-21. | Open P0-05 planning               |
| P0-05 AI Host + MCP Client           | REVIEW      | —                | Implementation on `feature/p0-05-ai-host` (branched from `develop` @ `dfddeae`, plan Revision 3 approved 2026-08-22): Gemini `gemini-3.5-flash-lite` tool-calling loop bounded to 3 MCP calls/turn (D-loop), async `IMcpClientProvider` (no sync-over-async), tool allowlist = intersection of approved names and MCP discovery (D5), pre-Gemini PII/CRM-intent input gate (D7), no-PII-to-Gemini data minimization (D1), `POST /api/chat`. `dotnet restore` PASS; `dotnet build CrmCopilot.slnx --no-restore` PASS, 0 warnings/0 errors; `dotnet test` 206/207 passed, 1 skipped (`LiveRagAcceptanceTests`, unrelated opt-in). **Live acceptance finding + fix (2026-08-22, before final live PASS):** first live `get_customer` run returned HTTP 400 `DUPLICATE_TOOL_CALL` — root cause: the minimized tool-result payload sent back to Gemini (`{status,sourceIds}` only) was insufficient for the model to recognize the call as complete, so it re-requested the identical call; fixed by adding the non-PII `customerId`/`interactionCount` fields to the minimized payload and one system-instruction line. Same live-gate run also surfaced that a controlled error response could carry the turn's already-fetched `CustomerDto` (PII) in `Data` — fixed: `Data` is now always `null` on any `Status=Error`/`NotFound` outcome; new/updated tests added (`ChatEndpointTests`, `McpToolResultParserTests`) proving both the fix and that `DUPLICATE_TOOL_CALL` still fires with exactly one real MCP call. **Mandatory live acceptance gate PASS 2026-08-22** (real Gemini `gemini-3.5-flash-lite` + real McpServer + MockCrmApi + Chroma with the default `crm-copilot-knowledge` collection ingested — resolves Blocker B-01; real `GEMINI_API_KEY` held only in Product Owner's own terminal, never read/printed/logged by Claude), filtered evidence: `get_customer` → HTTP 200, `status:success`, non-empty reply, `sourceIds=[crm:customer:CUS-0001]`, ToolTrace 1 entry (`get_customer`/success), exactly one MCP call, `errorCode:null`; `get_interactions` → HTTP 200, `status:success`, non-empty reply, `interactionCount:3`, `sourceIds=[crm:interaction:INT-0001..003]`, ToolTrace 1 entry, exactly one MCP call, `errorCode:null`; `search_product_knowledge` → HTTP 200, `status:success`, non-empty reply, `sourceIds=[kb:product:PRD-SAV-006M, PRD-SAV-012M, PRD-LOAN-PERSONAL-01]` with canonical `PRD-SAV-006M` ranked first, ToolTrace 1 entry, exactly one MCP call, `errorCode:null`. No PII is sent to Gemini (D1's data-minimized `FunctionResponse` payloads); no accumulated PII appears in any controlled-error response (`Data=null` on `Status=Error`/`NotFound`, verified by test); the filtered evidence recorded above contains no PII, raw DTO, or API key. On a **successful** turn, `ChatResponse.Data.Customer`/`Data.Interactions` still legitimately carries the full CRM DTO exactly as the approved contract specifies (docs/07 §4's trusted-local-path design, for the caller/UI, never sent to Gemini) — that is not a PII leak and is not being claimed as absent. Product Owner confirmed live gate PASS 2026-08-22. | Awaiting reviewer/Product Owner checkpoint PASS verdict; do not open P0-06 or merge until confirmed |
| P0-06 Conversation State             | NOT STARTED | —                | —                                                                                                                                                                                | Blocked by P0-05                  |
| P0-07 RAG Email Draft + PII          | NOT STARTED | —                | —                                                                                                                                                                                | Blocked by P0-03/P0-06            |
| P0-08 Web UI + Trace + Sources       | NOT STARTED | —                | —                                                                                                                                                                                | Blocked by P0-07                  |
| P0-09 Acceptance & Demo              | NOT STARTED | —                | —                                                                                                                                                                                | Blocked by P0-08                  |

Allowed status values: `NOT STARTED`, `PLANNING`, `APPROVED`, `IN PROGRESS`, `REVIEW`, `REWORK`, `BLOCKED`, `DONE`.

## 2. Frozen baseline

| Item                 | Value actually implemented                                                    | Evidence/version date                   |
| -------------------- | ----------------------------------------------------------------------------- | --------------------------------------- |
| .NET SDK/target      | `10.0.400` (`net10.0`), pinned qua `global.json` (`rollForward: latestPatch`) | 2026-08-19                              |
| MCP packages         | `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` `2.2.0` (exact pin), verified against NuGet v3-flatcontainer raw index | Verified 2026-08-21 (P0-04) |
| MCP transport        | Streamable HTTP                                                               | Decision baseline                       |
| Gemini chat          | `gemini-3.5-flash-lite`                                                       | Verified live 2026-08-22 (P0-05 mandatory live gate, real API calls) |
| Gemini embedding     | `gemini-embedding-001`                                                        | Verified live 2026-08-21 (real API call) |
| Embedding dimension  | 768 + L2 normalized (query norm=1.000000 observed live)                      | Verified live 2026-08-21                |
| Chroma version/image | `chromadb/chroma:1.5.9`, distance metric `l2`, HTTP client (no official .NET package) | Verified live 2026-08-21          |
| Dataset version/hash | seed=20260818, 12 customers / 26 interactions; SHA-256 customers.json=`14c500f9…6c4b2e9d8`, interactions.json=`52af5586…3a0a52da558` | 2026-08-19 |

## 3. Review log

| Date       | Checkpoint | Artifact reviewed      | Verdict | Required follow-up            |
| ---------- | ---------- | ---------------------- | ------- | ----------------------------- |
| 2026-08-18 | P0-00      | Documentation baseline | PASS    | Begin P0-01 plan; no code yet |
| 2026-08-19 | P0-02      | Synthetic dataset + Mock CRM API implementation, plus final audit corrections (409 contract, MockCrmGateway error mapping, ValidateOnStart fail-fast, README `.env` wording) | PASS | Open P0-03 planning |
| 2026-08-21 | P0-03      | Gemini embedding + Chroma RAG implementation (`feature/p0-03-rag-chroma`, merged to `develop` via PR #7); offline test suite + mandatory live acceptance run (real Gemini/Chroma) | PASS | Open P0-04 planning |
| 2026-08-21 | P0-04      | MCP Server Core Tools implementation (`feature/p0-04-mcp-server-tools`, merged to `develop` via PR #8); offline test suite + live MCP verification (real Gemini/Chroma, all 3 tools, canonical + invalid-argument cases) | PASS | Open P0-05 planning |

## 4. Blocker log

| ID  | Opened | Checkpoint | Blocker           | Owner | Status/decision |
| --- | ------ | ---------- | ----------------- | ----- | --------------- |
| B-01 | 2026-08-21 | P0-04 (found), targets P0-05 | Default Chroma collection `crm-copilot-knowledge` has 0 records — never ingested (only the isolated `crm-copilot-knowledge-livetest` collection, from P0-03's mandatory live acceptance run, is populated). Does **not** block P0-04 (its live verification explicitly used `crm-copilot-knowledge-livetest` via `CHROMA_COLLECTION_NAME`). | Product Owner | **Resolved 2026-08-22** — default collection `crm-copilot-knowledge` ingested (`--ingest-knowledge`, `CHROMA_COLLECTION_NAME` left unset) as part of P0-05's mandatory live acceptance gate; live `search_product_knowledge` call against it returned canonical `PRD-SAV-006M` ranked first. `crm-copilot-knowledge-livetest` was not touched/reused. |

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
