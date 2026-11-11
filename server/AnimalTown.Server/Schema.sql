CREATE TABLE IF NOT EXISTS players (
 id uuid PRIMARY KEY, name varchar(16) NOT NULL UNIQUE,
 salt bytea NOT NULL, password bytea NOT NULL, state bytea NOT NULL,
 revision bigint NOT NULL DEFAULT 0, chat_restricted boolean NOT NULL DEFAULT false
);
-- The old combined payload is retained only until that row is migrated.
ALTER TABLE players ALTER COLUMN state DROP NOT NULL;
ALTER TABLE players ADD COLUMN IF NOT EXISTS town_data bytea;
ALTER TABLE players ADD COLUMN IF NOT EXISTS house_positions bytea;
ALTER TABLE players ADD COLUMN IF NOT EXISTS shop_states bytea;
ALTER TABLE players ADD COLUMN IF NOT EXISTS player_villager bytea;
ALTER TABLE players ADD COLUMN IF NOT EXISTS player_villager2 bytea;
ALTER TABLE players ADD COLUMN IF NOT EXISTS player_villager3 bytea;
ALTER TABLE players ADD COLUMN IF NOT EXISTS player_villager4 bytea;
ALTER TABLE players DROP CONSTRAINT IF EXISTS players_complete_save_set;
ALTER TABLE players ADD CONSTRAINT players_complete_save_set CHECK (
 (state IS NOT NULL AND town_data IS NULL AND house_positions IS NULL AND shop_states IS NULL AND player_villager IS NULL AND player_villager2 IS NULL AND player_villager3 IS NULL AND player_villager4 IS NULL)
 OR (state IS NULL AND town_data IS NOT NULL AND house_positions IS NOT NULL AND shop_states IS NOT NULL AND player_villager IS NOT NULL AND
  ((player_villager2 IS NULL AND player_villager3 IS NULL AND player_villager4 IS NULL) OR (player_villager2 IS NOT NULL AND player_villager3 IS NOT NULL AND player_villager4 IS NOT NULL))));
CREATE TABLE IF NOT EXISTS sessions (
 token_hash text PRIMARY KEY, player uuid NOT NULL REFERENCES players(id), expires timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS friends (
 sender uuid NOT NULL REFERENCES players(id), recipient uuid NOT NULL REFERENCES players(id),
 PRIMARY KEY(sender,recipient), CHECK(sender<>recipient)
);
CREATE TABLE IF NOT EXISTS blocks (
 blocker uuid NOT NULL REFERENCES players(id), blocked uuid NOT NULL REFERENCES players(id),
 PRIMARY KEY(blocker,blocked), CHECK(blocker<>blocked)
);
CREATE TABLE IF NOT EXISTS gates (host uuid PRIMARY KEY REFERENCES players(id), expires timestamptz NOT NULL);
CREATE TABLE IF NOT EXISTS visits (
 id uuid PRIMARY KEY, host uuid NOT NULL REFERENCES players(id), guest uuid NOT NULL REFERENCES players(id),
 expires timestamptz NOT NULL, ended boolean NOT NULL DEFAULT false, CHECK(host<>guest)
);
CREATE UNIQUE INDEX IF NOT EXISTS one_active_visit ON visits(guest) WHERE NOT ended;
CREATE TABLE IF NOT EXISTS listings (
 id uuid PRIMARY KEY, seller uuid NOT NULL REFERENCES players(id), item int NOT NULL CHECK(item>0),
 price int NOT NULL CHECK(price BETWEEN 1 AND 999999), buyer uuid REFERENCES players(id), cancelled boolean NOT NULL DEFAULT false
);
CREATE TABLE IF NOT EXISTS grants (
 id uuid PRIMARY KEY, player uuid NOT NULL REFERENCES players(id), item int NOT NULL, claimed boolean NOT NULL DEFAULT false,
 source text NOT NULL UNIQUE
);
CREATE TABLE IF NOT EXISTS operations (
 player uuid NOT NULL REFERENCES players(id), id uuid NOT NULL, fingerprint text NOT NULL, result jsonb NOT NULL,
 PRIMARY KEY(player,id)
);
CREATE TABLE IF NOT EXISTS tents (
 host uuid NOT NULL REFERENCES players(id), guest uuid NOT NULL REFERENCES players(id), x int NOT NULL, z int NOT NULL,
 removal_day int, PRIMARY KEY(host,guest)
);
CREATE TABLE IF NOT EXISTS reports (
 id uuid PRIMARY KEY, reporter uuid NOT NULL REFERENCES players(id), reported uuid NOT NULL REFERENCES players(id),
 lines jsonb NOT NULL, created timestamptz NOT NULL DEFAULT now()
);
