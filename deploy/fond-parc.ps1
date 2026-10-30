# Envoie le fond du « Parc » (MonsutaPotaru/wwwroot/images/parc/clairiere.png) dans le dossier des images du serveur,
# ~/monsuta-potaru-images. Cette carte n'est pas téléchargeable comme les autres images (outils/images.py) : elle est
# assemblée en local avec un pack de tuiles qui ne se redistribue pas (outils/clairiere.py). À lancer une fois, et
# après chaque nouvelle carte ; le site la sert aussitôt, sans redémarrage.
#   deploy\fond-parc.cmd debian@IP_DU_VPS
param(
    [Parameter(Mandatory = $true)] [string] $Serveur
)
$ErrorActionPreference = "Stop"
$carte = Join-Path $PSScriptRoot "..\MonsutaPotaru\wwwroot\images\parc\clairiere.png"
if (-not (Test-Path $carte)) {
    throw "Carte absente : $carte. La créer avec outils\clairiere.py (pack Tiny Tales), ou la récupérer d'un autre PC (outils\recuperer-images.cmd)."
}

Write-Host "== Envoi du fond du parc vers $Serveur"
ssh $Serveur "mkdir -p ~/monsuta-potaru-images/parc"
if ($LASTEXITCODE -ne 0) { throw "Connexion impossible (adresse, utilisateur ou connexion SSH ?)." }
scp $carte "${Serveur}:monsuta-potaru-images/parc/clairiere.png"
if ($LASTEXITCODE -ne 0) { throw "Envoi impossible (voir les messages ci-dessus)." }
Write-Host "Fond du parc envoyé."
