(function () {
  "use strict";

  // ---- session/state -------------------------------------------------------------------------
  let sessionId = crypto.randomUUID(); // client-generated only — never persisted, never server-issued
  let busy = false;

  // Client-side accumulated state (Product-Owner-mandated condition 1): ChatResponseData is fresh
  // per HTTP turn on the server (ChatOrchestrator.HandleAsync resets it to all-null every call), so
  // the client — not the server — is what must remember what has already been shown this session.
  // Only resetSession() ever replaces this object wholesale (condition 2).
  let uiState = createInitialState();

  function createInitialState() {
    return {
      customer: null,
      customerCandidates: null,
      interactions: null,
      emailDraft: null,
      opportunities: null,
      campaigns: null,
      callScript: null,
      sourceChipIds: new Set(), // condition 4: deduped union of KnowledgeMatches[].sourceId ∪ EmailDraft.SourceIds
      trace: [], // accumulated ChatToolTraceEntry list across the whole session
      lastTurnStatus: null, // drives the stale-data notice; never clears the panels themselves
    };
  }

  // ---- DOM handles -----------------------------------------------------------------------------
  const els = {
    form: document.getElementById("chat-form"),
    input: document.getElementById("chat-input"),
    sendButton: document.getElementById("send-button"),
    resetButton: document.getElementById("reset-button"),
    loadingIndicator: document.getElementById("loading-indicator"),
    chatLog: document.getElementById("chat-log"),
    errorBanner: document.getElementById("error-banner"),
    staleDataNotice: document.getElementById("stale-data-notice"),
    customerCard: document.getElementById("customer-card"),
    customerCardBody: document.getElementById("customer-card-body"),
    candidateList: document.getElementById("candidate-list"),
    candidateListBody: document.getElementById("candidate-list-body"),
    interactionList: document.getElementById("interaction-list"),
    interactionListBody: document.getElementById("interaction-list-body"),
    draftPanel: document.getElementById("draft-panel"),
    draftSubject: document.getElementById("draft-subject"),
    draftBody: document.getElementById("draft-body"),
    draftApprovalLabel: document.getElementById("draft-approval-label"),
    draftSourceChips: document.getElementById("draft-source-chips"),
    opportunityList: document.getElementById("opportunity-list"),
    opportunityListBody: document.getElementById("opportunity-list-body"),
    campaignList: document.getElementById("campaign-list"),
    campaignListBody: document.getElementById("campaign-list-body"),
    callScriptPanel: document.getElementById("call-script-panel"),
    callScriptObjective: document.getElementById("call-script-objective"),
    callScriptInferredBadge: document.getElementById("call-script-inferred-badge"),
    callScriptOpening: document.getElementById("call-script-opening"),
    callScriptDiscovery: document.getElementById("call-script-discovery"),
    callScriptTalkingPoints: document.getElementById("call-script-talking-points"),
    callScriptObjections: document.getElementById("call-script-objections"),
    callScriptClosing: document.getElementById("call-script-closing"),
    callScriptApprovalLabel: document.getElementById("call-script-approval-label"),
    callScriptSourceChips: document.getElementById("call-script-source-chips"),
    tracePanel: document.getElementById("trace-panel"),
    traceEntries: document.getElementById("trace-entries"),
    sourceChips: document.getElementById("source-chips"),
  };

  // ---- rendering primitives — the ONLY way any server/model-derived string reaches the DOM ----
  // (hard requirement: no innerHTML with any interpolated/variable value anywhere in this file)
  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null) node.textContent = String(text);
    return node;
  }

  function chip(text) {
    return el("span", "chip", text);
  }

  function clearChildren(node) {
    while (node.firstChild) node.removeChild(node.firstChild);
  }

  function showPanel(node, visible) {
    node.hidden = !visible;
  }

  // ---- error message table (fallback only — body.error.message is already a fixed, safe,
  // Vietnamese string by contract per ChatTurnError's doc comment; this table is used only if that
  // field is ever missing/malformed) --------------------------------------------------------------
  const ERROR_MESSAGES = {
    INVALID_ARGUMENT: "Yêu cầu không hợp lệ.",
    PII_REJECTED:
      "Vui lòng không nhập email, số điện thoại, số tài khoản/CCCD hoặc địa chỉ trực tiếp vào khung chat. Hãy dùng mã khách hàng (ví dụ CUS-0001).",
    NAME_LOOKUP_NOT_SUPPORTED:
      "Tra cứu khách hàng qua chat chỉ hỗ trợ theo mã khách hàng (ví dụ CUS-0001), không hỗ trợ theo tên.",
    CUSTOMER_ID_REQUIRED: "Vui lòng cung cấp mã khách hàng (ví dụ CUS-0001).",
    CUSTOMER_ID_INVALID: "Mã khách hàng không hợp lệ. Vui lòng kiểm tra đúng định dạng và thử lại.",
    UNKNOWN_TOOL: "Yêu cầu công cụ không hợp lệ.",
    DUPLICATE_TOOL_CALL: "Yêu cầu công cụ bị lặp lại trong cùng một lượt.",
    MULTIPLE_FUNCTION_CALLS_NOT_SUPPORTED: "Không hỗ trợ gọi nhiều công cụ cùng lúc trong một lượt.",
    TOOL_LOOP_LIMIT_EXCEEDED: "Đã đạt giới hạn số lần gọi công cụ cho lượt này.",
    UPSTREAM_UNAVAILABLE: "Không thể kết nối tới hệ thống CRM. Vui lòng thử lại sau.",
    RAG_UNAVAILABLE: "Không thể truy xuất cơ sở tri thức sản phẩm. Vui lòng thử lại sau.",
    MCP_UNAVAILABLE: "Không thể kết nối tới MCP Server.",
    MODEL_ERROR: "Không thể tạo phản hồi từ mô hình AI.",
    MCP_PROTOCOL_ERROR: "MCP tool call trả về lỗi giao thức không mong đợi.",
    MCP_INVALID_RESPONSE: "Không thể đọc kết quả từ MCP tool.",
    INTERNAL_ERROR: "Đã xảy ra lỗi không mong muốn.",
  };

  function errorMessageFor(error) {
    if (error && error.message) return error.message;
    if (error && error.code && ERROR_MESSAGES[error.code]) return ERROR_MESSAGES[error.code];
    return "Đã xảy ra lỗi. Vui lòng thử lại.";
  }

  function showError(text) {
    els.errorBanner.textContent = text;
    els.errorBanner.hidden = false;
  }

  function hideError() {
    els.errorBanner.hidden = true;
    els.errorBanner.textContent = "";
  }

  // ---- busy/loading guard (blocks double-submit AND the documented Reset-vs-in-flight race) ----
  function setBusy(isBusy) {
    busy = isBusy;
    els.sendButton.disabled = isBusy;
    els.resetButton.disabled = isBusy;
    els.input.disabled = isBusy;
    els.loadingIndicator.hidden = !isBusy;
  }

  // ---- chat log (append-only; never cleared except by Reset) ----
  function appendChatBubble(role, text) {
    const bubble = el("div", "bubble bubble-" + role, text);
    els.chatLog.appendChild(bubble);
  }

  // ---- condition 1: non-destructive merge of this turn's data into the accumulated state ----
  function mergeTurnData(data) {
    if (!data) return;
    if (data.customer) uiState.customer = data.customer;
    if (data.customerCandidates) uiState.customerCandidates = data.customerCandidates;
    if (data.interactions) uiState.interactions = data.interactions;
    if (data.emailDraft) uiState.emailDraft = data.emailDraft;
    if (data.opportunities) uiState.opportunities = data.opportunities;
    if (data.campaigns) uiState.campaigns = data.campaigns;
    if (data.callScript) uiState.callScript = data.callScript;

    // condition 4: source chips from KnowledgeMatches, EmailDraft.SourceIds and (P0-10)
    // CallScript.SourceIds, deduped. The call-script source ids are server-authored — the Host
    // forces the opportunity/template/product actually used into them rather than trusting the
    // model to cite them (plan Amendment A3) — so this chip row is reliable evidence, not a
    // reflection of whatever the model volunteered.
    if (data.knowledgeMatches) {
      for (const match of data.knowledgeMatches) {
        if (match && match.sourceId) uiState.sourceChipIds.add(match.sourceId);
      }
    }
    if (data.emailDraft && data.emailDraft.sourceIds) {
      for (const sourceId of data.emailDraft.sourceIds) {
        uiState.sourceChipIds.add(sourceId);
      }
    }
    if (data.callScript && data.callScript.sourceIds) {
      for (const sourceId of data.callScript.sourceIds) {
        uiState.sourceChipIds.add(sourceId);
      }
    }
  }

  function mergeTrace(toolTrace) {
    if (!toolTrace) return;
    for (const entry of toolTrace) {
      uiState.trace.push(entry);
    }
  }

  // ---- renderAll: the single place every panel is (re)built from uiState ----
  function renderAll() {
    renderStaleDataNotice();
    renderCustomerCard();
    renderCandidateList();
    renderInteractionList();
    renderOpportunityList();
    renderCampaignList();
    renderCallScriptPanel();
    renderDraftPanel();
    renderTracePanel();
  }

  // Panels are never cleared by a failed turn (condition 1), so when the newest turn did NOT load
  // new data this states plainly whose data is still on screen — otherwise a "Không tìm thấy
  // khách hàng CUS-9999" turn sits directly above CUS-0001's populated cards.
  function renderStaleDataNotice() {
    var hasPanelData =
      uiState.customer ||
      uiState.interactions ||
      uiState.emailDraft ||
      uiState.opportunities ||
      uiState.campaigns ||
      uiState.callScript;
    var lastTurnLoadedNothing = uiState.lastTurnStatus && uiState.lastTurnStatus !== "success";

    if (!hasPanelData || !lastTurnLoadedNothing) {
      els.staleDataNotice.hidden = true;
      els.staleDataNotice.textContent = "";
      return;
    }

    var ownerId = uiState.customer ? uiState.customer.id : null;
    els.staleDataNotice.textContent = ownerId
      ? "Đang hiển thị dữ liệu của khách hàng " + ownerId + "."
      : "Đang hiển thị dữ liệu từ lượt trước đó.";
    els.staleDataNotice.hidden = false;
  }

  function renderCustomerCard() {
    if (!uiState.customer) {
      showPanel(els.customerCard, false);
      return;
    }
    const c = uiState.customer;
    clearChildren(els.customerCardBody);
    const rows = [
      ["Mã khách hàng", c.id],
      ["Họ tên", c.fullName],
      ["Phân khúc", c.segment],
      ["Thành phố", c.city],
      ["Trạng thái", c.status],
      ["Email", c.email],
      ["Điện thoại", c.phone],
      ["Số tài khoản", c.accountReference],
      ["Ngôn ngữ ưu tiên", c.preferredLanguage],
    ];
    for (const [label, value] of rows) {
      const row = el("div", "field");
      row.appendChild(el("span", "field-label", label + ":"));
      row.appendChild(document.createTextNode(value === null || value === undefined ? "" : String(value)));
      els.customerCardBody.appendChild(row);
    }
    showPanel(els.customerCard, true);
  }

  function renderCandidateList() {
    const candidates = uiState.customerCandidates;
    if (!candidates || candidates.length === 0) {
      showPanel(els.candidateList, false);
      return;
    }
    clearChildren(els.candidateListBody);
    for (const candidate of candidates) {
      const row = el(
        "div",
        "candidate-row",
        (candidate.id || "") + " — " + (candidate.fullName || "") + " (" + (candidate.segment || "") + ", " + (candidate.city || "") + ")"
      );
      els.candidateListBody.appendChild(row);
    }
    showPanel(els.candidateList, true);
  }

  function renderInteractionList() {
    const interactions = uiState.interactions;
    if (!interactions || interactions.length === 0) {
      showPanel(els.interactionList, false);
      return;
    }
    clearChildren(els.interactionListBody);
    // Rendered in the order last received — server already guarantees newest-first; client does not re-sort.
    for (const interaction of interactions) {
      const row = el("div", "field");
      const summary = (interaction.type || "") + " — " + (interaction.occurredAtUtc || "") + ": " + (interaction.summary || "");
      row.textContent = summary;
      els.interactionListBody.appendChild(row);
      if (interaction.nextAction) {
        els.interactionListBody.appendChild(el("div", "field", "Bước tiếp theo: " + interaction.nextAction));
      }
    }
    showPanel(els.interactionList, true);
  }

  function renderOpportunityList() {
    const opportunities = uiState.opportunities;
    if (!opportunities || opportunities.length === 0) {
      showPanel(els.opportunityList, false);
      return;
    }
    clearChildren(els.opportunityListBody);
    for (const opportunity of opportunities) {
      const row = el("div", "field");
      row.textContent =
        (opportunity.id || "") +
        " — " +
        (opportunity.productCode || "") +
        " (" +
        (opportunity.stage || "") +
        ", " +
        (opportunity.status || "") +
        ")";
      els.opportunityListBody.appendChild(row);
      els.opportunityListBody.appendChild(
        el(
          "div",
          "field",
          "Giá trị: " + formatVnd(opportunity.amountVnd) + " · Dự kiến chốt: " + (opportunity.expectedCloseDateUtc || "")
        )
      );
    }
    showPanel(els.opportunityList, true);
  }

  // Formatting only — the exact figure is shown to the RM here, on the trusted local path. It is
  // never what reaches the model: the server sends a coarse band instead (plan D12).
  function formatVnd(amount) {
    if (typeof amount !== "number") return "";
    try {
      return amount.toLocaleString("vi-VN") + " VND";
    } catch (formatError) {
      return String(amount) + " VND";
    }
  }

  function renderCampaignList() {
    const campaigns = uiState.campaigns;
    if (!campaigns || campaigns.length === 0) {
      showPanel(els.campaignList, false);
      return;
    }
    clearChildren(els.campaignListBody);
    for (const campaign of campaigns) {
      const row = el("div", "field");
      row.textContent = (campaign.id || "") + " — " + (campaign.name || "") + " (" + (campaign.status || "") + ")";
      els.campaignListBody.appendChild(row);
      if (campaign.objective) {
        els.campaignListBody.appendChild(el("div", "field", "Mục tiêu: " + campaign.objective));
      }
    }
    showPanel(els.campaignList, true);
  }

  function renderCallScriptPanel() {
    const script = uiState.callScript;
    if (!script) {
      showPanel(els.callScriptPanel, false);
      return;
    }

    setText(els.callScriptObjective, "Mục tiêu: " + (script.resolvedObjective || ""));

    // The badge is driven by the server's own warning vocabulary, not by inspecting the objective
    // text, so an RM can always tell an inferred objective from one they supplied.
    const inferred = Array.isArray(script.warnings) && script.warnings.indexOf("OBJECTIVE_INFERRED") !== -1;
    els.callScriptInferredBadge.textContent = inferred ? "Mục tiêu do hệ thống tự suy ra" : "";
    els.callScriptInferredBadge.hidden = !inferred;

    setText(els.callScriptOpening, script.opening || "");
    renderBulletList(els.callScriptDiscovery, script.discoveryQuestions);
    renderBulletList(els.callScriptTalkingPoints, script.talkingPoints);

    clearChildren(els.callScriptObjections);
    if (Array.isArray(script.objectionHandling)) {
      for (const item of script.objectionHandling) {
        if (!item) continue;
        els.callScriptObjections.appendChild(el("div", "field", "Từ chối: " + (item.objection || "")));
        els.callScriptObjections.appendChild(el("div", "field", "Phản hồi: " + (item.response || "")));
      }
    }

    setText(els.callScriptClosing, script.closing || "");
    els.callScriptApprovalLabel.textContent = script.requiresHumanApproval ? "Cần RM kiểm tra và phê duyệt" : "";

    clearChildren(els.callScriptSourceChips);
    if (Array.isArray(script.sourceIds)) {
      for (const sourceId of script.sourceIds) {
        els.callScriptSourceChips.appendChild(chip(sourceId));
      }
    }

    showPanel(els.callScriptPanel, true);
  }

  // Shared by the two generator tools: both carry requiresHumanApproval and a piiMaskSummary of
  // the same shape, so the trace renders them identically rather than duplicating the logic.
  function appendGenerationTraceDetail(draft) {
    els.traceEntries.appendChild(
      el("div", "trace-entry", "Cần RM duyệt: " + (draft.requiresHumanApproval ? "có" : "không"))
    );

    const maskedTypes = draft.piiMaskSummary && draft.piiMaskSummary.maskedFieldTypes;
    if (maskedTypes && maskedTypes.length > 0) {
      const maskedRow = el("div", "trace-entry", "Trường đã ẩn:");
      const chipRow = el("div", "chip-row");
      for (const fieldType of maskedTypes) {
        chipRow.appendChild(chip(fieldType));
      }
      maskedRow.appendChild(chipRow);
      els.traceEntries.appendChild(maskedRow);
    }
  }

  function renderBulletList(node, items) {
    clearChildren(node);
    if (!Array.isArray(items)) return;
    for (const item of items) {
      node.appendChild(el("li", null, item));
    }
  }

  function renderDraftPanel() {
    const draft = uiState.emailDraft;
    if (!draft) {
      showPanel(els.draftPanel, false);
      return;
    }
    setText(els.draftSubject, draft.subject || "");
    setText(els.draftBody, draft.body || "");
    els.draftApprovalLabel.textContent = draft.requiresHumanApproval
      ? "Cần RM kiểm tra và phê duyệt"
      : "";
    clearChildren(els.draftSourceChips);
    if (draft.sourceIds) {
      for (const sourceId of draft.sourceIds) {
        els.draftSourceChips.appendChild(chip(sourceId));
      }
    }
    showPanel(els.draftPanel, true);
  }

  function setText(node, text) {
    node.textContent = text;
  }

  function renderTracePanel() {
    if (uiState.trace.length === 0 && uiState.sourceChipIds.size === 0) {
      showPanel(els.tracePanel, false);
      return;
    }

    clearChildren(els.traceEntries);
    for (const entry of uiState.trace) {
      const row = el(
        "div",
        "trace-entry",
        (entry.toolName || "") + " — " + (entry.status || "") + " (" + (entry.durationMs ?? "?") + "ms, traceId=" + (entry.traceId || "") + ")"
      );
      els.traceEntries.appendChild(row);

      if (entry.toolName === "generate_email" && uiState.emailDraft) {
        appendGenerationTraceDetail(uiState.emailDraft);
      }

      if (entry.toolName === "generate_call_script" && uiState.callScript) {
        appendGenerationTraceDetail(uiState.callScript);

        if (Array.isArray(uiState.callScript.warnings) && uiState.callScript.warnings.length > 0) {
          const warningRow = el("div", "trace-entry", "Cảnh báo:");
          const warningChips = el("div", "chip-row");
          for (const warning of uiState.callScript.warnings) {
            warningChips.appendChild(chip(warning));
          }
          warningRow.appendChild(warningChips);
          els.traceEntries.appendChild(warningRow);
        }

        if (uiState.callScript.selectedOpportunityId) {
          els.traceEntries.appendChild(
            el("div", "trace-entry", "Cơ hội đã chọn: " + uiState.callScript.selectedOpportunityId)
          );
        }
      }
    }

    clearChildren(els.sourceChips);
    const sortedSourceIds = Array.from(uiState.sourceChipIds).sort();
    for (const sourceId of sortedSourceIds) {
      els.sourceChips.appendChild(chip(sourceId));
    }

    showPanel(els.tracePanel, true);
  }

  // ---- condition 3: parse-then-dispatch — never gate parsing/rendering on response.ok ----
  async function sendMessage(message) {
    if (busy) return;
    setBusy(true);
    hideError();
    appendChatBubble("rm", message);

    try {
      const res = await fetch("/api/chat", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ message: message, sessionId: sessionId }),
      });

      let body;
      try {
        body = await res.json();
      } catch (parseError) {
        showError("Không thể đọc phản hồi từ máy chủ.");
        return;
      }

      renderTurn(body);
    } catch (networkError) {
      showError("Không thể kết nối tới máy chủ. Vui lòng thử lại.");
    } finally {
      setBusy(false);
      els.input.value = "";
    }
  }

  function renderTurn(body) {
    uiState.lastTurnStatus = body.status;

    if (body.status === "not_found") {
      // The Host now returns a deterministic reply naming the id it actually looked up
      // ("Không tìm thấy khách hàng CUS-9999."), so it is shown as the assistant's turn in the
      // chat log and the banner is deliberately NOT also shown — one message, not two.
      appendChatBubble("assistant", body.reply || "Không tìm thấy.");
      hideError();
    } else {
      if (body.reply) appendChatBubble("assistant", body.reply);

      if (body.status === "error") {
        showError(errorMessageFor(body.error));
      } else {
        hideError();
      }
    }

    // Unconditional regardless of status — an ambiguous (409) or error response still renders
    // whatever data/trace the contract actually supplied (condition 3).
    mergeTurnData(body.data);
    mergeTrace(body.toolTrace);
    renderAll();
  }

  // ---- condition 2: Reset — always ends with a clean client UI state and a fresh sessionId,
  // even if the server-side DELETE fails or the network request itself throws. ----
  async function resetSession() {
    if (busy) return;
    setBusy(true);
    try {
      await fetch("/api/chat/sessions/" + encodeURIComponent(sessionId), { method: "DELETE" });
    } catch (networkError) {
      // best-effort — client state is still fully reset below regardless of outcome
    } finally {
      clearChildren(els.chatLog);
      hideError();
      uiState = createInitialState();
      renderAll();
      sessionId = crypto.randomUUID();
      setBusy(false);
    }
  }

  els.form.addEventListener("submit", function (e) {
    e.preventDefault();
    const message = els.input.value.trim();
    if (message) sendMessage(message);
  });

  els.resetButton.addEventListener("click", function () {
    resetSession();
  });

  renderAll();
})();
