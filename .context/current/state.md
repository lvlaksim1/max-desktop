# MAX Desktop — 27.10.2026, завершена статическая трассировка Mobile ID

Последний выпущенный MAX Desktop v0.1.21, manager project-manager, repo lvlaksim1/max-desktop/main. После повторного входа у пользователя TCP(Android)/физическая сеть и server-signed WebAppPlatform=android; мини-приложение «Коды подтверждения» открывается, затем требует доверенную привязку устройства с SIM и мобильным интернетом.

Точная сборка MAX Android 26.35.1 (6867), SHA256 e809176d..., декомпилирована целиком (24 272 Java файлов, MAX research CI 40409450577 success). Обнаружен приватный `WebAppVerifyMobileId`: requestId,url -> `dol` -> `ekl` `ConnectivityManager.requestNetwork(TRANSPORT_CELLULAR,INTERNET)` -> `fcl` сотовый HTTP-клиент -> code/headers/Base64-data -> приватный JS event. Ошибки no_cellular/cancelled. `getLine1Number`/`SubscriptionManager` не найдены; номер в запросе отсутствует. В MAX Desktop телефон уже есть в сохранённой сессии, но это не подтверждение реальной SIM.

Текущий WebView2 не содержит приватного Mobile ID bridge и не имеет Android/сотовой сети; нельзя выдать ПК за доверенное устройство изменением UI/UA/поля номера. Причина 1–2 мин ожидания не доказана, серверная привязка не воспроизводилась. Исследование: docs/protocol/mobile-id-native-verification-deep-research-2026-10-27.md . Системный VPN и код MAX Desktop не изменены, новый релиз не выпускался.
