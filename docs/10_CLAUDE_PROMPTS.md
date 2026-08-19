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

