# Monsuta Potaru

Banque Pokémon privée pour un cercle d'amis, basée sur [PKHeX.Core](https://github.com/kwsch/PKHeX). Anciennement
« PokeBank » (renommé pour s'éloigner de Pokémon Bank, le service de Nintendo) : le homebrew 3DS garde pour l'instant
son nom, **PokeBank Sync**, et les clés de connexion leur nom technique « PokeBank » (`SetApplicationName` dans
`Program.cs` : le changer déconnecterait tout le monde).

- **Stack** : ASP.NET Core 10 + Blazor Server (tout en C#, pas de JavaScript à écrire), SQLite, ASP.NET Identity.
- **Jeux testés** (vraies sauvegardes, `RealSavesTests` : voir plus bas) : Rouge, Bleu, Argent, Rouge Feu, Vert Feuille,
  SoulSilver, Noire, Noire 2, Rubis Oméga, Ultra-Soleil, Épée ; Platine par la sauvegarde d'exemple. Pas encore de vraie
  sauvegarde de la 9e génération (Écarlate / Violet), ni de Let's Go, Diamant Étincelant ou Légendes Arceus : PKHeX sait
  les lire, à valider au cas par cas.

## Lancer en local

Les images (Pokémon, objets, sprites Donjon Mystère, ~90 Mo) ne sont pas dans le dépôt Git : elles sont stockées sur le
serveur. Après avoir cloné, les récupérer par SSH (une archive, quelques secondes) :

```bash
outils\recuperer-images.cmd debian@IP_DU_VPS
```

Sans accès au serveur, `python outils/images.py` les reconstruit depuis leurs projets d'origine (~10 minutes). Sans images,
le site marche mais affiche des points d'interrogation, et les tests d'images échouent avec un message qui l'explique.

Les combats font tourner le simulateur de Pokémon Showdown avec Node.js (22 ou plus) : l'installer une fois après avoir
cloné (sans lui, le site marche mais les combats et leurs tests échouent avec un message qui l'explique) :

```bash
npm ci --prefix showdown
```

Sous PowerShell, si l'exécution des scripts est bloquée (« npm.ps1 ne peut pas être chargé »), appeler `npm.cmd` à la
place (`npm.cmd ci --prefix showdown`) : inutile de changer la politique d'exécution de Windows.

```bash
dotnet run --project MonsutaPotaru
```

Puis ouvrir http://localhost:5265. En développement, le code d'invitation de départ est `pokeamis`
(voir `MonsutaPotaru/appsettings.Development.json` ; ensuite, page « Administration »). La base SQLite est créée automatiquement dans `MonsutaPotaru/Data/app.db`.

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
randomisée : ses Pokémon sont signalés illégaux), Rouge Feu, Vert Feuille (2 parties de 3e génération, pour les combats ; PKHeX les reconnaît toutes deux comme Rouge Feu), SoulSilver, Noire (`noir.sav`), Noire 2, Rubis Oméga, Ultra-Soleil, Épée. SoulSilver : les blocs annexes
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

### Comptes

`Services/AccountService.cs`, pages dans `Components/Account` (ASP.NET Identity, tout en français) :

- **Inscription** : pseudo, adresse mail, mot de passe et **code d'invitation** (obligatoire, gardé en base : voir
  « Administration » ; majuscules, tirets et espaces ne comptent pas). Rôle **Standard**. Mot de passe d'**au moins 12
  caractères** (`AccountService.PasswordMinLength`), avec une majuscule, une minuscule, un chiffre et un caractère
  spécial ; un compte plus ancien garde son mot de passe plus court jusqu'à ce qu'il le change. **10 envois du formulaire par
  heure et par adresse IP** au plus (`AccountLimiter`) : sans mail de confirmation, une adresse déjà inscrite est
  forcément refusée, la limite empêche d'en essayer beaucoup (et de chercher le code d'invitation).
  Le **pseudo** (3 à 20 caractères : lettres, accents compris, chiffres, espaces, tirets, points, tirets bas ; espaces en
  trop retirés) est unique sans tenir compte des majuscules et **définitif**. C'est le nom d'utilisateur d'Identity : le
  menu (lien vers « Mon compte ») et la 3DS (`compte` de l'API) l'affichent ; l'adresse mail n'est montrée à personne.
- **Connexion** par l'**adresse mail** (unique, `RequireUniqueEmail`) et le mot de passe, pas par le pseudo. Pas de
  double authentification (`SignInAsync` direct, même si elle était restée active). Contre les essais de mots de passe :
  - **5 mauvais mots de passe d'affilée bloquent la connexion au compte 5 minutes**, même avec le bon (verrouillage
    d'Identity, `AccountService.FindForLoginAsync`) ; le blocage se lève tout seul, un bon mot de passe remet le
    compteur à zéro ;
  - **10 envois du formulaire par minute et par adresse IP** au plus (celle transmise par Caddy, `AccountLimiter`) :
    contre les essais sur beaucoup de comptes à la fois ; au-delà, « Trop d'essais de connexion : attends une minute » ;
  - les mauvais mots de passe tapés dans « Mon compte » (Adresse mail, Mot de passe, Supprimer le compte :
    `AccountService.CheckPasswordAsync`) **comptent avec ceux de la connexion** : une session volée ne permet pas de
    deviner le mot de passe. Bloqué, ces actions sont refusées (« Trop de mauvais mots de passe : réessaie dans 5
    minutes » : la personne est connectée, c'est son compte) ; la session ouverte, elle, reste ouverte ;
  - un seul message pour tout échec (« Adresse mail ou mot de passe incorrect. Après 5 échecs, la connexion à ce compte
    est bloquée 5 minutes. ») : la page ne dit pas si l'adresse a un compte, ni s'il est bloqué. Le mot de passe est
    vérifié même pour une adresse inconnue, pour que la durée de la réponse ne le dise pas non plus.
- **Comptes d'avant le pseudo** : leur nom d'utilisateur est encore leur adresse (« @ », interdit dans un pseudo :
  `AccountService.HasPseudo`). `Shared/PseudoGuard.razor` (autour du contenu de `MainLayout`) les renvoie sur
  « Choisis ton pseudo » (`/Account/Pseudo`) tant qu'ils n'en ont pas ; la 3DS continue de marcher.
- **Mon compte** : Profil (pseudo et adresse, en lecture), Adresse mail (changée **directement** avec le mot de passe :
  le site n'envoie aucun mail), Mot de passe, Données personnelles (téléchargement en JSON : identifiant, pseudo, adresse ;
  suppression du compte et de tout ce qu'il contient).
- **Retirés** : téléphone, double authentification, connexions externes, mot de passe oublié et confirmation par mail
  (rien n'était envoyé). Migration `RemovePhoneAndTwoFactor` : numéros effacés, double authentification coupée, clés et
  codes de secours supprimés. Un mot de passe oublié passe par l'administrateur.
- Messages d'Identity en français : `FrenchIdentityErrorDescriber` (même fichier).

### Multijoueurs

Menu « Multijoueurs » → **Rechercher un joueur** (`/joueurs`, `Pages/PlayerSearch.razor`) : une partie du pseudo (2
caractères au moins, majuscules et accents ignorés), parmi les profils trouvables (« Tout le monde » et « Amis seulement »). Profil d'un joueur : `/joueurs/{pseudo}`
(`Pages/PlayerProfilePage.razor`, `Shared/PlayerHeader.razor`) : un onglet « Profil » (son accueil, avec « Demander en ami »),
puis un onglet par page visible. Les pages Collection, Parc,
Inventaire et Living dex ont une seconde adresse (`/joueurs/{pseudo}/collection`...) qui les affiche pour ce joueur en
**lecture seule** : aucun bouton qui modifie, copie ou télécharge (et les actions refusent un visiteur), ni journal de
l'inventaire, ni noms de fichiers, ni ID de dresseur (`PokemonCard.HideTrainerId`) ; le porte-monnaie reste visible.
« Mon compte » → **Visibilité** (colonnes `Audience` et `PublicPages`, migrations `ProfileVisibility` puis `Friends`) :
« Qui peut voir mes pages ? », trois boutons radio, et les pages cochées une à une (les quatre par défaut) :
- **Tout le monde** (défaut) : trouvable, pages cochées visibles de tous ;
- **Amis seulement** : trouvable (on peut lui demander d'être ami), pages cochées visibles de ses amis seulement ; un
  autre joueur ne voit que son pseudo et « Demander en ami » ;
- **Personne** : introuvable, aucune page visible, même de ses amis ; ami par son code seulement (l'ancien profil
  privé devient « Personne »).

Un profil introuvable ou une page non visible répond comme un joueur inexistant, pour tous, administrateurs compris
(`Services/PlayerService.cs`) ; le joueur voit ses propres pages comme un ami.

**Amis** (Multijoueurs → Amis, `/amis`, `Pages/Friends.razor`, `Services/FriendService.cs`) : compteur rouge des demandes
reçues dans le menu (sur « Multijoueurs » replié aussi). Onglet **Amis** : la liste, cherchable par pseudo, avec un lien
vers chaque profil et « Retirer » (des deux côtés). Onglet **Demandes** (`?onglet=demandes`) : mon **code ami**
(`ABCD-EFGH`, permanent, régénérable : l'ancien ne marche plus ; colonne `FriendCode`), la saisie du code d'un autre
joueur (majuscules, tirets et espaces ignorés), les demandes reçues (accepter, refuser) et envoyées (annuler). Une
demande se fait aussi depuis le profil d'un joueur trouvable (« Demander en ami ») ; le code marche même pour un profil
« Personne ». Une demande qui croise celle de l'autre est acceptée d'emblée. Tables `FriendRequests` (une par sens et
par paire) et `Friendships` (deux lignes par amitié) ; supprimées avec le compte.

### Combats

Combats entre amis, génération par génération : la **3e** d'abord (Rubis, Saphir, Émeraude, Rouge Feu, Vert Feuille),
combat simple, équipes de 6, niveaux réels, sans clause (comme un combat de câble), Pokémon illégaux acceptés mais
signalés. Égalité (derniers Pokémon K.O. au même tour) : **match nul**, comme en 3e génération.

**Moteur** : le simulateur de [Pokémon Showdown](https://github.com/smogon/pokemon-showdown) (paquet `@pkmn/sim`, dossier
`showdown/`), toutes les capacités, talents et objets de la génération exacts. Un processus Node.js par combat
(`showdown/combat.js`, lancé par `Services/Battle/ShowdownBattle.cs`), qui reçoit les Pokémon par numéros (traduits en
noms de Showdown ; avec leurs **PP Plus** réels, Showdown en supposerait 3) et les choix des joueurs, et renvoie le
protocole de Showdown. Le pont **ne transmet que les choix** (« p1 move 2 », « p2 switch 3 ») et la fin forcée
(abandon, temps écoulé) : jamais `eval`, que le simulateur accepte aussi. Le hasard (graine) est tiré par le serveur.
Deoxys a la forme du jeu de sa sauvegarde (Rouge Feu : Attaque, Émeraude : Vitesse...).

**Déroulé** : Multijoueurs → **Combats** (`/combats`, `Pages/Battles.razor`), avec un ami, **en direct** (les deux
connectés), comme les échanges : l'ami, puis une paire de sauvegardes stockées de même génération parmi celles qui ont
leurs combats (`BattleService.SavePairsAsync`, amitié revérifiée), demande valable 10 minutes (compteur rouge dans le
menu), un combat à la fois. Chacun choisit ensuite **6 Pokémon** de sa sauvegarde (tous s'il en a moins ; équipe et
boîtes, sans les œufs), dans l'ordre (le premier entre en combat) ; chaque Pokémon garde sa place et son empreinte,
revérifiées au départ (`BattleService.TeamAsync`). Les Pokémon **illégaux** sont marqués au choix, et l'adversaire en est
averti au début du combat. Les sauvegardes ne sont jamais modifiées (combat amical).

Le combat (`Services/Battle/BattleHub.cs`, singleton, en mémoire) : chaque choix est **vérifié par le serveur** sur la
dernière requête de Showdown avant d'être transmis (capacité qui existe et n'est pas bloquée, Pokémon qui n'est ni au
combat ni K.O., pas de rappel d'un Pokémon piégé) ; un choix que Showdown refuse quand même (interdiction cachée) est
signalé et se refait. **60 secondes** pour chaque choix (remplaçant après un K.O. compris), comptées une fois les étapes
du tour jouées : passé ce délai, le joueur **perd** (`BattleHub.Tick`, chaque seconde). **Fuite** (avec confirmation),
quitter la page ou fermer l'onglet : défaite. Un simulateur arrêté par erreur annule le combat, sans vainqueur.

**Interface** façon Pokémon Platine, en CSS (`app.css`, « Combats ») : écran du haut (terrain, Pokémon, boîtes de PV avec
sexe, niveau, statut, Pokéballs de l'équipe adverse, PV exacts des siens, boîte de texte) ; écran du bas (ATTAQUE, SAC
grisé comme en combat de câble, POKéMON, FUITE ; capacités colorées par type avec leurs PP ; équipe avec PV et statut ;
compte à rebours). Les étapes défilent une à une (`BattleTiming` : 1,3 s par message) ; le Pokémon qui attaque
s'avance puis revient, celui qui est touché clignote, un K.O. tombe.

**Sprites** (`Services/Battle/BattleSprites.cs`) : chaque joueur voit le combat avec les sprites de **son** jeu (celui de
sa sauvegarde), ceux de l'adversaire de face, les siens **de dos**, chromatiques compris : Rubis / Saphir, Émeraude (dos de
Rubis / Saphir), Rouge Feu / Vert Feuille. Ce sont les dossiers des living dex (`images/livingdex/<jeu>/`), complétés par
`dos/`, `chromatique/` et `dos/chromatique/` (`outils/images.py combats`). Zarbi garde sa lettre (forme d'aspect
transmise à Showdown), Morphéo change selon le temps, Deoxys a la forme du jeu **de celui qui regarde** (seule image de
son jeu ; ses statistiques restent celles du jeu de son dresseur). Un sprite absent : l'image de la collection.

**Résultats** : chaque combat terminé (victoire, défaite, match nul ; abandon et temps écoulé compris, pas un combat
annulé) est écrit dans la table `BattleRecords` (`Services/Battle/BattleRecorder.cs`, à l'événement
`BattleHub.Finished` ; migration `BattleRecords`), sans clé étrangère, comme les échanges : victoires, défaites, matchs
nuls et adversaires différents dans les **Statistiques**.

**Traduction** (`Services/Battle/BattleView.cs`) : chaque joueur voit le combat de son côté, en français, comme dans
Pokémon Platine (« Pika utilise Surf ! », « Abo ennemi est K.O. ! », « L'Attaque d'Abo ennemi baisse ! »), PV exacts pour
ses Pokémon, en pourcentage pour ceux de l'adversaire ; noms français de PKHeX retrouvés depuis les noms anglais de
Showdown (`BattleNames`). Vérifié sur des centaines de combats aléatoires (toute la 3e génération) : aucune ligne du
protocole sans traduction (test `BattleTests.RandomBattles_EndAndAreFullyTranslated`).

### Échanges

Multijoueurs → **Échanges** (`/echanges`, `Pages/Trades.razor`), avec un ami, **en direct** (les deux connectés), entre
deux sauvegardes stockées du **même contexte de jeu** de PKHeX (même génération : Noire ↔ Noire 2, Platine ↔ HeartGold ;
pas Let's Go ↔ Soleil, ni Épée ↔ Diamant Étincelant). Compteur rouge des demandes reçues dans le menu.

1. **Demande** : l'ami, puis la paire de sauvegardes (avec la date de la version stockée et un rappel si elle a été
   modifiée par le site sans être téléchargée : l'échange porte sur la version stockée). Le serveur revérifie l'amitié
   avant de montrer les sauvegardes de l'autre (`TradeService.SavePairsAsync`) : rien pour un autre compte. Elle vaut 10 minutes, tant que
   son auteur reste sur la page ; l'ami l'accepte ou la refuse. Un joueur ne fait qu'un échange à la fois.
2. **Choix**, en direct sur les deux pages : chacun choisit, parmi les Pokémon de l'autre (équipe et boîtes, hors cases
   verrouillées), **celui qu'il veut recevoir** ; seuls ceux que sa sauvegarde peut recevoir sont proposés
   (`SaveFile.IsCompatiblePKM` après conversion de format). Les œufs aussi (espèce cachée).
3. **Validation** : « tu donnes X, tu reçois Y », par les deux ; un refus ramène au choix.
4. **Échange** (`Services/TradeService.cs`) : chaque Pokémon prend la place de celui qui part, au format de l'autre
   jeu, avec le joueur comme dresseur actuel et dans son Pokédex (`EntityImportSettings.All`) ; **évolution par échange**
   selon l'arbre des évolutions du jeu (PKHeX) : simple (Kadabra), objet tenu consommé (Onix + Peau Métal), Carabing ↔
   Escargaume entre eux ; ni œuf, ni Pierre Stase. Refusé si un Pokémon a changé depuis le choix (empreinte), ou si une
   équipe ne garderait que des œufs. Les deux sauvegardes sont écrites dans une même transaction (copies de sécurité
   `echange-avant` / `echange-apres`), leurs Pokémon relus dans la collection, et marquées **modifiées sur le serveur**
   (`SaveModification.PokemonTraded`, migration `Trades`) : à télécharger avant de les mettre à jour, comme après un retrait.

**Annuler l'échange** ramène à l'accueil ; **quitter la page** pendant un échange demande confirmation
(`NavigationLock`), puis l'annule ; l'autre joueur en est prévenu. Le déroulé est en mémoire (`Services/TradeHub.cs`,
singleton) : un redémarrage du serveur annule les échanges en cours.

**Statistiques** (`/statistiques`, `Pages/Statistics.razor`) : nombre d'échanges réussis et de partenaires différents
(table `TradeRecords`, et pour les combats victoires, défaites, matchs nuls et adversaires différents, table `BattleRecords`, sans clé étrangère : les statistiques d'un joueur survivent à la suppression du compte de son
partenaire), base des futurs succès. Le **pseudo** du menu est un menu repliable : « Paramètres du profil » (Mon compte)
et « Statistiques ».

### Thèmes

« Mon compte » → **Thèmes** (`Account/Pages/Manage/Themes.razor`) : une carte par thème, avec une miniature dessinée en
CSS ; un clic l'applique (la page est rechargée). Le choix est gardé dans le compte (colonne `Theme`, migration
`UserTheme` ; null = défaut), donc sur tous les appareils. Liste et défaut : `Services/SiteThemes.cs`. Le serveur pose la
clé du thème du compte sur la page dès le premier rendu (`<html data-theme="pokedex">`, `App.razor`).

**Thème par défaut : Ardoise** (`SiteThemes.Default`), pour les visiteurs sans compte et les comptes qui n'ont rien
choisi. Le défaut n'est jamais enregistré (colonne à null) : un compte qui n'a rien choisi suit un changement de défaut.
Conséquence au passage de Classique à Ardoise : les comptes restés sur Classique (enregistré à null, puisque c'était le
défaut) sont passés en Ardoise ; Classique, choisi de nouveau, est maintenant enregistré.

- **Classique** : le thème d'origine, qui n'a aucun style à lui.

- **Pokédex** (`wwwroot/themes/pokedex.css`, tout sous `[data-theme="pokedex"]`) : le Pokédex rouge, **sobre** (un
  premier essai inspiré du sac de Rubis Oméga / Saphir Alpha a été jugé trop chargé). Menu latéral comme le boîtier
  (rouge adouci `#c2323d` : celui du Pokédex était trop vif ; charnière plus sombre, lentille bleue et trois voyants devant
  le nom du site, lien actif en petit écran clair ; barre du haut de la page dans la même teinte),
  contenus dans des « écrans » gris-bleu au cadre ardoise (cartes, tableaux, boîtes du Transfert, grilles de Pokémon,
  cartes des living dex), dialogues et fiches de Pokémon dans un cadre rouge ; le rouge pour les accents (bouton
  principal, onglet choisi, objet ou Pokémon choisi, barres de progression), liens en bleu lentille, fond blanc. **Tout
  le site** : le menu, la barre du haut et le contenu des pages (`main`, dialogues compris). Le parc garde son décor.
  Les styles du menu sont isolés par composant (`NavMenu.razor.css`, chargés après) : ses sélecteurs commencent par
  `html` pour passer devant.
- **Ardoise** (`wwwroot/themes/ardoise.css`) : sobre, comme un outil de gestion. Le seul thème qui a **son propre cadre**
  (`Components/Layout/ArdoiseLayout.razor`, choisi par `MainLayout` d'après la clé du thème, transmise par `App.razor` →
  `Routes.razor` en valeur en cascade `SiteTheme`) :
  - menu latéral ardoise (`#232d3b`), lien de la page affichée marqué d'orange (`#e8751a`) ; rubriques Sauvegardes et
    Multijoueurs ouvertes d'un clic, et d'elles-mêmes sur l'une de leurs pages ;
  - bouton « Replier le menu » : colonne d'icônes (nom en bulle), pages d'une rubrique affichées à côté au survol.
    Choix gardé dans le cookie `menu=replie`, que `App.razor` relit pour poser `data-menu="replie"` sur la page dès le
    premier rendu (pas de menu qui se replie sous les yeux) ;
  - barre du haut blanche : fil d'Ariane (`Components/Layout/Breadcrumbs.cs`, le chemin de la page dans le menu, déduit
    de son adresse : « Multijoueurs › Rechercher un joueur › Sacha › Parc »), porte-monnaie (lien vers « Inventaire »),
    menu du compte en haut à droite (pastille à l'initiale du pseudo : paramètres du profil, statistiques,
    déconnexion) ; sans compte, « Inscription » et « Connexion » ;
  - téléphone : le menu s'ouvre par-dessus la page (bouton de la barre du haut), le fil d'Ariane ne garde que la page
    affichée ;
  - tout marche sans interactivité (pages du compte en rendu statique) : clics gérés par `wwwroot/js/app.js`, menu du
    compte en élément `details`.

  Contenu des pages (sous `[data-theme="ardoise"]`) : fond gris clair, cartes et tableaux en blocs blancs à bordure
  fine, onglets soulignés d'orange, bouton principal orange, autres boutons gris, grilles de Pokémon gris clair à la
  place du bleu ciel ; dialogues et fiches de Pokémon surélevés, liseré orange. Le parc garde son décor. Compteurs du
  menu (porte-monnaie, demandes d'ami, d'échange et de combat) : `Components/Layout/SiteMenuBase.cs`, communs aux deux
  menus.

  **En-têtes de page et blocs** (tous les thèmes) : chaque page commence par `Shared/PageHeader.razor` (titre h1, nombre
  en pastille, description, boutons d'action à droite, icône du menu ; lien de retour), ses parties titrées sont des
  `Shared/Block.razor` (titre h2, actions à droite ; `Flush` : liste collée aux bords). Classique et Pokédex : sans cadre,
  comme les titres d'avant (styles de base dans `app.css`). Ardoise : l'en-tête est un bandeau blanc avec l'icône de la
  page dans un carré orange (sur les pages d'un autre joueur, son en-tête `PlayerHeader` porte le bandeau et les onglets,
  le titre de la page dessous reste simple) ; les blocs sont des panneaux blancs à en-tête. Une nouvelle page : un
  `PageHeader` en tête, des `Block` pour ses parties.
- `app.css` fixe certaines couleurs en dur (`.btn-primary`, `.btn-link`, anneau bleu du focus) : un thème doit les
  écraser explicitement, pas seulement changer les variables de Bootstrap.

### Sécurité

- **Connexion et inscription** : voir « Comptes » (blocage après 5 échecs, limites par adresse IP).
- **En-têtes de toutes les réponses** (`SecurityHeaders.cs`, branché au début de `Program.cs`, fichiers statiques et API
  compris) : `X-Content-Type-Options: nosniff`, `Referrer-Policy: same-origin`, et une **politique de contenu (CSP)
  stricte** qui n'accepte que le site : scripts, images (plus les icônes `data:` du CSS), connexions (dont le WebSocket de
  Blazor), formulaires ; ni plugin, ni page du site dans le cadre d'un autre site. Les styles écrits dans la page restent
  permis (Blazor en écrit). La CSP que Blazor ajoute de lui-même est retirée (`ContentSecurityFrameAncestorsPolicy`) : la
  nôtre la contient. En `Development` seulement, les scripts de rechargement de Visual Studio (`localhost:*`) sont permis.
- **Aucun script écrit dans la page** : la CSP ne l'exécuterait pas. Les scripts du site sont dans `wwwroot/js` :
  `app.js` (chargé dans le `<head>` d'`App.razor` : images de remplacement, téléchargement, largeur d'écran, grille de
  l'inventaire, glisser-déposer, fermeture du menu sur téléphone) et `parc.js`. Pas de `onerror` / `onclick` dans le
  HTML : une image absente prend `data-remplacement="images/…/inconnu.png"` (ou `data-cacher-si-absente`), un seul
  écouteur d'`app.js` s'en charge. Pas d'`<ImportMap />` (script écrit dans la page) : `parc.js` est chargé par son
  chemin. Test `SecurityHeadersTests.Pages_HaveNoInlineScript`.
- **Fichiers envoyés** : 8 Mo au plus depuis le navigateur (« Mes sauvegardes », « Transfert » :
  `StoredSaveService.ReadUploadAsync`), 4 Mo depuis la 3DS (`SyncEndpoints.MaxSaveBytes`) ; les sauvegardes Pokémon
  font au plus quelques Mo.
- **Ce que le navigateur envoie peut être trafiqué** (valeurs d'événements, arguments des méthodes appelées par
  JavaScript) : les services revérifient le compte, l'amitié (`TradeService.SavePairsAsync`) et la visibilité
  (`ParkService.GetMemberAsync`, `PlayerService.FindAsync`).

### Administration

Rôles Identity **exclusifs** (`Data/SiteSetting.cs`, `SiteRoles`, créés avec la base, identifiants fixes) : **Standard**
pour tous (donné à l'inscription, et aux comptes existants par la migration `RolesAndInviteCode`), **Administrateur** pour
ceux qui gèrent le site, avec tout ce que peut un Standard en plus. Page « Administration » (`/administration`,
`Components/Pages/Admin.razor`, lien du menu pour les seuls administrateurs ; un autre compte y lit « Accès refusé ») :

- **Code d'invitation** : affiché, et **régénéré** au hasard (`ABCD-EFGH`, sans caractères ambigus, après confirmation) ;
  l'ancien ne marche plus aussitôt. Gardé en base (table `SiteSettings`) : au premier démarrage, il reprend
  `Bank:InviteCode` (`INVITE_CODE` du `.env`) ou est tiré au hasard ; ensuite la configuration n'est plus lue.
- **Comptes** : pseudo et rôle (jamais l'adresse ; « Pseudo pas encore choisi » pour un ancien compte), bouton pour
  donner ou retirer le rôle administrateur. On ne se le retire pas soi-même, et il en reste toujours au moins un
  (vérifié dans la requête même du retrait). Un changement de rôle renouvelle le tampon de sécurité du compte : il est
  déconnecté à la prochaine vérification (30 minutes au plus) et retrouve ses droits en se reconnectant. Chaque action
  revérifie le rôle en base (`AdminService`) : le cookie peut garder un rôle retiré d'ici là.
- **Premier administrateur** : le compte du pseudo `Bank:AdminPseudo` (`ADMIN_PSEUDO` du `.env`), **tant que le site n'en
  a aucun** : au démarrage s'il existe déjà, sinon à son inscription ou quand il choisit son pseudo. Une fois qu'il y a
  un administrateur, le réglage est ignoré (un rôle retiré ne revient pas au redémarrage).
- L'administrateur ne change pas les mots de passe (pas encore d'outil pour un mot de passe oublié).

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
  ci-dessus. Un fichier identique à la version stockée est ignoré (« déjà à jour » ; en Game Boy, la même mémoire du jeu suffit et le
  fichier du site est gardé) ; sous un autre nom (la console a reconnu la
  version exacte du jeu), seul le nom de la sauvegarde stockée change. Rien n'est jamais forcé : moins de
  temps de jeu, partie modifiée sur le serveur et pas encore téléchargée, partie d'un autre compte, collection pleine sont
  refusés avec le message du site, que la console affiche. Le nom de fichier envoyé est nettoyé (sans dossier, 100
  caractères au plus). L'emplacement du fichier sur la console (`cle`) est noté à chaque envoi réussi (table
  `SyncSaveLocations`, une sauvegarde stockée par fichier et par console ; supprimé avec la console ou la sauvegarde).
- **Récupération** (« Récupérer » sur la console) : la console liste les sauvegardes stockées du compte (empreinte du
  fichier actuel, temps de jeu, modifiée sur le serveur, console d'origine et jeu précis pour y trouver une ROM, emplacements
  connus sur cette console). Avant de remplacer sa sauvegarde, elle la fait **comparer** par le site (PKHeX) :
  refusé si ce n'est pas la même partie (autre dresseur) ou si celle de la console a **plus de temps de jeu** (sa
  progression serait perdue) ; identique (en Game Boy : même mémoire du jeu, les 32 premiers Ko, une sauvegarde d'émulateur ayant quelques octets
  de plus, `StoredSaveService.SameGameBoyMemory`) : « à jour » ; même temps de jeu mais contenu différent (retrait d'argent,
  transfert d'objets du site) ou moins de temps de jeu : récupérable. Le téléchargement par la console vaut
  « Télécharger » (lève le blocage de « Mettre à jour ») et note l'emplacement où elle l'écrit. Cas sans issue pour
  l'instant : la console a joué **après** une modification du site sans la récupérer (envoi refusé, récupération aussi).

API (JSON, messages en français prêts à afficher ; `MonsutaPotaru/Api/SyncEndpoints.cs`) :

| Requête | Réponse |
|---|---|
| `POST /api/sync/liaison` `{"code", "nom"}` | `{"jeton", "compte", "message"}` ; 400 si le code est faux ou expiré, 429 au-delà de 10 essais par minute depuis la même adresse IP (celle transmise par Caddy) |
| `GET /api/sync/compte` | `{"compte", "console"}` : vérifie le jeton (`compte` : le pseudo, ou l'adresse d'un compte qui ne l'a pas encore choisi) |
| `POST /api/sync/sauvegardes?fichier=nom.sav&cle=...`, corps = fichier brut (4 Mo au plus) | `{"statut", "raison", "message"}` ; `statut` : `ajoutee`, `mise-a-jour`, `inchangee` ou `refusee` (`raison` : `StoreStatus`) |
| `GET /api/sync/sauvegardes` | `{"sauvegardes": [{"id", "jeu", "dresseur", "idDresseur", "fichier", "empreinte", "tempsDeJeu", "modifieeSurSite", "console", "famille", "cles"}]}` ; `console` : `3ds`, `ds`, `gba`, `gb` ou `autre` ; `famille` en DS : `DP`, `Pt`, `HGSS`, `B`, `W`, `BW`, `B2`, `W2`, `B2W2` ; en Game Boy : `RB` (Rouge, Bleu, Vert), `YW` (Jaune), `GS` (Or, Argent), `C` (Cristal), suivis de `-JP` ou `-KO` (versions japonaise et coréenne, de disposition différente) (`SyncService.DescribeForConsole`) |
| `POST /api/sync/sauvegardes/{id}/comparer`, corps = sauvegarde de la console | `{"verdict", "raison", "modifieeSurSite", "message"}` ; `verdict` : `recuperable`, `a-jour` ou `refusee` ; `raison` d'un refus : `autre-partie` (la console cherche alors une autre place), `plus-avancee`, `illisible`, `absente` |
| `GET /api/sync/sauvegardes/{id}?cle=...` | Fichier brut de la version du site (404 si elle n'existe plus) |

Toutes sauf la liaison demandent `Authorization: Bearer <jeton>` (401 sinon, jeton inconnu ou console retirée).

### Filtres de « Ma collection »

Sous la recherche, des **pastilles** (« Type : Feu, Eau ▾ × ») : un clic ouvre les valeurs à cocher, la
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
`watchGridColumns` dans `wwwroot/js/app.js`). La
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
exact rendu par le serveur passe (empreinte SHA-256 dans `SaveModification` ; en Game Boy, la même mémoire du jeu, les 32
premiers Ko, la Console virtuelle n'ayant pas les octets ajoutés par un émulateur), l'ancienne sauvegarde du jeu est refusée.
Dissuasif, pas infaillible : rien n'oblige le joueur à remplacer réellement sa sauvegarde.

### Parc

Le menu « Parc » (`/parc`) est une boîte PC (fond « Forêt » de Diamant Étincelant, répété en largeur) qui prend toute la
largeur de la page et la hauteur visible (sur un téléphone : les proportions de la boîte), où vivent jusqu'à **30 Pokémon** de la
collection, stockage interne ou sauvegardes stockées, de **toutes les générations** (hors œufs ; `ParkService.Capacity`).
« Choisir les Pokémon » ouvre la collection avec la recherche, les pastilles de filtres de « Ma collection » (génération,
type, forme...) et le compteur. Les espèces dont SpriteCollab n'a pas encore les sprites (64, surtout de 9e génération)
y sont grisées (🔒) et refusées ; elles viendront quand il les aura (relancer `outils/images.py donjon`, puis
`images-serveur`). Pour gérer son parc : l'interrupteur **« Seulement ceux du parc »** de la fenêtre ne montre que les
Pokémon choisis (avec la recherche et les pastilles) ; la liste est figée quand on l'allume, un Pokémon décoché reste affiché
pour pouvoir le recocher. **« Tout retirer »** décoche tout (rien ne change avant « Valider »), et **« Vider le parc »**, à
côté de « Choisir les Pokémon », le vide d'un coup après confirmation (`ParkService.ClearAsync`) : ses Pokémon restent dans
la collection. Ni l'un ni l'autre pour un visiteur. Un clic sur un Pokémon du parc le fait se tourner vers toi et ouvre sa fiche, avec « Retirer du parc ». La fiche n'est
donnée que pour un Pokémon de ce parc (`ParkService.GetMemberAsync`) : le numéro vient du navigateur, et un visiteur ne
doit pas lire ainsi le reste de la collection.
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
de 5e (**Pokémon Noire, Blanche, Noire 2 et Blanche 2**), de 6e (**Pokémon X, Y, Rubis Oméga et Saphir Alpha**) et de
7e (**Pokémon Soleil, Lune, Ultra-Soleil, Ultra-Lune, Let's Go Pikachu et Let's Go Évoli**) et de 8e (**Pokémon Épée,
Bouclier, Diamant Étincelant, Perle Scintillante et Légendes Pokémon : Arceus**) et de 9e (**Pokémon Écarlate et
Violet** ; pas encore Légendes Z-A).

- **Vue générale** (`/livingdex`) : une carte par living dex (jaquette, sauvegarde, barre de progression, « x / y »).
  Boutons **Régional / National / Complet** pour la progression de toutes les cartes (`?mode=regional`, `?mode=complet`) :
  une carte dont le jeu n'a pas ce mode compte en National et l'indique (« National seul », 1re génération) ; un clic
  sur une carte ouvre le living dex dans le mode affiché. Même comptage que sur sa page (`LivingDexService.EntriesAsync`).
  **Tri** à côté (« Trier par » Génération / Complétion, et ↑ / ↓ pour le sens ; `?tri=completion`, `?ordre=desc`,
  `LivingDexGames.Sort`) : par génération, l'ordre des jeux ou son exact inverse ; par complétion, le pourcentage exact
  dans le mode affiché (celui de la carte, le National pour « National seul »), à égalité dans l'ordre des jeux.
- **Barres de progression** (cartes et page d'un living dex, `Shared/LivingDexProgress.razor`) : pourcentage au centre
  de la barre, avec une décimale **arrondie vers le bas** (« 5,6 % » ; 100 % seulement quand tout y est),
  `LivingDexGames.Percent`.
- **Création** : « + Créer un living dex », d'abord le **jeu** (jaquettes groupées par génération, **repliées** au
  départ : le titre de chaque génération, « ▸ 4e génération (5 jeux) », la déplie ou la replie ; repliée, celle du jeu
  choisi le montre encore ; un jeu qui a déjà son living dex est grisé), puis une de ses **sauvegardes** stockées pas encore liées. PKHeX reconnaît Jaune, Cristal,
  Émeraude, Platine, et depuis PKHeX 26 HeartGold et SoulSilver, mais pas Rouge de Bleu, Or d'Argent, Rubis de Saphir,
  Rouge Feu de Vert Feuille ni Diamant de Perle (même format) : une sauvegarde Rouge / Bleu est proposée pour Rouge comme
  pour Bleu, de même pour les autres paires (pour Rouge Feu / Vert Feuille, PKHeX répond toujours « Rouge Feu » faute de mieux). À partir
  de la 5e génération, chaque sauvegarde dit son jeu (Let's Go, Légendes Arceus et Écarlate / Violet compris). **Un living dex par jeu** et par compte, une
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
    Meloetta Danse n'existent qu'en combat ;
  - 6e génération : **Régional** (Kalos de X / Y : trois sous-Pokédex à la suite, **Centre** 153, **Côtes** 153 et
    **Montagnes** 151, 457 Pokémon sans doublon, chacun avec son titre dans la grille et sa numérotation à partir de 1,
    infobulle « Kalos Côtes #001 Baudrive (national #425) » ; Hoenn de Rubis Oméga / Saphir Alpha : 211), **National**
    (721) et **Complet** (825 en X / Y, 847 en Rubis Oméga / Saphir Alpha) : les formes de la 5e (les saisons ne changent
    plus, sans conseil), les 5 fleurs de Flabébé, Floette (sans l'Éternelle, jamais distribuée) et Florges, les 4 tailles
    de Pitrouille et Banshitrouye. **Méga-Évolutions et Primo-Résurgences** (formes de combat) : une case chacune, remplie
    par un exemplaire **de plus** de l'espèce qui tient sa Méga-Gemme (Gemme Rouge / Bleue pour les Primo ; Méga-Rayquaza,
    sans gemme : un Rayquaza de plus qui connaît Draco-Ascension) ; la case de l'espèce en prend un autre (Dracaufeu : 3
    exemplaires) ; infobulle « il faut un Dracaufeu de plus avec Dracaufite X », ou « avec Mysdibulite ». 30 en X / Y,
    50 en Rubis Oméga / Saphir Alpha (le nombre de formes de chaque jeu vient de PKHeX). Rubis Oméga / Saphir Alpha
    seulement : **Hoopa Déchaîné** comme Shaymin Céleste (deuxième Hoopa et Vase Scellé dans le sac) et une case **Pikachu
    Cosplayeur**, remplie par n'importe quel costume (un seul par partie). Une seule case, quelle que soit la forme :
    Prismillon (et Lépidonille, Pérégrain : trop de motifs à rassembler), Couafarel (les coupes ne tiennent qu'en équipe),
    Mistigrix (sa forme est le sexe), Arceus, Genesect ; Exagide Assaut et Xerneas Déchaîné n'existent qu'en combat ;
  - 7e génération : **Régional** (Alola de Soleil / Lune : 302, Brindibou n° 1, Marshadow n° 302 ; d'Ultra-Soleil /
    Ultra-Lune : 403, Zeraora n° 403 ; une seule liste, les Pokédex des îles se recoupent), **National** (802 ; 807 en
    Ultra) et **Complet** (956 en Soleil / Lune, **964** en Ultra : ça tient, de justesse, dans les 960 places des boîtes
    et les 6 de l'équipe) : les formes de la 6e (Méga comprises, les 50 : PKHeX dit que chaque gemme peut se tenir en 7e,
    `ItemStorage7SM.GetAllHeld`), les **18 formes d'Alola** (« Rattata d'Alola »), les 4 styles de Plumeline, Lougaroc
    Diurne, Nocturne et Crépusculaire (Ultra seulement), Zygarde **50 % et 10 %** (avec ou sans Rassemblement : la même
    case), les **7 noyaux de Météno**, Necrozma Crinière du Couchant et Ailes de l'Aurore (Ultra). **Shaymin Céleste et
    Hoopa Déchaîné** tiennent en boîte à partir de la 7e (règles de légalité de PKHeX) : cases ordinaires, sans deuxième
    exemplaire ni objet. Une seule case : **Silvallié** (comme Arceus), Pikachu (les **casquettes** ne comptent pas :
    distributions terminées), Amphinobi ; un **Pokémon Dominant** (Ultra : seule la taille change) remplit la case de sa
    forme ordinaire (Rattatac Dominant : Rattatac d'Alola). Formes de combat exclues : Météno Météore, Ultra-Necrozma,
    Zygarde Parfait, Froussardine en banc, Mimiqui démasqué, Amphinobi Sacha ;
  - **Let's Go Pikachu / Évoli** : seulement les 151 de Kanto, **Meltan et Melmetal** (n° 808 et 809) : son Pokédex est
    tout ce que le jeu peut contenir, donc **National** (153) et **Complet** (186 : les 18 formes d'Alola et 15 Méga ; ni
    casquette, ni Pikachu / Évoli Partenaire). Les Pokémon n'y tiennent **pas d'objet** : une case Méga se remplit avec un
    **exemplaire de plus** de l'espèce **et sa gemme dans le sac** (`LivingDexSlot.MegaInBag` ; Dracaufeu : trois pour les
    deux Méga) ; infobulle « il faut un Dracaufeu de plus et la Dracaufite X dans le sac ». Le Parc GO (Pokémon venus
    de Pokémon GO, hors des boîtes) ne compte pas ;
  - 8e génération : seulement ce que le jeu peut contenir, d'après PKHeX (`PersonalTable.IsPresentInGame` :
    `LivingDexGames.Present`), dans tous les modes ; pas de Méga.
    - **Épée / Bouclier** : **Régional** = Galar (400), puis ce que l'**Isolarmure** (110 de plus) et la **Couronneige**
      (74 de plus) ajoutent, sans doublon, en trois sections avec les numéros du jeu (`RegionalNumbers`) : 584 ;
      **National** : les 664 espèces du jeu ; **Complet** (764) : formes d'Alola et de Galar (« Miaouss de Galar »),
      Motisma, Kyurem, Necrozma, Lougaroc, Zygarde, Pitrouille..., Salarsen Grave, Shifours Mille Poings, Sylveroy du Froid
      et d'Effroi, les **9 crèmes de Charmilly** (« Charmilly (Lait Ruby) » ; n'importe quelle décoration), et **32 cases
      Gigamax** (« Dracaufeu Gigamax ») : le facteur Gigamax n'est pas une forme, sa case se remplit avec un exemplaire
      **de plus** qui l'a (comme une Méga ; « il faut un Dracaufeu de plus avec le facteur Gigamax ») ; une seule pour
      Charmilly et Salarsen (même aspect Gigamax), deux pour Shifours. Exclus : Pikachu à casquette, Zarude Papa et
      Magearna Couleur du Passé (distributions terminées), Théffroi / Polthégeist Authentique (même aspect), les formes de
      combat (Darumacho Transe, Zacian / Zamazenta Suprême, Bekaglaçon, Morpeko, Éthernatos Infinimax...) ;
    - **Diamant Étincelant / Perle Scintillante** : **Régional** (Sinnoh, le même que Diamant / Perle : 151), **National**
      (493) et **Complet** (536) : les formes de Diamant / Perle, Shaymin Céleste en case ordinaire (il tient en boîte à
      partir de la 7e) ; Papilord, une case (sa cape ne se voit pas) ;
    - **Légendes Arceus** : **Régional** (Hisui : 242, Brindibou n° 1), **National** (les mêmes 242, dans l'ordre
      national, comme Johto) et **Complet** (291) : formes de Hisui, Goupix et Feunard d'Alola, Dialga, Palkia et Giratina
      Originels, formes Totémiques (Amovénus compris), Bargantua Blanc, Zarbi, capes, mers, Motisma, Shaymin Céleste. Une
      espèce qui n'existe qu'en forme de Hisui (Caninos, Typhlosion...) a une seule case, à cette forme (« Caninos de
      Hisui », même en National) ; pas les formes Roi (impossibles à capturer) ;
  - 9e génération, **Écarlate / Violet** : comme Épée / Bouclier, seulement ce que le jeu peut contenir.
    **Régional** = Paldea (400), puis ce que **Septentria** (99 de plus) et la **Myrtille** (165 de plus) ajoutent, sans
    doublon, en trois sections avec les numéros du jeu : 664 ; **National** : les 733 espèces du jeu ; **Complet**
    (855, pour 966 places) : formes d'Alola, de Galar, de Hisui et de **Paldea** (Axoloto, les 3 races de Tauros : « Tauros de
    Paldea (race Combative) »), Ursaking Lune Vermeille, **Famignol Famille de Quatre** et **Deusolourdo Triple** (rares,
    mais des formes d'aspect), les 4 plumages de Tapatoès, les 3 formes de Nigirigon, **Mordudor Marche** (venu de
    Pokémon GO par HOME), les 4 masques d'Ogerpon, et les formes des générations précédentes que le jeu connaît
    (Motisma, Bargantua, saisons, fleurs, noyaux de Météno, crèmes de Charmilly...). Ni Méga ni Gigamax. Une seule case :
    Koraidon et Miraidon (leurs autres formes sont celles de la monture, jamais en boîte), Prismillon, Arceus,
    Silvallié. Exclus : Pikachu à casquette, Zarude Papa, Magearna Couleur du Passé, Poltchageist / Théffroyable
    Onéreux (même aspect), formes de sexe, formes de combat (Superdofin Super, Ogerpon et Terapagos Téracristal...).
- **Page d'un living dex** (`/livingdex/{id}`) : jaquette officielle, compteur et barre de progression, filtre
  **Tous / Manquants / Possédés** avec le nombre de chacun (`?filtre=manquants`, `?filtre=possedes`, gardé en changeant
  de mode), grille dans l'ordre du Pokédex avec les **sprites du jeu** (Rouge / Bleu : couleurs du Super Game Boy, Jaune :
  Game Boy Color ; Or, Argent et Cristal ont chacun les leurs ; Rubis / Saphir, Émeraude, Rouge Feu / Vert Feuille,
  Diamant / Perle, Platine et HeartGold / SoulSilver aussi ; ceux, fixes, de Noire / Blanche pour toute la 5e génération :
  PokéAPI n'a pas ceux de Noire 2 / Blanche 2 ; **à partir de la 6e génération, les images de « Ma collection »**, les
  sprites 3D de ces jeux n'ayant pas plu : `SpriteFolder` null, `LivingDexGames.Sprite` → `PokemonSprites.Url`, la Méga
  par sa gemme comme un Pokémon qui la tient, les Gigamax par `PokemonSprites.GigamaxUrl` ; 7e et 8e génération aussi),
  **une image par forme** (Zarbi B, Deoxys Vitesse...), en couleur si le Pokémon est dans la sauvegarde, en **silhouette
  noire** sinon. Infobulle : numéro, nom (« Zarbi B » en Complet, « Deoxys (forme Vitesse) »)
  et place (« Boîte 2, case 8 ») ou « manquant » ; un clic sur un Pokémon possédé ouvre sa fiche. Nombre de colonnes au
  curseur, comme dans « Ma collection » (3 à 20, selon la largeur de la fenêtre par défaut, `?colonnes=12` ;
  `Shared/GridColumns.cs`, commun aux deux pages).
- **Suppression** : « Supprimer ce living dex » (la sauvegarde reste). Supprimer la sauvegarde supprime son living dex
  (suppression en cascade), ce que la confirmation de « Mes sauvegardes » signale.
- **Living dex global** (`Services/GlobalLivingDex.cs`, jeu `LivingDexGame.Global`) : un par compte, choisi en tête de
  « + Créer un living dex », **sans sauvegarde** (`LivingDex.StoredSaveId` null, migration `GlobalLivingDex`) : il compte
  les Pokémon du **stockage interne** (« Transfert »), pas ceux des sauvegardes stockées ni les œufs, et **pas les
  chromatiques** (ignorés même s'ils pourraient remplir une case) ; chaque exemplaire
  ne remplit qu'une case (d'abord les cases qui en demandent un de plus, puis les formes exactes, puis « n'importe quelle
  forme »), dans l'ordre des dépôts. Carte et page : la **Master Ball** (image de l'objet) à la place d'une jaquette
  (`Shared/LivingDexCover.razor`), toujours **en tête** de la vue générale, quel que soit le tri.
  Une forme y est reconnue par son **nom anglais chez PKHeX**, pas par son numéro : Pikachu 1 est Rockeur en 6e
  génération, à casquette Originale ensuite. Trois modes :
  - **National** : les 1 025 espèces ;
  - **Complet** (1 283) : les cases Complet de tous les living dex par jeu réunies (formes régionales, Méga, Gigamax,
    formes d'aspect, avec les mêmes choix : un seul Prismillon, 9 crèmes de Charmilly, pas de casquettes...), sans
    Légendes Z-A ; la forme exacte l'emporte quand un jeu accepte n'importe laquelle et un autre non (Hoopa) ; plus de
    condition de sac (Shaymin Céleste, Méga de Let's Go : la gemme tenue) ;
  - **Ultime** (1 578 : 12 lots de 50 emplacements au-delà des 1 000 de départ) : **toutes les formes** qu'une boîte
    d'un jeu pris en charge peut garder (tables de chaque jeu de PKHeX ; présence dans le jeu à partir de la 8e), même
    celles au même aspect (Pikachu Partenaire, costumes et casquettes, Dominants, Papilord, Lépidonille, Théffroi
    Authentique, Zygarde Rassemblement...), les formes de couleur (20 Prismillon, **63 Charmilly** : crème × décoration,
    lue dans le `FormArgument`), **mâle et femelle à part** quand la femelle est différente (forme de base, liste de
    PokéAPI ; « Pikachu ♀ »), les Méga, Primo, Gigamax (deux pour Salarsen) et les formes de combat dues à un objet tenu
    (Zacian / Zamazenta Suprêmes : Épée / Bouclier Rouillés, Ultra-Necrozma : Ultranécrozélite), chacune avec un
    **exemplaire de plus**. Exclues : les formes de combat sans objet (Darumacho Transe, Exagide, Mimiqui démasqué,
    Zygarde Parfait, Superdofin Super, Téracristal...), les coupes de Couafarel (en équipe seulement, et le stockage
    interne ne reçoit que des Pokémon des boîtes), les formes de monture de Koraidon et Miraidon, Floette Éternelle
    (jamais distribuée), les formes Roi de Légendes Arceus, Arceus « ??? ». **Spinda** n'a qu'une case.
  Vue générale : bouton **Ultime** (`?mode=ultime`), qui ne montre que les living dex qui ont ce mode (les globaux) ; en
  Régional, une carte globale compte en National (« National seul »).
- **Living dex global chromatique** (`LivingDexGame.GlobalShiny`) : le même, un par compte, image le **Charme Chroma**,
  juste après le global ; il ne compte **que les chromatiques** du stockage interne (images chromatiques de la
  collection). Ses cases sont celles du global **sans ce qui n'a jamais existé en chromatique** dans un jeu pris en
  charge (`GlobalLivingDex.ShinyLocked`, d'après la « List of unobtainable Shiny Pokémon » de Bulbapedia, distributions
  et cadeaux de HOME compris) : 25 espèces (Victini, Hoopa, Volcanion — le chromatique de HOME vient de Légendes Z-A et
  ne va ni dans Épée / Bouclier ni dans Écarlate / Violet —, Cosmog, Cosmovum, Magearna, Marshadow, Wushours, Shifours,
  Zarude, Blizzeval, Spectreval, Sylveroy, et Serpente-Eau à Pêchaminus), les costumes et casquettes de Pikachu (sauf la
  Partenaire), Pikachu et Évoli Partenaires, Pichu Troizépi, Amphinobi Lien Naturel, le motif Poké Ball, Ursaking Lune
  Vermeille, Melmetal Gigamax, les Pokémon Dominants. National 1 000, Complet 1 244, Ultime 1 508.

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

« Agrandir » (page « Ma collection ») achète des lots de 50 emplacements pour 10 000 ₽ avec le porte-monnaie ;
l'achat apparaît dans le journal de l'inventaire. Valeurs dans `Services/CollectionCapacity.cs`.

Lors d'un retrait, PKHeX convertit le Pokémon au format du jeu si possible (ex. PK4 → PK5) et refuse ce qui
est incompatible (espèce, attaques ou objets inexistants dans le jeu, conversion vers une génération antérieure).

## Organisation du code

| Fichier | Rôle |
|---|---|
| `MonsutaPotaru/Services/SaveSession.cs` | Sauvegarde chargée + dépôts/retraits en attente (PKHeX) |
| `MonsutaPotaru/Services/AdminService.cs` | Administration : rôles (donner, retirer, garde-fous), liste des comptes, code d'invitation (lecture, régénération), initialisation au démarrage (code, premier administrateur) |
| `MonsutaPotaru/Components/Pages/Admin.razor` | Page « Administration » : code d'invitation, comptes et rôles |
| `MonsutaPotaru/Services/PlayerService.cs` | Multijoueurs : recherche par pseudo, joueur consultable selon sa visibilité et le lien avec le visiteur, visibilité du profil |
| `MonsutaPotaru/Services/FriendService.cs` | Amis : code ami, demandes (par pseudo ou par code), acceptation, refus, annulation, retrait, listes, compteur du menu |
| `MonsutaPotaru/Components/Pages/Friends.razor` | « Multijoueurs » → « Amis » : onglets Amis (recherche, retrait) et Demandes (code ami, reçues, envoyées) |
| `MonsutaPotaru/Data/Friendship.cs` | Tables des amitiés (deux lignes par amitié) et des demandes d'ami |
| `MonsutaPotaru/Services/TradeService.cs` | Échanges : sauvegardes compatibles, Pokémon proposés, évolution par échange, écriture des deux sauvegardes, statistiques |
| `MonsutaPotaru/Services/TradeHub.cs` | Échanges en direct (singleton, en mémoire) : demande, choix, validation, annulation, expiration |
| `showdown/` | Simulateur des combats : `package.json` (`@pkmn/sim`, version fixée), `combat.js` (pont : Pokémon par numéros, PP Plus, choix seulement) |
| `MonsutaPotaru/Components/Pages/Battles.razor` | « Multijoueurs » → « Combats » : demandes, ami et sauvegardes, choix de l'équipe, combat en direct (interface façon Platine) |
| `MonsutaPotaru/Services/Battle/BattleHub.cs` | Combats en direct (singleton, en mémoire) : demande, équipes, simulateur, choix vérifiés, minuteur, abandon, fin |
| `MonsutaPotaru/Services/Battle/BattleService.cs` | Combats : sauvegardes compatibles entre amis, Pokémon proposés pour l'équipe, équipe vérifiée, statistiques |
| `MonsutaPotaru/Services/Battle/BattleSprites.cs` | Sprites d'un combat vus par un joueur : ceux de son jeu, de face et de dos, chromatiques, formes (Zarbi, Morphéo, Deoxys) |
| `MonsutaPotaru/Services/Battle/BattleRecorder.cs` | Résultat de chaque combat terminé écrit en base (statistiques) |
| `MonsutaPotaru/Data/BattleRecord.cs` | Table des combats terminés (vainqueur ou match nul) |
| `MonsutaPotaru/Services/Battle/ShowdownBattle.cs` | Un combat dans le simulateur : processus Node.js, envoi des choix, réception du protocole |
| `MonsutaPotaru/Services/Battle/BattleTeam.cs` | Pokémon d'une sauvegarde → Pokémon du simulateur (format par génération, forme de Deoxys) |
| `MonsutaPotaru/Services/Battle/BattleProtocol.cs` | Lecture du protocole de Showdown : lignes, parties secrètes de chaque joueur, requêtes |
| `MonsutaPotaru/Services/Battle/BattleView.cs` | Combat vu par un joueur : terrain, PV, statuts, météo, étapes à animer, messages en français |
| `MonsutaPotaru/Services/Battle/BattleNames.cs` | Noms anglais de Showdown → noms français de PKHeX (espèces, capacités, objets, talents, types) |
| `MonsutaPotaru/Components/Pages/Trades.razor` | « Multijoueurs » → « Échanges » : demandes, choix de l'ami et des sauvegardes, échange en direct |
| `MonsutaPotaru/Components/Shared/TradePokemon.razor` | Pokémon choisi dans un échange (image, nom, place) |
| `MonsutaPotaru/Components/Pages/Statistics.razor` | Statistiques du joueur (menu du pseudo) : échanges, combats |
| `MonsutaPotaru/Data/TradeRecord.cs` | Table des échanges réussis (statistiques, succès) |
| `MonsutaPotaru/Components/Pages/PlayerSearch.razor` | « Multijoueurs » → « Rechercher un joueur » (`/joueurs`) |
| `MonsutaPotaru/Components/Pages/PlayerProfilePage.razor` | Profil d'un joueur (`/joueurs/{pseudo}`) : ses pages publiques en onglets |
| `MonsutaPotaru/Components/Shared/PlayerHeader.razor`, `PlayerHidden.razor` | En-tête des pages d'un joueur (pseudo, onglets, « lecture seule ») ; réponse d'un profil privé ou introuvable |
| `MonsutaPotaru/Components/Account/Pages/Manage/Visibility.razor` | « Mon compte » → « Visibilité » : profil public ou privé, pages publiques |
| `MonsutaPotaru/Services/SiteThemes.cs` | Thèmes du site (clé, nom, description) et thème par défaut |
| `MonsutaPotaru/Components/Account/Pages/Manage/Themes.razor` | « Mon compte » → « Thèmes » : choix du thème, miniatures en CSS |
| `MonsutaPotaru/wwwroot/themes/pokedex.css` | Thème Pokédex, sous `[data-theme="pokedex"]` : menu, barre du haut, contenu de toutes les pages |
| `MonsutaPotaru/wwwroot/themes/ardoise.css` | Thème Ardoise : son cadre (classes `ardoise-…`, menu replié, téléphone) et le contenu des pages sous `[data-theme="ardoise"]` |
| `MonsutaPotaru/Components/Shared/PageHeader.razor` | En-tête de chaque page : titre, nombre, description, actions, icône (bandeau dans le thème Ardoise) |
| `MonsutaPotaru/Components/Shared/Block.razor` | Partie titrée d'une page, avec ses actions (panneau blanc dans le thème Ardoise) |
| `MonsutaPotaru/Components/Layout/ArdoiseLayout.razor` | Cadre du thème Ardoise (choisi par `MainLayout`) : menu repliable, barre du haut (fil d'Ariane, porte-monnaie, menu du compte) |
| `MonsutaPotaru/Components/Layout/SiteMenuBase.cs` | Commun aux menus (`NavMenu`, `ArdoiseLayout`) : adresse de la page, solde, demandes reçues en direct, rubriques ouvertes |
| `MonsutaPotaru/Components/Layout/Breadcrumbs.cs` | Fil d'Ariane du thème Ardoise, déduit de l'adresse de la page (`BreadcrumbsTests`) |
| `MonsutaPotaru/Data/SiteSetting.cs` | Table des réglages du site (code d'invitation) et rôles des comptes (`SiteRoles`) |
| `MonsutaPotaru/Services/AccountService.cs` | Comptes : règles du pseudo, inscription, connexion par l'adresse (blocage après 5 échecs), choix du pseudo des anciens comptes, changement d'adresse ; réglages d'Identity, messages d'Identity en français |
| `MonsutaPotaru/Services/AccountLimiter.cs` | Limites d'envois par adresse IP : connexion (10 par minute), inscription (10 par heure) |
| `MonsutaPotaru/Components/Account/Pages/` | Pages du compte : connexion, inscription, « Choisis ton pseudo », « Mon compte » (`Manage/` : profil, adresse, mot de passe, données personnelles) |
| `MonsutaPotaru/Components/Account/Shared/PseudoGuard.razor` | Renvoie un compte sans pseudo sur « Choisis ton pseudo » (autour du contenu de `MainLayout`) |
| `MonsutaPotaru/Services/BankService.cs` | Accès base, validation transactionnelle, copies de sécurité |
| `MonsutaPotaru/Services/PokemonFingerprint.cs` | Empreinte d'un Pokémon et recherche de ses doublons dans le compte (vieille copie d'une sauvegarde) |
| `MonsutaPotaru/Services/PokemonInfo.cs` | Noms français (dont « Rouge / Bleu », « Or / Argent », « Diamant / Perle », « Or HeartGold / Argent SoulSilver » pour les groupes de jeux : une sauvegarde de ces jeux ne dit pas lequel des deux ; migrations `GameNames` et `GameGroupNames` pour les noms déjà enregistrés), sprites, légalité |
| `MonsutaPotaru/Components/Pages/Bank.razor` | Page « Transfert et stockage interne » (`/transfert`) : boîtes, glisser-déposer, sélection multiple |
| `MonsutaPotaru/Components/Pages/Park.razor` | Page « Parc » : boîte où vivent jusqu'à 30 Pokémon, fenêtre de choix (« Seulement ceux du parc », « Tout retirer »), « Vider le parc », fiche au clic |
| `MonsutaPotaru/SecurityHeaders.cs` | En-têtes de sécurité de toutes les réponses : nosniff, Referrer-Policy, CSP stricte (plus aucun script écrit dans la page) |
| `MonsutaPotaru/wwwroot/js/app.js` | Scripts du site (chargés dans le `<head>`) : images de remplacement (`data-remplacement`), téléchargement, largeur d'écran, glisser-déposer, menu sur téléphone, cadre du thème Ardoise (repli du menu, rubriques, menu du compte) |
| `MonsutaPotaru/wwwroot/js/parc.js` | Animation et comportement des Pokémon du parc (énergie, intentions, famille, nuit, curiosité) ; `simulate` pour les essais |
| `MonsutaPotaru/Services/ParkService.cs` | Pokémon du parc : liste, choix (30 au plus, avec des sprites Donjon Mystère), retrait, vidage, famille d'évolution |
| `MonsutaPotaru/Data/ParkMember.cs` | Table des Pokémon au parc (supprimée avec le Pokémon) |
| `MonsutaPotaru/Components/Pages/LivingDexes.razor` | Page « Living dex » : cartes des living dex du compte (progression dans le mode choisi), création (jeu, générations repliables, puis sauvegarde) |
| `MonsutaPotaru/Components/Shared/LivingDexProgress.razor` | Barre de progression d'un living dex, pourcentage au centre |
| `MonsutaPotaru/Components/Shared/LivingDexCover.razor` | Image d'un living dex : jaquette du jeu, ou Master Ball / Charme Chroma pour les globaux |
| `MonsutaPotaru/Components/Pages/LivingDexPage.razor` | Page d'un living dex : grille des sprites du jeu (silhouettes des manquants), progression, fiche au clic |
| `MonsutaPotaru/Services/LivingDexService.cs` | Living dex : jeux (`LivingDexGames` : jaquette, sprites, Pokémon de chaque mode, modes cachés), comptage dans la sauvegarde, création, suppression |
| `MonsutaPotaru/Data/LivingDex.cs` | Table des living dex (un par jeu et par compte, supprimé avec sa sauvegarde ; le global n'a pas de sauvegarde) |
| `MonsutaPotaru/Services/GlobalLivingDex.cs` | Living dex global : cases des modes National, Complet et Ultime, attribution des exemplaires du stockage interne |
| `MonsutaPotaru/Components/Pages/Collection.razor` | Toute la collection sur une page (chargée au fil du défilement), filtres en pastilles (`Shared/CollectionFilterChips.razor`), filtre par origine, tri par arrivée (les plus récents d'abord, par défaut), par nom d'espèce ou par numéro de Pokédex (croissants), inversable, gardé dans l'adresse (`?tri=nom&ordre=desc`) ; les œufs toujours à la fin (leur espèce reste cachée), à égalité les plus récents d'abord ; le tri par nom ignore les accents (colonne `SortName`, remplie au démarrage pour les Pokémon plus anciens) ; 5 / 10 / 15 / 20 colonnes par défaut selon la largeur de la fenêtre en pixels physiques (téléphone, jusqu'à 1920, jusqu'à 2560, au-delà ; mise à l'échelle de Windows comprise), recalculées au redimensionnement, modifiables au curseur |
| `MonsutaPotaru/Components/Pages/Saves.razor` | Gestion des sauvegardes stockées (ajout, mise à jour, suppression) |
| `MonsutaPotaru/Components/Pages/Sync3ds.razor` | « Synchronisation 3DS » : consoles reliées (code de liaison, attente de la console, retrait) |
| `MonsutaPotaru/Services/SyncService.cs` | Synchronisation 3DS : codes de liaison, jetons, envoi d'une sauvegarde (ajout, mise à jour ou « déjà à jour »), emplacements sur la console, liste, comparaison et téléchargement pour « Récupérer » |
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
| `MonsutaPotaru/Data/SyncDevice.cs` | Tables des consoles 3DS reliées (empreinte du jeton), des emplacements des sauvegardes sur chaque console et des codes de liaison |
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
| `.env.example` | Modèle des réglages : `DOMAIN`, `INVITE_CODE` (code de départ) et `ADMIN_PSEUDO` (premier administrateur) ; le vrai `.env` est créé sur le serveur |
| `deploy/publier.cmd` (`publier.ps1`) | Depuis Windows : envoie le code sur le VPS et le met à jour |
| `deploy/installer-serveur.sh` | Sur le VPS, une seule fois : pare-feu, Docker, `.env`, sauvegarde nocturne, démarrage |
| `deploy/sauvegarder.sh` | Sauvegarde du volume de données (lancée chaque nuit par cron, gardée 14 jours) |
| `deploy/renommer-serveur.cmd` (`.ps1`, `.sh`) | Une seule fois : passage du serveur de « PokeBank » à « Monsuta Potaru » (voir plus bas) |
| `.github/workflows/ci.yml` | GitHub Actions, CI : tests à chaque push, et analyse SonarCloud (voir « CI/CD ») |
| `.github/workflows/cd.yml` | GitHub Actions, CD : déploiement par un bouton, après lecture du résultat de la CI (voir « CI/CD ») |

**Première installation**

1. Depuis Windows, dans le dossier du projet : `deploy\publier.cmd ubuntu@IP_DU_VPS`
2. Sur le VPS (`ssh ubuntu@IP_DU_VPS`) : `bash ~/monsuta-potaru/deploy/installer-serveur.sh`
   (demande le nom de domaine et le pseudo de ton compte, futur administrateur ; génère le code d'invitation de départ et
   l'affiche à la fin). Inscris-toi ensuite avec ce pseudo : le compte devient administrateur.
3. Depuis Windows : `deploy\images-serveur.cmd ubuntu@IP_DU_VPS` (images, voir plus bas).

**Mises à jour** : par GitHub (« CI/CD » ci-dessous), ou `deploy\publier.cmd ubuntu@IP_DU_VPS` depuis Windows. Les
migrations de la base s'appliquent au démarrage ; les données restent dans le volume Docker.

**Serveur installé avant les rôles** (une seule fois) : ajouter `ADMIN_PSEUDO=<ton pseudo>` au `.env` du serveur
(`~/monsuta-potaru/.env`) avant le déploiement. Si ton compte n'a pas encore choisi son pseudo, il devient administrateur
quand il le choisit.

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

**CI/CD** (GitHub Actions, deux workflows séparés : `.github/workflows/ci.yml` et `cd.yml`, l'un se lance sans l'autre) :

- **CI, tests** à chaque push (ou « Run workflow » du workflow « CI » pour les relancer), sur une machine Ubuntu propre (un fichier oublié dans un commit s'y voit) : .NET 10, simulateur de combat (`npm ci --prefix showdown`), images
  copiées depuis `~/monsuta-potaru-images` du VPS par SSH (exigées par les tests d'images), puis `dotnet test`. Résultat dans
  l'onglet « Actions » du dépôt et à côté de chaque commit. Attention : Linux distingue majuscules et minuscules dans les
  noms de fichiers, Windows non.
- **Qualité du code** : dans le même job, [SonarCloud](https://sonarcloud.io) (organisation `ghalhyus`, projet
  `Monsuta-Potaru`, clé `ghalhyus_monsuta-potaru`) analyse le code pendant la compilation des tests (`dotnet-sonarscanner` : début avant les tests,
  envoi après), avec la **couverture** des tests (coverlet, format OpenCover). Exclus : `Data/Migrations` (généré),
  `wwwroot/lib` (Bootstrap) et `wwwroot/images` (copiées du VPS) ; pas de couverture comptée pour `outils`, `deploy` et
  `wwwroot`. **Pour information seulement** : un échec de l'analyse (jeton absent, SonarCloud injoignable, Quality Gate
  non passée) est signalé en avertissement, la CI reste verte si les tests passent, et le déploiement n'en dépend pas.
  Dans SonarCloud, l'**analyse automatique** doit rester désactivée (Administration → Analysis Method) : sinon l'envoi
  de la CI est refusé.
- **CD, déploiement** : onglet « Actions » → « CD » → « Run workflow » sur `main`. Il **ne relance pas les tests** : il lit
  le résultat de la CI déjà passée sur ce commit (API de GitHub, quelques secondes) et refuse de déployer si elle a
  échoué, n'a pas fini ou n'a jamais tourné, avec le motif en rouge ; la case **« Forcer »** du bouton déploie quand
  même (avertissement orange). Puis :
  sauvegarde des données (`sauvegarder.sh`, quelques secondes de coupure), envoi de la même archive que `publier.ps1`,
  **images manquantes** (`outils/images.py --sortie ~/monsuta-potaru-images`, comme `images-serveur` : les présentes sont gardées ;
  un échec ne bloque pas, avertissement orange et journal complet dans `~/monsuta-potaru-images.log` sur le VPS),
  `docker compose up -d --build`, et vérification que le site répond, script de Blazor compris (`_framework/blazor.web.js` ; une minute au plus, sinon échec). Le dépôt est
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
5. Pour SonarCloud : `SONAR_TOKEN` (jeton créé dans SonarCloud, « My Account » → « Security »).

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
| [Pokémon Showdown](https://github.com/smogon/pokemon-showdown) (Guangcong Luo et contributeurs), paquet [`@pkmn/sim`](https://github.com/pkmn/ps) | Simulateur des combats (`showdown/combat.js`) : règles, capacités, talents, objets de chaque génération | MIT |
| [PKHeX](https://github.com/kwsch/PKHeX) (Kaphotics et contributeurs), paquet `PKHeX.Core` | Lecture et écriture des sauvegardes, Pokémon, sacs, noms français, légalité ; images d'objets « Big Items » et « Artwork Items », fond de boîte « Forêt » de Diamant Étincelant du parc (`outils/images.py`) | GPL-3.0 |
| [PokéAPI sprites](https://github.com/PokeAPI/sprites) | Images des Pokémon (normales et chromatiques, formes, femelles, Méga-Évolutions, Gigamax), de la plupart des objets et des CT (`wwwroot/images`) ; sprites de Rouge / Bleu, Jaune, Or, Argent, Cristal, Rubis / Saphir, Émeraude, Rouge Feu / Vert Feuille, Diamant / Perle, Platine, HeartGold / SoulSilver et Noire / Blanche des living dex, formes comprises (`versions/generation-i` à `generation-v`, `wwwroot/images/livingdex`) ; sprites de dos et chromatiques de Rubis / Saphir, Émeraude et Rouge Feu / Vert Feuille des combats (`back/`, `shiny/`) | CC0 1.0 pour le dépôt ; images © The Pokémon Company |
| [PokéAPI](https://github.com/PokeAPI/pokeapi) (données) | Liste des formes de chaque espèce et des espèces dont la femelle est différente (tables `pokemon`, `pokemon_forms`, `pokemon_species`, lues par `outils/images.py` ; `has_gender_differences` reprise dans `GlobalLivingDex.GenderDifferences`) ; ordre des Pokédex régionaux des living dex (`pokemon_dex_numbers`, Pokédex « original-johto », « hoenn », « original-sinnoh », « extended-sinnoh », « updated-johto », « original-unova », « updated-unova », « kalos-central », « kalos-coastal », « kalos-mountain », « updated-hoenn », « original-alola », « updated-alola », « letsgo-kanto », « galar », « isle-of-armor », « crown-tundra », « hisui », « paldea », « kitakami » et « blueberry », repris dans `LivingDexGames`) | BSD-3-Clause |
| [PokéSprite](https://github.com/msikma/pokesprite) (msikma) | Images d'objets absentes de PokéAPI, retrouvées par numéro d'objet (`data/item-map.json`) | MIT ; images © Nintendo / Creatures Inc. / GAME FREAK Inc. |
| [SpriteCollab](https://github.com/PMDCollab/SpriteCollab) (PMD Collab, [sprites.pmdcollab.org](https://sprites.pmdcollab.org)) | Sprites animés façon Donjon Mystère du « Parc », animations Idle, Walk et Sleep (`wwwroot/images/donjon`, par `outils/images.py donjon`) ; auteurs de chaque sprite dans `wwwroot/images/donjon/credits.txt` | CC BY-NC 4.0 (usage non commercial, auteurs à citer) |
| [Bootstrap](https://getbootstrap.com) | Mise en page et composants de l'interface (`wwwroot/lib/bootstrap`) | MIT |
| [Bootstrap Icons](https://icons.getbootstrap.com) | Icônes des menus (dessins SVG dans `wwwroot/app.css`, « Icônes des menus ») | MIT |
| [ASP.NET Core, Blazor, Identity, Entity Framework Core](https://dotnet.microsoft.com) (Microsoft) | Site, comptes, accès à la base | MIT |
| [SQLite](https://sqlite.org) | Base de données | Domaine public |
| [xUnit](https://xunit.net), [coverlet](https://github.com/coverlet-coverage/coverlet) | Tests, couverture des tests (envoyée à SonarCloud) | Apache-2.0, MIT |
| [SonarScanner for .NET](https://github.com/SonarSource/sonar-scanner-msbuild) (SonarSource), [SonarCloud](https://sonarcloud.io) | Revue de la qualité du code dans la CI (`.github/workflows/ci.yml`), service en ligne gratuit pour les projets publics | LGPL-3.0 (scanner) |
| [Caddy](https://caddyserver.com), [Docker](https://www.docker.com) | Mise en ligne (HTTPS, conteneurs) | Apache-2.0 |
| [GitHub Actions](https://github.com/features/actions) : [actions/checkout](https://github.com/actions/checkout), [actions/setup-dotnet](https://github.com/actions/setup-dotnet) | Tests et déploiement (`.github/workflows/ci.yml`, `cd.yml`) | MIT |
| [Bulbapedia](https://bulbapedia.bulbagarden.net) | Référence consultée pour vérifier l'ordre des Noigrumes d'Or/Argent/Cristal | — |
| [Bulbagarden Archives](https://archives.bulbagarden.net) | Jaquettes officielles de Pokémon Rouge, Bleu, Jaune, Or, Argent, Cristal, Rubis, Saphir, Émeraude, Rouge Feu, Vert Feuille, Diamant, Perle, Platine, HeartGold, SoulSilver, Noire, Blanche, Noire 2, Blanche 2, X, Y, Rubis Oméga, Saphir Alpha, Soleil, Lune, Ultra-Soleil, Ultra-Lune, Let's Go Pikachu, Let's Go Évoli, Épée, Bouclier, Diamant Étincelant, Perle Scintillante, Légendes Arceus, Écarlate et Violet (versions anglaises réduites, « Red/Blue/Yellow/Gold/Silver/Crystal/Ruby/Sapphire/Emerald/FireRed/LeafGreen/Diamond/Pearl/Platinum/HeartGold/SoulSilver/Black/White/Black 2/White 2/X/Y/Omega Ruby/Alpha Sapphire/Sun/Moon/Ultra Sun/Ultra Moon/Lets Go Pikachu/Lets Go Eevee/Sword/Shield/Brilliant Diamond/Shining Pearl/Legends Arceus/Scarlet/Violet EN boxart », Émeraude, Diamant, Perle, HeartGold et SoulSilver en JPEG), images des living dex (`wwwroot/images/livingdex/jaquettes`, par `outils/images.py livingdex`) | Images © Nintendo / Creatures Inc. / GAME FREAK Inc. ; usage de fan, non commercial |

Pokémon et tous les noms et images associés sont des marques et créations de Nintendo, Creatures Inc., GAME FREAK Inc. et
The Pokémon Company. Monsuta Potaru est un projet de fan privé, sans lien avec eux.
