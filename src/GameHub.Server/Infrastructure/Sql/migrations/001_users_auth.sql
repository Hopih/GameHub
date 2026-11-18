-- Обновление существующей таблицы users.
BEGIN;

CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email ON public.users (lower(email));

ALTER TABLE public.users
    ALTER COLUMN nickname TYPE varchar(120),
    ALTER COLUMN password_hash TYPE varchar(200),
    ADD COLUMN IF NOT EXISTS role varchar(120) NOT NULL DEFAULT 'player',
    ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now(),
    DROP CONSTRAINT IF EXISTS users_email_key;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'public.users'::regclass
          AND conname = 'users_role_check'
    ) THEN
        ALTER TABLE public.users
            ADD CONSTRAINT users_role_check CHECK (role IN ('player', 'admin'));
    END IF;
END $$;

COMMIT;
