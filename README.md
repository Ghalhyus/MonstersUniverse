# Monsuta Potaru

Banque Pokémon privée pour un cercle d'amis, basée sur [PKHeX.Core](https://github.com/kwsch/PKHeX). Anciennement
« PokeBank » (renommé pour s'éloigner de Pokémon Bank, le service de Nintendo) : le homebrew 3DS garde pour l'instant
son nom, **PokeBank Sync**, et les clés de connexion leur nom technique « PokeBank » (`SetApplicationName` dans
`Program.cs` : le changer déconnecterait tout le monde).

- **Stack** : ASP.NET Core 9 + Blazor Server (tout en C#, pas de JavaScript à écrire), SQLite, ASP.NET Identity.
- **Jeux testés** : Pokémon Noir 2 / Blanc 2. PKHeX sait lire les autres générations ; à valider au cas par cas.

## Lancer en local

Les images (Pokémon, objets, sprites Donjon Mystère, ~90 Mo) ne sont pas dans le dépôt Git : elles sont stockées sur le
serveur. Après avoir cloné, les récupérer par SSH (une archive, quelques secondes) :

```bash
outils\recuperer-images.cmd debian@IP_DU_VPS
```

Sans accès au serveur, `python outils/images.py` les reconstruit depuis leurs projets d'origine (~10 minutes). Sans images,
le site marche mais affiche des points d'interrogation, et les tests d'images échouent avec un message qui l'explique.

```bash
dotnet run --project MonsutaPotaru
```

Puis ouvrir http://localhost:5265. En développement, le code d'invitation est `pokeamis`
(voir `MonsutaPotaru/appsettings.Development.json`). La base SQLite est créée automatiquement dans `MonsutaPotaru/Data/app.db`.

Tests :

```bash
dotnet test
```

`exemple-blanc2.sav` est une sauvegarde de test (Pikachu + Évoli) pour essayer le site sans vraie partie.
Pour la régénérer : `MONSUTA_POTARU_SAMPLE_OUT=chemin/fichier.sav dotnet test`.

`exemple-platine.sav` est une sauvegarde Platine (dresseur Lucas, 50 000 ₽) avec 50 Pokémon légaux d'espèces différentes,
tirés des vraies rencontres du jeu, et dans le sac 62 Pokéballs (Poké, Super, Hyper, Master, Luxe et Honor Ball),
26 médicaments (Potion, Super Potion, Total Soin, Rappel, Huile, Super Bonbon), 23 baies (Ceriz, Oran, Prine,
Sitrus, Enigma), 11 objets (Pierre Lune, Pierre Feu, Pépite, Restes, Attaque + dans la poche Objets de combat),
7 CT (2 CT01 Mitra-Poing, 3 CT06 Toxik, CT24 Tonnerre, CT26 Séisme), et ce que le site ne doit ni montrer ni toucher :
deux CS (Coupe, Surf), le Vélo (objet rare) et une Lettre Herbe.

Pour simuler « j'ai rejoué un peu » et passer la règle anti-copie de « Mes sauvegardes » (voir l'inventaire plus bas),
`MONSUTA_POTARU_ADD_PLAYTIME=chemin/fichier.sav dotnet test` ajoute une heure de jeu au fichier, modifié sur place (n'importe quel jeu). Pour la régénérer : `MONSUTA_POTARU_SAMPLE_PT_OUT=chemin/fichier.sav dotnet test`.

**Vraies sauvegardes de test** : les fichiers déposés dans `MonsutaPotaru.Tests/Sauvegardes/` (versionnés, malgré la règle `*.sav`) passent tout le parcours du site dans `RealSavesTests` : lecture et réécriture à l'identique,
dépôt d'un Pokémon puis retour au même emplacement, stockage (tous ses Pokémon dans la collection), retrait d'argent,
retrait de tous les objets du sac et du PC puis dépôt de retour (objets rares, CS et Cristaux Z intacts). Il suffit d'y ajouter un
fichier pour qu'il soit testé ; sans le dossier, ces tests ne vérifient rien. Actuellement : Rouge, Bleu, Argent (partie
randomisée : ses Pokémon sont signalés illégaux), SoulSilver, Noire (`noir.sav`), Noire 2, Rubis Oméga, Ultra-Soleil, Épée. SoulSilver : les blocs annexes
jamais écrits par le jeu (Tour de Combat, vidéos...) restent à zéro, sans somme de contrôle valide, et PKHeX marque à
l'écriture toutes les boîtes comme modifiées : le fichier change une fois, puis plus. En 1re et 2e génération, le fichier réécrit n'est pas identique octet pour octet (le contenu l'est) :
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
« Supprimer » propose de **télécharger et supprimer** (le fichier est d'abord rendu à l'utilisateur) ou de **supprimer sans
télécharger**, après une seconde confirmation. Ce second choix est grisé (et refusé par le serveur,
`StoredSaveService.DeleteWithoutDownloadAsync`) tant qu'une version modifiée sur le serveur (retrait d'argent, transfert
d'objets) n'a pas été téléchargée : ces changements, absents du jeu, seraient perdus. Une copie est gardée dans
`Data/backups/` à chaque fois, même sans téléchargement (non mentionnée au joueur : elle ne lui est pas accessible).

Partout où une sauvegarde stockée est nommée (« Jeu — Dresseur »), son **nom de fichier** l'accompagne, pour distinguer
deux parties du même jeu : en petit gris en dessous dans les fenêtres de choix (argent, objets), le détail d'un objet, la
fiche d'un Pokémon et le journal ; à la suite (« Noire — Ghal · noir.sav ») dans les listes déroulantes et l'infobulle
d'un Pokémon, qui n'ont qu'une ligne. Le journal garde le nom qu'avait le fichier au moment de l'opération, dans le même
texte que « Jeu — Dresseur » après un retour à la ligne (`WalletOperation.SaveLabelOf`) ; les opérations plus anciennes ont
reçu le nom actuel du fichier (migration `JournalFileNames`), sauf celles dont la sauvegarde avait été supprimée.

- Une même partie ne peut être stockée **qu'une fois sur tout le serveur**, tous comptes confondus. Elle est reconnue
  par génération + jeu + ID dresseur / ID secret + nom du dresseur (`SaveIdentity`) ; c'est dissuasif, pas infaillible
  (PKHeX permet de modifier ces valeurs). Les jeux de 1re et 2e génération n'ont pas d'ID secret.
- Tant qu'elle est stockée, la sauvegarde est **refusée dans « Transfert »** (au chargement et à la validation).
- Une mise à jour avec moins de temps de jeu que la version stockée demande confirmation (ancienne copie ?).

### Doublons (vieille copie d'une sauvegarde)

Un Pokémon déposé dans le stockage interne peut réapparaître dans une **vieille copie** de sa sauvegarde, celle d'avant le
dépôt (exemple vécu : le Pikachu Rockeur de Rubis Oméga, déposé le 13/10, retrouvé dans la sauvegarde stockée le 16/10).
Chaque Pokémon a une **empreinte** (`BankPokemon.Fingerprint`, `Services/PokemonFingerprint.cs`), sans l'espèce, qui
change à l'évolution :

- 3e génération et suivantes : PID + ID dresseur complet (ID + ID secret), qui ne changent jamais, même d'un jeu à l'autre ;
- 1re et 2e génération (sans PID) : IV + ID dresseur + nom du dresseur + famille d'évolution (environ 1 risque sur 65 000
  de confondre deux Pokémon du même dresseur et de la même famille).

Une sauvegarde dont un Pokémon est déjà **ailleurs dans le compte** (stockage interne ou autre sauvegarde stockée ;
**jamais entre comptes**) est **refusée** : « Sauvegarde refusée : 1 Pokémon de cette sauvegarde est déjà ailleurs dans ton
compte : Pikachu N.20 (stockage interne). C'est sans doute une vieille copie : envoie la sauvegarde actuelle de ton jeu. »
(5 noms au plus). Il faut envoyer la sauvegarde actuelle du jeu, qui n'a plus le Pokémon déposé.

- **« Mes sauvegardes »** (ajout et mise à jour) et la **3DS** (`raison` : `Duplicate`). Une mise à jour ne se compare
  pas à sa propre version précédente ; le contrôle passe avant la question du temps de jeu.
- **« Transfert »** : refus au chargement. La validation refuse aussi le dépôt d'un Pokémon déjà au stockage interne
  (déposé entre-temps depuis un autre onglet), avant les retraits : retirer l'original et déposer sa copie le dupliquerait.
  Un Pokémon retiré du stockage interne vers un jeu peut y revenir plus tard (il n'y est plus).
- Revers acceptés : deux Pokémon d'événement à PID fixe (même distribution) ou d'échange en jeu ont la même empreinte
  sans être des copies, et un Pokémon échangé entre deux de tes jeux bloque le second tant que le premier est stocké dans
  une version qui l'a encore (mettre à jour le premier d'abord).

L'empreinte est calculée à l'enregistrement, et au démarrage pour les Pokémon plus anciens (migration `PokemonFingerprints`).

### Synchronisation depuis une 3DS

Le homebrew **PokeBank Sync** (dépôt à part, `3DS_Saves_Synchronization`) envoie les sauvegardes Pokémon d'une 3DS
vers les sauvegardes stockées. Page « Synchronisation 3DS » (`/sauvegardes/3ds`), dans le menu repliable « Sauvegardes »
avec « Mes sauvegardes » (ouvert de lui-même sur l'une des deux pages) :

- **Relier une 3DS** affiche un code de 8 caractères (`ABCD-EFGH`, sans 0/O ni 1/I/L), valable 10 minutes et une seule
  fois ; en demander un autre ou « Annuler » le remplace. Tapé sur la console (majuscules, tiret et espaces facultatifs), il
  est échangé contre un **jeton longue durée** gardé sur sa carte SD ; le site n'en garde que l'empreinte SHA-256. La page
  guette la liaison (relecture toutes les 3 secondes tant que le code est affiché) et l'annonce d'elle-même.
- La liste des consoles reliées montre leur nom (envoyé par la console), la date de liaison et la dernière connexion.
  **Retirer** une console rend son jeton inutilisable ; les sauvegardes déjà envoyées restent.
- Un **envoi** ajoute une partie nouvelle, ou met à jour la partie déjà stockée **par ce compte**, avec toutes les règles
  ci-dessus. Un fichier identique à la version stockée est ignoré (« déjà à jour ») ; sous un autre nom (la console a reconnu la
  version exacte du jeu), seul le nom de la sauvegarde stockée change. Rien n'est jamais forcé : moins de
  temps de jeu, partie modifiée sur le serveur et pas encore téléchargée, partie d'un autre compte, collection pleine sont
  refusés avec le message du site, que la console affiche. Le nom de fichier envoyé est nettoyé (sans dossier, 100
  caractères au plus).

API (JSON, messages en français prêts à afficher ; `MonsutaPotaru/Api/SyncEndpoints.cs`) :

| Requête | Réponse |
|---|---|
| `POST /api/sync/liaison` `{"code", "nom"}` | `{"jeton", "compte", "message"}` ; 400 si le code est faux ou expiré, 429 au-delà de 10 essais par minute depuis la même adresse IP (celle transmise par Caddy) |
| `GET /api/sync/compte` | `{"compte", "console"}` : vérifie le jeton |
| `POST /api/sync/sauvegardes?fichier=nom.sav`, corps = fichier brut (4 Mo au plus) | `{"statut", "raison", "message"}` ; `statut` : `ajoutee`, `mise-a-jour`, `inchangee` ou `refusee` (`raison` : `StoreStatus`) |

Les deux dernières demandent `Authorization: Bearer <jeton>` (401 sinon, jeton inconnu ou console retirée).

### Filtres de « Ma collection »

Sous la recherche, des **pastilles** à la façon d'iTop (« Type : Feu, Eau ▾ × ») : un clic ouvre les valeurs à cocher, la
croix retire le filtre, « + Filtre » en ajoute un, « Tout effacer » les retire tous. Filtres : **génération** (d'après le
numéro de Pokédex : 1re = Bulbizarre à Mew...), **type** (types des jeux
récents de l'espèce et de sa forme : Mélofée est Fée même venu de Rouge/Bleu), **sexe** (mâle, femelle, asexué), **nature**,
**talent** (seuls ceux présents dans la collection sont proposés, avec une recherche), **IV** (minimum de 0 à 31 pour
chaque statistique, « IV : PV ≥ 31, Vit ≥ 25 »), **niveau** (« de » et « à », chacun facultatif, bornes comprises :
« Niveau : ≤ 29 », « ≥ 51 », « 20 à 40 » ; inversées si « de » dépasse « à »), **forme** (standard, Alola, Galar, Hisui,
Paldea, autre forme), **Méga-Évolution** (tient sa Méga-Gemme ou son orbe, comme pour l'image), **chromatique**,
**surnom** (nom affiché différent du nom de l'espèce : un surnom identique à l'espèce ne compte pas) et **œuf** (oui / non).
Les mêmes pastilles servent dans la fenêtre de choix du « Parc ». Les
valeurs d'un même filtre s'additionnent (Feu ou Eau), les filtres se cumulent (Feu et femelle) ; un filtre sans valeur
(« Tous ») ne filtre rien. Les œufs sont exclus dès qu'un autre filtre qu'« Œuf » est actif, pour ne pas révéler leur espèce.
Les filtres sont gardés dans l'adresse (`?generation=1,2&type=feu,eau&sexe=f&iv=pv31,vit25&niveau=20-40&surnom=oui&mega=oui`,
`niveau=-29` : 29 et moins, `niveau=51-` : 51 et plus), avec la recherche, l'origine et le tri.

En 1re et 2e génération, les IV (0 à 15) sont ramenés à l'échelle actuelle (× 2 + 1 : 15 → 31 ; Atq. Spé. et Déf. Spé.
valent le « Spécial »), la nature est celle que donne leur transfert vers les jeux suivants (expérience modulo 25, aussi
affichée sur leur fiche), et ils n'ont pas de talent (exclus par un filtre « Talent »). Ces caractéristiques sont
enregistrées avec chaque Pokémon (`Services/PokemonTraits.cs`) : la collection filtre en base, sans relire les fichiers.

### Images des Pokémon

Partout (collection, « Transfert », fiche d'un Pokémon), l'image suit le Pokémon : sa **forme** (Rattata d'Alola, Zarbi B,
Pikachu casquette...), sa **femelle** quand elle est différente du mâle (Pikachu, Rapasdepic...), sa version chromatique.
Un Pokémon qui **tient sa Méga-Gemme** est montré méga-évolué (Mysdibule avec Mysdibulite → Méga-Mysdibule ; Dracaufite X
ou Y → Méga-Dracaufeu X ou Y), de même Kyogre et Groudon avec leur orbe (Primo-Résurgence). Méga-Rayquaza n'a pas de gemme :
jamais montré. Un œuf garde l'image de son espèce. Une forme sans image à elle (identique à la forme de base, ou absente
de PokéAPI : Koraidon, Miraidon, Méga-Zygarde...) prend celle de l'espèce (liste dans `PokemonSpritesTests`). Les images
Gigamax sont rassemblées mais pas encore utilisées.

### Inventaire (Pokédollars et objets)

La page « Inventaire » affiche le porte-monnaie du compte et le « Stockage » de ses objets : un onglet par catégorie
(objets, Pokéballs, médicaments, baies, CT / DT, « Objets » ouvert par défaut ; l'onglet choisi est gardé dans l'adresse,
`?onglet=baies`), avec la quantité totale de chacune, en liste (par défaut) ou en grille (choix retenu par le navigateur). En grille, les colonnes sont automatiques (autant
que la largeur en permet, moins quand le journal est ouvert ; le curseur « Colonnes » affiche « Auto (10) ») ; le curseur
en fixe le nombre de 1 à 10 (vignettes élargies ou rétrécies), retenu par le navigateur, et le bouton « Auto » rend la
main à la largeur. Le nombre automatique est mesuré par le navigateur sur une grille vide invisible (`.item-grid-probe`,
`watchGridColumns` dans `App.razor`). La
recherche porte sur toute la source affichée : chaque onglet affiche alors le nombre d'objets trouvés. Chaque onglet a son
**tri**, retenu par le navigateur et valable pour toutes les sources : « Par défaut » (numéro d'objet commun à tous les jeux,
qui garde ensemble les Balls, les pierres, les potions... ; les CT par nom de capacité), « Nom » (accents ignorés ; pour une
CT, le nom de la capacité, pas son numéro) ou « Quantité » (à égalité, par nom), croissant au départ et inversable. Un sélecteur choisit
la source (gardée dans l'adresse, `?source=interne`) : « Tout » (par défaut, en premier : l'interne et toutes les
sauvegardes additionnés), l'inventaire « Interne » ou le sac d'une sauvegarde (sac et PC additionnés pour les jeux de 1re à 3e génération), en **lecture seule** (les transferts passent par les fenêtres de
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

### Parc

Le menu « Parc » (`/parc`) est une boîte PC (fond « Forêt » de Diamant Étincelant, répété en largeur) qui prend toute la
largeur de la page et la hauteur visible (sur un téléphone : les proportions de la boîte), où vivent jusqu'à **30 Pokémon** de la
collection, stockage interne ou sauvegardes stockées, de **toutes les générations** (hors œufs ; `ParkService.Capacity`).
« Choisir les Pokémon » ouvre la collection avec la recherche, les pastilles de filtres de « Ma collection » (génération,
type, forme...) et le compteur. Les espèces dont SpriteCollab n'a pas encore les sprites (64, surtout de 9e génération)
y sont grisées (🔒) et refusées ; elles viendront quand il les aura (relancer `outils/images.py donjon`, puis
`images-serveur`). Un clic sur un Pokémon du parc le fait se tourner vers toi et ouvre sa fiche, avec « Retirer du parc ».
Un Pokémon qui quitte la collection (retiré, sauvegarde mise à jour ou supprimée) quitte le parc (table `ParkMembers`,
suppression en cascade).

Animation dans le navigateur (`wwwroot/js/parc.js`), avec les sprites Donjon Mystère Idle, Walk et Sleep (8 directions)
de la **forme** du Pokémon (Raichu d'Alola, Ponyta de Galar, Zarbi B...) ou de sa **Méga-Évolution** s'il tient sa gemme,
chromatique et femelle quand SpriteCollab les a, sinon ceux de l'espèce (`PokemonSprites.DonjonFolder` : noms de formes de
PKHeX rapprochés de ceux de SpriteCollab, `images/donjon/formes.json`). Pour qu'ils aient l'air de vivre :
- une **énergie** qui baisse en marchant et remonte en dormant (3 à 5 minutes d'activité, puis une sieste d'une minute) ;
- des **intentions** : explorer un endroit du parc, rendre visite à un Pokémon de sa famille d'évolution (`ParkService.Family`)
  et rester un moment avec lui, observer autour de soi, aller dormir près d'un congénère ;
- un **caractère** tiré de la nature (Vitesse augmentée : plus actif ; diminuée : plus dormeur ; Déf. Spé. : plus sociable) ;
- la **nuit** (heure de l'appareil, 22 h – 6 h, progressive le soir et à l'aube) : le parc s'assombrit, ils s'endorment plus
  tôt et dorment longtemps ;
- la **curiosité** : un Pokémon éveillé se tourne vers le pointeur de la souris, et vient souvent voir de près ;
- le nom au survol, des « z » au-dessus des dormeurs, les plus bas dessinés devant. Les positions ne sont pas gardées.

### Living dex

Le menu « Living dex » (`/livingdex`, entre « Inventaire » et « Sauvegardes ») suit un **living dex** : un exemplaire de
chaque Pokémon du Pokédex d'un jeu. Pour l'instant les jeux de 1re génération (**Pokémon Rouge, Bleu et Jaune**), de 2e
(**Pokémon Or, Argent et Cristal**), de 3e (**Pokémon Rubis, Saphir, Émeraude, Rouge Feu et Vert Feuille** ; pas
Colosseum ni XD), de 4e (**Pokémon Diamant, Perle, Platine, Or HeartGold et Argent SoulSilver**, noms français de PKHeX)
et de 5e (**Pokémon Noire, Blanche, Noire 2 et Blanche 2**).

- **Création** : « + Créer un living dex », d'abord le **jeu** (jaquettes groupées par génération ; un jeu qui a déjà son
  living dex est grisé), puis une de ses **sauvegardes** stockées pas encore liées. PKHeX reconnaît Jaune, Cristal,
  Émeraude et Platine, mais pas Rouge de Bleu, Or d'Argent, Rubis de Saphir, Rouge Feu de Vert Feuille, Diamant de Perle
  ni HeartGold de SoulSilver (même format) : une sauvegarde Rouge / Bleu est proposée pour Rouge comme pour Bleu, de même
  pour les autres paires (pour Rouge Feu / Vert Feuille, PKHeX répond toujours « Rouge Feu » faute de mieux). En 5e
  génération, chaque sauvegarde dit son jeu. **Un living dex par jeu** et par compte, une
  sauvegarde pour un seul living dex (index uniques).
- **Comptage** : un Pokémon est enregistré s'il est dans l'**équipe ou les boîtes** de la sauvegarde liée (pas les œufs) ;
  le living dex suit donc chaque mise à jour de la sauvegarde (site ou 3DS). Le premier exemplaire (équipe, puis boîtes)
  remplit la case.
- **Modes** : Régional (le Pokédex de la région), National (tout le national que le jeu peut contenir) et Complet (chaque
  forme de chaque Pokémon du national : formes régionales, Méga-Évolutions, Gigamax, formes qui changent l'aspect comme
  Sancoki / Tritosor ou les lettres de Zarbi ; pas le sexe ; **seulement celles qu'une sauvegarde du jeu peut contenir**).
  En Régional et National, n'importe quelle forme d'une espèce compte ; en Complet, la forme exacte. **Un mode identique à
  un autre, ordre compris, est caché** :
  - 1re génération (une région, aucune forme) : le National seul, 151 Pokémon, sans onglets ;
  - 2e génération : **Régional** (Pokédex de Johto, `?mode=regional` : les mêmes 251 Pokémon que le national, dans l'ordre
    et avec les numéros du jeu, Germignon n° 1, Celebi n° 251 ; infobulle « Johto #001 Germignon (national #152) »),
    **National** (251) et **Complet** (276 : les 26 lettres de Zarbi, A à Z, enregistrées dans chaque Zarbi ; aucune autre
    forme dans ces jeux) ;
  - 3e génération : **Régional** (Hoenn pour Rubis, Saphir et Émeraude : 202 Pokémon, Arcko n° 1, Deoxys n° 202 ; Kanto
    pour Rouge Feu et Vert Feuille : les 151 premiers), **National** (386) et **Complet** (413 : les 28 lettres de Zarbi,
    avec « ! » et « ? »). Les formes de Morphéo n'existent qu'en combat : une seule case. **Deoxys** n'enregistre pas sa
    forme en 3e génération, c'est le jeu qui la donne : sa case (dans tous les modes) a la forme et l'image de celle du jeu
    (Normale dans Rubis / Saphir, Vitesse dans Émeraude, Attaque dans Rouge Feu, Défense dans Vert Feuille) et n'importe
    quel Deoxys la remplit. Les trois autres formes, impossibles dans la partie, ne comptent pas (une appli qui les compte,
    avec celles de Morphéo, arrive à 419) ;
  - 4e génération : **Régional** (Sinnoh de Diamant / Perle : 151, Tortipouss n° 1, Manaphy n° 151 ; Sinnoh agrandi de
    Platine : 210, Giratina n° 210 ; Johto de HeartGold / SoulSilver : 256, Celebi n° 256), **National** (493) et
    **Complet** : les 28 lettres de Zarbi, les 4 formes de Deoxys (chaque Deoxys garde la sienne en 4e), les 3 capes de
    Cheniti et de Cheniselle, les mers Occident / Orient de Sancoki et Tritosor (529 en Diamant / Perle) ; à partir de
    Platine, les 6 formes de Motisma, Giratina Originel (il tient l'Orbe Platiné, ce qui se garde en boîte) et Shaymin
    Céleste (536 en Platine) ; en HeartGold / SoulSilver, Pichu Troizépi (537 ; il ne peut pas aller dans les autres
    jeux). Les formes de chaque jeu viennent de PKHeX (`PersonalTable` : Motisma n'en a qu'une en Diamant / Perle).
    **Arceus** n'a qu'une case, quelle que soit sa Plaque ; Morphéo et Ceriflor non plus (formes de combat).
    **Shaymin Céleste** redevient Terrestre dans une boîte : pour ne pas bloquer l'équipe, sa case se remplit avec un
    **deuxième Shaymin** (n'importe quelle forme, le premier remplit la case Shaymin) **et la Gracidée** dans le sac de la
    sauvegarde (objet rare : la banque n'y touche pas) ; l'infobulle dit ce qui manque (« il faut un deuxième Shaymin et
    la Gracidée dans le sac ») ou « Gracidée dans le sac ». En Régional et National, un Shaymin suffit ;
  - 5e génération : **Régional** (Unys de Noire / Blanche : 156 Pokémon ; de Noire 2 / Blanche 2 : 301 ; numérotés **à
    partir de 0**, Victini n° 000, jusqu'à Genesect), **National** (649) et **Complet** : les formes de la 4e (sauf Pichu
    Troizépi, qui ne passe pas en 5e ; même règle pour Shaymin Céleste), Bargantua motif Bleu et les 4 saisons de
    Vivaldaim et Haydaim (699 en Noire / Blanche) ; en Noire 2 / Blanche 2, les formes Totémiques de Boréas, Fulguris et
    Démétéros, Kyurem Blanc et Noir, Keldeo Décidé (705). Seuls les Vivaldaim et Haydaim **de l'équipe** prennent la
    saison du jeu (au chargement) : en boîte, ils gardent la leur ; l'infobulle d'une saison manquante le rappelle (« à
    garder en boîte »). **Genesect** n'a qu'une case, quel que soit son Module (comme Arceus) ; Darumacho Transe et
    Meloetta Danse n'existent qu'en combat.
- **Page d'un living dex** (`/livingdex/{id}`) : jaquette officielle, compteur et barre de progression, « Manquants
  seulement », grille dans l'ordre du Pokédex avec les **sprites du jeu** (Rouge / Bleu : couleurs du Super Game Boy, Jaune :
  Game Boy Color ; Or, Argent et Cristal ont chacun les leurs ; Rubis / Saphir, Émeraude, Rouge Feu / Vert Feuille,
  Diamant / Perle, Platine et HeartGold / SoulSilver aussi ; ceux, fixes, de Noire / Blanche pour toute la 5e génération :
  PokéAPI n'a pas ceux de Noire 2 / Blanche 2),
  **une image par forme** (Zarbi B, Deoxys Vitesse...), en couleur si le Pokémon est dans la sauvegarde, en **silhouette
  noire** sinon. Infobulle : numéro, nom (« Zarbi B » en Complet, « Deoxys (forme Vitesse) »)
  et place (« Boîte 2, case 8 ») ou « manquant » ; un clic sur un Pokémon possédé ouvre sa fiche. Nombre de colonnes au
  curseur, comme dans « Ma collection » (3 à 20, selon la largeur de la fenêtre par défaut, `?colonnes=12` ;
  `Shared/GridColumns.cs`, commun aux deux pages).
- **Suppression** : « Supprimer ce living dex » (la sauvegarde reste). Supprimer la sauvegarde supprime son living dex
  (suppression en cascade), ce que la confirmation de « Mes sauvegardes » signale.

Données : table `LivingDexes` (`Data/LivingDex.cs`), jeux et modes dans `LivingDexGames` (`Services/LivingDexService.cs`).
Images : `outils/images.py livingdex` (sprites de PokéAPI, jaquettes de Bulbagarden Archives, en anglais faute de
françaises), téléchargées sur le VPS à chaque déploiement (ou `deploy\images-serveur.cmd`) ; formes : `LIVINGDEX_FORMS`
(noms de fichiers de PokéAPI ; Deoxys, Cheniselle, Motisma, Giratina, Shaymin, Bargantua, les Totémiques, Kyurem et Keldeo
d'après ses Pokémon 10001 à 10024). Sans
elles, la grille affiche l'image de la même
forme sur le site (`LivingDexGames.Fallback`).

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
| `MonsutaPotaru/Services/SaveSession.cs` | Sauvegarde chargée + dépôts/retraits en attente (PKHeX) |
| `MonsutaPotaru/Services/BankService.cs` | Accès base, validation transactionnelle, copies de sécurité |
| `MonsutaPotaru/Services/PokemonFingerprint.cs` | Empreinte d'un Pokémon et recherche de ses doublons dans le compte (vieille copie d'une sauvegarde) |
| `MonsutaPotaru/Services/PokemonInfo.cs` | Noms français (dont « Rouge / Bleu », « Or / Argent », « Diamant / Perle », « Or HeartGold / Argent SoulSilver » pour les groupes de jeux : une sauvegarde de ces jeux ne dit pas lequel des deux ; migrations `GameNames` et `GameGroupNames` pour les noms déjà enregistrés), sprites, légalité |
| `MonsutaPotaru/Components/Pages/Bank.razor` | Page « Transfert et stockage interne » (`/transfert`) : boîtes, glisser-déposer, sélection multiple |
| `MonsutaPotaru/Components/Pages/Park.razor` | Page « Parc » : boîte où vivent jusqu'à 30 Pokémon, fenêtre de choix, fiche au clic |
| `MonsutaPotaru/wwwroot/js/parc.js` | Animation et comportement des Pokémon du parc (énergie, intentions, famille, nuit, curiosité) ; `simulate` pour les essais |
| `MonsutaPotaru/Services/ParkService.cs` | Pokémon du parc : liste, choix (30 au plus, avec des sprites Donjon Mystère), retrait, famille d'évolution |
| `MonsutaPotaru/Data/ParkMember.cs` | Table des Pokémon au parc (supprimée avec le Pokémon) |
| `MonsutaPotaru/Components/Pages/LivingDexes.razor` | Page « Living dex » : cartes des living dex du compte, création (jeu, puis sauvegarde) |
| `MonsutaPotaru/Components/Pages/LivingDexPage.razor` | Page d'un living dex : grille des sprites du jeu (silhouettes des manquants), progression, fiche au clic |
| `MonsutaPotaru/Services/LivingDexService.cs` | Living dex : jeux (`LivingDexGames` : jaquette, sprites, Pokémon de chaque mode, modes cachés), comptage dans la sauvegarde, création, suppression |
| `MonsutaPotaru/Data/LivingDex.cs` | Table des living dex (un par jeu et par compte, supprimé avec sa sauvegarde) |
| `MonsutaPotaru/Components/Pages/Collection.razor` | Toute la collection sur une page (chargée au fil du défilement), filtres en pastilles (`Shared/CollectionFilterChips.razor`), filtre par origine, tri par arrivée (les plus récents d'abord, par défaut), par nom d'espèce ou par numéro de Pokédex (croissants), inversable, gardé dans l'adresse (`?tri=nom&ordre=desc`) ; les œufs toujours à la fin (leur espèce reste cachée), à égalité les plus récents d'abord ; le tri par nom ignore les accents (colonne `SortName`, remplie au démarrage pour les Pokémon plus anciens) ; 5 / 10 / 15 / 20 colonnes par défaut selon la largeur de la fenêtre en pixels physiques (téléphone, jusqu'à 1920, jusqu'à 2560, au-delà ; mise à l'échelle de Windows comprise), recalculées au redimensionnement, modifiables au curseur |
| `MonsutaPotaru/Components/Pages/Saves.razor` | Gestion des sauvegardes stockées (ajout, mise à jour, suppression) |
| `MonsutaPotaru/Components/Pages/Sync3ds.razor` | « Synchronisation 3DS » : consoles reliées (code de liaison, attente de la console, retrait) |
| `MonsutaPotaru/Services/SyncService.cs` | Synchronisation 3DS : codes de liaison, jetons, envoi d'une sauvegarde (ajout, mise à jour ou « déjà à jour ») |
| `MonsutaPotaru/Api/SyncEndpoints.cs` | API du homebrew PokeBank Sync (`/api/sync/...`) et limite d'essais de codes |
| `MonsutaPotaru/Services/StoredSaveService.cs` | Stockage, mise à jour et suppression des sauvegardes stockées |
| `MonsutaPotaru/Services/SaveIdentity.cs` | Empreinte d'une sauvegarde (unicité sur le serveur) |
| `MonsutaPotaru/Services/InventoryService.cs` | Inventaire : retrait de Pokédollars, retrait et dépôt d'objets (sauvegardes stockées), achat d'emplacements, journal |
| `MonsutaPotaru/Services/SaveBag.cs` | Objets du sac et du PC d'une sauvegarde (objets, Pokéballs, médicaments, baies, CT/DT par capacité ; jamais les objets rares, Lettres ni CS) : lecture, retrait, dépôt (PKHeX), conversion des numéros d'objets des jeux de 1re à 3e génération |
| `MonsutaPotaru/Services/CollectionCapacity.cs` | Taille maximale de la collection : règles, occupation, messages |
| `MonsutaPotaru/Components/Pages/Inventory.razor` | Page « Inventaire » (solde, stockage en onglets avec recherche, tri par onglet, liste/grille (colonnes de la grille au curseur) et choix de la source — interne, tout, une sauvegarde —, journal et détail d'un objet en panneau latéral, fenêtre de retrait d'argent) |
| `MonsutaPotaru/Components/Shared/ItemTransferDialog.razor` | Fenêtre de retrait / dépôt d'objets (toutes catégories en une opération ; onglets Sac / PC pour les jeux de 1re à 3e génération) |
| `MonsutaPotaru/Components/Shared/ItemSprite.razor` | Image d'un objet ; celle de l'objet inconnu quand le site n'en a pas |
| `MonsutaPotaru/Services/PokemonCriteria.cs` | Filtres de « Ma collection » : valeurs choisies, requête en base, lecture et écriture dans l'adresse |
| `MonsutaPotaru/Services/PokemonTraits.cs` | Caractéristiques d'un Pokémon enregistrées pour ces filtres (types récents, nature, talent, IV, forme, Méga) |
| `MonsutaPotaru/Services/PokemonSprites.cs` | Choix de l'image d'un Pokémon : forme (noms de PKHeX → noms de PokéAPI), Méga-Évolution selon l'objet tenu, femelle, chromatique |
| `MonsutaPotaru/wwwroot/images/` | Hors du dépôt Git (voir « Lancer en local ») ; images servies par le site : `pokemon/<numéro d'espèce>.png`, `pokemon/<numéro>-<forme>.png` (`19-alola.png`, `6-mega-x.png`, `6-gmax.png`), `pokemon/femelle/`, `pokemon/chromatique/` (mêmes sous-dossiers), `objets/<numéro d'objet>.png`, `objets/ct-<type>.png` pour les CT/DT, un `inconnu.png` dans chaque dossier |
| `MonsutaPotaru/wwwroot/images/parc/foret.png` | Fond du parc (boîte « Forêt » de Diamant Étincelant, PKHeX) |
| `MonsutaPotaru/wwwroot/images/donjon/` | Sprites animés de SpriteCollab (Idle, Walk, Sleep) dans les dossiers de SpriteCollab (`0025/`, `0025/0000/0001/` chromatique...) : planches `<Animation>-Anim.png` et `AnimData.xml` réduit à ces animations ; `credits.txt` (auteurs) |
| `outils/recuperer-images.cmd` | Copie les images du serveur dans `wwwroot/images` par SSH (après un clonage) |
| `deploy/images-serveur.cmd` (`images-serveur.ps1`) | Remplit ou met à jour les images du serveur (`~/monsuta-potaru-images`) et redémarre le site |
| `outils/images.py` | Rassemble ces images (PokéAPI ; pour les objets, puis pokesprite et PKHeX par numéro d'objet) ; à relancer pour en ajouter |
| `MonsutaPotaru/Data/BankPokemon.cs` | Table des Pokémon (octets bruts + métadonnées ; `StoredSaveId` si lecture seule) |
| `MonsutaPotaru/Data/StoredSave.cs` | Table des sauvegardes stockées |
| `MonsutaPotaru/Data/SyncDevice.cs` | Tables des consoles 3DS reliées (empreinte du jeton) et des codes de liaison |
| `MonsutaPotaru/Data/Wallet.cs` | Tables du porte-monnaie et du journal des opérations |
| `MonsutaPotaru/Data/OwnedItem.cs` | Table des objets de l'inventaire (objets, Pokéballs, médicaments, baies ; CT/DT sous une clé de capacité, `SaveBag.MachineKey`) |

Ajouter une migration après modification du modèle :

```bash
dotnet tool restore
dotnet ef migrations add NomDeLaMigration --project MonsutaPotaru
```

## Mise en ligne sur un serveur

Sur un VPS Ubuntu / Debian, avec Docker : l'application tourne derrière **Caddy**, qui reçoit les visiteurs et gère
seul le certificat HTTPS (Let's Encrypt). Il faut un nom de domaine dont l'enregistrement DNS (type A) pointe vers
l'IP du VPS.

| Fichier | Rôle |
|---|---|
| `Dockerfile` | Image de l'application |
| `docker-compose.yml` | Application + Caddy, volume de données `monsuta-potaru_monsuta-potaru-data` |
| `Caddyfile` | Réglages de Caddy (le domaine vient de `.env`) |
| `.env.example` | Modèle des réglages : `DOMAIN` et `INVITE_CODE` (le vrai `.env` est créé sur le serveur) |
| `deploy/publier.cmd` (`publier.ps1`) | Depuis Windows : envoie le code sur le VPS et le met à jour |
| `deploy/installer-serveur.sh` | Sur le VPS, une seule fois : pare-feu, Docker, `.env`, sauvegarde nocturne, démarrage |
| `deploy/sauvegarder.sh` | Sauvegarde du volume de données (lancée chaque nuit par cron, gardée 14 jours) |
| `deploy/renommer-serveur.cmd` (`.ps1`, `.sh`) | Une seule fois : passage du serveur de « PokeBank » à « Monsuta Potaru » (voir plus bas) |
| `.github/workflows/ci-cd.yml` | GitHub Actions : tests à chaque push, déploiement par un bouton (voir « CI/CD ») |

**Première installation**

1. Depuis Windows, dans le dossier du projet : `deploy\publier.cmd ubuntu@IP_DU_VPS`
2. Sur le VPS (`ssh ubuntu@IP_DU_VPS`) : `bash ~/monsuta-potaru/deploy/installer-serveur.sh`
   (demande le nom de domaine, génère le code d'invitation et l'affiche à la fin).
3. Depuis Windows : `deploy\images-serveur.cmd ubuntu@IP_DU_VPS` (images, voir plus bas).

**Mises à jour** : par GitHub (« CI/CD » ci-dessous), ou `deploy\publier.cmd ubuntu@IP_DU_VPS` depuis Windows. Les
migrations de la base s'appliquent au démarrage ; les données restent dans le volume Docker.

**Changement de nom du serveur** (une seule fois, pour un serveur installé sous le nom « PokeBank ») : Docker préfixe ses
volumes du nom du projet, le nouveau nom repartirait donc d'une base vide. Après avoir poussé le code renommé :

1. `deploy\renommer-serveur.cmd ubuntu@IP_DU_VPS` : sauvegarde (comme chaque nuit), arrêt de l'ancien site, **copie**
   des volumes `pokebank_*` vers `monsuta-potaru_*` (base, copies de sécurité, clés de connexion, certificats HTTPS ;
   nombre de fichiers vérifié), `~/pokebank-images` déplacé en `~/monsuta-potaru-images`, `.env` copié dans
   `~/monsuta-potaru`, sauvegarde nocturne repointée. Relancé, il ne refait rien. Le site est arrêté à partir d'ici.
2. « Run workflow » : envoie le code dans `~/monsuta-potaru` et redémarre le site (sa sauvegarde du début est sautée
   cette fois-là : le dossier n'a encore que le `.env`).

`~/pokebank` et les volumes `pokebank_*` restent pour revenir en arrière ; le script affiche à la fin les commandes de
retour et de ménage (à faire une fois le site vérifié). Les anciennes archives `pokebank-*.tgz` de `~/sauvegardes`
partent au bout de 14 jours, comme les nouvelles.

**CI/CD** (GitHub Actions, `.github/workflows/ci-cd.yml`) :

- **Tests** à chaque push, sur une machine Ubuntu propre (un fichier oublié dans un commit s'y voit) : .NET 9, images
  copiées depuis `~/monsuta-potaru-images` du VPS par SSH (exigées par les tests d'images), puis `dotnet test`. Résultat dans
  l'onglet « Actions » du dépôt et à côté de chaque commit. Attention : Linux distingue majuscules et minuscules dans les
  noms de fichiers, Windows non.
- **Déploiement** : onglet « Actions » → « CI/CD » → « Run workflow » sur `main`. Il ne part que si les tests passent, puis :
  sauvegarde des données (`sauvegarder.sh`, quelques secondes de coupure), envoi de la même archive que `publier.ps1`,
  **images manquantes** (`outils/images.py --sortie ~/monsuta-potaru-images`, comme `images-serveur` : les présentes sont gardées ;
  un échec ne bloque pas, avertissement orange et journal complet dans `~/monsuta-potaru-images.log` sur le VPS),
  `docker compose up -d --build`, et vérification que le site répond (une minute au plus, sinon échec). Le dépôt est
  public, ses journaux d'Actions aussi : ils n'affichent ni secret ni journal du site (à lire par SSH). Jamais deux déploiements à la fois. La première installation se fait toujours avec `publier.cmd`.

Mise en place (une fois), avec une clé SSH réservée à GitHub (révocable sans toucher à la tienne), dans PowerShell :

1. Créer la clé, sans phrase de passe (Entrée deux fois) :
   `ssh-keygen -t ed25519 -C github-actions-monsuta-potaru -f "$HOME\.ssh\monsuta-potaru-github"`
2. L'autoriser sur le VPS :
   `Get-Content "$HOME\.ssh\monsuta-potaru-github.pub" | ssh ubuntu@IP_DU_VPS "cat >> ~/.ssh/authorized_keys"`
3. Lire l'empreinte du VPS que ton PC connaît déjà (lignes sans `#`) : `ssh-keygen -F IP_DU_VPS`
4. Sur GitHub, « Settings » → « Secrets and variables » → « Actions » → « New repository secret » :
   `VPS_HOST` (IP du VPS), `VPS_USER` (`ubuntu`, `debian`...), `VPS_SSH_KEY` (tout le contenu de
   `$HOME\.ssh\monsuta-potaru-github`, la clé **privée**, lignes `-----BEGIN/END` comprises), `VPS_KNOWN_HOSTS` (les lignes
   de l'étape 3).

Pour retirer l'accès de GitHub : effacer la ligne `github-actions-monsuta-potaru` de `~/.ssh/authorized_keys` sur le VPS
(`github-actions-pokebank` pour la clé créée avant le changement de nom : elle reste valable, rien à refaire).

**Images** : ni dans le dépôt ni dans la publication. Elles sont stockées sur le VPS dans `~/monsuta-potaru-images` (hors de la
sauvegarde nocturne : elles se retéléchargent), monté en lecture seule à la place de `wwwroot/images` (`docker-compose.yml`)
et servi depuis le disque (`Program.cs` : `MapStaticAssets` ne connaît que les fichiers présents à la compilation).
`deploy\images-serveur.cmd ubuntu@IP_DU_VPS` lance `outils/images.py --sortie ~/monsuta-potaru-images` sur le VPS (téléchargement
depuis les projets d'origine, ~10 minutes la première fois, les images présentes sont gardées), puis redémarre le site,
qui lit la liste des images au démarrage. À relancer pour mettre les images à jour. Les développeurs les copient depuis le
VPS avec `outils\recuperer-images.cmd` (voir « Lancer en local »).

**Sauvegardes** : la sauvegarde de l'hébergeur ne couvre souvent que 24 h, or la base est le seul endroit où sont les
Pokémon déposés. `deploy/sauvegarder.sh` archive chaque nuit le volume dans `~/sauvegardes` sur le VPS ; en garder aussi
une copie ailleurs, par exemple : `scp -r ubuntu@IP_DU_VPS:~/sauvegardes C:\Sauvegardes\Monsuta Potaru`.
Pour restaurer une archive : `docker compose stop monsuta-potaru`, puis
`docker run --rm -v monsuta-potaru_monsuta-potaru-data:/data -v ~/sauvegardes:/backup alpine sh -c "rm -rf /data/* && tar xzf /backup/ARCHIVE.tgz -C /data"`,
puis `docker compose start monsuta-potaru`.

Réglages avancés (variables d'environnement, jamais de secret dans `appsettings.json`) : `Bank__InviteCode`,
`ConnectionStrings__DefaultConnection`, `Bank__BackupPath`, `Bank__KeysPath` (clés de connexion ; par défaut dans
`Data/keys`, donc dans le volume).

## Points de vigilance

- Licence : PKHeX est sous GPL-3.0 ; si tu publies ce code, il doit l'être sous GPL aussi (voir « Crédits »).
- Les images des Pokémon et des objets sont servies par le site (`wwwroot/images`, stockées sur le serveur, voir « Images » de
  la mise en ligne) : le navigateur ne charge plus rien depuis GitHub. Les formes, femelles et Méga-Évolutions sont décrites plus haut (« Images des Pokémon »). Pour
  les objets, PokéAPI les nomme d'après le nom anglais, qui ne correspond pas toujours (pokesprite et PKHeX complètent, par numéro).
  Depuis le 17/10/2026, PokéAPI a aussi les objets d'Épée/Bouclier et suivants (bonbons de Let's Go, ingrédients...) : il
  ne reste sans image (~330) que des objets rares, Cristaux Z en double, objets de Légendes Arceus et accessoires de
  pique-nique, qui affichent l'objet inconnu en attendant qu'on dépose leur image sous leur numéro d'objet.
- Usage privé uniquement.
- Sur Noir 2 / Blanc 2, PKHeX accepte les Pokémon de génération 5 ou antérieure ; un Pokémon de génération 6+
  déposé depuis un autre jeu restera dans le stockage interne.

## Crédits

Monsuta Potaru s'appuie sur le travail d'autres projets :

| Projet | Utilisé pour | Licence |
|---|---|---|
| [PKHeX](https://github.com/kwsch/PKHeX) (Kaphotics et contributeurs), paquet `PKHeX.Core` | Lecture et écriture des sauvegardes, Pokémon, sacs, noms français, légalité ; images d'objets « Big Items » et « Artwork Items », fond de boîte « Forêt » de Diamant Étincelant du parc (`outils/images.py`) | GPL-3.0 |
| [PokéAPI sprites](https://github.com/PokeAPI/sprites) | Images des Pokémon (normales et chromatiques, formes, femelles, Méga-Évolutions, Gigamax), de la plupart des objets et des CT (`wwwroot/images`) ; sprites de Rouge / Bleu, Jaune, Or, Argent, Cristal, Rubis / Saphir, Émeraude, Rouge Feu / Vert Feuille, Diamant / Perle, Platine, HeartGold / SoulSilver et Noire / Blanche des living dex, formes comprises (`versions/generation-i` à `generation-v`, `wwwroot/images/livingdex`) | CC0 1.0 pour le dépôt ; images © The Pokémon Company |
| [PokéAPI](https://github.com/PokeAPI/pokeapi) (données) | Liste des formes de chaque espèce et des espèces dont la femelle est différente (tables `pokemon`, `pokemon_forms`, `pokemon_species`, lues par `outils/images.py`) ; ordre des Pokédex régionaux des living dex (`pokemon_dex_numbers`, Pokédex « original-johto », « hoenn », « original-sinnoh », « extended-sinnoh », « updated-johto », « original-unova » et « updated-unova », repris dans `LivingDexGames`) | BSD-3-Clause |
| [PokéSprite](https://github.com/msikma/pokesprite) (msikma) | Images d'objets absentes de PokéAPI, retrouvées par numéro d'objet (`data/item-map.json`) | MIT ; images © Nintendo / Creatures Inc. / GAME FREAK Inc. |
| [SpriteCollab](https://github.com/PMDCollab/SpriteCollab) (PMD Collab, [sprites.pmdcollab.org](https://sprites.pmdcollab.org)) | Sprites animés façon Donjon Mystère du « Parc », animations Idle, Walk et Sleep (`wwwroot/images/donjon`, par `outils/images.py donjon`) ; auteurs de chaque sprite dans `wwwroot/images/donjon/credits.txt` | CC BY-NC 4.0 (usage non commercial, auteurs à citer) |
| [Bootstrap](https://getbootstrap.com) | Mise en page et composants de l'interface (`wwwroot/lib/bootstrap`) | MIT |
| [ASP.NET Core, Blazor, Identity, Entity Framework Core](https://dotnet.microsoft.com) (Microsoft) | Site, comptes, accès à la base | MIT |
| [SQLite](https://sqlite.org) | Base de données | Domaine public |
| [xUnit](https://xunit.net), [coverlet](https://github.com/coverlet-coverage/coverlet) | Tests | Apache-2.0, MIT |
| [Caddy](https://caddyserver.com), [Docker](https://www.docker.com) | Mise en ligne (HTTPS, conteneurs) | Apache-2.0 |
| [GitHub Actions](https://github.com/features/actions) : [actions/checkout](https://github.com/actions/checkout), [actions/setup-dotnet](https://github.com/actions/setup-dotnet) | Tests et déploiement (`.github/workflows/ci-cd.yml`) | MIT |
| [Bulbapedia](https://bulbapedia.bulbagarden.net) | Référence consultée pour vérifier l'ordre des Noigrumes d'Or/Argent/Cristal | — |
| [Bulbagarden Archives](https://archives.bulbagarden.net) | Jaquettes officielles de Pokémon Rouge, Bleu, Jaune, Or, Argent, Cristal, Rubis, Saphir, Émeraude, Rouge Feu, Vert Feuille, Diamant, Perle, Platine, HeartGold, SoulSilver, Noire, Blanche, Noire 2 et Blanche 2 (versions anglaises réduites, « Red/Blue/Yellow/Gold/Silver/Crystal/Ruby/Sapphire/Emerald/FireRed/LeafGreen/Diamond/Pearl/Platinum/HeartGold/SoulSilver/Black/White/Black 2/White 2 EN boxart », Émeraude, Diamant, Perle, HeartGold et SoulSilver en JPEG), images des living dex (`wwwroot/images/livingdex/jaquettes`, par `outils/images.py livingdex`) | Images © Nintendo / Creatures Inc. / GAME FREAK Inc. ; usage de fan, non commercial |

Pokémon et tous les noms et images associés sont des marques et créations de Nintendo, Creatures Inc., GAME FREAK Inc. et
The Pokémon Company. Monsuta Potaru est un projet de fan privé, sans lien avec eux.
