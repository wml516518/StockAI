#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUTPUT="${1:-$ROOT_DIR/dist/stockai-release.tar.gz}"

mkdir -p "$(dirname "$OUTPUT")"
OUTPUT="$(cd "$(dirname "$OUTPUT")" && pwd)/$(basename "$OUTPUT")"

for required in \
    docker-compose.yml \
    frontend/package-lock.json \
    python-data-service/config.example.ini \
    src/StockAnalyse.Api/StockAnalyse.Api.csproj; do
    [[ -f "$ROOT_DIR/$required" ]] || {
        echo "缺少发布文件: $required" >&2
        exit 1
    }
done

TAR_METADATA_FLAGS=()
if tar --version 2>/dev/null | grep -q 'bsdtar'; then
    TAR_METADATA_FLAGS=(--no-xattrs --no-mac-metadata --no-acls)
fi

COPYFILE_DISABLE=1 tar \
    "${TAR_METADATA_FLAGS[@]}" \
    --exclude='./.git' \
    --exclude='./.env' \
    --exclude='./.env.local' \
    --exclude='./.env.*.local' \
    --exclude='*/.env' \
    --exclude='*/.env.local' \
    --exclude='*/.env.*.local' \
    --exclude='./.DS_Store' \
    --exclude='./dist' \
    --exclude='./backups' \
    --exclude='./data' \
    --exclude='./logs' \
    --exclude='./**/bin' \
    --exclude='./**/obj' \
    --exclude='./**/node_modules' \
    --exclude='./**/dist' \
    --exclude='*/bin' \
    --exclude='*/obj' \
    --exclude='*/node_modules' \
    --exclude='*/dist' \
    --exclude='./**/__pycache__' \
    --exclude='./**/*.pyc' \
    --exclude='./**/*.db' \
    --exclude='./**/*.db-shm' \
    --exclude='./**/*.db-wal' \
    --exclude='./**/*.log' \
    --exclude='./NuGet.config' \
    --exclude='./python-data-service/config.ini' \
    -czf "$OUTPUT" \
    -C "$ROOT_DIR" .

if tar -tzf "$OUTPUT" | grep -Eq '(^|/)\.env$|(^|/)config\.ini$'; then
    rm -f "$OUTPUT"
    echo "发布包包含敏感配置，已删除。请检查排除规则。" >&2
    exit 1
fi

for required in \
    docker-compose.yml \
    deploy/server/deploy-stockai.sh \
    frontend/package-lock.json \
    python-data-service/config.example.ini \
    src/StockAnalyse.Api/StockAnalyse.Api.csproj; do
    if ! tar -tzf "$OUTPUT" | grep -Eq "(^|/)${required//\//\\/}$"; then
        rm -f "$OUTPUT"
        echo "发布包缺少: $required" >&2
        exit 1
    fi
done

if command -v sha256sum >/dev/null 2>&1; then
    (cd "$(dirname "$OUTPUT")" && sha256sum "$(basename "$OUTPUT")") \
        > "$OUTPUT.sha256"
else
    (cd "$(dirname "$OUTPUT")" && shasum -a 256 "$(basename "$OUTPUT")") \
        > "$OUTPUT.sha256"
fi
echo "发布包: $OUTPUT"
echo "校验文件: $OUTPUT.sha256"
