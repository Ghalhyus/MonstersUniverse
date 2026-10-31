# CI/CD

`.github/workflows/ci.yml` et `cd.yml` (README, « CI/CD »), séparés :
- CI : tests sur Ubuntu à chaque push, avec l'analyse SonarCloud (organisation `ghalhyus`, clé `ghalhyus_monsuta-potaru`,
  secret `SONAR_TOKEN` ; pour information, jamais bloquante : `continue-on-error`). Réglages passés à
  `dotnet sonarscanner begin` (pas de `sonar-project.properties`) ; règles écartées pour `outils/**` par
  `sonar.issue.ignore.multicriteria` : `python:S2245` (aléatoire à graine fixe voulu) et `pythonsecurity:*` (chemins de
  la ligne de commande : scripts lancés à la main). Le reste (complexité...) se corrige dans le code.
- Problèmes à corriger : l'utilisateur colle le JSON de l'API SonarCloud (`issues` : règle, fichier, ligne). Une
  refonte de `outils/generer-parc.py` se vérifie en comparant ses sorties avant/après (`cmp` : cartes et aperçus
  identiques octet pour octet, graine fixe).
- CD : bouton « Run workflow » de « CD » ; ne relance pas les tests, lit le résultat de la CI du commit (case « Forcer »
  pour passer outre) ; vérifie que `blazor.web.js` répond.
- Le CLI `gh` n'est pas installé : l'utilisateur copie le message d'un échec.
- Les tests y tournent sous **Linux** (rappelé dans `CLAUDE.md`) : noms de fichiers sensibles à la casse, séparateur `/`,
  culture `fr-FR` par ICU.
- Un nouveau fichier nécessaire au déploiement s'ajoute à l'archive des deux côtés (`publier.ps1` et le workflow ; rappelé
  dans `CLAUDE.md`).
