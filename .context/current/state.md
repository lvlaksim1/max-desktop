# Состояние MAX Desktop — 26.10.2026

- Репозиторий: https://github.com/lvlaksim1/max-desktop (`main`).
- **Актуальный релиз v0.1.7**: https://github.com/lvlaksim1/max-desktop/releases/tag/v0.1.7. Проверены Setup, Update, SHA-256; предыдущие Releases удалены.
- CI Windows [40253731349](https://github.com/lvlaksim1/max-desktop/actions/runs/40253731349) = success; Release workflow [40253731311](https://github.com/lvlaksim1/max-desktop/actions/runs/40253731311) = success.
- В независимом [40253115470](https://github.com/lvlaksim1/max-desktop/actions/runs/40253115470) подтверждён WebSocket `wss://ws-api.oneme.ru/websocket` и успешный анонимный INIT v11 без номера, пароля или токенов.
- Реализован WEB/WebSocket SMS → код → дополнительный пароль → LOGIN, хранение DPAPI с меткой транспорта; прежний TCP оставлен альтернативой. В диалоге выбираются автоматический, WebSocket, TCP. QR не используется по указанию пользователя.
- Главный X сворачивает в трей, «−» сворачивает в панель задач, из меню трея можно завершить приложение, повторный запуск активирует имеющийся экземпляр. Для обновления реализовано явное завершение.
- Ранее в пользовательской сети версии v0.1.5–v0.1.6 показывали TLS_HANDSHAKE/EOF до сертификата на всех трёх TCP-узлах, через прокси и напрямую.
- **НЕТ пользовательской приёмки v0.1.7**. В частности, не доказаны SMS/2FA/LOGIN, получение чатов и фактический GUI-жизненный цикл.
- Техническая спецификация: `docs/protocol/websocket-sms-2026-10-26.md`.
