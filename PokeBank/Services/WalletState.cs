namespace PokeBank.Services;

/// <summary>
/// Solde du porte-monnaie affiché dans le menu, partagé dans le circuit Blazor de l'utilisateur :
/// la page « Inventaire » le rafraîchit après un retrait et le menu se met à jour aussitôt.
/// </summary>
public sealed class WalletState(InventoryService inventory)
{
    public long? Balance { get; private set; }
    public event Action? Changed;

    public async Task RefreshAsync(string userId)
    {
        Balance = await inventory.GetBalanceAsync(userId);
        Changed?.Invoke();
    }
}
