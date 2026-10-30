// Parc : les Pokémon s'y promènent avec les sprites Donjon Mystère de SpriteCollab (animations Idle, Walk et Sleep), sur
// une carte faite dans Tiled avec le pack Cute Fantasy (outils/carte-parc.py), affichée par Phaser (moteur de jeu 2D).
// Tout se passe dans le navigateur ; le serveur ne donne que la liste des Pokémon (Park.razor) et est prévenu d'un clic sur
// l'un d'eux.
//
// Comportement : chaque Pokémon a une énergie (baisse en marchant, remonte en dormant : il s'endort quand il est fatigué,
// plus tôt la nuit), une intention (explorer un endroit, rejoindre un Pokémon de sa famille, observer autour de lui, aller
// dormir près d'un congénère), un caractère tiré de sa nature (plus ou moins actif, dormeur, sociable) et de la curiosité
// pour le pointeur de la souris. Il contourne les obstacles (calque « Collisions » de la carte) et reste dans la zone
// « Départ » ; le reste de la carte est assombri (à débloquer plus tard) et se découvre en faisant glisser la vue.
import * as Phaser from "../lib/phaser/phaser.esm.min.js";

// Carte : coordonnées du parc en pixels de la carte (16 par tuile). La hauteur affichée est celle de la zone « Départ »
// (320 pixels), à un nombre entier de pixels d'écran par pixel de carte (dessin net).
const MAP_URL = "parc/parc-1.json";
const TILES_URL = "images/parc/tuiles/"; // planches du pack, hors de Git (deploy/fond-parc.cmd)
const TILE = 16;
const GRASS = "#33984b"; // couleur de l'herbe, sans les planches (absentes, ou pas encore chargées)
const FOG = { color: 0x0b1222, alpha: 0.55 }; // hors de la zone « Départ »
const NIGHT = { color: 0x101848, alpha: 0.38 };
// Taille d'un pixel de sprite par rapport à un pixel de la carte (deux tiers : ×2 quand la carte est à ×3), arrondie à un
// nombre entier de pixels d'écran (dessin net), jamais moins de 2 pixels d'écran tant que la carte en a autant.
const SPRITE_SIZE = 2 / 3;
const WALK_SPEED = 27; // vitesse de marche (pixels de la carte par seconde), selon le caractère
const WALK_ANIMATION_SPEED = 30; // vitesse à laquelle l'animation de marche joue à son rythme normal (pieds sans glisser),
                                 // pour des sprites à la taille des pixels de la carte (plus petits : foulée plus courte)
const DRAG_THRESHOLD = 6; // pixels (CSS) avant qu'un clic devienne un glissement de la vue
const ANIMATIONS = ["Idle", "Walk", "Sleep"];
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
            const xml = new DOMParser().parseFromString(await (await fetch(folder + "AnimData.xml")).text(), "text/xml");
            const defs = new Map([...xml.querySelectorAll("Anim")].map(a => [a.querySelector("Name").textContent, a]));
            const result = {};
            for (const name of ANIMATIONS) {
                let def = defs.get(name);
                while (def?.querySelector("CopyOf")) def = defs.get(def.querySelector("CopyOf").textContent); // animation copiée d'une autre
                if (!def) continue;
                const source = def.querySelector("Name").textContent;
                const image = new Image();
                image.src = `${folder}${source}-Anim.png`;
                await image.decode();
                const fw = +def.querySelector("FrameWidth").textContent, fh = +def.querySelector("FrameHeight").textContent;
                result[name] = {
                    image, fw, fh,
                    key: image.src, // texture de Phaser
                    columns: Math.max(1, Math.round(image.width / fw)),
                    rows: Math.max(1, Math.round(image.height / fh)),
                    durations: [...def.querySelectorAll("Duration")].map(d => +d.textContent * 1000 / 60), // images à 60 par seconde
                };
            }
            result.Idle ??= result.Walk; // au cas où une planche manquerait
            result.Walk ??= result.Idle;
            result.Sleep ??= result.Idle;
            return result;
        })());
    }
    return sheets.get(folder);
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
 * Terrain de la carte : cases où l'on peut marcher (dans la zone « Départ », sans tuile du calque « Collisions »), et
 * chemins entre deux points (A* sur les cases, en diagonale seulement si les deux côtés sont libres, puis lissé en
 * lignes droites). Sans carte : un pré de 24 × 20 cases, sans obstacle.
 */
class Ground {
    constructor(data) {
        this.data = data;
        this.columns = data?.width ?? 24;
        this.lines = data?.height ?? 20;
        const zone = data?.layers.find(l => l.name === "Zones")?.objects?.find(o => o.name === "Départ");
        this.start = zone
            ? { x: Math.round(zone.x / TILE), y: Math.round(zone.y / TILE), w: Math.round(zone.width / TILE), h: Math.round(zone.height / TILE) }
            : { x: 0, y: 0, w: this.columns, h: this.lines };
        const collisions = data?.layers.find(l => l.name === "Collisions" && l.type === "tilelayer")?.data ?? [];
        this.walkable = new Uint8Array(this.columns * this.lines);
        this.free = []; // cases libres, pour tirer un endroit au hasard
        const { x: sx, y: sy, w: sw, h: sh } = this.start;
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
        this.open = new Int32Array(n);
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
        const gx = goalTile % this.columns, gy = Math.floor(goalTile / this.columns);
        const columns = this.columns, cost = this.cost, previous = this.previous, seen = this.seen, closed = this.closed, open = this.open;
        const estimate = i => {
            const dx = Math.abs(i % columns - gx), dy = Math.abs(Math.floor(i / columns) - gy);
            return cost[i] + Math.max(dx, dy) + (Math.SQRT2 - 1) * Math.min(dx, dy);
        };
        const search = ++this.search;
        cost[startTile] = 0;
        seen[startTile] = search;
        open[0] = startTile;
        let openCount = 1, found = false;
        while (openCount) {
            let best = 0;
            for (let i = 1; i < openCount; i++) if (estimate(open[i]) < estimate(open[best])) best = i;
            const current = open[best];
            open[best] = open[--openCount];
            if (current === goalTile) { found = true; break; }
            closed[current] = search;
            const cx = current % columns, cy = Math.floor(current / columns);
            for (let dy = -1; dy <= 1; dy++) {
                for (let dx = -1; dx <= 1; dx++) {
                    if ((!dx && !dy) || !this.tileFree(cx + dx, cy + dy)) continue;
                    if (dx && dy && (!this.tileFree(cx + dx, cy) || !this.tileFree(cx, cy + dy))) continue; // pas de coin coupé
                    const next = (cy + dy) * columns + cx + dx;
                    const c = cost[current] + (dx && dy ? Math.SQRT2 : 1);
                    if (closed[next] === search || (seen[next] === search && c >= cost[next])) continue;
                    if (seen[next] !== search) open[openCount++] = next; // pas encore dans la liste
                    seen[next] = search;
                    cost[next] = c;
                    previous[next] = current;
                }
            }
        }
        if (!found) return null;
        const tiles = [];
        for (let t = goalTile; t !== startTile; t = previous[t]) tiles.unshift(t);
        // Lissage : du point actuel, aller tout droit au point de passage le plus loin qu'on voit.
        const points = [start, ...tiles.slice(0, -1).map(t => this.center(t)), to];
        const result = [];
        for (let i = 0; i < points.length - 1;) {
            let j = points.length - 1;
            while (j > i + 1 && !this.lineFree(points[i], points[j])) j--;
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

    get anim() { return this.anims[this.state === "walk" ? "Walk" : this.state === "sleep" ? "Sleep" : "Idle"]; }

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
        let total = 0, from = this;
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

export async function create(container, component) {
    let data = null;
    try { data = await (await fetch(MAP_URL)).json(); }
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
    let scene = null, camera = null, night = null, label = null, labelText = null;

    // ---------- Taille : toute la largeur du conteneur ; en hauteur, un nombre entier de fois la zone « Départ » en
    // pixels d'écran (chaque pixel de la carte et des sprites couvre autant de pixels d'écran : dessin net), le plus grand
    // qui tient dans la hauteur visible sous le parc (sur un téléphone : pas plus que la zone entière en largeur) ----------
    const measure = () => {
        const width = container.clientWidth || viewW;
        const top = container.getBoundingClientRect().top + window.scrollY;
        const ratio = window.devicePixelRatio || 1;
        const visible = Math.max(240, window.innerHeight - top - 24) * ratio;
        const fitWidth = Math.round(width * ratio / viewW); // zone à peu près entière en largeur
        const zoom = Math.max(1, Math.round(240 * ratio / viewH), Math.min(Math.floor(visible / viewH), fitWidth));
        return { ratio, zoom, width: Math.round(width * ratio), height: zoom * viewH };
    };
    const applyZoom = zoom => {
        k = zoom;
        spriteK = Math.max(Math.min(k, 2), Math.round(k * SPRITE_SIZE));
        spriteScale = spriteK / k;
    };
    const resize = () => {
        if (!scene) return;
        const size = measure();
        const center = { x: camera.midPoint.x, y: camera.midPoint.y };
        game.scale.setZoom(1 / size.ratio); // canvas en pixels physiques, affiché en pixels CSS
        game.scale.resize(size.width, size.height);
        applyZoom(size.zoom);
        camera.setZoom(k);
        camera.centerOn(center.x, center.y);
        for (const p of pokemon) for (const text of p.texts ?? []) text.setResolution(k);
        labelText?.setResolution(k);
    };
    const initial = measure();
    applyZoom(initial.zoom);

    let ready;
    const started = new Promise(resolve => ready = resolve);
    const game = new Phaser.Game({
        type: Phaser.AUTO,
        parent: container,
        width: initial.width,
        height: initial.height,
        backgroundColor: GRASS,
        pixelArt: true,
        roundPixels: true,
        banner: false,
        audio: { noAudio: true },
        scale: { mode: Phaser.Scale.NONE, zoom: 1 / initial.ratio },
        input: { mouse: { preventDefaultWheel: false } }, // la molette fait défiler la page
        scene: {
            preload() {
                for (const tileset of data?.tilesets ?? []) this.load.image("tuiles:" + tileset.image, TILES_URL + tileset.image);
                this.load.on("loaderror", () => { }); // planches absentes (images pas récupérées) : de l'herbe unie
            },
            create() { build(this); ready(); },
            update(_, delta) { tick(delta); },
        },
    });

    // ---------- Décor : carte, brouillard hors de la zone « Départ », nuit ----------
    function build(s) {
        scene = s;
        camera = s.cameras.main;
        game.canvas.classList.add("park-canvas");
        // La carte ne bouge pas : ses calques sont dessinés une seule fois, dans une image sous les Pokémon (sol, décors) et
        // une devant (feuillages, brouillard). Des calques de tuiles redessinés à chaque image créaient des dizaines de Ko
        // de déchets par image : petites saccades au passage du ramasse-miettes.
        const below = s.add.renderTexture(0, 0, ground.width, ground.height).setOrigin(0).setDepth(0);
        const above = s.add.renderTexture(0, 0, ground.width, ground.height).setOrigin(0).setDepth(3);
        if (data) {
            s.cache.tilemap.add("parc", { format: Phaser.Tilemaps.Formats.TILED_JSON, data });
            const map = s.make.tilemap({ key: "parc" });
            const tilesets = data.tilesets
                .filter(t => s.textures.exists("tuiles:" + t.image))
                .map(t => map.addTilesetImage(t.name, "tuiles:" + t.image))
                .filter(t => t);
            for (const [name, target] of [["Sol", below], ["Décors", below], ["Devant", above]]) {
                if (!tilesets.length || map.getLayerIndex(name) === null) continue;
                const layer = map.createLayer(name, tilesets, 0, 0);
                target.draw(layer);
                layer.destroy();
            }
        }
        const fog = s.add.graphics().fillStyle(FOG.color, FOG.alpha);
        const { x, y, w, h } = ground.start;
        const [zx, zy, zw, zh] = [x * TILE, y * TILE, w * TILE, h * TILE];
        fog.fillRect(0, 0, ground.width, zy);
        fog.fillRect(0, zy + zh, ground.width, ground.height - zy - zh);
        fog.fillRect(0, zy, zx, zh);
        fog.fillRect(zx + zw, zy, ground.width - zx - zw, zh);
        above.draw(fog);
        fog.destroy();
        night = s.add.rectangle(0, 0, ground.width, ground.height, NIGHT.color).setOrigin(0).setDepth(4).setVisible(false);
        label = s.add.graphics().setDepth(6).setVisible(false);
        labelText = s.add.text(0, 0, "", { fontFamily: "sans-serif", fontStyle: "bold", fontSize: "8px", color: "#ffffff", resolution: k })
            .setDepth(6.1).setVisible(false);

        camera.setBounds(0, 0, ground.width, ground.height);
        camera.setZoom(k);
        camera.centerOn(zx + zw / 2, zy + zh / 2);
        camera.roundPixels = true;
        listen(s);
    }

    // ---------- Souris : glisser pour explorer, curiosité, nom au survol, clic ----------
    /** Pokémon sous le pointeur (le plus en avant d'abord). */
    const at = point => [...pokemon].sort((a, b) => b.y - a.y).find(p => {
        const a = p.anim, w = a.fw * spriteScale / 2, h = a.fh * spriteScale;
        return Math.abs(point.x - p.x) < w * 0.7 && point.y < p.y + 4 && point.y > p.y - h * 0.75;
    });
    const cursor = () => { game.canvas.style.cursor = drag?.moved ? "grabbing" : hovered ? "pointer" : "grab"; };

    function listen(s) {
        const worldOf = pointer => camera.getWorldPoint(pointer.x, pointer.y);
        s.input.on("pointerdown", pointer => {
            drag = { x: pointer.x, y: pointer.y, scrollX: camera.scrollX, scrollY: camera.scrollY, moved: false };
        });
        s.input.on("pointermove", pointer => {
            if (drag && pointer.isDown) {
                const dx = pointer.x - drag.x, dy = pointer.y - drag.y;
                if (!drag.moved && Math.hypot(dx, dy) > DRAG_THRESHOLD * (window.devicePixelRatio || 1)) drag.moved = true;
                if (drag.moved) camera.setScroll(drag.scrollX - dx / k, drag.scrollY - dy / k);
            }
            const point = worldOf(pointer);
            mouse = { x: point.x, y: point.y };
            hovered = drag?.moved ? null : at(mouse);
            cursor();
        });
        const release = pointer => {
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
     * Refaite si la taille des sprites change (fenêtre redimensionnée).
     */
    function shadowOf(anim) {
        if (anim.shadowK !== spriteK) {
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
            p.sprite.setTexture(anim.key, row * anim.columns + p.frame % anim.columns)
                .setScale(s)
                .setPosition(snap(p.x), snap(p.y - 6 * s));
            const depth = 2 + Math.round(p.y) / 10000; // les plus bas passent devant (tri seulement s'ils changent de ligne)
            if (p.sprite.depth !== depth) p.sprite.setDepth(depth);
            drawZzz(p, anim.fh * s);
        }
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
        step(dt, level);
        draw(level);
    }

    await started;
    const observer = new ResizeObserver(resize);
    observer.observe(container);
    window.addEventListener("resize", resize);
    resize();

    const removeFromScene = p => { p.sprite.destroy(); p.shadow.destroy(); for (const text of p.texts) text.destroy(); };

    return {
        /** Pokémon du parc : ceux qui restent gardent leur place, les nouveaux arrivent à un endroit libre. */
        async setPokemon(list) {
            const kept = pokemon.filter(p => list.some(i => i.id === p.id));
            for (const p of pokemon) if (!kept.includes(p)) removeFromScene(p);
            const arriving = list.filter(i => !kept.some(p => p.id === i.id));
            const loaded = await Promise.all(arriving.map(async info => {
                try { return { info, anims: await loadSheets(info.spriteFolder) }; }
                catch { return null; } // sprites absents (images pas récupérées) : ce Pokémon n'apparaît pas
            }));
            const placed = [...kept];
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
        /** Pour les essais : la caméra (zone affichée, zoom) et la zone « Départ », en pixels de la carte. */
        view() {
            // Calculée ici : camera.worldView n'est mise à jour qu'au dessin (rare quand l'onglet est caché)
            const width = camera.width / k, height = camera.height / k;
            const x = camera.scrollX + (camera.width - width) / 2, y = camera.scrollY + (camera.height - height) / 2;
            return { x, y, width, height, zoom: k, spriteK, start: ground.start, map: { width: ground.width, height: ground.height } };
        },
        dispose() {
            observer.disconnect();
            window.removeEventListener("resize", resize);
            game.destroy(true);
        },
    };
}
