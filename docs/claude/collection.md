# Ma collection : colonnes calculées, doublons

README : « Filtres de « Ma collection » », « Doublons ».

## Colonnes remplies à l'enregistrement et au démarrage
- Filtres (`Type1`, `Nature`, `IvHp`... : `PokemonTraits.Fill`) : remplis à l'enregistrement (`BankService.CreateEntry`)
  et au démarrage pour les Pokémon plus anciens (`FillFilterDetailsAsync`, repère : `Nature` null). Un nouveau filtre qui
  demande une nouvelle colonne suit le même chemin.
- `BankPokemon.Fingerprint` (doublons, `PokemonFingerprint`) : même chemin (repère : null ; vide si illisible).
- `BankPokemon.Gender` et `HeldItem` : voir `images.md`.

## Doublons
- Une sauvegarde dont un Pokémon est déjà dans le compte est refusée.
- Les Pokémon de test `SaveSessionTests.CreatePk5(espèce, niveau)` ont **tous le même PID** : deux sauvegardes stockées
  par un même compte, ou un dépôt à côté de Pokémon déjà au stockage interne, sont alors refusés comme doublons. Utiliser
  `CreatePk5(espèce, niveau, dresseur, n°)` (PID propre au dresseur, stable : les tests de la 3DS renvoient des fichiers
  identiques) ou changer le PID (`CollectionCapacityTests.AddBankPokemon`).
