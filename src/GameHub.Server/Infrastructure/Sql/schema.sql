
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

COMMIT;
