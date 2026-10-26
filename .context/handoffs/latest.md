# Передача MAX Desktop, 26.10.2026 — v0.1.9

Идентичность: прежний manager_id=project-manager, repo lvlaksim1/max-desktop, ветки main; вход восстановления .context/ENTRYPOINT.md. Обновлена капсула после серии подтверждённых экспериментов.

Приоритетное требование владельца: системный VPN **не отключать и не перенастраивать**; трафик MAX может идти любым маршрутом, включая вне VPN.

Доказательства: PC Runner Gateway Issue #399 — при включённом VPN curl с физической привязкой IPv4 получает ws-api.oneme.ru HTTP 403 и web.max.ru HTTP 200, обычный путь даёт ошибку 35. Issue #400 — полноценные проверенный TLS и WebSocket HTTP 101 на локальном ПК. Issue #402 — один подходящий Ethernet/WiFi IPv4 с шлюзом. Issue #401 — локальный dotnet CLI отсутствует, поэтому самостоятельный C#-пробник из исходников на ПК не запускался. GitHub Actions 40322276206 — новый PhysicalMaxWebSocketChannel C# получил реальный анонимный SESSION_INIT, CI 40322590907 успешен.

Реализация: .NET 8/WPF; PhysicalMaxWebSocketChannel использует SocketsHttpHandler.ConnectCallback, Socket.Bind к физическому интерфейсу, UseProxy=false и строгую TLS-проверку. Автоматически выбирается Physical → Edge → .NET → TCP с безопасностью одного SMS. QR не использовать.

Выпуск: v0.1.9, https://github.com/lvlaksim1/max-desktop/releases/tag/v0.1.9 , успешный workflow 40323017999, Setup/Update/SHA-256, старый двоичный выпуск удалён.

Следующее действие: пользовательское обновление и на ПК владельца «Добавить аккаунт» → «Проверить соединение (без номера)», добиться SESSION_INIT при включённом VPN. Затем один SMS→код→2FA при необходимости→LOGIN. Реальная авторизация, чаты/сообщения и GUI-приёмка пока не подтверждены.
