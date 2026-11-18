CREATE TABLE IF NOT EXISTS public.users (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    nickname varchar(120) NOT NULL UNIQUE CHECK (length(btrim(nickname)) > 0),
    email varchar(120) NOT NULL UNIQUE CHECK (length(btrim(email)) > 0),
    password_hash varchar(200) NOT NULL,
    role varchar(120) NOT NULL DEFAULT 'player' CHECK (role IN ('player', 'admin')),
    created_at timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE public.users alter column nickname var
ALTER TABLE public.users ALTER COLUMN password_hash TYPE varchar(200);
ALTER TABLE public.users ADD COLUMN IF NOT EXISTS role       varchar(20) NOT NULL DEFAULT 'player';
ALTER TABLE public.users ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'users_role_check') THEN
ALTER TABLE public.users ADD CONSTRAINT users_role_check CHECK (role IN ('player', 'admin'));
END IF;
END $$;

DO $$
BEGIN
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email ON public.users (lower(email));
EXCEPTION WHEN unique_violation THEN
    RAISE WARNING 'Индекс ux_users_email не создан: в таблице users есть повторяющиеся email';
END $$;