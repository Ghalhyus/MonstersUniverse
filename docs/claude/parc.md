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

## Animation (`parc.js`)
- Réglages en tête du fichier (`ENERGY`, `SPRITE_SCALE`, marges) et dans `decide` / `update`.
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
