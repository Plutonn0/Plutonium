CREATE TABLE IF NOT EXISTS accounts (
 uuid text PRIMARY KEY, username text NOT NULL, role text NOT NULL DEFAULT 'user' CHECK(role IN ('user','admin')),
 disabled boolean NOT NULL DEFAULT false, reason text NOT NULL DEFAULT '',
 first_seen timestamptz NOT NULL DEFAULT now(), last_seen timestamptz NOT NULL DEFAULT now(),
 client_seen timestamptz, launcher_version text NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS accounts_name ON accounts(lower(username));
CREATE TABLE IF NOT EXISTS sessions (
 hash text PRIMARY KEY, uuid text NOT NULL REFERENCES accounts(uuid),
 expires timestamptz NOT NULL, owner_until timestamptz
);
CREATE INDEX IF NOT EXISTS sessions_expiry ON sessions(expires);
CREATE TABLE IF NOT EXISTS owner_flows (
 hash text PRIMARY KEY REFERENCES sessions(hash) ON DELETE CASCADE, device_code text NOT NULL,
 expires timestamptz NOT NULL, next_poll timestamptz NOT NULL, interval_seconds int NOT NULL
);
CREATE TABLE IF NOT EXISTS installations (
 id text PRIMARY KEY, first_seen timestamptz NOT NULL DEFAULT now(), last_seen timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS activity_days (
 uuid text NOT NULL REFERENCES accounts(uuid), day date NOT NULL DEFAULT CURRENT_DATE, PRIMARY KEY(uuid,day)
);
CREATE TABLE IF NOT EXISTS configuration (
 id int PRIMARY KEY CHECK(id=1), value jsonb NOT NULL, revision int NOT NULL DEFAULT 1
);
INSERT INTO configuration(id,value) VALUES(1,'{"maintenance":false,"message":"The launcher is currently under maintenance.","disabledFeatures":[]}') ON CONFLICT(id) DO NOTHING;
CREATE TABLE IF NOT EXISTS appeals (
 id bigserial PRIMARY KEY, uuid text NOT NULL REFERENCES accounts(uuid), username text NOT NULL,
 explanation text NOT NULL, status text NOT NULL DEFAULT 'pending' CHECK(status IN ('pending','accepted','rejected')),
 created_at timestamptz NOT NULL DEFAULT now(), reviewed_at timestamptz, reviewed_by text
);
CREATE UNIQUE INDEX IF NOT EXISTS one_pending_appeal ON appeals(uuid) WHERE status='pending';
CREATE TABLE IF NOT EXISTS audit (
 id bigserial PRIMARY KEY, actor text NOT NULL, action text NOT NULL, target text NOT NULL,
 details jsonb NOT NULL DEFAULT '{}', created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS rate_limits (
 key text PRIMARY KEY, count int NOT NULL, expires timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS owner_devices (
 hash text PRIMARY KEY, uuid text NOT NULL REFERENCES accounts(uuid), microsoft_oid text NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(), last_used timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE sessions ADD COLUMN IF NOT EXISTS owner_device_hash text;
