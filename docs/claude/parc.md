# Parc

README : « Parc ». Page `Park.razor`, service `ParkService`, affichage et animation `wwwroot/js/parc.js` (Phaser).

Projet à venir (refuge, agrandissements, IA par espèce) : `docs/projet-parc.md`.

## Sprites
- `PokemonSprites.DonjonFolder` (forme, Méga, chromatique, femelle). Formes de SpriteCollab (`images/donjon/formes.json`,
  écrit par `outils/images.py donjon` depuis son `tracker.json`) rapprochées des noms de PKHeX : nom compacté (« Mega X »
  ↔ « Mega_X »), noms de PokéAPI, mots retirés à la fin (« Paldea Combat » ↔ « Paldea »), puis seul nom qui commence
  ainsi (« Dusk » ↔ « Dusk_Mane »). ~240 formes de PKHeX trouvent leur sprite ; les autres n'en ont pas encore chez
  SpriteCollab (casquettes de Pikachu, coupes de Couafarel...).
- Une espèce peut aller au parc si `images/donjon/<espèce sur 4 chiffres>/AnimData.xml` existe (`HasDonjonSprites`).
- SpriteCollab centre chaque image sur le même point, les pieds 4 à 10 px sous le centre (le dessin remonte l'image de
  6 px de sprite pour poser les pieds sur l'ombre).

## Carte (Tiled, pack Cute Fantasy)
- `wwwroot/parc/parc-1.json` (dans Git : numéros de tuiles seulement), écrite par `outils/carte-parc.py <carte .tmx>`
  (exporte le `.tmx` avec `tiled.exe --embed-tilesets --export-map json`, vérifie, copie les planches). Planches dans
  `images/parc/tuiles/<nom du fichier du pack>` : **jamais dans Git** (licence : ni redistribution ni revente, même
  modifié), envoyées par `deploy\fond-parc.cmd`, rapatriées par `recuperer-images.cmd`. Sans elles : herbe unie (`GRASS`).
- Pack : `C:\Sources\Asset_Cute_Fantasy` (+ Desert, Dungeons, ShroomLands, Volcano), tuiles 16 px. Les jeux de couleurs
  vont par numéro : `Grass_Tiles_2`, `Grass_2_Middle` et `Water_Tile_2` ont la même herbe.
- Cartes de l'utilisateur : `C:\Sources\Parcs` (`Parc_1.tmx` = la carte du site ; `generer_parc.py`, hors du dépôt, l'a
  générée : bordures d'eau et de chemin automatiques, forêt en quinconce). Tiled 1.12 installé (`C:\Program Files\Tiled`,
  `tmxrasterizer.exe` rend une carte en PNG pour vérifier).
- Calques lus, **dessinés une seule fois** au démarrage dans deux `RenderTexture` : « Sol » + « Décors » (profondeur 0),
  « Devant » + brouillard hors « Départ » (3 ; feuillages : cachent aussi la tête d'un Pokémon devant un arbre, limite
  connue). Entre les deux : ombres (1,9), Pokémon (2 + ligne). Nuit (4, cachée le jour), nom et « z » (6).
  « Collisions » : toute tuile = case bloquée, jamais dessiné. « Zones » : rectangle « Départ » (les Pokémon y restent).
  Agrandissements à venir : redessiner les deux images.
- `ParkMapTests` : calques en CSV, jeux de tuiles incorporés avec un simple nom de fichier, « Départ » dans la carte,
  toutes ses cases libres reliées (même règle de diagonale que `parc.js`).

## Affichage (Phaser 3.90.0, `wwwroot/lib/phaser/phaser.esm.min.js`)
- Importé par `parc.js` (module ES, CSP : rien à ajouter ; `img-src blob:` déjà permis pour le chargeur de Phaser ; son
  `new Function("return this")` est dans un `try`). Phaser 4 existe (4.2.1 le 2026-10-30), pas adopté.
- Netteté : canvas en pixels physiques (`scale.zoom = 1 / devicePixelRatio`, `Scale.NONE`), caméra à un zoom entier `k`
  (hauteur = `k × 320`, la zone « Départ ») : chez l'utilisateur `k` = 3. Sprites : `spriteK = round(k × 2/3)` pixels
  d'écran par pixel de sprite (jamais moins de 2 si `k` ≥ 2), positions arrondies au pixel d'écran (`snap`). Il a voulu
  les sprites 20 % plus petits que ×3 ; 2,4 n'étant pas net, il a choisi ×2. Ne pas revenir à un étirement.
- Glisser : au-delà de 6 px CSS, un appui devient un glissement (pas de clic) ; `pointerupoutside` aussi ; vue bornée à la
  carte par la caméra. `touch-action: none` sur le canvas ; `.park-empty` laisse passer les appuis (sauf son bouton).
- Zoom (`zoomTo`, `applyZoom`) : crans entiers de ×1 à `maxZoom` (zoom de départ), point visé gardé fixe. Limites de la
  caméra recalculées à chaque zoom : Phaser colle la carte à gauche quand la vue est plus large qu'elle (`clampX`), d'où
  des limites élargies et centrées. Hors carte : `OUTSIDE` (couleur de fond) ; sous la carte : rectangle `GRASS`.
- Boutons `.park-controls` créés par `parc.js` dans le conteneur (DOM, pas Blazor ; icônes SVG de Bootstrap Icons en
  `innerHTML`, pas de script : CSP respectée). Plein écran : `requestFullscreen` sur le conteneur (`.park-stage:fullscreen`),
  `fullscreenchange` → `resize` (zoom de départ recalculé sur la hauteur de l'écran). Molette : seulement en plein écran,
  cumulée par 100 (pavé tactile). Pincement : `activePointers: 2`, un cran par écart ×1,35.
- **Le panneau de navigateur de l'app refuse le plein écran** (« Permissions check failed », même sur un vrai clic) : pour
  l'essayer, simuler (`Object.defineProperty(document, "fullscreenElement", …)`, conteneur en `position: fixed; inset: 0`,
  puis événement `fullscreenchange`), ou demander à l'utilisateur.
- Textures : planches de SpriteCollab ajoutées à Phaser (`textures.addImage`, une image par case) ; textes à
  `resolution: k` (nets à la caméra).

## Animation et comportement (`parc.js`)
- Unités : pixels de la carte (16 par tuile). Réglages en tête du fichier (`ENERGY`, `WALK_SPEED`...) et dans `decide` /
  `update` ; distances divisées par 1,5 lors du passage à la carte (anciennes unités : 1,5 par pixel).
- `Ground` : cases libres, `nearestFree`, `path` (A* 8 directions sans couper les coins, lissé en lignes droites,
  vérifiées tous les 4 px). En marchant, une case bloquée n'est jamais franchie (glisse le long, sinon s'arrête).
- Fluidité : la vitesse rejoint progressivement celle voulue, freine à l'arrivée ; l'animation de marche suit la vitesse.
- **Pas de déchets à chaque image** (l'utilisateur a senti des saccades : ramasse-miettes) : 2 Mo/s à 144 Hz suffisent à en
  causer. Mesuré le 2026-10-30 (30 Pokémon, 2750 × 960) : calques de tuiles redessinés + ombres en `Graphics` = 50-60 Ko
  et 0,6 ms par image ; carte en `RenderTexture`, ombres en images toutes faites (`shadowOf`, adoucies, en pixels
  d'écran), profondeur changée seulement au changement de ligne, nom redessiné seulement quand il change, heure lue une
  fois par seconde, A* sur des tableaux gardés (`Ground`) = 2-5 Ko et 0,18 ms. Ne pas remettre de `Graphics` vidé et
  redessiné à chaque image, ni de calque de tuiles affiché.
- Sprites absents (images pas récupérées) : le Pokémon n'apparaît pas, sans erreur.

## Tester
- **Après chaque modification de `parc.js`, recharger le parc et lire la console** : `node --check` ne voit que la
  syntaxe. Une constante passée par erreur dans un commentaire (`NIGHT is not defined`, 2026-10-30) arrêtait `build` :
  carte collée à gauche, aucun Pokémon, chez l'utilisateur.
- Panneau du navigateur de test souvent **masqué** : Phaser ne dessine que pendant les captures (`camera.worldView` pas à
  jour : `view()` la recalcule). Pour juger le comportement : `import("/js/parc.js?v=…")` dans la page, `create` sur un
  `div` (en `position: fixed`) avec un faux composant `{ invokeMethodAsync }`, `setPokemon`, puis `simulate(secondes,
  heure)` (état, `free` : jamais sur une case bloquée) et `view()` (caméra). Clic et glissement : `MouseEvent` sur le canvas
  (`mouseup` sur `window` = relâché dehors). Netteté : copier le canvas juste après un `game.step(...)` (pas de
  `requestAnimationFrame` : panneau masqué) et compter les blocs `k × k` uniformes. Coût et déchets : exposer `game`
  le temps de l'essai, enchaîner des `game.step(performance.now(), 16.7)` (fin par un `gl.readPixels` pour attendre le
  GPU), déchets par `performance.memory.usedJSHeapSize` relevé tous les 50 pas (après un `setTimeout`, valeurs
  grossières : répéter).
- Sans les images : faux fichiers `MonsutaPotaru/wwwroot/images/donjon/<0001…1025>/AnimData.xml` (contenu `<AnimData/>`)
  pour que le parc accepte des Pokémon ; supprimer `wwwroot/images` après (vérifier d'abord qu'il ne contient que ces
  faux fichiers). Pour `ParkServiceTests` : `PokemonSprites.Load` sur un dossier temporaire du même genre, le temps de
  l'essai, puis remettre le test (`Sprites_AndFamilies` exige les vrais fichiers).
- Fenêtre de choix : « Seulement ceux du parc » fige la liste des choisis à l'allumage (un Pokémon décoché reste affiché) ;
  « Tout retirer » ne change rien avant « Valider » ; « Vider le parc » : `ParkService.ClearAsync`, après confirmation.
