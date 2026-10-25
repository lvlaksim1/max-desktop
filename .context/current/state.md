# Состояние MAX Desktop на 26.10.2026

- Репозиторий: https://github.com/lvlaksim1/max-desktop (public, main).
- Архитектура: Windows .NET 8 / WPF, отдельные `MaxDesktop.App`, `MaxDesktop.Protocol`, `MaxDesktop.UpdateAgent`.
- Интерфейс: главное трёхпанельное окно, настройки, светлая/тёмная/системная темы, восемь цветов, изменяемые разделители, сохранение состояния, значок области уведомлений.
- Обновление: проверка GitHub Releases, загрузка, проверка SHA-256, запуск отдельного установщика после закрытия приложения.
- Инсталляция: Inno Setup без администраторских прав, отдельные Setup/Update.
- Проверки: Windows CI `40238392001` = success; выпуск `40238615264` = success.
- Выпуск: https://github.com/lvlaksim1/max-desktop/releases/tag/v0.1.0. Файлы: `MaxDesktop_Setup_v0.1.0.exe`, `MaxDesktop_Update_v0.1.0.exe`, `MaxDesktop_Update_v0.1.0.exe.sha256`.
- **Не подтверждено:** успешная установка/запуск на компьютере владельца, обновление с 0.1.0 до более новой версии, любая сетевая операция MAX.
