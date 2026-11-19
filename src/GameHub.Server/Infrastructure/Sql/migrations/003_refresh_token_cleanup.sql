BEGIN;

CREATE INDEX IF NOT EXISTS ix_refresh_tokens_expiry ON public.refresh_tokens (expires_at);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_replacement ON public.refresh_tokens (replaced_by) WHERE replaced_by IS NOT NULL;

COMMIT;
