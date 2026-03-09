#!/bin/sh

if [ $# -eq 0 ]; then
  set -- "dotnet BabyStepsMultiplayerServer.dll"
fi

# Append flags only when the corresponding env var is set.

[ -n "${PORT:-}" ] && set -- "$@" "--port=${PORT}"
[ "${PASSWORD+x}" = "x" ] && set -- "$@" "--password=${PASSWORD}"
[ -n "${PLAYER_TRANSMIT_CUTOFF:-}" ] && set -- "$@" "--player_transmit_cutoff=${PLAYER_TRANSMIT_CUTOFF}"
[ -n "${OUTER_PLAYER_TRANSMIT_CUTOFF:-}" ] && set -- "$@" "--outer_player_transmit_cutoff=${OUTER_PLAYER_TRANSMIT_CUTOFF}"
[ -n "${STATIC_UPDATE_RATE:-}" ] && set -- "$@" "--static_update_rate=${STATIC_UPDATE_RATE}"
[ -n "${MAX_BANDWIDTH_KBPS:-}" ] && set -- "$@" "--max_bandwidth_kbps=${MAX_BANDWIDTH_KBPS}"
[ -n "${TELEMETRY_ENABLED:-}" ] && set -- "$@" "--telemetry_enabled=${TELEMETRY_ENABLED}"
[ -n "${TELEMETRY_UPDATE_INTERVAL:-}" ] && set -- "$@" "--telemetry_update_interval=${TELEMETRY_UPDATE_INTERVAL}"
[ -n "${VOICE_CHAT_ENABLED:-}" ] && set -- "$@" "--voice_chat_enabled=${VOICE_CHAT_ENABLED}"
[ "${DISCORD_WEBHOOK_URL+x}" = "x" ] && set -- "$@" "--discord_webhook_url=${DISCORD_WEBHOOK_URL}"
[ -n "${DISCORD_WEBHOOK_ENABLED:-}" ] && set -- "$@" "--discord_webhook_enabled=${DISCORD_WEBHOOK_ENABLED}"

exec "$@"