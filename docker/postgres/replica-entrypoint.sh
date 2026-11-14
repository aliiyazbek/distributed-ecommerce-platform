#!/bin/sh
# Starts a read-only streaming replica of the primary database (Requirement 3).
# First start (empty volume): create the "replicator" role on the primary if needed, then clone
# the primary with pg_basebackup. Every start: run Postgres in standby mode (read-only),
# continuously replaying the primary's WAL stream.
set -eu

PGDATA="${PGDATA:-/var/lib/postgresql/data}"
PRIMARY_PORT="${PRIMARY_PORT:-5432}"

if [ ! -s "$PGDATA/PG_VERSION" ]; then
    echo "replica: waiting for primary ${PRIMARY_HOST}:${PRIMARY_PORT}..."
    until pg_isready -q -h "$PRIMARY_HOST" -p "$PRIMARY_PORT" -U "$POSTGRES_USER"; do sleep 1; done

    echo "replica: ensuring replication role '${REPLICATION_USER}' exists on the primary"
    PGPASSWORD="$POSTGRES_PASSWORD" psql -h "$PRIMARY_HOST" -p "$PRIMARY_PORT" -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
        -v ON_ERROR_STOP=1 -v repl_user="$REPLICATION_USER" -v repl_pass="$REPLICATION_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE %I WITH REPLICATION LOGIN PASSWORD %L', :'repl_user', :'repl_pass')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'repl_user') \gexec
SQL

    echo "replica: cloning the primary (pg_basebackup)"
    mkdir -p "$PGDATA"
    chown postgres:postgres "$PGDATA"
    chmod 700 "$PGDATA"
    su-exec postgres env PGPASSWORD="$REPLICATION_PASSWORD" \
        pg_basebackup -h "$PRIMARY_HOST" -p "$PRIMARY_PORT" -U "$REPLICATION_USER" -D "$PGDATA" -X stream -R

    # -R already wrote standby.signal; set the connection explicitly (later lines win)
    # so the password is always present and the replica is named in pg_stat_replication.
    echo "primary_conninfo = 'host=${PRIMARY_HOST} port=${PRIMARY_PORT} user=${REPLICATION_USER} password=${REPLICATION_PASSWORD} application_name=read_replica'" \
        >> "$PGDATA/postgresql.auto.conf"
    echo "replica: clone complete"
fi

chown -R postgres:postgres "$PGDATA"
chmod 700 "$PGDATA"

echo "replica: starting in hot-standby (read-only) mode"
exec su-exec postgres postgres -c hot_standby=on
