import "dotenv/config";
import express from "express";
import path from "path";
import { fileURLToPath } from "url";
import {
  authenticateAdmin,
  authenticateSession,
  createAdminToken,
  createSessionToken,
  hashPassword,
  verifyPassword
} from "./lib/auth.js";
import { db, initDb } from "./lib/db.js";
import {
  addMonthsIso,
  createLicenseCode,
  decryptSecret,
  encryptSecret,
  nowIso,
  randomId,
  sanitizeLicense
} from "./lib/utils.js";
import {
  consumeQuota,
  ensureQuotaWindow,
  getLicenseRateSummary,
  getPlanDefinition,
  getRateLimitConfig,
  saveRateLimitConfig,
  normalizePlan
} from "./lib/plans.js";

const app = express();
const port = Number(process.env.PORT || 3000);
const __dirname = path.dirname(fileURLToPath(import.meta.url));

app.use(express.json());
app.use(express.urlencoded({ extended: true }));
app.use("/static", express.static(path.join(__dirname, "..", "web")));
app.use("/admin", express.static(path.join(__dirname, "..", "web", "admin")));

initDb();
seedAdmin();

app.get("/health", (_req, res) => {
  res.json({ ok: true, now: nowIso() });
});

app.post("/api/admin/login", (req, res) => {
  const { username, password } = req.body ?? {};
  const admin = db
    .prepare("SELECT id, username, password_hash FROM admins WHERE username = ?")
    .get(username);

  if (!admin || !verifyPassword(password || "", admin.password_hash)) {
    return res.status(401).json({ error: "Invalid credentials" });
  }

  return res.json({
    token: createAdminToken({ adminId: admin.id, username: admin.username })
  });
});

app.get("/api/admin/dashboard", authenticateAdmin, (_req, res) => {
  syncExpiredLicenses();
  const licenses = db.prepare("SELECT COUNT(*) AS count FROM licenses").get().count;
  const redeemed = db
    .prepare("SELECT COUNT(*) AS count FROM licenses WHERE redeemed_at IS NOT NULL")
    .get().count;
  const activeSessions = db
    .prepare("SELECT COUNT(*) AS count FROM sessions WHERE revoked_at IS NULL")
    .get().count;
  const usageEvents = db.prepare("SELECT COUNT(*) AS count FROM usage_events").get().count;
  const providerKeys = db.prepare("SELECT COUNT(*) AS count FROM provider_keys").get().count;
  const statusCounts = db
    .prepare(
      `SELECT status, COUNT(*) AS count
       FROM licenses
       GROUP BY status`
    )
    .all()
    .reduce((acc, row) => {
      acc[row.status] = Number(row.count);
      return acc;
    }, {});
  const activeProviderKey = db
    .prepare(
      "SELECT id, name, provider, active, created_at, updated_at FROM provider_keys WHERE active = 1 ORDER BY updated_at DESC LIMIT 1"
    )
    .get();
  const latestLicenses = db
    .prepare(
      `SELECT id, code, plan, status, customer_email, duration_months, redeemed_at, expires_at, disabled_at, disabled_reason, reset_at, created_at
       FROM licenses
       ORDER BY created_at DESC
       LIMIT 5`
    )
    .all()
    .map(sanitizeLicense);

  res.json({
    licenses,
    redeemed,
    activeSessions,
    usageEvents,
    providerKeys,
    statusCounts,
    activeProviderKey: activeProviderKey ? providerKeyView(activeProviderKey) : null,
    latestLicenses,
    rateLimitConfig: getRateLimitConfig()
  });
});

app.get("/api/admin/licenses", authenticateAdmin, (_req, res) => {
  syncExpiredLicenses();
  const rows = db
    .prepare(
      `SELECT id, code, plan, status, customer_email, duration_months, redeemed_at, expires_at, disabled_at, disabled_reason, reset_at, created_at
       FROM licenses
       ORDER BY created_at DESC`
    )
    .all();
  res.json(rows.map(sanitizeLicense));
});

app.get("/api/admin/licenses/:id", authenticateAdmin, (req, res) => {
  syncExpiredLicenses();
  const license = db.prepare("SELECT * FROM licenses WHERE id = ?").get(req.params.id);
  if (!license) {
    return res.status(404).json({ error: "License not found" });
  }

  return res.json({
    license: licenseView(license),
    sessions: listLicenseSessions(license.id),
    recentUsage: listLicenseUsage(license.id)
  });
});

app.post("/api/admin/licenses", authenticateAdmin, (req, res) => {
  const quantity = Math.max(1, Math.min(Number(req.body?.quantity || 1), 500));
  const plan = normalizePlan(req.body?.plan);
  const durationMonths = Math.max(1, Math.min(Number(req.body?.durationMonths || 1), 36));
  const createdBy = req.admin.username;
  const created = [];

  const insert = db.prepare(
    `INSERT INTO licenses (
      id, code, plan, status, quota_used, duration_months, reset_at, created_at, updated_at, created_by
    ) VALUES (?, ?, ?, 'new', 0, ?, ?, ?, ?, ?)`
  );

  const tx = db.transaction(() => {
    for (let i = 0; i < quantity; i += 1) {
      const id = randomId("lic");
      const code = createLicenseCode();
      const timestamp = nowIso();
      const resetAt = timestamp;
      insert.run(id, code, plan, durationMonths, resetAt, timestamp, timestamp, createdBy);
      created.push({ id, code, plan, durationMonths, resetAt });
    }
  });

  tx();
  res.status(201).json(created);
});

app.patch("/api/admin/licenses/:id", authenticateAdmin, (req, res) => {
  const existing = db.prepare("SELECT * FROM licenses WHERE id = ?").get(req.params.id);
  if (!existing) {
    return res.status(404).json({ error: "License not found" });
  }

  const updates = [];
  const values = [];
  if (req.body?.plan) {
    updates.push("plan = ?");
    values.push(normalizePlan(req.body.plan));
  }
  if (req.body?.durationMonths !== undefined) {
    const durationMonths = Math.max(1, Math.min(Number(req.body.durationMonths || 1), 36));
    updates.push("duration_months = ?");
    values.push(durationMonths);
  }
  if (req.body?.customerEmail !== undefined) {
    updates.push("customer_email = ?");
    values.push(req.body.customerEmail || null);
  }
  if (req.body?.expiresAt !== undefined) {
    updates.push("expires_at = ?");
    values.push(req.body.expiresAt || null);
  }

  if (updates.length === 0) {
    return res.json(licenseView(existing));
  }

  updates.push("updated_at = ?");
  values.push(nowIso(), req.params.id);
  db.prepare(`UPDATE licenses SET ${updates.join(", ")} WHERE id = ?`).run(...values);

  const updated = db.prepare("SELECT * FROM licenses WHERE id = ?").get(req.params.id);
  return res.json(licenseView(updated));
});

app.post("/api/admin/licenses/:id/extend", authenticateAdmin, (req, res) => {
  const license = db.prepare("SELECT * FROM licenses WHERE id = ?").get(req.params.id);
  if (!license) {
    return res.status(404).json({ error: "License not found" });
  }

  const months = Math.max(1, Math.min(Number(req.body?.months || 1), 36));
  const base = license.expires_at && new Date(license.expires_at) > new Date() ? license.expires_at : nowIso();
  const expiresAt = addMonthsIso(base, months);
  db.prepare(
    `UPDATE licenses
     SET expires_at = ?,
         status = CASE
           WHEN status = 'expired' THEN 'active'
           ELSE status
         END,
         updated_at = ?
     WHERE id = ?`
  ).run(expiresAt, nowIso(), req.params.id);

  const updated = db.prepare("SELECT * FROM licenses WHERE id = ?").get(req.params.id);
  return res.json(licenseView(updated));
});

app.post("/api/admin/licenses/:id/disable", authenticateAdmin, (req, res) => {
  const id = req.params.id;
  const reason = req.body?.reason || "Disabled by admin";
  const timestamp = nowIso();

  db.prepare(
    `UPDATE licenses
     SET status = 'disabled',
         disabled_at = ?,
         disabled_reason = ?,
         updated_at = ?
     WHERE id = ?`
  ).run(timestamp, reason, timestamp, id);

  db.prepare("UPDATE sessions SET revoked_at = ? WHERE license_id = ? AND revoked_at IS NULL").run(timestamp, id);
  const updated = db.prepare("SELECT * FROM licenses WHERE id = ?").get(id);
  res.json(sanitizeLicense(updated));
});

app.post("/api/admin/licenses/:id/enable", authenticateAdmin, (req, res) => {
  const id = req.params.id;
  const license = db.prepare("SELECT * FROM licenses WHERE id = ?").get(id);
  if (!license) {
    return res.status(404).json({ error: "License not found" });
  }

  const status = license.expires_at && new Date(license.expires_at) <= new Date() ? "expired" : license.redeemed_at ? "active" : "new";
  db.prepare(
    `UPDATE licenses
     SET status = ?,
         disabled_at = NULL,
         disabled_reason = NULL,
         updated_at = ?
     WHERE id = ?`
  ).run(status, nowIso(), id);

  const updated = db.prepare("SELECT * FROM licenses WHERE id = ?").get(id);
  res.json(sanitizeLicense(updated));
});

app.post("/api/admin/licenses/:id/reset-device", authenticateAdmin, (req, res) => {
  const id = req.params.id;
  const license = db.prepare("SELECT * FROM licenses WHERE id = ?").get(id);
  if (!license) {
    return res.status(404).json({ error: "License not found" });
  }

  db.prepare(
    `UPDATE licenses
     SET device_id = NULL,
         updated_at = ?
     WHERE id = ?`
  ).run(nowIso(), id);

  const updated = db.prepare("SELECT * FROM licenses WHERE id = ?").get(id);
  res.json(licenseView(updated));
});

app.post("/api/admin/licenses/:id/revoke-sessions", authenticateAdmin, (req, res) => {
  const id = req.params.id;
  const timestamp = nowIso();
  db.prepare("UPDATE sessions SET revoked_at = ? WHERE license_id = ? AND revoked_at IS NULL").run(timestamp, id);
  res.json({
    ok: true,
    revokedAt: timestamp,
    sessions: listLicenseSessions(id)
  });
});

app.post("/api/admin/provider-keys", authenticateAdmin, (req, res) => {
  const { provider = "openai", name, apiKey } = req.body ?? {};

  if (!name || !apiKey) {
    return res.status(400).json({ error: "name and apiKey are required" });
  }

  const timestamp = nowIso();

  db.prepare("UPDATE provider_keys SET active = 0, updated_at = ?").run(timestamp);

  const id = randomId("pk");
  db.prepare(
    `INSERT INTO provider_keys (
      id, provider, name, encrypted_key, active, created_at, updated_at
    ) VALUES (?, ?, ?, ?, 1, ?, ?)`
  ).run(id, provider, name, encryptSecret(apiKey), timestamp, timestamp);

  res.status(201).json({ id, provider, name, active: true, updatedAt: timestamp });
});

app.get("/api/admin/provider-keys", authenticateAdmin, (_req, res) => {
  const rows = db
    .prepare(
      `SELECT id, provider, name, active, created_at, updated_at
       FROM provider_keys
       ORDER BY updated_at DESC`
    )
    .all();
  res.json(rows.map(providerKeyView));
});

app.post("/api/admin/provider-keys/:id/activate", authenticateAdmin, (req, res) => {
  const existing = db.prepare("SELECT * FROM provider_keys WHERE id = ?").get(req.params.id);
  if (!existing) {
    return res.status(404).json({ error: "Provider key not found" });
  }

  const timestamp = nowIso();
  db.prepare("UPDATE provider_keys SET active = 0, updated_at = ?").run(timestamp);
  db.prepare("UPDATE provider_keys SET active = 1, updated_at = ? WHERE id = ?").run(timestamp, req.params.id);
  const updated = db.prepare("SELECT * FROM provider_keys WHERE id = ?").get(req.params.id);
  res.json(providerKeyView(updated));
});

app.delete("/api/admin/provider-keys/:id", authenticateAdmin, (req, res) => {
  const existing = db.prepare("SELECT * FROM provider_keys WHERE id = ?").get(req.params.id);
  if (!existing) {
    return res.status(404).json({ error: "Provider key not found" });
  }

  db.prepare("DELETE FROM provider_keys WHERE id = ?").run(req.params.id);
  res.status(204).end();
});

app.get("/api/admin/sessions", authenticateAdmin, (_req, res) => {
  syncExpiredLicenses();
  const rows = db
    .prepare(
      `SELECT
         sessions.id,
         sessions.license_id,
         sessions.device_id,
         sessions.created_at,
         sessions.revoked_at,
         licenses.code,
         licenses.plan,
         licenses.status,
         licenses.customer_email
       FROM sessions
       JOIN licenses ON licenses.id = sessions.license_id
       ORDER BY sessions.created_at DESC`
    )
    .all();
  res.json(rows.map(sessionView));
});

app.post("/api/admin/sessions/:id/revoke", authenticateAdmin, (req, res) => {
  const session = db.prepare("SELECT * FROM sessions WHERE id = ?").get(req.params.id);
  if (!session) {
    return res.status(404).json({ error: "Session not found" });
  }

  const timestamp = nowIso();
  db.prepare("UPDATE sessions SET revoked_at = ? WHERE id = ?").run(timestamp, req.params.id);
  const updated = db
    .prepare(
      `SELECT
         sessions.id,
         sessions.license_id,
         sessions.device_id,
         sessions.created_at,
         sessions.revoked_at,
         licenses.code,
         licenses.plan,
         licenses.status,
         licenses.customer_email
       FROM sessions
       JOIN licenses ON licenses.id = sessions.license_id
       WHERE sessions.id = ?`
    )
    .get(req.params.id);
  res.json(sessionView(updated));
});

app.get("/api/admin/rate-limits", authenticateAdmin, (_req, res) => {
  res.json(getRateLimitConfig());
});

app.put("/api/admin/rate-limits", authenticateAdmin, (req, res) => {
  const config = req.body;
  if (!config?.planProfiles) {
    return res.status(400).json({ error: "Invalid rate limit config" });
  }

  res.json(saveRateLimitConfig(config));
});

app.post("/api/auth/redeem", (req, res) => {
  syncExpiredLicenses();
  const { code, email, deviceId } = req.body ?? {};
  if (!code || !email) {
    return res.status(400).json({ error: "code and email are required" });
  }

  const license = db.prepare("SELECT * FROM licenses WHERE code = ?").get(code);
  if (!license) {
    return res.status(404).json({ error: "License not found" });
  }
  if (license.redeemed_at) {
    return res.status(409).json({ error: "License already redeemed" });
  }

  const timestamp = nowIso();
  const expiresAt = addMonthsIso(timestamp, license.duration_months || 1);
  db.prepare(
    `UPDATE licenses
     SET status = 'active',
         customer_email = ?,
         device_id = ?,
         redeemed_at = ?,
         expires_at = ?,
         updated_at = ?
     WHERE id = ?`
  ).run(email, deviceId || null, timestamp, expiresAt, timestamp, license.id);

  const updated = db.prepare("SELECT * FROM licenses WHERE id = ?").get(license.id);
  const session = createSession(updated, deviceId);
  res.json(sessionPayload(updated, session));
});

app.post("/api/auth/login", (req, res) => {
  syncExpiredLicenses();
  const { code, deviceId } = req.body ?? {};
  const license = db.prepare("SELECT * FROM licenses WHERE code = ?").get(code);

  if (!license || !license.redeemed_at) {
    return res.status(401).json({ error: "License is not active" });
  }
  if (license.status !== "active") {
    return res.status(403).json({ error: `License is ${license.status}` });
  }

  if (license.device_id && deviceId && license.device_id !== deviceId) {
    return res.status(403).json({ error: "License is already bound to another device" });
  }

  const refreshed = ensureQuotaWindow(license);
  const session = createSession(refreshed, deviceId || refreshed.device_id);
  res.json(sessionPayload(refreshed, session));
});

app.get("/api/client/me", authenticateSession, (req, res) => {
  syncExpiredLicenses();
  if (!assertLicenseUsable(req.license, res)) {
    return;
  }
  const license = ensureQuotaWindow(req.license);
  res.json(licenseView(license));
});

app.post("/api/client/consume", authenticateSession, (req, res) => {
  syncExpiredLicenses();
  if (!assertLicenseUsable(req.license, res)) {
    return;
  }
  const units = Math.max(1, Number(req.body?.units || 1));
  const license = ensureQuotaWindow(req.license);
  const result = consumeQuota(license, {
    requestUnits: units,
    category: req.body?.category || "local_messages",
    model: req.body?.model || "gpt-5.4-mini"
  });
  res.json(result);
});

app.post("/api/client/refresh", authenticateSession, (req, res) => {
  syncExpiredLicenses();
  if (!assertLicenseUsable(req.license, res)) {
    return;
  }
  const license = ensureQuotaWindow(req.license);
  const providerKey = db
    .prepare(
      "SELECT id, provider, name, updated_at FROM provider_keys WHERE active = 1 ORDER BY updated_at DESC LIMIT 1"
    )
    .get();
  res.json({
    license: licenseView(license),
    providerKey: providerKey
      ? {
          id: providerKey.id,
          provider: providerKey.provider,
          name: providerKey.name,
          updatedAt: providerKey.updated_at
        }
      : null
  });
});

app.post("/api/client/logout", authenticateSession, (req, res) => {
  const timestamp = nowIso();
  db.prepare("UPDATE sessions SET revoked_at = ? WHERE id = ?").run(timestamp, req.session.id);
  res.json({ ok: true, revokedAt: timestamp });
});

app.post("/api/client/responses", authenticateSession, async (req, res) => {
  syncExpiredLicenses();
  if (!assertLicenseUsable(req.license, res)) {
    return;
  }
  const { model = "gpt-5.4-mini", input, instructions } = req.body ?? {};
  if (!input) {
    return res.status(400).json({ error: "input is required" });
  }

  const providerKey = db
    .prepare("SELECT * FROM provider_keys WHERE active = 1 ORDER BY updated_at DESC LIMIT 1")
    .get();
  if (!providerKey) {
    return res.status(503).json({ error: "No active provider key configured" });
  }

  const previewQuota = consumeQuotaPreview(req.license, model);
  if (!previewQuota.ok) {
    return res.status(429).json(previewQuota);
  }

  try {
    const upstream = await fetch("https://api.openai.com/v1/responses", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${decryptSecret(providerKey.encrypted_key)}`
      },
      body: JSON.stringify({
        model,
        input,
        instructions
      })
    });

    const body = await upstream.json();
    let quota = previewQuota;

    if (upstream.ok) {
      const usage = body?.usage || {};
      quota = consumeQuota(req.license, {
        requestUnits: 1,
        category: "local_messages",
        model,
        inputTokens: usage?.input_tokens ?? usage?.inputTokens ?? 0,
        outputTokens: usage?.output_tokens ?? usage?.outputTokens ?? 0,
        cachedInputTokens: usage?.cached_input_tokens ?? usage?.cachedInputTokens ?? 0
      });
    }

    res.status(upstream.status).json({
      quota,
      upstream: {
        id: body.id,
        model: body.model,
        output_text: body.output_text,
        error: body.error,
        raw: body
      }
    });
  } catch (error) {
    res.status(502).json({
      error: "Upstream request failed",
      detail: error instanceof Error ? error.message : String(error)
    });
  }
});

app.listen(port, () => {
  console.log(`License gateway running at http://localhost:${port}`);
});

function seedAdmin() {
  const username = process.env.ADMIN_USERNAME || "admin";
  const password = process.env.ADMIN_PASSWORD || "change-me";
  const existing = db.prepare("SELECT id FROM admins WHERE username = ?").get(username);
  if (existing) {
    return;
  }
  db.prepare(
    `INSERT INTO admins (id, username, password_hash, created_at)
     VALUES (?, ?, ?, ?)`
  ).run(randomId("adm"), username, hashPassword(password), nowIso());
}

function createSession(license, deviceId) {
  const id = randomId("ses");
  const timestamp = nowIso();
  db.prepare(
    `INSERT INTO sessions (
      id, license_id, device_id, created_at, revoked_at
    ) VALUES (?, ?, ?, ?, NULL)`
  ).run(id, license.id, deviceId || null, timestamp);
  return { id, token: createSessionToken({ sessionId: id, licenseId: license.id }) };
}

function sessionPayload(license, session) {
  return {
    sessionToken: session.token,
    license: licenseView(license)
  };
}

function licenseView(license) {
  const definition = getPlanDefinition(license.plan);
  return {
    id: license.id,
    code: license.code,
    plan: license.plan,
    status: license.status,
    customerEmail: license.customer_email,
    deviceId: license.device_id,
    durationMonths: Number(license.duration_months || 0),
    redeemedAt: license.redeemed_at,
    expiresAt: license.expires_at,
    disabledAt: license.disabled_at,
    disabledReason: license.disabled_reason,
    resetAt: license.reset_at,
    createdAt: license.created_at,
    quotaUsed: Number(license.quota_used || 0),
    quotaLimit: definition.unlimited ? null : "See rateLimits",
    quotaRemaining: definition.unlimited ? null : "See rateLimits",
    rateLimits: getLicenseRateSummary(license)
  };
}

function consumeQuotaPreview(license, model) {
  const summary = getLicenseRateSummary(license);
  const normalizedModel = model === "gpt-5-mini" ? "gpt-5.4-mini" : model;
  const local = summary.categories?.local_messages?.[normalizedModel];
  if (!local) {
    return {
      ok: false,
      error: `Model ${normalizedModel} is not enabled for local_messages on plan ${license.plan}`
    };
  }
  return local;
}

function syncExpiredLicenses() {
  const timestamp = nowIso();
  db.prepare(
    `UPDATE licenses
     SET status = 'expired',
         updated_at = ?
     WHERE expires_at IS NOT NULL
       AND expires_at <= ?
       AND status NOT IN ('expired', 'disabled')`
  ).run(timestamp, timestamp);
}

function assertLicenseUsable(license, res) {
  const fresh = db.prepare("SELECT * FROM licenses WHERE id = ?").get(license.id);
  if (!fresh) {
    res.status(401).json({ error: "License not found" });
    return false;
  }
  if (fresh.status !== "active") {
    res.status(403).json({
      error: `License is ${fresh.status}`,
      license: licenseView(fresh)
    });
    return false;
  }
  return true;
}

function providerKeyView(row) {
  if (!row) {
    return null;
  }

  return {
    id: row.id,
    provider: row.provider,
    name: row.name,
    active: Boolean(row.active),
    createdAt: row.created_at,
    updatedAt: row.updated_at
  };
}

function sessionView(row) {
  return {
    id: row.id,
    licenseId: row.license_id,
    licenseCode: row.code,
    plan: row.plan,
    licenseStatus: row.status,
    customerEmail: row.customer_email,
    deviceId: row.device_id,
    createdAt: row.created_at,
    revokedAt: row.revoked_at
  };
}

function listLicenseSessions(licenseId) {
  const rows = db
    .prepare(
      `SELECT
         sessions.id,
         sessions.license_id,
         sessions.device_id,
         sessions.created_at,
         sessions.revoked_at,
         licenses.code,
         licenses.plan,
         licenses.status,
         licenses.customer_email
       FROM sessions
       JOIN licenses ON licenses.id = sessions.license_id
       WHERE sessions.license_id = ?
       ORDER BY sessions.created_at DESC`
    )
    .all(licenseId);
  return rows.map(sessionView);
}

function listLicenseUsage(licenseId) {
  return db
    .prepare(
      `SELECT id, category, model, request_units, input_tokens, output_tokens, cached_input_tokens, created_at
       FROM usage_events
       WHERE license_id = ?
       ORDER BY created_at DESC
       LIMIT 25`
    )
    .all(licenseId)
    .map((row) => ({
      id: row.id,
      category: row.category,
      model: row.model,
      requestUnits: Number(row.request_units || 0),
      inputTokens: Number(row.input_tokens || 0),
      outputTokens: Number(row.output_tokens || 0),
      cachedInputTokens: Number(row.cached_input_tokens || 0),
      createdAt: row.created_at
    }));
}
