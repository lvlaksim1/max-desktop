using MaxDesktop.Protocol;
bool physical = args.Any(arg => arg.Equals("--physical", StringComparison.OrdinalIgnoreCase));
using var transport = physical
    ? new NativeMaxWebSocketTransport(new EmptyStore(), new PhysicalMaxWebSocketChannel(), "WEB_PHYSICAL")
    : new NativeMaxWebSocketTransport(new EmptyStore());
Console.WriteLine(physical
    ? "Anonymous MAX WEB physical-interface diagnostic; no phone, SMS, QR, password or token."
    : "Anonymous MAX WEB diagnostic; no phone, SMS, QR, password or token.");
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    var result = await transport.ProbeConnectionAsync(timeout.Token);
    Console.WriteLine("WS_PROBE_STATE=" + result.State);
    Console.WriteLine("WS_PROBE_DETAIL=" + (result.Error ?? "(none)"));
}
catch (Exception e)
{
    Console.WriteLine("WS_PROBE_FAILURE=" + e.GetType().Name);
}
// External connectivity differs by region; record diagnostic, never claim auth.
return 0;

sealed class EmptyStore : IMaxSessionStore
{
    public MaxSavedSession? Load() => null;
    public void Save(MaxSavedSession session) => throw new Exception("No personal session data allowed");
    public void Delete() { }
}
