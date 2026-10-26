using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using MaxDesktop.App;
using MaxDesktop.Protocol;

// No account, SMS, network, or user secrets; run on Windows in STA.
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var owner = new Window { Width = 300, Height = 180, Title = "MAX owner smoke" };
        app.MainWindow = owner;
        owner.Show();
        try
        {
            Check(owner, ConnectionState.Disconnected, true);
            Check(owner, ConnectionState.AwaitingVerification, true);
            Check(owner, ConnectionState.Disconnected, false);
            TestAccountVault();
            Console.WriteLine("Modal login close, account vault and owner lifetime: PASS");
            return 0;
        }
        finally { owner.Close(); app.Shutdown(); }
    }


    private static void TestAccountVault()
    {
        string folder = Path.Combine(Path.GetTempPath(), "MaxDesktopVaultTest-" + Guid.NewGuid().ToString("N"));
        var original = new MaxSavedSession("+70000000001", "legacy-test-token",
            "legacy-device", "legacy-instance", "TCP_PHYSICAL");
        var second = new MaxSavedSession("+70000000002", "second-test-token",
            "second-device", "second-instance", "WEB_PHYSICAL");
        try
        {
            var vault = new ProtectedMaxAccountVault(folder, () => original);
            if (vault.Phones().Count != 1 || vault.ActivePhone != original.Phone ||
                vault.Slot(original.Phone).Load()?.LoginToken != original.LoginToken)
                throw new Exception("Legacy DPAPI migration did not preserve the session");
            if (System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder, "max-accounts.dat")))
                    .Contains(original.LoginToken, StringComparison.Ordinal))
                throw new Exception("Token stored in clear text");
            var newSlot = vault.Slot();
            if (newSlot.Load() is not null)
                throw new Exception("Fresh account unexpectedly reused existing credentials");
            newSlot.Save(second);
            if (vault.Phones().Count != 2 || vault.ActivePhone != original.Phone)
                throw new Exception("Adding another account modified active session");
            vault.Activate(second.Phone);
            var reopened = new ProtectedMaxAccountVault(folder, () => original);
            if (reopened.ActivePhone != second.Phone ||
                reopened.Find(original.Phone)?.LoginToken != original.LoginToken ||
                reopened.Find(second.Phone)?.LoginToken != second.LoginToken)
                throw new Exception("Independent DPAPI sessions did not survive restart");
            bool rejected = false;
            try { reopened.Slot(original.Phone).Save(second); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Cross-account token overwrite accepted");
            reopened.Remove(original.Phone);
            var clean = new ProtectedMaxAccountVault(folder, () => original);
            if (clean.Phones().Count != 1 || clean.Find(original.Phone) is not null ||
                clean.ActivePhone != second.Phone)
                throw new Exception("Deleted migrated account was resurrected");
            clean.Remove(second.Phone);
            var empty = new ProtectedMaxAccountVault(folder, () => original);
            if (empty.Phones().Count != 0 || empty.ActivePhone is not null)
                throw new Exception("Deletion did not persist without legacy resurrection");
            // Invalid vaults fail closed; the manager must never erase them.
            string path = Path.Combine(folder, "max-accounts.dat");
            byte[] damaged = File.ReadAllBytes(path);
            damaged[^1] ^= 0x6e;
            File.WriteAllBytes(path, damaged);
            bool failedClosed = false;
            try { new ProtectedMaxAccountVault(folder, () => original).Phones(); }
            catch (System.Security.Cryptography.CryptographicException) { failedClosed = true; }
            if (!failedClosed || !File.ReadAllBytes(path).SequenceEqual(damaged))
                throw new Exception("Damaged encrypted vault was replaced or accepted");
            Console.WriteLine("DPAPI multi-account migration, selection, deletion and corruption: PASS");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static void Check(Window owner, ConnectionState state, bool button)
    {
        var fake = new FakeTransport(state);
        var login = new LoginWindow(fake) { Owner = owner };
        login.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (button)
            {
                if (login.FindName("CloseLoginButton") is not Button close)
                    throw new Exception("Close control missing");
                close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            else login.Close();
        }));
        bool? result = login.ShowDialog();
        if (result == true || login.IsVisible || !owner.IsVisible ||
            !ReferenceEquals(Application.Current.MainWindow, owner))
            throw new Exception("Closing auth dialog destroyed the application");
        if (fake.CancelCount != (state == ConnectionState.Disconnected ? 0 : 1))
            throw new Exception("Incorrect auth teardown on dialog close");
    }

    private sealed class FakeTransport(ConnectionState initial) : IMaxAuthenticationTransport
    {
        public ConnectionState State { get; private set; } = initial;
        public string? CurrentPhone => null;
        public int CancelCount { get; private set; }
        public Task CancelPendingLoginAsync()
        {
            CancelCount++;
            State = ConnectionState.Disconnected;
            return Task.CompletedTask;
        }
        public Task<AuthResult> ProbeConnectionAsync(CancellationToken ct=default) => Task.FromResult(new AuthResult(ConnectionState.Disconnected,null));
        public Task<AuthResult> RestoreSessionAsync(CancellationToken ct=default) => Task.FromResult(new AuthResult(ConnectionState.Disconnected,null));
        public Task<AuthResult> BeginLoginAsync(string phone,CancellationToken ct=default) => throw new Exception("No live login");
        public Task<AuthResult> VerifyCodeAsync(string code,CancellationToken ct=default) => throw new Exception("No OTP");
        public Task<AuthResult> VerifyPasswordAsync(string pwd,CancellationToken ct=default) => throw new Exception("No password");
        public Task LogoutAsync(CancellationToken ct=default) => Task.CompletedTask;
        public Task<IReadOnlyList<ChatSummary>> GetChatsAsync(CancellationToken ct=default) => Task.FromResult<IReadOnlyList<ChatSummary>>([]);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(long id,CancellationToken ct=default) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task SendMessageAsync(long id,string txt,CancellationToken ct=default) => throw new NotSupportedException();
        public void Dispose() {}
    }
}
