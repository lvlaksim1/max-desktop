# Передача менеджера MAX Desktop — 27.10.2026, поколение 33

Менеджер project-manager, репозиторий lvlaksim1/max-desktop, ветка main. Восстановление из .context/ENTRYPOINT.md; SHA-маркер состояния 33 должен проверяться 8/8.

Владелец получил полноценный TCP(Android) и server-signed WebAppPlatform=android после повторной авторизации на v0.1.21. Мини-приложение «Коды подтверждения» открывается, но примерно после 1–2 минут требует привязать доверенное устройство. Нужна именно операторская/SIM проверка, а не простая платформа Android.

Исследован точный base.apk MAX 26.35.1 (6867), SHA256 e809176d... в lvlaksim1/max-android-research. GitHub Actions декомпилировал 24 272 Java-файла. Нет ссылок на getLine1Number/SubscriptionManager/getSubscriberId/getSimSerialNumber, но есть getNetworkOperator/getSimOperator и WebAppVerifyMobileId + VerifyMobileIdResponseInfo. Отчёт: max-android-research/docs/telephony-api-audit-26.35.1.md; краткое заключение: max-desktop/docs/protocol/verified-apk-sim-mobileid-audit-2026-10-27.md.

В MAX Desktop текущий номер уже хранится в MaxSavedSession.Phone; его копирование в якобы SIM поле не подтверждает владение SIM, и его подмена для обхода доверенного устройства не реализовывалась. v0.1.21 остаётся последним рабочим релизом. Следующее — исследовать разрешённый официальной платформой путь поддержки доверенного ПК или объяснить отсутствие такого варианта. Не менять VPN, не публиковать коды/подписанные URL, QR/TinyFish и отправка обычных сообщений не требуются.
