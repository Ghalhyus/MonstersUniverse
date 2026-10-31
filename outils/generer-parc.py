# Génère la carte du parc pour Tiled (Parc_1.tmx) avec le pack Cute Fantasy : herbe, chemins et eau à bordures
# automatiques, forêt en quinconce, arbres, bancs, rochers, fleurs, et le calque « Collisions » qui va avec. Tirages
# fixés (graine 7) : relancé sans changement, il redonne la même carte. Ensuite : python outils/carte-parc.py <Parc_1.tmx>.
# Aperçus (apercu.png, apercu_collisions.png : cases bloquées en rouge) dans le dossier de sortie, ou dans le troisième.
# Usage (Python 3 avec Pillow) :
#   python outils/generer-parc.py <dossier Cute_Fantasy du pack> <dossier de sortie> [dossier des aperçus]
import math
import os
import random
import sys
from PIL import Image

PACK, SORTIE = sys.argv[1], sys.argv[2]
L, H = 80, 60
DEPART = (28, 20, 24, 20)  # zone de départ : x, y, largeur, hauteur (en tuiles)
rng = random.Random(7)

# --- Jeux de tuiles (incorporés à la carte) -------------------------------------------------
JEUX = []  # (nom, chemin relatif au pack, largeur, hauteur)


def jeu(nom, chemin):
    largeur, hauteur = Image.open(os.path.join(PACK, chemin)).size
    JEUX.append((nom, chemin, largeur, hauteur))


jeu("herbe", "Tiles/Grass/Grass_Tiles_2.png")
jeu("herbe_unie", "Tiles/Grass/Grass_2_Middle.png")
jeu("eau", "Tiles/Water/Water_Tile_2.png")
jeu("eau_unie", "Tiles/Water/Water_Middle.png")
jeu("eau_details", "Tiles/Water/Water_Decoration.png")
jeu("chemin_details", "Tiles/Grass/Path_Decoration.png")
jeu("decors", "Outdoor decoration/Outdoor_Decor.png")
jeu("fleurs", "Outdoor decoration/Flowers.png")
jeu("bancs", "Outdoor decoration/Benches.png")
jeu("chene", "Trees/Medium_Oak_Tree.png")
jeu("sapin", "Trees/Medium_Spruce_Tree.png")
jeu("bouleau", "Trees/Medium_Birch_Tree.png")
jeu("grand_chene", "Trees/Big_Oak_Tree.png")

PREMIER = {}
gid = 1
for nom, chemin, w, h in JEUX:
    PREMIER[nom] = gid
    gid += (w // 16) * (h // 16)
GID_COLLISION = gid  # dernier jeu : collisions.png (une tuile rouge)


def t(nom, i):
    return PREMIER[nom] + i


def colonnes(nom):
    return next(w // 16 for n, _, w, _ in JEUX if n == nom)


# --- Calques ---------------------------------------------------------------------------------
sol = [[0] * L for _ in range(H)]
decors = [[0] * L for _ in range(H)]
devant = [[0] * L for _ in range(H)]
bloque = [[False] * L for _ in range(H)]


def dedans(x, y):
    return 0 <= x < L and 0 <= y < H


# --- Formes : forêt de bordure, eau, chemins -----------------------------------------------
def bruit(n, amplitude, periode, graine):
    r = random.Random(graine)
    a, b, c = r.random() * 6.3, r.random() * 6.3, r.random() * 6.3
    return [amplitude * (math.sin(i / periode + a) * 0.6 + math.sin(i / (periode * 0.45) + b) * 0.3
                         + math.sin(i / (periode * 0.21) + c) * 0.1) for i in range(n)]


foret = set()
haut, bas = bruit(L, 2.2, 6, 1), bruit(L, 2.2, 6, 2)
gauche, droite = bruit(H, 2.2, 6, 3), bruit(H, 2.2, 6, 4)
for y in range(H):
    for x in range(L):
        if (y < 6 + haut[x] or y >= H - 5 + bas[x] or x < 5 + gauche[y] or x >= L - 5 + droite[y]):
            foret.add((x, y))


def tache(cx, cy, rx, ry):
    return {(x, y) for y in range(H) for x in range(L) if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1}


# Bosquets à l'intérieur (au nord-ouest et à l'est)
foret |= tache(15, 14, 5, 3.5) | tache(19, 12, 3, 3)
foret |= tache(70, 33, 4, 5)

eau = set()
eau |= tache(47.5, 25.5, 4.2, 3.2) | tache(50, 27, 2.5, 2.2)  # étang de la zone de départ
eau |= tache(60, 14, 7, 4.5) | tache(65, 17, 5, 3.5) | tache(56, 16, 3, 2.5)  # lac au nord-est
eau |= tache(24, 48, 4, 2.6)  # mare au sud-ouest


def chemin_h(y0, x0, x1, epaisseur=3, graine=5):
    ondule = bruit(L, 1.6, 7, graine)
    return {(x, round(y0 + ondule[x]) + k) for x in range(x0, x1) for k in range(epaisseur)}


def chemin_v(x0, y0, y1, epaisseur=3, graine=6):
    ondule = bruit(H, 1.6, 7, graine)
    return {(round(x0 + ondule[y]) + k, y) for y in range(y0, y1) for k in range(epaisseur)}


chemin = chemin_h(33, 0, L) | chemin_v(39, 0, H)


def voisins8(cellules):
    return {(x + dx, y + dy) for x, y in cellules for dx in (-1, 0, 1) for dy in (-1, 0, 1)}


# Le chemin s'arrête à la lisière et contourne l'eau d'une case
chemin -= foret
chemin -= voisins8(eau)
eau -= foret


def nettoyer(cellules):
    """Garde les cellules couvertes par un carré 2 × 2 entièrement dans la forme (bordures sans trous)."""
    cellules = set(cellules)
    while True:
        gardees = set()
        for x, y in cellules:
            for dx in (0, -1):
                for dy in (0, -1):
                    if all((x + dx + i, y + dy + j) in cellules for i in (0, 1) for j in (0, 1)):
                        gardees.add((x, y))
        # Une diagonale isolée (deux carrés qui ne se touchent que par un coin) : on comble
        for x, y in list(gardees):
            for dx, dy in ((1, 1), (1, -1)):
                if (x + dx, y + dy) in gardees and (x + dx, y) not in gardees and (x, y + dy) not in gardees:
                    gardees.add((x + dx, y))
        if gardees == cellules:
            return cellules
        cellules = gardees


eau = nettoyer(eau)
chemin = nettoyer(chemin)


def plus_grand_morceau(cellules):
    restantes, meilleur = set(cellules), set()
    while restantes:
        pile, morceau = [restantes.pop()], set()
        while pile:
            x, y = pile.pop()
            morceau.add((x, y))
            for v in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if v in restantes:
                    restantes.remove(v)
                    pile.append(v)
        meilleur = max(meilleur, morceau, key=len)
    return meilleur


chemin = plus_grand_morceau(chemin)
lisiere = set(foret)


# --- Sol : herbe, chemin et eau, bordures automatiques ---------------------------------------
def bordures(forme, nom, coins, interieur, centre):
    """coins : HG H HD G D BG B BD ; interieur : coins rentrants SE SO NE NO ; centre : fonction."""
    hg, h, hd, g, d, bg, b, bd = coins
    ise, iso, ine, ino = interieur
    for x, y in forme:
        n, s = (x, y - 1) in forme, (x, y + 1) in forme
        o, e = (x - 1, y) in forme, (x + 1, y) in forme
        if not n and not o: i = hg
        elif not n and not e: i = hd
        elif not s and not o: i = bg
        elif not s and not e: i = bd
        elif not n: i = h
        elif not s: i = b
        elif not o: i = g
        elif not e: i = d
        elif (x + 1, y + 1) not in forme: i = ise
        elif (x - 1, y + 1) not in forme: i = iso
        elif (x + 1, y - 1) not in forme: i = ine
        elif (x - 1, y - 1) not in forme: i = ino
        else:
            sol[y][x] = centre()
            continue
        sol[y][x] = t(nom, i)


for y in range(H):
    for x in range(L):
        sol[y][x] = t("herbe", rng.choice((149, 150, 151))) if rng.random() < 0.12 else t("herbe_unie", 0)

bordures(chemin, "herbe", (80, 81, 82, 96, 98, 112, 113, 114), (128, 129, 144, 145),
         lambda: t("chemin_details", rng.randrange(3)) if rng.random() < 0.12 else t("herbe", 97))
bordures(eau, "eau", (0, 1, 2, 3, 5, 6, 7, 8), (9, 10, 12, 13),
         lambda: t("eau_details", rng.randrange(3)) if rng.random() < 0.08 else t("eau_unie", 0))
for x, y in eau:
    bloque[y][x] = True

# --- Objets : arbres, rochers, buissons, fleurs ----------------------------------------------
occupe_decors, occupe_devant = set(), set()


def libre_sol(x, y):
    return dedans(x, y) and (x, y) not in eau and (x, y) not in chemin


def poser(nom, gauche, haut_, largeur, hauteur, x, y, rang_devant=0, bloque_lignes=(), bloque_cols=None,
          verifier=True, marge=0):
    """Pose un bloc de tuiles d'une planche. Les lignes < rang_devant vont dans « Devant »."""
    cols = colonnes(nom)
    cellules = [(x + i, y + j) for j in range(hauteur) for i in range(largeur)]
    if verifier:
        for cx, cy in cellules:
            if not dedans(cx, cy):
                return False
            calque = occupe_devant if cy - y < rang_devant else occupe_decors
            if (cx, cy) in calque:
                return False
        for j in range(rang_devant, hauteur):
            for i in range(largeur):
                if not libre_sol(x + i, y + j) or (x + i, y + j) in lisiere:
                    return False
        zone = {(x + i + dx, y + j + dy) for j in range(rang_devant, hauteur) for i in range(largeur)
                for dx in range(-marge, marge + 1) for dy in range(-marge, marge + 1)}
        if any(c in occupe_decors for c in zone):
            return False
    for j in range(hauteur):
        for i in range(largeur):
            cx, cy = x + i, y + j
            if not dedans(cx, cy):
                continue
            tuile = t(nom, (haut_ + j) * cols + gauche + i)
            if j < rang_devant:
                devant[cy][cx] = tuile
                occupe_devant.add((cx, cy))
            else:
                decors[cy][cx] = tuile
                occupe_decors.add((cx, cy))
            if j in bloque_lignes and (bloque_cols is None or i in bloque_cols):
                bloque[cy][cx] = True
    return True


ARBRES = ["chene"] * 6 + ["sapin"] * 3 + ["bouleau"]

# Forêt dense : un arbre toutes les deux cases, en quinconce ; tout le massif est infranchissable
for y in range(-2, H, 2):
    for x in range(-1 + (y // 2) % 2, L, 2):
        if (x, min(y + 2, H - 1)) in foret or (x + 1, min(y + 2, H - 1)) in foret:
            poser(rng.choice(ARBRES), 2, 0, 2, 3, x, y, rang_devant=2, bloque_lignes=(2,), verifier=False)
for x, y in foret:
    if (x, y) not in occupe_devant and (x, y) not in occupe_decors:
        continue  # bord du massif sans arbre : de l'herbe où l'on peut marcher
    bloque[y][x] = True
    # Sous-bois : des buissons sombres sous les feuillages, pour une forêt bien touffue
    if (x, y) not in occupe_decors and rng.random() < 0.85:
        decors[y][x] = t("decors", rng.choice((86, 87, 88)))
        occupe_decors.add((x, y))


def dans_depart(x, y, marge=0):
    dx, dy, dl, dh = DEPART
    return dx - marge <= x < dx + dl + marge and dy - marge <= y < dy + dh + marge


def essayer(n, fonction):
    for _ in range(n * 40):
        if n <= 0:
            break
        x, y = rng.randrange(L), rng.randrange(H)
        if fonction(x, y):
            n -= 1


# Grands chênes et arbres isolés (peu dans la zone de départ, pour laisser de la place)
essayer(7, lambda x, y: not dans_depart(x + 2, y + 3, 2)
        and poser("grand_chene", 4, 0, 4, 5, x, y, rang_devant=3, bloque_lignes=(3,), bloque_cols=(1, 2), marge=1))
essayer(1, lambda x, y: dans_depart(x, y) and x < 36 and y < 28
        and poser("grand_chene", 4, 0, 4, 5, x, y, rang_devant=3, bloque_lignes=(3,), bloque_cols=(1, 2), marge=1))
essayer(26, lambda x, y: not dans_depart(x, y, 1)
        and poser(rng.choice(ARBRES), 2, 0, 2, 3, x, y, rang_devant=2, bloque_lignes=(2,), marge=1))

# Bancs au bord de l'étang (dossier et assise bloqués : on ne marche pas sur le dossier)
poser("bancs", 2, 0, 2, 2, 44, 30, bloque_lignes=(0, 1), verifier=True)
poser("bancs", 2, 0, 2, 2, 52, 23, bloque_lignes=(0, 1), verifier=True)

# Gros rochers (zone rocheuse au sud-est)
for _ in range(3):
    essayer(1, lambda x, y: 50 <= x <= 70 and 40 <= y <= 52
            and poser("decors", rng.choice((4, 6)), 11, 2, 2, x, y, bloque_lignes=(0, 1), marge=1))


def objet(liste, n, zone=lambda x, y: True, bloquant=False, marge=0):
    def un(x, y):
        if not zone(x, y):
            return False
        return poser("decors", 0, 0, 1, 1, x, y, verifier=True, marge=marge) and remplacer(x, y)

    def remplacer(x, y):
        decors[y][x] = t("decors", rng.choice(liste))
        bloque[y][x] = bloquant
        return True
    essayer(n, un)


ROCHERS = [40, 41, 45, 46, 57, 58, 59]
BUISSONS = [50, 84, 86, 87, 88]
SOUCHES = [55, 81, 83]
CHAMPIGNONS = [15, 16, 17, 25, 26, 27, 28, 29, 36, 37, 38]
PETITES_FLEURS = [0, 1, 2, 3, 4, 5, 9, 10, 11, 12, 13, 14, 18, 19, 20, 21, 22, 23]
PLANTES = [24, 49, 71, 78, 79, 80, 89]
pres_foret = lambda x, y: any((x + dx, y + dy) in foret for dx in (-3, 0, 3) for dy in (-3, 0, 3))

objet(ROCHERS, 14, lambda x, y: 48 <= x <= 72 and 38 <= y <= 54, bloquant=True, marge=1)
objet(ROCHERS, 8, bloquant=True, marge=1)
objet(BUISSONS, 22, lambda x, y: not dans_depart(x, y), bloquant=True, marge=1)
objet(BUISSONS, 3, lambda x, y: dans_depart(x, y), bloquant=True, marge=1)
objet(SOUCHES, 6, pres_foret, bloquant=True, marge=1)
objet(CHAMPIGNONS, 30, pres_foret)
objet(PLANTES, 40)
objet(PETITES_FLEURS, 70)

# Troncs couchés (2 cases)
essayer(4, lambda x, y: not dans_depart(x, y, 2)
        and poser("decors", rng.choice((0, 4)), 7, 2, 1, x, y, bloque_lignes=(0,), marge=1))

# Prairie fleurie au sud-ouest, et quelques fleurs autour de l'étang
FLEURS = [r * 10 + c for r in range(10) for c in range(5)]


def fleur(x, y, zone):
    if not zone(x, y) or not libre_sol(x, y) or (x, y) in occupe_decors or (x, y) in lisiere:
        return False
    decors[y][x] = t("fleurs", rng.choice(FLEURS))
    occupe_decors.add((x, y))
    return True


essayer(90, lambda x, y: fleur(x, y, lambda x, y: 6 <= x <= 32 and 38 <= y <= 54))
essayer(14, lambda x, y: fleur(x, y, lambda x, y: 42 <= x <= 55 and 21 <= y <= 31))

# Nénuphars, rochers et plantes dans l'eau (pas sur les bords)
eau_libre = [(x, y) for x, y in eau if all((x + dx, y + dy) in eau for dx in (-1, 0, 1) for dy in (-1, 0, 1))]
for x, y in rng.sample(eau_libre, min(len(eau_libre), 18)):
    decors[y][x] = t("decors", rng.choice([30, 30, 31, 32, 39, 39, 42, 43, 51, 52, 33, 34]))

# --- Écriture du .tmx ------------------------------------------------------------------------
os.makedirs(SORTIE, exist_ok=True)
Image.new("RGBA", (16, 16), (230, 40, 40, 255)).save(os.path.join(SORTIE, "collisions.png"))
relatif = os.path.relpath(PACK, SORTIE).replace("\\", "/")


def csv(grille):
    return ",\n".join(",".join(str(v) for v in ligne) for ligne in grille)


collisions = [[GID_COLLISION if bloque[y][x] else 0 for x in range(L)] for y in range(H)]
morceaux = [
    '<?xml version="1.0" encoding="UTF-8"?>',
    f'<map version="1.10" tiledversion="1.12.2" orientation="orthogonal" renderorder="right-down" width="{L}" '
    f'height="{H}" tilewidth="16" tileheight="16" infinite="0" nextlayerid="6" nextobjectid="2">',
]
for nom, chemin_, w, h in JEUX:
    morceaux.append(f' <tileset firstgid="{PREMIER[nom]}" name="{nom}" tilewidth="16" tileheight="16" '
                    f'tilecount="{(w // 16) * (h // 16)}" columns="{w // 16}">')
    morceaux.append(f'  <image source="{relatif}/{chemin_}" width="{w}" height="{h}"/>')
    morceaux.append(' </tileset>')
morceaux.append(f' <tileset firstgid="{GID_COLLISION}" name="collisions" tilewidth="16" tileheight="16" '
                f'tilecount="1" columns="1">')
morceaux.append('  <image source="collisions.png" width="16" height="16"/>')
morceaux.append(' </tileset>')
for i, (nom, grille, extra) in enumerate([("Sol", sol, ""), ("Décors", decors, ""), ("Devant", devant, ""),
                                         ("Collisions", collisions, ' visible="0" opacity="0.45"')], 1):
    morceaux.append(f' <layer id="{i}" name="{nom}" width="{L}" height="{H}"{extra}>')
    morceaux.append(f'  <data encoding="csv">\n{csv(grille)}\n</data>')
    morceaux.append(' </layer>')
dx, dy, dl, dh = DEPART
morceaux.append(' <objectgroup id="5" name="Zones">')
morceaux.append(f'  <object id="1" name="Départ" x="{dx * 16}" y="{dy * 16}" width="{dl * 16}" height="{dh * 16}"/>')
morceaux.append(' </objectgroup>')
morceaux.append('</map>')
with open(os.path.join(SORTIE, "Parc_1.tmx"), "w", encoding="utf-8") as f:
    f.write("\n".join(morceaux) + "\n")

# --- Aperçu (pour vérifier, pas pour Tiled) --------------------------------------------------
planches = {nom: Image.open(os.path.join(PACK, c)).convert("RGBA") for nom, c, _, _ in JEUX}
apercu = Image.new("RGBA", (L * 16, H * 16), (0, 0, 0, 255))


def dessiner(grille):
    for y in range(H):
        for x in range(L):
            g = grille[y][x]
            if not g or g >= GID_COLLISION:
                continue
            nom = max((n for n in PREMIER if PREMIER[n] <= g), key=lambda n: PREMIER[n])
            i, cols = g - PREMIER[nom], colonnes(nom)
            tuile = planches[nom].crop(((i % cols) * 16, (i // cols) * 16, (i % cols) * 16 + 16, (i // cols) * 16 + 16))
            apercu.alpha_composite(tuile, (x * 16, y * 16))


for grille in (sol, decors, devant):
    dessiner(grille)
apercu.save(os.path.join(sys.argv[3] if len(sys.argv) > 3 else SORTIE, "apercu.png"))
voile = apercu.copy()
for y in range(H):
    for x in range(L):
        if bloque[y][x]:
            voile.alpha_composite(Image.new("RGBA", (16, 16), (230, 40, 40, 110)), (x * 16, y * 16))
voile.save(os.path.join(sys.argv[3] if len(sys.argv) > 3 else SORTIE, "apercu_collisions.png"))
print("ok", len(eau), "cases d'eau,", len(chemin), "de chemin,", sum(map(sum, bloque)), "bloquées")
