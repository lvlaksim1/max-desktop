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
            Console.WriteLine("Modal login close and owner lifetime: PASS");
            return 0;
        }
        finally { owner.Close(); app.Shutdown(); }
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
