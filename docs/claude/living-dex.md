# Living dex

README : « Living dex ». Jeux et modes : `LivingDexGames` (`Services/LivingDexService.cs`) ; global : `GlobalLivingDex`.

## Ajouter un jeu
- Son entrée dans `All` : jaquette, dossier de sprites, Pokédex régional, dernier numéro national.
- `For(SaveFile)` : quelle sauvegarde. PKHeX ne distingue pas Rouge de Bleu, Or d'Argent, Rubis de Saphir, Rouge Feu de
  Vert Feuille, ni Diamant de Perle. HeartGold de SoulSilver, si (depuis PKHeX 26 : `soulsilver.sav` est reconnue SoulSilver).
- Les formes du mode Complet (`Forms`) : régionales, Méga, Gigamax, d'aspect, lettres de Zarbi ; pas le sexe ;
  **seulement celles qu'une sauvegarde du jeu peut contenir** :
  - les formes de Morphéo n'existent qu'en combat ;
  - Deoxys de 3e génération a la forme de son jeu (`DeoxysForm`, case `AnyForm`) ;
  - à partir de la 4e, le nombre de formes de chaque jeu vient de `PersonalTable` de PKHeX (`FormCount`, liste
    `AspectForms`), et les règles de légalité de PKHeX disent ce qu'une boîte peut garder (`FormVerifier` sur GitHub).
- Le premier numéro du Pokédex régional (`RegionalStart` : 0 pour Unys).
- Les images dans `outils/images.py` (`LIVINGDEX_SPRITES`, `LIVINGDEX_FORMS`, `LIVINGDEX_COVERS`, mêmes noms de dossiers
  et de fichiers que `LivingDexGames.Sprite` : « 201-b.png », « 386-speed.png »). Jusqu'à la 5e génération seulement ; à
  partir de la 6e, images de la collection (`SpriteFolder` null) : l'utilisateur trouve les sprites 3D hideux.
- Chaque forme a sa propre image (test `Complete_EachFormHasItsOwnNameAndSprite`).
- Vérifier que le mode Complet tient dans la sauvegarde (964 cases pour 966 places en Ultra-Soleil / Ultra-Lune).

## Préférences de l'utilisateur
- Ne jamais bloquer l'équipe pour une forme qui ne tient pas en boîte (Shaymin Céleste en 4e) : case `KeyItem`, remplie
  par un **deuxième** exemplaire de l'espèce et l'objet rare dans le sac (lu dans la sauvegarde stockée,
  `BagItemsAsync`), ce qui manque en infobulle (`LivingDexEntry.Note`).
- Conseil d'une case manquante : `LivingDexSlot.Hint` (saisons de Vivaldaim et Haydaim en 5e : seuls ceux de l'équipe
  changent de saison). Vérifier ce genre de règle avant de l'affirmer.
- Une seule case pour Prismillon (motifs), Arceus et Genesect. Espèce à beaucoup de formes : lui demander avant de toutes
  les compter (il a voulu les 7 noyaux de Météno ; retiré les casquettes de Pikachu et les Dominants).

## Par génération
- **6e, Méga-Évolutions** : cases `MegaItem` / `MegaMove`, remplies par un exemplaire **de plus** qui tient la gemme
  (`BankPokemon.HeldItem`) ; `EntriesAsync` leur attribue leurs exemplaires d'abord, les autres cases ne les reprennent
  pas. Gemme d'une forme : `ItemStorage9ZA.GetExpectedMegaStoneOrPrimalOrb` (formes limitées à celles du jeu,
  `FormCount` : PKHeX connaît aussi les Méga de Légendes Z-A). Méga-Rayquaza n'a pas de gemme : Draco-Ascension, lue dans
  les octets des seuls Rayquaza. Sous-Pokédex (Kalos) : `LivingDexGameInfo.Sections`, `RegionalNumber`.
- **7e** : Shaymin Céleste et Hoopa Déchaîné tiennent en boîte (`FormVerifier` : `Format <= 6`), cases ordinaires.
  Let's Go : ni objet tenu, Méga par la gemme du sac (`MegaInBag`), son Pokédex = son national
  (`LivingDexGameInfo.National`). Une sauvegarde Let's Go vide de PKHeX n'est pas reconnue par `SaveUtil` : écrire le pied
  « BEEF » et le numéro de bloc 0x13 (`LivingDexServiceTests.LetsGoWith`).
- **8e** : ce que le jeu peut contenir vient de `PersonalTable.IsPresentInGame` (`LivingDexGames.Present`), pas de
  `FormCount` seul (Épée / Bouclier : 664 espèces ; Légendes Arceus n'a que les Caninos de Hisui : `FirstForm`). Formes
  régionales par leur nom anglais chez PKHeX (« Alola », « Galar », « Hisui ») ; la casquette d'Alola de Pikachu s'appelle
  aussi « Alola ». Gigamax : case `Gigamax`, un exemplaire de plus avec le facteur (`IGigantamax`, lu dans les octets des
  seules espèces de `GigamaxSpecies`). Sauvegarde Légendes Arceus vide non reconnue par `SaveUtil` (pas de test du service
  pour ce jeu) ; Diamant Étincelant vide, si.
- **9e** : Écarlate / Violet seulement (`For` : `SAV9SV`) ; Légendes Z-A est aussi en 9e chez PKHeX, sans living dex.
  Sauvegarde vide non reconnue par `SaveUtil`, pas de vraie sauvegarde : images vérifiées par un test temporaire
  (`PokemonSprites.Load(RealSavesTests.WebRootWithImages())`, chaque case a son image), pas dans le navigateur.

## Living dex global (`GlobalLivingDex`)
- Sur le stockage interne (`BankPokemon.StoredSaveId` null), sans les chromatiques (voulu), image : Master Ball
  (`CoverItem`). Forme reconnue par son nom anglais chez PKHeX (`FormKey`, « Normal » pour la base, que PKHeX laisse vide
  dans un jeu où l'espèce n'a qu'une forme) ; chaque exemplaire pour une seule case (`Assign`).
- Complet = les cases Complet des jeux ; Ultime = en plus tout ce que les tables de PKHeX donnent par jeu, moins `Excluded`.
  Voulu : toutes les formes, même au même aspect et de couleur (pas les chromatiques), mâle / femelle, Spinda seul ; pas
  les formes de combat sans objet tenu ; pas Légendes Z-A.
- Nouveau jeu : son Complet entre tout seul dans le global ; ajouter sa table à `GlobalLivingDex.Games` pour l'Ultime.
- Chromatique (`GlobalShiny`) : les mêmes cases moins `ShinyLocked` (liste validée par l'utilisateur, d'après Bulbapedia ;
  Dominants retirés aussi), seulement les chromatiques. Image : Charme Chroma. Forme ou espèce jamais distribuée en
  chromatique : l'ajouter à `ShinyLocked`.

## Images et jaquettes
- Les tests n'exigent **pas** ces images (la CI les copie du VPS, qui ne les reçoit qu'au déploiement) : tester le chemin,
  pas la présence.
- Jaquettes : Bulbagarden Archives (API MediaWiki, `iiurlwidth=480`). Sur ce PC, Python refuse son certificat (magasin de
  Windows) et Bulbagarden refuse Python (403) : `curl` avec l'agent « PokeBank (outils/images.py) » passe, ou un contexte
  SSL avec `cafile` (paquet de Git : `/usr/ssl/certs/ca-bundle.crt`), ou `outils\recuperer-images.cmd`. Sur le VPS, ça passe.
