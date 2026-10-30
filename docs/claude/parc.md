# Parc

README : « Parc ». Page `Park.razor`, service `ParkService`, animation `wwwroot/js/parc.js`.

## Sprites
- `PokemonSprites.DonjonFolder` (forme, Méga, chromatique, femelle). Formes de SpriteCollab (`images/donjon/formes.json`,
  écrit par `outils/images.py donjon` depuis son `tracker.json`) rapprochées des noms de PKHeX : nom compacté (« Mega X »
  ↔ « Mega_X »), noms de PokéAPI, mots retirés à la fin (« Paldea Combat » ↔ « Paldea »), puis seul nom qui commence
  ainsi (« Dusk » ↔ « Dusk_Mane »). ~240 formes de PKHeX trouvent leur sprite ; les autres n'en ont pas encore chez
  SpriteCollab (casquettes de Pikachu, coupes de Couafarel...).
- Une espèce peut aller au parc si `images/donjon/<espèce sur 4 chiffres>/AnimData.xml` existe (`HasDonjonSprites`).
- SpriteCollab centre chaque image sur le même point, les pieds 4 à 10 px sous le centre (le dessin remonte l'image de
  6 px × l'échelle pour poser les pieds sur l'ombre).

## Fond (clairière)
- `images/parc/clairiere.png`, 384 × 320 = l'ancienne boîte (576 × 486 unités) en pixels de sprite, pour garder la taille
  des sprites et la surface d'avant. Refusées : bandeau de 1536 × 320 (parc à ×1, trop petit), 320 × 256 (×4, Pokémon
  trop gros et entassés). Assemblée par `outils/clairiere.py` (graine 3 sans embranchement ; Pillow
  absent de son Python : `pip install pillow`, ou `pip install --target <scratchpad>/pylib pillow` + `PYTHONPATH`). Pack
  Tiny Tales dans `C:\Users\ghalh\Downloads\Tiny Tales_2D_Overworld_2.0.1\Tiled`.
- **Jamais dans Git** (licence : pas de redistribution des tuiles ni de leurs dérivés tels quels), ni dans `images.py` (rien
  à télécharger) : `deploy\fond-parc.cmd` l'envoie dans `~/monsuta-potaru-images/parc/` (servie aussitôt, pas de
  redémarrage), `recuperer-images.cmd` la rapatrie. Sans elle : herbe unie (`GRASS`), pas d'erreur.
- Netteté : un pixel de la carte = `MAP_PIXEL` (1,5) unités du parc. La hauteur du canvas en pixels physiques est
  `k × 320` (`resize`, `k` le plus grand qui tient dans la hauteur visible : 3 chez l'utilisateur), donc `k` pixels d'écran
  par pixel de carte ; le fond se dessine en pixels d'écran. Ne pas revenir à un étirement à la hauteur disponible.
- Sprites : `spriteK = round(k × SPRITE_SIZE)` pixels d'écran par pixel de sprite (2/3, jamais moins de 2 si `k` ≥ 2) :
  ×2 chez l'utilisateur. Il les a voulus 20 % plus petits que ×3 ; 2,4 n'étant pas net, il a choisi ×2 (−33 %) plutôt que
  des pixels inégaux. `spriteScale` (unités du parc par pixel de sprite) sert au clic, au nom, aux « z » ; l'animation de
  marche suit la taille (foulée plus courte).
- `MARGIN_TOP` (100) : pieds sous la lisière (bas des arbres ~52 px de carte en médiane, buissons devant jusqu'à ~75).
  Nouvelle carte avec une autre lisière : remesurer.

## Animation (`parc.js`)
- Réglages en tête du fichier (`ENERGY`, `SPRITE_SIZE`, marges) et dans `decide` / `update`.
- Fluidité : sprites dessinés en pixels d'écran, position arrondie au pixel d'écran, échelle entière (`k`) ; la vitesse
  rejoint progressivement celle voulue (`vx`, `vy`), freine à l'arrivée ; l'animation de marche suit la vitesse
  (`WALK_ANIMATION_SPEED`). Une image coûte ~0,1 ms pour 30 Pokémon : une saccade vient du dessin, pas du calcul.
- Sprites absents (images pas récupérées) : le Pokémon n'apparaît pas, sans erreur.

## Tester
- Panneau du navigateur de test souvent **masqué** : la page ne s'anime que pendant les captures. Pour juger le
  comportement : importer le module dans la page, créer un moteur dans un `div`, appeler `simulate(secondes, heure)`
  (état de chaque Pokémon) : temps passé à dormir ou marcher, distance entre membres d'une famille, effet de la souris
  (`PointerEvent` sur le canvas). Pour voir la nuit, remplacer `window.Date` le temps de la capture.
- Sans les images : faux fichiers `MonsutaPotaru/wwwroot/images/donjon/<0001…1025>/AnimData.xml` (contenu `<AnimData/>`)
  pour que le parc accepte des Pokémon ; supprimer `wwwroot/images` après (vérifier d'abord qu'il ne contient que ces
  faux fichiers). Pour `ParkServiceTests` : `PokemonSprites.Load` sur un dossier temporaire du même genre, le temps de
  l'essai, puis remettre le test (`Sprites_AndFamilies` exige les vrais fichiers).
- Fenêtre de choix : « Seulement ceux du parc » fige la liste des choisis à l'allumage (un Pokémon décoché reste affiché) ;
  « Tout retirer » ne change rien avant « Valider » ; « Vider le parc » : `ParkService.ClearAsync`, après confirmation.
