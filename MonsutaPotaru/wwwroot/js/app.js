// Scripts du site, chargés dans le <head> d'App.razor : la politique de sécurité (CSP, SecurityHeaders.cs) interdit tout
// script écrit dans la page, attributs onerror ou onclick compris. Ce fichier est chargé avant le corps de la page, pour
// que l'écouteur des images soit en place avant qu'elles ne se chargent.

// Image absente (à la place de onerror) : data-remplacement="images/pokemon/inconnu.png" la remplace par cette image,
// une seule fois (pas de boucle si l'image de remplacement manque aussi) ; data-cacher-si-absente la cache. Les erreurs
// d'images ne remontent pas : écoutées à la capture, sur tout le document (pages interactives comprises).
document.addEventListener("error", e => {
    const img = e.target;
    if (!(img instanceof HTMLImageElement)) return;
    if ("cacherSiAbsente" in img.dataset) {
        img.style.visibility = "hidden";
        return;
    }
    const replacement = img.dataset.remplacement;
    if (replacement && img.src !== new URL(replacement, document.baseURI).href) img.src = replacement;
}, true);

// Menu (NavMenu.razor) : sur téléphone, un clic dans le menu le referme, sauf sur un menu parent (il déplie ses
// sous-menus). La case .navbar-toggler ouvre et ferme le menu.
document.addEventListener("click", e => {
    if (!(e.target instanceof Element) || !e.target.closest(".nav-scrollable") || e.target.closest("summary")) return;
    document.querySelector(".navbar-toggler")?.click();
});

// Cadre du thème Ardoise (ArdoiseLayout.razor) : tout passe par ici, pour marcher aussi sur les pages du compte (rendu
// statique, sans Blazor interactif).
// - « Replier le menu » : attribut data-menu="replie" de la page, gardé dans un cookie qu'App.razor relit (la page
//   suivante arrive déjà repliée) ;
// - une rubrique (Sauvegardes, Multijoueurs) s'ouvre et se ferme d'un clic (classe « ouvert ») ;
// - téléphone : le bouton de la barre du haut ouvre le menu par-dessus la page (data-menu-ouvert) ; un clic sur un lien
//   ou à côté le referme ;
// - le menu du compte (élément details) se referme après un clic dedans ou à côté, et avec Échap.
document.addEventListener("click", e => {
    if (!(e.target instanceof Element)) return;
    const root = document.documentElement;
    for (const account of document.querySelectorAll(".ardoise-account[open]")) {
        if (!account.contains(e.target) || e.target.closest(".ardoise-account-menu a, .ardoise-account-menu button"))
            account.open = false;
    }
    if (e.target.closest(".ardoise-collapse")) {
        const collapse = root.dataset.menu !== "replie";
        if (collapse) root.dataset.menu = "replie"; else delete root.dataset.menu;
        document.cookie = collapse ? "menu=replie; path=/; max-age=31536000; samesite=lax" : "menu=; path=/; max-age=0; samesite=lax";
        return;
    }
    const groupTitle = e.target.closest(".ardoise-group-title");
    if (groupTitle) {
        const open = groupTitle.parentElement.classList.toggle("ouvert");
        groupTitle.setAttribute("aria-expanded", open ? "true" : "false");
        return;
    }
    if (e.target.closest(".ardoise-burger")) {
        root.toggleAttribute("data-menu-ouvert");
        return;
    }
    if (e.target.closest(".ardoise-backdrop") || e.target.closest(".ardoise-menu a")) root.removeAttribute("data-menu-ouvert");
});
document.addEventListener("keydown", e => {
    if (e.key !== "Escape") return;
    document.documentElement.removeAttribute("data-menu-ouvert");
    for (const account of document.querySelectorAll(".ardoise-account[open]")) account.open = false;
});

// Téléchargement d'un fichier envoyé par le serveur (sauvegarde modifiée, .pk5...).
window.downloadFileFromStream = async (fileName, streamRef) => {
    const blob = new Blob([await streamRef.arrayBuffer()]);
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName ?? "";
    a.click();
    URL.revokeObjectURL(url);
};

// Largeur de la fenêtre (« Ma collection ») : 0 pour une fenêtre étroite (téléphone), sinon 1 + le nombre de largeurs
// dépassées, en pixels physiques (une fenêtre plein écran sur un 3440 × 1440 compte pour 3440, quelle que soit la
// mise à l'échelle de Windows ou le zoom : les deux sont compensés par devicePixelRatio). Le composant est prévenu
// à chaque changement de tranche (fenêtre redimensionnée ou déplacée sur un autre écran, téléphone tourné).
window.watchScreenSize = (narrowWidth, windowWidths, component) => {
    const size = () => {
        if (innerWidth < narrowWidth) return 0;
        const physicalWidth = Math.round(innerWidth * devicePixelRatio);
        return 1 + windowWidths.filter(w => physicalWidth > w).length;
    };
    let last = size();
    const onResize = () => {
        const current = size();
        if (current === last) return;
        last = current;
        component.invokeMethodAsync("OnScreenSizeChanged", current);
    };
    addEventListener("resize", onResize);
    return {
        size,
        dispose: () => removeEventListener("resize", onResize),
    };
};

// Nombre de colonnes de la grille automatique d'objets (« Inventaire ») : mesuré sur une grille vide de même largeur
// (.item-grid-probe), dont le navigateur calcule les colonnes avec les règles de .item-grid. Le composant est prévenu
// à chaque changement (fenêtre redimensionnée, journal ouvert ou fermé).
window.watchGridColumns = (probe, component) => {
    const count = () => getComputedStyle(probe).gridTemplateColumns.split(" ").filter(t => t).length;
    let last = count();
    const observer = new ResizeObserver(() => {
        const current = count();
        if (current === last) return;
        last = current;
        component.invokeMethodAsync("OnGridColumnsChanged", current);
    });
    observer.observe(probe);
    return {
        count,
        dispose: () => observer.disconnect(),
    };
};

// Glisser-déposer des Pokémon : la logique est côté serveur (Bank.razor), qui marque les zones
// acceptant le Pokémon en cours de glissement (.drop-active / .drop-target). Ici, uniquement ce
// que le navigateur exige de façon synchrone (un aller-retour serveur à chaque "dragover" serait trop lent).
const dropZone = e => e.target instanceof Element ? e.target.closest(".drop-active, .drop-target") : null;
document.addEventListener("dragstart", e => {
    if (e.target instanceof Element && e.target.matches(".slot[draggable=true]")) {
        e.dataTransfer.effectAllowed = "move";
        e.dataTransfer.setData("text/plain", ""); // Firefox ne démarre pas le glissement sans données

        // Sélection multiple : image de glissement en forme de pile, avec le nombre de Pokémon emportés.
        const count = e.target.dataset.dragCount;
        if (count) {
            const ghost = e.target.cloneNode(true);
            ghost.className = "slot drag-ghost";
            ghost.style.width = e.target.offsetWidth + "px";
            ghost.style.height = e.target.offsetHeight + "px";
            const badge = document.createElement("span");
            badge.className = "drag-count";
            badge.textContent = count;
            ghost.appendChild(badge);
            document.body.appendChild(ghost);
            e.dataTransfer.setDragImage(ghost, e.offsetX, e.offsetY);
            setTimeout(() => ghost.remove());
        }
    }
});
document.addEventListener("dragover", e => {
    if (dropZone(e)) {
        e.preventDefault(); // autorise le lâcher
        e.dataTransfer.dropEffect = "move";
    }
});
document.addEventListener("drop", e => {
    if (dropZone(e)) e.preventDefault(); // pas de comportement par défaut (texte inséré dans un champ...)
});
