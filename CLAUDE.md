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

## Comptes

- Le pseudo est le `UserName` d'Identity ; on se connecte avec l'adresse (`AccountService.FindForLoginAsync`). Un ancien
  compte a encore son adresse pour `UserName` : « @ » est interdit dans un pseudo, c'est le repère (`HasPseudo`). Ne
  jamais afficher l'adresse d'un compte à la place du pseudo.
- Connexion protégée (README « Comptes ») : `FindForLoginAsync` compte les échecs (`CheckPasswordAsync` du service :
  5 bloquent le compte 5 minutes) et `AccountLimiter` (singleton) limite les envois par adresse IP (connexion,
  inscription). Un échec de connexion, quelle qu'en soit la raison, a le même message (`LoginFailedMessage`) : ne
  jamais dire qu'une adresse a un compte ou qu'il est bloqué. Toute nouvelle vérification du mot de passe passe par
  `AccountService.CheckPasswordAsync` (jamais `UserManager.CheckPasswordAsync` : il ne compte pas les échecs).
  En test dans le navigateur : un compte bloqué se débloque en passant `LockoutEnd` dans le passé dans la base de test ;
  `localhost` et `127.0.0.1` ont chacun leur compteur d'IP. Envoyer le formulaire par `fetch` (`FormData` du
  formulaire) : après une connexion réussie, recharger la page (jeton antiforgery lié à l'ancien compte : 400).
- Identity **modifie l'objet du compte avant de valider** (`SetUserNameAsync`, `SetEmailAsync`) : un refus laisse la
  valeur refusée dessus. D'où les vérifications d'unicité faites avant, dans `AccountService`.
- Rôles exclusifs Standard / Administrateur (`SiteRoles`, README « Administration ») ; `AdminService` revérifie le rôle
  en base à chaque action (le cookie peut le garder 30 minutes). Le code d'invitation est en base (`SiteSettings`) :
  `Bank:InviteCode` ne sert qu'au premier démarrage. EF ne traduit pas `Guid.NewGuid()` dans un `ExecuteUpdate` :
  calculer la valeur avant.
- Thèmes (README « Thèmes ») : tout le style d'un thème sous `[data-theme="<clé>"]` (posé par `App.razor`), le Classique
  n'a rien. `app.css` fixe des couleurs en dur (`.btn-primary`, `.btn-link`, focus) : les écraser explicitement ; le CSS
  isolé des composants (menu) se charge après : sélecteurs en `html[data-theme=...]`. Le thème Pokédex couvre tout le
  site (contenu sous `main`) : un nouvel élément coloré en dur dans `app.css` (bleu, orange) doit y recevoir sa version.
  L'utilisateur veut du **sobre** : il a refusé un thème Hoenn (sac de ROSA) trop chargé (fond rayé, pastilles et
  bordures épaisses partout), et trouvé le rouge du Pokédex `#dc0a2d` trop vif (`#c2323d` retenu).
- Tests de migration : ajouter les comptes en SQL (`MigrationsTests.AddUserAsync`), pas par `db.Users` : le modèle actuel
  a des colonnes (`Theme`) qu'une base migrée à une étape plus ancienne n'a pas.
- Tests : `AccountServiceTests` monte Identity comme `Program.cs` (`AccountService.ConfigureIdentity`) sur la base de test.
  Les comptes de `TestDatabase` sont créés sans Identity (pas d'adresse) : n'y appeler aucune méthode qui les revalide.

## Multijoueurs

- Collection, Parc, Inventaire et Living dex (liste et page d'un living dex) ont une seconde route `/joueurs/{Pseudo}/...` :
  `Visitor` (paramètre `Pseudo`), compte du joueur trouvé par `PlayerService.FindAsync(pseudo, page)` à la place du sien.
  En visiteur : tout le contenu dans `else` d'un `@if (Visitor && player is null)`, boutons d'action masqués **et**
  méthodes d'action qui sortent tout de suite (`if (Visitor) return;`), ni journal, ni noms de fichiers
  (`LabelAndFile` → `Label`, `PublicSaveLabel` des living dex), ni ID de dresseur (`PokemonCard.HideTrainerId`), ni
  téléchargement. Une nouvelle action ou une nouvelle donnée sur ces pages : décider ce qu'en voit un visiteur.
- Le premier rendu d'un visiteur a lieu avant que le joueur soit trouvé : ce qui touche aux éléments de la page dans
  `OnAfterRenderAsync` (parc, grille de l'inventaire) attend que le contenu soit affiché (`engineStarted`, `contentStarted`).
- Visibilité à trois valeurs (`ProfileAudience` : tout le monde, amis seulement, personne). `PlayerService.FindAsync` prend
  le compte qui regarde : un ami (ou le joueur lui-même) voit les pages d'un profil « Amis seulement » ; « Personne »
  reste introuvable, même des amis. Profil caché, page non visible, compte sans pseudo : même réponse (`PlayerHidden`),
  administrateurs compris.
- Amis : `FriendService` ; les amitiés sont en double (une ligne par compte). Une action de la page qui change les
  demandes passe par `FriendService.Notify` pour que le menu relise son compteur.
- Échanges : `TradeService` (mécanique, testée sans navigateur) et `TradeHub` (déroulé en direct, singleton : tout
  changement passe par ses méthodes sous verrou, les pages s'abonnent à `TradeSession.Changed`). C'est la page du second à
  valider qui écrit les deux sauvegardes. Pour essayer à deux dans le navigateur intégré (cookies partagés entre onglets),
  ouvrir un joueur sur `localhost:5299` et l'autre sur `127.0.0.1:5299` : un cookie est propre au nom d'hôte. La
  confirmation de départ (`NavigationLock`) se teste en remplaçant `window.confirm` dans la page.
- Blazor Server : la valeur d'un événement (`<select>`, champ, `[JSInvokable]`) vient du navigateur et peut être
  trafiquée. Un service appelé avec l'identifiant d'un autre compte revérifie la relation lui-même (amitié :
  `FriendService.RelationAsync`, visibilité : `PlayerService.FindAsync`), sans se fier à la liste affichée. Pour le
  tester : changer la `value` d'une option dans la page, puis déclencher `change`. Pour un argument de `[JSInvokable]`
  (parc : `OnPokemonClicked`), remplacer `WebSocket.prototype.send` en gardant l'original dans une variable, réécrire
  l'argument dans le message (« [137] » → « [136] », même longueur), puis **remettre l'original** (le supprimer casse la
  page). Le Pokémon du parc se clique en cherchant ses pixels dans le canvas (`getImageData`).
- Le panneau du navigateur de test, masqué, ne remplit pas les listes virtualisées (« Ma collection ») : faire une
  capture d'écran pour qu'elles se chargent.

## Conventions du code

- **CSP stricte** (`SecurityHeaders.cs`, README « Sécurité ») : aucun script écrit dans la page, il ne s'exécuterait
  pas. Pas de bloc `<script>` sans `src`, pas d'attribut `onerror` / `onclick` / `onload` (les `@onclick` de Blazor, eux,
  marchent) : le JavaScript va dans `wwwroot/js/app.js` (ou un module comme `parc.js`), une image absente prend
  `data-remplacement` ou `data-cacher-si-absente`. `SecurityHeadersTests.Pages_HaveNoInlineScript` le vérifie. Une
  ressource d'un autre site (police, script, image) serait bloquée : l'ajouter à la CSP, ou plutôt la servir depuis le
  site. Dans le navigateur de test, un refus apparaît dans la console (« Content Security Policy »).
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

`.github/workflows/ci.yml` et `cd.yml` (README, « CI/CD »), séparés : tests sur Ubuntu à chaque push, avec l'analyse
SonarCloud (organisation `ghalhyus`, projet de clé `ghalhyus_monsuta-potaru`, secret `SONAR_TOKEN` ; pour information, elle ne fait jamais
échouer la CI : `continue-on-error`) ; déploiement par
le bouton « Run workflow » de « CD », qui ne relance pas les tests mais lit le résultat de la CI du commit (case
« Forcer » pour passer outre). Le CLI `gh` n'est pas installé : les résultats de GitHub ne sont pas lisibles d'ici, l'utilisateur copie le
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
- Compte de test : en créer un sur l'instance de test (code d'invitation de développement : `pokeamis` sur une base neuve ;
  administrateur : passer `--Bank:AdminPseudo=<pseudo>`), noter ses
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
`outils/images.py` : le prochain déploiement (« Run workflow » de « CD ») les télécharge sur le VPS ; les tests
d'images existants exigent qu'elles soient là (`WebRootWithImages`).

Servies par le site, plus rien n'est chargé depuis GitHub : `wwwroot/images/pokemon/<numéro d'espèce>.png`,
`<numéro>-<forme>.png` (nom de forme de PokéAPI), `femelle/`, et les mêmes sous `pokemon/chromatique/` ;
`wwwroot/images/objets/<numéro d'objet>.png` (numéros de PKHeX, ceux de `SaveBag`), `ct-<type>.png` ; dans chaque dossier
un `inconnu.png` affiché en remplacement (attribut `data-remplacement` des balises, géré par `wwwroot/js/app.js` :
jamais de `onerror`, bloqué par la CSP ; composant `ItemSprite` pour les objets).
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
  Arceus et Genesect. Lui poser la question pour une espèce à beaucoup de formes avant de toutes les compter (il a
  pourtant voulu les 7 noyaux de Météno, et retiré les casquettes de Pikachu et les Dominants). Vérifier aussi que le
  mode Complet tient dans la sauvegarde : 964 cases pour 966 places en Ultra-Soleil / Ultra-Lune.
- 7e génération : Shaymin Céleste et Hoopa Déchaîné tiennent en boîte (`FormVerifier` : `Format <= 6`), cases
  ordinaires. Let's Go : ni objet tenu, Méga par la gemme du sac (`MegaInBag`), son Pokédex = son national
  (`LivingDexGameInfo.National`). Une sauvegarde Let's Go vide de PKHeX n'est pas reconnue par `SaveUtil` : écrire le pied
  « BEEF » et le numéro de bloc 0x13 (`LivingDexServiceTests.LetsGoWith`). Bulbagarden refuse Python sur ce PC
  (403) ; `curl` avec l'agent « PokeBank (outils/images.py) » passe.
- 8e génération : ce que le jeu peut contenir vient de `PersonalTable.IsPresentInGame` (`LivingDexGames.Present`), pas
  de `FormCount` seul (Épée / Bouclier n'ont que 664 espèces ; Légendes Arceus n'a que les Caninos de Hisui :
  `FirstForm`). Formes régionales par leur nom anglais chez PKHeX (« Alola », « Galar », « Hisui ») ; attention, la
  casquette d'Alola de Pikachu s'appelle aussi « Alola ». Gigamax : case `Gigamax`, un exemplaire de plus avec le facteur
  (`IGigantamax`, lu dans les octets des seules espèces de `GigamaxSpecies`). Une sauvegarde Légendes Arceus vide n'est
  pas reconnue par `SaveUtil` (pas de test du service pour ce jeu) ; Diamant Étincelant vide, oui.
- Living dex global (`GlobalLivingDex`) : sur le stockage interne (`BankPokemon.StoredSaveId` null), sans les chromatiques
  (ignorés, voulu par l'utilisateur), image : la Master Ball (`CoverItem`) ; une forme reconnue
  par son nom anglais chez PKHeX (`FormKey`, « Normal » pour la base, que PKHeX laisse vide dans un jeu où l'espèce
  n'a qu'une forme), chaque exemplaire pour une seule case (`Assign`). Le Complet réunit les cases Complet des jeux ;
  l'Ultime y ajoute tout ce que les tables de PKHeX donnent par jeu, moins `Excluded`. L'utilisateur a voulu : toutes
  les formes, même au même aspect et de couleur (pas les chromatiques), mâle / femelle, Spinda seul ; pas les formes de
  combat sans objet tenu ; pas Légendes Z-A (pas pris en charge par le site). Un nouveau jeu : son Complet entre tout
  seul dans le global ; ajouter sa table à `GlobalLivingDex.Games` pour l'Ultime.
- Living dex global chromatique (`GlobalShiny`) : les mêmes cases moins `ShinyLocked` (liste validée par l'utilisateur,
  d'après Bulbapedia ; les Dominants retirés aussi, cadeaux fixes), seulement les chromatiques. Image : Charme Chroma.
  Une nouvelle forme ou espèce jamais distribuée en chromatique : l'ajouter à `ShinyLocked`.
- 9e génération : Écarlate / Violet seulement (`For` : `SAV9SV`) ; Légendes Z-A est aussi en 9e chez PKHeX, sans living
  dex pour l'instant. Une sauvegarde Écarlate / Violet vide n'est pas reconnue par `SaveUtil`, et l'utilisateur n'en a
  pas déposé : les images ont été vérifiées par un test temporaire (`PokemonSprites.Load(RealSavesTests.WebRootWithImages())`,
  chaque case a son image, présente), pas dans le navigateur.
- Les tests ne doivent **pas** exiger ces images : la CI les copie depuis le VPS, qui ne les reçoit qu'au déploiement
  (CD), lancé après la CI du commit. Une nouvelle image : tester son chemin, pas sa présence.
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
