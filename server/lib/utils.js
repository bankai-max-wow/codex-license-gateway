import crypto from "crypto";

const masterKey = crypto
  .createHash("sha256")
  .update(process.env.MASTER_KEY || "change-me")
  .digest();

export function randomId(prefix) {
  return `${prefix}_${crypto.randomBytes(9).toString("hex")}`;
}

export function createLicenseCode() {
  const part = () => crypto.randomBytes(3).toString("hex").toUpperCase();
  return `${part()}-${part()}-${part()}-${part()}`;
}

export function nowIso() {
  return new Date().toISOString();
}

export function sanitizeLicense(row) {
  return {
    id: row.id,
    code: row.code,
    plan: row.plan,
    status: row.status,
    durationMonths: row.duration_months,
    customerEmail: row.customer_email,
    redeemedAt: row.redeemed_at,
    expiresAt: row.expires_at,
    disabledAt: row.disabled_at,
    disabledReason: row.disabled_reason,
    resetAt: row.reset_at,
    createdAt: row.created_at
  };
}

export function encryptSecret(value) {
  const iv = crypto.randomBytes(12);
  const cipher = crypto.createCipheriv("aes-256-gcm", masterKey, iv);
  const encrypted = Buffer.concat([cipher.update(value, "utf8"), cipher.final()]);
  const tag = cipher.getAuthTag();
  return [iv, tag, encrypted].map((item) => item.toString("base64")).join(".");
}

export function decryptSecret(payload) {
  const [ivBase64, tagBase64, dataBase64] = payload.split(".");
  const iv = Buffer.from(ivBase64, "base64");
  const tag = Buffer.from(tagBase64, "base64");
  const encrypted = Buffer.from(dataBase64, "base64");
  const decipher = crypto.createDecipheriv("aes-256-gcm", masterKey, iv);
  decipher.setAuthTag(tag);
  return Buffer.concat([decipher.update(encrypted), decipher.final()]).toString("utf8");
}

export function addMonthsIso(baseIso, months) {
  const date = new Date(baseIso);
  date.setUTCMonth(date.getUTCMonth() + Number(months || 0));
  return date.toISOString();
}
