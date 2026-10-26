using MaxDesktop.Protocol;
using var transport = new NativeMaxWebSocketTransport(new EmptyStore());
Console.WriteLine("Anonymous MAX WEB diagnostic; no phone, SMS, QR, password or token.");
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
