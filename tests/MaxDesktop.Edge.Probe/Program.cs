using System.Windows;
using MaxDesktop.App;
using MaxDesktop.Protocol;
using Microsoft.Web.WebView2.Wpf;

namespace MaxDesktop.EdgeProbe;

public static class Program
{
    [STAThread]
    public static int Main()
    {
        Console.WriteLine("EDGE_PROBE_START: Chromium WebSocket INIT only; no phone, QR or account data.");
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var control = new WebView2
        {
            Width = 3,
            Height = 3,
            IsHitTestVisible = false,
            Focusable = false
        };
        var window = new Window
        {
            Title = "MAX anonymous Chromium transport test",
            Width = 110,
            Height = 80,
            ShowInTaskbar = false,
            Content = control
        };
        window.Loaded += async (_, _) =>
        {
            try
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(65));
                using var channel = new EdgeMaxWebSocketChannel(control);
                using var transport = new NativeMaxWebSocketTransport(
                    new EmptyStore(), channel, "WEB_EDGE");
                var result = await transport.ProbeConnectionAsync(stop.Token);
                Console.WriteLine("EDGE_PROBE_STATE=" + result.State);
                Console.WriteLine("EDGE_PROBE_DETAIL=" + (result.Error ?? "(none)"));
            }
            catch (Exception e)
            {
                // Never print exception messages from browser pages; error types
                // are sufficient for a privacy-safe integration diagnostic.
                Console.WriteLine("EDGE_PROBE_EXCEPTION=" + e.GetType().Name);
            }
            finally
            {
                window.Close();
                application.Shutdown();
            }
        };
        application.Run(window);
        return 0;
    }

    private sealed class EmptyStore : IMaxSessionStore
    {
        public MaxSavedSession? Load() => null;
        public void Save(MaxSavedSession session) => throw new Exception("No account data allowed");
        public void Delete() { }
    }
}
