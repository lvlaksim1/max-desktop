# Архитектура MAX Desktop — фактическое состояние v0.1.8 (26.10.2026)

Базовое проектирование: `docs/architecture-baseline.md`; подробности авторизации: `docs/protocol/authorization-26.35.1.md`, `docs/protocol/websocket-sms-2026-10-26.md`, `docs/protocol/chromium-websocket-2026-10-26.md`, `docs/protocol/network-root-cause-2026-10-26.md`.

- `src/MaxDesktop.App`: WPF-окна, темы и настройки, `LoginWindow`, `MainWindow`, трей, однократный экземпляр через Mutex и сигнал активации, восстановление существующего окна, закрытие X→трей / «−»→панель задач. `ProtectedMaxSessionStore` использует DPAPI для сохранения токенов и идентификаторов.
- `src/MaxDesktop.Protocol`: опытный бинарный TCP/TLS-транспорт (пакеты MessagePack, LZ4, отпечаток Android APK 26.35.1, TLS-проверка) и WEB JSON v11 (`MaxWebSocketCodec`, `NativeMaxWebSocketTransport`, `HybridMaxTransport`). В WEB используются команды INIT, запрос SMS, код, 2FA, LOGIN. QR не реализован.
- `DotNetMaxWebSocketChannel`: WEB-протокол по системному .NET WebSocket. `EdgeMaxWebSocketChannel`: тот же WEB-протокол через изолированную локальную страницу WebView2/Chromium. Переключение только до возможной отправки SMS, не дублировать коды.
- `src/MaxDesktop.UpdateAgent`: отдельный процесс обновления, явное завершение приложения, сохранение пользовательских данных.
- `installer/full.iss`, `installer/update.iss`: установка и обновление для текущего пользователя в `%LOCALAPPDATA%\Programs\MaxDesktop`. Настройки/данные в `%LOCALAPPDATA%\MaxDesktop`.
- `tests/` и `.github/workflows/`: Windows CI, проверка кадрирования TCP, WEB JSON, имитация SMS, отдельная анонимная проверка доступности WebSocket, проверки обновителя. GitHub Release хранит только последний Setup/Update/SHA-256.
- **Ограничения фактической приёмки:** TLS/WS не работает с машины пользователя даже в обычном браузере и `curl.exe`; реальная отправка SMS и LOGIN не приняты, чаты и сообщения не интегрированы. CI из других сетей не доказывает работоспособность.
