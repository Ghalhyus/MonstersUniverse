# Notes pour Claude

Monsuta Potaru (anciennement PokeBank) : banque Pokémon privée (ASP.NET Core 9, Blazor Server, EF Core + SQLite, Identity,
PKHeX.Core, xUnit). Noms : « Monsuta Potaru » affiché, `MonsutaPotaru` dans le code, `monsuta-potaru` sur le serveur
(dossiers, service et volumes Docker). Restent « PokeBank » exprès : le homebrew 3DS **PokeBank Sync** (autre dépôt, à
renommer plus tard) et `SetApplicationName("PokeBank")` (clés de connexion : le changer déconnecte tout le monde).
Le fonctionnement, les règles métier et l'organisation du code sont dans le `README.md` : le lire en premier.
Ce fichier ne garde que ce que le README ne dit pas.

## Façon de travailler

- **Ne jamais commiter ni pousser** : l'utilisateur s'en charge. Lui signaler seulement les fichiers nouveaux à ajouter.
- **Nouvelle fonctionnalité** : poser d'abord les questions (pièges, choix à faire, avec une recommandation), puis
  **attendre son « go »** explicite avant d'écrire du code. Les petites corrections demandées se font directement.
- Tout en français : code commenté en français, interface qui tutoie (« Retire de l'argent… »), messages prêts à afficher.
- Son écran : 3440×1440 à 150 % de mise à l'échelle Windows. Il raisonne en **pixels physiques**
  (`innerWidth * devicePixelRatio`), pas en pixels CSS.
- Vérifier dans un vrai navigateur (instance de test, voir plus bas) avant d'annoncer qu'une fonctionnalité marche.
- Tenir le README à jour (règles métier, sauvegardes d'exemple, tableau « Organisation du code ») à chaque fonctionnalité.
- **Toute ressource venant d'un autre projet** (bibliothèque, paquet, images, données, table reprise d'un site) est citée
  dans la section « Crédits » du README, avec son usage et sa licence, dès qu'elle entre dans le projet.

## Conventions du code

- Services *scoped* à constructeur primaire, accès base par `IDbContextFactory<ApplicationDbContext>`.
- Résultats en `record` portant un message français prêt à afficher (`ItemTransferResult(bool Ok, string Message)`...).
- Écritures en transaction ; jeton de concurrence `Version` sur `StoredSave` ; débits et crédits atomiques avec
  `ExecuteUpdateAsync` conditionnel. Copie de sécurité avant/après dans `Data/backups/<id utilisateur>/`.
- Toute modification d'une sauvegarde stockée passe par `InventoryService.SaveServerChangeAsync` (trace
  `SaveModification` : règle anti-copie, voir le README).
- Tests : xUnit, base SQLite temporaire via `TestDatabase` ; sauvegardes fabriquées en mémoire (`SaveSessionTests.CreateBw2Save`,
  `CreatePlatinumSave`, `GiveItems`).
- Vraies sauvegardes de l'utilisateur dans `MonsutaPotaru.Tests/Sauvegardes/` (versionnées, exception à `*.sav` dans `.gitignore`) :
  `RealSavesTests` les passe toutes. Quand il en ajoute (fichiers bruts d'un émulateur, souvent sans extension), les y
  ranger sous un nom clair en `.sav` (`rubis-omega.sav`), les inspecter, lancer les tests et s'en servir pour les essais
  dans le navigateur ; mettre à jour la liste du README.
  **`git status` ne montre pas les `.sav`** (ignorés) : chercher les fichiers déposés avec `ls`, pas avec Git.

## CI/CD

`.github/workflows/ci-cd.yml` (README, « CI/CD ») : tests sur Ubuntu à chaque push, déploiement par le bouton « Run
workflow ». Le CLI `gh` n'est pas installé : les résultats de GitHub ne sont pas lisibles d'ici, l'utilisateur copie le
message d'un échec. Les tests y tournent sous **Linux** : noms de fichiers sensibles à la casse, séparateur `/`, culture
`fr-FR` par ICU. Un nouveau fichier nécessaire au déploiement s'ajoute à l'archive des deux côtés (`publier.ps1` et le
workflow).

## Compiler et tester pendant que l'appli tourne

L'appli lancée par l'utilisateur verrouille `MonsutaPotaru/bin` : compiler et tester dans un dossier à part.

```bash
dotnet build MonsutaPotaru/MonsutaPotaru.csproj --artifacts-path <dossier temporaire>/build
dotnet test MonsutaPotaru.Tests/MonsutaPotaru.Tests.csproj --artifacts-path <dossier temporaire>/build
```

Migration EF : la générer dans une **copie** du projet (`MonsutaPotaru` copié dans un dossier temporaire, `dotnet ef migrations add
<Nom>` dessus), puis recopier les fichiers de `Data/Migrations` (migration, designer, snapshot). La base se met à jour
toute seule au démarrage (`Database.Migrate()`). Le code doit compiler avant de générer la migration.
La copie génère l'espace de noms `MonsutaPotaru.Data.Migrations` : le remettre à `MonsutaPotaru.Migrations` comme les autres. Une
migration qui modifie des données (SQL seul, sans changement de modèle : `GameNames`, `JournalFileNames`, `GameGroupNames`) se teste dans
`MigrationsTests` (base migrée jusqu'à la précédente, lignes ajoutées, puis migration complète et retour) : les autres
tests créent la base avec `EnsureCreated`, sans passer par les migrations.

## Tester dans le navigateur

- Instance de test sur un autre port, avec sa propre base, ses copies et ses clés (dans un dossier temporaire), en lançant
  la DLL compilée plus haut :
  `dotnet <build>/bin/MonsutaPotaru/debug/MonsutaPotaru.dll --contentRoot <dépôt>/MonsutaPotaru --environment Development --urls http://localhost:5299
  --ConnectionStrings:DefaultConnection="DataSource=<tmp>/test.db;Cache=Shared" --Bank:BackupPath=<tmp>/backups --Bank:KeysPath=<tmp>/keys`.
  Le `.claude/launch.json` correspondant est temporaire (dossier ignoré par Git) : le supprimer après les tests.
- Compte de test : en créer un sur l'instance de test (code d'invitation de développement : `pokeamis`), noter ses
  identifiants dans un fichier du dossier temporaire, jamais dans la conversation.
- Envoi d'un `.sav` : servir le dossier des fichiers avec un petit serveur HTTP local qui autorise CORS, puis, dans la page,
  `fetch` du fichier → `DataTransfer` → `input.files`, et déclencher l'événement `change`.
- Règle anti-copie : une sauvegarde modifiée sur le serveur n'accepte une mise à jour qu'après téléchargement, et avec plus
  de temps de jeu. En test : passer `DownloadedSinceModification` à 1 dans la base de test, et ajouter du temps de jeu au
  fichier (`MONSUTA_POTARU_ADD_PLAYTIME`, voir le README).
- Sauvegardes d'exemple (ignorées par Git) : `MONSUTA_POTARU_SAMPLE_OUT` (Noir 2), `MONSUTA_POTARU_SAMPLE_PT_OUT` (Platine). Après une
  régénération de `exemple-platine.sav`, lui ajouter des heures de jeu pour qu'elle dépasse la version déjà stockée chez
  l'utilisateur.

## Pièges de PKHeX

- PKHeX **écrit dans le tableau d'octets qu'on lui donne** : lui passer `data.ToArray()` quand l'original doit rester intact.
- Une sauvegarde de 3e ou 4e génération créée vide (`new SAV3E()`, `new SAV4Pt()`) ne peut pas être écrite (`Write()`
  plante) : tester ces jeux en mémoire, ou partir d'un fichier brut valide (voir `CreatePlatinumSave`).
- `SaveUtil.TryGetSaveFile` dans les tests (les méthodes internes de PKHeX ne sont pas visibles).
- Sac : lire `sav.Inventory`, modifier les poches, puis **réaffecter** `sav.Inventory`. Le PC d'objets (`InventoryType.PCItems`,
  1re à 3e génération) n'est lu et modifié que comme `BagPlace.Pc`. Après un retrait, `ClearCount0()` seulement sur les
  poches vidées.
- Or/Argent/Cristal : PKHeX **n'écrit pas une quantité de CT tombée à zéro** (poche CT/CS, un octet par numéro) ;
  `SaveBag.ClearEmptiedMachines` remet ces octets à zéro (position de la poche lue par réflexion, champ interne `Offset`).
- Une sauvegarde de 1re ou 2e génération créée vide (`new SAV1(...)`, `new SAV2(...)`) n'est **pas reconnue** par
  `SaveUtil` une fois écrite : la tester en mémoire (relire `sav.Inventory`), ou partir d'une vraie sauvegarde
  (`Sauvegardes/rouge.sav`, voir `InventoryServiceTests.RedFile`).
- Numéros d'objets : 3e génération avec `ItemConverter.GetItemOld3` / `GetItemFuture3`, 2e avec `GetItemFuture2` (128 : sans
  équivalent) / `GetItemOld2`, Rouge/Bleu avec la table `SaveBag.Gen1Items` (PKHeX n'a rien). La 2e génération sort en
  numéros de 4e : les Noigrumes Bleue/Jaune (486/487) y sont inversées par rapport aux jeux suivants (`SwapApricorns`).
- Les poches changent d'un jeu à l'autre (Balls avec les objets en Noir 2, baies de Let's Go avec les Balls...) : chercher
  un objet dans toutes les poches avec `CanContain`, jamais par type de poche.
- CT : un même numéro n'apprend pas la même capacité selon le jeu, d'où le stockage par capacité (`SaveBag.MachineKey`).
  Correspondances dans `SaveBag` ; Épée/Bouclier et Écarlate/Violet ont un ordre particulier (commentés), vérifiés par
  des valeurs connues dans les tests (Séisme : CT26, DT10, CT149). Les CS ne sont jamais touchées.

## Images des Pokémon et des objets

**Hors du dépôt Git et de la publication** : stockées sur le VPS (`~/monsuta-potaru-images`, monté sur `wwwroot/images`,
rempli à chaque déploiement de la CI/CD et par `deploy\images-serveur.cmd`), copiées en local par `outils\recuperer-images.cmd` (SSH), servies depuis le disque
par `UseStaticFiles` (`Program.cs` ; `MapStaticAssets` ne connaît que les fichiers de la compilation, d'où aussi
`Content Remove` dans le `.csproj`). Ne jamais les remettre dans Git. Un nouveau type d'image : l'ajouter à
`outils/images.py` : le prochain déploiement (« Run workflow ») les télécharge sur le VPS, après les tests ; les tests
d'images existants exigent qu'elles soient là (`WebRootWithImages`).

Servies par le site, plus rien n'est chargé depuis GitHub : `wwwroot/images/pokemon/<numéro d'espèce>.png`,
`<numéro>-<forme>.png` (nom de forme de PokéAPI), `femelle/`, et les mêmes sous `pokemon/chromatique/` ;
`wwwroot/images/objets/<numéro d'objet>.png` (numéros de PKHeX, ceux de `SaveBag`), `ct-<type>.png` ; dans chaque dossier
un `inconnu.png` affiché en remplacement (`onerror` des balises, composant `ItemSprite` pour les objets).
`outils/images.py` les rassemble (Python seul, garde les fichiers présents) : le relancer après une mise à jour de
PKHeX.Core (nouvelles espèces), en changeant la version en tête du script. Il n'utilise pas l'API GitHub (limitée sans
jeton) : seulement des fichiers bruts, en essayant les sources dans l'ordre (une image absente répond 404, on passe à la
suivante), sans lister les dossiers des dépôts.

- Sprites animés Donjon Mystère (SpriteCollab, CC BY-NC 4.0) : `wwwroot/images/donjon/`, dans les dossiers de SpriteCollab
  (`<espèce sur 4 chiffres>/<forme>/<chromatique 0001>/<femelle 0002>`), `python outils/images.py donjon` (3 min 30,
  ~13 000 fichiers, 85 Mo). Seules Idle, Walk et Sleep sont gardées (`DONJON_ANIMATIONS`), avec les animations qu'elles
  copient (`<CopyOf>` : pas de planche à elles) ; `AnimData.xml` est réduit à ces animations. `credits.txt` : auteurs.
- `PokemonSprites` choisit l'image parmi les fichiers présents (liste lue au démarrage, `PokemonSprites.Load`) : nom de forme
  de PKHeX (selon le jeu du Pokémon : `EntityContext`) mis en forme, puis les noms de PokéAPI (`Aliases`, `Suffixes`) ; sinon
  l'image de l'espèce. Méga-Évolution : `ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb` ; PKHeX ne marque comme méga qu'une
  forme jumelle (« M-Mega », pas « F-Mega »).
- Les colonnes des filtres de « Ma collection » (`Type1`, `Nature`, `IvHp`... : `PokemonTraits.Fill`) sont remplies à
  l'enregistrement (`BankService.CreateEntry`) et au démarrage pour les Pokémon plus anciens (`FillFilterDetailsAsync`,
  repère : `Nature` null). Un nouveau filtre qui demande une nouvelle colonne suit le même chemin.
- `BankPokemon.Fingerprint` (doublons, `PokemonFingerprint`) : même chemin, remplie au démarrage (repère : null ; vide si
  illisible). Une sauvegarde dont un Pokémon est déjà dans le compte est refusée. Or les Pokémon de test
  (`SaveSessionTests.CreatePk5(espèce, niveau)`) ont **tous le même PID** : deux sauvegardes stockées par un même compte,
  ou un dépôt à côté de Pokémon déjà au stockage interne, sont alors refusés comme doublons. Utiliser
  `CreatePk5(espèce, niveau, dresseur, n°)` (PID propre au dresseur, stable : les tests de la 3DS renvoient des fichiers
  identiques) ou changer le PID (`CollectionCapacityTests.AddBankPokemon`).
- `BankPokemon.Gender` et `HeldItem` (pour l'image) sont remplis au démarrage pour les Pokémon plus anciens
  (`BankService.FillSpriteDetailsAsync`).
- Tests : `PokemonSpritesTests` (formes, femelles, Méga, et la liste des formes de PKHeX sans image), `RealSavesTests`
  (chaque espèce, chaque objet des vraies sauvegardes).

## Parc

- Sprites d'un Pokémon : `PokemonSprites.DonjonFolder` (forme, Méga, chromatique, femelle). Les formes de SpriteCollab
  (`images/donjon/formes.json`, écrit par `outils/images.py donjon` depuis son `tracker.json`) sont rapprochées des noms
  de PKHeX : nom compacté (« Mega X » ↔ « Mega_X »), noms de PokéAPI, mots retirés à la fin (« Paldea Combat » ↔
  « Paldea »), puis seul nom qui commence ainsi (« Dusk » ↔ « Dusk_Mane »). Sur toutes les formes de PKHeX, ~240 trouvent
  leur sprite ; les autres n'en ont pas encore chez SpriteCollab (casquettes de Pikachu, coupes de Couafarel...).
- Animation et comportement : `wwwroot/js/parc.js` (module importé par `Park.razor`). Réglages en tête du fichier
  (`ENERGY`, `SPRITE_SCALE`, marges) et dans `decide` / `update`.
- Le panneau du navigateur de test est souvent **masqué** : la page ne s'anime alors que pendant les captures d'écran. Pour
  juger le comportement, importer le module dans la page, créer un moteur dans un `div` et appeler `simulate(secondes, heure)`
  (rend l'état de chaque Pokémon) : temps passé à dormir ou marcher, distance entre membres d'une famille, effet de la
  souris (`PointerEvent` sur le canvas). Pour voir la nuit, remplacer `window.Date` le temps de la capture.
- Images des sprites : SpriteCollab centre chaque image sur le même point, les pieds 4 à 10 px sous le centre (le dessin
  remonte l'image de 6 px × l'échelle pour poser les pieds sur l'ombre).
- Fluidité : les sprites se dessinent en pixels d'écran, position arrondie au pixel d'écran et échelle entière (`k`) ;
  la vitesse rejoint progressivement celle voulue (`vx`, `vy`), freine à l'arrivée, et l'animation de marche suit la
  vitesse (`WALK_ANIMATION_SPEED`). Une image coûte ~0,1 ms pour 30 Pokémon : une saccade vient du dessin, pas du calcul.

## Living dex

- Jeux et modes : `LivingDexGames` (`Services/LivingDexService.cs`). Ajouter un jeu : son entrée (`All` : jaquette,
  dossier de sprites, Pokédex régional, dernier numéro national), `For(SaveFile)` (quelle sauvegarde, sachant que PKHeX ne
  distingue pas Rouge de Bleu, Or d'Argent, Rubis de Saphir, Rouge Feu de Vert Feuille, Diamant de Perle ni HeartGold de
  SoulSilver), les formes du mode Complet (`Forms` : régionales, Méga, Gigamax, d'aspect, lettres de Zarbi ; pas le sexe ;
  **seulement celles qu'une sauvegarde du jeu peut contenir** : les formes de Morphéo n'existent qu'en combat, Deoxys de 3e
  génération a la forme de son jeu, `DeoxysForm`, case `AnyForm` ; à partir de la 4e, le nombre de formes de chaque jeu
  vient de `PersonalTable` de PKHeX (`FormCount`, liste `AspectForms`), et les règles de légalité de PKHeX disent ce
  qu'une boîte peut garder : `FormVerifier` sur GitHub), le premier numéro du Pokédex régional (`RegionalStart` : 0 pour
  Unys), et les images dans `outils/images.py` (jusqu'à la 5e génération seulement : à partir de la 6e, les images de la
  collection, `SpriteFolder` null ; l'utilisateur a trouvé les sprites 3D hideux) (`LIVINGDEX_SPRITES`, `LIVINGDEX_FORMS`,
  `LIVINGDEX_COVERS`, mêmes noms de dossiers et de fichiers que `LivingDexGames.Sprite` : « 201-b.png », « 386-speed.png »).
  Chaque forme a sa propre image (test `Complete_EachFormHasItsOwnNameAndSprite`).
- Forme qui ne tient pas en boîte (Shaymin Céleste en 4e) : l'utilisateur ne veut pas bloquer l'équipe. Case
  `KeyItem` : remplie par un **deuxième** exemplaire de l'espèce et l'objet rare dans le sac (lu dans la sauvegarde
  stockée, `BagItemsAsync`), avec ce qui manque en infobulle (`LivingDexEntry.Note`). Conseil d'une case manquante :
  `LivingDexSlot.Hint` (saisons de Vivaldaim et Haydaim en 5e : seuls ceux de l'équipe changent de saison ; l'utilisateur
  m'a repris quand j'ai dit « même en boîte », vérifier ce genre de règle avant de l'affirmer).
- Méga-Évolutions (6e) : cases `MegaItem` / `MegaMove`, remplies par un exemplaire **de plus** qui tient la gemme
  (`BankPokemon.HeldItem`) ; `EntriesAsync` leur attribue leurs exemplaires d'abord, les autres cases ne les reprennent
  pas. Gemme d'une forme : `ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb` (formes limitées à celles du jeu,
  `FormCount` : PKHeX connaît aussi les Méga de Légendes Z-A). Méga-Rayquaza n'a pas de gemme : Draco-Ascension, lue
  dans les octets des seuls Rayquaza. Sous-Pokédex (Kalos) : `LivingDexGameInfo.Sections`, `RegionalNumber`.
- L'utilisateur trouve certaines formes trop longues à rassembler : une seule case pour Prismillon (motifs), comme
  Arceus et Genesect. Lui poser la question pour une espèce à beaucoup de formes avant de toutes les compter.
- Les tests ne doivent **pas** exiger ces images : la CI les copie depuis le VPS, qui ne les reçoit qu'au déploiement,
  après les tests. Une nouvelle image : tester son chemin, pas sa présence.
- Jaquettes : Bulbagarden Archives (API MediaWiki, version réduite `iiurlwidth=480`). Sur ce PC, Python refuse son
  certificat (« certificate has expired ») à cause du magasin de certificats de Windows, pas du site : sur le VPS (Debian)
  ça passe. Pour les avoir en local, `outils\recuperer-images.cmd`, ou un contexte SSL avec `cafile` (paquet de Git :
  `/usr/ssl/certs/ca-bundle.crt`).

## Ajouter une catégorie d'objets à l'inventaire

Même modèle que les Pokéballs, médicaments, baies et CT : `ItemCategory` et la liste d'objets dans `SaveBag`
(`CategoryOf`, `KeyOf`, `Capacity`), `ItemCategories.All` (carte et onglet), `BagSummary` du dialogue de transfert,
des tests dans `InventoryServiceTests`, des objets dans la sauvegarde d'exemple Platine, et le README. « Objets » est la
catégorie fourre-tout (tout ce qui n'est dans aucune autre, limité aux poches `SaveBag.ItemPouches`) : une nouvelle
catégorie (les bonbons, envisagée) se teste avant elle dans `CategoryOf`. Objets rares, Lettres, Cristaux Z et CS ne
sont jamais touchés. Pas de migration : la table `OwnedItems` stocke une clé par objet.
