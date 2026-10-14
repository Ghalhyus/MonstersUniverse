#!/usr/bin/env bash
# Sauvegarde des données de PokeBank (base SQLite, copies des sauvegardes de jeu, clés de connexion).
# Lancée chaque nuit par cron (voir installer-serveur.sh) ; peut aussi être lancée à la main :
#   bash ~/pokebank/deploy/sauvegarder.sh
# Le site est arrêté quelques secondes pendant la copie, pour que la base soit copiée dans un état cohérent.
set -euo pipefail
cd "$(dirname "$0")/.."

DEST="${1:-$HOME/sauvegardes}"
KEEP_DAYS=14
mkdir -p "$DEST"

docker compose stop pokebank
trap 'docker compose start pokebank' EXIT # redémarre le site même si la copie échoue

docker run --rm -v pokebank_pokebank-data:/data:ro -v "$DEST":/backup alpine \
    tar czf "/backup/pokebank-$(date +%F-%H%M).tgz" -C /data .

find "$DEST" -name 'pokebank-*.tgz' -mtime +"$KEEP_DAYS" -delete
echo "$(date '+%F %T') sauvegarde terminée dans $DEST"
