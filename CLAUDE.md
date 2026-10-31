# Notes pour Claude

Monsuta Potaru (anciennement PokeBank) : banque Pokémon privée (ASP.NET Core 10, Blazor Server, EF Core + SQLite,
Identity, PKHeX.Core, xUnit). « Monsuta Potaru » affiché, `MonsutaPotaru` dans le code, `monsuta-potaru` sur le serveur
(dossiers, service, volumes Docker). Restent « PokeBank » exprès : le homebrew 3DS **PokeBank Sync** (autre dépôt, à
renommer plus tard) et `SetApplicationName("PokeBank")` (le changer déconnecte tout le monde).

Le `README.md` (100 Ko) décrit le fonctionnement, les règles métier et l'organisation du code : n'en lire que la section
utile à la tâche. Ce fichier ne garde que ce que le README ne dit pas.

## Fiches par sujet : lire celle du sujet avant d'y toucher

| Sujet | Fiche |
|---|---|
| Comptes, connexion, rôles, thèmes, pages du compte | `docs/claude/comptes.md` |
| Multijoueurs : pages des joueurs, visibilité, amis, échanges, valeurs trafiquées | `docs/claude/multijoueurs.md` |
| Combats (Showdown) | `docs/claude/combats.md` |
| Images des Pokémon et des objets, `outils/images.py`, `PokemonSprites` | `docs/claude/images.md` |
| Parc | `docs/claude/parc.md` |
| Living dex (par jeu, global, chromatique) | `docs/claude/living-dex.md` |
| Inventaire : sacs, numéros d'objets, CT, nouvelle catégorie | `docs/claude/inventaire.md` |
| Pièges de PKHeX (octets, sauvegardes vides) | `docs/claude/pkhex.md` |
| Ma collection : colonnes remplies au démarrage, doublons, PID des Pokémon de test | `docs/claude/collection.md` |
| Tester dans le navigateur : instance de test, compte, envoi d'un `.sav`, deux comptes, anti-copie | `docs/claude/navigateur.md` |
| CI/CD : workflows, SonarCloud, déploiement | `docs/claude/ci-cd.md` |

Une fiche fausse ou incomplète : la corriger dans la même tâche. Un nouveau sujet : nouvelle fiche, et une ligne ici.

## Façon de travailler

- **Ne jamais commiter ni pousser** : l'utilisateur s'en charge. Lui signaler seulement les fichiers nouveaux à ajouter.
- **Nouvelle fonctionnalité** : poser d'abord les questions (pièges, choix, avec une recommandation), puis **attendre son
  « go »** explicite avant d'écrire du code. Petites corrections demandées : directement.
- **Dès les questions, vérifier l'environnement** et signaler ce qui manque : SDK .NET 10 (`dotnet --list-sdks`), images
  (`MonsutaPotaru/wwwroot/images` ; `outils\recuperer-images.cmd`, par SSH : c'est l'utilisateur qui le lance), Node.js 22+
  et `showdown/node_modules` (combats).
- Tout en français : code commenté en français, interface qui tutoie (« Retire de l'argent… »), messages prêts à afficher.
- Son écran : 3440×1440 à 150 % de mise à l'échelle Windows. Il raisonne en **pixels physiques**
  (`innerWidth * devicePixelRatio`), pas en pixels CSS.
- Vérifier dans un vrai navigateur (instance de test : `docs/claude/navigateur.md`) avant d'annoncer qu'une
  fonctionnalité marche.
- Tenir le README à jour (règles métier, sauvegardes d'exemple, tableau « Organisation du code ») à chaque fonctionnalité.
- **Toute ressource venant d'un autre projet** (bibliothèque, paquet, images, données, table reprise d'un site) : citée
  dans la section « Crédits » du README, avec son usage et sa licence, dès qu'elle entre dans le projet.

## Conventions du code

- **CSP stricte** (`SecurityHeaders.cs`) : aucun script écrit dans la page. Pas de `<script>` sans `src`, pas d'attribut
  `onerror` / `onclick` / `onload` (les `@onclick` de Blazor marchent) : le JavaScript va dans `wwwroot/js/app.js` ou un
  module (`parc.js`) ; une image absente prend `data-remplacement` ou `data-cacher-si-absente`. Vérifié par
  `SecurityHeadersTests.Pages_HaveNoInlineScript`. Une ressource d'un autre site serait bloquée : la servir depuis le site
  (ou l'ajouter à la CSP). Un refus apparaît dans la console du navigateur (« Content Security Policy »).
- Services *scoped* à constructeur primaire, accès base par `IDbContextFactory<ApplicationDbContext>`.
- Résultats en `record` portant un message français prêt à afficher (`ItemTransferResult(bool Ok, string Message)`).
- Écritures en transaction ; jeton de concurrence `Version` sur `StoredSave` ; débits et crédits atomiques avec
  `ExecuteUpdateAsync` conditionnel. Copie de sécurité avant/après dans `Data/backups/<id utilisateur>/`.
- Toute modification d'une sauvegarde stockée passe par `InventoryService.SaveServerChangeAsync` (trace
  `SaveModification` : règle anti-copie, voir le README).
- Une valeur venue du navigateur (`<select>`, champ, `[JSInvokable]`) peut être trafiquée : le service revérifie
  (compte, amitié, visibilité). Détails et méthode de test : `docs/claude/multijoueurs.md`.
- Tests : xUnit, base SQLite temporaire via `TestDatabase` ; sauvegardes fabriquées en mémoire
  (`SaveSessionTests.CreateBw2Save`, `CreatePlatinumSave`, `GiveItems`).
- Vraies sauvegardes de l'utilisateur dans `MonsutaPotaru.Tests/Sauvegardes/` (versionnées, exception à `*.sav` dans
  `.gitignore`) : `RealSavesTests` les passe toutes. Quand il en ajoute (fichiers bruts d'un émulateur, souvent sans
  extension) : les ranger sous un nom clair en `.sav` (`rubis-omega.sav`), les inspecter, lancer les tests, s'en servir
  pour les essais dans le navigateur, mettre à jour la liste du README (ligne « Jeux testés » comprise).
  **`git status` ne montre pas les `.sav`** (ignorés) : chercher les fichiers déposés avec `ls`.

## Compiler et tester

L'appli lancée par l'utilisateur verrouille `MonsutaPotaru/bin` : compiler et tester dans un dossier à part.

```bash
dotnet build MonsutaPotaru/MonsutaPotaru.csproj --artifacts-path <dossier temporaire>/build
dotnet test MonsutaPotaru.Tests/MonsutaPotaru.Tests.csproj --artifacts-path <dossier temporaire>/build
```

- Pendant le développement, seulement les tests concernés (`--filter`), la suite complète à la fin ; filtrer la sortie
  (bilan et échecs).
- Sans images ou sans Node, les tests qui en dépendent échouent avec un message clair : les compter à part.
- La CI les passe sous **Linux** : noms de fichiers sensibles à la casse, séparateur `/`, culture `fr-FR` par ICU.
- Un nouveau fichier nécessaire au déploiement s'ajoute à l'archive des deux côtés (`publier.ps1` et le workflow).
- `RequiresAspNetWebAssets` dans le `.csproj` : en .NET 10, les scripts de Blazor (`_framework/blazor.web.js`) viennent
  d'un paquet que le SDK n'ajoute que s'il voit des `.razor` à la restauration ; le `Dockerfile` restaure avec le seul
  `.csproj` (sans ce réglage : pages affichées mais jamais interactives). Pour un changement de version de .NET ou de la
  publication : refaire les étapes du `Dockerfile` en local (restauration du `.csproj` seul dans un dossier vide, copie
  des sources, `dotnet publish -c Release --no-restore`) et lancer le résultat en `--environment Production` (CSP sans
  les exceptions de développement).
- **Migration EF** : la générer dans une **copie** du projet (`MonsutaPotaru` copié dans un dossier temporaire,
  `dotnet ef migrations add <Nom>` dessus), puis recopier `Data/Migrations` (migration, designer, snapshot) et remettre
  l'espace de noms `MonsutaPotaru.Migrations` (la copie génère `MonsutaPotaru.Data.Migrations`). Le code doit compiler
  avant. La base se met à jour au démarrage (`Database.Migrate()`). Une migration qui modifie des données (SQL seul :
  `GameNames`, `JournalFileNames`, `GameGroupNames`) se teste dans `MigrationsTests` (base migrée jusqu'à la précédente,
  lignes ajoutées, migration complète et retour) ; les autres tests créent la base avec `EnsureCreated`.
