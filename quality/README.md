# Локальный контроль качества MAX Desktop

Проверки: подготовленность ресурсов VK CAPTCHA, сборка WPF,
синтетический протокол, имитация WebSocket, тест обновления
в отдельном временном каталоге и карта архитектуры .NET.
Реальная авторизация, SMS и сетевые проверки не запускаются.

Из чистого checkout перед первой сборкой требуется подготовить SDK:

```powershell
python scripts/prepare_captcha_resources.py --prepare
python scripts/quality_runner.py --profile full
```

Команда подготовки скачивает закреплённые npm-зависимости из package.json,
генерирует JS/CSS локально и выполняет тест callback. Готовые файлы
не включаются в историю Git, но нужны для сборки WPF.
Повторная обычная проверка `python scripts/prepare_captcha_resources.py`
только читает состояние и не делает сетевых запросов.

Отчёт: `quality/reports/quality-full-*.json`.
Карта: `quality/reports/architecture.html`.
