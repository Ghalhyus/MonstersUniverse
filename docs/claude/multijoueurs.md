# Multijoueurs : pages des joueurs, amis, échanges

README : « Multijoueurs », « Échanges ».

## Pages vues par un visiteur
- Collection, Parc, Inventaire et Living dex (liste et page d'un living dex) ont une seconde route `/joueurs/{Pseudo}/...` :
  `Visitor` (paramètre `Pseudo`), compte trouvé par `PlayerService.FindAsync(pseudo, page)` à la place du sien.
- En visiteur :
  - tout le contenu dans le `else` d'un `@if (Visitor && player is null)` ;
  - boutons d'action masqués **et** méthodes d'action qui sortent tout de suite (`if (Visitor) return;`) ;
  - ni journal, ni noms de fichiers (`LabelAndFile` → `Label`, `PublicSaveLabel` des living dex), ni ID de dresseur
    (`PokemonCard.HideTrainerId`), ni téléchargement.
- Nouvelle action ou nouvelle donnée sur ces pages : décider ce qu'en voit un visiteur.
- Le premier rendu d'un visiteur a lieu avant que le joueur soit trouvé : ce qui touche aux éléments de la page dans
  `OnAfterRenderAsync` (parc, grille de l'inventaire) attend que le contenu soit affiché (`engineStarted`, `contentStarted`).

## Visibilité
- Trois valeurs (`ProfileAudience` : tout le monde, amis seulement, personne). `PlayerService.FindAsync` prend le compte
  qui regarde : un ami (ou le joueur lui-même) voit les pages d'un profil « Amis seulement » ; « Personne » reste
  introuvable, même des amis.
- Profil caché, page non visible, compte sans pseudo : même réponse (`PlayerHidden`), administrateurs compris.

## Amis
- `FriendService` ; amitiés en double (une ligne par compte).
- Une action qui change les demandes passe par `FriendService.Notify` (le menu relit son compteur).

## Échanges
- `TradeService` : mécanique, testée sans navigateur. `TradeHub` : déroulé en direct, singleton ; tout changement passe
  par ses méthodes sous verrou, les pages s'abonnent à `TradeSession.Changed`.
- La page du second à valider écrit les deux sauvegardes.
- Confirmation de départ (`NavigationLock`) : se teste en remplaçant `window.confirm` dans la page.

## Valeurs trafiquées (Blazor Server)
- La valeur d'un événement (`<select>`, champ, `[JSInvokable]`) vient du navigateur. Un service appelé avec l'identifiant
  d'un autre compte revérifie la relation lui-même (amitié : `FriendService.RelationAsync`, visibilité :
  `PlayerService.FindAsync`), sans se fier à la liste affichée.
- Test d'un `<select>` : changer la `value` d'une option dans la page, puis déclencher `change`.
- Test d'un argument de `[JSInvokable]` (parc : `OnPokemonClicked`) : remplacer `WebSocket.prototype.send` en gardant
  l'original dans une variable, réécrire l'argument dans le message (« [137] » → « [136] », même longueur), puis
  **remettre l'original** (le supprimer casse la page). Le Pokémon du parc se clique en cherchant ses pixels dans le
  canvas (`getImageData`).

## Essai à deux dans le navigateur
Cookies partagés entre onglets : un joueur sur `localhost:5299`, l'autre sur `127.0.0.1:5299` (un cookie est propre au
nom d'hôte).
