# MAX Desktop — определение номера SIM и Mobile ID в оригинальном APK
Дата: 2026-10-27. Исходник: `lvlaksim1/max-android-research`, `base.apk` MAX 26.35.1 (6867), SHA256 `e809176d6bf6ff69247274433ec170a5db8a5687a02a04299a78a2ae978119fc`.

## Полная статическая проверка
Исследованы 24 272 декомпилированных Java-класса оригинального APK, запущено на GitHub Actions. Детальный отчёт:
https://github.com/lvlaksim1/max-android-research/blob/main/docs/telephony-api-audit-26.35.1.md

Обращения к `TelephonyManager.getLine1Number`, `SubscriptionManager`, `getSimSerialNumber`, `getSubscriberId` — **не обнаружены** в результатах JADX.
`getNetworkOperator()` — 4 строки, `getSimOperator()` — 2 строки. Эти методы возвращают данные оператора, не MSISDN.
Отдельно в APK найден `WebAppVerifyMobileId` (создание вызова, обработка и модель `VerifyMobileIdResponseInfo`). Это свидетельствует о наличии специального механизма подтверждения через Mobile ID, но не доказывает точную последовательность вызовов на экране без динамической проверки.

Исследование не изменяло APK, не подключалось к аккаунтам и не получало кодов.

## Состояние MAX Desktop
- В текущем `MaxSavedSession` есть `Phone`, подтверждённый при авторизации. Само наличие номера не равно подтверждению, что эта SIM установлена и обслуживается оператором на текущем устройстве.
- Уже работает TCP(Android) и серверный BOT_WEB_APP URL с платформой android, однако бот «Коды подтверждения» требует привязки устройства.
- Подстановка поля номера в WebView2, User-Agent, WebAppData или запрос Mobile ID не способна легитимно доказать владение физической SIM. Подмену проверки/ответа не реализовывать.
- Официальная Android справка отдельно предупреждает, что значение номера из SIM может быть пустым или ненадёжным для задач безопасности: https://developer.android.com/reference/android/telephony/SubscriptionManager .
- Официальная проверка MAX через Mobile ID и варианты со СМС описаны в https://help.max.ru/help/gosuslugi/svyazka_gosuslug_s_max/kak_smenit_doverennoe_ustroistvo_dlya_vhoda_v_gosuslugi .

## Дальнейшее
Для защищённых кодов — использовать проверку доверенного устройства/операторское подтверждение по официальной процедуре на устройстве с настоящей SIM. Прямая поддержка кодов на ПК требует разрешённого MAX способа привязки настольного устройства; пока он не подтверждён.

Рабочие сеансы MAX Desktop, системный VPN и релиз v0.1.21 в данном исследовании не менялись.
