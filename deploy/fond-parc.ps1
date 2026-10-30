# Envoie les planches du « Parc » (MonsutaPotaru/wwwroot/images/parc/tuiles/, copiées depuis le pack Cute Fantasy par
# outils/carte-parc.py) dans le dossier des images du serveur, ~/monsuta-potaru-images. Elles ne sont pas téléchargeables
# comme les autres images (outils/images.py) : le pack ne se redistribue pas. La carte elle-même (wwwroot/parc/*.json) est
# dans Git et part avec le site. À lancer une fois, et après chaque nouvelle carte ; le site les sert aussitôt, sans
# redémarrage.
#   deploy\fond-parc.cmd debian@IP_DU_VPS
param(
    [Parameter(Mandatory = $true)] [string] $Serveur
)
$ErrorActionPreference = "Stop"
$tuiles = Join-Path $PSScriptRoot "..\MonsutaPotaru\wwwroot\images\parc\tuiles"
$planches = @(Get-ChildItem -Path $tuiles -Filter *.png -ErrorAction SilentlyContinue)
if ($planches.Count -eq 0) {
    throw "Planches absentes : $tuiles. Les copier avec outils\carte-parc.py (carte Tiled et pack Cute Fantasy), ou les récupérer d'un autre PC (outils\recuperer-images.cmd)."
}

Write-Host "== Envoi des $($planches.Count) planches du parc vers $Serveur"
ssh $Serveur "mkdir -p ~/monsuta-potaru-images/parc/tuiles"
if ($LASTEXITCODE -ne 0) { throw "Connexion impossible (adresse, utilisateur ou connexion SSH ?)." }
scp $planches.FullName "${Serveur}:monsuta-potaru-images/parc/tuiles/"
if ($LASTEXITCODE -ne 0) { throw "Envoi impossible (voir les messages ci-dessus)." }
Write-Host "Planches du parc envoyées."
