using System.Windows;
using MaxDesktop.Protocol;

namespace MaxDesktop.App;

public partial class LoginWindow : Window
{
    private readonly IMaxAuthenticationTransport _transport;
    private bool _busy;

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
            var result = await _transport.BeginLoginAsync(PhoneBox.Text.Trim());
            ShowResult(result);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка входа: " + ex.GetType().Name;
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
            ShowResult(await _transport.VerifyCodeAsync(code));
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
            ShowResult(await _transport.VerifyPasswordAsync(password));
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

    private void Busy(bool state)
    {
        _busy = state;
        SendCodeButton.IsEnabled = !state;
        VerifyCodeButton.IsEnabled = !state;
        VerifyPasswordButton.IsEnabled = !state;
        PhoneBox.IsEnabled = !state;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy) Close();
    }
}
