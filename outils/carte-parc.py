"""
Carte du « Parc » : prend une carte faite dans Tiled avec le pack Cute Fantasy et la prépare pour le site.

- La carte (numéros de tuiles, calques, zone de départ) est écrite dans MonsutaPotaru/wwwroot/parc/<nom>.json, dans Git :
  elle ne contient aucun dessin du pack.
- Les planches du pack qu'elle utilise sont copiées dans MonsutaPotaru/wwwroot/images/parc/tuiles/, hors de Git comme les
  autres images : la licence du pack interdit de le redistribuer, même modifié. deploy\\fond-parc.cmd les envoie sur le
  serveur.

Ce que la carte doit avoir (README, « Parc ») : vue de dessus (orthogonale), tuiles de 16 px, taille fixe (pas
« infinie »), jeux de tuiles incorporés à la carte, calques de tuiles « Sol », « Décors », « Devant » (feuillages,
au-dessus de leur tronc) et « Collisions » (une tuile quelconque = case où l'on ne passe pas), et un calque d'objets « Zones » avec un
rectangle « Départ » : la partie de la carte où vivent les Pokémon (le reste est assombri, à débloquer plus tard).

Lancement (Python 3 seul) :
  python outils/carte-parc.py <carte .tmx ou .tmj> [--nom parc-1] [--tiled <chemin de tiled.exe>]
Un .tmx est d'abord exporté en JSON par Tiled lui-même (en ligne de commande).
"""
import json
import os
import shutil
import subprocess
import sys

args = sys.argv[1:]
def option(name, default):
    if name in args:
        i = args.index(name)
        value = args[i + 1]
        del args[i:i + 2]
        return value
    return default
NAME = option("--nom", "parc-1")
TILED = option("--tiled", shutil.which("tiled") or r"C:\Program Files\Tiled\tiled.exe")
if len(args) != 1:
    sys.exit("Usage : python outils/carte-parc.py <carte .tmx ou .tmj> [--nom parc-1] [--tiled <chemin de tiled.exe>]")

SOURCE = os.path.abspath(args[0])
WWWROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "MonsutaPotaru", "wwwroot")
MAP_OUT = os.path.join(WWWROOT, "parc", NAME + ".json")
TILES_OUT = os.path.join(WWWROOT, "images", "parc", "tuiles")
LAYERS = ["Sol", "Décors", "Devant", "Collisions"]

# ---------- Lecture (un .tmx passe d'abord par l'export JSON de Tiled) ----------
if SOURCE.lower().endswith(".tmx"):
    if not os.path.exists(TILED):
        sys.exit(f"Tiled introuvable ({TILED}) : l'indiquer avec --tiled, ou exporter la carte en JSON depuis Tiled "
                 "(Fichier → Exporter sous… → .tmj).")
    # L'export s'écrit à côté de la carte : les chemins des images restent relatifs à son dossier
    exported = os.path.join(os.path.dirname(SOURCE), f".{NAME}-export.tmj")
    result = subprocess.run([TILED, "--embed-tilesets", "--export-map", "json", SOURCE, exported])
    if result.returncode != 0 or not os.path.exists(exported):
        sys.exit("Tiled n'a pas pu exporter la carte (voir les messages ci-dessus).")
    with open(exported, encoding="utf-8") as f:
        data = json.load(f)
    os.remove(exported)
else:
    with open(SOURCE, encoding="utf-8") as f:
        data = json.load(f)
base = os.path.dirname(SOURCE)

# ---------- Vérifications : un message clair plutôt qu'un parc qui ne s'affiche pas ----------
errors = []
if data.get("orientation") != "orthogonal":
    errors.append("la carte doit être orthogonale (vue de dessus)")
if data.get("tilewidth") != 16 or data.get("tileheight") != 16:
    errors.append("les tuiles doivent faire 16 × 16 pixels")
if data.get("infinite"):
    errors.append("la carte doit avoir une taille fixe (Carte → Propriétés de la carte : décocher « Infinie »)")
layers = {layer["name"]: layer for layer in data.get("layers", [])}
for name in LAYERS:
    if layers.get(name, {}).get("type") != "tilelayer":
        errors.append(f"calque de tuiles « {name} » absent")
for name, layer in layers.items():
    if layer.get("type") == "tilelayer" and layer.get("encoding", "csv") != "csv" and not isinstance(layer.get("data"), list):
        errors.append(f"calque « {name} » : format de calque de tuiles « CSV » attendu (pas de compression)")
zones = layers.get("Zones", {}).get("objects", [])
start = next((o for o in zones if o.get("name") == "Départ"), None)
if start is None:
    errors.append("rectangle « Départ » absent (calque d'objets « Zones »)")
for tileset in data.get("tilesets", []):
    if "source" in tileset:
        errors.append(f"jeu de tuiles « {tileset['source']} » enregistré à part : l'incorporer à la carte "
                      "(bouton « Incorporer le jeu de tuiles » du panneau Jeux de Tuiles)")
    elif "image" not in tileset:
        errors.append(f"jeu de tuiles « {tileset.get('name')} » : une seule image par jeu (pas de « collection d'images »)")
if errors:
    sys.exit("Carte refusée :\n- " + "\n- ".join(errors))

# ---------- Planches : copiées sous leur nom de fichier (le même dans toutes les cartes) ----------
os.makedirs(TILES_OUT, exist_ok=True)
copied = {}
for tileset in data["tilesets"]:
    image = os.path.normpath(os.path.join(base, tileset["image"]))
    if not os.path.exists(image):
        sys.exit(f"Planche introuvable : {image}")
    file_name = os.path.basename(image)
    if copied.setdefault(file_name, image) != image:
        sys.exit(f"Deux planches différentes s'appellent « {file_name} » : renommer l'une des deux.")
    shutil.copyfile(image, os.path.join(TILES_OUT, file_name))
    tileset["image"] = file_name  # le parc la cherche dans images/parc/tuiles/

# ---------- Carte : seulement ce que le parc lit, sur une ligne ----------
os.makedirs(os.path.dirname(MAP_OUT), exist_ok=True)
with open(MAP_OUT, "w", encoding="utf-8", newline="\n") as f:
    json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    f.write("\n")
print(f"Carte écrite : {os.path.normpath(MAP_OUT)} ({data['width']} × {data['height']} tuiles, départ "
      f"{int(start['width']) // 16} × {int(start['height']) // 16})")
print(f"{len(copied)} planches copiées dans {os.path.normpath(TILES_OUT)} (à envoyer sur le serveur : deploy\\fond-parc.cmd)")
