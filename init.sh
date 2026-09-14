#!/bin/sh
# Runs schema.sql then seed.sql against the SQL Server container. Idempotent:
# every CREATE in schema.sql is guarded with IF OBJECT_ID(...) IS NULL, and every
# INSERT in seed.sql is guarded with IF NOT EXISTS, so re-running this on an
# already-initialized database is a safe no-op.
set -e

echo "Waiting for SQL Server to accept connections..."
until /opt/mssql-tools18/bin/sqlcmd -C -S "$DB_HOST" -U sa -P "$SA_PASSWORD" -Q "SELECT 1" > /dev/null 2>&1; do
  sleep 2
done

echo "Applying schema..."
/opt/mssql-tools18/bin/sqlcmd -C -S "$DB_HOST" -U sa -P "$SA_PASSWORD" -i /db/schema.sql

echo "Applying seed data..."
/opt/mssql-tools18/bin/sqlcmd -C -S "$DB_HOST" -U sa -P "$SA_PASSWORD" -i /db/seed.sql

echo "Database ready."
