# Images des Pokémon et des objets

README : « Images des Pokémon », « Mise en ligne » (Images), « Lancer en local ».

## Où elles sont
- **Hors du dépôt Git et de la publication** : sur le VPS (`~/monsuta-potaru-images`, monté sur `wwwroot/images`, rempli
  à chaque déploiement de la CD et par `deploy\images-serveur.cmd`), copiées en local par `outils\recuperer-images.cmd`
  (SSH : c'est l'utilisateur qui le lance). Ne jamais les remettre dans Git.
- Servies depuis le disque par `UseStaticFiles` (`Program.cs`) : `MapStaticAssets` ne connaît que les fichiers de la
  compilation, d'où aussi `Content Remove` dans le `.csproj`.
- Rien n'est chargé depuis GitHub par le navigateur.
- Exception : `parc/clairiere.png` (fond du parc) ne vient pas de `images.py` mais de `outils/clairiere.py`, envoyée par
  `deploy\fond-parc.cmd` (voir `parc.md`).

## Noms des fichiers
- `pokemon/<numéro d'espèce>.png`, `<numéro>-<forme>.png` (nom de forme de PokéAPI), `femelle/`, et les mêmes sous
  `pokemon/chromatique/`.
- `objets/<numéro d'objet>.png` (numéros de PKHeX, ceux de `SaveBag`), `ct-<type>.png`.
- Un `inconnu.png` par dossier, affiché en remplacement (attribut `data-remplacement`, géré par `wwwroot/js/app.js` ;
  jamais de `onerror`) ; composant `ItemSprite` pour les objets.

## `outils/images.py`
- Python seul, garde les fichiers présents. Le relancer après une mise à jour de PKHeX.Core (nouvelles espèces), en
  changeant la version en tête du script.
- Nouveau type d'image : l'ajouter au script ; le prochain déploiement (« Run workflow » de « CD ») les télécharge sur le VPS.
- Pas d'API GitHub (limitée sans jeton) : seulement des fichiers bruts, sources essayées dans l'ordre (404 → la suivante),
  sans lister les dossiers des dépôts.
- Sprites animés Donjon Mystère (SpriteCollab, CC BY-NC 4.0) : `wwwroot/images/donjon/`, dossiers de SpriteCollab
  (`<espèce sur 4 chiffres>/<forme>/<chromatique 0001>/<femelle 0002>`), `python outils/images.py donjon` (3 min 30,
  ~13 000 fichiers, 85 Mo). Seules Idle, Walk et Sleep (`DONJON_ANIMATIONS`), avec les animations qu'elles copient
  (`<CopyOf>`) ; `AnimData.xml` réduit à ces animations ; `credits.txt` : auteurs.

## Choix de l'image (`PokemonSprites`)
- Parmi les fichiers présents (liste lue au démarrage, `PokemonSprites.Load`) : nom de forme de PKHeX (selon le jeu du
  Pokémon : `EntityContext`) mis en forme, puis les noms de PokéAPI (`Aliases`, `Suffixes`) ; sinon l'image de l'espèce.
- Méga-Évolution : `ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb` ; PKHeX ne marque comme méga qu'une forme jumelle
  (« M-Mega », pas « F-Mega »).
- `BankPokemon.Gender` et `HeldItem` (pour l'image) remplis au démarrage pour les Pokémon plus anciens
  (`BankService.FillSpriteDetailsAsync`).

## Tests
- `PokemonSpritesTests` (formes, femelles, Méga, liste des formes de PKHeX sans image), `RealSavesTests` (chaque espèce,
  chaque objet des vraies sauvegardes) : exigent les images (`WebRootWithImages`, échec clair sinon).
- Une nouvelle image : tester son chemin, pas sa présence (la CI copie les images depuis le VPS, qui ne les reçoit qu'au
  déploiement, lancé après la CI du commit).
