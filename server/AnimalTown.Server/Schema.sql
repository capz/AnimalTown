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
  ((player_villager2 IS NULL AND player_villager3 IS NULL AND player_villager4 IS NULL AND CASE WHEN octet_length(town_data)>=8 THEN get_byte(town_data,4) BETWEEN 1 AND 3 ELSE false END) OR (player_villager2 IS NOT NULL AND player_villager3 IS NOT NULL AND player_villager4 IS NOT NULL))));
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
ALTER TABLE gates ADD COLUMN IF NOT EXISTS generation uuid NOT NULL DEFAULT gen_random_uuid();
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
CREATE TABLE IF NOT EXISTS gate_removals (
 host uuid NOT NULL REFERENCES gates(host) ON DELETE CASCADE,
 guest uuid NOT NULL REFERENCES players(id),
 PRIMARY KEY(host,guest)
);
CREATE TABLE IF NOT EXISTS device_links (
 secret_hash text PRIMARY KEY,
 user_code_hash text NOT NULL UNIQUE,
 expires timestamptz NOT NULL,
 player uuid REFERENCES players(id)
);
CREATE INDEX IF NOT EXISTS device_links_expiry ON device_links(expires);
CREATE TABLE IF NOT EXISTS devices (
 id uuid PRIMARY KEY,
 player uuid NOT NULL REFERENCES players(id),
 town_identity text NOT NULL CHECK(town_identity ~ '^[0-9A-F]{16}$'),
 access_hash text NOT NULL UNIQUE,
 refresh_hash text NOT NULL UNIQUE,
 access_expires timestamptz NOT NULL,
 expires timestamptz NOT NULL,
 generation bigint NOT NULL DEFAULT 1 CHECK(generation>=1),
 previous_refresh_hash text,
 last_rotation uuid,
 rotation_nonce text,
 revoked_at timestamptz
);
CREATE UNIQUE INDEX IF NOT EXISTS one_active_device_per_account ON devices(player) WHERE revoked_at IS NULL;
ALTER TABLE device_links ADD COLUMN IF NOT EXISTS town_identity text;
ALTER TABLE device_links ADD COLUMN IF NOT EXISTS device uuid REFERENCES devices(id);
CREATE TABLE IF NOT EXISTS initial_town_imports (
 player uuid PRIMARY KEY REFERENCES players(id),
 town_identity text NOT NULL UNIQUE,
 fingerprint text NOT NULL,
 revision bigint NOT NULL,
 save_commit bytea NOT NULL,
 save_records bytea[] NOT NULL CHECK(cardinality(save_records)=7)
);
CREATE TABLE IF NOT EXISTS checkpoint_uploads (
 id uuid PRIMARY KEY,
 player uuid NOT NULL UNIQUE REFERENCES players(id),
 device uuid NOT NULL,
 town_identity text NOT NULL,
 fingerprint text NOT NULL,
 total_bytes integer NOT NULL CHECK(total_bytes BETWEEN 288 AND 4194304),
 received_bytes integer NOT NULL DEFAULT 0 CHECK(received_bytes BETWEEN 0 AND total_bytes),
 expires timestamptz NOT NULL,
 writer uuid,
 writer_expires timestamptz,
 revision bigint
);
ALTER TABLE checkpoint_uploads ADD COLUMN IF NOT EXISTS parent_revision bigint CHECK(parent_revision>0);
CREATE TABLE IF NOT EXISTS offline_town_checkpoints (
 player uuid PRIMARY KEY REFERENCES initial_town_imports(player),
 parent_revision bigint NOT NULL CHECK(parent_revision>0),
 revision bigint NOT NULL CHECK(revision=parent_revision+1),
 fingerprint text NOT NULL,
 save_commit bytea NOT NULL,
 save_records bytea[] NOT NULL CHECK(cardinality(save_records)=7)
);
CREATE INDEX IF NOT EXISTS checkpoint_uploads_expiry ON checkpoint_uploads(expires);
CREATE TABLE IF NOT EXISTS checkpoint_upload_chunks (
 upload uuid NOT NULL REFERENCES checkpoint_uploads(id) ON DELETE CASCADE,
 position integer NOT NULL CHECK(position >= 0),
 payload bytea NOT NULL CHECK(octet_length(payload) BETWEEN 1 AND 16384),
 PRIMARY KEY(upload,position)
);
CREATE TABLE IF NOT EXISTS guest_placements (
 host uuid NOT NULL REFERENCES players(id),
 id uuid NOT NULL,
 guest uuid NOT NULL REFERENCES players(id),
 villager text NOT NULL,
 item int NOT NULL CHECK(item>0),
 x int NOT NULL,
 z int NOT NULL,
 rotation int NOT NULL CHECK(rotation BETWEEN 0 AND 3),
 room int NOT NULL CHECK(room>=0),
 owner_slot int NOT NULL,
 version int NOT NULL CHECK(version>0),
 PRIMARY KEY(host,id),
 CHECK(host<>guest)
);
CREATE TABLE IF NOT EXISTS town_review_leases (
 host uuid PRIMARY KEY REFERENCES players(id),
 id uuid NOT NULL,
 device uuid NOT NULL,
 generation bigint NOT NULL,
 villager text NOT NULL,
 expires timestamptz NOT NULL
);
-- A lease and a monotonically increasing epoch fence every hosted worker.
-- Wire epochs are unsigned 32-bit; exhaustion fails closed rather than wraps.
CREATE SEQUENCE IF NOT EXISTS hosted_epoch AS bigint MAXVALUE 4294967295 NO CYCLE;
CREATE TABLE IF NOT EXISTS hosted_towns (
 host uuid PRIMARY KEY REFERENCES players(id), gate uuid NOT NULL,
 epoch bigint NOT NULL CHECK(epoch BETWEEN 1 AND 4294967295),
 worker uuid NOT NULL, expires timestamptz NOT NULL,
 cursor bigint NOT NULL DEFAULT 0 CHECK(cursor>=0),
 simulated_at bigint NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS hosted_actors (
 host uuid NOT NULL REFERENCES hosted_towns(host), player uuid NOT NULL REFERENCES players(id),
 slot integer NOT NULL CHECK(slot BETWEEN 0 AND 2), visit uuid NOT NULL,
 device uuid NOT NULL REFERENCES devices(id), generation bigint NOT NULL,
 connection uuid NOT NULL, room integer NOT NULL DEFAULT 0,
 joined_cursor bigint NOT NULL DEFAULT 0 CHECK(joined_cursor>=0),
 x real NOT NULL, z real NOT NULL, facing integer NOT NULL DEFAULT 0,
 expires timestamptz NOT NULL, updated timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(host,player), UNIQUE(host,slot)
);
ALTER TABLE hosted_actors ADD COLUMN IF NOT EXISTS joined_cursor bigint NOT NULL DEFAULT 0 CHECK(joined_cursor>=0);
CREATE TABLE IF NOT EXISTS hosted_events (
 host uuid NOT NULL REFERENCES hosted_towns(host), cursor bigint NOT NULL CHECK(cursor>0),
 audience uuid REFERENCES players(id), kind integer NOT NULL,
 actor uuid REFERENCES players(id), payload bytea NOT NULL CHECK(octet_length(payload) BETWEEN 1 AND 4096),
 created timestamptz NOT NULL DEFAULT clock_timestamp(), PRIMARY KEY(host,cursor)
);
CREATE TABLE IF NOT EXISTS hosted_mutes (
 player uuid NOT NULL REFERENCES players(id), muted uuid NOT NULL REFERENCES players(id),
 PRIMARY KEY(player,muted), CHECK(player<>muted)
);
CREATE TABLE IF NOT EXISTS hosted_chat_evidence (
 host uuid NOT NULL REFERENCES hosted_towns(host), cursor bigint NOT NULL,
 actor uuid NOT NULL REFERENCES players(id), text varchar(128) NOT NULL,
 created timestamptz NOT NULL DEFAULT clock_timestamp(), PRIMARY KEY(host,cursor)
);
CREATE TABLE IF NOT EXISTS online_card_heads (
 player uuid PRIMARY KEY REFERENCES initial_town_imports(player), revision bigint NOT NULL CHECK(revision>0),
 fingerprint text NOT NULL, save_commit bytea NOT NULL,
 save_records bytea[] NOT NULL CHECK(cardinality(save_records)=7)
);
CREATE TABLE IF NOT EXISTS online_travel (
 id uuid PRIMARY KEY, player uuid NOT NULL REFERENCES players(id), device uuid NOT NULL REFERENCES devices(id),
 town_identity text NOT NULL, profile integer NOT NULL CHECK(profile BETWEEN 0 AND 3),
 host uuid NOT NULL REFERENCES players(id), visit uuid NOT NULL,
 parent_revision bigint NOT NULL CHECK(parent_revision>0), fingerprint text NOT NULL,
 phase integer NOT NULL DEFAULT 0 CHECK(phase BETWEEN 0 AND 3),
 revision bigint, publication_fingerprint text, save_commit bytea,
 save_records bytea[] CHECK(save_records IS NULL OR cardinality(save_records)=7),
 created timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE UNIQUE INDEX IF NOT EXISTS one_online_travel ON online_travel(player) WHERE phase<3;
CREATE TABLE IF NOT EXISTS hosted_chat_limits (
 player uuid PRIMARY KEY REFERENCES players(id), started timestamptz NOT NULL, count integer NOT NULL
);
CREATE TABLE IF NOT EXISTS hosted_mail (
 id uuid PRIMARY KEY, host uuid NOT NULL REFERENCES players(id), sender uuid NOT NULL REFERENCES players(id),
 sender_identity text NOT NULL, recipient_identity text NOT NULL,
 recipient_profile integer NOT NULL CHECK(recipient_profile BETWEEN -1 AND 3),
 recipient_npc integer NOT NULL CHECK(recipient_npc BETWEEN -1 AND 5),
 body text NOT NULL CHECK(octet_length(body)<=4000), due bigint NOT NULL,
 delivered boolean NOT NULL DEFAULT false, replied boolean NOT NULL DEFAULT false,
 created timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE IF NOT EXISTS hosted_jobs (
 id uuid PRIMARY KEY, host uuid NOT NULL REFERENCES players(id), player uuid NOT NULL REFERENCES players(id),
 villager text NOT NULL, profile integer NOT NULL CHECK(profile BETWEEN 0 AND 3),
 requester integer NOT NULL CHECK(requester BETWEEN 0 AND 5), target integer NOT NULL CHECK(target BETWEEN -1 AND 5),
 template integer NOT NULL CHECK(template BETWEEN 0 AND 11), item integer NOT NULL,
 phase integer NOT NULL CHECK(phase BETWEEN 0 AND 7), version integer NOT NULL DEFAULT 1 CHECK(version>0),
 accepted bigint NOT NULL, due bigint NOT NULL,
 parcel_pocket integer NOT NULL DEFAULT -1 CHECK(parcel_pocket BETWEEN -1 AND 29),
 borrowers integer NOT NULL DEFAULT 0 CHECK(borrowers BETWEEN 0 AND 2),
 previous_target integer NOT NULL DEFAULT -1, reward_identity text,
 created timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE UNIQUE INDEX IF NOT EXISTS one_hosted_offer ON hosted_jobs(host,player,villager,requester) WHERE phase=0;
CREATE UNIQUE INDEX IF NOT EXISTS one_hosted_favor_resident ON hosted_jobs(host,requester) WHERE template>=5 AND phase BETWEEN 1 AND 4;
CREATE UNIQUE INDEX IF NOT EXISTS one_hosted_favor_type ON hosted_jobs(host,template) WHERE template>=5 AND phase BETWEEN 1 AND 4;
CREATE TABLE IF NOT EXISTS hosted_job_conversations (
 host uuid NOT NULL REFERENCES players(id), player uuid NOT NULL REFERENCES players(id),
 npc integer NOT NULL CHECK(npc BETWEEN 0 AND 5), expires timestamptz NOT NULL,
 PRIMARY KEY(host,player)
);
-- Online receipts never allocate from a card's local job-ID counter while
-- that card can be played offline. Their issuer namespace is disjoint.
CREATE TABLE IF NOT EXISTS hosted_identity (
 id integer PRIMARY KEY CHECK(id=1), issuer text NOT NULL UNIQUE
 CHECK(issuer ~ '^[0-9A-F]{16}$' AND issuer<>'0000000000000000')
);
CREATE SEQUENCE IF NOT EXISTS hosted_identity_counter AS bigint NO CYCLE;
