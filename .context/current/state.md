# Текущее состояние MAX Desktop — 26.10.2026

- Публичный репозиторий `lvlaksim1/max-desktop`, основная ветка `main`.
- Последний выпуск: [v0.1.5](https://github.com/lvlaksim1/max-desktop/releases/tag/v0.1.5), `MaxDesktop_Setup_v0.1.5.exe`, `MaxDesktop_Update_v0.1.5.exe`, `.sha256`.
- Подтверждённая Windows-сборка: [40248715602](https://github.com/lvlaksim1/max-desktop/actions/runs/40248715602), success. Выпуск: [40248874326](https://github.com/lvlaksim1/max-desktop/actions/runs/40248874326), success.
- Последний пользовательский снимок: v0.1.4, кнопка проверки без номера, все 3 соединения к `api2.oneme.ru` обрываются на TLS_HANDSHAKE до предъявления сертификата. Номер, пароль, код не передавались.
- Проверены на GitHub Windows [40248116311](https://github.com/lvlaksim1/max-desktop/actions/runs/40248116311): `api2.oneme.ru`, `api.oneme.ru`, `api-tg.oneme.ru`; для каждого TLS 1.3 + анонимный SESSION_INIT (opcode 6, callsSeed).
- С версии v0.1.5 предусмотрены разные узлы с проверенным TLS/SNI (до 6 попыток, пауза 5 с), обезличенная диагностика по узлам, кнопка отмены. Успех через эти узлы в пользовательской сети ещё не подтверждён.
- Реальные AUTH_REQUEST/AUTH/LOGIN и переписки/сообщения не приняты; интерфейс и установка Windows работают, протокол экспериментален.
