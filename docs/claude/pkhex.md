# Pièges de PKHeX (sauvegardes, Pokémon)

Pour les sacs, objets et CT : `inventaire.md`.

- PKHeX 26 (C# 14) a des **propriétés d'extension** : `result.IsSuccess`, `context.IsValid`, sans parenthèses.
- Octets d'un Pokémon au format des boîtes : `BankService.StoredData(pk)` (`DecryptedBoxData` n'existe plus).
- PKHeX **écrit dans le tableau d'octets qu'on lui donne** : lui passer `data.ToArray()` quand l'original doit rester intact.
- Dans les tests : `SaveUtil.TryGetSaveFile` (les méthodes internes de PKHeX ne sont pas visibles).
- Sauvegardes créées vides :
  - 3e et 4e génération (`new SAV3E()`, `new SAV4Pt()`) : `Write()` plante. Tester en mémoire, ou partir d'un fichier brut
    valide (voir `CreatePlatinumSave`) ;
  - 1re et 2e génération (`new SAV1(...)`, `new SAV2(...)`) : **pas reconnues** par `SaveUtil` une fois écrites. Tester en
    mémoire (relire `sav.Inventory`), ou partir d'une vraie sauvegarde (`Sauvegardes/rouge.sav`, voir
    `InventoryServiceTests.RedFile`) ;
  - Let's Go, Légendes Arceus, Écarlate / Violet : voir `living-dex.md`.
- Après une mise à jour de PKHeX.Core : relancer `outils/images.py` (nouvelles espèces, voir `images.md`).
