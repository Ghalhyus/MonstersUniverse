# PokeBank

Banque Pokémon privée pour un cercle d'amis, basée sur [PKHeX.Core](https://github.com/kwsch/PKHeX).

- **Stack** : ASP.NET Core 9 + Blazor Server (tout en C#, pas de JavaScript à écrire), SQLite, ASP.NET Identity.
- **Jeux testés** : Pokémon Noir 2 / Blanc 2. PKHeX sait lire les autres générations ; à valider au cas par cas.

## Lancer en local

```bash
dotnet run --project PokeBank
```

Puis ouvrir http://localhost:5265. En développement, le code d'invitation est `pokeamis`
(voir `PokeBank/appsettings.Development.json`). La base SQLite est créée automatiquement dans `PokeBank/Data/app.db`.

Tests :

```bash
dotnet test
```

`exemple-blanc2.sav` est une sauvegarde de test (Pikachu + Évoli) pour essayer le site sans vraie partie.
Pour la régénérer : `POKEBANK_SAMPLE_OUT=chemin/fichier.sav dotnet test`.

`exemple-platine.sav` est une sauvegarde Platine (dresseur Lucas, 50 000 ₽) avec 50 Pokémon légaux d'espèces différentes,
tirés des vraies rencontres du jeu, et dans le sac 62 Pokéballs (Poké, Super, Hyper, Master, Luxe et Honor Ball),
26 médicaments (Potion, Super Potion, Total Soin, Rappel, Huile, Super Bonbon), 23 baies (Ceriz, Oran, Prine,
Sitrus, Enigma), 11 objets (Pierre Lune, Pierre Feu, Pépite, Restes, Attaque + dans la poche Objets de combat),
7 CT (2 CT01 Mitra-Poing, 3 CT06 Toxik, CT24 Tonnerre, CT26 Séisme), et ce que le site ne doit ni montrer ni toucher :
deux CS (Coupe, Surf), le Vélo (objet rare) et une Lettre Herbe.

Pour simuler « j'ai rejoué un peu » et passer la règle anti-copie de « Mes sauvegardes » (voir l'inventaire plus bas),
`POKEBANK_ADD_PLAYTIME=chemin/fichier.sav dotnet test` ajoute une heure de jeu au fichier, modifié sur place (n'importe quel jeu). Pour la régénérer : `POKEBANK_SAMPLE_PT_OUT=chemin/fichier.sav dotnet test`.

**Vraies sauvegardes de test** : les fichiers déposés dans `PokeBank.Tests/Sauvegardes/` (versionnés, malgré la règle `*.sav`) passent tout le parcours du site dans `RealSavesTests` : lecture et réécriture à l'identique,
dépôt d'un Pokémon puis retour au même emplacement, stockage (tous ses Pokémon dans la collection), retrait d'argent,
retrait de tous les objets du sac et du PC puis dépôt de retour (objets rares, CS et Cristaux Z intacts). Il suffit d'y ajouter un
fichier pour qu'il soit testé ; sans le dossier, ces tests ne vérifient rien. Actuellement : Rouge, Bleu, Argent (partie
randomisée : ses Pokémon sont signalés illégaux), Noir, Rubis Oméga, Ultra-Soleil, Épée. En 1re et 2e génération, le fichier réécrit n'est pas identique octet pour octet (le contenu l'est) :
le jeu ne prépare ses boîtes qu'au premier changement de boîte et PKHeX les écrit vides en règle ; et une boîte y est une
liste sans trou, tassée à l'écriture comme le fait le jeu (un Pokémon déposé dans la banque laisse sa place au suivant).

## Fonctionnement

1. L'utilisateur charge sa sauvegarde (`.sav`, `.dsv`…) dans « Transfert ».
2. Il dépose / retire des Pokémon : les changements restent **en attente** (en mémoire) et sont annulables.
3. « Valider et télécharger » : la base est mise à jour dans une transaction, une copie *avant/après* de la
   sauvegarde est écrite dans `Data/backups/<id utilisateur>/`, et la sauvegarde modifiée est téléchargée.
4. L'utilisateur **remplace** sa sauvegarde par ce fichier.

### Sauvegardes stockées (consultation)

Dans « Mes sauvegardes », l'utilisateur peut aussi **stocker** une sauvegarde complète : le fichier reste sur le serveur
et tous ses Pokémon (équipe, boîtes, pension, Boîte Combat…) apparaissent dans la collection **en lecture seule**
(ni retrait vers un jeu, ni téléchargement `.pkX`). « Mettre à jour » remplace le fichier et tous ses Pokémon ;
« Supprimer » renvoie d'abord le fichier à l'utilisateur. Une copie est gardée dans `Data/backups/` à chaque fois.

- Une même partie ne peut être stockée **qu'une fois sur tout le serveur**, tous comptes confondus. Elle est reconnue
  par génération + jeu + ID dresseur / ID secret + nom du dresseur (`SaveIdentity`) ; c'est dissuasif, pas infaillible
  (PKHeX permet de modifier ces valeurs). Les jeux de 1re et 2e génération n'ont pas d'ID secret.
- Tant qu'elle est stockée, la sauvegarde est **refusée dans « Transfert »** (au chargement et à la validation).
- Une mise à jour avec moins de temps de jeu que la version stockée demande confirmation (ancienne copie ?).

### Inventaire (Pokédollars et objets)

La page « Inventaire » affiche le porte-monnaie du compte et le « Stockage » de ses objets : un onglet par catégorie
(objets, Pokéballs, médicaments, baies, CT / DT, « Objets » ouvert par défaut ; l'onglet choisi est gardé dans l'adresse,
`?onglet=baies`), avec la quantité totale de chacune, en liste (par défaut) ou en grille (choix retenu par le navigateur). La
recherche porte sur toute la source affichée : chaque onglet affiche alors le nombre d'objets trouvés. Chaque onglet a son
**tri**, retenu par le navigateur et valable pour toutes les sources : « Par défaut » (numéro d'objet commun à tous les jeux,
qui garde ensemble les Balls, les pierres, les potions... ; les CT par nom de capacité), « Nom » (accents ignorés ; pour une
CT, le nom de la capacité, pas son numéro) ou « Quantité » (à égalité, par nom), croissant au départ et inversable. Un sélecteur choisit
la source (gardée dans l'adresse, `?source=tout`) : l'inventaire « Interne » (par défaut), « Tout » (l'interne et toutes les
sauvegardes additionnés) ou le sac d'une sauvegarde (sac et PC additionnés pour les jeux de 1re à 3e génération), en **lecture seule** (les transferts passent par les fenêtres de
retrait et de dépôt). Pour une sauvegarde, une CT porte son numéro dans le jeu (« CT26 Séisme ») ; pour « Tout », le numéro
de chaque jeu (« Séisme — CT26 (Platine), DT10 (Épée) »), et un clic sur un objet ouvre à droite le détail de l'endroit où il
se trouve (avec le sac ou le PC : « Rouge / Bleu — Red (CT26, PC) »). Les sauvegardes illisibles ou non prises en charge sont signalées au-dessus de la liste. Le bouton « Journal » ouvre
le journal des opérations dans ce même panneau à droite (un tiers de la page, fermé à chaque arrivée) qui réduit la largeur de la
page ; sur un téléphone, il recouvre l'écran. « Retirer de l'argent »
ouvre une fenêtre en trois étapes (sauvegarde stockée → montant libre → confirmation) : la somme est retirée **de la
copie stockée sur le serveur** et créditée sur le porte-monnaie, en une seule transaction (copie avant/après dans
`Data/backups/`). Il n'y a pas encore de transfert du porte-monnaie vers une sauvegarde.

Les **objets** (objets ordinaires, Pokéballs, médicaments, baies, CT et DT) vont dans les deux sens, avec la même fenêtre
en trois étapes (sauvegarde stockée → quantité de chaque objet, un onglet par catégorie, avec une recherche par nom →
confirmation) : « Retirer d'une sauvegarde » les prend dans le sac de la
copie stockée, « Déposer dans une sauvegarde » les y ajoute. Une même opération peut mêler les catégories ; le journal liste
les objets déplacés (« 10 × Poké Ball, 6 × Potion, 1 × Super Bonbon »). Les jeux de 1re à 3e génération ont aussi un **PC
d'objets** : la fenêtre ajoute pour eux deux onglets « Sac » et « PC » au-dessus des catégories, une même opération peut
toucher les deux (au dépôt, l'onglet choisit la destination), et le journal les distingue (« Sac : 3 × Potion ; PC : 1 ×
Pierre Lune »). Règles (`Services/SaveBag.cs`) :

- Catégories : les **objets ordinaires** (tout le reste, dans les poches Objets, Objets de combat, Trésors, Ingrédients,
  Bonbons et Méga-Gemmes : `SaveBag.ItemPouches`), toutes les Pokéballs (`SaveBag.BallItems`) et tout ce que les jeux de la 4e à la 9e génération rangent dans
  leur poche Médicaments (`SaveBag.MedicineItems` : soins, statuts, Rappel, Huile/Élixir, vitamines, PP Plus/Max,
  Super Bonbon, Plumes...), toutes les baies du sac (`SaveBag.BerryItems` : poche Baies de la 3e à la 9e génération,
  baies de capture de Let's Go, Baie Hopo, Extra Baies de Légendes Z-A), et les CT et DT (Disques Techniques d'Épée/Bouclier), dans une même catégorie. Les **CS ne
  sont jamais montrées ni modifiées** (les retirer pourrait bloquer la progression du joueur) : elles restent dans le sac,
  même quand toutes les CT de la poche sont retirées.
- **Jamais touchés** non plus : les objets rares (Vélo, Cannes...), pour la même raison, les Lettres (`SaveBag.MailItems` :
  leur message est enregistré à part, même quand le jeu les range avec les objets) et les Cristaux Z. Chaque jeu décide de
  la poche : un objet que le jeu de destination range parmi les objets rares y est refusé au dépôt. Le PC d'objets accepte
  presque tout : les objets rares et les CS y sont aussi ignorés. Rouge/Bleu n'ont pas de poche « Objets rares » (Bicyclette,
  Scope Sylphe, Cannes mélangés aux autres) : seuls les objets de la table de conversion sont transférables ; les fossiles
  le sont.
- **Baie Enigma** : en 3e génération, elle vient de l'e-Reader et son nom et son effet sont enregistrés dans la sauvegarde ;
  elle n'y est ni montrée, ni retirée, ni déposée. À partir de la 4e génération, c'est une baie normale. Seul le sac est
  concerné : les baies plantées ne sont jamais touchées.
- Les CT et DT sont rangées **par capacité** (« Séisme »), pas par numéro : un même numéro n'apprend pas la même capacité
  d'une génération à l'autre (CT01 : Mitra-Poing en Platine, Affûtage en Noir 2). Au dépôt, le site donne la CT ou la DT
  qui apprend cette capacité dans le jeu choisi (Séisme : CT26 dans Platine, DT10 dans Épée/Bouclier, CT149 dans
  Écarlate/Violet) ; si aucune ne l'apprend, ou seulement une CS (Surf dans Platine), le dépôt est refusé. Correspondances
  numéro → capacité de chaque jeu : listes PKHeX, dans `SaveBag` (Épée/Bouclier et Écarlate/Violet ont un ordre particulier).
- Un objet est repéré par son numéro d'objet PKHeX de 4e génération et suivantes, le même d'un jeu à l'autre : une Super
  Ball retirée de Platine peut être déposée dans Noir 2. Les jeux de 1re à 3e génération numérotent leurs objets autrement
  (Potion = 13 en 3e génération, 20 dans Rouge/Bleu) : ils sont convertis, par PKHeX pour la 2e et la 3e, par une table du
  site pour Rouge/Bleu (PKHeX ne la fait pas ; le Multi Exp de Rouge/Bleu, sans équivalent exact, n'y est pas). Les baies
  d'Or/Argent/Cristal deviennent leur équivalent (Baie → Baie Oran, Baie Mystère → Baie Mepo, Baie Miracle → Baie Prine...),
  l'Appât Ball est ajoutée à la main, et les Noigrumes Bleue et Jaune, dont les numéros sont inversés en 4e génération, sont
  remises dans l'ordre des jeux suivants. Les objets d'Or/Argent sans équivalent (Feuilles d'Or et d'Argent, Rubans, Gène
  Hargne...) ne sont ni montrés ni touchés. L'objet est cherché dans toutes les poches du sac (Balls dans la poche « Balls » en
  4e génération mais « Objets » en 5e, médicaments dans « Objets » en 3e).
- Pas de limite sur le site. Au dépôt, le jeu limite : un objet qu'il ne connaît pas (ex. la Plume Santé dans Platine,
  l'Ultra Ball des Ultra-Chimères dans Noir 2) est refusé, comme ce qui dépasse le maximum du sac (999 par objet en 4e et
  5e génération, 99 dans les poches de la 1re à la 3e génération et pour les CT de la 4e ; 20 objets différents dans le sac de
  Rouge/Bleu, 50 dans le PC des 1re à 3e générations). Une CT réutilisable (5e à 7e génération,
  Let's Go, CT d'Épée/Bouclier, Légendes Z-A) est limitée à 1 par sac ; la retirer l'enlève du jeu.
- **CT à usage unique** (1re à 4e génération, Diamant Étincelant/Perle Scintillante, Écarlate/Violet ; pas les DT
  d'Épée/Bouclier) : dès qu'une sauvegarde a **80 exemplaires ou plus** d'une même CT (sac et PC additionnés), cette CT y est cachée (sac de la
  sauvegarde, « Tout », détail d'un objet) et ne peut ni en être retirée ni y être déposée. Seule cette CT de cette
  sauvegarde est concernée : les autres CT du sac, et la même CT dans une autre sauvegarde, restent normales. Un dépôt ne
  peut pas l'amener à 80 (79 au plus). Le stockage interne n'a pas de limite. Seuil : `SaveBag.LimitedMachineThreshold`.

Après un retrait (argent ou objets) ou un dépôt, le jeu du joueur n'a pas ces changements : la sauvegarde est donc marquée
« modifiée sur le serveur ». Sa mise à jour est bloquée tant qu'elle n'a pas été téléchargée (bouton « Télécharger » de
« Mes sauvegardes »), puis n'accepte qu'un fichier avec **plus** de temps de jeu ; à temps de jeu égal, seul le fichier
exact rendu par le serveur passe (empreinte SHA-256 dans `SaveModification`), l'ancienne sauvegarde du jeu est refusée.
Dissuasif, pas infaillible : rien n'oblige le joueur à remplacer réellement sa sauvegarde.

### Taille de la collection

Chaque compte a une limite de Pokémon dans sa collection (stockage interne + sauvegardes stockées, œufs compris) : 1 000 au
départ, y compris pour les comptes créés avant cette règle. Toute opération qui fait grossir la collection au-delà
est refusée **en entière** avec un message qui chiffre le manque : dépôt dans le stockage interne (vérifié dès le glisser-déposer,
puis à la validation), ajout ou mise à jour d'une sauvegarde stockée. Ce qui réduit la collection n'est jamais bloqué :
un compte au-dessus de sa limite garde tout, mais ne peut plus rien ajouter.

« Agrandir » (page « Ma collection ») achète des lots de 100 emplacements pour 10 000 ₽ avec le porte-monnaie ;
l'achat apparaît dans le journal de l'inventaire. Valeurs dans `Services/CollectionCapacity.cs`.

Lors d'un retrait, PKHeX convertit le Pokémon au format du jeu si possible (ex. PK4 → PK5) et refuse ce qui
est incompatible (espèce, attaques ou objets inexistants dans le jeu, conversion vers une génération antérieure).

## Organisation du code

| Fichier | Rôle |
|---|---|
| `PokeBank/Services/SaveSession.cs` | Sauvegarde chargée + dépôts/retraits en attente (PKHeX) |
| `PokeBank/Services/BankService.cs` | Accès base, validation transactionnelle, copies de sécurité |
| `PokeBank/Services/PokemonInfo.cs` | Noms français (dont « Rouge / Bleu », « Or / Argent » pour les groupes de jeux), sprites, légalité |
| `PokeBank/Components/Pages/Bank.razor` | Page « Transfert et stockage interne » (`/transfert`) : boîtes, glisser-déposer, sélection multiple |
| `PokeBank/Components/Pages/Collection.razor` | Toute la collection sur une page (chargée au fil du défilement), filtre par origine, tri par arrivée (les plus récents d'abord, par défaut), par nom d'espèce ou par numéro de Pokédex (croissants), inversable, gardé dans l'adresse (`?tri=nom&ordre=desc`) ; les œufs toujours à la fin (leur espèce reste cachée), à égalité les plus récents d'abord ; le tri par nom ignore les accents (colonne `SortName`, remplie au démarrage pour les Pokémon plus anciens) ; 5 / 10 / 15 / 20 colonnes par défaut selon la largeur de la fenêtre en pixels physiques (téléphone, jusqu'à 1920, jusqu'à 2560, au-delà ; mise à l'échelle de Windows comprise), recalculées au redimensionnement, modifiables au curseur |
| `PokeBank/Components/Pages/Saves.razor` | Gestion des sauvegardes stockées (ajout, mise à jour, suppression) |
| `PokeBank/Services/StoredSaveService.cs` | Stockage, mise à jour et suppression des sauvegardes stockées |
| `PokeBank/Services/SaveIdentity.cs` | Empreinte d'une sauvegarde (unicité sur le serveur) |
| `PokeBank/Services/InventoryService.cs` | Inventaire : retrait de Pokédollars, retrait et dépôt d'objets (sauvegardes stockées), achat d'emplacements, journal |
| `PokeBank/Services/SaveBag.cs` | Objets du sac et du PC d'une sauvegarde (objets, Pokéballs, médicaments, baies, CT/DT par capacité ; jamais les objets rares, Lettres ni CS) : lecture, retrait, dépôt (PKHeX), conversion des numéros d'objets des jeux de 1re à 3e génération |
| `PokeBank/Services/CollectionCapacity.cs` | Taille maximale de la collection : règles, occupation, messages |
| `PokeBank/Components/Pages/Inventory.razor` | Page « Inventaire » (solde, stockage en onglets avec recherche, tri par onglet, liste/grille et choix de la source — interne, tout, une sauvegarde —, journal et détail d'un objet en panneau latéral, fenêtre de retrait d'argent) |
| `PokeBank/Components/Shared/ItemTransferDialog.razor` | Fenêtre de retrait / dépôt d'objets (toutes catégories en une opération ; onglets Sac / PC pour les jeux de 1re à 3e génération) |
| `PokeBank/Components/Shared/ItemSprite.razor` | Image d'un objet ; celle de l'objet inconnu quand le site n'en a pas |
| `PokeBank/wwwroot/images/` | Images servies par le site : `pokemon/<numéro d'espèce>.png` (et `pokemon/chromatique/`), `objets/<numéro d'objet>.png`, `objets/ct-<type>.png` pour les CT/DT, un `inconnu.png` dans chaque dossier |
| `outils/images.py` | Rassemble ces images (PokéAPI ; pour les objets, puis pokesprite et PKHeX par numéro d'objet) ; à relancer pour en ajouter |
| `PokeBank/Data/BankPokemon.cs` | Table des Pokémon (octets bruts + métadonnées ; `StoredSaveId` si lecture seule) |
| `PokeBank/Data/StoredSave.cs` | Table des sauvegardes stockées |
| `PokeBank/Data/Wallet.cs` | Tables du porte-monnaie et du journal des opérations |
| `PokeBank/Data/OwnedItem.cs` | Table des objets de l'inventaire (objets, Pokéballs, médicaments, baies ; CT/DT sous une clé de capacité, `SaveBag.MachineKey`) |

Ajouter une migration après modification du modèle :

```bash
dotnet tool restore
dotnet ef migrations add NomDeLaMigration --project PokeBank
```

## Mise en ligne sur un serveur

Sur un VPS Ubuntu / Debian, avec Docker : l'application tourne derrière **Caddy**, qui reçoit les visiteurs et gère
seul le certificat HTTPS (Let's Encrypt). Il faut un nom de domaine dont l'enregistrement DNS (type A) pointe vers
l'IP du VPS.

| Fichier | Rôle |
|---|---|
| `Dockerfile` | Image de l'application |
| `docker-compose.yml` | Application + Caddy, volume de données `pokebank_pokebank-data` |
| `Caddyfile` | Réglages de Caddy (le domaine vient de `.env`) |
| `.env.example` | Modèle des réglages : `DOMAIN` et `INVITE_CODE` (le vrai `.env` est créé sur le serveur) |
| `deploy/publier.cmd` (`publier.ps1`) | Depuis Windows : envoie le code sur le VPS et le met à jour |
| `deploy/installer-serveur.sh` | Sur le VPS, une seule fois : pare-feu, Docker, `.env`, sauvegarde nocturne, démarrage |
| `deploy/sauvegarder.sh` | Sauvegarde du volume de données (lancée chaque nuit par cron, gardée 14 jours) |

**Première installation**

1. Depuis Windows, dans le dossier du projet : `deploy\publier.cmd ubuntu@IP_DU_VPS`
2. Sur le VPS (`ssh ubuntu@IP_DU_VPS`) : `bash ~/pokebank/deploy/installer-serveur.sh`
   (demande le nom de domaine, génère le code d'invitation et l'affiche à la fin).

**Mises à jour** : `deploy\publier.cmd ubuntu@IP_DU_VPS` suffit. Les migrations de la base s'appliquent au démarrage ;
les données restent dans le volume Docker.

**Sauvegardes** : la sauvegarde de l'hébergeur ne couvre souvent que 24 h, or la base est le seul endroit où sont les
Pokémon déposés. `deploy/sauvegarder.sh` archive chaque nuit le volume dans `~/sauvegardes` sur le VPS ; en garder aussi
une copie ailleurs, par exemple : `scp -r ubuntu@IP_DU_VPS:~/sauvegardes C:\Sauvegardes\PokeBank`.
Pour restaurer une archive : `docker compose stop pokebank`, puis
`docker run --rm -v pokebank_pokebank-data:/data -v ~/sauvegardes:/backup alpine sh -c "rm -rf /data/* && tar xzf /backup/ARCHIVE.tgz -C /data"`,
puis `docker compose start pokebank`.

Réglages avancés (variables d'environnement, jamais de secret dans `appsettings.json`) : `Bank__InviteCode`,
`ConnectionStrings__DefaultConnection`, `Bank__BackupPath`, `Bank__KeysPath` (clés de connexion ; par défaut dans
`Data/keys`, donc dans le volume).

## Points de vigilance

- Licence : PKHeX est sous GPL-3.0 ; si tu publies ce code, il doit l'être sous GPL aussi (voir « Crédits »).
- Les images des Pokémon et des objets sont copiées dans le site (`wwwroot/images`, par `outils/images.py`) : le navigateur
  ne charge plus rien depuis GitHub. Une image par espèce (normale ou chromatique), sans les formes (Alola, Galar...). Pour
  les objets, PokéAPI les nomme d'après le nom anglais, qui ne correspond pas toujours, et n'a pas ceux d'Épée/Bouclier. Aucune source n'a les bonbons de Let's Go, les objets de
  Légendes Arceus et Z-A ni les ingrédients et matériaux d'Écarlate/Violet : ils affichent l'objet inconnu, en attendant
  qu'on dépose leur image sous leur numéro d'objet.
- Usage privé uniquement.
- Sur Noir 2 / Blanc 2, PKHeX accepte les Pokémon de génération 5 ou antérieure ; un Pokémon de génération 6+
  déposé depuis un autre jeu restera dans le stockage interne.

## Crédits

PokeBank s'appuie sur le travail d'autres projets :

| Projet | Utilisé pour | Licence |
|---|---|---|
| [PKHeX](https://github.com/kwsch/PKHeX) (Kaphotics et contributeurs), paquet `PKHeX.Core` | Lecture et écriture des sauvegardes, Pokémon, sacs, noms français, légalité ; images d'objets « Big Items » et « Artwork Items » (`outils/images.py`) | GPL-3.0 |
| [PokéAPI sprites](https://github.com/PokeAPI/sprites) | Images des Pokémon (normales et chromatiques), de la plupart des objets et des CT (`wwwroot/images`) | CC0 1.0 pour le dépôt ; images © The Pokémon Company |
| [PokéSprite](https://github.com/msikma/pokesprite) (msikma) | Images d'objets absentes de PokéAPI, retrouvées par numéro d'objet (`data/item-map.json`) | MIT ; images © Nintendo / Creatures Inc. / GAME FREAK Inc. |
| [Bootstrap](https://getbootstrap.com) | Mise en page et composants de l'interface (`wwwroot/lib/bootstrap`) | MIT |
| [ASP.NET Core, Blazor, Identity, Entity Framework Core](https://dotnet.microsoft.com) (Microsoft) | Site, comptes, accès à la base | MIT |
| [SQLite](https://sqlite.org) | Base de données | Domaine public |
| [xUnit](https://xunit.net), [coverlet](https://github.com/coverlet-coverage/coverlet) | Tests | Apache-2.0, MIT |
| [Caddy](https://caddyserver.com), [Docker](https://www.docker.com) | Mise en ligne (HTTPS, conteneurs) | Apache-2.0 |
| [Bulbapedia](https://bulbapedia.bulbagarden.net) | Référence consultée pour vérifier l'ordre des Noigrumes d'Or/Argent/Cristal | — |

Pokémon et tous les noms et images associés sont des marques et créations de Nintendo, Creatures Inc., GAME FREAK Inc. et
The Pokémon Company. PokeBank est un projet de fan privé, sans lien avec eux.
