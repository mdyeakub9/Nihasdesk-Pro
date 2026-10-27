#!/bin/sh
# Creates the application role on first start of the local database (SEC-11, SEC-15).
# The app connects as nihasdesk_app: not the table owner, not superuser, cannot bypass RLS.
# Migrations run as POSTGRES_USER (the owner). Safe to run again; see infra/README.md.
set -eu

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v app_password="$APP_DB_PASSWORD" <<'SQL'
SELECT format(
  'CREATE ROLE nihasdesk_app LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS',
  :'app_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'nihasdesk_app')
\gexec

SELECT format('ALTER ROLE nihasdesk_app PASSWORD %L', :'app_password')
\gexec

SELECT format('GRANT CONNECT ON DATABASE %I TO nihasdesk_app', current_database())
\gexec
SQL
