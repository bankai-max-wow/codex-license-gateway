import bcrypt from "bcryptjs";
import jwt from "jsonwebtoken";
import { db } from "./db.js";

const jwtSecret = process.env.JWT_SECRET || "change-me";

export function hashPassword(password) {
  return bcrypt.hashSync(password, 10);
}

export function verifyPassword(password, hash) {
  return bcrypt.compareSync(password, hash);
}

export function createAdminToken(payload) {
  return jwt.sign({ type: "admin", ...payload }, jwtSecret, { expiresIn: "12h" });
}

export function createSessionToken(payload) {
  return jwt.sign({ type: "session", ...payload }, jwtSecret, { expiresIn: "30d" });
}

export function authenticateAdmin(req, res, next) {
  const token = bearerToken(req);
  if (!token) {
    return res.status(401).json({ error: "Missing bearer token" });
  }

  try {
    const payload = jwt.verify(token, jwtSecret);
    if (payload.type !== "admin") {
      return res.status(403).json({ error: "Invalid token type" });
    }
    req.admin = payload;
    return next();
  } catch {
    return res.status(401).json({ error: "Invalid token" });
  }
}

export function authenticateSession(req, res, next) {
  const token = bearerToken(req);
  if (!token) {
    return res.status(401).json({ error: "Missing bearer token" });
  }

  try {
    const payload = jwt.verify(token, jwtSecret);
    if (payload.type !== "session") {
      return res.status(403).json({ error: "Invalid token type" });
    }

    const session = db
      .prepare("SELECT * FROM sessions WHERE id = ? AND revoked_at IS NULL")
      .get(payload.sessionId);
    if (!session) {
      return res.status(401).json({ error: "Session not found" });
    }

    const license = db
      .prepare("SELECT * FROM licenses WHERE id = ?")
      .get(payload.licenseId);
    if (!license) {
      return res.status(401).json({ error: "License not found" });
    }

    req.session = session;
    req.license = license;
    return next();
  } catch {
    return res.status(401).json({ error: "Invalid token" });
  }
}

function bearerToken(req) {
  const value = req.headers.authorization || "";
  if (!value.startsWith("Bearer ")) {
    return null;
  }
  return value.slice("Bearer ".length);
}
