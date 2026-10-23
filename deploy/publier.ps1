# Envoie Monsuta Potaru sur le VPS puis le (re)construit et le redémarre. À lancer depuis Windows :
#   deploy\publier.cmd ubuntu@IP_DU_VPS   (lanceur qui contourne le blocage des scripts PowerShell)
# Première fois : le script s'arrête après l'envoi et indique la commande d'installation à lancer sur le VPS.
# Les données (base, sauvegardes) ne sont jamais touchées : elles sont dans un volume Docker sur le serveur.
param(
    [Parameter(Mandatory = $true)] [string] $Serveur,
    [string] $Dossier = "monsuta-potaru"
)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$archive = Join-Path $env:TEMP "monsuta-potaru.tgz"
Write-Host "== Préparation de l'archive"
# Sans les images : elles sont dans ~/monsuta-potaru-images sur le serveur (deploy\images-serveur.cmd). Avec outils/, pour
# que le script des images soit sur le serveur.
tar -czf $archive --exclude=bin --exclude=obj --exclude=.vs --exclude="app.db*" --exclude=backups --exclude=keys `
    --exclude=MonsutaPotaru/wwwroot/images Dockerfile .dockerignore docker-compose.yml Caddyfile .env.example MonsutaPotaru.sln MonsutaPotaru deploy outils
if ($LASTEXITCODE -ne 0) { throw "Impossible de créer l'archive." }

Write-Host "== Envoi vers $Serveur"
scp $archive "${Serveur}:monsuta-potaru.tgz"
if ($LASTEXITCODE -ne 0) { throw "Envoi impossible (adresse, utilisateur ou connexion SSH ?)." }

Write-Host "== Mise à jour sur le serveur"
# Dossier des images créé avant Docker : sinon Docker le crée au nom de root et le script des images ne peut plus y écrire.
$commande = "mkdir -p $Dossier ~/monsuta-potaru-images && rm -rf $Dossier/MonsutaPotaru/wwwroot/images && tar -xzf monsuta-potaru.tgz -C $Dossier && cd $Dossier && " +
    "if [ -f .env ]; then docker compose up -d --build && docker image prune -f > /dev/null && echo 'Monsuta Potaru est à jour.'; " +
    "else echo 'Première installation : connecte-toi au VPS et lance  bash ~/$Dossier/deploy/installer-serveur.sh'; fi"
ssh $Serveur $commande
if ($LASTEXITCODE -ne 0) { throw "La mise à jour sur le serveur a échoué (voir les messages ci-dessus)." }
