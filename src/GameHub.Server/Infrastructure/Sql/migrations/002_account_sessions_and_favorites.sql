BEGIN;

CREATE TABLE IF NOT EXISTS public.refresh_tokens (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    family_id uuid NOT NULL,
    token_hash char(64) NOT NULL UNIQUE,
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz,
    revoke_reason varchar(30),
    replaced_by uuid REFERENCES public.refresh_tokens(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user ON public.refresh_tokens (user_id);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_family ON public.refresh_tokens (family_id);

CREATE TABLE IF NOT EXISTS public.user_favorites (
    user_id uuid NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    rawg_game_id integer NOT NULL CHECK (rawg_game_id > 0),
    game jsonb NOT NULL,
    added_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, rawg_game_id)
);

COMMIT;
