using MaxDesktop.Protocol;

var client = new UnavailableMaxTransport();
if (client.State != ConnectionState.Disconnected)
    throw new Exception("Wrong initial state.");
var login = await client.BeginLoginAsync("+70000000000");
if (login.State != ConnectionState.Failed || string.IsNullOrEmpty(login.Error))
    throw new Exception("Login must fail closed.");
if ((await client.GetChatsAsync()).Count != 0)
    throw new Exception("Synthetic chat data detected.");
try
{
    await client.SendMessageAsync(1, "test");
    throw new Exception("Unverified send accepted.");
}
catch (NotSupportedException)
{
    Console.WriteLine("Protocol boundary checks passed.");
}
