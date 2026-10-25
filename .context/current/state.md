# Состояние MAX Desktop на 26.10.2026

Репозиторий: https://github.com/lvlaksim1/max-desktop, ветка `main`.
Текущий выпуск: **v0.1.2** — https://github.com/lvlaksim1/max-desktop/releases/tag/v0.1.2.
Windows CI: **40243527019 = success**. Выпуск установщиков: **40243526991 = success**. Политика хранения: success.

## Исправление обновления
- Пользователь сообщил ошибку обновления v0.1.1: `The process cannot access the file because it is being used by another process`.
- Причина установлена в коде: `using var input = File.OpenRead(temporaryPath)` продолжал удерживать файл при вызове `File.Move`.
- Добавлен `MaxDesktop.UpdateCore.VerifiedInstaller`; поток закрывается до переименования.
- Новый тест `tests/MaxDesktop.Update.Smoke` прошёл Windows CI: успешный перенос, замена, отклонение неправильной SHA-256.
- v0.1.2 содержит `MaxDesktop_Setup_v0.1.2.exe`, `MaxDesktop_Update_v0.1.2.exe` и `.sha256`.
- Для перехода v0.1.1 → v0.1.2 необходим **ручной запуск установщика обновления**; встроенный механизм v0.1.1 не может сам получить исправление из-за собственного дефекта.

## Ограничения
- Успешный реальный запуск обновления пользователем пока не проверен.
- На снимке присутствует отдельное сообщение `ProtocolException`; источник не установлен.
- Протокол входа MAX на реальном аккаунте не подтверждён; чаты и сообщения не реализованы.
