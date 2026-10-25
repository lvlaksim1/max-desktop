# MAX Desktop

Нативный клиент MAX для Windows 10/11 на C# / .NET 8 / WPF.

## Реализовано в исходном каркасе 0.1.0

- Главное трёхпанельное окно (аккаунты, чаты, переписка), изменяемые разделители.
- Темы оформления, восемь основных цветов, сохранение размеров и положения окон.
- Одно запущенное приложение, значок в области уведомлений, отдельные настройки.
- Изолированная библиотека протокола MAX с проверками, не позволяющими имитировать успешное подключение.
- Проверка новых GitHub Releases, загрузка обновления с проверкой SHA-256 и отдельный агент установки.
- Полный и обновляющий установщики Inno Setup без прав администратора.
- Автоматизированная Windows-сборка, проверка протокольной границы, выпуск установщиков и политика хранения.

**Сетевой вход, чаты и обмен сообщениями ещё не реализованы.** В версии 0.1.0 готовится рабочий интерфейс и механизмы установки и обновления, а не полноценный мессенджер. Исследование серверного протокола — в [max-android-research](https://github.com/lvlaksim1/max-android-research).

## Код и сборка

- `src/MaxDesktop.App` — нативное WPF-приложение.
- `src/MaxDesktop.Protocol` — интерфейсы и изолированный транспорт.
- `src/MaxDesktop.UpdateAgent` — запуск установщика после закрытия программы.
- `installer/full.iss`, `installer/update.iss` — сценарии Inno Setup.
- `tests/MaxDesktop.Protocol.Smoke` — минимальные автоматические проверки.
- [Архитектура](docs/architecture-baseline.md) · [Примечания по реализации](docs/implementation-notes.md).

Сборка Windows: `dotnet build src/MaxDesktop.App/MaxDesktop.App.csproj -c Release`. Для первой установки применяется `MaxDesktop_Setup_vX.Y.Z.exe`, для обновления — `MaxDesktop_Update_vX.Y.Z.exe`. Готовые выпуски доступны в [GitHub Releases](https://github.com/lvlaksim1/max-desktop/releases) после успешной проверки.

Двоичные сборки не сохраняются в истории Git или в артефактах GitHub Actions. Установлен профиль Project Manager v2 через [Repo Factory](https://github.com/lvlaksim1/repo-factory).
