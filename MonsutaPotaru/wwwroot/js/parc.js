// Parc : les Pokémon s'y promènent avec les sprites Donjon Mystère de SpriteCollab (animations Idle, Walk et Sleep).
// Tout se passe dans le navigateur (une image par rafraîchissement de l'écran) ; le serveur ne donne que la liste des
// Pokémon (Park.razor) et est prévenu d'un clic sur l'un d'eux.
//
// Comportement : chaque Pokémon a une énergie (baisse en marchant, remonte en dormant : il s'endort quand il est fatigué,
// plus tôt la nuit), une intention (explorer un endroit, rejoindre un Pokémon de sa famille, observer autour de lui, aller
// dormir près d'un congénère), un caractère tiré de sa nature (plus ou moins actif, dormeur, sociable) et de la curiosité
// pour le pointeur de la souris.

// Coordonnées du parc : 486 de haut (le fond, boîte « Forêt » de Diamant Étincelant, fait 576 × 486) ; la largeur suit celle
// de la page (le parc prend toute la largeur et la hauteur visible, le fond se répète) : 576 au moins, comme sur un téléphone.
const BACKGROUND_W = 576, WORLD_H = 486;
let worldW = BACKGROUND_W;
const SPRITE_SCALE = 1.5; // taille des sprites dans le parc (arrondie à un nombre entier de pixels d'écran au dessin)
const WALK_SPEED = 40; // vitesse de marche (pixels du parc par seconde), selon le caractère
const WALK_ANIMATION_SPEED = 45; // vitesse à laquelle l'animation de marche joue à son rythme normal (pieds sans glisser)
const MARGIN_X = 22, MARGIN_TOP = 40, MARGIN_BOTTOM = 14; // le Pokémon (ses pieds) reste dans ces limites
const ANIMATIONS = ["Idle", "Walk", "Sleep"];
// Énergie (par seconde) : 3 à 5 minutes d'activité le jour avant une sieste d'environ une minute ; la nuit, ils
// s'endorment plus tôt et récupèrent lentement (longues nuits).
const ENERGY = { walk: 0.004, idle: 0.0012, sleepDay: 0.012, sleepNight: 0.004 };

// Lignes des planches de SpriteCollab : bas, bas-droite, droite, haut-droite, haut, haut-gauche, gauche, bas-gauche.
const FRONT = 0;
const directionOf = (dx, dy) => (((Math.round((90 - Math.atan2(dy, dx) * 180 / Math.PI) / 45)) % 8) + 8) % 8;
const vectorOf = dir => { const a = (90 - dir * 45) * Math.PI / 180; return [Math.cos(a), Math.sin(a)]; };

const random = (min, max) => min + Math.random() * (max - min);
const chance = perSecond => dt => Math.random() < perSecond * dt; // probabilité par seconde, ramenée au pas de temps
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

class Pokemon {
    constructor(info, anims, x, y) {
        Object.assign(this, { id: info.id, family: info.family, species: info.species, nickname: info.nickname, anims, x, y });
        this.personality = personalityOf(info.nature);
        this.dir = Math.floor(random(0, 8));
        this.state = "idle";
        this.timer = random(0.5, 3);
        this.lookTimer = random(1, 3);
        this.energy = random(0.35, 1);
        this.frame = 0;
        this.frameTime = 0;
        this.target = null;
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

    walkTo(x, y, intent) {
        this.target = { x: clamp(x, MARGIN_X, worldW - MARGIN_X), y: clamp(y, MARGIN_TOP, WORLD_H - MARGIN_BOTTOM) };
        this.intent = intent;
        this.progress = { time: 0, distance: distance(this, this.target) };
        this.setState("walk");
    }

    rest(seconds) {
        this.vx = this.vy = 0;
        this.target = null;
        this.intent = null;
        this.timer = seconds;
        this.setState("idle");
    }

    sleep() {
        this.vx = this.vy = 0;
        this.target = null;
        this.intent = null;
        this.setState("sleep");
        this.wakeAt = random(0.85, 1);
    }

    face(other) { this.dir = directionOf(other.x - this.x, other.y - this.y); }

    /** Avance l'animation ; la marche joue au rythme de la vitesse (les pieds ne glissent pas, ne patinent pas). */
    advanceFrame(dtMs) {
        const anim = this.anim;
        const rate = this.state === "walk" ? clamp(Math.hypot(this.vx, this.vy) / WALK_ANIMATION_SPEED, 0.35, 1.4) : 1;
        this.frameTime += dtMs * rate;
        while (this.frameTime >= anim.durations[this.frame % anim.durations.length]) {
            this.frameTime -= anim.durations[this.frame % anim.durations.length];
            this.frame = (this.frame + 1) % anim.durations.length;
        }
    }
}

export function create(container, component) {
    const canvas = document.createElement("canvas");
    canvas.className = "park-canvas";
    container.appendChild(canvas);
    const context = canvas.getContext("2d");
    const background = new Image();
    background.src = "images/parc/foret.png";

    let pokemon = [];
    let mouse = null; // position du pointeur dans le parc, null hors du parc
    let hovered = null;
    let last = performance.now();
    let frameRequest = 0;
    let scale = 1;

    // ---------- Taille : toute la largeur du conteneur, et la hauteur visible sous le parc (pas plus que les proportions du
    // fond : un téléphone garde la boîte entière) ; le terrain s'élargit, les sprites gardent leur taille ----------
    const resize = () => {
        const width = container.clientWidth;
        const top = container.getBoundingClientRect().top + window.scrollY;
        const height = Math.max(240, Math.min(window.innerHeight - top - 24, width * WORLD_H / BACKGROUND_W));
        const ratio = window.devicePixelRatio || 1;
        canvas.style.width = `${width}px`;
        canvas.style.height = `${height}px`;
        canvas.width = Math.round(width * ratio);
        canvas.height = Math.round(height * ratio);
        scale = canvas.height / WORLD_H;
        worldW = Math.max(BACKGROUND_W * 0.5, canvas.width / scale);
        for (const p of pokemon) p.x = clamp(p.x, MARGIN_X, worldW - MARGIN_X); // terrain rétréci : personne dehors
    };
    const observer = new ResizeObserver(resize);
    observer.observe(container);
    window.addEventListener("resize", resize);
    resize();

    // ---------- Souris : curiosité, nom au survol, clic ----------
    const toWorld = e => {
        const r = canvas.getBoundingClientRect();
        return { x: (e.clientX - r.left) / r.width * worldW, y: (e.clientY - r.top) / r.height * WORLD_H };
    };
    /** Pokémon sous le pointeur (le plus en avant d'abord). */
    const at = point => [...pokemon].sort((a, b) => b.y - a.y).find(p => {
        const a = p.anim, w = a.fw * SPRITE_SCALE / 2, h = a.fh * SPRITE_SCALE;
        return Math.abs(point.x - p.x) < w * 0.7 && point.y < p.y + 6 && point.y > p.y - h * 0.75;
    });
    const onMove = e => { mouse = toWorld(e); hovered = at(mouse); canvas.style.cursor = hovered ? "pointer" : "default"; };
    const onLeave = () => { mouse = null; hovered = null; };
    const onClick = e => {
        const p = at(toWorld(e));
        if (!p) return;
        p.dir = FRONT; // se tourne vers toi
        p.rest(4);
        component.invokeMethodAsync("OnPokemonClicked", p.id);
    };
    canvas.addEventListener("pointermove", onMove);
    canvas.addEventListener("pointerleave", onLeave);
    canvas.addEventListener("click", onClick);

    // ---------- Comportement ----------
    const randomSpot = () => ({ x: random(MARGIN_X + 10, worldW - MARGIN_X - 10), y: random(MARGIN_TOP + 10, WORLD_H - MARGIN_BOTTOM - 10) });
    const friendsOf = p => pokemon.filter(o => o !== p && o.family === p.family);

    /** Nouvelle intention quand le Pokémon a fini ce qu'il faisait. */
    function decide(p, night) {
        const { activity, sleepy, sociable, curious } = p.personality;
        const friends = friendsOf(p);
        if (p.energy < 0.22 + night * 0.4 + sleepy) {
            // Fatigué : il va dormir près d'un congénère qui dort, sinon sur place.
            const sleeper = friends.filter(f => f.state === "sleep").sort((a, b) => distance(p, a) - distance(p, b))[0];
            if (sleeper && Math.random() < sociable && distance(p, sleeper) > 30) {
                const side = p.x < sleeper.x ? -1 : 1;
                p.walkTo(sleeper.x + side * random(24, 34), sleeper.y + random(-8, 8), "sleep");
            } else {
                p.sleep();
            }
            return;
        }
        if (mouse && distance(p, mouse) < 200 && Math.random() < 0.6 * curious) {
            // Curieux : il vient voir le pointeur de près, puis le regarde.
            if (distance(p, mouse) > 45) {
                const angle = Math.atan2(p.y - mouse.y, p.x - mouse.x);
                p.walkTo(mouse.x + Math.cos(angle) * 32, mouse.y + Math.sin(angle) * 24, "curious");
            } else {
                p.face(mouse);
                p.rest(random(2, 5));
            }
            return;
        }
        const roll = Math.random();
        const nearest = friends.sort((a, b) => distance(p, a) - distance(p, b))[0];
        if (nearest && distance(p, nearest) < 70) {
            // Avec un congénère : il reste un moment avec lui (se repose face à lui, ou flâne autour).
            if (roll < 0.45 + 0.35 * sociable) {
                if (Math.random() < 0.55) {
                    p.face(nearest);
                    p.rest(random(3, 8) / activity);
                } else {
                    const angle = random(0, Math.PI * 2);
                    p.walkTo(nearest.x + Math.cos(angle) * random(26, 50), nearest.y + Math.sin(angle) * random(16, 34), "stroll");
                }
                return;
            }
        } else if (friends.length && roll < 0.1 + 0.45 * sociable) {
            // Rendre visite à un Pokémon de sa famille, de préférence proche.
            const friend = friends[Math.floor(Math.random() ** 2 * friends.length)];
            const angle = random(0, Math.PI * 2);
            p.walkTo(friend.x + Math.cos(angle) * 30, friend.y + Math.sin(angle) * 20, "visit");
            return;
        }
        if (Math.random() < 0.55) {
            // Explorer un endroit du parc, ni trop près ni trop loin (tiré dans le parc : pas d'entassement contre les bords).
            let spot = randomSpot();
            for (let tries = 0; tries < 12 && !(distance(p, spot) > 60 && distance(p, spot) < 240); tries++) spot = randomSpot();
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
            if (p.energy >= p.wakeAt && (night < 0.5 || chance(0.002)(dt))) { // la nuit, rares réveils
                p.dir = FRONT;
                p.rest(random(1.5, 3)); // se réveille, regarde devant lui un moment
            }
            return;
        }

        // Curiosité : un Pokémon éveillé près du pointeur se tourne vers lui, et parfois s'en approche.
        const nearMouse = mouse && distance(p, mouse) < 140;
        if (nearMouse && p.state === "idle") {
            p.face(mouse);
            if (chance(0.12 * curious)(dt) && distance(p, mouse) > 40)
                p.walkTo(mouse.x + random(-20, 20), mouse.y + random(15, 30), "curious");
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

        // Marche vers sa cible, en s'écartant des autres.
        p.energy = Math.max(0, p.energy - dt * ENERGY.walk * activity);
        let dx = p.target.x - p.x, dy = p.target.y - p.y;
        const remaining = Math.hypot(dx, dy);
        // Ralentit en approchant de sa cible (pas d'arrêt net).
        const speed = WALK_SPEED * activity * (p.intent === "sleep" ? 0.7 : 1) * clamp(remaining / 18, 0.35, 1);
        if (remaining < 2.5) {
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
        dx /= remaining; dy /= remaining;
        for (const o of pokemon) {
            const d = distance(p, o);
            if (o !== p && d < 22 && d > 0) { dx += (p.x - o.x) / d * 0.6; dy += (p.y - o.y) / d * 0.6; }
        }
        const norm = Math.hypot(dx, dy) || 1;
        // La vitesse rejoint progressivement celle voulue : virages arrondis, démarrages en douceur.
        const ease = Math.min(1, dt * 6);
        p.vx += (dx / norm * speed - p.vx) * ease;
        p.vy += (dy / norm * speed - p.vy) * ease;
        p.x = clamp(p.x + p.vx * dt, MARGIN_X, worldW - MARGIN_X);
        p.y = clamp(p.y + p.vy * dt, MARGIN_TOP, WORLD_H - MARGIN_BOTTOM);
        // Regarde où il va, et ne change de direction que franchement (pas de tremblement entre deux directions voisines).
        const moving = Math.hypot(p.vx, p.vy);
        if (moving > 4) {
            const [fx, fy] = vectorOf(p.dir);
            if ((fx * p.vx + fy * p.vy) / moving < 0.85) p.dir = directionOf(p.vx, p.vy);
        }
        // Petites pauses en chemin, et abandon si quelqu'un lui barre la route.
        p.progress.time += dt;
        if (p.progress.time > 2) {
            if (p.progress.distance - remaining < 10) { p.rest(random(1, 2)); return; }
            p.progress = { time: 0, distance: remaining };
        }
        if (p.intent !== "sleep" && remaining > 40 && chance(0.04)(dt)) {
            const target = p.target, intent = p.intent;
            p.rest(random(0.4, 1.2));
            p.timer = random(0.4, 1.2);
            p.resume = { target, intent };
        }
    }

    // ---------- Dessin ----------
    function draw(night) {
        context.imageSmoothingEnabled = false;
        context.setTransform(1, 0, 0, 1, 0, 0);
        context.clearRect(0, 0, canvas.width, canvas.height);
        context.setTransform(scale, 0, 0, scale, 0, 0);
        if (background.complete && background.naturalWidth) {
            // Le fond se répète en largeur, centré (un parc large garde la boîte au milieu).
            const offset = ((worldW - BACKGROUND_W) / 2) % BACKGROUND_W;
            for (let x = offset - BACKGROUND_W * Math.ceil(offset / BACKGROUND_W); x < worldW; x += BACKGROUND_W)
                context.drawImage(background, Math.floor(x), 0, BACKGROUND_W + 1, WORLD_H);
        }

        // Les sprites se dessinent en pixels d'écran : position arrondie au pixel d'écran (déplacement continu, pas par
        // bonds d'un pixel du parc) et chaque pixel du sprite sur un nombre entier de pixels d'écran (pas d'ondulation).
        context.setTransform(1, 0, 0, 1, 0, 0);
        const k = Math.max(1, Math.round(SPRITE_SCALE * scale));
        for (const p of [...pokemon].sort((a, b) => a.y - b.y)) { // les plus bas passent devant
            const anim = p.anim;
            const w = anim.fw * k, h = anim.fh * k;
            const x = Math.round(p.x * scale), y = Math.round(p.y * scale);
            // Ombre au sol, puis l'image : SpriteCollab centre le Pokémon dans chaque image, les pieds un peu sous le centre.
            context.fillStyle = "rgba(0, 0, 0, 0.18)";
            context.beginPath();
            context.ellipse(x, y, Math.min(w, 48 * k / SPRITE_SCALE) * 0.28, 4.5 * scale, 0, 0, Math.PI * 2);
            context.fill();
            const row = Math.min(p.state === "sleep" ? 0 : p.dir, anim.rows - 1);
            context.drawImage(anim.image, p.frame * anim.fw, row * anim.fh, anim.fw, anim.fh,
                x - Math.round(w / 2), y - Math.round(h / 2) - 6 * k, w, h);
            if (p.state === "sleep") drawZzz(p, h / scale);
        }
        context.setTransform(scale, 0, 0, scale, 0, 0);

        if (night > 0) {
            context.fillStyle = `rgba(16, 24, 72, ${0.38 * night})`;
            context.fillRect(0, 0, worldW, WORLD_H);
        }
        if (hovered) drawName(hovered);
    }

    function drawZzz(p, h) {
        context.save();
        context.setTransform(scale, 0, 0, scale, 0, 0);
        const t = p.zzz % 2.4;
        context.font = "bold 11px sans-serif";
        context.fillStyle = `rgba(255, 255, 255, ${Math.min(1, 2.4 - t) * 0.85})`;
        context.fillText("z", p.x + 8 + t * 6, p.y - h * 0.35 - t * 10);
        if (t > 1.2) context.fillText("Z", p.x + 14 + (t - 1.2) * 6, p.y - h * 0.35 - (t - 1.2) * 10 - 6);
        context.restore();
    }

    function drawName(p) {
        const h = p.anim.fh * SPRITE_SCALE;
        context.font = "bold 12px sans-serif";
        const width = context.measureText(p.nickname).width + 10;
        const x = clamp(p.x - width / 2, 2, worldW - width - 2), y = Math.max(2, p.y - h * 0.75 - 18);
        context.fillStyle = "rgba(0, 0, 0, 0.6)";
        context.beginPath();
        context.roundRect(x, y, width, 16, 4);
        context.fill();
        context.fillStyle = "#fff";
        context.fillText(p.nickname, x + 5, y + 12);
    }

    // ---------- Boucle ----------
    function step(dt, night) {
        for (const p of pokemon) {
            if (p.state === "idle" && p.resume && p.timer <= dt) { // reprend sa marche après une petite pause
                const { target, intent } = p.resume;
                p.resume = null;
                p.walkTo(target.x, target.y, intent);
            }
            update(p, dt, night);
            p.advanceFrame(dt * 1000);
        }
    }

    function tick(now) {
        const dt = Math.min(0.1, (now - last) / 1000); // onglet en arrière-plan : pas de bond au retour
        last = now;
        const night = nightLevel();
        step(dt, night);
        draw(night);
        frameRequest = requestAnimationFrame(tick);
    }
    frameRequest = requestAnimationFrame(tick);

    return {
        /** Pokémon du parc : ceux qui restent gardent leur place, les nouveaux arrivent à un endroit libre. */
        async setPokemon(list) {
            const kept = pokemon.filter(p => list.some(i => i.id === p.id));
            const arriving = list.filter(i => !kept.some(p => p.id === i.id));
            const loaded = await Promise.all(arriving.map(async info => {
                try { return { info, anims: await loadSheets(info.spriteFolder) }; }
                catch { return null; } // sprites absents (images pas récupérées) : ce Pokémon n'apparaît pas
            }));
            const placed = [...kept];
            for (const item of loaded.filter(l => l)) {
                let spot;
                for (let tries = 0; tries < 30; tries++) {
                    spot = { x: random(MARGIN_X + 20, worldW - MARGIN_X - 20), y: random(MARGIN_TOP + 20, WORLD_H - MARGIN_BOTTOM - 10) };
                    if (placed.every(o => distance(o, spot) > 40)) break;
                }
                const p = new Pokemon(item.info, item.anims, spot.x, spot.y);
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
            const night = hour === undefined ? nightLevel() : nightLevel(new Date(2000, 0, 1, Math.floor(hour), (hour % 1) * 60));
            for (let t = 0; t < seconds; t += 1 / 30) step(1 / 30, night);
            draw(night);
            return pokemon.map(p => ({ id: p.id, species: p.species, state: p.state, intent: p.intent, x: Math.round(p.x),
                y: Math.round(p.y), energy: Math.round(p.energy * 100) / 100 }));
        },
        dispose() {
            cancelAnimationFrame(frameRequest);
            observer.disconnect();
            window.removeEventListener("resize", resize);
            canvas.remove();
        },
    };
}
