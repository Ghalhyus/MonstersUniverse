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

## Fonctionnement

1. L'utilisateur charge sa sauvegarde (`.sav`, `.dsv`…) dans « Ma banque ».
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
- Tant qu'elle est stockée, la sauvegarde est **refusée dans « Ma banque »** (au chargement et à la validation).
- Une mise à jour avec moins de temps de jeu que la version stockée demande confirmation (ancienne copie ?).

Lors d'un retrait, PKHeX convertit le Pokémon au format du jeu si possible (ex. PK4 → PK5) et refuse ce qui
est incompatible (espèce, attaques ou objets inexistants dans le jeu, conversion vers une génération antérieure).

## Organisation du code

| Fichier | Rôle |
|---|---|
| `PokeBank/Services/SaveSession.cs` | Sauvegarde chargée + dépôts/retraits en attente (PKHeX) |
| `PokeBank/Services/BankService.cs` | Accès base, validation transactionnelle, copies de sécurité |
| `PokeBank/Services/PokemonInfo.cs` | Noms français, sprites, légalité |
| `PokeBank/Components/Pages/Bank.razor` | Page principale (boîtes, glisser-déposer, sélection multiple) |
| `PokeBank/Components/Pages/Collection.razor` | Toute la collection sur une page (chargée au fil du défilement), filtre par origine |
| `PokeBank/Components/Pages/Saves.razor` | Gestion des sauvegardes stockées (ajout, mise à jour, suppression) |
| `PokeBank/Services/StoredSaveService.cs` | Stockage, mise à jour et suppression des sauvegardes stockées |
| `PokeBank/Services/SaveIdentity.cs` | Empreinte d'une sauvegarde (unicité sur le serveur) |
| `PokeBank/Data/BankPokemon.cs` | Table des Pokémon (octets bruts + métadonnées ; `StoredSaveId` si lecture seule) |
| `PokeBank/Data/StoredSave.cs` | Table des sauvegardes stockées |

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

- Licence : PKHeX est sous GPL-3.0 ; si tu publies ce code, il doit l'être sous GPL aussi.
- Les sprites viennent du dépôt GitHub de PokéAPI (chargés par le navigateur).
- Usage privé uniquement.
- Sur Noir 2 / Blanc 2, PKHeX accepte les Pokémon de génération 5 ou antérieure ; un Pokémon de génération 6+
  déposé depuis un autre jeu restera dans la banque.
