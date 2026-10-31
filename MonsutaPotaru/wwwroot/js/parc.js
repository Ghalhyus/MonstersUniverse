// Parc : les Pokémon s'y promènent avec les sprites Donjon Mystère de SpriteCollab (animations Idle, Walk et Sleep), sur
// une carte faite dans Tiled avec le pack Cute Fantasy (outils/carte-parc.py), affichée par Phaser (moteur de jeu 2D).
// Tout se passe dans le navigateur ; le serveur ne donne que la liste des Pokémon (Park.razor) et est prévenu d'un clic sur
// l'un d'eux.
//
// Comportement : chaque Pokémon a une énergie (baisse en marchant, remonte en dormant : il s'endort quand il est fatigué,
// plus tôt la nuit), une intention (explorer un endroit, rejoindre un Pokémon de sa famille, observer autour de lui, aller
// dormir près d'un congénère), un caractère tiré de sa nature (plus ou moins actif, dormeur, sociable) et de la curiosité
// pour le pointeur de la souris. Il contourne les obstacles (calque « Collisions » de la carte) et reste dans la zone
// « Parc » (la taille achetée : forêt dense autour, assombrie) ; on la parcourt en faisant glisser la vue.
import * as Phaser from "../lib/phaser/phaser.esm.min.js";

// Carte : coordonnées du parc en pixels de la carte (16 par tuile). La hauteur affichée est celle de la zone « Départ »
// (320 pixels), à un nombre entier de pixels d'écran par pixel de carte (dessin net). Une carte par taille du parc (1 à 4,
// agrandissements achetés + 1, ParkService), toutes de la même taille : seule la zone « Parc » et la forêt autour changent.
const MAP_URL = size => `parc/parc-1-${size}.json`;
const VIEW_MARGIN = 6; // cases de forêt qu'on peut voir au-delà de la zone « Parc » en faisant glisser la vue
const TILES_URL = "images/parc/tuiles/"; // planches du pack, hors de Git (deploy/fond-parc.cmd)
const TILE = 16;
const GRASS = 0x33984b; // couleur de l'herbe, sans les planches (absentes, ou pas encore chargées)
const OUTSIDE = "#132a1c"; // autour de la carte, quand on a dézoomé plus large qu'elle
// Icônes des boutons sur la carte (Bootstrap Icons : dash-lg, plus-lg, fullscreen, fullscreen-exit)
const svg = path => `<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16" aria-hidden="true"><path fill-rule="evenodd" d="${path}"/></svg>`;
const ICONS = {
    minus: svg("M2 8a.5.5 0 0 1 .5-.5h11a.5.5 0 0 1 0 1h-11A.5.5 0 0 1 2 8"),
    plus: svg("M8 2a.5.5 0 0 1 .5.5v5h5a.5.5 0 0 1 0 1h-5v5a.5.5 0 0 1-1 0v-5h-5a.5.5 0 0 1 0-1h5v-5A.5.5 0 0 1 8 2"),
    expand: svg("M1.5 1a.5.5 0 0 0-.5.5v4a.5.5 0 0 1-1 0v-4A1.5 1.5 0 0 1 1.5 0h4a.5.5 0 0 1 0 1zM10 .5a.5.5 0 0 1 .5-.5h4A1.5 1.5 0 0 1 16 1.5v4a.5.5 0 0 1-1 0v-4a.5.5 0 0 0-.5-.5h-4a.5.5 0 0 1-.5-.5M.5 10a.5.5 0 0 1 .5.5v4a.5.5 0 0 0 .5.5h4a.5.5 0 0 1 0 1h-4A1.5 1.5 0 0 1 0 14.5v-4a.5.5 0 0 1 .5-.5m15 0a.5.5 0 0 1 .5.5v4a1.5 1.5 0 0 1-1.5 1.5h-4a.5.5 0 0 1 0-1h4a.5.5 0 0 0 .5-.5v-4a.5.5 0 0 1 .5-.5"),
    shrink: svg("M5.5 0a.5.5 0 0 1 .5.5v4A1.5 1.5 0 0 1 4.5 6h-4a.5.5 0 0 1 0-1h4a.5.5 0 0 0 .5-.5v-4a.5.5 0 0 1 .5-.5m5 0a.5.5 0 0 1 .5.5v4a.5.5 0 0 0 .5.5h4a.5.5 0 0 1 0 1h-4A1.5 1.5 0 0 1 10 4.5v-4a.5.5 0 0 1 .5-.5M0 10.5a.5.5 0 0 1 .5-.5h4A1.5 1.5 0 0 1 6 11.5v4a.5.5 0 0 1-1 0v-4a.5.5 0 0 0-.5-.5h-4a.5.5 0 0 1-.5-.5m10 1a1.5 1.5 0 0 1 1.5-1.5h4a.5.5 0 0 1 0 1h-4a.5.5 0 0 0-.5.5v4a.5.5 0 0 1-1 0z"),
};
// Hors de la zone « Parc » : de plus en plus sombre, une marche par case à partir de la deuxième (la lisière reste claire)
const FOG = { color: 0x0b1222, alpha: 0.18, steps: 4 };
// Profondeur d'affichage d'un Pokémon ou d'un feuillage, selon la ligne de ses pieds (de son tronc) : les plus bas devant.
// Entre 2 et 3 : au-dessus de la carte (0) et des ombres (1,9), sous le brouillard (3) et la nuit (4).
const depthOf = y => 2 + y / 10000;
const NIGHT = { color: 0x101848, alpha: 0.38 };
// Taille d'un pixel de sprite par rapport à un pixel de la carte (deux tiers : ×2 quand la carte est à ×3), arrondie à un
// nombre entier de pixels d'écran (dessin net), jamais moins de 2 pixels d'écran tant que la carte en a autant.
const SPRITE_SIZE = 2 / 3;
const WALK_SPEED = 27; // vitesse de marche (pixels de la carte par seconde), selon le caractère
const WALK_ANIMATION_SPEED = 30; // vitesse à laquelle l'animation de marche joue à son rythme normal (pieds sans glisser),
                                 // pour des sprites à la taille des pixels de la carte (plus petits : foulée plus courte)
const DRAG_THRESHOLD = 6; // pixels (CSS) avant qu'un clic devienne un glissement de la vue (ou soulève un Pokémon)
const LONG_PRESS = 300; // ms d'appui sans bouger, au doigt, pour soulever un Pokémon (sinon le doigt fait glisser la vue)
const LIFT = 8; // hauteur (pixels de la carte) d'un Pokémon soulevé au-dessus de son ombre
const HELD_DEPTH = 3.5; // un Pokémon soulevé passe devant tout (feuillages, brouillard), sous la nuit (4)
const EDGE_SCROLL = { margin: 48, speed: 420 }; // en le portant près du bord (pixels CSS) : la vue défile (pixels CSS / s)
const ANIMATIONS = ["Idle", "Walk", "Sleep", "Charge"];
const OPTIONAL = new Set(["Charge"]); // planche absente (images d'avant, sprite incomplet) : Idle à la place
const ANIMATION_OF = { idle: "Idle", walk: "Walk", sleep: "Sleep", held: "Charge" }; // selon l'état du Pokémon
// Énergie (par seconde) : 3 à 5 minutes d'activité le jour avant une sieste d'environ une minute ; la nuit, ils
// s'endorment plus tôt et récupèrent lentement (longues nuits).
const ENERGY = { walk: 0.004, idle: 0.0012, sleepDay: 0.012, sleepNight: 0.004 };

// Lignes des planches de SpriteCollab : bas, bas-droite, droite, haut-droite, haut, haut-gauche, gauche, bas-gauche.
const FRONT = 0;
const directionOf = (dx, dy) => (((Math.round((90 - Math.atan2(dy, dx) * 180 / Math.PI) / 45)) % 8) + 8) % 8;
const vectorOf = dir => { const a = (90 - dir * 45) * Math.PI / 180; return [Math.cos(a), Math.sin(a)]; };

const random = (min, max) => min + Math.random() * (max - min);
const chance = (perSecond, dt) => Math.random() < perSecond * dt; // probabilité par seconde, ramenée au pas de temps
const clamp = (v, min, max) => Math.max(min, Math.min(max, v));
const distance = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);

/** Animations d'un dossier de sprites (AnimData.xml + planches), chargées une fois par dossier. */
const sheets = new Map();
function loadSheets(folder) {
    if (!sheets.has(folder)) {
        sheets.set(folder, (async () => {
            const response = await fetch(folder + "AnimData.xml");
            if (!response.ok) throw new Error(`Sprites absents : ${folder}`);
            const xml = new DOMParser().parseFromString(await response.text(), "text/xml");
            const defs = new Map([...xml.querySelectorAll("Anim")].map(a => [a.querySelector("Name").textContent, a]));
            const loaded = await Promise.all(ANIMATIONS.map(name => loadAnimation(folder, defs, name))); // planches en parallèle
            const result = {};
            ANIMATIONS.forEach((name, i) => { if (loaded[i]) result[name] = loaded[i]; });
            result.Idle ??= result.Walk; // au cas où une planche manquerait
            if (!result.Idle) throw new Error(`Ni Idle ni Walk : ${folder}`); // ce Pokémon n'apparaît pas (setPokemon)
            result.Walk ??= result.Idle;
            result.Sleep ??= result.Idle;
            result.Charge ??= result.Idle; // soulevé
            return result;
        })());
    }
    return sheets.get(folder);
}

/** Une animation d'un dossier de sprites (définitions de AnimData.xml), ou null si elle manque. */
async function loadAnimation(folder, defs, name) {
    let def = defs.get(name);
    while (def?.querySelector("CopyOf")) def = defs.get(def.querySelector("CopyOf").textContent); // animation copiée d'une autre
    if (!def) return null;
    const source = def.querySelector("Name").textContent;
    const image = new Image();
    image.src = `${folder}${source}-Anim.png`;
    try {
        await image.decode();
    } catch (e) {
        if (OPTIONAL.has(name)) return null;
        throw e;
    }
    const fw = +def.querySelector("FrameWidth").textContent, fh = +def.querySelector("FrameHeight").textContent;
    return {
        image, fw, fh,
        key: image.src, // texture de Phaser
        columns: Math.max(1, Math.round(image.width / fw)),
        rows: Math.max(1, Math.round(image.height / fh)),
        durations: [...def.querySelectorAll("Duration")].map(d => +d.textContent * 1000 / 60), // images à 60 par seconde
    };
}

/** Heure de la nuit : 0 le jour, 1 la nuit (22 h – 6 h), progressif le soir (19 h – 22 h) et à l'aube (6 h – 8 h). */
function nightLevel(date = new Date()) {
    const h = date.getHours() + date.getMinutes() / 60;
    if (h >= 22 || h < 6) return 1;
    if (h >= 19) return (h - 19) / 3;
    if (h < 8) return 1 - (h - 6) / 2;
    return 0;
}

/** Caractère tiré de la nature (index PKHeX : statistique augmentée = n / 5, diminuée = n % 5 ; 2 = Vitesse, 4 = Déf. Spé.). */
function personalityOf(nature) {
    const up = Math.floor(nature / 5), down = nature % 5, neutral = up === down;
    return {
        activity: clamp(1 + (neutral ? 0 : (up === 2) * 0.3 - (down === 2) * 0.3) + random(-0.1, 0.1), 0.6, 1.4),
        sleepy: clamp((neutral ? 0 : (down === 2) * 0.12 - (up === 2) * 0.08) + random(-0.05, 0.05), -0.1, 0.2),
        sociable: clamp(0.55 + (neutral ? 0 : (up === 4) * 0.25 - (down === 4) * 0.2) + random(-0.1, 0.1), 0.2, 0.95),
        curious: random(0.3, 1),
    };
}

/**
 * Terrain de la carte : cases où l'on peut marcher (dans la zone « Parc », sans tuile du calque « Collisions »), et
 * chemins entre deux points (A* sur les cases, en diagonale seulement si les deux côtés sont libres, puis lissé en
 * lignes droites). Sans carte : un pré de 24 × 20 cases, sans obstacle.
 */
class Ground {
    constructor(data) {
        this.data = data;
        this.columns = data?.width ?? 24;
        this.lines = data?.height ?? 20;
        const zones = data?.layers.find(l => l.name === "Zones")?.objects ?? [];
        const tiles = zone => zone && { x: Math.round(zone.x / TILE), y: Math.round(zone.y / TILE), w: Math.round(zone.width / TILE), h: Math.round(zone.height / TILE) };
        // Vue de départ (sa hauteur remplit l'écran) ; partie de la carte où vivent les Pokémon (le reste est assombri)
        this.start = tiles(zones.find(o => o.name === "Départ")) ?? { x: 0, y: 0, w: this.columns, h: this.lines };
        this.area = tiles(zones.find(o => o.name === "Parc")) ?? this.start;
        const collisions = data?.layers.find(l => l.name === "Collisions" && l.type === "tilelayer")?.data ?? [];
        this.walkable = new Uint8Array(this.columns * this.lines);
        this.free = []; // cases libres, pour tirer un endroit au hasard
        const { x: sx, y: sy, w: sw, h: sh } = this.area;
        for (let y = sy; y < sy + sh; y++) {
            for (let x = sx; x < sx + sw; x++) {
                const i = y * this.columns + x;
                if (x < 0 || y < 0 || x >= this.columns || y >= this.lines || collisions[i]) continue;
                this.walkable[i] = 1;
                this.free.push(i);
            }
        }
        // Recherche de chemin : tableaux gardés d'une recherche à l'autre (pas de déchets à chaque décision)
        const n = this.columns * this.lines;
        this.cost = new Float64Array(n);
        this.previous = new Int32Array(n);
        this.seen = new Uint32Array(n); // numéro de la recherche qui a vu la case (évite de tout remettre à zéro)
        this.closed = new Uint32Array(n);
        // Cases à explorer : tas binaire (la plus prometteuse d'abord), une case peut y être plusieurs fois
        this.heap = new Int32Array(8 * n + 1);
        this.heapScore = new Float64Array(8 * n + 1);
        this.search = 0;
    }

    get width() { return this.columns * TILE; }
    get height() { return this.lines * TILE; }

    tileFree(tx, ty) { return tx >= 0 && ty >= 0 && tx < this.columns && ty < this.lines && this.walkable[ty * this.columns + tx] === 1; }
    isFree(x, y) { return this.tileFree(Math.floor(x / TILE), Math.floor(y / TILE)); }
    center(i) { return { x: (i % this.columns) * TILE + TILE / 2, y: Math.floor(i / this.columns) * TILE + TILE / 2 }; }

    /** Endroit libre au hasard (un peu décalé du centre de sa case). */
    randomSpot() {
        if (!this.free.length) return { x: this.width / 2, y: this.height / 2 };
        const c = this.center(this.free[Math.floor(Math.random() * this.free.length)]);
        return { x: c.x + random(-4, 4), y: c.y + random(-4, 4) };
    }

    /** Le point lui-même s'il est libre, sinon le centre de la case libre la plus proche. */
    nearestFree(x, y) {
        if (this.isFree(x, y)) return { x, y };
        let best = -1, bestDistance = Infinity;
        for (const i of this.free) {
            const cx = (i % this.columns) * TILE + TILE / 2, cy = Math.floor(i / this.columns) * TILE + TILE / 2;
            const d = (cx - x) ** 2 + (cy - y) ** 2;
            if (d < bestDistance) { best = i; bestDistance = d; }
        }
        return best < 0 ? { x, y } : this.center(best);
    }

    /** Ligne droite sans obstacle (vérifiée tous les 4 pixels). */
    lineFree(a, b) {
        const steps = Math.ceil(distance(a, b) / 4);
        for (let s = 1; s <= steps; s++) {
            if (!this.isFree(a.x + (b.x - a.x) * s / steps, a.y + (b.y - a.y) * s / steps)) return false;
        }
        return true;
    }

    /** Points de passage de <from> à <to> (sans <from>), ou null s'il n'y a pas de chemin. */
    path(from, to) {
        if (this.lineFree(from, to)) return [to];
        const start = this.nearestFree(from.x, from.y);
        const startTile = Math.floor(start.y / TILE) * this.columns + Math.floor(start.x / TILE);
        const goalTile = Math.floor(to.y / TILE) * this.columns + Math.floor(to.x / TILE);
        if (!this.explore(startTile, goalTile)) return null;
        const tiles = [];
        for (let t = goalTile; t !== startTile; t = this.previous[t]) tiles.unshift(t);
        return this.smooth([start, ...tiles.slice(0, -1).map(t => this.center(t)), to]);
    }

    /** Recherche A* de <startTile> à <goalTile> : vrai si elle aboutit (chemin dans previous, depuis l'arrivée). */
    explore(startTile, goalTile) {
        const gx = goalTile % this.columns, gy = Math.floor(goalTile / this.columns);
        const columns = this.columns, cost = this.cost, previous = this.previous, seen = this.seen, closed = this.closed;
        const heap = this.heap, score = this.heapScore;
        let size = 0;
        const push = (tile, value) => {
            let i = size++;
            while (i > 0) {
                const parent = (i - 1) >> 1;
                if (score[parent] <= value) break;
                heap[i] = heap[parent]; score[i] = score[parent]; i = parent;
            }
            heap[i] = tile; score[i] = value;
        };
        const pop = () => {
            const top = heap[0], tile = heap[--size], value = score[size];
            let i = 0;
            for (let child = 1; child < size; child = 2 * i + 1) {
                if (child + 1 < size && score[child + 1] < score[child]) child++;
                if (score[child] >= value) break;
                heap[i] = heap[child]; score[i] = score[child]; i = child;
            }
            heap[i] = tile; score[i] = value;
            return top;
        };
        const estimate = i => {
            const dx = Math.abs(i % columns - gx), dy = Math.abs(Math.floor(i / columns) - gy);
            return cost[i] + Math.max(dx, dy) + (Math.SQRT2 - 1) * Math.min(dx, dy);
        };
        const search = ++this.search;
        let current = startTile;
        const visit = (next, step) => {
            const c = cost[current] + step;
            if (closed[next] === search || (seen[next] === search && c >= cost[next])) return;
            seen[next] = search;
            cost[next] = c;
            previous[next] = current;
            push(next, estimate(next));
        };
        cost[startTile] = 0;
        seen[startTile] = search;
        push(startTile, estimate(startTile));
        while (size) {
            current = pop();
            if (closed[current] === search) continue; // déjà exploré par un chemin plus court
            if (current === goalTile) return true;
            closed[current] = search;
            this.forEachNeighbour(current, visit);
        }
        return false;
    }

    /** Appelle visit(case, coût du pas) pour chaque case voisine où l'on peut aller. */
    forEachNeighbour(tile, visit) {
        const columns = this.columns, cx = tile % columns, cy = Math.floor(tile / columns);
        for (let dy = -1; dy <= 1; dy++) {
            for (let dx = -1; dx <= 1; dx++) {
                if (this.canStep(cx, cy, dx, dy)) visit((cy + dy) * columns + cx + dx, dx && dy ? Math.SQRT2 : 1);
            }
        }
    }

    /** Pas de (cx, cy) vers la case voisine (cx + dx, cy + dy) : libre, et sans couper de coin en diagonale. */
    canStep(cx, cy, dx, dy) {
        if ((!dx && !dy) || !this.tileFree(cx + dx, cy + dy)) return false;
        return !dx || !dy || (this.tileFree(cx + dx, cy) && this.tileFree(cx, cy + dy));
    }

    /** Lissage : du point actuel, aller tout droit tant qu'on voit le point de passage suivant. */
    smooth(points) {
        const result = [];
        for (let i = 0; i < points.length - 1;) {
            let j = i + 1;
            while (j + 1 < points.length && this.lineFree(points[i], points[j + 1])) j++;
            result.push(points[j]);
            i = j;
        }
        return result;
    }
}

class Pokemon {
    constructor(info, anims, ground, x, y) {
        Object.assign(this, { id: info.id, family: info.family, species: info.species, nickname: info.nickname, anims, ground, x, y });
        this.personality = personalityOf(info.nature);
        this.dir = Math.floor(random(0, 8));
        this.state = "idle";
        this.timer = random(0.5, 3);
        this.lookTimer = random(1, 3);
        this.energy = random(0.35, 1);
        this.frame = 0;
        this.frameTime = 0;
        this.target = null;
        this.path = [];
        this.intent = null;
        this.progress = { time: 0, distance: Infinity };
        this.zzz = 0;
        this.vx = 0; // vitesse actuelle : accélère, tourne et freine progressivement
        this.vy = 0;
    }

    get anim() { return this.anims[ANIMATION_OF[this.state]]; }

    setState(state) {
        if (state === this.state) return;
        this.state = state;
        this.frame = 0;
        this.frameTime = 0;
    }

    /** Part vers un endroit (le plus proche où l'on peut aller), en contournant les obstacles. */
    walkTo(x, y, intent) {
        const target = this.ground.nearestFree(x, y);
        const path = this.ground.path(this, target);
        if (!path) { this.rest(random(1, 2)); return; }
        this.target = target;
        this.path = path;
        this.intent = intent;
        this.progress = { time: 0, distance: this.remaining() };
        this.setState("walk");
    }

    /** Distance qu'il reste à parcourir, en suivant le chemin. */
    remaining() {
        let total = 0, from = { x: this.x, y: this.y };
        for (const point of this.path) { total += distance(from, point); from = point; }
        return total;
    }

    rest(seconds) {
        this.vx = this.vy = 0;
        this.target = null;
        this.path = [];
        this.intent = null;
        this.timer = seconds;
        this.setState("idle");
    }

    sleep() {
        this.vx = this.vy = 0;
        this.target = null;
        this.path = [];
        this.intent = null;
        this.setState("sleep");
        this.wakeAt = random(0.85, 1);
    }

    face(other) { this.dir = directionOf(other.x - this.x, other.y - this.y); }

    /** Avance l'animation ; la marche joue au rythme de la vitesse (les pieds ne glissent pas, ne patinent pas). */
    advanceFrame(dtMs, size) {
        const anim = this.anim;
        const rate = this.state === "walk" ? clamp(Math.hypot(this.vx, this.vy) / (WALK_ANIMATION_SPEED * size), 0.35, 1.4) : 1;
        this.frameTime += dtMs * rate;
        while (this.frameTime >= anim.durations[this.frame % anim.durations.length]) {
            this.frameTime -= anim.durations[this.frame % anim.durations.length];
            this.frame = (this.frame + 1) % anim.durations.length;
        }
    }
}

/**
 * Parc dans <container> ; <component> (Park.razor) est prévenu d'un clic sur un Pokémon. Options : canMove (le
 * propriétaire peut soulever ses Pokémon et les poser ailleurs ; rien n'est enregistré), size (taille du parc, de 1 à 4 :
 * sa carte).
 */
export async function create(container, component, options) {
    const canMove = options?.canMove === true;
    const size = Math.max(1, Math.floor(Number(options?.size) || 1));
    let data = null;
    try { data = await (await fetch(MAP_URL(size))).json(); }
    catch { /* carte illisible : un pré sans obstacle */ }
    const ground = new Ground(data);
    const viewW = ground.start.w * TILE, viewH = ground.start.h * TILE; // la zone « Départ » remplit la hauteur

    let pokemon = [];
    let mouse = null; // position du pointeur dans le parc, null hors du parc
    let hovered = null;
    let drag = null; // glissement de la vue en cours
    let k = 1; // pixels d'écran par pixel de la carte (zoom de la caméra)
    let spriteK = 2; // pixels d'écran par pixel de sprite
    let spriteScale = SPRITE_SIZE; // pixels de la carte par pixel de sprite
    let scene = null, camera = null, night = null, label = null, labelText = null, marker = null;
    let held = null; // Pokémon soulevé : { p, from (sa place), dx, dy (écart au pointeur), pointer }

    // ---------- Taille : toute la largeur du conteneur ; en hauteur, un nombre entier de fois la zone « Départ » en
    // pixels d'écran (chaque pixel de la carte et des sprites couvre autant de pixels d'écran : dessin net), le plus grand
    // qui tient dans la hauteur visible sous le parc (sur un téléphone : pas plus que la zone entière en largeur). En plein
    // écran : tout l'écran, et le plus grand zoom où la zone « Départ » tient en hauteur ----------
    const isFullscreen = () => document.fullscreenElement === container;
    const measure = () => {
        const ratio = window.devicePixelRatio || 1;
        if (isFullscreen()) {
            const height = Math.round(container.clientHeight * ratio);
            return { ratio, zoom: Math.max(1, Math.floor(height / viewH)), width: Math.round(container.clientWidth * ratio), height };
        }
        const width = container.clientWidth || viewW;
        const top = container.getBoundingClientRect().top + window.scrollY;
        const visible = Math.max(240, window.innerHeight - top - 24) * ratio;
        const fitWidth = Math.round(width * ratio / viewW); // zone à peu près entière en largeur
        const zoom = Math.max(1, Math.round(240 * ratio / viewH), Math.min(Math.floor(visible / viewH), fitWidth));
        return { ratio, zoom, width: Math.round(width * ratio), height: zoom * viewH };
    };
    let maxZoom = 1; // zoom par défaut, et le plus grand permis (on dézoome de là jusqu'à ×1)
    const applyZoom = zoom => {
        k = zoom;
        spriteK = Math.max(Math.min(k, 2), Math.round(k * SPRITE_SIZE));
        spriteScale = spriteK / k;
        if (!scene) return;
        camera.setZoom(k);
        // Limites de la vue : la zone « Parc » et un peu de forêt autour (dans la carte), ou plus large et centrée quand la
        // vue l'est (la forêt continue jusqu'au bord de la carte)
        const vw = camera.width / k, vh = camera.height / k;
        const bx = Math.max(0, (ground.area.x - VIEW_MARGIN) * TILE), by = Math.max(0, (ground.area.y - VIEW_MARGIN) * TILE);
        const bw = Math.min(ground.width, (ground.area.x + ground.area.w + VIEW_MARGIN) * TILE) - bx;
        const bh = Math.min(ground.height, (ground.area.y + ground.area.h + VIEW_MARGIN) * TILE) - by;
        const cw = Math.max(bw, vw), ch = Math.max(bh, vh);
        camera.setBounds(bx + (bw - cw) / 2, by + (bh - ch) / 2, cw, ch);
        for (const p of pokemon) for (const text of p.texts) text.setResolution(k);
        labelText.setResolution(k);
        zoomOutButton.disabled = k <= 1;
        zoomInButton.disabled = k >= maxZoom;
    };
    /** Centre de la vue, en pixels de la carte (camera.midPoint n'est mis à jour qu'au dessin). */
    const viewCenter = () => ({ x: camera.scrollX + camera.width / 2, y: camera.scrollY + camera.height / 2 });
    /** Change le zoom (un nombre entier, de ×1 au zoom par défaut) en gardant fixe le point (en pixels du canvas) visé. */
    function zoomTo(zoom, sx = camera.width / 2, sy = camera.height / 2) {
        zoom = clamp(zoom, 1, maxZoom);
        if (zoom === k) return;
        const center = viewCenter();
        const wx = center.x + (sx - camera.width / 2) / k, wy = center.y + (sy - camera.height / 2) / k;
        applyZoom(zoom);
        camera.centerOn(wx - (sx - camera.width / 2) / k, wy - (sy - camera.height / 2) / k);
    }
    const resize = () => {
        if (!scene) return;
        const size = measure();
        const center = viewCenter();
        game.scale.setZoom(1 / size.ratio); // canvas en pixels physiques, affiché en pixels CSS
        game.scale.resize(size.width, size.height);
        if (size.zoom !== maxZoom) { maxZoom = size.zoom; applyZoom(maxZoom); } // autre taille (plein écran...) : zoom par défaut
        else applyZoom(clamp(k, 1, maxZoom));
        camera.centerOn(center.x, center.y);
    };
    const initial = measure();
    maxZoom = initial.zoom;
    applyZoom(initial.zoom);

    // ---------- Boutons sur la carte : dézoomer, zoomer, plein écran ----------
    const controls = document.createElement("div");
    controls.className = "park-controls";
    const button = (title, icon, action) => {
        const b = document.createElement("button");
        b.type = "button";
        b.title = title;
        b.setAttribute("aria-label", title);
        b.innerHTML = icon;
        b.addEventListener("click", action);
        controls.appendChild(b);
        return b;
    };
    const zoomOutButton = button("Dézoomer", ICONS.minus, () => zoomTo(k - 1));
    const zoomInButton = button("Zoomer", ICONS.plus, () => zoomTo(k + 1));
    const fullscreenButton = button("Plein écran", ICONS.expand, () => {
        if (isFullscreen()) document.exitFullscreen().catch(() => { });
        else container.requestFullscreen().catch(() => { });
    });
    fullscreenButton.hidden = !document.fullscreenEnabled; // iPhone : pas de plein écran pour une page
    container.appendChild(controls);
    const onFullscreen = () => {
        const full = isFullscreen();
        fullscreenButton.innerHTML = full ? ICONS.shrink : ICONS.expand;
        fullscreenButton.title = full ? "Quitter le plein écran (Échap)" : "Plein écran";
        fullscreenButton.setAttribute("aria-label", fullscreenButton.title);
        resize();
    };
    document.addEventListener("fullscreenchange", onFullscreen);
    // Molette : zoom en plein écran seulement, autour du pointeur (sinon elle fait défiler la page)
    let wheel = 0;
    const onWheel = e => {
        if (!isFullscreen() || !scene) return;
        e.preventDefault();
        wheel += e.deltaMode === 0 ? e.deltaY : Math.sign(e.deltaY) * 100; // pavé tactile : petits pas cumulés
        if (Math.abs(wheel) < 100) return;
        const r = game.canvas.getBoundingClientRect();
        zoomTo(k - Math.sign(wheel), (e.clientX - r.left) * game.canvas.width / r.width, (e.clientY - r.top) * game.canvas.height / r.height);
        wheel = 0;
    };

    let ready;
    const started = new Promise(resolve => ready = resolve);
    const game = new Phaser.Game({
        type: Phaser.AUTO,
        parent: container,
        width: initial.width,
        height: initial.height,
        backgroundColor: OUTSIDE,
        pixelArt: true,
        roundPixels: true,
        banner: false,
        audio: { noAudio: true },
        scale: { mode: Phaser.Scale.NONE, zoom: 1 / initial.ratio },
        input: { mouse: { preventDefaultWheel: false }, activePointers: 2 }, // molette : voir onWheel ; deux doigts : pincement
        scene: {
            preload() {
                for (const tileset of data?.tilesets ?? []) this.load.image("tuiles:" + tileset.image, TILES_URL + tileset.image);
                this.load.on("loaderror", () => { }); // planches absentes (images pas récupérées) : de l'herbe unie
            },
            create() { build(this); ready(); },
            update(_, delta) { tick(delta); },
        },
    });

    // ---------- Décor : carte, brouillard hors de la zone « Parc », nuit ----------
    function build(s) {
        scene = s;
        camera = s.cameras.main;
        game.canvas.classList.add("park-canvas");
        game.canvas.addEventListener("wheel", onWheel, { passive: false });
        s.add.rectangle(0, 0, ground.width, ground.height, GRASS).setOrigin(0).setDepth(-1); // sous la carte (planches absentes)
        // La carte ne bouge pas : ses calques sont dessinés une seule fois, dans une image sous les Pokémon (sol, décors), des
        // morceaux de feuillages triés avec eux (foliage) et une image devant tout (brouillard). Des calques de tuiles
        // redessinés à chaque image créaient des dizaines de Ko de déchets par image : petites saccades au passage du
        // ramasse-miettes.
        const below = s.add.renderTexture(0, 0, ground.width, ground.height).setOrigin(0).setDepth(0);
        const above = s.add.renderTexture(0, 0, ground.width, ground.height).setOrigin(0).setDepth(3);
        if (data) {
            s.cache.tilemap.add("parc", { format: Phaser.Tilemaps.Formats.TILED_JSON, data });
            const map = s.make.tilemap({ key: "parc" });
            const tilesets = data.tilesets
                .filter(t => s.textures.exists("tuiles:" + t.image))
                .map(t => map.addTilesetImage(t.name, "tuiles:" + t.image))
                .filter(Boolean);
            for (const name of ["Sol", "Décors", "Devant"]) {
                if (!tilesets.length || map.getLayerIndex(name) === null) continue;
                const layer = map.createLayer(name, tilesets, 0, 0);
                if (name === "Devant") foliage(s, layer);
                else below.draw(layer);
                layer.destroy();
            }
        }
        const fog = s.add.graphics().fillStyle(FOG.color, FOG.alpha);
        const fill = (x, y, w, h) => { if (w > 0 && h > 0) fog.fillRect(x, y, w, h); }; // rien hors de la carte (taille 4 : toute la carte)
        for (let step = 1; step <= FOG.steps; step++) { // marches superposées : plus sombre à chaque case
            const { x, y, w, h } = ground.area;
            const [zx, zy, zw, zh] = [(x - step) * TILE, (y - step) * TILE, (w + 2 * step) * TILE, (h + 2 * step) * TILE];
            fill(0, 0, ground.width, zy);
            fill(0, zy + zh, ground.width, ground.height - zy - zh);
            fill(0, zy, zx, zh);
            fill(zx + zw, zy, ground.width - zx - zw, zh);
        }
        above.draw(fog);
        fog.destroy();
        night = s.add.rectangle(0, 0, ground.width, ground.height, NIGHT.color).setOrigin(0).setDepth(4).setVisible(false);
        label = s.add.graphics().setDepth(6).setVisible(false);
        labelText = s.add.text(0, 0, "", { fontFamily: "sans-serif", fontStyle: "bold", fontSize: "8px", color: "#ffffff", resolution: k })
            .setDepth(6.1).setVisible(false);
        marker = s.add.graphics().setDepth(HELD_DEPTH - 0.1).setVisible(false); // case visée par un Pokémon soulevé

        camera.roundPixels = true;
        applyZoom(k); // zoom et limites de la vue
        const start = ground.start;
        camera.centerOn((start.x + start.w / 2) * TILE, (start.y + start.h / 2) * TILE); // vue de départ : la zone « Départ »
        listen(s);
    }

    /**
     * Feuillages (calque « Devant »), triés avec les Pokémon : chaque tuile appartient à l'arbre dont le tronc est la
     * première case sous elle qui bloque ou n'a plus de feuillage. Un Pokémon aux pieds plus hauts que le milieu de ce
     * tronc passe derrière l'arbre, plus bas devant. Les feuillages d'une même ligne de troncs, proches les uns des autres,
     * forment un morceau ; tous sont dessinés une seule fois, dans une même texture (une image par morceau).
     */
    function foliage(s, layer) {
        const leaves = layer.layer.data; // tuiles, [ligne][colonne]
        const pieces = foliagePieces(trunkRows(leaves));
        if (!pieces.length) return;
        const { width, height } = packPieces(pieces);
        const texture = s.textures.addDynamicTexture("feuillages", width, height);
        layer.setSkipCull(true); // dessiné ailleurs qu'à sa place : ne pas écarter les tuiles hors de la vue
        layer.forEachTile(t => { t.visible = false; });
        pieces.forEach((p, i) => {
            for (const c of p.cells) leaves[c.y][c.x].visible = true;
            texture.draw(layer, p.ax - p.x0 * TILE, p.ay - p.y0 * TILE);
            for (const c of p.cells) leaves[c.y][c.x].visible = false;
            texture.add("f" + i, 0, p.ax, p.ay, p.w, p.h);
            s.add.image(p.x0 * TILE, p.y0 * TILE, "feuillages", "f" + i).setOrigin(0)
                .setDepth(depthOf(p.trunk * TILE + TILE / 2 + 0.5)); // + 0,5 : jamais à égalité avec un Pokémon
        });
    }

    /** Cases de feuillage, par ligne du tronc de leur arbre. */
    function trunkRows(leaves) {
        const { columns, lines } = ground;
        const blocked = data.layers.find(l => l.name === "Collisions" && l.type === "tilelayer")?.data ?? [];
        const has = (x, y) => leaves[y][x].index !== -1;
        const rows = new Map(); // ligne du tronc → cases de feuillage
        for (let y = 0; y < lines; y++) {
            for (let x = 0; x < columns; x++) {
                if (!has(x, y)) continue;
                let trunk = y;
                while (trunk < lines && has(x, trunk) && !blocked[trunk * columns + x]) trunk++;
                if (!rows.has(trunk)) rows.set(trunk, []);
                rows.get(trunk).push({ x, y });
            }
        }
        return rows;
    }

    /** Morceaux de feuillage : les cases d'une même ligne de troncs, proches les unes des autres. */
    function foliagePieces(rows) {
        const pieces = [];
        for (const [trunk, cells] of rows) {
            let piece = null;
            for (const c of cells.sort((a, b) => a.x - b.x)) {
                if (!piece || c.x - piece.x1 > 3) {
                    piece = { trunk, cells: [], x0: c.x, x1: c.x, y0: c.y, y1: c.y };
                    pieces.push(piece);
                }
                piece.cells.push(c);
                piece.x1 = c.x;
                piece.y0 = Math.min(piece.y0, c.y);
                piece.y1 = Math.max(piece.y1, c.y);
            }
        }
        return pieces;
    }

    /**
     * Rangement des morceaux dans la texture (place de chacun : ax, ay) : par étagères, les plus hauts d'abord, un peu
     * d'écart (pas de bavure d'un morceau à l'autre). Rend la taille de la texture.
     */
    function packPieces(pieces) {
        const GAP = 2;
        for (const p of pieces) [p.w, p.h] = [(p.x1 - p.x0 + 1) * TILE, (p.y1 - p.y0 + 1) * TILE];
        const width = Math.max(1024, ...pieces.map(p => p.w));
        let px = 0, py = 0, shelf = 0;
        for (const p of [...pieces].sort((a, b) => b.h - a.h)) {
            if (px + p.w > width) [px, py, shelf] = [0, py + shelf + GAP, 0];
            [p.ax, p.ay] = [px, py];
            px += p.w + GAP;
            shelf = Math.max(shelf, p.h);
        }
        return { width, height: py + shelf };
    }

    // ---------- Souris : glisser pour explorer, curiosité, nom au survol, clic ----------
    /** Pokémon sous le pointeur (le plus en avant d'abord). */
    const at = point => [...pokemon].sort((a, b) => b.y - a.y).find(p => {
        const a = p.anim, w = a.fw * spriteScale / 2, h = a.fh * spriteScale;
        return Math.abs(point.x - p.x) < w * 0.7 && point.y < p.y + 4 && point.y > p.y - h * 0.75;
    });
    const cursor = () => {
        let style = "grab";
        if (held || drag?.moved) style = "grabbing";
        else if (hovered) style = "pointer";
        game.canvas.style.cursor = style;
    };

    // ---------- Pokémon soulevé (propriétaire) : suit le pointeur, posé seulement sur une case où il peut marcher ----------
    const spot = new Phaser.Math.Vector2(); // gardé : la position suit le pointeur à chaque image, sans déchets

    function pickUp(p, pointer) {
        camera.getWorldPoint(pointer.x, pointer.y, spot);
        held = { p, from: { x: p.x, y: p.y }, dx: p.x - spot.x, dy: p.y - spot.y, pointer };
        Object.assign(p, { vx: 0, vy: 0, target: null, path: [], intent: null, resume: null, dir: FRONT });
        p.setState("held"); // un dormeur se réveille
        drag = null;
        hovered = null;
        cursor();
    }

    /** Sous le pointeur, même quand la vue défile ou change de zoom. */
    function carry() {
        camera.getWorldPoint(held.pointer.x, held.pointer.y, spot);
        held.p.x = clamp(spot.x + held.dx, 0, ground.width - 1);
        held.p.y = clamp(spot.y + held.dy, 0, ground.height - 1);
    }

    /** Posé là où il est s'il peut y marcher, sinon (ou <back>) remis à sa place ; il reste un moment immobile. */
    function putDown(back = false) {
        const { p, from } = held;
        held = null;
        if (back || !ground.isFree(p.x, p.y)) Object.assign(p, from);
        p.dir = FRONT;
        p.rest(random(2, 4));
        cursor();
    }

    /** Près du bord de la vue, la vue défile vers lui (de plus en plus vite en s'en approchant). */
    function edgeScroll(dt) {
        const ratio = window.devicePixelRatio || 1, margin = EDGE_SCROLL.margin * ratio;
        const push = (v, size) => {
            if (v < margin) return -(1 - v / margin);
            if (v > size - margin) return (v - size + margin) / margin;
            return 0;
        };
        const vx = clamp(push(held.pointer.x, camera.width), -1, 1), vy = clamp(push(held.pointer.y, camera.height), -1, 1);
        if (!vx && !vy) return;
        const speed = EDGE_SCROLL.speed * ratio / k * dt; // pixels de la carte
        camera.setScroll(camera.scrollX + vx * speed, camera.scrollY + vy * speed);
    }

    /** Cadre de la case visée : clair si on peut l'y poser, rouge sinon (redessiné seulement quand ça change). */
    function drawMarker() {
        marker.setVisible(!!held);
        if (!held) return;
        const p = held.p, ok = ground.isFree(p.x, p.y);
        if (marker.ok !== ok) {
            marker.ok = ok;
            const color = ok ? 0xffffff : 0xff4d4d;
            marker.clear().fillStyle(color, 0.18).fillRect(0, 0, TILE, TILE)
                .lineStyle(1, color, 0.9).strokeRect(0.5, 0.5, TILE - 1, TILE - 1);
        }
        marker.setPosition(Math.floor(p.x / TILE) * TILE, Math.floor(p.y / TILE) * TILE);
    }

    function listen(s) {
        const worldOf = pointer => camera.getWorldPoint(pointer.x, pointer.y);
        // Pincement à deux doigts : un cran de zoom chaque fois que l'écart change d'un tiers environ
        const fingers = () => [s.input.pointer1, s.input.pointer2];
        const pinching = () => fingers().every(f => f.isDown);
        const spread = () => { const [a, b] = fingers(); return Math.hypot(a.x - b.x, a.y - b.y); };
        let pinch = null;
        s.input.on("pointerdown", pointer => {
            if (pinching()) { // deux doigts : pincement (un Pokémon porté retourne à sa place)
                if (held) putDown(true);
                drag = null;
                pinch = { spread: spread() };
                return;
            }
            if (held) return;
            // Sur un Pokémon (propriétaire) : à la souris, il est soulevé dès qu'on bouge ; au doigt, après un appui long
            // (tick), un glissement avant fait défiler la vue
            const target = canMove ? at(worldOf(pointer)) ?? null : null;
            drag = { x: pointer.x, y: pointer.y, scrollX: camera.scrollX, scrollY: camera.scrollY, moved: false,
                target, touch: pointer.wasTouch, time: performance.now(), pointer };
        });
        const pinchMove = () => {
            if (!pinching()) return;
            const [a, b] = fingers(), now = spread();
            if (now > pinch.spread * 1.35 || now < pinch.spread / 1.35) {
                zoomTo(k + (now > pinch.spread ? 1 : -1), (a.x + b.x) / 2, (a.y + b.y) / 2);
                pinch.spread = now;
            }
        };
        // Bouton appuyé : au-delà du seuil, le Pokémon visé est soulevé ou la vue défile
        const dragMove = pointer => {
            const dx = pointer.x - drag.x, dy = pointer.y - drag.y;
            if (!drag.moved && Math.hypot(dx, dy) > DRAG_THRESHOLD * (window.devicePixelRatio || 1)) {
                if (drag.target && !drag.touch) pickUp(drag.target, pointer);
                else drag.moved = true;
            }
            if (drag?.moved) camera.setScroll(drag.scrollX - dx / k, drag.scrollY - dy / k);
        };
        s.input.on("pointermove", pointer => {
            if (pinch) {
                pinchMove();
                return;
            }
            if (drag && pointer.isDown) dragMove(pointer);
            if (held) carry();
            const point = worldOf(pointer);
            mouse = { x: point.x, y: point.y };
            hovered = held || drag?.moved ? null : at(mouse);
            cursor();
        });
        const release = pointer => {
            if (pinch) { // un doigt levé : fin du pincement (sans clic ni glissement)
                if (!pinching()) pinch = null;
                drag = null;
                mouse = hovered = null;
                return;
            }
            if (held) {
                carry();
                putDown();
                if (pointer.wasTouch) mouse = hovered = null;
                return;
            }
            const click = drag && !drag.moved;
            drag = null;
            if (click) {
                const p = at(worldOf(pointer));
                if (p) {
                    p.dir = FRONT; // se tourne vers toi
                    p.rest(4);
                    component.invokeMethodAsync("OnPokemonClicked", p.id);
                }
            }
            if (pointer.wasTouch) mouse = hovered = null; // doigt levé : plus de pointeur à regarder
            cursor();
        };
        s.input.on("pointerup", release);
        s.input.on("pointerupoutside", release); // relâché hors du parc après un glissement
        s.input.on("gameout", () => { mouse = hovered = null; drag = null; cursor(); });
    }

    // ---------- Comportement ----------
    const friendsOf = p => pokemon.filter(o => o !== p && o.family === p.family);

    /** Nouvelle intention quand le Pokémon a fini ce qu'il faisait. */
    function decide(p, night) {
        const { activity, sleepy, sociable, curious } = p.personality;
        const friends = friendsOf(p);
        if (p.energy < 0.22 + night * 0.4 + sleepy) {
            // Fatigué : il va dormir près d'un congénère qui dort, sinon sur place.
            const sleeper = friends.filter(f => f.state === "sleep").sort((a, b) => distance(p, a) - distance(p, b))[0];
            if (sleeper && Math.random() < sociable && distance(p, sleeper) > 20) {
                const side = p.x < sleeper.x ? -1 : 1;
                p.walkTo(sleeper.x + side * random(16, 23), sleeper.y + random(-5, 5), "sleep");
            } else {
                p.sleep();
            }
            return;
        }
        if (mouse && distance(p, mouse) < 133 && Math.random() < 0.6 * curious) {
            // Curieux : il vient voir le pointeur de près, puis le regarde.
            if (distance(p, mouse) > 30) {
                const angle = Math.atan2(p.y - mouse.y, p.x - mouse.x);
                p.walkTo(mouse.x + Math.cos(angle) * 21, mouse.y + Math.sin(angle) * 16, "curious");
            } else {
                p.face(mouse);
                p.rest(random(2, 5));
            }
            return;
        }
        const roll = Math.random();
        const nearest = friends.sort((a, b) => distance(p, a) - distance(p, b))[0];
        if (nearest && distance(p, nearest) < 47) {
            // Avec un congénère : il reste un moment avec lui (se repose face à lui, ou flâne autour).
            if (roll < 0.45 + 0.35 * sociable) {
                if (Math.random() < 0.55) {
                    p.face(nearest);
                    p.rest(random(3, 8) / activity);
                } else {
                    const angle = random(0, Math.PI * 2);
                    p.walkTo(nearest.x + Math.cos(angle) * random(17, 33), nearest.y + Math.sin(angle) * random(11, 23), "stroll");
                }
                return;
            }
        } else if (friends.length && roll < 0.1 + 0.45 * sociable) {
            // Rendre visite à un Pokémon de sa famille, de préférence proche.
            const friend = friends[Math.floor(Math.random() ** 2 * friends.length)];
            const angle = random(0, Math.PI * 2);
            p.walkTo(friend.x + Math.cos(angle) * 20, friend.y + Math.sin(angle) * 13, "visit");
            return;
        }
        if (Math.random() < 0.55) {
            // Explorer un endroit du parc, ni trop près ni trop loin.
            let spot = ground.randomSpot();
            for (let tries = 0; tries < 12 && !(distance(p, spot) > 40 && distance(p, spot) < 160); tries++) spot = ground.randomSpot();
            p.walkTo(spot.x, spot.y, "explore");
        } else {
            // Rester là à regarder autour de soi.
            p.rest(random(2, 6) / activity);
        }
    }

    function update(p, dt, night) {
        const { activity, curious } = p.personality;
        if (p.state === "held") return; // porté : suit le pointeur
        if (p.state === "sleep") {
            p.energy = Math.min(1, p.energy + dt * (night > 0.5 ? ENERGY.sleepNight : ENERGY.sleepDay));
            p.zzz += dt;
            if (p.energy >= p.wakeAt && (night < 0.5 || chance(0.002, dt))) { // la nuit, rares réveils
                p.dir = FRONT;
                p.rest(random(1.5, 3)); // se réveille, regarde devant lui un moment
            }
            return;
        }

        // Curiosité : un Pokémon éveillé près du pointeur se tourne vers lui, et parfois s'en approche.
        const nearMouse = mouse && distance(p, mouse) < 93;
        if (nearMouse && p.state === "idle") {
            p.face(mouse);
            if (chance(0.12 * curious, dt) && distance(p, mouse) > 27)
                p.walkTo(mouse.x + random(-13, 13), mouse.y + random(10, 20), "curious");
        }

        if (p.state === "idle") {
            p.energy = Math.max(0, p.energy - dt * ENERGY.idle);
            p.timer -= dt;
            p.lookTimer -= dt;
            if (p.lookTimer <= 0 && !nearMouse) {
                // Il regarde autour de lui : un quart de tour, ou de nouveau devant lui.
                p.dir = Math.random() < 0.25 ? FRONT : (p.dir + (Math.random() < 0.5 ? 1 : 7)) % 8;
                p.lookTimer = random(1.2, 3.5);
            }
            if (p.timer <= 0) decide(p, night);
            return;
        }

        // Marche vers le prochain point de passage, en s'écartant des autres.
        p.energy = Math.max(0, p.energy - dt * ENERGY.walk * activity);
        while (p.path.length > 1 && distance(p, p.path[0]) < 4) p.path.shift();
        const remaining = p.remaining();
        // Ralentit en approchant de sa cible (pas d'arrêt net).
        const speed = WALK_SPEED * activity * (p.intent === "sleep" ? 0.7 : 1) * clamp(remaining / 12, 0.35, 1);
        if (remaining < 1.7) {
            if (p.intent === "sleep") p.sleep();
            else {
                if (p.intent === "curious" && mouse) p.face(mouse);
                if (p.intent === "visit" || p.intent === "stroll") {
                    // Arrivé chez un congénère : ils se regardent.
                    const friend = friendsOf(p).sort((a, b) => distance(p, a) - distance(p, b))[0];
                    if (friend) { p.face(friend); if (friend.state === "idle") friend.face(p); }
                }
                p.rest(random(1.5, 5) / activity);
            }
            return;
        }
        const next = p.path[0];
        let dx = next.x - p.x, dy = next.y - p.y;
        const toNext = Math.hypot(dx, dy) || 1;
        dx /= toNext; dy /= toNext;
        for (const o of pokemon) {
            const d = distance(p, o);
            if (o !== p && d < 15 && d > 0) { dx += (p.x - o.x) / d * 0.6; dy += (p.y - o.y) / d * 0.6; }
        }
        const norm = Math.hypot(dx, dy) || 1;
        // La vitesse rejoint progressivement celle voulue : virages arrondis, démarrages en douceur.
        const ease = Math.min(1, dt * 6);
        p.vx += (dx / norm * speed - p.vx) * ease;
        p.vy += (dy / norm * speed - p.vy) * ease;
        // Jamais sur une case bloquée : contre un obstacle, il glisse le long (ou s'arrête).
        const nx = p.x + p.vx * dt, ny = p.y + p.vy * dt;
        if (ground.isFree(nx, ny)) { p.x = nx; p.y = ny; }
        else if (ground.isFree(nx, p.y)) { p.x = nx; p.vy = 0; }
        else if (ground.isFree(p.x, ny)) { p.y = ny; p.vx = 0; }
        else { p.vx = p.vy = 0; }
        // Regarde où il va, et ne change de direction que franchement (pas de tremblement entre deux directions voisines).
        const moving = Math.hypot(p.vx, p.vy);
        if (moving > 3) {
            const [fx, fy] = vectorOf(p.dir);
            if ((fx * p.vx + fy * p.vy) / moving < 0.85) p.dir = directionOf(p.vx, p.vy);
        }
        // Petites pauses en chemin, et abandon si quelqu'un lui barre la route.
        p.progress.time += dt;
        if (p.progress.time > 2) {
            if (p.progress.distance - remaining < 7) { p.rest(random(1, 2)); return; }
            p.progress = { time: 0, distance: remaining };
        }
        if (p.intent !== "sleep" && remaining > 27 && chance(0.04, dt)) {
            const target = p.target, intent = p.intent;
            p.rest(random(0.4, 1.2));
            p.timer = random(0.4, 1.2);
            p.resume = { target, intent };
        }
    }

    // ---------- Dessin (objets de Phaser mis à jour à chaque image) ----------
    /** Planches d'un Pokémon dans les textures de Phaser : une image par case de l'animation. */
    function addTextures(anims) {
        for (const anim of new Set(Object.values(anims))) {
            if (scene.textures.exists(anim.key)) continue;
            const texture = scene.textures.addImage(anim.key, anim.image);
            for (let r = 0; r < anim.rows; r++)
                for (let c = 0; c < anim.columns; c++) texture.add(r * anim.columns + c, 0, c * anim.fw, r * anim.fh, anim.fw, anim.fh);
        }
    }

    /**
     * Ombre au sol d'une planche, dessinée une fois (adoucie) en pixels d'écran : pas d'ellipse recalculée à chaque image.
     * Refaite si la taille des sprites change (fenêtre redimensionnée), ou dans un nouveau parc (planches gardées par le
     * module, textures propres à chaque jeu de Phaser : sans, la texture « manquante » de Phaser, un carré noir).
     */
    function shadowOf(anim) {
        if (anim.shadowK !== spriteK || !scene.textures.exists(anim.shadow)) {
            const w = Math.max(2, Math.round(Math.min(anim.fw, 32) * spriteK * 0.56)), h = 6 * spriteK;
            anim.shadow = `ombre:${w}x${h}`;
            anim.shadowK = spriteK;
            if (!scene.textures.exists(anim.shadow)) {
                const texture = scene.textures.createCanvas(anim.shadow, w, h);
                const context = texture.getContext();
                context.fillStyle = "rgba(0, 0, 0, 0.18)";
                context.beginPath();
                context.ellipse(w / 2, h / 2, w / 2, h / 2, 0, 0, Math.PI * 2);
                context.fill();
                texture.refresh();
            }
        }
        return anim.shadow;
    }

    const snap = v => Math.round(v * k) / k; // au pixel d'écran près : déplacement continu, sans bonds d'un pixel de carte

    function draw(level) {
        night.setVisible(level > 0);
        if (level > 0) night.setFillStyle(NIGHT.color, NIGHT.alpha * level);
        for (const p of pokemon) {
            const anim = p.anim, s = spriteScale;
            // Ombre au sol, puis l'image : SpriteCollab centre le Pokémon dans chaque image, les pieds un peu sous le centre.
            p.shadow.setTexture(shadowOf(anim)).setScale(1 / k).setPosition(snap(p.x), snap(p.y));
            const row = Math.min(p.state === "sleep" ? 0 : p.dir, anim.rows - 1);
            const lift = p.state === "held" ? LIFT : 0;
            p.sprite.setTexture(anim.key, row * anim.columns + p.frame % anim.columns)
                .setScale(s)
                .setPosition(snap(p.x), snap(p.y - 6 * s - lift));
            // Les plus bas passent devant (tri seulement s'ils changent de ligne) ; soulevé : devant tout
            const depth = lift ? HELD_DEPTH : depthOf(Math.round(p.y));
            if (p.sprite.depth !== depth) p.sprite.setDepth(depth);
            drawZzz(p, anim.fh * s);
        }
        drawMarker();
        drawName(hovered);
    }

    function drawZzz(p, h) {
        const [small, big] = p.texts;
        const sleeping = p.state === "sleep";
        small.setVisible(sleeping);
        big.setVisible(sleeping && p.zzz % 2.4 > 1.2);
        if (!sleeping) return;
        const t = p.zzz % 2.4, alpha = Math.min(1, 2.4 - t) * 0.85;
        small.setPosition(snap(p.x + 5 + t * 4), snap(p.y - h * 0.35 - t * 7 - 8)).setAlpha(alpha);
        big.setPosition(snap(p.x + 9 + (t - 1.2) * 4), snap(p.y - h * 0.35 - (t - 1.2) * 7 - 12)).setAlpha(alpha);
    }

    let labelFor = null; // Pokémon dont le nom est affiché (fond redessiné seulement quand il change)
    function drawName(p) {
        label.setVisible(!!p);
        labelText.setVisible(!!p);
        if (!p) { labelFor = null; return; }
        if (labelFor !== p) {
            labelFor = p;
            labelText.setText(p.nickname);
            label.clear().fillStyle(0x000000, 0.6).fillRoundedRect(0, 0, labelText.width + 7, 11, 3);
        }
        const width = labelText.width + 7, h = p.anim.fh * spriteScale;
        const x = snap(clamp(p.x - width / 2, 1, ground.width - width - 1)), y = snap(Math.max(1, p.y - h * 0.75 - 12));
        label.setPosition(x, y);
        labelText.setPosition(x + 3.5, y + 1);
    }

    // ---------- Boucle ----------
    function step(dt, level) {
        for (const p of pokemon) {
            if (p.state === "idle" && p.resume && p.timer <= dt) { // reprend sa marche après une petite pause
                const { target, intent } = p.resume;
                p.resume = null;
                p.walkTo(target.x, target.y, intent);
            }
            update(p, dt, level);
            p.advanceFrame(dt * 1000, spriteScale);
        }
    }

    let level = nightLevel(), levelAge = 0;
    function tick(delta) {
        const dt = Math.min(0.1, delta / 1000); // onglet en arrière-plan : pas de bond au retour
        levelAge += dt;
        if (levelAge > 1) { level = nightLevel(); levelAge = 0; } // l'heure, une fois par seconde
        if (drag?.target && drag.touch && !drag.moved && drag.pointer.isDown && performance.now() - drag.time >= LONG_PRESS)
            pickUp(drag.target, drag.pointer); // appui long sur un Pokémon
        if (held) { edgeScroll(dt); carry(); }
        step(dt, level);
        draw(level);
    }

    await started;
    const observer = new ResizeObserver(resize);
    observer.observe(container);
    window.addEventListener("resize", resize);
    resize();

    const removeFromScene = p => {
        if (held?.p === p) held = null;
        if (drag?.target === p) drag.target = null;
        if (hovered === p) hovered = null;
        p.sprite.destroy(); p.shadow.destroy(); for (const text of p.texts) text.destroy();
    };
    let lists = 0; // numéro de la dernière liste reçue (setPokemon)

    return {
        /** Pokémon du parc : ceux qui restent gardent leur place, les nouveaux arrivent à un endroit libre. */
        async setPokemon(list) {
            const call = ++lists;
            const kept = pokemon.filter(p => list.some(i => i.id === p.id));
            for (const p of pokemon) if (!kept.includes(p)) removeFromScene(p);
            // Retirés de la liste dessinée avant d'attendre les planches des nouveaux : dessiner un Pokémon détruit lève une
            // erreur qui arrête la boucle de Phaser (parc figé, vu sous Firefox en retirant et ajoutant d'un coup).
            pokemon = kept;
            const arriving = list.filter(i => !kept.some(p => p.id === i.id));
            const loaded = await Promise.all(arriving.map(async info => {
                try { return { info, anims: await loadSheets(info.spriteFolder) }; }
                catch { return null; } // sprites absents (images pas récupérées) : ce Pokémon n'apparaît pas
            }));
            if (call !== lists) return; // une liste plus récente est arrivée pendant le chargement : c'est elle qui compte
            const placed = [...pokemon];
            for (const item of loaded.filter(l => l)) {
                let spot = ground.randomSpot();
                for (let tries = 0; tries < 30 && !placed.every(o => distance(o, spot) > 27); tries++) spot = ground.randomSpot();
                const p = new Pokemon(item.info, item.anims, ground, spot.x, spot.y);
                addTextures(p.anims);
                p.sprite = scene.add.image(0, 0, p.anim.key, 0);
                p.shadow = scene.add.image(0, 0, shadowOf(p.anim)).setDepth(1.9);
                p.texts = ["z", "Z"].map(z => scene.add.text(0, 0, z, { fontFamily: "sans-serif", fontStyle: "bold", fontSize: "7px",
                    color: "#ffffff", resolution: k }).setDepth(6).setVisible(false));
                placed.push(p);
            }
            pokemon = placed;
            if (hovered && !pokemon.includes(hovered)) hovered = null;
        },
        /**
         * Pour les essais : fait vivre le parc <seconds> secondes d'un coup (sans dessin), à l'heure <hour> (sinon l'heure
         * actuelle), et rend l'état de chaque Pokémon.
         */
        simulate(seconds, hour) {
            const level = hour === undefined ? nightLevel() : nightLevel(new Date(2000, 0, 1, Math.floor(hour), (hour % 1) * 60));
            for (let t = 0; t < seconds; t += 1 / 30) step(1 / 30, level);
            draw(level);
            return pokemon.map(p => ({ id: p.id, species: p.species, state: p.state, intent: p.intent, x: Math.round(p.x),
                y: Math.round(p.y), energy: Math.round(p.energy * 100) / 100, free: ground.isFree(p.x, p.y) }));
        },
        /** Pour les essais : pose un Pokémon à un endroit (pixels de la carte), immobile un long moment. */
        place(id, x, y) {
            const p = pokemon.find(o => o.id === id);
            if (!p) return false;
            Object.assign(p, { x, y, vx: 0, vy: 0, target: null, path: [], intent: null, timer: 600 });
            p.setState("idle");
            draw(nightLevel());
            return true;
        },
        /** Pour les essais : la caméra (zone affichée, zoom), les zones « Départ » et « Parc » (en cases), en pixels de la carte. */
        view() {
            // Calculée ici : camera.worldView n'est mise à jour qu'au dessin (rare quand l'onglet est caché)
            const width = camera.width / k, height = camera.height / k;
            const x = camera.scrollX + (camera.width - width) / 2, y = camera.scrollY + (camera.height - height) / 2;
            return { x, y, width, height, zoom: k, spriteK, start: ground.start, area: ground.area, size,
                map: { width: ground.width, height: ground.height } };
        },
        dispose() {
            observer.disconnect();
            window.removeEventListener("resize", resize);
            document.removeEventListener("fullscreenchange", onFullscreen);
            if (isFullscreen()) document.exitFullscreen().catch(() => { });
            controls.remove();
            game.destroy(true);
        },
    };
}
