# MAX Desktop — 2026-10-27: исследование и исправление отсутствия SMS fallback

- Официальный MAX: SMS при «Привязать новое устройство» в боте «Коды подтверждения» документирован для Госуслуг и аккаунта с паролем входа (2FA), устанавливаемым на Android/iOS, не веб. URL источников в docs/protocol/sms-fallback-mobileid-2026-10-27.md.
- v0.1.21 уже умеет SMS при логине и дополнительный пароль, но для привязки доверенного устройства это другой серверный процесс. В оригинальном APK приватный интерфейс PrivateWebViewHandler.postEvent('WebAppVerifyMobileId',...) и штатная отрицательная ошибка client.verify_mobile_id.no_cellular отсутствовали в нашем WebView2.
- Исследовательское изменение v0.1.22: minimal JS bridge в top-frame HTTPS origin выданного MAX адреса, распознаёт только Mobile ID requestId, возвращает исходный JSON error requestId / error.code по WebApp.sendEvent. Не делает запросов к оператору и не имитирует успех. Smoke tests проверены Windows CI; телефон/SMS успешная пользовательская приёмка НЕ проводилась.
- Документ docs/protocol/sms-fallback-mobileid-2026-10-27.md и заметки релиза docs/release-notes/v0.1.22.md. Выпуск бинарника должен подтверждаться GitHub Release, не считать один git-коммит выпущенным.
- Действующие сессии и VPN без изменений, APK исследовательского проекта к текущему исправлению не относится.
