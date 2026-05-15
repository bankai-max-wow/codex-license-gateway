const state = {
  token: localStorage.getItem("admin_token") || "",
  dashboard: null,
  licenses: [],
  providerKeys: [],
  sessions: [],
  rateLimitConfig: null,
  selectedLicenseId: null,
  selectedLicenseDetail: null,
  filters: {
    query: "",
    status: "all"
  }
};

const elements = {
  loginView: document.getElementById("loginView"),
  appView: document.getElementById("appView"),
  loginButton: document.getElementById("loginButton"),
  logoutButton: document.getElementById("logoutButton"),
  loginError: document.getElementById("loginError"),
  username: document.getElementById("username"),
  password: document.getElementById("password"),
  plan: document.getElementById("plan"),
  quantity: document.getElementById("quantity"),
  durationMonths: document.getElementById("durationMonths"),
  createLicensesButton: document.getElementById("createLicensesButton"),
  createdLicenses: document.getElementById("createdLicenses"),
  providerName: document.getElementById("providerName"),
  providerKey: document.getElementById("providerKey"),
  saveProviderKeyButton: document.getElementById("saveProviderKeyButton"),
  rateLimitConfig: document.getElementById("rateLimitConfig"),
  saveRateLimitsButton: document.getElementById("saveRateLimitsButton"),
  metricCards: document.getElementById("metricCards"),
  statusLine: document.getElementById("statusLine"),
  activeKeyName: document.getElementById("activeKeyName"),
  activeKeyMeta: document.getElementById("activeKeyMeta"),
  licenseSearch: document.getElementById("licenseSearch"),
  licenseStatusFilter: document.getElementById("licenseStatusFilter"),
  licenseCount: document.getElementById("licenseCount"),
  licenseRows: document.getElementById("licenseRows"),
  licenseDetail: document.getElementById("licenseDetail"),
  providerKeyRows: document.getElementById("providerKeyRows"),
  sessionRows: document.getElementById("sessionRows"),
  globalBanner: document.getElementById("globalBanner")
};

bindEvents();

if (state.token) {
  boot();
}

async function login() {
  elements.loginError.textContent = "";
  try {
    const body = await jsonFetch("/api/admin/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        username: elements.username.value.trim(),
        password: elements.password.value
      })
    });
    state.token = body.token;
    localStorage.setItem("admin_token", state.token);
    await boot();
  } catch (error) {
    elements.loginError.textContent = error.message || "Login failed.";
  }
}

async function boot() {
  elements.loginView.classList.add("hidden");
  elements.appView.classList.remove("hidden");
  await refreshAll();
}

async function refreshAll() {
  clearBanner();
  const [dashboard, licenses, providerKeys, sessions, rateLimitConfig] = await Promise.all([
    authJson("/api/admin/dashboard"),
    authJson("/api/admin/licenses"),
    authJson("/api/admin/provider-keys"),
    authJson("/api/admin/sessions"),
    authJson("/api/admin/rate-limits")
  ]);

  state.dashboard = dashboard;
  state.licenses = licenses;
  state.providerKeys = providerKeys;
  state.sessions = sessions;
  state.rateLimitConfig = rateLimitConfig;

  if (!state.selectedLicenseId || !state.licenses.some((row) => row.id === state.selectedLicenseId)) {
    state.selectedLicenseId = state.licenses[0]?.id || null;
  }

  if (state.selectedLicenseId) {
    await loadLicenseDetail(state.selectedLicenseId);
  } else {
    state.selectedLicenseDetail = null;
  }

  render();
}

async function loadLicenseDetail(id) {
  state.selectedLicenseId = id;
  state.selectedLicenseDetail = await authJson(`/api/admin/licenses/${id}`);
  renderLicenses();
  renderLicenseDetail();
}

function render() {
  renderDashboard();
  renderLicenses();
  renderProviderKeys();
  renderSessions();
  renderRateLimits();
  renderLicenseDetail();
}

function renderDashboard() {
  const dashboard = state.dashboard || {};
  const activeKey = dashboard.activeProviderKey;
  elements.activeKeyName.textContent = activeKey?.name || "No active key";
  elements.activeKeyMeta.textContent = activeKey
    ? `${activeKey.provider} | updated ${formatDate(activeKey.updatedAt)}`
    : "Customer app refreshes will fail until a provider key is active.";

  const counts = dashboard.statusCounts || {};
  elements.statusLine.innerHTML = [
    statusChip("Active", counts.active || 0, "active"),
    statusChip("New", counts.new || 0, ""),
    statusChip("Disabled", counts.disabled || 0, "disabled"),
    statusChip("Expired", counts.expired || 0, "expired")
  ].join("");

  const cards = [
    {
      label: "License Pool",
      value: dashboard.licenses || 0,
      detail: `${dashboard.redeemed || 0} redeemed`
    },
    {
      label: "Live Sessions",
      value: dashboard.activeSessions || 0,
      detail: "Revokable from the browser"
    },
    {
      label: "Stored Provider Keys",
      value: dashboard.providerKeys || 0,
      detail: activeKey ? `Active: ${activeKey.name}` : "No key active"
    },
    {
      label: "Usage Events",
      value: dashboard.usageEvents || 0,
      detail: "Tracked for quota windows"
    },
    {
      label: "5x Active",
      value: state.licenses.filter((row) => row.plan === "5x" && row.status === "active").length,
      detail: "Customer licenses on 5x"
    },
    {
      label: "20x / Unlimited",
      value: state.licenses.filter((row) => row.status === "active" && row.plan !== "5x").length,
      detail: "Higher tier active licenses"
    }
  ];

  elements.metricCards.innerHTML = cards
    .map(
      (card) => `
        <section class="panel metric-card">
          <div class="metric-label">${escapeHtml(card.label)}</div>
          <div class="metric-value">${escapeHtml(String(card.value))}</div>
          <div class="subtle">${escapeHtml(card.detail)}</div>
        </section>
      `
    )
    .join("");

  const latest = dashboard.latestLicenses || [];
  if (!latest.length) {
    elements.createdLicenses.className = "list empty";
    elements.createdLicenses.textContent = "No recent license activity yet.";
    return;
  }

  elements.createdLicenses.className = "list";
  elements.createdLicenses.innerHTML = latest
    .map(
      (row) => `
        <div class="recent-item">
          <div style="display:flex; justify-content:space-between; gap:12px; align-items:center;">
            <strong class="mono">${escapeHtml(row.code)}</strong>
            <span class="status-pill ${escapeHtml(row.status)}">${escapeHtml(row.status)}</span>
          </div>
          <div class="subtle">${escapeHtml(row.plan)} | ${escapeHtml(row.customerEmail || "not redeemed")} | ${escapeHtml(formatDate(row.createdAt))}</div>
        </div>
      `
    )
    .join("");
}

function renderLicenses() {
  const filtered = filteredLicenses();
  elements.licenseCount.textContent = `${filtered.length} visible of ${state.licenses.length} total licenses`;

  if (!filtered.length) {
    elements.licenseRows.innerHTML = `
      <tr>
        <td colspan="7">
          <div class="empty">No licenses match the current filter.</div>
        </td>
      </tr>
    `;
    return;
  }

  elements.licenseRows.innerHTML = filtered
    .map(
      (row) => `
        <tr data-license-id="${escapeHtml(row.id)}" class="${row.id === state.selectedLicenseId ? "selected" : ""}">
          <td class="mono">${escapeHtml(row.code)}</td>
          <td>${escapeHtml(row.plan)}</td>
          <td><span class="status-pill ${escapeHtml(row.status)}">${escapeHtml(row.status)}</span></td>
          <td>${escapeHtml(row.customerEmail || "-")}</td>
          <td>${escapeHtml(String(row.durationMonths || "-"))}</td>
          <td>${escapeHtml(formatDate(row.expiresAt))}</td>
          <td>${escapeHtml(formatDate(row.resetAt))}</td>
        </tr>
      `
    )
    .join("");
}

function renderProviderKeys() {
  if (!state.providerKeys.length) {
    elements.providerKeyRows.innerHTML = `
      <tr>
        <td colspan="5">
          <div class="empty">No provider key has been stored yet.</div>
        </td>
      </tr>
    `;
    return;
  }

  elements.providerKeyRows.innerHTML = state.providerKeys
    .map(
      (row) => `
        <tr>
          <td>${escapeHtml(row.name)}</td>
          <td>${escapeHtml(row.provider)}</td>
          <td><span class="status-pill ${row.active ? "active" : "new"}">${row.active ? "active" : "stored"}</span></td>
          <td>${escapeHtml(formatDate(row.updatedAt))}</td>
          <td>
            <div class="inline-actions">
              <button class="secondary" type="button" data-provider-action="activate" data-provider-id="${escapeHtml(row.id)}" ${row.active ? "disabled" : ""}>Activate</button>
              <button class="danger" type="button" data-provider-action="delete" data-provider-id="${escapeHtml(row.id)}">Delete</button>
            </div>
          </td>
        </tr>
      `
    )
    .join("");
}

function renderSessions() {
  if (!state.sessions.length) {
    elements.sessionRows.innerHTML = `
      <tr>
        <td colspan="7">
          <div class="empty">No sessions issued yet.</div>
        </td>
      </tr>
    `;
    return;
  }

  elements.sessionRows.innerHTML = state.sessions
    .map(
      (row) => `
        <tr>
          <td class="mono">${escapeHtml(row.licenseCode)}</td>
          <td>${escapeHtml(row.plan)}</td>
          <td>${escapeHtml(row.customerEmail || "-")}</td>
          <td class="mono">${escapeHtml(row.deviceId || "-")}</td>
          <td>${escapeHtml(formatDate(row.createdAt))}</td>
          <td><span class="status-pill ${row.revokedAt ? "disabled" : "active"}">${row.revokedAt ? "revoked" : "live"}</span></td>
          <td>
            <button class="danger" type="button" data-session-action="revoke" data-session-id="${escapeHtml(row.id)}" ${row.revokedAt ? "disabled" : ""}>Revoke</button>
          </td>
        </tr>
      `
    )
    .join("");
}

function renderRateLimits() {
  elements.rateLimitConfig.value = JSON.stringify(state.rateLimitConfig || {}, null, 2);
}

function renderLicenseDetail() {
  const detail = state.selectedLicenseDetail;
  if (!detail?.license) {
    elements.licenseDetail.className = "empty";
    elements.licenseDetail.textContent = "Select a license from the table to manage it.";
    return;
  }

  const license = detail.license;
  const sessions = detail.sessions || [];
  const recentUsage = detail.recentUsage || [];
  const rateLimits = formatRateLimits(license.rateLimits);

  elements.licenseDetail.className = "";
  elements.licenseDetail.innerHTML = `
    <div class="split">
      <div class="info-block">
        <div class="metric-label">Selected License</div>
        <div style="display:flex; justify-content:space-between; gap:12px; align-items:center;">
          <div class="mono" style="font-size:20px;">${escapeHtml(license.code)}</div>
          <span class="status-pill ${escapeHtml(license.status)}">${escapeHtml(license.status)}</span>
        </div>
        <div class="kv">
          <div class="kv-row"><span>Customer</span><strong>${escapeHtml(license.customerEmail || "-")}</strong></div>
          <div class="kv-row"><span>Device</span><strong class="mono">${escapeHtml(license.deviceId || "-")}</strong></div>
          <div class="kv-row"><span>Redeemed</span><strong>${escapeHtml(formatDate(license.redeemedAt))}</strong></div>
          <div class="kv-row"><span>Expires</span><strong>${escapeHtml(formatDate(license.expiresAt))}</strong></div>
        </div>
      </div>
      <div class="info-block">
        <div class="metric-label">Immediate Controls</div>
        <div class="actions">
          <button class="${license.status === "disabled" ? "good" : "danger"}" type="button" data-license-action="${license.status === "disabled" ? "enable" : "disable"}" data-license-id="${escapeHtml(license.id)}">
            ${license.status === "disabled" ? "Re-enable License" : "Disable License"}
          </button>
          <button class="warning" type="button" data-license-action="reset-device" data-license-id="${escapeHtml(license.id)}">Reset Device Lock</button>
          <button class="ghost" type="button" data-license-action="revoke-sessions" data-license-id="${escapeHtml(license.id)}">Revoke All Sessions</button>
        </div>
        <div class="subtle">Disable kicks out current sessions. Device reset clears the bound machine so the next login can attach a new one.</div>
      </div>
    </div>

    <div class="split" style="margin-top:12px;">
      <div class="info-block">
        <div class="metric-label">Edit License</div>
        <div class="form-grid">
          <div class="field">
            <label for="detailPlan">Plan</label>
            <select id="detailPlan">
              <option value="5x" ${license.plan === "5x" ? "selected" : ""}>5x</option>
              <option value="20x" ${license.plan === "20x" ? "selected" : ""}>20x</option>
              <option value="unlimited" ${license.plan === "unlimited" ? "selected" : ""}>Unlimited</option>
            </select>
          </div>
          <div class="field">
            <label for="detailMonths">Duration Months</label>
            <input id="detailMonths" type="number" min="1" max="36" value="${escapeHtml(String(license.durationMonths || 1))}" />
          </div>
          <div class="field">
            <label for="detailEmail">Customer Email</label>
            <input id="detailEmail" value="${escapeHtml(license.customerEmail || "")}" />
          </div>
          <div class="field">
            <label for="detailExpiresAt">Expiry</label>
            <input id="detailExpiresAt" type="datetime-local" value="${escapeHtml(toLocalInputValue(license.expiresAt))}" />
          </div>
          <div class="actions">
            <button id="saveLicenseChangesButton" class="secondary" type="button">Save Changes</button>
          </div>
        </div>
      </div>

      <div class="info-block">
        <div class="metric-label">Renew Subscription</div>
        <div class="form-grid">
          <div class="field">
            <label for="extendMonths">Extend By Months</label>
            <input id="extendMonths" type="number" min="1" max="36" value="1" />
          </div>
          <div class="actions">
            <button id="extendLicenseButton" class="primary" type="button">Add Time</button>
          </div>
          <div class="subtle">If the subscription is already expired, the extension starts from now. If it is still active, it extends from the current expiry.</div>
        </div>
      </div>
    </div>

    <div class="info-block" style="margin-top:12px;">
      <div class="metric-label">Rate Window Snapshot</div>
      ${rateLimits || '<div class="empty">No plan rate data for this license.</div>'}
    </div>

    <div class="split" style="margin-top:12px;">
      <div class="info-block">
        <div class="metric-label">Sessions For This License</div>
        ${renderCompactSessions(sessions)}
      </div>
      <div class="info-block">
        <div class="metric-label">Recent Usage</div>
        ${renderUsage(recentUsage)}
      </div>
    </div>
  `;

  bindLicenseDetailActions();
}

function renderCompactSessions(sessions) {
  if (!sessions.length) {
    return '<div class="empty">No sessions issued for this license.</div>';
  }

  return `
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Device</th>
            <th>Created</th>
            <th>State</th>
          </tr>
        </thead>
        <tbody>
          ${sessions
            .map(
              (row) => `
                <tr>
                  <td class="mono">${escapeHtml(row.deviceId || "-")}</td>
                  <td>${escapeHtml(formatDate(row.createdAt))}</td>
                  <td><span class="status-pill ${row.revokedAt ? "disabled" : "active"}">${row.revokedAt ? "revoked" : "live"}</span></td>
                </tr>
              `
            )
            .join("")}
        </tbody>
      </table>
    </div>
  `;
}

function renderUsage(items) {
  if (!items.length) {
    return '<div class="empty">No usage events captured yet.</div>';
  }

  return `
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Category</th>
            <th>Model</th>
            <th>Units</th>
            <th>Tokens</th>
            <th>When</th>
          </tr>
        </thead>
        <tbody>
          ${items
            .map(
              (row) => `
                <tr>
                  <td>${escapeHtml(row.category)}</td>
                  <td>${escapeHtml(row.model)}</td>
                  <td>${escapeHtml(String(row.requestUnits))}</td>
                  <td>${escapeHtml(String((row.inputTokens || 0) + (row.outputTokens || 0)))}</td>
                  <td>${escapeHtml(formatDate(row.createdAt))}</td>
                </tr>
              `
            )
            .join("")}
        </tbody>
      </table>
    </div>
  `;
}

function formatRateLimits(summary) {
  const categories = summary?.categories || {};
  const cards = Object.entries(categories)
    .map(([category, models]) => {
      const rows = Object.entries(models)
        .map(
          ([model, window]) => `
            <div class="kv-row">
              <span>${escapeHtml(`${category} / ${model}`)}</span>
              <strong>${window.limit === null ? "Unlimited" : `${window.remaining}/${window.limit} left`}</strong>
            </div>
            <div class="kv-row">
              <span>Weekly</span>
              <strong>${window.weeklyLimit === null ? "Unlimited" : `${window.weeklyRemaining}/${window.weeklyLimit} left`}</strong>
            </div>
          `
        )
        .join("");
      return `<div class="info-block">${rows}</div>`;
    })
    .join("");

  return cards ? `<div class="card-grid">${cards}</div>` : "";
}

function filteredLicenses() {
  const query = state.filters.query.trim().toLowerCase();
  const status = state.filters.status;
  return state.licenses.filter((row) => {
    const matchesStatus = status === "all" ? true : row.status === status;
    const haystack = [row.code, row.plan, row.status, row.customerEmail || ""].join(" ").toLowerCase();
    const matchesQuery = query ? haystack.includes(query) : true;
    return matchesStatus && matchesQuery;
  });
}

function bindEvents() {
  elements.loginButton.addEventListener("click", login);
  elements.logoutButton.addEventListener("click", logout);
  elements.createLicensesButton.addEventListener("click", createLicenses);
  elements.saveProviderKeyButton.addEventListener("click", saveProviderKey);
  elements.saveRateLimitsButton.addEventListener("click", saveRateLimits);
  elements.licenseSearch.addEventListener("input", (event) => {
    state.filters.query = event.target.value;
    renderLicenses();
  });
  elements.licenseStatusFilter.addEventListener("change", (event) => {
    state.filters.status = event.target.value;
    renderLicenses();
  });

  elements.licenseRows.addEventListener("click", async (event) => {
    const row = event.target.closest("[data-license-id]");
    if (!row) {
      return;
    }
    await loadLicenseDetail(row.getAttribute("data-license-id"));
  });

  elements.providerKeyRows.addEventListener("click", async (event) => {
    const button = event.target.closest("[data-provider-action]");
    if (!button) {
      return;
    }

    const id = button.getAttribute("data-provider-id");
    const action = button.getAttribute("data-provider-action");

    try {
      if (action === "activate") {
        await authJson(`/api/admin/provider-keys/${id}/activate`, { method: "POST" });
      }
      if (action === "delete") {
        if (!confirm("Delete this stored provider key?")) {
          return;
        }
        await authFetch(`/api/admin/provider-keys/${id}`, { method: "DELETE" });
      }
      await refreshAll();
      setBanner("Provider key state updated.", "success");
    } catch (error) {
      setBanner(error.message, "error");
    }
  });

  elements.sessionRows.addEventListener("click", async (event) => {
    const button = event.target.closest("[data-session-action='revoke']");
    if (!button) {
      return;
    }

    try {
      await authJson(`/api/admin/sessions/${button.getAttribute("data-session-id")}/revoke`, {
        method: "POST"
      });
      await refreshAll();
      setBanner("Session revoked.", "success");
    } catch (error) {
      setBanner(error.message, "error");
    }
  });
}

function bindLicenseDetailActions() {
  document.getElementById("saveLicenseChangesButton")?.addEventListener("click", saveSelectedLicenseChanges);
  document.getElementById("extendLicenseButton")?.addEventListener("click", extendSelectedLicense);

  elements.licenseDetail.querySelectorAll("[data-license-action]").forEach((button) => {
    button.addEventListener("click", async () => {
      const id = button.getAttribute("data-license-id");
      const action = button.getAttribute("data-license-action");

      try {
        if (action === "disable") {
          const reason = prompt("Disable reason", "Disabled by admin") || "Disabled by admin";
          await authJson(`/api/admin/licenses/${id}/disable`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ reason })
          });
        }
        if (action === "enable") {
          await authJson(`/api/admin/licenses/${id}/enable`, { method: "POST" });
        }
        if (action === "reset-device") {
          await authJson(`/api/admin/licenses/${id}/reset-device`, { method: "POST" });
        }
        if (action === "revoke-sessions") {
          await authJson(`/api/admin/licenses/${id}/revoke-sessions`, { method: "POST" });
        }
        await refreshAll();
        setBanner(`License action "${action}" completed.`, "success");
      } catch (error) {
        setBanner(error.message, "error");
      }
    });
  });
}

async function createLicenses() {
  try {
    const body = await authJson("/api/admin/licenses", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        quantity: Number(elements.quantity.value),
        plan: elements.plan.value,
        durationMonths: Number(elements.durationMonths.value)
      })
    });

    elements.createdLicenses.className = "list";
    elements.createdLicenses.innerHTML = body
      .map(
        (item) => `
          <div class="recent-item">
            <div class="mono">${escapeHtml(item.code)}</div>
            <div class="subtle">${escapeHtml(item.plan)} | ${escapeHtml(String(item.durationMonths))} month${item.durationMonths === 1 ? "" : "s"}</div>
          </div>
        `
      )
      .join("");

    await refreshAll();
    setBanner(`Generated ${body.length} license code${body.length === 1 ? "" : "s"}.`, "success");
  } catch (error) {
    setBanner(error.message, "error");
  }
}

async function saveProviderKey() {
  try {
    await authJson("/api/admin/provider-keys", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        name: elements.providerName.value.trim(),
        apiKey: elements.providerKey.value,
        provider: "openai"
      })
    });
    elements.providerKey.value = "";
    elements.providerName.value = "";
    await refreshAll();
    setBanner("New provider key stored and activated.", "success");
  } catch (error) {
    setBanner(error.message, "error");
  }
}

async function saveRateLimits() {
  try {
    const parsed = JSON.parse(elements.rateLimitConfig.value);
    await authJson("/api/admin/rate-limits", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(parsed)
    });
    state.rateLimitConfig = parsed;
    setBanner("Rate-limit policy saved.", "success");
  } catch (error) {
    setBanner(error.message || "Invalid JSON.", "error");
  }
}

async function saveSelectedLicenseChanges() {
  if (!state.selectedLicenseId) {
    return;
  }

  try {
    await authJson(`/api/admin/licenses/${state.selectedLicenseId}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        plan: document.getElementById("detailPlan").value,
        durationMonths: Number(document.getElementById("detailMonths").value),
        customerEmail: document.getElementById("detailEmail").value.trim(),
        expiresAt: fromLocalInputValue(document.getElementById("detailExpiresAt").value)
      })
    });
    await refreshAll();
    setBanner("License changes saved.", "success");
  } catch (error) {
    setBanner(error.message, "error");
  }
}

async function extendSelectedLicense() {
  if (!state.selectedLicenseId) {
    return;
  }

  try {
    await authJson(`/api/admin/licenses/${state.selectedLicenseId}/extend`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        months: Number(document.getElementById("extendMonths").value)
      })
    });
    await refreshAll();
    setBanner("Subscription extended.", "success");
  } catch (error) {
    setBanner(error.message, "error");
  }
}

function logout() {
  localStorage.removeItem("admin_token");
  state.token = "";
  location.reload();
}

function statusChip(label, value, className) {
  return `<span class="chip ${className}">${escapeHtml(label)} <strong>${escapeHtml(String(value))}</strong></span>`;
}

function setBanner(message, type) {
  elements.globalBanner.className = `banner ${type}`;
  elements.globalBanner.textContent = message;
}

function clearBanner() {
  elements.globalBanner.className = "banner";
  elements.globalBanner.textContent = "";
}

async function authJson(url, init = {}) {
  return jsonFetch(url, withAuth(init));
}

async function authFetch(url, init = {}) {
  const response = await fetch(url, withAuth(init));
  if (response.status === 401) {
    logout();
    throw new Error("Session expired.");
  }
  if (!response.ok) {
    throw new Error(await extractError(response));
  }
  return response;
}

function withAuth(init) {
  const headers = new Headers(init.headers || {});
  headers.set("Authorization", `Bearer ${state.token}`);
  return { ...init, headers };
}

async function jsonFetch(url, init = {}) {
  const response = await fetch(url, init);
  if (response.status === 401 && init.headers instanceof Headers && init.headers.get("Authorization")) {
    logout();
    throw new Error("Session expired.");
  }
  if (!response.ok) {
    throw new Error(await extractError(response));
  }
  return response.status === 204 ? null : response.json();
}

async function extractError(response) {
  try {
    const body = await response.json();
    return body.error || JSON.stringify(body);
  } catch {
    return `Request failed with status ${response.status}`;
  }
}

function formatDate(value) {
  if (!value) {
    return "-";
  }
  return new Date(value).toLocaleString();
}

function toLocalInputValue(value) {
  if (!value) {
    return "";
  }
  const date = new Date(value);
  const pad = (num) => String(num).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function fromLocalInputValue(value) {
  if (!value) {
    return "";
  }
  return new Date(value).toISOString();
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}
