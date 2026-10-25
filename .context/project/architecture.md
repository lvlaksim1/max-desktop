# Архитектура MAX Desktop

Базовый документ продукта: `docs/architecture-baseline.md`.

- `src/MaxDesktop.App`: WPF-окна, темы, настройки, однократный экземпляр, значок области уведомлений и пользовательская проверка обновлений.
- `src/MaxDesktop.Protocol`: предметные модели и интерфейс транспорта MAX независимо от WPF. Пока используется `UnavailableMaxTransport`: все сетевые операции отказывают или возвращают пустые реальные коллекции.
- `src/MaxDesktop.UpdateAgent`: проверка установщика после выхода основного процесса и запуск Inno Setup.
- `installer/full.iss`, `installer/update.iss`: первая установка и обновление поверх текущего приложения.
- `tests/MaxDesktop.Protocol.Smoke`: защита от ложной успешной авторизации и имитации данных.
- `.github/workflows/ci.yml`: Windows-сборка и автоматическая проверка.
- `.github/workflows/release.yml`: самостоятельная публикация Windows x64 и двух установщиков.

Данные пользователя: `%LOCALAPPDATA%\MaxDesktop`; файлы установки: `%LOCALAPPDATA%\Programs\MaxDesktop`. Инвариант: изменяемые данные не должны попадать в каталог установки.
