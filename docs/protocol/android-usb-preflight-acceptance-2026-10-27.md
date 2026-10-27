# MAX Desktop — приёмка подготовительной USB-проверки на физическом Android
Дата: 27.10.2026. Источник: прямой вывод владельца из Windows CMD, команда `scripts/android-usb-preflight.cmd` на собственном Android с установленным оригинальным MAX.

## Полученные признаки
- ADB устройство: **авторизовано**, состояние `device` подтверждено ранее; USB round-trip искусственной строки `MAX_USB_BRIDGE_SMOKE_OK`: **PASS**.
- `ro.product.model = 23049PCD8G`, `ro.build.version.release = 15`.
- Приложение `ru.oneme.app`, `versionName = 26.36.2`, `versionCode = 6879`.
- `settings get global mobile_data = 1` (разрешено использование мобильных данных).
- `settings get global wifi_on = 0` (Wi-Fi выключен).
- `getprop gsm.network.type = LTE,LTE`.
- Важная оговорка: эти свойства **не доказывают** ни `NET_CAPABILITY_INTERNET` у конкретной активной `TRANSPORT_CELLULAR` сети, ни прохождение операторского запроса, ни доверенную привязку Windows.

## Что принято
- **Начальная проверка USB-взаимодействия Windows ↔ Android на искусственных данных завершена успешно.** На Android ничего не устанавливалось и не изменялось; рабочие авторизации и доверенная привязка оригинального MAX сохранены.
- План не следует помечать как «компонент Android и серверное подтверждение реализованы»: выполнен только транспортный контроль локальной связи, не разрабатывалось отдельное Android-приложение.

## Что ещё не установлено
- Был ли вызов `WebAppVerifyMobileId` в успешном просмотре кода на уже ранее доверенном физическом Android. Владелец подтвердил, что коды на нём просматривались до эксперимента; поэтому последний просмотр не доказывает новую привязку.
- Может ли отдельный настольный MAX-сеанс получить серверное подтверждение с реального Android.
- Может ли мини-приложение на Windows попасть в штатную серверную SMS-ветку.
- Необходимое условие Android (`TRANSPORT_CELLULAR` + `NET_CAPABILITY_INTERNET`) пока не проверено непосредственно через список текущих сетевых возможностей `ConnectivityManager`.

## Следующий содержательный эксперимент
Проверить реальную **текущую** сеть Android с `TRANSPORT_CELLULAR` и `NET_CAPABILITY_INTERNET`, которая соответствует запросу оригинального APK `new NetworkRequest.Builder().addTransportType(0).addCapability(12)`. Источник: lvlaksim1/max-android-research/docs/mobileid-bridge-trace-26.35.1.md.

Android `dumpsys connectivity` может отображать `NetworkAgentInfo` с `nc{ Transports: CELLULAR Capabilities: INTERNET...`; отображение зависит от версии прошивки. Фильтрация должна проверять строку активной сети, а не соседний `NetworkRequest`. Отсутствие текстового шаблона **не является доказательством отсутствия сотового транспорта**. Официальные определения: https://developer.android.com/reference/android/net/NetworkCapabilities и https://developer.android.com/tools/dumpsys.

**Не делать:** не сбрасывать уже действующее доверенное устройство, не вытаскивать или пересылать коды/секреты, не подделывать ответ оператора, не пытаться обойти защиту снимка экрана. Новый релиз Windows не нужен для этого шага.
