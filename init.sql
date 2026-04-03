-- ══════════════════════════════════════════════════════════════════════
--  SecureBrowser PostgreSQL Schema
--  Auto-runs on first docker-compose up
-- ══════════════════════════════════════════════════════════════════════

-- ── Users ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS users (
    id           SERIAL PRIMARY KEY,
    username     VARCHAR(100) NOT NULL UNIQUE,
    password_hash VARCHAR(64) NOT NULL,
    display_name VARCHAR(200) NOT NULL DEFAULT '',
    role         VARCHAR(20)  NOT NULL DEFAULT 'User',
    department   VARCHAR(100) NOT NULL DEFAULT '',
    is_active    BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- ── Permissions (one row per user) ────────────────────────────────────
CREATE TABLE IF NOT EXISTS permissions (
    id              SERIAL PRIMARY KEY,
    username        VARCHAR(100) NOT NULL UNIQUE REFERENCES users(username) ON DELETE CASCADE,
    allow_clipboard BOOLEAN NOT NULL DEFAULT FALSE,
    allow_print     BOOLEAN NOT NULL DEFAULT FALSE,
    ssl_only        BOOLEAN NOT NULL DEFAULT TRUE,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ── URL whitelist (many rows per user) ────────────────────────────────
CREATE TABLE IF NOT EXISTS url_whitelist (
    id       SERIAL PRIMARY KEY,
    username VARCHAR(100) NOT NULL REFERENCES users(username) ON DELETE CASCADE,
    url      VARCHAR(2000) NOT NULL,
    UNIQUE(username, url)
);

-- ── Allowed locations (many rows per user) ────────────────────────────
CREATE TABLE IF NOT EXISTS allowed_locations (
    id       SERIAL PRIMARY KEY,
    username VARCHAR(100) NOT NULL REFERENCES users(username) ON DELETE CASCADE,
    location VARCHAR(100) NOT NULL,
    UNIQUE(username, location)
);

-- ── Audit log ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS audit_log (
    id         SERIAL PRIMARY KEY,
    timestamp  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    username   VARCHAR(100) NOT NULL DEFAULT '',
    event_type VARCHAR(50)  NOT NULL DEFAULT '',
    details    TEXT         NOT NULL DEFAULT '',
    severity   VARCHAR(20)  NOT NULL DEFAULT 'Info',
    location   VARCHAR(100) NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS idx_audit_timestamp ON audit_log(timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_audit_username  ON audit_log(username);
CREATE INDEX IF NOT EXISTS idx_audit_type      ON audit_log(event_type);

-- ══════════════════════════════════════════════════════════════════════
--  Seed data — demo users and policies
--  Password hashes are SHA-256 lowercase hex
--    admin  → Admin123!  → 0f01aeb9cda62ab2f7435fbdc8e0617c47e1c0e0b1c0a9e4b1dca88e9f3a3b10
--    alice  → Alice123!
--    bob    → Bob123!
-- ══════════════════════════════════════════════════════════════════════

-- We use a function so it's idempotent
DO $$
BEGIN
    -- ── admin ─────────────────────────────────────────────────────────
    INSERT INTO users (username, password_hash, display_name, role, department)
    VALUES ('admin',
            encode(sha256(convert_to('Admin123!', 'UTF8')), 'hex'),
            'System Administrator', 'Admin', 'IT Security')
    ON CONFLICT (username) DO NOTHING;

    INSERT INTO permissions (username, allow_clipboard, allow_print, ssl_only)
    VALUES ('admin', TRUE, TRUE, FALSE)
    ON CONFLICT (username) DO NOTHING;

    INSERT INTO url_whitelist (username, url)
    VALUES ('admin', '*')
    ON CONFLICT DO NOTHING;

    INSERT INTO allowed_locations (username, location)
    VALUES ('admin', 'Office'), ('admin', 'Remote'), ('admin', 'Branch')
    ON CONFLICT DO NOTHING;

    -- ── alice ─────────────────────────────────────────────────────────
    INSERT INTO users (username, password_hash, display_name, role, department)
    VALUES ('alice',
            encode(sha256(convert_to('Alice123!', 'UTF8')), 'hex'),
            'Alice Johnson', 'User', 'Finance')
    ON CONFLICT (username) DO NOTHING;

    INSERT INTO permissions (username, allow_clipboard, allow_print, ssl_only)
    VALUES ('alice', FALSE, FALSE, TRUE)
    ON CONFLICT (username) DO NOTHING;

    INSERT INTO url_whitelist (username, url)
    VALUES ('alice', 'https://www.google.com'),
           ('alice', 'https://google.com'),
           ('alice', 'https://github.com'),
           ('alice', 'https://www.github.com')
    ON CONFLICT DO NOTHING;

    INSERT INTO allowed_locations (username, location)
    VALUES ('alice', 'Office')
    ON CONFLICT DO NOTHING;

    -- ── bob ───────────────────────────────────────────────────────────
    INSERT INTO users (username, password_hash, display_name, role, department)
    VALUES ('bob',
            encode(sha256(convert_to('Bob123!', 'UTF8')), 'hex'),
            'Bob Smith', 'User', 'Operations')
    ON CONFLICT (username) DO NOTHING;

    INSERT INTO permissions (username, allow_clipboard, allow_print, ssl_only)
    VALUES ('bob', TRUE, FALSE, TRUE)
    ON CONFLICT (username) DO NOTHING;

    INSERT INTO url_whitelist (username, url)
    VALUES ('bob', 'https://www.google.com'),
           ('bob', 'https://google.com'),
           ('bob', 'https://www.microsoft.com'),
           ('bob', 'https://microsoft.com'),
           ('bob', 'https://stackoverflow.com')
    ON CONFLICT DO NOTHING;

    INSERT INTO allowed_locations (username, location)
    VALUES ('bob', 'Office'), ('bob', 'Remote')
    ON CONFLICT DO NOTHING;
END $$;
