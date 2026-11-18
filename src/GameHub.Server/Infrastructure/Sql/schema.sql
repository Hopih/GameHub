
BEGIN;

CREATE TABLE IF NOT EXISTS public.users (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    nickname varchar(120) NOT NULL UNIQUE CHECK (length(btrim(nickname)) > 0),
    email varchar(120) NOT NULL CHECK (length(btrim(email)) > 0),
    password_hash varchar(200) NOT NULL,
    role varchar(120) NOT NULL DEFAULT 'player' CHECK (role IN ('player', 'admin')),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email ON public.users (lower(email));

CREATE TABLE IF NOT EXISTS public.refresh_tokens (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id       uuid NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    family_id     uuid NOT NULL,              -- цепочка токенов одного входа (login)
    token_hash    char(64) NOT NULL UNIQUE,   -- SHA-256 в hex
    created_at    timestamptz NOT NULL DEFAULT now(),
    expires_at    timestamptz NOT NULL,
    revoked_at    timestamptz,                -- NULL = токен активен
    revoke_reason varchar(30),                -- rotated | logout | reuse
    replaced_by   uuid REFERENCES public.refresh_tokens(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user   ON public.refresh_tokens (user_id);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_family ON public.refresh_tokens (family_id);

CREATE TABLE IF NOT EXISTS public.user_favorites (
    user_id uuid NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    rawg_game_id integer NOT NULL CHECK (rawg_game_id > 0),
    game jsonb NOT NULL,
    added_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, rawg_game_id)
);

COMMIT;
