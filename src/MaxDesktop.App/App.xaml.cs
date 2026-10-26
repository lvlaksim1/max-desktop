using System.Threading;
using System.Windows;

namespace MaxDesktop.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationSignal;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private volatile bool _exiting;
    public bool IsExitRequested { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(true, @"Local\MaxDesktop.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            try { using var signal = EventWaitHandle.OpenExisting(@"Local\MaxDesktop.Activate"); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        _activationSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MaxDesktop.Activate");
        var watcher = new Thread(() =>
        {
            while (!_exiting)
            {
                try { _activationSignal.WaitOne(); }
                catch (ObjectDisposedException) { break; }
                if (!_exiting) Dispatcher.BeginInvoke(new Action(ShowMainWindow));
            }
        }) { IsBackground = true, Name = "MAX activation signal" };
        watcher.Start();

        var window = new MainWindow();
        MainWindow = window;
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Открыть MAX Desktop", null, (_, _) => Dispatcher.BeginInvoke(new Action(ShowMainWindow)));
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.BeginInvoke(new Action(ExitApplication)));
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "MAX Desktop",
            Icon = LoadTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(ShowMainWindow));
        window.Show();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            string? executable = Environment.ProcessPath;
            if (executable is not null)
                return System.Drawing.Icon.ExtractAssociatedIcon(executable) ?? System.Drawing.SystemIcons.Application;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            // The generic icon is safe if the executable icon is unavailable.
        }
        return System.Drawing.SystemIcons.Application;
    }

    public void ExitApplication()
    {
        if (IsExitRequested) return;
        IsExitRequested = true;
        if (MainWindow is { } window) window.Close();
        Shutdown();
    }

    private void ShowMainWindow()
    {
        if (MainWindow is null) return;
        if (IsExitRequested) return;
        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
        MainWindow.Topmost = true;
        MainWindow.Topmost = false;
        MainWindow.Focus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExitRequested = true;
        _exiting = true;
        _activationSignal?.Set();
        _activationSignal?.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
        }
        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}
