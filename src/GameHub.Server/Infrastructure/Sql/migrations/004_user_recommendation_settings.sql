BEGIN;

CREATE TABLE IF NOT EXISTS public.user_recommendation_settings (
    user_id uuid PRIMARY KEY REFERENCES public.users(id) ON DELETE CASCADE,
    by_description boolean NOT NULL,
    by_personal boolean NOT NULL
);

COMMIT;
