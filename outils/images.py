"""
Rassemble les images servies par le site, dans PokeBank/wwwroot/images/ :

- pokemon/<numéro d'espèce>.png, pokemon/<numéro>-<forme>.png (formes régionales, Méga-Évolutions, Primo-Résurgences,
  Gigamax, Zarbi... sous le nom de forme de PokéAPI : 19-alola.png, 6-mega-x.png), pokemon/femelle/<numéro>.png (espèces
  dont la femelle est différente), les mêmes en chromatique sous pokemon/chromatique/, pokemon/inconnu.png en
  remplacement (images et liste des formes de PokéAPI) ;
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
import csv
import io
import json
import os
import pathlib
import re
import sys
import unicodedata
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor

PKHEX = "https://raw.githubusercontent.com/kwsch/PKHeX/25.12.21"  # même version que le paquet PKHeX.Core du site
POKEAPI = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites"
POKEAPI_DATA = "https://raw.githubusercontent.com/PokeAPI/pokeapi/master/data/v2/csv"
POKESPRITE = "https://raw.githubusercontent.com/msikma/pokesprite/master"
TYPES = ["normal", "fighting", "flying", "poison", "ground", "rock", "bug", "ghost", "steel", "fire", "water", "grass",
         "electric", "psychic", "ice", "dragon", "dark", "fairy"]

OUT = pathlib.Path(__file__).resolve().parent.parent / "PokeBank" / "wwwroot" / "images"


def get(url):
    headers = {"Authorization": f"Bearer {os.environ['GITHUB_TOKEN']}"} if "GITHUB_TOKEN" in os.environ and "api.github" in url else {}
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=60) as r:
        return r.read()


def lines(path):
    """Fichier texte de PKHeX : une ligne par numéro (espèce, objet...)."""
    return get(f"{PKHEX}/PKHeX.Core/Resources/text/{path}").decode("utf-8-sig").splitlines()


TREES = {}  # arbres GitHub déjà lus : l'API n'accepte que 60 appels par heure sans jeton (variable GITHUB_TOKEN)


def files_of(repo, branch, folder):
    """Fichiers d'un dossier d'un dépôt GitHub (API des arbres, sans limite de 1 000 fichiers)."""
    def tree(sha):
        if (repo, sha) not in TREES:
            TREES[repo, sha] = json.loads(get(f"https://api.github.com/repos/{repo}/git/trees/{sha}"))["tree"]
        return TREES[repo, sha]
    sha = branch
    for part in folder.split("/"):
        sha = next(t["sha"] for t in tree(sha) if t["path"] == part)
    return {t["path"] for t in tree(sha) if t["type"] == "blob"}


def slug(name):
    """Nom de fichier PokéAPI d'un nom anglais : « King’s Rock » → kings-rock, « Poké Ball » → poke-ball."""
    ascii_name = unicodedata.normalize("NFKD", name).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9-]", "", ascii_name.lower().replace(" ", "-"))


def table(name):
    """Table CSV des données de PokéAPI (une ligne par Pokémon, par forme...)."""
    return list(csv.DictReader(io.StringIO(get(f"{POKEAPI_DATA}/{name}.csv").decode("utf-8"))))


def pokemon_jobs():
    """
    Images des espèces connues de PKHeX, de leurs formes et de leurs femelles, normales et chromatiques. Sans liste des
    fichiers de PokéAPI : une image absente est simplement sautée (voir fetch).
    """
    species = len(lines("other/en/text_Species_en.txt")) - 1  # la ligne 0 est « Egg »
    jobs = {"pokemon/inconnu.png": f"{POKEAPI}/pokemon/0.png"}
    folders = {"": "pokemon", "chromatique/": "pokemon/shiny"}  # dossier du site → dossier de PokéAPI
    for number in range(1, species + 1):
        for mine, theirs in folders.items():
            jobs[f"pokemon/{mine}{number}.png"] = f"{POKEAPI}/{theirs}/{number}.png"

    # Formes : PokéAPI range les variétés (formes régionales, Méga...) sous leur propre numéro (10091.png pour Rattata
    # d'Alola), les autres formes sous <espèce>-<forme>.png (201-b.png). Le site les range toutes sous <espèce>-<forme>.
    pokemon = {p["id"]: p for p in table("pokemon")}
    for form in table("pokemon_forms"):
        variety = pokemon[form["pokemon_id"]]
        number, name = int(variety["species_id"]), form["form_identifier"]
        if not name or number > species or (form["is_default"] == "1" and variety["is_default"] == "1"):
            continue  # forme de base : même image que l'espèce
        sources = [f"{number}-{name}.png"] + ([f"{form['pokemon_id']}.png"] if form["is_default"] == "1" else [])
        for mine, theirs in folders.items():
            jobs[f"pokemon/{mine}{number}-{name}.png"] = [f"{POKEAPI}/{theirs}/{source}" for source in sources]

    # Femelles différentes des mâles (Pikachu, Rapasdepic, Hippodocus...), pour la forme de base seulement.
    for s in table("pokemon_species"):
        if s["has_gender_differences"] == "1" and int(s["id"]) <= species:
            for mine, theirs in folders.items():
                jobs[f"pokemon/{mine}femelle/{s['id']}.png"] = [f"{POKEAPI}/{theirs}/female/{s['id']}.png"]
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
    """
    Télécharge une image si elle n'est pas déjà là. Une liste d'adresses est une suite d'essais, et une image qui n'y est
    pas du tout (404 partout) est sautée sans erreur : renvoie « absente ».
    """
    file, urls = job
    path = OUT / file
    if path.exists():
        return None
    for url in [urls] if isinstance(urls, str) else urls:
        try:
            data = get(url)
        except urllib.error.HTTPError as e:
            if e.code == 404 and not isinstance(urls, str):
                continue
            return f"{file} ({url}) : {e}"
        except urllib.error.URLError as e:
            return f"{file} ({url}) : {e}"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return None
    return "absente"


def main():
    failed = False
    for kind, make in (("Pokémon", pokemon_jobs), ("objets", item_jobs)):
        jobs, missing = make()
        with ThreadPoolExecutor(16) as pool:
            results = list(pool.map(fetch, jobs.items()))
        errors = [e for e in results if e and e != "absente"]
        absent = results.count("absente")
        print(f"{kind} : {len(jobs) - absent} images, {len(missing) + absent} sans image, {len(errors)} erreurs")
        for e in errors:
            print("  erreur :", e)
        failed |= bool(errors)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
