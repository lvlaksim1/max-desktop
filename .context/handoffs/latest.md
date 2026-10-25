# Передача состояния — MAX Desktop

Проект `lvlaksim1/max-desktop`, публичная ветка `main`, менеджер Project Manager v2.

**Текущий выпуск v0.1.2** https://github.com/lvlaksim1/max-desktop/releases/tag/v0.1.2.
Windows CI 40243527019 и Release workflow 40243526991 завершились успешно. Установщики и файл SHA-256 присутствуют, предыдущий Release удалён согласно политике.

Инцидент: пользователь на v0.1.1 получил `The process cannot access the file because it is being used by another process` при нажатии «Обновить». Причина точно установлена: `using var input` не закрывался до `File.Move`. Добавлены библиотека `MaxDesktop.UpdateCore`, безопасный `VerifiedInstaller` и Windows-проверки. В v0.1.2 исправлено, но установленная v0.1.1 не может обновиться старой неисправной кнопкой — нужен однократный **ручной запуск Update v0.1.2 поверх установленной программы без удаления**.

Отдельный текст `ProtocolException: Full-message response does not contain a recognizable message object` на пользовательском снимке пока не локализован. Не утверждать, что он исправлен.

Нужны пользовательская приёмка v0.1.2, следующая проверка кнопки обновления и реальная проверка авторизации MAX. Вход на сервер, SMS, чаты и отправка остаются неподтверждёнными/нереализованными.
