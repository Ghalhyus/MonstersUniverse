# Projet « Refuge » : la suite du parc

*Discussion du 30 octobre 2026. Rien n'est réalisé pour l'instant.*

## Objectif

Faire du parc un **refuge de Pokémon** :
- un parc qu'on **agrandit en payant en pokédollars** ;
- **plusieurs parcs** par joueur ;
- on **fait glisser la carte** pour explorer ;
- à terme, une **IA propre à chaque espèce** ;
- un jour peut-être, des parcs **construits par les joueurs**.

## Décisions prises

| Sujet | Choix |
|---|---|
| Moteur | **Phaser 3** (3.90.0, moteur de jeu 2D web, MIT) plutôt que Unity WebGL : léger, immédiat, compatible avec la CSP et Blazor, marche sur téléphone |
| Graphismes | Pack **Cute Fantasy** (+ Desert, Dungeons, ShroomLands, Volcano), tuiles 16 × 16, remplace Tiny Tales (`C:\Sources\Asset_Cute_Fantasy`, [kenmi-art.itch.io/cute-fantasy-rpg](https://kenmi-art.itch.io/cute-fantasy-rpg)) |
| Licence du pack | Usage commercial et modifications permis, **aucune redistribution** : hors Git, envoyé sur le serveur, cité dans les « Crédits » |
| Cartes | Dessinées dans **Tiled** ([mapeditor.org](https://www.mapeditor.org), à installer), exportées en JSON que Phaser lit directement |
| Architecture | La carte devient une **donnée** (plus une image toute faite) ; le **serveur valide** les achats, la capacité et le nombre de parcs |
| Vue | Un peu de dézoom à chaque agrandissement (zoom entier pour rester net), puis **glisser** pour explorer |

## Étapes prévues

1. **Fondations** : plusieurs parcs (le parc actuel devient le n° 1), une carte Tiled affichée par Phaser, l'IA actuelle
   avec chemins et collisions, la vue qu'on fait glisser.
   *Fait le 30 octobre 2026 :* carte Tiled (`Parc_1`, 80 × 60 tuiles) affichée par Phaser, zone « Départ » de 24 × 20
   tuiles (le reste assombri), vue qu'on fait glisser, chemins et collisions, zoom par crans entiers et plein écran,
   saccades corrigées (README, « Parc »). Essai de charge : 150 Pokémon sur toute la carte restent fluides (~0,5 ms de
   calcul par image) ; l'utilisateur a essayé 150, 120 puis 100 avant de revenir à 30 (capacité des parcs agrandis encore
   à choisir). *Reste :* plusieurs parcs
   en base, feuillages triés selon la hauteur (aujourd'hui toujours devant les Pokémon).
2. **Agrandissements payants** : débit atomique des pokédollars, une zone cachée qui se dévoile.
3. **IA par espèce** : profils tirés des types et des groupes d'œufs, à partir d'un fichier de comportements fourni par
   l'utilisateur.
4. *(Idée lointaine)* **Construction par les joueurs**.

## Questions à trancher au lancement

1. ~~Qui dessine la première carte~~ : générée par un script (`C:\Sources\Parcs\generer_parc.py`), retouchable dans Tiled.
2. La capacité en Pokémon d'un parc agrandi (départ : 24 × 20 tuiles, 30 Pokémon).
3. La forme des agrandissements (recommandé : dévoiler la carte déjà dessinée autour de « Départ ») et leurs prix.

## Points de vigilance

- La netteté du pixel art impose des zooms entiers : ×3, ×2 ou ×1, et à ×1 les Pokémon sont trop petits.
- L'échelle des sprites Donjon Mystère face aux tuiles de 16 px est à essayer.
- Chaque navigateur fait tourner sa propre simulation : deux visiteurs ne voient pas les Pokémon aux mêmes endroits.
