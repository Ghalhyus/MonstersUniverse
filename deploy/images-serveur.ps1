# Remplit (ou met à jour) le dossier des images du serveur, ~/pokebank-images, en lançant outils/images.py sur le VPS :
# il télécharge les images depuis leurs projets d'origine (PokéAPI, SpriteCollab...), puis le site redémarre pour voir les
# nouvelles. ~5 minutes la première fois ; les images déjà présentes sont gardées. À lancer après une première publication,
# et quand les images doivent être mises à jour (nouvelle version de PKHeX, nouveaux sprites...).
#   deploy\images-serveur.cmd ubuntu@IP_DU_VPS
param(
    [Parameter(Mandatory = $true)] [string] $Serveur,
    [string] $Dossier = "pokebank"
)
$ErrorActionPreference = "Stop"

Write-Host "== Images sur $Serveur (téléchargement depuis les projets d'origine, quelques minutes)"
$commande = "command -v python3 > /dev/null || { echo 'Python 3 manque sur le serveur : sudo apt install python3'; exit 1; } && " +
    "mkdir -p ~/pokebank-images && python3 ~/$Dossier/outils/images.py --sortie ~/pokebank-images && " +
    "cd ~/$Dossier && docker compose restart pokebank && echo 'Images à jour, PokeBank redémarré.'"
ssh $Serveur $commande
if ($LASTEXITCODE -ne 0) { throw "La mise à jour des images a échoué (voir les messages ci-dessus)." }
