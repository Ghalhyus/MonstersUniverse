"""
Rassemble les images servies par le site, dans PokeBank/wwwroot/images/ :

- pokemon/<numéro d'espèce>.png, pokemon/chromatique/<numéro>.png (PokéAPI), pokemon/inconnu.png en remplacement ;
- objets/<numéro d'objet>.png (numéros de PKHeX, ceux du site), objets/ct-<type>.png pour les CT et DT (une par type de
  capacité), objets/inconnu.png pour les objets sans image.

Sources des objets, dans l'ordre :
  1. PokéAPI, par nom anglais (l'apparence d'origine du site), sauf quand ce nom est porté par plusieurs objets ;
  2. pokesprite, par numéro d'objet (table data/item-map.json) ;
  3. PKHeX, par numéro d'objet (images « Big Items », puis « Artwork Items »).
Les objets qu'aucune source n'a (bonbons de Let's Go, ingrédients d'Écarlate/Violet, objets de Légendes Arceus...)
gardent inconnu.png : il suffit de déposer leur image sous leur numéro pour la remplacer.

Lancement (Python 3, rien à installer) : python outils/images.py
Les images déjà présentes sont gardées (supprimer un dossier pour le reprendre). À relancer quand PKHeX connaît de
nouvelles espèces ou de nouveaux objets (après avoir mis à jour PKHEX ci-dessous).
"""
import json
import pathlib
import re
import sys
import unicodedata
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor

PKHEX = "https://raw.githubusercontent.com/kwsch/PKHeX/25.12.21"  # même version que le paquet PKHeX.Core du site
POKEAPI = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites"
POKESPRITE = "https://raw.githubusercontent.com/msikma/pokesprite/master"
TYPES = ["normal", "fighting", "flying", "poison", "ground", "rock", "bug", "ghost", "steel", "fire", "water", "grass",
         "electric", "psychic", "ice", "dragon", "dark", "fairy"]

OUT = pathlib.Path(__file__).resolve().parent.parent / "PokeBank" / "wwwroot" / "images"


def get(url):
    with urllib.request.urlopen(url, timeout=60) as r:
        return r.read()


def lines(path):
    """Fichier texte de PKHeX : une ligne par numéro (espèce, objet...)."""
    return get(f"{PKHEX}/PKHeX.Core/Resources/text/{path}").decode("utf-8-sig").splitlines()


def files_of(repo, branch, folder):
    """Fichiers d'un dossier d'un dépôt GitHub (API des arbres, sans limite de 1 000 fichiers)."""
    sha = branch
    for part in folder.split("/"):
        tree = json.loads(get(f"https://api.github.com/repos/{repo}/git/trees/{sha}"))
        sha = next(t["sha"] for t in tree["tree"] if t["path"] == part)
    tree = json.loads(get(f"https://api.github.com/repos/{repo}/git/trees/{sha}"))
    return {t["path"] for t in tree["tree"] if t["type"] == "blob"}


def slug(name):
    """Nom de fichier PokéAPI d'un nom anglais : « King’s Rock » → kings-rock, « Poké Ball » → poke-ball."""
    ascii_name = unicodedata.normalize("NFKD", name).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9-]", "", ascii_name.lower().replace(" ", "-"))


def pokemon_jobs():
    """Images des espèces connues de PKHeX, normales et chromatiques."""
    species = len(lines("other/en/text_Species_en.txt")) - 1  # la ligne 0 est « Egg »
    jobs = {"pokemon/inconnu.png": f"{POKEAPI}/pokemon/0.png"}
    for number in range(1, species + 1):
        jobs[f"pokemon/{number}.png"] = f"{POKEAPI}/pokemon/{number}.png"
        jobs[f"pokemon/chromatique/{number}.png"] = f"{POKEAPI}/pokemon/shiny/{number}.png"
    return jobs, []


def item_jobs():
    names = lines("items/text_Items_en.txt")
    pokeapi = files_of("PokeAPI/sprites", "master", "sprites/items")
    pokesprite = json.loads(get(f"{POKESPRITE}/data/item-map.json"))
    big = files_of("kwsch/PKHeX", "25.12.21", "PKHeX.Drawing.PokeSprite/Resources/img/Big Items")
    artwork = files_of("kwsch/PKHeX", "25.12.21", "PKHeX.Drawing.PokeSprite/Resources/img/Artwork Items")
    shared = {n for n in names if names.count(n) > 1}

    jobs = {}  # fichier de sortie → adresse
    missing = []
    for item, name in enumerate(names):
        # Les CT, DT et CS ont l'image de leur type ; les « ★… » ne sont pas des objets.
        if item == 0 or not name.strip() or name.startswith(("???", "★")) or re.match(r"^(TM|TR|HM)\d", name):
            continue
        file = f"objets/{item}.png"
        exact = pokesprite.get(f"item_{item:04d}")
        if f"{slug(name)}.png" in pokeapi and not (name in shared and exact):
            jobs[file] = f"{POKEAPI}/items/{slug(name)}.png"
        elif exact:
            jobs[file] = f"{POKESPRITE}/items/{exact}.png"
        elif f"bitem_{item}.png" in big:
            jobs[file] = f"{PKHEX}/PKHeX.Drawing.PokeSprite/Resources/img/Big%20Items/bitem_{item}.png"
        elif f"aitem_{item}.png" in artwork:
            jobs[file] = f"{PKHEX}/PKHeX.Drawing.PokeSprite/Resources/img/Artwork%20Items/aitem_{item}.png"
        else:
            missing.append(f"{item} {name}")
    for t in TYPES:
        jobs[f"objets/ct-{t}.png"] = f"{POKEAPI}/items/tm-{t}.png"
    jobs["objets/inconnu.png"] = f"{PKHEX}/PKHeX.Drawing.PokeSprite/Resources/img/Big%20Items/bitem_unk.png"
    return jobs, missing


def fetch(job):
    file, url = job
    path = OUT / file
    if path.exists():
        return None
    path.parent.mkdir(parents=True, exist_ok=True)
    try:
        path.write_bytes(get(url))
        return None
    except urllib.error.URLError as e:
        return f"{file} ({url}) : {e}"


def main():
    failed = False
    for kind, make in (("Pokémon", pokemon_jobs), ("objets", item_jobs)):
        jobs, missing = make()
        with ThreadPoolExecutor(16) as pool:
            errors = [e for e in pool.map(fetch, jobs.items()) if e]
        print(f"{kind} : {len(jobs)} images, {len(missing)} sans image, {len(errors)} erreurs")
        for e in errors:
            print("  erreur :", e)
        failed |= bool(errors)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
