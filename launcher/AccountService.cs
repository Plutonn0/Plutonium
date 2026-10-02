using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using System.IO;
using XboxAuthNet.Game.Accounts;

namespace PlutoniumLauncher;

public sealed record SavedAccount(string Id, string Name);

public sealed class AccountService
{
    private readonly JELoginHandler _login;

    public AccountService(string dataDirectory)
    {
        var storage = new DpapiJsonFileStorage(Path.Combine(dataDirectory, "accounts.bin"));
        var accounts = new JsonXboxGameAccountManager(storage, JEGameAccount.FromSessionStorage, null);
        _login = new JELoginHandlerBuilder().WithAccountManager(accounts).Build();
    }

    public IReadOnlyList<SavedAccount> GetAccounts() => _login.AccountManager.GetAccounts()
        .OfType<JEGameAccount>()
        .Where(account => !string.IsNullOrWhiteSpace(account.Identifier))
        .Select(account => new SavedAccount(account.Identifier!, account.Profile?.Username ?? account.Gamertag ?? "Minecraft account"))
        .OrderBy(account => account.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task<(SavedAccount Account, MSession Session)> AddAsync(CancellationToken cancellationToken)
    {
        // Use a new account explicitly; default authentication silently reuses the previous account.
        var session = await _login.AuthenticateInteractively(_login.AccountManager.NewAccount(), cancellationToken);
        _login.AccountManager.SaveAccounts();
        var account = GetAccounts().FirstOrDefault(a => a.Name == session.Username)
            ?? throw new InvalidOperationException("The signed-in account could not be saved.");
        return (account, session);
    }

    public async Task<MSession> AuthenticateAsync(string accountId, CancellationToken cancellationToken)
    {
        var session = await _login.Authenticate(Find(accountId), cancellationToken);
        _login.AccountManager.SaveAccounts();
        return session;
    }

    public async Task RemoveAsync(string accountId, CancellationToken cancellationToken)
    {
        await _login.Signout(Find(accountId), cancellationToken);
        _login.AccountManager.SaveAccounts();
    }

    private IXboxGameAccount Find(string id) =>
        _login.AccountManager.GetAccounts().TryGetAccount(id, out var account) && account is not null
            ? account : throw new InvalidOperationException("This account was removed. Choose another account or sign in again.");
}
