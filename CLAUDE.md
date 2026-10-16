# Notes pour Claude

PokeBank : banque Pokémon privée (ASP.NET Core 9, Blazor Server, EF Core + SQLite, Identity, PKHeX.Core, xUnit).
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
- Vraies sauvegardes de l'utilisateur dans `PokeBank.Tests/Sauvegardes/` (versionnées, exception à `*.sav` dans `.gitignore`) :
  `RealSavesTests` les passe toutes. Quand il en ajoute (fichiers bruts d'un émulateur, souvent sans extension), les y
  ranger sous un nom clair en `.sav` (`rubis-omega.sav`), les inspecter, lancer les tests et s'en servir pour les essais
  dans le navigateur ; mettre à jour la liste du README.
  **`git status` ne montre pas les `.sav`** (ignorés) : chercher les fichiers déposés avec `ls`, pas avec Git.

## Compiler et tester pendant que l'appli tourne

L'appli lancée par l'utilisateur verrouille `PokeBank/bin` : compiler et tester dans un dossier à part.

```bash
dotnet build PokeBank/PokeBank.csproj --artifacts-path <dossier temporaire>/build
dotnet test PokeBank.Tests/PokeBank.Tests.csproj --artifacts-path <dossier temporaire>/build
```

Migration EF : la générer dans une **copie** du projet (`PokeBank` copié dans un dossier temporaire, `dotnet ef migrations add
<Nom>` dessus), puis recopier les fichiers de `Data/Migrations` (migration, designer, snapshot). La base se met à jour
toute seule au démarrage (`Database.Migrate()`). Le code doit compiler avant de générer la migration.
La copie génère l'espace de noms `PokeBank.Data.Migrations` : le remettre à `PokeBank.Migrations` comme les autres. Une
migration qui modifie des données (SQL seul, sans changement de modèle : `GameNames`, `JournalFileNames`) se teste dans
`MigrationsTests` (base migrée jusqu'à la précédente, lignes ajoutées, puis migration complète et retour) : les autres
tests créent la base avec `EnsureCreated`, sans passer par les migrations.

## Tester dans le navigateur

- Instance de test sur un autre port, avec sa propre base, ses copies et ses clés (dans un dossier temporaire), en lançant
  la DLL compilée plus haut :
  `dotnet <build>/bin/PokeBank/debug/PokeBank.dll --contentRoot <dépôt>/PokeBank --environment Development --urls http://localhost:5299
  --ConnectionStrings:DefaultConnection="DataSource=<tmp>/test.db;Cache=Shared" --Bank:BackupPath=<tmp>/backups --Bank:KeysPath=<tmp>/keys`.
  Le `.claude/launch.json` correspondant est temporaire (dossier ignoré par Git) : le supprimer après les tests.
- Compte de test : en créer un sur l'instance de test (code d'invitation de développement : `pokeamis`), noter ses
  identifiants dans un fichier du dossier temporaire, jamais dans la conversation.
- Envoi d'un `.sav` : servir le dossier des fichiers avec un petit serveur HTTP local qui autorise CORS, puis, dans la page,
  `fetch` du fichier → `DataTransfer` → `input.files`, et déclencher l'événement `change`.
- Règle anti-copie : une sauvegarde modifiée sur le serveur n'accepte une mise à jour qu'après téléchargement, et avec plus
  de temps de jeu. En test : passer `DownloadedSinceModification` à 1 dans la base de test, et ajouter du temps de jeu au
  fichier (`POKEBANK_ADD_PLAYTIME`, voir le README).
- Sauvegardes d'exemple (ignorées par Git) : `POKEBANK_SAMPLE_OUT` (Noir 2), `POKEBANK_SAMPLE_PT_OUT` (Platine). Après une
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

Servies par le site, plus rien n'est chargé depuis GitHub : `wwwroot/images/pokemon/<numéro d'espèce>.png`,
`<numéro>-<forme>.png` (nom de forme de PokéAPI), `femelle/`, et les mêmes sous `pokemon/chromatique/` ;
`wwwroot/images/objets/<numéro d'objet>.png` (numéros de PKHeX, ceux de `SaveBag`), `ct-<type>.png` ; dans chaque dossier
un `inconnu.png` affiché en remplacement (`onerror` des balises, composant `ItemSprite` pour les objets).
`outils/images.py` les rassemble (Python seul, garde les fichiers présents) : le relancer après une mise à jour de
PKHeX.Core (nouvelles espèces), en changeant la version en tête du script. L'API GitHub n'accepte que 60 appels par heure
sans jeton (variable `GITHUB_TOKEN`) ; le script les limite.

- `PokemonSprites` choisit l'image parmi les fichiers présents (liste lue au démarrage, `PokemonSprites.Load`) : nom de forme
  de PKHeX (selon le jeu du Pokémon : `EntityContext`) mis en forme, puis les noms de PokéAPI (`Aliases`, `Suffixes`) ; sinon
  l'image de l'espèce. Méga-Évolution : `ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb` ; PKHeX ne marque comme méga qu'une
  forme jumelle (« M-Mega », pas « F-Mega »).
- `BankPokemon.Gender` et `HeldItem` (pour l'image) sont remplis au démarrage pour les Pokémon plus anciens
  (`BankService.FillSpriteDetailsAsync`).
- Tests : `PokemonSpritesTests` (formes, femelles, Méga, et la liste des formes de PKHeX sans image), `RealSavesTests`
  (chaque espèce, chaque objet des vraies sauvegardes).

## Ajouter une catégorie d'objets à l'inventaire

Même modèle que les Pokéballs, médicaments, baies et CT : `ItemCategory` et la liste d'objets dans `SaveBag`
(`CategoryOf`, `KeyOf`, `Capacity`), `ItemCategories.All` (carte et onglet), `BagSummary` du dialogue de transfert,
des tests dans `InventoryServiceTests`, des objets dans la sauvegarde d'exemple Platine, et le README. « Objets » est la
catégorie fourre-tout (tout ce qui n'est dans aucune autre, limité aux poches `SaveBag.ItemPouches`) : une nouvelle
catégorie (les bonbons, envisagée) se teste avant elle dans `CategoryOf`. Objets rares, Lettres, Cristaux Z et CS ne
sont jamais touchés. Pas de migration : la table `OwnedItems` stocke une clé par objet.
