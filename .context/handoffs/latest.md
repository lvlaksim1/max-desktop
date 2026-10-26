# MAX Desktop — передача менеджера 2026-10-27, поколение 35

Репозиторий lvlaksim1/max-desktop, менеджер project-manager. Восстановление .context/ENTRYPOINT.md, проверка state-integrity.json 8/8. MAX Desktop v0.1.21 не изменялся; Android/TCP и серверный WebAppPlatform=android были ранее подтверждены. Защищённые коды требуют привязки доверенного мобильного устройства.

В исследовательском репозитории lvlaksim1/max-android-research из точного MAX 26.35.1 (6867) создан диагностический mod APK. Github Actions 40414205114 completed success, artifact 12693639279, output SHA256 b5a8510caf37ab38ebe9a40dd7457b3e74106f9dbd507da52301bad31d00acba, размер 134098887. Пересобран apktool 3, подписан заново, apksigner v2/v3 verified. Телефонный запуск не проверен. Отдельная подпись несовместима с обновлением официального установленного MAX, на основном устройстве не экспериментировать.

Мод сохраняет только события `bridge_request` и `cellular_http_attempt` с временем в /sdcard/Android/data/ru.oneme.app/files/mobileid-research.jsonl. НЕ экспортирует токены, cookie, SIM-идентификаторы, операторские подтверждения, URL, HTTP-ответы или коды. Он НЕ переносит авторизацию в MAX Desktop. Пользователь получил скачиваемый бинарник и предупреждение. Док: https://github.com/lvlaksim1/max-android-research/blob/main/docs/mobileid-research-mod-2026-10-27.md. Системный VPN неизменен.
