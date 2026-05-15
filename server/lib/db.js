import Database from "better-sqlite3";
import path from "path";

const dataDir = process.env.DATA_DIR || process.cwd();
const dbPath = path.join(dataDir, "data.sqlite");
export const db = new Database(dbPath);

export function initDb() {
  db.pragma("journal_mode = WAL");

  db.exec(`
    CREATE TABLE IF NOT EXISTS admins (
      id TEXT PRIMARY KEY,
      username TEXT NOT NULL UNIQUE,
      password_hash TEXT NOT NULL,
      created_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS licenses (
      id TEXT PRIMARY KEY,
      code TEXT NOT NULL UNIQUE,
      plan TEXT NOT NULL,
      status TEXT NOT NULL,
      customer_email TEXT,
      device_id TEXT,
      quota_used INTEGER NOT NULL DEFAULT 0,
      duration_months INTEGER NOT NULL DEFAULT 1,
      redeemed_at TEXT,
      expires_at TEXT,
      disabled_at TEXT,
      disabled_reason TEXT,
      reset_at TEXT NOT NULL,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL,
      created_by TEXT
    );

    CREATE TABLE IF NOT EXISTS sessions (
      id TEXT PRIMARY KEY,
      license_id TEXT NOT NULL,
      device_id TEXT,
      created_at TEXT NOT NULL,
      revoked_at TEXT,
      FOREIGN KEY (license_id) REFERENCES licenses(id)
    );

    CREATE TABLE IF NOT EXISTS provider_keys (
      id TEXT PRIMARY KEY,
      provider TEXT NOT NULL,
      name TEXT NOT NULL,
      encrypted_key TEXT NOT NULL,
      active INTEGER NOT NULL DEFAULT 0,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS settings (
      key TEXT PRIMARY KEY,
      value_json TEXT NOT NULL,
      updated_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS usage_events (
      id TEXT PRIMARY KEY,
      license_id TEXT NOT NULL,
      category TEXT NOT NULL,
      model TEXT NOT NULL,
      request_units INTEGER NOT NULL DEFAULT 1,
      input_tokens INTEGER NOT NULL DEFAULT 0,
      output_tokens INTEGER NOT NULL DEFAULT 0,
      cached_input_tokens INTEGER NOT NULL DEFAULT 0,
      created_at TEXT NOT NULL,
      FOREIGN KEY (license_id) REFERENCES licenses(id)
    );
  `);

  ensureColumn("licenses", "duration_months", "INTEGER NOT NULL DEFAULT 1");
  ensureColumn("licenses", "expires_at", "TEXT");
  ensureColumn("licenses", "disabled_at", "TEXT");
  ensureColumn("licenses", "disabled_reason", "TEXT");
}

function ensureColumn(tableName, columnName, definition) {
  const columns = db.prepare(`PRAGMA table_info(${tableName})`).all();
  if (!columns.some((column) => column.name === columnName)) {
    db.exec(`ALTER TABLE ${tableName} ADD COLUMN ${columnName} ${definition}`);
  }
}
