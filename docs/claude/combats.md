# Combats

README : « Combats ».

## Moteur
- Simulateur de Pokémon Showdown (`showdown/`, `@pkmn/sim` en version fixée ; `npm ci --prefix showdown` en local et dans
  la CI ; étape `node` du `Dockerfile`). Aucune mécanique de combat réécrite en C# : le site traduit le protocole
  (`BattleView`) et dessine l'interface.
- Nouveau format (génération) : `BattleTeam.Format`.
- `combat.js` reçoit les Pokémon **par numéros** (PKHeX) et les traduit en noms de Showdown : `byNum` préfère l'entrée
  standard de la génération (un objet « Past » de la 2e génération partage parfois son numéro : « Berry » / « Oran Berry »).
- `combat.js` ne transmet que les choix et la fin forcée. `>eval` existe dans le simulateur : **jamais**.

## Protocole
- Lignes `|split|<camp>` suivies de la version secrète (pour ce camp) puis de la publique.
- Un choix peut être refusé (« [Unavailable choice] ») : Showdown renvoie une requête corrigée, à laquelle on répond de nouveau.
- Nouvelle ligne ou nouvel effet : `BattleView` note ce qu'elle ne sait pas traduire (`Unhandled`).
- Cas rares : monter temporairement le nombre de combats de `RandomBattles_EndAndAreFullyTranslated` (300 : ~2 min 30) ;
  relire les phrases : écrire le journal d'un combat (`Steps1`) dans un fichier.
- Recensement des lignes produites par Showdown : en Node (`BattleStreams`, équipes au hasard en « gen3customgame » :
  `@pkmn/sim` n'a pas le générateur d'équipes aléatoires).

## Combat en direct
- `BattleHub` tient l'état sous `BattleSession.Gate` (le simulateur écrit sur son propre fil) ; les pages lisent par
  `Snapshot` et `Steps` (copies).
- Ordre des verrous : celui du hub, puis celui de la session, jamais l'inverse.
- Délai de choix : à partir de la requête + la durée des étapes du tour (`BattleTiming.Playback`).
- Page : `PlayAsync` ne fait rien quand il n'y a pas d'étape nouvelle (un simple « l'autre a choisi » refermerait le menu
  ouvert sous le clic du joueur).
- Résultats : `BattleRecorder` (service hébergé) écoute `BattleHub.Finished` et écrit `BattleRecords` à côté du fil du
  simulateur. Tests : `BattleRecorder.RecordAsync` appelé directement.

## Sprites (`BattleSprites`)
- Ceux du jeu **de celui qui regarde** (sa sauvegarde, connue quand son équipe est acceptée : `BattleTeamResult.Sprites`),
  dans les dossiers des living dex de 3e génération (`dos/`, `chromatique/`, `outils/images.py combats`).
- Forme qui ne change que l'image (lettre de Zarbi) : passe quand même par `forme` ; `combat.js` la donne à Showdown comme
  forme d'aspect (`cosmeticFormes`), qui la remet dans la description du Pokémon.
- Nouvelle génération de combats : ses dossiers dans `BATTLE_SPRITES` (PokéAPI a des dos jusqu'à la 5e).

## Tests
- Les tests de combat lancent le vrai simulateur (`BattleTests.Showdown()` : échec clair s'il n'est pas installé ; il faut Node 22+).

## Essai à deux dans le navigateur
- Sacha sur `127.0.0.1:5299`, Ondine sur `localhost:5299` ; chacune a une sauvegarde de 3e génération dans la base de
  test (Vert Feuille, Rouge Feu).
- Clics par `javascript_tool` (un script ne dépasse pas 45 s : découper les attentes). « Fuite » : remplacer
  `window.confirm` avant de cliquer. Le minuteur se vérifie sans rien choisir pendant ~70 s.
