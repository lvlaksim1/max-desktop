# MAX Desktop — передача менеджера, поколение 36, 27.10.2026

Менеджер project-manager, репозиторий lvlaksim1/max-desktop/main. Восстановление через .context/ENTRYPOINT.md и контроль state-integrity.json 8/8.

Задача владельца: реальный код из бота «Коды подтверждения» прямо в Windows MAX Desktop. Официальная справка MAX подтверждает SMS-путь для привязки нового доверенного устройства с 2FA в сценарии Госуслуг. Пароль 2FA устанавливают в мобильном MAX; SMS на вход в MAX Desktop уже работала и не равна SMS привязки.

Исследован исходный Android MAX 26.35.1: JavaScript мосты WebViewHandler и PrivateWebViewHandler с postEvent(String,String); original bridge error response client.verify_mobile_id.no_cellular, JSON {requestId,error:{code}}. Публичный SDK max-web-app.js принимает через window.WebApp.sendEvent(name,json). Источники lvlaksim1/max-android-research docs/webapp-jsbridge-inspection-26.35.1.md и docs/public-max-bridge-js-sms-2026-10-27.md.

Эксперимент v0.1.22 в MAX Desktop: MiniAppWindow.cs добавляет WebMessage-enabled scoped bridge для top-level HTTPS origin выданного MAX адреса и отвечает на WebAppVerifyMobileId штатным no_cellular; MaxMiniAppCellularUnavailable.cs чистая модель/валидация; tests/MaxDesktop.Protocol.Smoke. Нет SIM spoofing, нет SMS endpoint guessing, нет токенов/секретов в журнале. Windows build прогон завершён успешно, но появление формы SMS и реального кода в клиенте пока не проверено владельцем. Последняя готовность/публикация проверять по GitHub release, рабочие сеансы и VPN не менялись.
