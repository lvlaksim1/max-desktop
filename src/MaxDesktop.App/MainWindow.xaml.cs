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
    private readonly ProtectedMaxAccountVault _vault;
    private IMaxAuthenticationTransport _transport;
    private readonly ObservableCollection<string> _accounts = new();
    private bool _ignoreAccountSelection;
    private bool _accountOperation;
    private string? _currentAccountPhone;
    private readonly ObservableCollection<ChatSummary> _chats = new();
    private readonly ObservableCollection<ChatMessage> _messages = new();
    private CancellationTokenSource? _chatRefreshCancellation;
    private int _chatRefreshGeneration;
    private CancellationTokenSource? _historyCancellation;
    private int _historyGeneration;
    private bool _historyLoading;
    private bool _historyHasMore;
    private long? _historyBefore;
    private long? _historyChatId;
    private readonly HashSet<long> _historyIds = new();

    public MainWindow()
    {
        _settings = SettingsStore.Load();
        SettingsStore.ApplyTheme(_settings);
        InitializeComponent();
        _vault = new ProtectedMaxAccountVault();
        _transport = CreateTransport(null);
        AccountList.ItemsSource = _accounts;
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
        StatusLabel.Text = "Восстановление сохранённых аккаунтов MAX...";
        try
        {
            RefreshAccountList();
            string? active = _vault.ActivePhone;
            if (active is not null)
                await SwitchAccountAsync(active);
            else
                ApplyAuthResult(new AuthResult(ConnectionState.Disconnected, null));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
        {
            // Never overwrite a damaged encrypted vault with an empty one.
            StatusLabel.Text = "Не удалось прочитать защищённое хранилище: " + ex.GetType().Name;
            AddAccountButton.IsEnabled = false;
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
            _chatRefreshCancellation?.Cancel();
            _historyCancellation?.Cancel();
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

    private void MiniAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ChatActionButton action })
            return;
        if (!MaxMiniAppLink.TryResolve(action.WebApp, action.Payload, out Uri? uri) || uri is null)
        {
            StatusLabel.Text = "Неподдерживаемый адрес мини-приложения MAX.";
            return;
        }
        new MiniAppWindow(uri, action.Text) { Owner = this }.Show();
    }


    private IMaxAuthenticationTransport CreateTransport(string? phone) =>
        new HybridMaxTransport(_vault.Slot(phone),
            new EdgeMaxWebSocketChannel(MaxEdgeChannel));

    private void RefreshAccountList()
    {
        _ignoreAccountSelection = true;
        try
        {
            _accounts.Clear();
            foreach (string phone in _vault.Phones()) _accounts.Add(phone);
            AccountList.SelectedItem = _currentAccountPhone;
            RemoveAccountButton.IsEnabled = AccountList.SelectedItem is string;
        }
        finally { _ignoreAccountSelection = false; }
    }

    private void SetAccountButtons(bool enabled)
    {
        AddAccountButton.IsEnabled = enabled;
        AccountList.IsEnabled = enabled;
        RemoveAccountButton.IsEnabled = enabled && AccountList.SelectedItem is string;
    }

    private void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        if (_accountOperation) return;
        // Use a fresh transport and session slot: a failed/cancelled login
        // cannot revoke the currently active account's stored credentials.
        var candidate = CreateTransport(null);
        bool adopted = false;
        _accountOperation = true;
        SetAccountButtons(false);
        try
        {
            var dialog = new LoginWindow(candidate) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            string phone = candidate.CurrentPhone ??
                throw new InvalidOperationException("MAX не вернул номер подтверждённого аккаунта.");
            _vault.Activate(phone);
            _chatRefreshCancellation?.Cancel();
            ResetHistory();
            var previous = _transport;
            _transport = candidate;
            _currentAccountPhone = phone;
            adopted = true;
            previous.Dispose();
            RefreshAccountList();
            ApplyAuthResult(new AuthResult(ConnectionState.Connected, null));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            StatusLabel.Text = "Не удалось добавить аккаунт: " + ex.Message;
        }
        finally
        {
            if (!adopted) candidate.Dispose();
            _accountOperation = false;
            SetAccountButtons(true);
        }
    }

    private async void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreAccountSelection || _accountOperation ||
            AccountList.SelectedItem is not string phone ||
            phone == _currentAccountPhone)
            return;
        await SwitchAccountAsync(phone);
    }

    private async Task SwitchAccountAsync(string phone)
    {
        if (_accountOperation) return;
        _accountOperation = true;
        SetAccountButtons(false);
        IMaxAuthenticationTransport? next = null;
        bool adopted = false;
        try
        {
            // Authenticate the target session before disposing the working one.
            next = CreateTransport(phone);
            StatusLabel.Text = "Подключаю выбранный аккаунт MAX...";
            AuthResult result = await next.RestoreSessionAsync();
            if (result.State != ConnectionState.Connected)
            {
                StatusLabel.Text = "Не удалось подключить аккаунт: " +
                    (result.Error ?? "Сервер не подтвердил сохранённый сеанс.");
                return;
            }
            _vault.Activate(phone);
            _chatRefreshCancellation?.Cancel();
            ResetHistory();
            var previous = _transport;
            _transport = next;
            _currentAccountPhone = phone;
            adopted = true;
            previous.Dispose();
            RefreshAccountList();
            ApplyAuthResult(result);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            InvalidOperationException or System.Security.Cryptography.CryptographicException or
            ObjectDisposedException)
        {
            StatusLabel.Text = "Ошибка переключения аккаунта: " + ex.GetType().Name;
        }
        finally
        {
            if (!adopted) next?.Dispose();
            _accountOperation = false;
            RefreshAccountList();
            SetAccountButtons(true);
        }
    }

    private async void RemoveAccount_Click(object sender, RoutedEventArgs e)
    {
        if (_accountOperation || AccountList.SelectedItem is not string phone) return;
        if (System.Windows.MessageBox.Show(this,
            "Удалить выбранный аккаунт с этого компьютера? Другие аккаунты останутся сохранёнными.",
            "Удаление аккаунта MAX", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;
        _accountOperation = true;
        SetAccountButtons(false);
        string? fallback = null;
        try
        {
            bool wasActive = phone == _currentAccountPhone;
            if (wasActive)
            {
                _chatRefreshCancellation?.Cancel();
                ResetHistory();
                var previous = _transport;
                _transport = CreateTransport(null);
                _currentAccountPhone = null;
                previous.Dispose();
                ApplyAuthResult(new AuthResult(ConnectionState.Disconnected, null));
            }
            // Delete the old single-account backup only when the selected
            // account is that legacy session, so deletion is truly local.
            var legacy = new ProtectedMaxSessionStore();
            if (legacy.Load()?.Phone == phone) legacy.Delete();
            _vault.Remove(phone);
            fallback = wasActive ? _vault.ActivePhone : null;
            RefreshAccountList();
            StatusLabel.Text = "Аккаунт удалён с компьютера. Остальные сеансы сохранены.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            InvalidOperationException or UnauthorizedAccessException or
            System.Security.Cryptography.CryptographicException)
        {
            StatusLabel.Text = "Не удалось удалить аккаунт: " + ex.GetType().Name;
        }
        finally
        {
            _accountOperation = false;
            SetAccountButtons(true);
        }
        if (fallback is not null)
            await SwitchAccountAsync(fallback);
    }

    private void ApplyAuthResult(AuthResult result)
    {
        bool connected = result.State == ConnectionState.Connected;
        AccountStateText.Text = connected ? "MAX подключён" : "Не подключён";
        AccountPhoneText.Text = connected
            ? _transport.CurrentPhone ?? "Сеанс подтверждён"
            : "Нет активного сеанса";
        AddAccountButton.Content = "+ Добавить аккаунт";
        RemoveAccountButton.Visibility = Visibility.Visible;
        RemoveAccountButton.IsEnabled = AccountList.SelectedItem is string && !_accountOperation;
        ConversationHint.Text = connected
            ? "Выберите чат из списка"
            : "Выберите чат после подключения аккаунта";
        _chatRefreshCancellation?.Cancel();
        ResetHistory();
        _chats.Clear();
        _messages.Clear();
        RefreshChatsButton.IsEnabled = connected;
        StatusLabel.Text = connected
            ? "Авторизация MAX подтверждена. Загружаю чаты..."
            : result.Error ?? "Аккаунт MAX не подключён.";
        if (connected) _ = RefreshChatsAsync();
    }

    private async void RefreshChats_Click(object sender, RoutedEventArgs e)
    {
        await RefreshChatsAsync();
    }

    private async Task RefreshChatsAsync()
    {
        _chatRefreshCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _chatRefreshCancellation = cancellation;
        int generation = ++_chatRefreshGeneration;
        RefreshChatsButton.IsEnabled = false;
        StatusLabel.Text = "Загрузка чатов MAX...";
        try
        {
            var items = await _transport.GetChatsAsync(cancellation.Token);
            if (generation != _chatRefreshGeneration || cancellation.IsCancellationRequested)
                return;
            _chats.Clear();
            foreach (var chat in items) _chats.Add(chat);
            StatusLabel.Text = items.Count == 0
                ? "MAX вернул пустой список чатов."
                : $"Загружено чатов: {items.Count}.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            if (generation == _chatRefreshGeneration)
                StatusLabel.Text = "Ошибка загрузки чатов: " + ex.Message;
        }
        finally
        {
            if (generation == _chatRefreshGeneration)
            {
                RefreshChatsButton.IsEnabled = _transport.State == ConnectionState.Connected;
                _chatRefreshCancellation = null;
            }
            cancellation.Dispose();
        }
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
        ResetHistory();
        if (ChatList.SelectedItem is not ChatSummary chat)
        {
            ConversationTitle.Text = "Переписка не выбрана";
            ConversationHint.Text = "Выберите чат из списка";
            return;
        }
        if (!MaxHistoryCodec.CanOpenChat(chat.Id))
        {
            ConversationTitle.Text = chat.Title;
            ConversationHint.Text = "Служебный чат MAX (ID 0). Просмотр пока не поддерживается.";
            StatusLabel.Text = "Служебный чат 0 нельзя открыть как обычную переписку.";
            return;
        }
        _historyChatId = chat.Id;
        ConversationTitle.Text = chat.Title;
        ConversationHint.Text = "Загружаю последние сообщения...";
        await LoadHistoryPageAsync(older: false);
    }

    private void ResetHistory()
    {
        _historyCancellation?.Cancel();
        ++_historyGeneration;
        _historyLoading = false;
        _historyHasMore = false;
        _historyBefore = null;
        _historyChatId = null;
        _historyIds.Clear();
        _messages.Clear();
        if (MessageList is not null) MessageList.Visibility = Visibility.Collapsed;
        if (EmptyConversation is not null) EmptyConversation.Visibility = Visibility.Visible;
        if (LoadOlderMessagesButton is not null)
        {
            LoadOlderMessagesButton.IsEnabled = false;
            LoadOlderMessagesButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void LoadOlderMessages_Click(object sender, RoutedEventArgs e) =>
        await LoadHistoryPageAsync(older: true);

    private void MessageList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Only a genuine upward scroll triggers auto-pagination. Initial
        // layout and the first render must not drain the entire history.
        if (e.OriginalSource is ScrollViewer && e.VerticalChange < 0 &&
            e.VerticalOffset <= 4 && _historyHasMore && !_historyLoading)
            _ = LoadHistoryPageAsync(older: true);
    }

    private async Task LoadHistoryPageAsync(bool older)
    {
        if (_historyChatId is not long chatId || _historyLoading ||
            (older && (!_historyHasMore || _historyBefore is null)))
            return;

        _historyLoading = true;
        int generation = _historyGeneration;
        long? previousCursor = _historyBefore;
        var cancellation = new CancellationTokenSource();
        _historyCancellation = cancellation;
        LoadOlderMessagesButton.IsEnabled = false;
        try
        {
            ChatHistoryPage page = await _transport.GetHistoryPageAsync(
                chatId, older ? previousCursor : null,
                MaxHistoryCodec.PageSize, cancellation.Token);
            if (generation != _historyGeneration || _historyChatId != chatId ||
                cancellation.IsCancellationRequested)
                return;

            int added = 0;
            // Both transports return messages oldest-first.
            foreach (var item in page.Messages)
            {
                if (!_historyIds.Add(item.Id)) continue;
                if (older) _messages.Insert(added, item);
                else _messages.Add(item);
                added++;
            }
            _historyBefore = page.NextBefore;
            _historyHasMore = page.HasMore &&
                page.NextBefore is { } next &&
                (!older || (previousCursor.HasValue && next < previousCursor.Value)) &&
                added > 0;
            LoadOlderMessagesButton.Visibility =
                _historyHasMore ? Visibility.Visible : Visibility.Collapsed;
            MessageList.Visibility = _messages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyConversation.Visibility = _messages.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            ConversationHint.Text = _messages.Count == 0
                ? "Сервер вернул пустую историю."
                : $"Сообщений загружено: {_messages.Count}. Только чтение.";
            StatusLabel.Text = _messages.Count == 0
                ? "В выбранном чате сервер не вернул сообщений."
                : $"История загружена: {_messages.Count} сообщений.";
            if (!older && _messages.Count > 0)
                MessageList.ScrollIntoView(_messages[^1]);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            InvalidOperationException or TimeoutException or OperationCanceledException or
            NotSupportedException or ArgumentException)
        {
            if (generation == _historyGeneration && _historyChatId == chatId)
            {
                ConversationHint.Text = "Ошибка загрузки истории. Выберите чат ещё раз.";
                StatusLabel.Text = "Ошибка истории: " + ex.Message;
            }
        }
        finally
        {
            if (generation == _historyGeneration)
            {
                _historyLoading = false;
                LoadOlderMessagesButton.IsEnabled = _historyHasMore;
                if (ReferenceEquals(_historyCancellation, cancellation))
                    _historyCancellation = null;
            }
            cancellation.Dispose();
        }
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
