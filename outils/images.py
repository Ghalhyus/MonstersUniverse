"""
Rassemble les images servies par le site, dans MonsutaPotaru/wwwroot/images/ :

- pokemon/<numéro d'espèce>.png, pokemon/<numéro>-<forme>.png (formes régionales, Méga-Évolutions, Primo-Résurgences,
  Gigamax, Zarbi... sous le nom de forme de PokéAPI : 19-alola.png, 6-mega-x.png), pokemon/femelle/<numéro>.png (espèces
  dont la femelle est différente), les mêmes en chromatique sous pokemon/chromatique/, pokemon/inconnu.png en
  remplacement (images et liste des formes de PokéAPI) ;
- objets/<numéro d'objet>.png (numéros de PKHeX, ceux du site), objets/ct-<type>.png pour les CT et DT (une par type de
  capacité), objets/inconnu.png pour les objets sans image ;
- donjon/ : sprites animés façon Donjon Mystère de SpriteCollab (PMD Collab, https://sprites.pmdcollab.org), animations
  Idle, Walk et Sleep seulement, dans les dossiers de SpriteCollab : donjon/<espèce sur 4 chiffres>[/<forme>[/<chromatique>
  [/<femelle>]]]/ (donjon/0025/ Pikachu, donjon/0025/0000/0001/ chromatique, donjon/0025/0000/0000/0002/ femelle...).
  Chaque dossier a ses planches <Animation>-Anim.png et un AnimData.xml réduit à ces animations (taille des images,
  durées) ; donjon/credits.txt liste les auteurs de chaque dossier (licence CC BY-NC 4.0 : à citer), donjon/formes.json
  le dossier de chaque forme par espèce (« 0026 » : {« Alola » : « 0001 », « Mega_X » : « 0002 »...}) ;
- parc/foret.png : fond du « Parc », le fond de boîte PC « Forêt » de Diamant Étincelant / Perle Scintillante (PKHeX) ;
- livingdex/ : sprites des jeux de chaque living dex (livingdex/rouge-bleu/<numéro>.png : Super Game Boy, livingdex/jaune/ :
  Game Boy Color, livingdex/or/, argent/, cristal/, rubis-saphir/, emeraude/, rouge-feu-vert-feuille/, diamant-perle/,
  platine/, heartgold-soulsilver/, noire-blanche/ ; PokéAPI, versions/generation-i à v/<jeu> ; à partir de la 6e, les
  images de pokemon/), avec les formes qu'une
  sauvegarde du jeu peut contenir (Zarbi : 201-b.png..., 201-exclamation.png, 201-question.png ; Deoxys : 386-speed.png,
  386-attack.png, 386-defense.png ; 4e génération : 412-sandy.png, 422-east.png, 479-heat.png, 487-origin.png,
  492-sky.png... ; 5e : 550-blue-striped.png, 585-summer.png, 641-therian.png, 646-black.png, 647-resolute.png...), et
  jaquettes officielles (livingdex/jaquettes/rouge.png, bleu.png, jaune.png, or.png, argent.png, cristal.png, rubis.png,
  saphir.png, emeraude.png, rouge-feu.png, vert-feuille.png, diamant.png, perle.png, platine.png, heartgold.png,
  soulsilver.png, noire.png, blanche.png, noire-2.png, blanche-2.png, x.png, y.png, rubis-omega.png, saphir-alpha.png,
  soleil.png, lune.png, ultra-soleil.png, ultra-lune.png, lets-go-pikachu.png, lets-go-evoli.png, epee.png, bouclier.png,
  diamant-etincelant.png, perle-scintillante.png, legendes-arceus.png, ecarlate.png, violet.png : Bulbagarden Archives,
  versions anglaises, faute de françaises).

Sources des objets, dans l'ordre :
  1. PokéAPI, par nom anglais (l'apparence d'origine du site), sauf quand ce nom est porté par plusieurs objets ;
  2. pokesprite, par numéro d'objet (table data/item-map.json) ;
  3. PKHeX, par numéro d'objet (images « Big Items », puis « Artwork Items »).
Chaque source est essayée tour à tour (404 : la suivante), sans lister les dossiers des dépôts : pas d'API GitHub ni de
jeton, seulement des fichiers bruts.
Les objets qu'aucune source n'a (objets rares, objets de Légendes Arceus, accessoires de pique-nique...)
gardent inconnu.png : il suffit de déposer leur image sous leur numéro pour la remplacer.

Lancement (Python 3, rien à installer) : python outils/images.py [pokemon] [objets] [donjon] [fonds] [livingdex] [--sortie <dossier>]
(tout, sans argument ; dans MonsutaPotaru/wwwroot/images, sauf --sortie : le serveur l'utilise pour ~/monsuta-potaru-images, voir
deploy/images-serveur.ps1). Les images ne sont pas dans le dépôt Git : en local, outils/recuperer-images.cmd les copie
depuis le serveur, plus vite que les reconstruire.
Les images déjà présentes sont gardées (supprimer un dossier pour le reprendre). À relancer quand PKHeX connaît de
nouvelles espèces ou de nouveaux objets (après avoir mis à jour PKHEX ci-dessous).
"""
import csv
import io
import xml.etree.ElementTree as ElementTree
import json
import pathlib
import re
import sys
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor

PKHEX = "https://raw.githubusercontent.com/kwsch/PKHeX/26.10.18"  # même version que le paquet PKHeX.Core du site
POKEAPI = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites"
POKEAPI_DATA = "https://raw.githubusercontent.com/PokeAPI/pokeapi/master/data/v2/csv"
POKESPRITE = "https://raw.githubusercontent.com/msikma/pokesprite/master"
SPRITECOLLAB = "https://raw.githubusercontent.com/PMDCollab/SpriteCollab/master"
BULBAGARDEN = "https://archives.bulbagarden.net/w/api.php"  # Bulbagarden Archives (jaquettes)
DONJON_ANIMATIONS = ["Idle", "Walk", "Sleep"]  # animations gardées de SpriteCollab (il y en a une quarantaine)
TYPES = ["normal", "fighting", "flying", "poison", "ground", "rock", "bug", "ghost", "steel", "fire", "water", "grass",
         "electric", "psychic", "ice", "dragon", "dark", "fairy"]

OUT = pathlib.Path(__file__).resolve().parent.parent / "MonsutaPotaru" / "wwwroot" / "images"


def get(url):
    # Avec un nom : Bulbagarden refuse les requêtes anonymes de Python.
    request = urllib.request.Request(url, headers={"User-Agent": "Monsuta Potaru (outils/images.py)"})
    with urllib.request.urlopen(request, timeout=60) as r:
        return r.read()


def lines(path):
    """Fichier texte de PKHeX : une ligne par numéro (espèce, objet...)."""
    return get(f"{PKHEX}/PKHeX.Core/Resources/text/{path}").decode("utf-8-sig").splitlines()


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
    species_names = {s["id"]: s["identifier"] for s in table("pokemon_species")}
    for form in table("pokemon_forms"):
        variety = pokemon[form["pokemon_id"]]
        number, name = int(variety["species_id"]), form["form_identifier"]
        if not name or number > species or (form["is_default"] == "1" and variety["is_default"] == "1"):
            continue  # forme de base : même image que l'espèce
        if name == "gmax":  # Shifours et Salarsen ont un Gigamax par forme, tous « gmax » : 892-rapid-strike-gmax.png
            name = variety["identifier"].removeprefix(species_names[variety["species_id"]] + "-")
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
    """
    Images des objets connus de PKHeX : pour chacun, les sources dans l'ordre (voir en tête du fichier). Sans liste des
    fichiers des dépôts : la première image qui existe est gardée, un objet qu'aucune source n'a est sauté (voir fetch).
    """
    names = lines("items/text_Items_en.txt")
    pokesprite = json.loads(get(f"{POKESPRITE}/data/item-map.json"))
    shared = {n for n in names if names.count(n) > 1}

    jobs = {}  # fichier de sortie → adresses à essayer dans l'ordre
    for item, name in enumerate(names):
        # Les CT, DT et CS ont l'image de leur type ; les « ★… » ne sont pas des objets.
        if item == 0 or not name.strip() or name.startswith(("???", "★")) or re.match(r"^(TM|TR|HM)\d", name):
            continue
        exact = pokesprite.get(f"item_{item:04d}")
        sources = []
        if not (name in shared and exact):  # nom anglais porté par plusieurs objets : pokesprite, qui a le bon numéro
            sources.append(f"{POKEAPI}/items/{slug(name)}.png")
        if exact:
            sources.append(f"{POKESPRITE}/items/{exact}.png")
        sources.append(f"{PKHEX}/PKHeX.Drawing.PokeSprite/Resources/img/Big%20Items/bitem_{item}.png")
        sources.append(f"{PKHEX}/PKHeX.Drawing.PokeSprite/Resources/img/Artwork%20Items/aitem_{item}.png")
        jobs[f"objets/{item}.png"] = sources
    for t in TYPES:
        jobs[f"objets/ct-{t}.png"] = f"{POKEAPI}/items/tm-{t}.png"
    jobs["objets/inconnu.png"] = f"{PKHEX}/PKHeX.Drawing.PokeSprite/Resources/img/Big%20Items/bitem_unk.png"
    return jobs, []


def donjon_jobs():
    """
    Sprites animés de SpriteCollab : pour chaque dossier qui a des sprites (espèce, forme, chromatique, femelle), les
    planches des animations de DONJON_ANIMATIONS et celles qu'elles réutilisent (une animation peut être la copie d'une
    autre, <CopyOf>, sans planche à elle), et un AnimData.xml réduit à ces animations, écrit tout de suite.
    """
    tracker = json.loads(get(f"{SPRITECOLLAB}/tracker.json"))
    names = {}  # identifiant d'auteur (Discord) → nom affiché
    for line in get(f"{SPRITECOLLAB}/credit_names.txt").decode("utf-8").splitlines()[1:]:
        parts = line.split("\t")
        if len(parts) >= 2:
            names[parts[1]] = parts[0]
    folders = []  # (dossier, nom, auteurs)
    def walk(node, path, title):
        if node.get("sprite_complete", 0) > 0 and any(a in node.get("sprite_files", {}) for a in DONJON_ANIMATIONS):
            credit = node.get("sprite_credit", {})
            authors = [credit.get("primary", "")] + credit.get("secondary", [])
            folders.append((path, title, [names.get(a, a) for a in authors if a]))
        for key, child in node.get("subgroups", {}).items():
            walk(child, f"{path}/{key}", f"{title} {child.get('name', '')}".strip())
    for number, node in tracker.items():
        walk(node, number, node.get("name", ""))

    def sheets(folder):
        """Planches nécessaires d'un dossier, d'après son AnimData.xml (réduit et écrit s'il n'est pas déjà là)."""
        local = OUT / "donjon" / folder / "AnimData.xml"
        try:
            data = local.read_bytes() if local.exists() else get(f"{SPRITECOLLAB}/sprite/{folder}/AnimData.xml")
        except urllib.error.URLError as e:
            return folder, None, f"{folder}/AnimData.xml : {e}"
        root = ElementTree.fromstring(data)
        anims = {a.findtext("Name"): a for a in root.iter("Anim")}
        kept, todo = set(), [a for a in DONJON_ANIMATIONS if a in anims]
        while todo:  # suit les <CopyOf> jusqu'aux animations qui ont une planche
            name = todo.pop()
            if name in kept:
                continue
            kept.add(name)
            if (copy := anims[name].findtext("CopyOf")) and copy in anims:
                todo.append(copy)
        if not local.exists():
            container = root.find("Anims")
            for anim in list(container):
                if anim.findtext("Name") not in kept:
                    container.remove(anim)
            local.parent.mkdir(parents=True, exist_ok=True)
            local.write_bytes(ElementTree.tostring(root, encoding="utf-8", xml_declaration=True))
        return folder, [n for n in kept if not anims[n].findtext("CopyOf")], None

    jobs, missing = {}, []
    with ThreadPoolExecutor(16) as pool:
        for folder, needed, error in pool.map(sheets, [f for f, _, _ in folders]):
            if error:
                missing.append(error)
                continue
            for name in needed:
                jobs[f"donjon/{folder}/{name}-Anim.png"] = [f"{SPRITECOLLAB}/sprite/{folder}/{name}-Anim.png"]

    # Formes de chaque espèce (nom SpriteCollab → dossier), pour que le site retrouve le sprite d'une forme (PokemonSprites).
    forms = {number: {child.get("name", ""): key for key, child in node.get("subgroups", {}).items() if child.get("name")}
             for number, node in tracker.items()}
    (OUT / "donjon").mkdir(parents=True, exist_ok=True)
    (OUT / "donjon" / "formes.json").write_text(json.dumps({k: v for k, v in forms.items() if v}, ensure_ascii=False), encoding="utf-8")

    # Auteurs de chaque dossier : la licence CC BY-NC 4.0 demande de les citer.
    lines = ["Sprites animés : SpriteCollab (PMD Collab, https://sprites.pmdcollab.org), licence CC BY-NC 4.0.",
             "Auteurs de chaque dossier de sprites (animations Idle, Walk et Sleep) :", ""]
    lines += [f"{folder}\t{title}\t{', '.join(authors)}" for folder, title, authors in folders]
    (OUT / "donjon").mkdir(parents=True, exist_ok=True)
    (OUT / "donjon" / "credits.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return jobs, missing


def fonds_jobs():
    """Fonds de boîte PC (PKHeX) : celui du « Parc »."""
    return {"parc/foret.png": f"{PKHEX}/PKHeX.Drawing.Misc/Resources/img/box/bdsp/box_wp01bdsp.png"}, []


# Living dex : dossier des sprites de chaque groupe de jeux (dossier PokéAPI, avec fond transparent : « transparent » en 1re et
# 2e génération, ceux de la 3e le sont déjà ; nombre d'espèces), formes de chaque groupe (fichier du site ← fichier PokéAPI)
# et jaquettes (fichier Bulbagarden Archives). À garder en accord avec LivingDexGames (MonsutaPotaru/Services/LivingDexService.cs).
LIVINGDEX_SPRITES = {"rouge-bleu": ("generation-i/red-blue/transparent", 151), "jaune": ("generation-i/yellow/transparent", 151),
                     "or": ("generation-ii/gold/transparent", 251), "argent": ("generation-ii/silver/transparent", 251),
                     "cristal": ("generation-ii/crystal/transparent", 251),
                     "rubis-saphir": ("generation-iii/ruby-sapphire", 386), "emeraude": ("generation-iii/emerald", 386),
                     "rouge-feu-vert-feuille": ("generation-iii/firered-leafgreen", 386),
                     "diamant-perle": ("generation-iv/diamond-pearl", 493), "platine": ("generation-iv/platinum", 493),
                     "heartgold-soulsilver": ("generation-iv/heartgold-soulsilver", 493),
                     # 5e génération : PokéAPI n'a que les sprites (fixes) de Noire / Blanche, repris par Noire 2 / Blanche 2
                     "noire-blanche": ("generation-v/black-white", 649)}
# À partir de la 6e génération, les living dex prennent les images de la collection (pokemon/) : pas de sprites à eux.
UNOWN_2 = {f"201-{c}": f"201-{c}" for c in "bcdefghijklmnopqrstuvwxyz"}  # Zarbi B à Z (A : 201.png)
UNOWN_3 = {**UNOWN_2, "201-exclamation": "201-exclamation", "201-question": "201-question"}
LIVINGDEX_FORMS = {"or": UNOWN_2, "argent": UNOWN_2, "cristal": UNOWN_2, "rubis-saphir": UNOWN_3,
                   # Deoxys : formes Vitesse (Émeraude), Attaque (Rouge Feu) et Défense (Vert Feuille), Pokémon 10001 à 10003 de PokéAPI
                   "emeraude": {**UNOWN_3, "386-speed": "10003"},
                   "rouge-feu-vert-feuille": {**UNOWN_3, "386-attack": "10001", "386-defense": "10002"}}
# 4e génération : chaque Deoxys garde sa forme ; capes de Cheniti (412) et Cheniselle (413, Pokémon 10004 et 10005), mers de
# Sancoki (422) et Tritosor (423). Platine et HeartGold / SoulSilver ajoutent Motisma (479, 10008 à 10012), Giratina
# Originel (487, 10007) et Shaymin Céleste (492, 10006) ; HeartGold / SoulSilver, Pichu Troizépi (172).
UNOWN_DEOXYS_4 = {**UNOWN_3, "386-attack": "10001", "386-defense": "10002", "386-speed": "10003",
                  "412-sandy": "412-sandy", "412-trash": "412-trash", "413-sandy": "10004", "413-trash": "10005",
                  "422-east": "422-east", "423-east": "423-east"}
PLATINUM_4 = {**UNOWN_DEOXYS_4, "479-heat": "10008", "479-wash": "10009", "479-frost": "10010", "479-fan": "10011",
              "479-mow": "10012", "487-origin": "10007", "492-sky": "10006"}
# 5e génération (Pichu Troizépi ne la rejoint pas) : motif Bleu de Bargantua (550, 10016), saisons de Vivaldaim (585) et
# Haydaim (586) ; Noire 2 / Blanche 2 ajoutent les formes Totémiques de Boréas, Fulguris, Démétéros (10019 à 10021),
# Kyurem Noir et Blanc (10022, 10023) et Keldeo Décidé (10024) : un seul dossier pour les quatre jeux.
UNOVA_5 = {**PLATINUM_4, "550-blue-striped": "10016",
           **{f"{n}-{s}": f"{n}-{s}" for n in (585, 586) for s in ("summer", "autumn", "winter")},
           "641-therian": "10019", "642-therian": "10020", "645-therian": "10021",
           "646-black": "10022", "646-white": "10023", "647-resolute": "10024"}
LIVINGDEX_FORMS.update({"diamant-perle": UNOWN_DEOXYS_4, "platine": PLATINUM_4,
                        "heartgold-soulsilver": {**PLATINUM_4, "172-spiky-eared": "172-spiky-eared"},
                        "noire-blanche": UNOVA_5})
# (Émeraude, Diamant, Perle, HeartGold, SoulSilver : le fichier de Bulbagarden est un JPEG ; le site le garde en .png comme
# les autres, le navigateur le lit quand même.)
LIVINGDEX_COVERS = {"rouge": "File:Red EN boxart.png", "bleu": "File:Blue EN boxart.png", "jaune": "File:Yellow EN boxart.png",
                    "or": "File:Gold EN boxart.png", "argent": "File:Silver EN boxart.png", "cristal": "File:Crystal EN boxart.png",
                    "rubis": "File:Ruby EN boxart.png", "saphir": "File:Sapphire EN boxart.png", "emeraude": "File:Emerald EN boxart.jpg",
                    "rouge-feu": "File:FireRed EN boxart.png", "vert-feuille": "File:LeafGreen EN boxart.png",
                    "diamant": "File:Diamond EN boxart.jpg", "perle": "File:Pearl EN boxart.jpg",
                    "platine": "File:Platinum EN boxart.png", "heartgold": "File:HeartGold EN boxart.jpg",
                    "soulsilver": "File:SoulSilver EN boxart.jpg",
                    "noire": "File:Black EN boxart.png", "blanche": "File:White EN boxart.png",
                    "noire-2": "File:Black 2 EN boxart.png", "blanche-2": "File:White 2 EN boxart.png",
                    "x": "File:X EN boxart.png", "y": "File:Y EN boxart.png",
                    "rubis-omega": "File:Omega Ruby EN boxart.png", "saphir-alpha": "File:Alpha Sapphire EN boxart.png",
                    "soleil": "File:Sun EN boxart.png", "lune": "File:Moon EN boxart.png",
                    "ultra-soleil": "File:Ultra Sun EN boxart.png", "ultra-lune": "File:Ultra Moon EN boxart.png",
                    "lets-go-pikachu": "File:Lets Go Pikachu EN boxart.png", "lets-go-evoli": "File:Lets Go Eevee EN boxart.png",
                    "epee": "File:Sword EN boxart.png", "bouclier": "File:Shield EN boxart.png",
                    "diamant-etincelant": "File:Brilliant Diamond EN boxart.png", "perle-scintillante": "File:Shining Pearl EN boxart.png",
                    "legendes-arceus": "File:Legends Arceus EN boxart.png",
                    "ecarlate": "File:Scarlet EN boxart.png", "violet": "File:Violet EN boxart.png"}


def livingdex_jobs():
    """Sprites des jeux des living dex (PokéAPI, fond transparent) et jaquettes officielles (Bulbagarden Archives)."""
    jobs, missing = {}, []
    for folder, (theirs, count) in LIVINGDEX_SPRITES.items():
        for number in range(1, count + 1):
            jobs[f"livingdex/{folder}/{number}.png"] = f"{POKEAPI}/pokemon/versions/{theirs}/{number}.png"
        for mine, their_file in LIVINGDEX_FORMS.get(folder, {}).items():
            jobs[f"livingdex/{folder}/{mine}.png"] = f"{POKEAPI}/pokemon/versions/{theirs}/{their_file}.png"
    for name, title in LIVINGDEX_COVERS.items():
        if (OUT / "livingdex" / "jaquettes" / f"{name}.png").exists():
            continue
        try:  # adresse d'une version réduite (480 px de large, ~450 Ko au lieu de 1,5 Mo), donnée par l'API de Bulbagarden Archives
            query = json.loads(get(f"{BULBAGARDEN}?action=query&prop=imageinfo&iiprop=url&iiurlwidth=480&format=json&titles={urllib.parse.quote(title)}"))
            jobs[f"livingdex/jaquettes/{name}.png"] = next(iter(query["query"]["pages"].values()))["imageinfo"][0]["thumburl"]
        except (urllib.error.URLError, KeyError, StopIteration) as e:
            missing.append(f"jaquette {name} ({title}) : {e}")
            print(f"  jaquette {name} introuvable ({title}) : {e}")
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
    global OUT
    args = sys.argv[1:]
    if "--sortie" in args:
        i = args.index("--sortie")
        OUT = pathlib.Path(args[i + 1]).expanduser().resolve()
        del args[i:i + 2]
    failed = False
    parts = {"pokemon": ("Pokémon", pokemon_jobs), "objets": ("objets", item_jobs), "donjon": ("sprites Donjon Mystère", donjon_jobs),
             "fonds": ("fonds", fonds_jobs), "livingdex": ("living dex", livingdex_jobs)}
    chosen = [parts[a] for a in args if a in parts] or list(parts.values())
    for kind, make in chosen:
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
