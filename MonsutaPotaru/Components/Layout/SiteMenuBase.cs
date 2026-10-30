using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Components.Layout;

/// <summary>
/// Ce que les menus du site ont en commun (menu du thème Classique et du Pokédex, <c>NavMenu</c> ; cadre du thème Ardoise,
/// <c>ArdoiseLayout</c>) : adresse de la page affichée, solde du porte-monnaie et demandes reçues (amis, échanges,
/// combats), tenus à jour en direct.
/// </summary>
public abstract class SiteMenuBase : ComponentBase, IDisposable
{
    [Inject] protected NavigationManager NavigationManager { get; set; } = default!;
    [Inject] protected WalletState Wallet { get; set; } = default!;
    [Inject] private FriendService Friends { get; set; } = default!;
    [Inject] private TradeHub Trades { get; set; } = default!;
    [Inject] private BattleHub Battles { get; set; } = default!;

    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    /// <summary>Adresse de la page affichée, relative à la racine du site (« sauvegardes/3ds »).</summary>
    protected string? currentUrl;

    private string? userId;
    protected int pendingRequests; // demandes d'ami reçues, relues à chaque navigation
    protected int tradeRequests; // demandes d'échange reçues (en direct, TradeHub)
    protected int battleRequests; // demandes de combat reçues (en direct, BattleHub)

    /// <summary>Toutes les demandes reçues : la pastille du menu « Multijoueurs ».</summary>
    protected int PlayerRequests => pendingRequests + tradeRequests + battleRequests;

    /// <summary>Menu « Sauvegardes » ouvert : sur « Mes sauvegardes » ou « Synchronisation 3DS ».</summary>
    protected bool savesOpen => currentUrl is { } url && (url == "sauvegardes" || url.StartsWith("sauvegardes/") || url.StartsWith("sauvegardes?"));

    /// <summary>Menu « Multijoueurs » ouvert : sur la recherche ou les pages d'un joueur.</summary>
    protected bool playersOpen => currentUrl is { } url
        && (url == "joueurs" || url.StartsWith("joueurs/") || url.StartsWith("joueurs?") || url == "amis" || url.StartsWith("amis?")
            || url == "echanges" || url.StartsWith("echanges?") || url == "combats" || url.StartsWith("combats?"));

    /// <summary>Menu du pseudo ouvert : sur les paramètres du profil ou les statistiques.</summary>
    protected bool accountOpen => currentUrl is { } url && (url.StartsWith("Account/Manage", StringComparison.OrdinalIgnoreCase) || url == "statistiques");

    protected override async Task OnInitializedAsync()
    {
        currentUrl = NavigationManager.ToBaseRelativePath(NavigationManager.Uri);
        NavigationManager.LocationChanged += OnLocationChanged;
        Wallet.Changed += OnWalletChanged;
        Friends.Changed += OnFriendsChanged;
        Trades.RequestsChanged += OnTradeRequestsChanged;
        Battles.RequestsChanged += OnBattleRequestsChanged;

        // Solde du porte-monnaie pour l'utilisateur connecté (rafraîchi par « Inventaire » après un retrait).
        userId = AuthState is null ? null : (await AuthState).User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null)
        {
            await Wallet.RefreshAsync(userId);
            pendingRequests = await Friends.PendingCountAsync(userId);
            tradeRequests = Trades.Incoming(userId).Count;
            battleRequests = Battles.Incoming(userId).Count;
        }
    }

    private void OnBattleRequestsChanged(string user)
    {
        if (user != userId)
            return;
        InvokeAsync(() =>
        {
            battleRequests = Battles.Incoming(user).Count;
            StateHasChanged();
        });
    }

    private void OnTradeRequestsChanged(string user)
    {
        if (user != userId)
            return;
        InvokeAsync(() =>
        {
            tradeRequests = Trades.Incoming(user).Count;
            StateHasChanged();
        });
    }

    private void OnWalletChanged() => InvokeAsync(StateHasChanged);

    private void OnFriendsChanged() => InvokeAsync(async () =>
    {
        if (userId is not null)
            pendingRequests = await Friends.PendingCountAsync(userId);
        StateHasChanged();
    });

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => InvokeAsync(async () =>
    {
        currentUrl = NavigationManager.ToBaseRelativePath(e.Location);
        if (userId is not null)
            pendingRequests = await Friends.PendingCountAsync(userId);
        StateHasChanged();
    });

    public void Dispose()
    {
        NavigationManager.LocationChanged -= OnLocationChanged;
        Wallet.Changed -= OnWalletChanged;
        Friends.Changed -= OnFriendsChanged;
        Trades.RequestsChanged -= OnTradeRequestsChanged;
        Battles.RequestsChanged -= OnBattleRequestsChanged;
        GC.SuppressFinalize(this);
    }
}
