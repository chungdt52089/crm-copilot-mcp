# Checkpoint Status

Baseline date: 2026-08-18  
Current phase: Planning complete; implementation not started  
Current checkpoint to open: **P0-01**

## Git branch control

| Item                   | Current value                 |
| ---------------------- | ----------------------------- |
| Branch strategy        | `main → develop → feature/**` |
| Stable/release branch  | `main`                        |
| Integration branch     | `develop`                     |
| Expected active branch | `feature/p0-01-scaffold`      |
| Actual active branch   | `feature/p0-01-scaffold`      |
| Base commit            | `afa47fd`                     |
| Merge target           | `develop`                     |
| Current merge status   | NOT MERGED                    |

## 1. Status board

| Checkpoint                           | Status      | Reviewer verdict | Evidence summary                                                                                                                                                                 | Next action                       |
| ------------------------------------ | ----------- | ---------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------- |
| P0-00 Documentation Baseline         | DONE        | PASS             | 16-file Markdown planning kit cross-checked                                                                                                                                      | Open P0-01 planning               |
| P0-01 Repository & Solution Scaffold | DONE        | PASS             | restore PASS; build PASS with 0 warnings/errors; tests 3/3 PASS using Microsoft.Testing.Platform; three Kestrel health endpoints returned HTTP 200; secret hygiene PASS. | Open P0-02 planning                |
| P0-02 Synthetic Data & Mock CRM API  | NOT STARTED | —                | —                                                                                                                                                                                | Open P0-02 planning               |
| P0-03 Gemini Embedding & Chroma RAG  | NOT STARTED | —                | —                                                                                                                                                                                | Blocked by P0-01                  |
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
| Gemini embedding     | `gemini-embedding-001`                                                        | Verify at P0-03                         |
| Embedding dimension  | 768 + L2 normalized                                                           | Verify at P0-03                         |
| Chroma version/image | TBD and pinned at P0-03                                                       | —                                       |
| Dataset version/hash | TBD at P0-02                                                                  | —                                       |

## 3. Review log

| Date       | Checkpoint | Artifact reviewed      | Verdict | Required follow-up            |
| ---------- | ---------- | ---------------------- | ------- | ----------------------------- |
| 2026-08-18 | P0-00      | Documentation baseline | PASS    | Begin P0-01 plan; no code yet |

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
