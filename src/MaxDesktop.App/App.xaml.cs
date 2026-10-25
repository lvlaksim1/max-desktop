using System.Threading;
using System.Windows;

namespace MaxDesktop.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationSignal;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private volatile bool _exiting;

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
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.BeginInvoke(new Action(() => MainWindow.Close())));
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "MAX Desktop",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(ShowMainWindow));
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Minimized) window.Hide();
        };
        window.Show();
    }

    private void ShowMainWindow()
    {
        if (MainWindow is null) return;
        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
