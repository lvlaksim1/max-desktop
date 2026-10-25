# Архитектура MAX Desktop

Базовый документ: docs/architecture-baseline.md. Текущий технический этап — опытная авторизация v0.1.1.

- src/MaxDesktop.App: нативные WPF-окна, настройки, темы, однократный экземпляр, значок области уведомлений, обновление. LoginWindow запрашивает номер, код и дополнительный пароль; ProtectedMaxSessionStore шифрует сеанс DPAPI.
- src/MaxDesktop.Protocol: отдельный от WPF протокол (IMaxTransport, IMaxAuthenticationTransport), TCP/TLS с системной проверкой сертификата, MessagePack, LZ4, 10-байтовое кадрирование, отпечаток APK 26.35.1, SESSION_INIT, AUTH_REQUEST, AUTH, LOGIN. UnavailableMaxTransport сохранён только как явно отказывающая тестовая заглушка; главное окно использует NativeMaxTransport.
- src/MaxDesktop.UpdateAgent: проверка и установка обновлений вне процесса программы.
- installer/full.iss, installer/update.iss: первая установка и обновление.
- tests/MaxDesktop.Protocol.Smoke: автономные проверки MessagePack, фреймов, LZ4, отпечатка, ошибочных входных данных.
- .github/workflows/ci.yml: Windows компиляция и тесты.
- .github/workflows/release.yml: публикация Setup, Update, SHA-256, очистка старых выпусков.

Каталог установки: %LOCALAPPDATA%\Programs\MaxDesktop; изменяемые данные: %LOCALAPPDATA%\MaxDesktop.
Реальная успешная авторизация MAX, чаты и сообщения ещё не подтверждены. Сетевой протокол и его границы: docs/protocol/authorization-26.35.1.md.
