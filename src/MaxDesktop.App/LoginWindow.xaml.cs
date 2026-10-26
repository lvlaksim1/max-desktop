using System.Windows;
using System.ComponentModel;
using MaxDesktop.Protocol;

namespace MaxDesktop.App;

public partial class LoginWindow : Window
{
    private readonly IMaxAuthenticationTransport _transport;
    private bool _busy;
    private bool _authenticated;
    private CancellationTokenSource? _operationCancel;

    public LoginWindow(IMaxAuthenticationTransport transport)
    {
        InitializeComponent();
        _transport = transport;
    }

    private async void SendCode_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        Busy(true);
        StatusText.Text = "Подключение к MAX и запрос кода...";
        try
        {
            var result = await _transport.BeginLoginAsync(PhoneBox.Text.Trim(), _operationCancel!.Token);
            ShowResult(result);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка входа: " + ex.GetType().Name;
        }
        finally { Busy(false); }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        Busy(true);
        StatusText.Text = "Проверка TCP, TLS и начального соединения без номера телефона...";
        try
        {
            var result = await _transport.ProbeConnectionAsync(_operationCancel!.Token);
            StatusText.Text = result.Error ?? "Проверка завершена.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка диагностики: " + ex.GetType().Name;
        }
        finally { Busy(false); }
    }

    private async void VerifyCode_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        Busy(true);
        StatusText.Text = "Проверка кода...";
        try
        {
            string code = CodeBox.Password;
            CodeBox.Clear();
            ShowResult(await _transport.VerifyCodeAsync(code, _operationCancel!.Token));
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка проверки: " + ex.GetType().Name;
        }
        finally { Busy(false); }
    }

    private async void VerifyPassword_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        Busy(true);
        StatusText.Text = "Проверка дополнительного пароля...";
        try
        {
            string password = PasswordBox.Password;
            PasswordBox.Clear();
            ShowResult(await _transport.VerifyPasswordAsync(password, _operationCancel!.Token));
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка проверки: " + ex.GetType().Name;
        }
        finally { Busy(false); }
    }

    private void ShowResult(AuthResult result)
    {
        if (result.State == ConnectionState.Connected)
        {
            StatusText.Text = "Сеанс MAX подтверждён сервером.";
            _authenticated = true;
            DialogResult = true;
            return;
        }
        if (result.State == ConnectionState.AwaitingVerification)
        {
            CodeSection.Visibility = Visibility.Visible;
            PasswordSection.Visibility = Visibility.Collapsed;
            CodeHint.Text = result.CodeLength is int length && length > 0
                ? $"Код подтверждения ({length} цифр)" : "Код подтверждения";
            StatusText.Text = "Запрос принят. Введите код, полученный от MAX.";
            CodeBox.Focus();
            return;
        }
        if (result.State == ConnectionState.AwaitingPassword)
        {
            CodeSection.Visibility = Visibility.Collapsed;
            PasswordSection.Visibility = Visibility.Visible;
            PasswordHint.Text = result.PasswordHint ?? "Укажите дополнительный пароль аккаунта.";
            StatusText.Text = result.Error ?? "Введите дополнительный пароль.";
            PasswordBox.Focus();
            return;
        }
        StatusText.Text = result.Error ?? "Сервер не подтвердил авторизацию.";
    }

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Only the user-visible diagnostic message is copied.
            // Do not include phone, SMS code, password or session storage.
            System.Windows.Clipboard.SetText(StatusText.Text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            StatusText.Text = "Не удалось скопировать сообщение в буфер обмена.";
        }
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy) return;
        _operationCancel?.Cancel();
        CancelOperationButton.IsEnabled = false;
        StatusText.Text = "Отмена подключения...";
    }

    private void Busy(bool state)
    {
        if (state)
        {
            _operationCancel?.Dispose();
            _operationCancel = new CancellationTokenSource();
        }
        else
        {
            _operationCancel?.Dispose();
            _operationCancel = null;
        }
        CancelOperationButton.Visibility = state ? Visibility.Visible : Visibility.Collapsed;
        CancelOperationButton.IsEnabled = state;
        _busy = state;
        SendCodeButton.IsEnabled = !state;
        TestConnectionButton.IsEnabled = !state;
        VerifyCodeButton.IsEnabled = !state;
        VerifyPasswordButton.IsEnabled = !state;
        PhoneBox.IsEnabled = !state;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_busy && !_authenticated) e.Cancel = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy) Close();
    }
}
