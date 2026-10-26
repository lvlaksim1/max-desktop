# Текущее состояние MAX Desktop — 26.10.2026

- Репозиторий: `lvlaksim1/max-desktop` (public, main).
- Последний релиз: [v0.1.4](https://github.com/lvlaksim1/max-desktop/releases/tag/v0.1.4), файлы `MaxDesktop_Setup_v0.1.4.exe`, `MaxDesktop_Update_v0.1.4.exe` и `.sha256`.
- Windows CI: [40247220377](https://github.com/lvlaksim1/max-desktop/actions/runs/40247220377) success; release pipeline [40247380634](https://github.com/lvlaksim1/max-desktop/actions/runs/40247380634) success.
- Ранее подтверждены корректность пользовательского интерфейса за исключением исправленных тем; авторизация MAX на рабочем сервере не принята.
- Последнее сообщение пользователя: v0.1.3 показывает `Этап TLS_HANDSHAKE: соединение закрыто удалённым узлом (EOF)` при запросе кода.
- GitHub Windows [40246937237](https://github.com/lvlaksim1/max-desktop/actions/runs/40246937237): `BundledMaxRoot` успешен и с системным TLS, и с явным TLS 1.2/1.3. Проблема пользователя **не воспроизведена** в этой среде.
- Изменения v0.1.4: системный TLS, ограниченные альтернативные IP через DNS с паузой ≥5 с, обязательная проверка имени и сертификата MAX, окно входа с `Проверить соединение (без номера)`, более точная диагностика обрыва.
- Интеграционный результат на компьютере владельца **не получен**. Серверные `AUTH_REQUEST/AUTH/LOGIN`, SMS и переписки остаются не подтверждены.
