# Comptes, connexion, rôles, thèmes

README : « Comptes », « Administration », « Thèmes ».

## Pseudo et adresse
- Pseudo = `UserName` d'Identity ; connexion par l'adresse (`AccountService.FindForLoginAsync`).
- Ancien compte : son adresse est encore son `UserName`. « @ » est interdit dans un pseudo : c'est le repère (`HasPseudo`).
- Ne jamais afficher l'adresse d'un compte à la place du pseudo.

## Connexion protégée
- `FindForLoginAsync` compte les échecs : 5 bloquent le compte 5 minutes. `AccountLimiter` (singleton) limite les envois
  par adresse IP (connexion, inscription).
- Tout échec de connexion a le même message (`LoginFailedMessage`) : ne jamais dire qu'une adresse a un compte ni qu'il est bloqué.
- Toute vérification du mot de passe passe par `AccountService.CheckPasswordAsync`, jamais `UserManager.CheckPasswordAsync`
  (il ne compte pas les échecs).
- En test : débloquer un compte en passant `LockoutEnd` dans le passé dans la base de test ; `localhost` et `127.0.0.1`
  ont chacun leur compteur d'IP.

## Pages du compte (rendu statique)
- Depuis .NET 10 (`BlazorDisableThrowNavigationException` dans le `.csproj`), `IdentityRedirectManager.RedirectTo` ne lance
  plus d'exception : le code qui suit s'exécute. Toujours `return` juste après une redirection.
- `UserAccessor.GetRequiredUserAsync` renvoie null pour un compte supprimé (page redirigée) : le vérifier dans
  `OnInitializedAsync` **et** dans la méthode d'envoi du formulaire (elle s'exécute aussi).
- Identity modifie l'objet du compte avant de valider (`SetUserNameAsync`, `SetEmailAsync`) : un refus laisse la valeur
  refusée dessus. D'où les vérifications d'unicité faites avant, dans `AccountService`.

## Rôles et réglages
- Rôles exclusifs Standard / Administrateur (`SiteRoles`). `AdminService` revérifie le rôle en base à chaque action (le
  cookie peut le garder 30 minutes).
- Code d'invitation en base (`SiteSettings`) : `Bank:InviteCode` ne sert qu'au premier démarrage.
- EF ne traduit pas `Guid.NewGuid()` dans un `ExecuteUpdate` : calculer la valeur avant.

## Thèmes
- Tout le style d'un thème sous `[data-theme="<clé>"]` (posé par `App.razor`) ; le Classique n'a rien.
- `app.css` fixe des couleurs en dur (`.btn-primary`, `.btn-link`, focus) : les écraser explicitement. Le CSS isolé des
  composants (menu) se charge après : sélecteurs en `html[data-theme=...]`.
- Le thème Pokédex couvre tout le site (contenu sous `main`) : un nouvel élément coloré en dur dans `app.css` (bleu,
  orange) doit y recevoir sa version.
- L'utilisateur veut du **sobre** : thème trop chargé refusé (fond rayé, pastilles et bordures épaisses partout) ; rouge
  du Pokédex `#c2323d` (`#dc0a2d` trop vif).
- **Ardoise** : inspiré de l'interface d'un logiciel de gestion, mais **aucune référence à ce logiciel** nulle part (code,
  commentaires, textes, README). Seul thème à cadre propre (`ArdoiseLayout`, choisi par `MainLayout` via la valeur en
  cascade `SiteTheme` de `Routes.razor`) : un lien ou une rubrique ajoutés au menu vont dans `NavMenu` **et**
  `ArdoiseLayout`, une nouvelle page dans `Breadcrumbs.For` (sinon fil d'Ariane vide), un compteur dans `SiteMenuBase`.
- Le cadre Ardoise sert aussi aux pages du compte (rendu statique) : aucun `@onclick` dedans, tout par `app.js`. La
  classe `ouvert` d'une rubrique est posée par Blazor (page de la rubrique) puis changée par `app.js` : Blazor ne la
  réécrit que si sa propre valeur change.
- Icônes des menus : dans `app.css` (globales), plus dans `NavMenu.razor.css` ; blanches, `filter: invert(...)` sur fond clair.
- Test du menu replié : cookie `menu=replie` ; le survol d'une rubrique ne se vérifie qu'avec `computer` `hover` + capture.

## Tests
- `AccountServiceTests` monte Identity comme `Program.cs` (`AccountService.ConfigureIdentity`) sur la base de test.
- Les comptes de `TestDatabase` sont créés sans Identity (pas d'adresse) : n'y appeler aucune méthode qui les revalide.
- Tests de migration : comptes ajoutés en SQL (`MigrationsTests.AddUserAsync`), pas par `db.Users` : le modèle actuel a
  des colonnes (`Theme`) qu'une base migrée à une étape plus ancienne n'a pas.
