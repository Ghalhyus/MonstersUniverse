#!/usr/bin/env bash
# Sauvegarde des données de Monsuta Potaru (base SQLite, copies des sauvegardes de jeu, clés de connexion).
# Lancée chaque nuit par cron (voir installer-serveur.sh) ; peut aussi être lancée à la main :
#   bash ~/monsuta-potaru/deploy/sauvegarder.sh
# Le site est arrêté quelques secondes pendant la copie, pour que la base soit copiée dans un état cohérent.
set -euo pipefail
cd "$(dirname "$0")/.."

DEST="${1:-$HOME/sauvegardes}"
KEEP_DAYS=14
mkdir -p "$DEST"

docker compose stop monsuta-potaru
trap 'docker compose start monsuta-potaru' EXIT # redémarre le site même si la copie échoue

docker run --rm -v monsuta-potaru_monsuta-potaru-data:/data:ro -v "$DEST":/backup alpine \
    tar czf "/backup/monsuta-potaru-$(date +%F-%H%M).tgz" -C /data .

# (anciennes archives « pokebank-* » d'avant le changement de nom : supprimées au même rythme)
find "$DEST" \( -name 'monsuta-potaru-*.tgz' -o -name 'pokebank-*.tgz' \) -mtime +"$KEEP_DAYS" -delete
echo "$(date '+%F %T') sauvegarde terminée dans $DEST"
