# Parc

README : « Parc ». Page `Park.razor`, service `ParkService`, affichage et animation `wwwroot/js/parc.js` (Phaser).

Projet à venir (refuge, plusieurs parcs, IA par espèce) : `docs/projet-parc.md`.

## Agrandissements (`ParkService`)
- Capacité `CapacityOf(ParkExpansions)` = 25 + 25 par agrandissement, `MaxExpansions` = 3 (colonne
  `AspNetUsers.ParkExpansions`, migration `ParkExpansions`). `ExpandAsync` : dans une transaction, `ExecuteUpdate`
  conditionnel sur la taille (`< MaxExpansions`) puis débit conditionnel du porte-monnaie ; débit refusé : `RollbackAsync`
  (la taille revient). Ligne de journal `WalletOperationKind.ExpandPark` (libellé dans `Inventory.razor`).
- `SetAsync` au-dessus de la capacité : accepté seulement si les Pokémon voulus sont tous déjà au parc (anciens parcs de
  30). La fenêtre de choix suit la même règle (`!c.InPark`).
- Après l'achat, `Park.razor` rafraîchit `WalletState` (solde du menu) et redémarre le parc (`StartEngine` : `dispose`
  puis `create` avec `size = ParkExpansions + 1`).
- **Planches gardées par le module** (`sheets`) d'un jeu de Phaser à l'autre : une texture doit être recréée dans chaque
  jeu (`shadowOf` vérifie `textures.exists`). Sans : la texture « manquante » de Phaser, un carré noir à la place des
  ombres (vu au redémarrage après un achat, et en revenant sur la page sans la recharger).

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
- `wwwroot/parc/parc-1-1.json` à `parc-1-4.json`, une par taille (dans Git : numéros de tuiles seulement), écrites par
  `outils/carte-parc.py <carte .tmx>` (nom tiré du fichier : `Parc_1-2.tmx` → `parc-1-2` ; exporte le `.tmx` avec
  `tiled.exe --embed-tilesets --export-map json`, vérifie, copie les planches). Planches dans
  `images/parc/tuiles/<nom du fichier du pack>` : **jamais dans Git** (licence : ni redistribution ni revente, même
  modifié), envoyées par `deploy\fond-parc.cmd`, rapatriées par `recuperer-images.cmd`. Sans elles : herbe unie (`GRASS`).
- Pack : `C:\Sources\Asset_Cute_Fantasy` (+ Desert, Dungeons, ShroomLands, Volcano), tuiles 16 px. Les jeux de couleurs
  vont par numéro : `Grass_Tiles_2`, `Grass_2_Middle` et `Water_Tile_2` ont la même herbe.
- Cartes de l'utilisateur : `C:\Sources\Parcs` (`Parc_1-1.tmx` à `Parc_1-4.tmx` = les cartes du site, sorties telles
  quelles de `outils/generer-parc.py` : les corriger dans le script, pas dans Tiled ; bordures d'eau et de chemin
  automatiques, forêt en quinconce, graine fixe ; `Parc_1.tmx` : l'ancienne carte unique). Python du système sans Pillow :
  un `venv` dans un dossier temporaire. Lancer
  `python outils/generer-parc.py C:/Sources/Asset_Cute_Fantasy/Cute_Fantasy C:/Sources/Parcs <dossier des aperçus>`,
  puis `python outils/carte-parc.py C:/Sources/Parcs/Parc_1-<n>.tmx` pour chacune ; comparer l'ancien et le nouveau JSON
  calque par calque (seules les cases voulues doivent changer). Objets de plusieurs lignes (bancs, gros rochers) : toutes
  leurs lignes bloquées (le 2026-10-31, le dossier des bancs ne l'était pas : on marchait dessus).
- **Tailles** (`ZONES` du générateur, rectangle « Parc » de chaque carte ; « Départ » reste la première, la dernière est
  la carte entière : l'utilisateur veut retrouver toute la carte d'origine une fois tout débloqué) : le parc entier
  est tiré une fois (objets enregistrés dans `objets`, tuiles et cases bloquées), puis `taille(zone, libres_avant)` :
  forêt hors de la zone avec une **lisière irrégulière** (0 à 2 cases vers l'intérieur, `LISIERE` ; moins que l'écart de
  5 cases au moins entre deux zones), qui contourne les objets et plans d'eau tenant dans la zone ; l'eau coupée devient forêt ;
  arbres de forêt tirés d'après leur case (les mêmes d'une taille à l'autre) quand les deux cases du tronc sont boisées
  (feuillage qui déborde en bas sur la zone : un objet de la zone qui le croise n'est pas posé) ; buissons sur le reste ;
  toute la forêt bloquée, sauf le bord sans arbre de la forêt d'origine (bordure, bosquets) dans la zone : de l'herbe où
  l'on marche, comme sur la carte du parc entier ; objets gardés s'ils sont tout entiers dans la zone et ne bloquent aucune case libre de la
  taille d'avant ; poches d'herbe enfermées → buissons. Le script s'arrête si une taille bloque une case libre de la
  précédente (`ParkMapTests` le vérifie aussi). Libres : 362, 999, 1 784, 3 062 cases (14 à 30 par
  Pokémon).
- Tiled 1.12 installé (`C:\Program Files\Tiled`, `tmxrasterizer.exe` rend une carte en PNG pour vérifier).
- Calques lus, **dessinés une seule fois** au démarrage : « Sol » + « Décors » dans une `RenderTexture` (profondeur 0),
  brouillard hors « Parc » dans une autre (3 ; `FOG.steps` marches superposées, une par case à partir de la deuxième :
  la lisière reste claire). Ombres (1,9). Nuit (4, cachée le jour), nom et « z » (6).
  « Collisions » : toute tuile = case bloquée, jamais dessiné. « Zones » : « Départ » (vue de départ : sa hauteur donne
  le zoom, son centre la position) et « Parc » (`ground.area` : les Pokémon y restent ; sans lui, « Départ »).
  `create(..., { size })` charge `parc-1-{size}.json` (`MAP_URL`).
- **Feuillages triés** (`foliage`) : une tuile de « Devant » appartient au tronc = première case sous elle (même colonne)
  qui bloque ou n'a plus de feuillage. Profondeur `depthOf(ligne du tronc × 16 + 8,5)`, comme les Pokémon
  (`depthOf(pieds)`) : pieds au-dessus du milieu du tronc = derrière. Morceaux = feuillages d'une même ligne de troncs à
  moins de 4 cases d'écart (84 à 122 avec la forêt dense, texture de 1280 × 1272 au plus ; 0,04 ms par image sans
  Pokémon, mesuré le 2026-10-31), rangés par étagères dans une seule `DynamicTexture` « feuillages » (une
  `Image` par morceau). Dessin d'un morceau : toutes les tuiles du calque `visible = false` sauf les siennes,
  `setSkipCull(true)` (dessiné ailleurs qu'à sa place). Dans la forêt, deux arbres en quinconce se chevauchent : le haut
  d'un feuillage peut prendre le tronc d'un voisin (cases bloquées, aucun Pokémon : sans effet).
- `ParkMapTests` : calques en CSV, jeux de tuiles incorporés avec un simple nom de fichier, « Départ » dans la carte,
  toutes ses cases libres reliées (même règle de diagonale que `parc.js`).

## Affichage (Phaser 3.90.0, `wwwroot/lib/phaser/phaser.esm.min.js`)
- Importé par `parc.js` (module ES, CSP : rien à ajouter ; `img-src blob:` déjà permis pour le chargeur de Phaser ; son
  `new Function("return this")` est dans un `try`). Phaser 4 existe (4.2.1 le 2026-10-30), pas adopté.
- Netteté : canvas en pixels physiques (`scale.zoom = 1 / devicePixelRatio`, `Scale.NONE`), caméra à un zoom entier `k`
  (hauteur = `k × 320`, la zone « Départ ») : chez l'utilisateur `k` = 3. Sprites : `spriteK = round(k × 2/3)` pixels
  d'écran par pixel de sprite (jamais moins de 2 si `k` ≥ 2), positions arrondies au pixel d'écran (`snap`). Il a voulu
  les sprites 20 % plus petits que ×3 ; 2,4 n'étant pas net, il a choisi ×2. Ne pas revenir à un étirement.
- Glisser : au-delà de 6 px CSS, un appui devient un glissement (pas de clic) ; `pointerupoutside` aussi ; vue bornée par
  la caméra à la zone « Parc » plus `VIEW_MARGIN` (6) cases de forêt (`applyZoom`). `touch-action: none` sur le canvas ; `.park-empty` laisse passer les appuis (sauf son bouton).
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

## Déplacer un Pokémon (`parc.js`, propriétaire)
- `create(container, component, { canMove })` : `Park.razor` passe `canMove = !Visitor`. Rien côté serveur (positions
  jamais enregistrées).
- `pointerdown` sur un Pokémon (`at`) : `drag.target`. Souris : au-delà de `DRAG_THRESHOLD`, `pickUp` au lieu de faire
  glisser la vue. Doigt (`pointer.wasTouch`) : `tick` soulève après `LONG_PRESS` sans avoir bougé ; avant, un glissement
  fait défiler la vue. Clic sans bouger : fiche, comme avant.
- `held` : `{ p, from, dx, dy, pointer }` ; état `"held"` (animation `Charge` par `ANIMATION_OF`, `OPTIONAL` : planche
  absente = Idle ; `update` ne fait rien), dessiné `LIFT` px plus haut, profondeur `HELD_DEPTH` (3,5 : devant le
  brouillard, sous la nuit). `carry` à chaque image (Vector2 gardé : pas de déchets) suit le pointeur même quand la vue
  défile (`edgeScroll`, `EDGE_SCROLL`). `marker` (Graphics redessiné seulement quand la case change de permise à
  interdite) : `ground.isFree`. `putDown` : case libre, sinon `from` ; puis `rest(2 à 4 s)`. Pincement : `putDown(true)`.
- Essais : `MouseEvent` (`mousedown`, `mousemove` sur le canvas, `mouseup` sur `window`) ; doigt : `resize_window`
  « mobile », recharger la page, `new Touch(...)` + `TouchEvent` sur le canvas. Panneau masqué : la caméra n'est à jour
  qu'après un dessin (capture d'écran) : sans, `getWorldPoint` vise à côté et le Pokémon n'est pas pris ; l'appui long
  attend aussi une image (`tick`).

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
- Sprites absents (images pas récupérées) : le Pokémon n'apparaît pas, sans erreur (`loadSheets` échoue sur un
  `AnimData.xml` en 404 ou sans Idle ni Walk ; avant le 2026-10-31, un 404 donnait des animations vides et `addTextures`
  plantait). Planche `Charge` absente (images d'avant le 2026-10-31) : Idle quand on le soulève.
- **`setPokemon` est asynchrone** (planches à charger) : les Pokémon retirés sont détruits **et** sortis de `pokemon`
  avant l'attente. Le 2026-10-31, ils restaient dans la liste pendant le chargement : `draw` sur un objet détruit
  (« this.scene is undefined » dans `setTexture`) arrêtait la boucle de Phaser, parc figé (Firefox, choix qui retire et
  ajoute beaucoup de Pokémon). Une liste plus récente reçue pendant le chargement l'emporte (`lists`). Essai : `setPokemon`
  sans l'attendre, puis `simulate(0.05)` pendant le chargement (le panneau masqué ne dessine pas de lui-même).

## Tester
- **Après chaque modification de `parc.js`, recharger le parc et lire la console** : `node --check` ne voit que la
  syntaxe. Une constante passée par erreur dans un commentaire (`NIGHT is not defined`, 2026-10-30) arrêtait `build` :
  carte collée à gauche, aucun Pokémon, chez l'utilisateur.
- `place(id, x, y)` (essais) : pose un Pokémon immobile ; pour vérifier un tri, en poser un derrière et un devant un arbre
  puis agrandir une partie du canvas dans un `<canvas>` en `imageSmoothingEnabled = false` (`view()` donne la caméra :
  pixel du canvas = (carte − `view().x`) × `zoom`), juste après un `requestAnimationFrame`.
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
