#!/usr/bin/env bash
# Passage du serveur de « PokeBank » à « Monsuta Potaru », une seule fois : lancé depuis Windows par
# deploy\renommer-serveur.cmd, avant le premier « Run workflow » du nouveau nom.
#   ~/pokebank          -> ~/monsuta-potaru (le .env seulement : le workflow y envoie ensuite le code)
#   ~/pokebank-images   -> ~/monsuta-potaru-images (déplacé)
#   volumes pokebank_*  -> monsuta-potaru_* (copiés : base, copies de sécurité, clés de connexion, certificats HTTPS)
#   sauvegarde nocturne -> ~/monsuta-potaru/deploy/sauvegarder.sh
# L'ancien dossier et les anciens volumes restent intacts pour revenir en arrière (commandes à la fin). Le site est
# arrêté à partir d'ici jusqu'à la fin du « Run workflow ».
set -euo pipefail

OLD="$HOME/pokebank"
NEW="$HOME/monsuta-potaru"

if [ -f "$NEW/.env" ]; then
    echo "Déjà fait : $NEW/.env existe. Lance « Run workflow » si le site n'est pas encore reparti."
    exit 0
fi
if [ ! -f "$OLD/.env" ]; then
    echo "$OLD/.env introuvable : rien à renommer sur ce serveur."
    exit 1
fi

echo "== 1/6 Sauvegarde des données (comme chaque nuit, dans ~/sauvegardes)"
bash "$OLD/deploy/sauvegarder.sh"

echo "== 2/6 Arrêt de l'ancien site"
(cd "$OLD" && docker compose down)

echo "== 3/6 Copie des volumes"
# Volumes créés avec les étiquettes de docker compose : il les reconnaît comme les siens (pas d'avertissement).
copy_volume() {
    local from="pokebank_$1" to="monsuta-potaru_$2"
    if ! docker volume inspect "$from" > /dev/null 2>&1; then
        echo "  $from absent : ignoré"
        return
    fi
    docker volume create --label com.docker.compose.project=monsuta-potaru --label "com.docker.compose.volume=$2" "$to" > /dev/null
    docker run --rm -v "$from":/de:ro -v "$to":/vers alpine sh -c 'cp -a /de/. /vers/'
    local before after
    before=$(docker run --rm -v "$from":/v:ro alpine sh -c 'find /v | wc -l')
    after=$(docker run --rm -v "$to":/v:ro alpine sh -c 'find /v | wc -l')
    if [ "$before" != "$after" ]; then
        echo "  $from -> $to : $before fichiers avant, $after après. Arrêt : rien n'est supprimé, relance l'ancien site avec"
        echo "  cd ~/pokebank && docker compose up -d"
        exit 1
    fi
    echo "  $from -> $to ($after fichiers)"
}
copy_volume pokebank-data monsuta-potaru-data
copy_volume caddy-data caddy-data
copy_volume caddy-config caddy-config

echo "== 4/6 Dossier des images"
if [ -d "$HOME/pokebank-images" ] && [ ! -e "$HOME/monsuta-potaru-images" ]; then
    mv "$HOME/pokebank-images" "$HOME/monsuta-potaru-images"
    echo "  ~/pokebank-images -> ~/monsuta-potaru-images"
else
    mkdir -p "$HOME/monsuta-potaru-images"
    echo "  ~/monsuta-potaru-images prêt"
fi

echo "== 5/6 Réglages (.env)"
mkdir -p "$NEW"
cp -p "$OLD/.env" "$NEW/.env"
chmod 600 "$NEW/.env"

echo "== 6/6 Sauvegarde automatique chaque nuit à 4 h, depuis le nouveau dossier"
job="0 4 * * * bash $NEW/deploy/sauvegarder.sh >> $HOME/sauvegardes.log 2>&1"
( crontab -l 2> /dev/null | grep -v 'deploy/sauvegarder.sh' || true ; echo "$job" ) | crontab -

echo
echo "Serveur prêt. Lance maintenant « Run workflow » sur GitHub : il envoie le code dans ~/monsuta-potaru et redémarre le site."
echo
echo "Revenir en arrière (avant le ménage) :"
echo "  cd ~/monsuta-potaru && docker compose down ; mv ~/monsuta-potaru-images ~/pokebank-images ; cd ~/pokebank && docker compose up -d"
echo "  ( crontab -l | grep -v 'deploy/sauvegarder.sh' ; echo '0 4 * * * bash $OLD/deploy/sauvegarder.sh >> $HOME/sauvegardes.log 2>&1' ) | crontab -"
echo "Ménage, une fois le site vérifié (quelques jours plus tard) :"
echo "  docker volume rm pokebank_pokebank-data pokebank_caddy-data pokebank_caddy-config ; rm -rf ~/pokebank ~/pokebank.tgz ~/pokebank-images.log"
