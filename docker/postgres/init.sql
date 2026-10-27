CREATE TABLE IF NOT EXISTS users (
    id uuid PRIMARY KEY,
    email varchar(320) NOT NULL UNIQUE,
    password_hash text NOT NULL,
    email_verified boolean NOT NULL DEFAULT false,
    role varchar(32) NOT NULL DEFAULT 'Customer',
    created_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS customers (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    full_name text NOT NULL
);

CREATE TABLE IF NOT EXISTS admins (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    display_name text NOT NULL
);

CREATE TABLE IF NOT EXISTS email_tokens (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash text NOT NULL,
    purpose varchar(64) NOT NULL,
    expires_at timestamptz NOT NULL,
    used boolean NOT NULL DEFAULT false
);

CREATE INDEX IF NOT EXISTS ix_email_tokens_lookup
    ON email_tokens (user_id, purpose, token_hash);
