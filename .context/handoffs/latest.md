# Передача менеджера MAX Desktop — 27.10.2026, поколение 34

Менеджер project-manager, repo lvlaksim1/max-desktop/main. Восстановление через .context/ENTRYPOINT.md и проверку state-integrity.json 8/8.

Пользователь в v0.1.21 подтвердил TCP/Android, WebAppPlatform=android и настоящее miniapp кодов. Оно спустя примерно 1–2 мин требует привязки доверенного устройства через мобильную SIM. СМС в интерфейсе MAX Desktop не предлагается.

Детально исследован неизменный оригинальный base.apk 26.35.1 (6867): полная JADX декомпиляция 24 272 Java, Actions 40409450577 success. Приватный JS мост `WebAppVerifyMobileId` принимает только requestId и url; эмуляции телефонного номера нет. `ekl` просит Android cellular NetworkRequest (TRANSPORT_CELLULAR, NET_CAPABILITY_INTERNET), `fcl` выполняет HTTP через сотовую сеть; `qrk` возвращает код/headers/данные, `a8l` событие. Специальная ошибка no_cellular, отмена cancelled. Endpoint сервера, успешное подтверждение и точная причина долгой загрузки статически не доказаны. Отчёт: docs/protocol/mobile-id-native-verification-deep-research-2026-10-27.md, исходники: max-android-research/docs/mobileid-bridge-trace-26.35.1.md.

Windows MAX Desktop имеет сохранённый номер в MaxSavedSession.Phone и Android TCP-сеанс, но WebView2 не реализует приватный JS-bridge и не содержит настоящую Android cellular сеть. Подставить номер/симулировать доверие нельзя; нужна только подтверждённая MAX штатная процедура для ПК, пока не обнаружена. Никаких изменений бинарника, системного VPN, аккаунтов или выпуска после v0.1.21; QR/TinyFish/отправка сообщений не добавлены.
