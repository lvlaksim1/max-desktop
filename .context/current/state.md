# Состояние MAX Desktop — 26.10.2026

- Репозиторий: https://github.com/lvlaksim1/max-desktop (main).
- **Последний выпуск v0.1.8**: https://github.com/lvlaksim1/max-desktop/releases/tag/v0.1.8. Setup, Update, SHA-256 присутствуют; предыдущие выпуски удалены.
- Windows CI: [40255959364](https://github.com/lvlaksim1/max-desktop/actions/runs/40255959364) success. Release workflow: [40255959472](https://github.com/lvlaksim1/max-desktop/actions/runs/40255959472) success, проверено наличие Resources/max-bridge.html в publish.
- Независимая анонимная проверка Chromium WebSocket + INIT: [40255463057](https://github.com/lvlaksim1/max-desktop/actions/runs/40255463057) success. Функциональные офлайн-тесты имитируют SMS-код/LOGIN и отсутствие повторной отправки на неопределённом ответе.
- В v0.1.8 реализован выбор «Автоматически (сначала Microsoft Edge)», «WebSocket через браузер Edge», «WebSocket через .NET», «TCP». Chromium WebView2 подключается через изолированную локальную страницу с CSP; прокси/сертификаты контролируются браузером. QR не используется.
- Сохранено шифрованное хранилище DPAPI и все прежние типы сеансов; браузерный WEB_EDGE сохраняется отдельно.
- Последние пользовательские скриншоты v0.1.7: TCP = TLS_HANDSHAKE/EOF до сертификата; WebSocket .NET = WS_CONNECT/WebSocketException. Проверка была без номера; причина сбоев на компьютере не установлена.
- Интерфейс с X→трей, «−»→панель задач, меню «Выход» и одним экземпляром сохраняется в v0.1.8.
- **Пользовательской приёмки v0.1.8 нет**: WebView2 в его сети, SMS, 2FA, LOGIN, переписка и поведение трея пока не подтверждены.
- Спецификация: docs/protocol/chromium-websocket-2026-10-26.md.

## Новые данные: пользователь v0.1.8
Четыре режима из сети владельца не достигают даже анонимного INIT. TCP — EOF до сертификата, .NET WS — TLS/EOF, Edge WS — error при соединении. Само приложение не доказано единственной причиной: все способы зависят от *.oneme.ru. Неизвестно, доступен ли этот домен из обычного браузера на той же машине. Подготовлен анализ docs/protocol/network-root-cause-2026-10-26.md. v0.1.8 остаётся последним релизом; **новый установщик не выпускать без независимой локальной проверки**.
