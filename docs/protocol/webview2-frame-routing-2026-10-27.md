# MAX Desktop — маршрутизация сообщений основного документа и вложенных кадров WebView2
Дата: 27.10.2026. Статус: **исправлены исходники; Windows CI пройден; реальный экран защищённых кодов не принят**.

## Доказанный дефект прежней архитектуры
Официальный WebView2 разделяет события `CoreWebView2.WebMessageReceived` (главный документ) и `CoreWebView2Frame.WebMessageReceived` (дочерний документ). В MAX Desktop v0.1.23 был только первый обработчик. Простое внедрение JavaScript в iframe НЕ обеспечивало получение событий на стороне C#: нужно подписаться на событие каждого кадра.

Дополнительно прежний сценарий имел `if (window !== window.top || location.origin !== SIGNED_ORIGIN) return`; это исключало все iframe, даже если они совпадали по происхождению с подписанным запуском. Оригинальный Android `addJavascriptInterface` доступен во всех кадрах, но `PrivateWebViewHandler` в APK регистрируется условно (`ekl.Z`). Публичная JS-библиотека MAX при работе в iframe использует `parent.postMessage`; это не доказывает, что закрытый бот посылает приватный `WebAppVerifyMobileId` именно таким способом.

Источники:
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.webmessagereceived
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2frame.webmessagereceived
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2frame.executescriptasync
- docs/protocol/miniapp-bridge-frame-analysis-2026-10-27.md
- lvlaksim1/max-android-research/docs/webapp-jsbridge-inspection-26.35.1.md

## Что изменено
`src/MaxDesktop.App/MiniAppWindow.cs`:
1. Встроенный сценарий доступен в верхнем документе и iframe **только при точном совпадении origin** с происхождением, извлечённым из серверного подписанного URL запуска; другие происхождения не получают `WebViewHandler`/ `PrivateWebViewHandler`.
2. JavaScript верхнего документа передаёт метку `miniapp-bridge-ready`, вложенный документ — отдельную метку `miniapp-frame-ready`, которая означает только локальное внедрение собственного сценария, не подтверждает вызов MAX или привязку.
3. C# обработчик `CoreWebView2.FrameCreated` вызывает рекурсивную регистрацию `CoreWebView2Frame.WebMessageReceived` и `CoreWebView2Frame.FrameCreated`, чтобы учитывать кадры с любой глубиной вложенности.
4. Событие из iframe проходит ту же проверку `args.Source` по `GetLeftPart(UriPartial.Authority)` и принимает только узкий формат `WebAppVerifyMobileId` с `requestId`. Никакие адреса, HTTP-данные, токены или значения кодов не сохраняются.
5. Штатный **отрицательный** ответ `client.verify_mobile_id.no_cellular` возвращается в **исходный кадр** через `CoreWebView2Frame.ExecuteScriptAsync`, а не ошибочно в главный документ через `CoreWebView2.ExecuteScriptAsync`. Подделки операторского успеха нет.
6. Диагностическая строка показывает общее количество созданных кадров, количество срабатываний сценария на том же origin, запросы Mobile ID из кадров и **логический признак совпадения происхождения** текущего главного документа — без раскрытия URL.
7. Изменены офлайновые проверки `tests/MaxDesktop.MiniAppBridge.Smoke/bridge.test.cjs`: синтетический top и iframe одного origin, отказ при чужом origin, выборочная обработка события и контроль C# маршрута.

## Автоматическая проверка
- Windows build + Node test `40437087432`: SUCCESS на коммите `252c24fdbb38963f96bf15deee96f489593c4619`. Эту проверку использовать как исходную ссылку для текущего состояния кода `MiniAppWindow.cs` и теста.
- Предыдущая сборка `40436911565` была красной из-за устаревших проверок пути обработчика в тестовом файле. Тест скорректирован и повторная сборка успешна.
- Публикация новой пользовательской версии **не выполнялась**. Настоящее мини-приложение и сервер ещё не испытаны с новой обработкой кадров.

## Проверка присутствия процесса через доверенный PC Gateway
Из подключённого Windows PC Gateway выполнены три считывающих проверки без обращения к аккаунтам и данным:
- Запрос #499 (success): `app=1; same_session=1; webview2=6`, номер версии exe по метаданным не получен.
- Запрос #500 (success исполнения, `window_check_failed`), #501 (`window_check_failed_MethodInvocationException`).
- Исправлен делегат перебора окон; #502 (success): `window_presence:visible=0;miniapp=0;same_session=True`. При этом **отсутствие видимых окон через `EnumWindows` фонового исполнителя не доказывает отсутствие UI на пользовательском рабочем столе**: оконные станции/рабочие столы могут различаться в одном сеансе.

Ни одно из этих наблюдений не означает, что в браузере был запущен новый обработчик. Рабочая установленная v0.1.23 не менялась.

## Осталось проверить
- Создаёт ли реальный бот «Коды подтверждения» iframe и его origin, использует ли его при операции.
- Доставляется ли реальный вызов `WebAppVerifyMobileId` как событие в верхнем документе или кадре.
- Поддерживает ли сервер разрешённое подтверждение через физический Android либо штатную SMS-ветку для нового Windows устройства.
- **Главный критерий:** подлинный код после принятой сервером проверки отображён в MAX Desktop. Пока не выполнен.

Не сбрасывать доверие оригинального Android, не подменять операторский ответ, не обходить запрет создания снимка защищённого экрана, не выпускать непроверенные функциональные версии.
