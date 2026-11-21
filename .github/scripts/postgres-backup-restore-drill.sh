#!/usr/bin/env bash
set -euo pipefail

: "${PGHOST:?PGHOST is required}"
: "${PGPORT:?PGPORT is required}"
: "${PGUSER:?PGUSER is required}"
: "${PGPASSWORD:?PGPASSWORD is required}"
: "${PGDATABASE:?PGDATABASE is required}"

restore_database="reqviora_restore_drill_${GITHUB_RUN_ID:-local}_${RANDOM}"
work_directory="$(mktemp -d)"
dump_file="${work_directory}/reqviora.dump"

cleanup() {
  dropdb --if-exists --force "${restore_database}" >/dev/null 2>&1 || true
  rm -f -- "${dump_file}" "${dump_file}.sha256"
  rmdir -- "${work_directory}" 2>/dev/null || true
}
trap cleanup EXIT

pg_dump --format=custom --no-owner --no-privileges --file="${dump_file}" "${PGDATABASE}"
sha256sum "${dump_file}" > "${dump_file}.sha256"
sha256sum --check "${dump_file}.sha256"

createdb "${restore_database}"
pg_restore --exit-on-error --no-owner --no-privileges --dbname="${restore_database}" "${dump_file}"

restored_tables="$(psql --dbname="${restore_database}" --tuples-only --no-align \
  --command="SELECT count(*) FROM pg_catalog.pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema');")"
if [[ ! "${restored_tables}" =~ ^[1-9][0-9]*$ ]]; then
  echo "Restore drill produced no application tables." >&2
  exit 1
fi

echo "Backup checksum verified and ${restored_tables} table(s) restored into isolated database ${restore_database}."
