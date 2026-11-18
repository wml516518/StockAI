#!/bin/sh
set -eu

: "${APP_AUTH_USERNAME:?APP_AUTH_USERNAME is required}"
: "${APP_AUTH_PASSWORD:?APP_AUTH_PASSWORD is required}"

clean_username=$(printf '%s' "$APP_AUTH_USERNAME" | tr -d '\r\n')
clean_password=$(printf '%s' "$APP_AUTH_PASSWORD" | tr -d '\r\n')
if [ "$clean_username" != "$APP_AUTH_USERNAME" ] \
    || [ "$clean_password" != "$APP_AUTH_PASSWORD" ] \
    || printf '%s' "$APP_AUTH_USERNAME" | grep -q ':'; then
    echo "APP_AUTH credentials must not contain line breaks; the username must not contain ':'." >&2
    exit 1
fi

umask 077
mkdir -p /etc/nginx/auth
htpasswd -bc /etc/nginx/auth/.htpasswd "$APP_AUTH_USERNAME" "$APP_AUTH_PASSWORD" >/dev/null
chown -R nginx:nginx /etc/nginx/auth
chmod 750 /etc/nginx/auth
chmod 600 /etc/nginx/auth/.htpasswd

exec nginx -g 'daemon off;'
