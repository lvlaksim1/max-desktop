using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MaxDesktop.Protocol;

namespace MaxDesktop.App;

public partial class MainWindow : Window
{
    private readonly UserSettings _settings;
    private readonly IMaxAuthenticationTransport _transport = new HybridMaxTransport(new ProtectedMaxSessionStore());
    private readonly ObservableCollection<ChatSummary> _chats = new();
    private readonly ObservableCollection<ChatMessage> _messages = new();

    public MainWindow()
    {
        _settings = SettingsStore.Load();
        SettingsStore.ApplyTheme(_settings);
        InitializeComponent();
        ChatList.ItemsSource = _chats;
        MessageList.ItemsSource = _messages;
        Title = "MAX Desktop — v" + typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
        VersionLabel.Text = "v" + typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Width = Math.Clamp(_settings.WindowWidth, 820, 2800);
        Height = Math.Clamp(_settings.WindowHeight, 520, 1800);
        AccountColumn.Width = new GridLength(Math.Clamp(_settings.LeftPaneWidth, 150, 360));
        ChatColumn.Width = new GridLength(Math.Clamp(_settings.ChatPaneWidth, 220, 550));
        if (_settings.WindowLeft is double left && _settings.WindowTop is double top &&
            double.IsFinite(left) && double.IsFinite(top) &&
            left + Width > SystemParameters.VirtualScreenLeft &&
            left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
            top + Height > SystemParameters.VirtualScreenTop &&
            top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            Left = left;
            Top = top;
        }
        StatusLabel.Text = "Восстановление сессии MAX...";
        try
        {
            ApplyAuthResult(await _transport.RestoreSessionAsync());
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Не удалось восстановить сеанс MAX: " + ex.GetType().Name;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App app && !app.IsExitRequested)
        {
            // X means minimize to the tray. The taskbar minimize control
            // retains ordinary Windows minimization; don't hide on StateChanged.
            e.Cancel = true;
            Hide();
            return;
        }

        try
        {
            if (WindowState == WindowState.Normal)
            {
                _settings.WindowWidth = ActualWidth;
                _settings.WindowHeight = ActualHeight;
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
            }
            _settings.LeftPaneWidth = AccountColumn.ActualWidth;
            _settings.ChatPaneWidth = ChatColumn.ActualWidth;
            SettingsStore.Save(_settings);
            _transport.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusLabel.Text = "Не удалось сохранить расположение окна: " + ex.Message;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        dialog.ShowDialog();
    }

    private void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LoginWindow(_transport) { Owner = this };
        if (dialog.ShowDialog() == true)
            ApplyAuthResult(new AuthResult(ConnectionState.Connected, null));
    }

    private async void RemoveAccount_Click(object sender, RoutedEventArgs e)
    {
        RemoveAccountButton.IsEnabled = false;
        try
        {
            await _transport.LogoutAsync();
            ApplyAuthResult(new AuthResult(ConnectionState.Disconnected, null));
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Ошибка выхода: " + ex.GetType().Name;
        }
        finally { RemoveAccountButton.IsEnabled = true; }
    }

    private void ApplyAuthResult(AuthResult result)
    {
        bool connected = result.State == ConnectionState.Connected;
        AccountStateText.Text = connected ? "MAX подключён" : "Не подключён";
        AccountPhoneText.Text = connected
            ? _transport.CurrentPhone ?? "Сеанс подтверждён"
            : "Нет активного сеанса";
        AddAccountButton.Content = connected ? "Сменить аккаунт" : "+ Добавить аккаунт";
        RemoveAccountButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        ConversationHint.Text = connected
            ? "Вход подтверждён; загрузка чатов ещё не реализована"
            : "Выберите чат после подключения аккаунта";
        StatusLabel.Text = connected
            ? "Авторизация MAX подтверждена. Подключение чатов будет добавлено отдельно."
            : result.Error ?? "Аккаунт MAX не подключён.";
    }

    private void ChatSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ChatList.ItemsSource is null) return;
        var view = CollectionViewSource.GetDefaultView(ChatList.ItemsSource);
        string term = ChatSearch.Text.Trim();
        view.Filter = item => item is ChatSummary chat &&
            (term.Length == 0 || chat.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        view.Refresh();
    }

    private async void ChatList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChatList.SelectedItem is not ChatSummary chat)
        {
            ConversationTitle.Text = "Переписка не выбрана";
            MessageList.Visibility = Visibility.Collapsed;
            EmptyConversation.Visibility = Visibility.Visible;
            return;
        }
        ConversationTitle.Text = chat.Title;
        _messages.Clear();
        var items = await _transport.GetMessagesAsync(chat.Id);
        foreach (var message in items) _messages.Add(message);
        MessageList.Visibility = Visibility.Visible;
        EmptyConversation.Visibility = Visibility.Collapsed;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateProgressBar.Value = 0;
        try
        {
            StatusLabel.Text = "Проверка обновлений...";
            var progress = new Progress<UpdateProgress>(item =>
            {
                StatusLabel.Text = item.Message;
                UpdateProgressBar.Value = item.Percent;
            });
            var update = await UpdateService.PrepareAsync(progress);
            if (update is null)
            {
                StatusLabel.Text = "Новых выпусков нет.";
                UpdateProgressBar.Visibility = Visibility.Collapsed;
                return;
            }
            string agent = Path.Combine(AppContext.BaseDirectory, "MaxDesktop.UpdateAgent.exe");
            if (!File.Exists(agent)) throw new FileNotFoundException("Модуль установки обновлений не найден", agent);
            string args = $"\"{update.InstallerPath}\" {Environment.ProcessId} {update.Sha256}";
            Process.Start(new ProcessStartInfo(agent, args) { UseShellExecute = false });
            // Explicit update exit must bypass the X-to-tray cancellation;
            // otherwise the update agent waits indefinitely for this process.
            if (System.Windows.Application.Current is App app)
                app.ExitApplication();
            else
                System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or
            System.Text.Json.JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusLabel.Text = "Ошибка обновления: " + ex.Message;
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
            UpdateProgressBar.Visibility = Visibility.Collapsed;
        }
    }
}
