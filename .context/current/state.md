# MAX Desktop — 2026-10-27, модифицированный исследовательский APK

- MAX Desktop: последний опубликованный v0.1.21, мобильный TCP Android и серверный WebAppPlatform=android подтверждены пользователем; мини-приложение кодов требует штатной привязки устройства.
- APK MAX 26.35.1 (6867) ранее статически исследован; механизм WebAppVerifyMobileId использует сотовое подключение Android.
- В lvlaksim1/max-android-research добавлены tools/patch_mobileid_diagnostics.py и workflow build-mobileid-research-mod.yml. Run 40414205114 SUCCESS, артефакт 12693639279, APK SHA256 b5a8510caf37ab38ebe9a40dd7457b3e74106f9dbd507da52301bad31d00acba, размер 134098887 байт. Подписан отдельным тестовым сертификатом, корректность подписи v2/v3 проверена. Реальное Android-испытание ещё не проведено, замена официальной установки без удаления невозможна.
- Мод записывает ТОЛЬКО обезличенные записи об обращении к Mobile ID и начале сотового HTTP в JSONL. Действительные токены/авторизационные заголовки/ответы Mobile ID/коды не экспортируются. Следовательно, пользователя нельзя вводить в заблуждение: это не средство переноса сеанса в MAX Desktop.
- Подробности: https://github.com/lvlaksim1/max-android-research/blob/main/docs/mobileid-research-mod-2026-10-27.md. Рабочий Windows MAX Desktop, VPN и авторизация не изменены.
