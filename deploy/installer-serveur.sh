#!/usr/bin/env bash
# Première installation de Monsuta Potaru sur un VPS Ubuntu / Debian vide.
# À lancer une seule fois, sur le VPS, après le premier envoi :  bash ~/monsuta-potaru/deploy/installer-serveur.sh
# Le relancer ne casse rien (chaque étape vérifie ce qui existe déjà).
set -euo pipefail
cd "$(dirname "$0")/.."

echo "== 1/6 Mise à jour du système"
sudo apt-get update
sudo DEBIAN_FRONTEND=noninteractive apt-get upgrade -y
sudo apt-get install -y ufw openssl curl cron # cron : absent des images Debian minimales
sudo systemctl enable --now cron

echo "== 2/6 Pare-feu : seulement SSH, HTTP et HTTPS"
sudo ufw allow OpenSSH
sudo ufw allow 80,443/tcp
sudo ufw --force enable

echo "== 3/6 Docker"
if ! command -v docker > /dev/null; then
    # HTTPS exigé, redirections comprises : le script téléchargé est lancé avec sudo.
    curl -fsSL --proto '=https' --tlsv1.2 https://get.docker.com | sudo sh
fi
sudo usermod -aG docker "$USER" # docker sans sudo, à partir de la prochaine connexion

echo "== 4/6 Réglages (.env)"
if [ ! -f .env ]; then
    read -rp "Nom de domaine (ex. monsuta-potaru.mondomaine.fr ; vide = test en HTTP sur l'adresse IP) : " domain
    read -rp "Pseudo de ton compte, qui sera administrateur (à l'inscription) : " admin
    code=$(openssl rand -base64 18 | tr -d '/+=')
    printf 'DOMAIN=%s\nINVITE_CODE=%s\nADMIN_PSEUDO=%s\n' "$domain" "$code" "$admin" > .env
    chmod 600 .env
else
    echo ".env existe déjà : conservé."
fi

echo "== 5/6 Sauvegarde automatique chaque nuit à 4 h (gardée 14 jours dans ~/sauvegardes)"
job="0 4 * * * bash $PWD/deploy/sauvegarder.sh >> $HOME/sauvegardes.log 2>&1"
( crontab -l 2> /dev/null | grep -v 'deploy/sauvegarder.sh' || true ; echo "$job" ) | crontab -

echo "== 6/6 Construction et démarrage (quelques minutes la première fois)"
sudo docker compose up -d --build

domain=$(grep '^DOMAIN=' .env | cut -d= -f2-)
echo
echo "Monsuta Potaru est lancé."
if [ -n "$domain" ]; then
    echo "  Adresse : https://$domain  (le certificat HTTPS est obtenu à la première visite)"
else
    ip=$(curl -fsS --proto '=https' https://api.ipify.org 2> /dev/null || echo IP_DU_VPS)
    echo "  Adresse IP : $ip, en HTTP simple, sans chiffrement (test seulement : pas de HTTPS sans nom de domaine)"
fi
echo "  Code d'invitation de départ : $(grep '^INVITE_CODE=' .env | cut -d= -f2-)  (ensuite : page « Administration »)"
echo "  Déconnecte-toi puis reconnecte-toi pour utiliser « docker » sans sudo."
