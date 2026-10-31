# Tester dans le navigateur

- Instance de test sur un autre port, avec sa propre base, ses copies et ses clés (dans un dossier temporaire), en lançant
  la DLL compilée (`CLAUDE.md`, « Compiler et tester ») :
  `dotnet <build>/bin/MonsutaPotaru/debug/MonsutaPotaru.dll --contentRoot <dépôt>/MonsutaPotaru --environment Development --urls http://localhost:5299
  --ConnectionStrings:DefaultConnection="DataSource=<tmp>/test.db;Cache=Shared" --Bank:BackupPath=<tmp>/backups --Bank:KeysPath=<tmp>/keys`.
  Le `.claude/launch.json` correspondant est temporaire (dossier ignoré par Git) : le supprimer après les tests.
- **Compte de test** : inscription par un script dans la page, qui génère lui-même un mot de passe jetable (l'inscription
  connecte : le mot de passe n'a jamais à être écrit ni relu). Code d'invitation `pokeamis` sur une base neuve ;
  administrateur : `--Bank:AdminPseudo=<pseudo>`. Envoyer un formulaire par `fetch` (`FormData` du formulaire) ; après une
  connexion réussie, recharger la page (jeton antiforgery lié à l'ancien compte : 400).
- **Envoi d'un `.sav`** : servir le dossier avec un petit serveur HTTP local qui autorise CORS, appelé par
  `http://localhost:<port>` (la CSP de développement refuse `127.0.0.1`), puis dans la page `fetch` du fichier →
  `DataTransfer` → `input.files`, et déclencher `change`.
- Vérifier par `javascript_tool` (textes, états des boutons) ; une capture d'écran seulement quand la mise en page compte
  (elles coûtent cher). Exception : panneau masqué, les listes virtualisées (« Ma collection ») ne se remplissent qu'avec
  une capture.
- Deux comptes à la fois : un sur `localhost:5299`, l'autre sur `127.0.0.1:5299` (cookies propres au nom d'hôte).
- **Règle anti-copie** : une sauvegarde modifiée sur le serveur n'accepte une mise à jour qu'après téléchargement, et avec
  plus de temps de jeu. En test : passer `DownloadedSinceModification` à 1 dans la base de test, et ajouter du temps de
  jeu au fichier (`MONSUTA_POTARU_ADD_PLAYTIME`, voir le README).
- Sauvegardes d'exemple (ignorées par Git) : `MONSUTA_POTARU_SAMPLE_OUT` (Noir 2), `MONSUTA_POTARU_SAMPLE_PT_OUT`
  (Platine). Après une régénération de `exemple-platine.sav`, lui ajouter des heures de jeu pour qu'elle dépasse la
  version déjà stockée chez l'utilisateur.
