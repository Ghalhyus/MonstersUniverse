# Passe le serveur de « PokeBank » à « Monsuta Potaru » (dossiers, volumes Docker, sauvegarde nocturne), une seule fois,
# avant le premier « Run workflow » du nouveau nom. À lancer depuis Windows :
#   deploy\renommer-serveur.cmd ubuntu@IP_DU_VPS
# Envoie deploy/renommer-serveur.sh sur le VPS et l'y lance (détail dans ce script). Le site est arrêté jusqu'à la fin du
# « Run workflow » ; l'ancien dossier et les anciens volumes restent, pour revenir en arrière.
param(
    [Parameter(Mandatory = $true)] [string] $Serveur
)
$ErrorActionPreference = "Stop"

Write-Host "== Envoi du script vers $Serveur"
scp (Join-Path $PSScriptRoot "renommer-serveur.sh") "${Serveur}:renommer-serveur.sh"
if ($LASTEXITCODE -ne 0) { throw "Envoi impossible (adresse, utilisateur ou connexion SSH ?)." }

Write-Host "== Passage au nouveau nom sur le serveur"
ssh $Serveur "bash ~/renommer-serveur.sh; code=`$?; rm -f ~/renommer-serveur.sh; exit `$code"
if ($LASTEXITCODE -ne 0) { throw "Le passage au nouveau nom a échoué (voir les messages ci-dessus)." }
