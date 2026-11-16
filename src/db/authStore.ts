/**
 * SQLite-backed local user accounts and session tokens.
 */

import type { Database } from 'bun:sqlite';
import crypto from 'node:crypto';

export interface AuthUser {
  id: string;
  name: string;
  email: string;
}

export class AuthStore {
  constructor(private readonly db: Database) {}

  private hashPassword(password: string): string {
    const salt = crypto.randomBytes(16).toString('hex');
    const derived = crypto.scryptSync(password, salt, 64).toString('hex');
    return `${salt}:${derived}`;
  }

  private verifyPassword(password: string, stored: string): boolean {
    const [salt, hex] = stored.split(':');
    if (!salt || !hex) return false;
    const derived = crypto.scryptSync(password, salt, 64);
    const expected = Buffer.from(hex, 'hex');
    return derived.length === expected.length && crypto.timingSafeEqual(derived, expected);
  }

  public signUpUser(input: { name?: string; email: string; password: string }): {
    user: AuthUser;
    token: string;
  } {
    const email = input.email.trim().toLowerCase();
    const name = (input.name || email.split('@')[0] || 'User').trim().slice(0, 80);
    if (!email || !email.includes('@')) throw new Error('Please enter a valid email address.');
    if (!input.password || input.password.length < 6) throw new Error('Password must be at least 6 characters.');

    const existing = this.db.query<any, [string]>(`SELECT user_id FROM users WHERE email = ? LIMIT 1`).get(email);
    if (existing) throw new Error('An account with this email already exists.');

    const userId = `usr-${crypto.randomUUID().slice(0, 12)}`;
    const now = new Date().toISOString();
    this.db.run(
      `INSERT INTO users (user_id, name, email, password_hash, created_at) VALUES (?, ?, ?, ?, ?)`,
      [userId, name, email, this.hashPassword(input.password), now]
    );
    const token = crypto.randomBytes(32).toString('hex');
    const expiresAt = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString();
    this.db.run(
      `INSERT INTO user_sessions (token, user_id, created_at, expires_at) VALUES (?, ?, ?, ?)`,
      [token, userId, now, expiresAt]
    );
    return { user: { id: userId, name, email }, token };
  }

  public signInUser(input: { email: string; password: string }): {
    user: AuthUser;
    token: string;
  } {
    const email = input.email.trim().toLowerCase();
    const row = this.db.query<any, [string]>(
      `SELECT user_id, name, email, password_hash FROM users WHERE email = ? LIMIT 1`
    ).get(email);
    if (!row || !this.verifyPassword(input.password || '', row.password_hash)) {
      throw new Error('Invalid email or password.');
    }
    const token = crypto.randomBytes(32).toString('hex');
    const now = new Date().toISOString();
    const expiresAt = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString();
    this.db.run(
      `INSERT INTO user_sessions (token, user_id, created_at, expires_at) VALUES (?, ?, ?, ?)`,
      [token, row.user_id, now, expiresAt]
    );
    return { user: { id: row.user_id, name: row.name, email: row.email }, token };
  }

  public getUserBySessionToken(token: string | undefined): AuthUser | null {
    if (!token) return null;
    const row = this.db.query<any, [string, string]>(
      `SELECT u.user_id, u.name, u.email
       FROM user_sessions s
       JOIN users u ON u.user_id = s.user_id
       WHERE s.token = ? AND s.expires_at > ?
       LIMIT 1`
    ).get(token, new Date().toISOString());
    if (!row) return null;
    return { id: row.user_id, name: row.name, email: row.email };
  }

  public signOutUserSession(token: string | undefined): void {
    if (!token) return;
    this.db.run(`DELETE FROM user_sessions WHERE token = ?`, [token]);
  }
}
