# Чеклист внешних сервисов (не коммитить секреты)

Заполняется вручную при выполнении [фазы 01](../plan/phase-01-external-services.md). Сами ключи хранить в менеджере паролей команды или CI secrets.

## Firebase

- [ ] Project ID: _______________
- [ ] Blaze billing включён
- [ ] iOS bundle ID: _______________
- [ ] Android package: _______________
- [ ] `GoogleService-Info.plist` получен (локально)
- [ ] `google-services.json` получен (локально)

## Photon

- [ ] PUN2 App ID: _______________
- [ ] Регион по умолчанию: _______________
- [ ] Webhook URL задан на RoomClosed → `onMatchFinished`
- [ ] `PHOTON_WEBHOOK_SECRET` сохранён в Firebase Functions config

## Apple

- [ ] Team ID: _______________
- [ ] APNs Key загружен в Firebase Cloud Messaging

## Google Play

- [ ] Доступ к консоли для загрузки AAB
- [ ] SHA-1/256 для signing (debug/release) добавлены при необходимости

## Branch / AppsFlyer

- [ ] Выбран провайдер: _______________
- [ ] Live key / test key (локально)

## Примечание

После заполнения удалите значения из любых копий, попадающих в git.
