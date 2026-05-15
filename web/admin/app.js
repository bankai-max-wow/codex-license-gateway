const state = {
  token: localStorage.getItem("admin_token") || ""
};

const loginView = document.getElementById("loginView");
const appView = document.getElementById("appView");

document.getElementById("loginButton").addEventListener("click", login);
document.getElementById("createLicensesButton").addEventListener("click", createLicenses);
document.getElementById("saveProviderKeyButton").addEventListener("click", saveProviderKey);
document.getElementById("saveRateLimitsButton").addEventListener("click", saveRateLimits);

if (state.token) {
  boot();
}

async function login() {
  const username = document.getElementById("username").value;
  const password = document.getElementById("password").value;
  const errorEl = document.getElementById("loginError");
  errorEl.textContent = "";

  const response = await fetch("/api/admin/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password })
  });

  if (!response.ok) {
    errorEl.textContent = "Login failed.";
    return;
  }

  const body = await response.json();
  state.token = body.token;
  localStorage.setItem("admin_token", state.token);
  boot();
}

async function boot() {
  loginView.classList.add("hidden");
  appView.classList.remove("hidden");
  await Promise.all([loadDashboard(), loadLicenses(), loadRateLimits()]);
}

async function loadDashboard() {
  const response = await authFetch("/api/admin/dashboard");
  const body = await response.json();
  const activeName = body.activeProviderKey?.name || "none";
  document.getElementById("stats").textContent =
    `Licenses: ${body.licenses} | Redeemed: ${body.redeemed} | Active sessions: ${body.activeSessions} | Active provider key: ${activeName}`;
}

async function loadLicenses() {
  const response = await authFetch("/api/admin/licenses");
  const rows = await response.json();
  document.getElementById("licenseRows").innerHTML = rows
    .map(
      (row) => `
        <tr>
          <td><code>${row.code}</code></td>
          <td>${row.plan}</td>
          <td>${row.status}</td>
          <td>${row.durationMonths || "-"}</td>
          <td>${row.customerEmail || "-"}</td>
          <td>${row.redeemedAt || "-"}</td>
          <td>${row.expiresAt || "-"}</td>
          <td>${row.resetAt || "-"}</td>
          <td><button data-license-id="${row.id}" data-next-action="${row.status === "disabled" ? "enable" : "disable"}">${row.status === "disabled" ? "Enable" : "Disable"}</button></td>
        </tr>
      `
    )
    .join("");

  document.querySelectorAll("[data-license-id]").forEach((button) => {
    button.addEventListener("click", async () => {
      const id = button.getAttribute("data-license-id");
      const nextAction = button.getAttribute("data-next-action");
      const body =
        nextAction === "disable"
          ? { reason: prompt("Disable reason", "Disabled by admin") || "Disabled by admin" }
          : {};
      await authFetch(`/api/admin/licenses/${id}/${nextAction}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body)
      });
      await Promise.all([loadDashboard(), loadLicenses()]);
    });
  });
}

async function createLicenses() {
  const quantity = Number(document.getElementById("quantity").value);
  const plan = document.getElementById("plan").value;
  const durationMonths = Number(document.getElementById("durationMonths").value);
  const response = await authFetch("/api/admin/licenses", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ quantity, plan, durationMonths })
  });
  const body = await response.json();
  document.getElementById("createdLicenses").innerHTML = body
    .map((item) => `<div><code>${item.code}</code> (${item.plan}, ${item.durationMonths} month)</div>`)
    .join("");
  await Promise.all([loadDashboard(), loadLicenses()]);
}

async function saveProviderKey() {
  const name = document.getElementById("providerName").value;
  const apiKey = document.getElementById("providerKey").value;
  await authFetch("/api/admin/provider-keys", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name, apiKey, provider: "openai" })
  });
  document.getElementById("providerKey").value = "";
  await loadDashboard();
}

async function loadRateLimits() {
  const response = await authFetch("/api/admin/rate-limits");
  const body = await response.json();
  document.getElementById("rateLimitConfig").value = JSON.stringify(body, null, 2);
}

async function saveRateLimits() {
  const raw = document.getElementById("rateLimitConfig").value;
  const parsed = JSON.parse(raw);
  await authFetch("/api/admin/rate-limits", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(parsed)
  });
  await loadDashboard();
}

async function authFetch(url, init = {}) {
  const headers = new Headers(init.headers || {});
  headers.set("Authorization", `Bearer ${state.token}`);
  const response = await fetch(url, { ...init, headers });
  if (response.status === 401) {
    localStorage.removeItem("admin_token");
    location.reload();
  }
  return response;
}
