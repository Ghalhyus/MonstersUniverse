# Envoie PokeBank sur le VPS puis le (re)construit et le redémarre. À lancer depuis Windows :
#   deploy\publier.cmd ubuntu@IP_DU_VPS   (lanceur qui contourne le blocage des scripts PowerShell)
# Première fois : le script s'arrête après l'envoi et indique la commande d'installation à lancer sur le VPS.
# Les données (base, sauvegardes) ne sont jamais touchées : elles sont dans un volume Docker sur le serveur.
param(
    [Parameter(Mandatory = $true)] [string] $Serveur,
    [string] $Dossier = "pokebank"
)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$archive = Join-Path $env:TEMP "pokebank.tgz"
Write-Host "== Préparation de l'archive"
tar -czf $archive --exclude=bin --exclude=obj --exclude=.vs --exclude="app.db*" --exclude=backups --exclude=keys `
    Dockerfile .dockerignore docker-compose.yml Caddyfile .env.example PokeBank.sln PokeBank deploy
if ($LASTEXITCODE -ne 0) { throw "Impossible de créer l'archive." }

Write-Host "== Envoi vers $Serveur"
scp $archive "${Serveur}:pokebank.tgz"
if ($LASTEXITCODE -ne 0) { throw "Envoi impossible (adresse, utilisateur ou connexion SSH ?)." }

Write-Host "== Mise à jour sur le serveur"
$commande = "mkdir -p $Dossier && tar -xzf pokebank.tgz -C $Dossier && cd $Dossier && " +
    "if [ -f .env ]; then docker compose up -d --build && docker image prune -f > /dev/null && echo 'PokeBank est à jour.'; " +
    "else echo 'Première installation : connecte-toi au VPS et lance  bash ~/$Dossier/deploy/installer-serveur.sh'; fi"
ssh $Serveur $commande
if ($LASTEXITCODE -ne 0) { throw "La mise à jour sur le serveur a échoué (voir les messages ci-dessus)." }
