"""
Fond du « Parc » : une clairière assemblée avec les tuiles du pack « Tiny Tales 2D Overworld » (Megatiles, artiste
Rayane Félix), écrite dans MonsutaPotaru/wwwroot/images/parc/clairiere.png.

Carte de 24 × 20 tuiles de 16 px (384 × 320 : l'ancienne boîte PC du parc, 576 × 486, en pixels de sprite) : herbe,
chemin qui serpente dans le bas (avec --embranchement : un chemin qui monte et traverse la forêt), lisière d'arbres en
haut (2 à 4 rangées, trouée là où passe le chemin), fleurs, rochers et buissons bas dans la clairière. Elle se raccorde à
gauche et à droite : le parc la répète en largeur sans couture.

Le pack n'est pas libre de redistribution (ni ses fichiers ni la carte assemblée dans le dépôt Git) : la carte reste hors
de Git comme les autres images, envoyée sur le serveur par deploy/fond-parc.cmd. Ce script lit le dossier « Tiled » du
pack (Images/*.png et Set_A_Hills1.tsx), à récupérer chez Megatiles.

Lancement (Python 3 et Pillow : pip install pillow) :
  python outils/clairiere.py "<pack>/Tiled" [--graine 3] [--embranchement] [--sortie <dossier>]
La graine 3, sans embranchement, donne la carte choisie (une autre graine : autre chemin, autre lisière).
"""
import math
import os
import random
import re
import sys

from PIL import Image

args = sys.argv[1:]
def option(name, default):
    if name in args:
        i = args.index(name)
        value = args[i + 1]
        del args[i:i + 2]
        return value
    return default
SEED = int(option("--graine", "3"))
BRANCH = "--embranchement" in args   # chemin qui monte dans la forêt : revient à chaque répétition de la carte, pas retenu
if BRANCH:
    args.remove("--embranchement")
OUT = option("--sortie", os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "MonsutaPotaru", "wwwroot", "images", "parc"))
if len(args) != 1:
    sys.exit('Usage : python outils/clairiere.py "<pack Tiny Tales>/Tiled" [--graine 3] [--embranchement] [--sortie <dossier>]')
PACK = args[0]

W, H = 24, 20   # en tuiles : 384 × 320 px (576 × 480 unités dans le parc, chaque pixel ×1,5 comme les sprites)
T = 16
rng = random.Random(SEED)


def sheet(name):
    im = Image.open(os.path.join(PACK, "Images", name)).convert("RGBA")
    px = im.load()
    for y in range(im.height):            # couleur de transparence des .tsx (trans="ff01fe")
        for x in range(im.width):
            if px[x, y][:3] == (0xff, 0x01, 0xfe):
                px[x, y] = (0, 0, 0, 0)
    return im


HILLS, EXT = sheet("Set_A_Hills1.png"), sheet("Set_B_Exteriors.png")
def tile(im, i, w=1, h=1):
    x, y = (i % 16) * T, (i // 16) * T
    return im.crop((x, y, x + w * T, y + h * T))


# Tuiles par coins (haut gauche, haut droit, bas gauche, bas droit), lues dans le .tsx.
def corner_tiles(terrain):
    t = open(os.path.join(PACK, "Set_A_Hills1.tsx"), encoding="utf-8").read()
    table = {}
    for tid, ter in re.findall(r'<tile id="(\d+)" terrain="([^"]*)"', t):
        parts = ter.split(",")
        if any(parts) and all(p in ("", terrain) for p in parts):
            table[tuple(p == terrain for p in parts)] = int(tid)
    return table
GRASS, ROAD = corner_tiles("2"), corner_tiles("3")
SAND = 20


# Bruit lisse et périodique en x (la carte se raccorde à gauche et à droite).
def wave(x, parts):
    return sum(a * math.sin(2 * math.pi * (k * x / W) + p) for k, a, p in parts)
def rand_wave(n, amp):
    return [(k, amp / k ** 0.7 * rng.uniform(0.5, 1), rng.uniform(0, 6.3)) for k in range(1, n + 1)]


# ---------- Sol : coins herbe (G), chemin (R), sable (S) ----------
CW, CH = W, H + 1   # coins : W en largeur (périodique), H + 1 en hauteur
corner = [["G"] * CW for _ in range(CH)]
path_wave = rand_wave(3, 1.2)
def path_y(x): return H * 0.72 + wave(x, path_wave)   # chemin qui serpente dans le bas de la clairière
for x in range(CW):
    yc = path_y(x)
    for y in range(CH):
        if abs(y - yc) <= 0.6:
            corner[y][x] = "R"
# Embranchement qui monte vers la forêt.
bx = rng.randrange(W)   # tiré même sans embranchement : une graine donne la même carte, au chemin près
for y in range(0, int(path_y(bx)) + 1 if BRANCH else 0):   # jusqu'en haut : il traverse la forêt
    corner[y][(bx + int(1.2 * math.sin(y * 0.7))) % W] = "R"
# Pas de chemin en diagonale (deux coins opposés) : la jonction laisserait un trou de sable.
changed = True
while changed:
    changed = False
    for y in range(CH - 1):
        for x in range(CW):
            c = [corner[y][x], corner[y][(x + 1) % W], corner[y + 1][x], corner[y + 1][(x + 1) % W]]
            if (c[0] == c[3] == "R" and c[1] != "R" and c[2] != "R") or (c[1] == c[2] == "R" and c[0] != "R" and c[3] != "R"):
                corner[y][(x + 1) % W] = "R" if c[0] == "R" else corner[y][(x + 1) % W]
                if c[1] == "R":
                    corner[y][x] = "R"
                changed = True
    for y in range(1, CH - 1):   # coin d'herbe pris entre deux coins de chemin : bouché
        for x in range(CW):
            if corner[y][x] != "R" and ((corner[y][x - 1] == corner[y][(x + 1) % W] == "R") or (corner[y - 1][x] == corner[y + 1][x] == "R")):
                corner[y][x] = "R"
                changed = True

img = Image.new("RGBA", (W * T, H * T))
for ty in range(H):
    for tx in range(W):
        cs = (corner[ty][tx], corner[ty][(tx + 1) % W], corner[ty + 1][tx], corner[ty + 1][(tx + 1) % W])
        img.alpha_composite(tile(HILLS, SAND), (tx * T, ty * T))
        for kind, table in (("G", GRASS), ("R", ROAD)):
            key = tuple(c == kind for c in cs)
            if any(key):
                img.alpha_composite(tile(HILLS, table[key]), (tx * T, ty * T))

# ---------- Objets : dessinés du fond vers l'avant (par le bas de l'objet) ----------
objects = []   # (y du bas en px, x px, image)
def put(x, y, im):   # x, y : coin haut gauche en px ; répété à gauche et à droite pour le raccord
    objects.append((y + im.height, x, im))


def free(tx, ty, w=1, h=1):
    return all(0 <= ty + j < H and all(corner[ty + j + dy][(tx + i + dx) % W] == "G" for dy in (0, 1) for dx in (0, 1))
               for i in range(w) for j in range(h))
used = set()
def take(tx, ty, w, h):
    cells = {((tx + i) % W, ty + j) for i in range(w) for j in range(h)}
    if cells & used:
        return False
    used.update(cells)
    return True


# Lisière : arbres serrés en quinconce, profondeur variable (2 à 4 rangées), quelques sapins.
TREES = [tile(EXT, 64, 2, 2)] * 5 + [tile(EXT, 68, 2, 2)] * 2
PINE = tile(EXT, 72, 1, 2)
edge_wave = rand_wave(4, 0.9)
def edge(x): return max(2.2, 2.6 + wave(x, edge_wave))   # nombre de rangées d'arbres (jamais de trou dans la lisière)
for row in range(-1, 5):
    for col in range(0, W, 2):
        x = col + (row % 2)
        near_road = any(corner[y][(x + dx) % W] == "R" for y in range(max(0, row), min(CH, row + 3)) for dx in range(0, 3))
        if row <= edge(x + 1) - 1 + rng.uniform(-0.3, 0.3) and not near_road:   # trouée là où passe le chemin
            put(x * T + rng.randint(-1, 1), row * T + rng.randint(-1, 1), rng.choice(TREES))
            for i in range(2):
                for j in range(2):
                    used.add(((x + i) % W, row + j))
# Devant la lisière : buissons, sapins, souches.
EDGE_DECOR = [(tile(EXT, 2), 1, 1)] * 4 + [(tile(EXT, 1), 1, 1)] * 2 + [(tile(EXT, 16), 1, 1)] + \
             [(tile(EXT, 38), 1, 1)] * 2 + [(PINE, 1, 2)] * 2 + [(tile(EXT, 50), 1, 1), (tile(EXT, 51), 1, 1)] + \
             [(tile(EXT, 62, 2, 1), 2, 1)]
for x in range(W):
    if rng.random() < 0.22:
        im, w, h = rng.choice(EDGE_DECOR)
        y = int(edge(x + 0.5)) + 1 + rng.randint(0, 1) - (h - 1)
        if free(x, y, w, h) and take(x, y, w, h):
            put(x * T, y * T, im)

# Clairière : massifs de fleurs, quelques rochers et buissons isolés (rien de haut : les Pokémon y marchent).
FLOWERS = [tile(EXT, 48)] * 6 + [tile(EXT, 32), tile(EXT, 33), tile(EXT, 34)]   # surtout des fleurs jaunes, peu de champignons
ROCKS = [tile(HILLS, 70), tile(HILLS, 71), tile(HILLS, 86), tile(HILLS, 87)]
def count(per_96x20):   # même densité quelle que soit la taille de la carte (réglée sur une carte de 96 × 20)
    return max(1, round(per_96x20 * W * H / (96 * 20)))
for _ in range(count(10)):    # massifs : 2 à 4 fleurs d'une même sorte
    kind = rng.choice(FLOWERS)
    cx, cy = rng.randrange(W), rng.randrange(5, H)
    for _ in range(rng.randint(2, 4)):
        x, y = cx + rng.randint(-2, 2), cy + rng.randint(-1, 1)
        if y < H and free(x, y) and take(x, y, 1, 1):
            put((x % W) * T, y * T, kind)
for _ in range(count(9)):
    x, y = rng.randrange(W), rng.randrange(6, H)
    if free(x, y) and take(x, y, 1, 1):
        put(x * T, y * T, rng.choice(ROCKS))
for _ in range(count(6)):
    x, y = rng.randrange(W), rng.randrange(6, H - 1)
    if free(x, y) and take(x, y, 1, 1):
        put(x * T, y * T, rng.choice([tile(EXT, 2), tile(EXT, 38), tile(EXT, 50)]))


def blit(dst, im, x, y):   # alpha_composite refuse ce qui dépasse : on découpe à la main
    sx, sy = max(0, -x), max(0, -y)
    dx, dy = x + sx, y + sy
    if dx < dst.width and dy < dst.height and sx < im.width and sy < im.height:
        dst.alpha_composite(im.crop((sx, sy, min(im.width, sx + dst.width - dx), min(im.height, sy + dst.height - dy))), (dx, dy))
for bottom, x, im in sorted(objects, key=lambda o: o[0]):
    for shift in (-W * T, 0, W * T):
        blit(img, im, x + shift, bottom - im.height)

os.makedirs(OUT, exist_ok=True)
img.convert("RGB").save(os.path.join(OUT, "clairiere.png"))
print("Clairière écrite :", os.path.normpath(os.path.join(OUT, "clairiere.png")), img.size)
