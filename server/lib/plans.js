import { db } from "./db.js";
import { nowIso, randomId } from "./utils.js";

const FIVE_HOURS_MS = 5 * 60 * 60 * 1000;
const SEVEN_DAYS_MS = 7 * 24 * 60 * 60 * 1000;

const PLUS_BASELINE = {
  local_messages: {
    "gpt-5.4": 20,
    "gpt-5.4-mini": 60,
    "gpt-5.3-codex": 30
  },
  cloud_tasks: {
    "gpt-5.3-codex": 10
  },
  code_reviews: {
    "gpt-5.3-codex": 20
  }
};

const MODEL_ALIASES = {
  "gpt-5-mini": "gpt-5.4-mini",
  "gpt-5.4-mini": "gpt-5.4-mini",
  "gpt-5.4": "gpt-5.4",
  "gpt-5.3": "gpt-5.3-codex",
  "gpt-5.3-codex": "gpt-5.3-codex",
  "gpt-5.2": "gpt-5.2",
  "gpt-5.5": "gpt-5.5"
};

export function normalizePlan(input) {
  if (input === "5x" || input === "20x" || input === "unlimited") {
    return input;
  }
  return "5x";
}

export function normalizeModel(input) {
  return MODEL_ALIASES[input] || input || "gpt-5.4-mini";
}

export function getPlanDefinition(plan) {
  const config = getRateLimitConfig();
  const normalized = normalizePlan(plan);
  return config.planProfiles[normalized] || config.planProfiles["5x"];
}

export function getRateLimitConfig() {
  const row = db
    .prepare("SELECT value_json FROM settings WHERE key = 'rate_limit_config'")
    .get();

  if (row?.value_json) {
    return JSON.parse(row.value_json);
  }

  const defaults = buildDefaultRateLimitConfig();
  saveRateLimitConfig(defaults);
  return defaults;
}

export function saveRateLimitConfig(config) {
  const payload = JSON.stringify(config, null, 2);
  db.prepare(
    `INSERT INTO settings (key, value_json, updated_at)
     VALUES ('rate_limit_config', ?, ?)
     ON CONFLICT(key) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at`
  ).run(payload, nowIso());
  return config;
}

export function getLicenseRateSummary(license) {
  const plan = getPlanDefinition(license.plan);
  const categories = ["local_messages", "cloud_tasks", "code_reviews"];

  const summary = {
    plan: normalizePlan(license.plan),
    unlimited: Boolean(plan.unlimited),
    categories: {}
  };

  for (const category of categories) {
    const models = Object.keys(plan.limits?.[category] || {});
    if (models.length === 0) {
      continue;
    }

    summary.categories[category] = {};
    for (const model of models) {
      summary.categories[category][model] = getUsageWindow(license.id, license.plan, category, model);
    }
  }

  return summary;
}

export function consumeQuota(license, options = {}) {
  const category = options.category || "local_messages";
  const model = normalizeModel(options.model || "gpt-5.4-mini");
  const requestUnits = Math.max(1, Number(options.requestUnits || 1));

  const window = getUsageWindow(license.id, license.plan, category, model);
  if (!window.ok) {
    return window;
  }

  db.prepare(
    `INSERT INTO usage_events (
      id, license_id, category, model, request_units, input_tokens, output_tokens, cached_input_tokens, created_at
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`
  ).run(
    randomId("use"),
    license.id,
    category,
    model,
    requestUnits,
    Number(options.inputTokens || 0),
    Number(options.outputTokens || 0),
    Number(options.cachedInputTokens || 0),
    nowIso()
  );

  return getUsageWindow(license.id, license.plan, category, model);
}

export function ensureQuotaWindow(license) {
  return license;
}

function buildDefaultRateLimitConfig() {
  return {
    version: 1,
    source:
      "Derived from publicly visible Plus baseline ranges on chatgpt.com/codex/pricing. Pro-style 5x and 20x plans are expressed as configurable multipliers on the conservative floor values.",
    windows: {
      fiveHourWindowHours: 5,
      weeklyWindowDays: 7
    },
    planProfiles: {
      "5x": buildPlanProfile("5x", 5),
      "20x": buildPlanProfile("20x", 20),
      unlimited: {
        id: "unlimited",
        unlimited: true,
        limits: {}
      }
    }
  };
}

function buildPlanProfile(id, multiplier) {
  const limits = {};
  const weeklyLimits = {};

  for (const [category, models] of Object.entries(PLUS_BASELINE)) {
    limits[category] = {};
    weeklyLimits[category] = {};

    for (const [model, value] of Object.entries(models)) {
      limits[category][model] = value * multiplier;
      weeklyLimits[category][model] = value * multiplier * 7;
    }
  }

  return {
    id,
    unlimited: false,
    limits,
    weeklyLimits
  };
}

function getUsageWindow(licenseId, planName, category, requestedModel) {
  const plan = getPlanDefinition(planName);
  const model = normalizeModel(requestedModel);

  if (plan.unlimited) {
    return {
      ok: true,
      category,
      model,
      plan: normalizePlan(planName),
      limit: null,
      used: 0,
      remaining: null,
      weeklyLimit: null,
      weeklyUsed: 0,
      weeklyRemaining: null,
      resetAt: null
    };
  }

  const limit = plan.limits?.[category]?.[model];
  if (!Number.isFinite(limit)) {
    return {
      ok: false,
      error: `Model ${model} is not enabled for ${category} on plan ${planName}`
    };
  }

  const weeklyLimit = plan.weeklyLimits?.[category]?.[model] ?? limit * 7;

  const fiveHour = loadWindowUsage(licenseId, category, model, FIVE_HOURS_MS);
  const weekly = loadWindowUsage(licenseId, category, model, SEVEN_DAYS_MS);

  const nextFiveHour = nextResetAt(fiveHour.oldestCreatedAt, FIVE_HOURS_MS);
  const nextWeekly = nextResetAt(weekly.oldestCreatedAt, SEVEN_DAYS_MS);

  if (fiveHour.used >= limit) {
    return {
      ok: false,
      error: "5-hour rate limit exceeded",
      category,
      model,
      plan: normalizePlan(planName),
      limit,
      used: fiveHour.used,
      remaining: 0,
      weeklyLimit,
      weeklyUsed: weekly.used,
      weeklyRemaining: Math.max(0, weeklyLimit - weekly.used),
      resetAt: nextFiveHour,
      weeklyResetAt: nextWeekly
    };
  }

  if (weekly.used >= weeklyLimit) {
    return {
      ok: false,
      error: "Weekly rate limit exceeded",
      category,
      model,
      plan: normalizePlan(planName),
      limit,
      used: fiveHour.used,
      remaining: Math.max(0, limit - fiveHour.used),
      weeklyLimit,
      weeklyUsed: weekly.used,
      weeklyRemaining: 0,
      resetAt: nextFiveHour,
      weeklyResetAt: nextWeekly
    };
  }

  return {
    ok: true,
    category,
    model,
    plan: normalizePlan(planName),
    limit,
    used: fiveHour.used,
    remaining: Math.max(0, limit - fiveHour.used),
    weeklyLimit,
    weeklyUsed: weekly.used,
    weeklyRemaining: Math.max(0, weeklyLimit - weekly.used),
    resetAt: nextFiveHour,
    weeklyResetAt: nextWeekly
  };
}

function loadWindowUsage(licenseId, category, model, windowMs) {
  const cutoff = new Date(Date.now() - windowMs).toISOString();
  const categories = category === "local_messages" || category === "cloud_tasks"
    ? ["local_messages", "cloud_tasks"]
    : [category];

  const placeholders = categories.map(() => "?").join(", ");
  const row = db
    .prepare(
      `SELECT
         COALESCE(SUM(request_units), 0) AS used,
         MIN(created_at) AS oldest_created_at
       FROM usage_events
       WHERE license_id = ?
         AND model = ?
         AND category IN (${placeholders})
         AND created_at >= ?`
    )
    .get(licenseId, model, ...categories, cutoff);

  return {
    used: Number(row?.used || 0),
    oldestCreatedAt: row?.oldest_created_at || null
  };
}

function nextResetAt(oldestCreatedAt, windowMs) {
  if (!oldestCreatedAt) {
    return null;
  }
  return new Date(new Date(oldestCreatedAt).getTime() + windowMs).toISOString();
}
