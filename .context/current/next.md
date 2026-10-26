# MAX Desktop — приёмка исследовательского APK

1. Артефакт подписанного mod APK: https://github.com/lvlaksim1/max-android-research/actions/runs/40414205114 (asset MAX-MobileID-Research-Mod-26.35.1, ID 12693639279, retention 14 days). APK SHA256 b5a8510caf37ab38ebe9a40dd7457b3e74106f9dbd507da52301bad31d00acba.
2. На выделенном тестовом устройстве проверить установку под отдельной подписью и работу реального приложения. Не удалять без предварительной подготовки работающий официальный MAX: это не совместимое обновление.
3. После штатной пробной проверки Mobile ID получить /sdcard/Android/data/ru.oneme.app/files/mobileid-research.jsonl командой adb pull. Данные только schema,time_ms,event.
4. Не путать диагностический журнал с авторизационными данными и не утверждать, что мобильное доверие переносимо. MAX Desktop остаётся v0.1.21 без изменений.
