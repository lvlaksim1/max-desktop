# MAX Desktop — 27.10.2026, выпуск v0.1.23
- Пользователь показал, что в v0.1.22 мини-приложение оставалось на стадии «Проверяем номер телефона». Поля ввода номера там нет: это автоматическая Mobile ID проверка, а не запрос номера у пользователя.
- Установлено: номер аккаунта уже есть в сохранённом MAX сеансе (MaxSavedSession.Phone); WebAppVerifyMobileIdRequest оригинального APK содержит requestId,url и не принимает номер как поле. Номер в UI не равен подтверждению физической SIM.
- Исправлена конкретная ошибка v0.1.22: публичный WebViewHandler.postEvent игнорировал события, теперь защищённая отрицательная ветка WebAppVerifyMobileId обрабатывается и из публичного, и из PrivateWebViewHandler. JS stage ready/request/response shown in window. Account number passed explicitly from MainWindow to MiniAppWindow.
- Выпущена v0.1.23: GitHub Release https://github.com/lvlaksim1/max-desktop/releases/tag/v0.1.23 ; windows CI 40421564693 successful; installers workflow 40421564642 successful. Реальная СМС-форма и код не проверены пользователем.
- Сессии, системный VPN, операторский Mobile ID не менялись. SMS-проверка MAX по справке может зависеть от включённой 2FA и вида кода.
