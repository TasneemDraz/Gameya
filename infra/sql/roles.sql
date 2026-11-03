-- Idempotent role/schema setup. Run as a superuser with psql variables:
--   psql -v migrator_password=... -v app_password=... -f roles.sql
-- psql does not interpolate variables inside DO $$ bodies, so roles are created with \gexec instead.

SELECT 'CREATE ROLE gameya_migrator LOGIN'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'gameya_migrator')
\gexec

SELECT 'CREATE ROLE gameya_app LOGIN'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'gameya_app')
\gexec

ALTER ROLE gameya_migrator PASSWORD :'migrator_password';
ALTER ROLE gameya_app PASSWORD :'app_password';

GRANT CREATE, USAGE ON SCHEMA public TO gameya_migrator;
GRANT USAGE ON SCHEMA public TO gameya_app;

CREATE SCHEMA IF NOT EXISTS hangfire AUTHORIZATION gameya_app;

ALTER DEFAULT PRIVILEGES FOR ROLE gameya_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO gameya_app;
ALTER DEFAULT PRIVILEGES FOR ROLE gameya_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO gameya_app;
