# Покупки за реальные деньги

Unity IAP 5 (`com.unity.purchasing`), StoreKit 2. Клиент: `UI/Shop/IapStore.cs`. Сервер: `functions/src/purchases.js`, правила в `purchaseRules.js`.

## Товары

Id одинаковые в App Store Connect, `StoreCatalog`/`AvatarCatalog` и `purchaseRules.js`; `purchases.test.js` сверяет их.

| Id | Тип | Даёт | Цена (база США) |
|---|---|---|---|
| `gems_30` | Consumable | 30 гемов | $1.99 |
| `gems_120` | Consumable | 120 гемов | $5.99 |
| `gems_360` | Consumable | 360 гемов | $14.99 |
| `hero_skinny` | Non-Consumable | герой SKINNY | $6.99 |
| `hero_chubby` | Non-Consumable | герой CHUBBY | $6.99 |
| `hero_skeleton` | Non-Consumable | герой SKELETON | $9.99 |

На экране цена приходит из магазина в валюте игрока; цена из каталога видна только до ответа магазина.

## Как проходит покупка

1. Магазин списывает деньги и отдаёт подписанную Apple транзакцию (JWS).
2. `verifyPurchase` проверяет подпись по корневому сертификату Apple (`functions/src/certs/AppleRootCA-G3.cer`), bundle id и app id, потом закрепляет транзакцию за аккаунтом в `purchases/{transactionId}` и прибавляет купленные гемы в `purchaseTotals/{uid}`.
3. Телефон зачисляет товар. Гемы идут в кошелёк с квитанцией `store:<transactionId>`, повторно не зачислятся.
4. Только теперь транзакция закрывается в магазине.

Сбой на любом шаге оставляет транзакцию открытой, и магазин доставит её снова при следующем запуске: оплаченная покупка не теряется.

Проверка принимает и production, и sandbox: так делают ревью Apple и TestFlight.

## Восстановление

Гем-пак платит один раз одному аккаунту. Герой навсегда: `IapStore` при подключении смотрит подтверждённые покупки и выдаёт героев, которых на этом аккаунте ещё нет. Кнопка RESTORE PURCHASES внизу магазина обязательна для Apple.

Гемы гостя живут на устройстве, как и весь его прогресс. Вход сохраняет UID, поэтому купленное остаётся.

## Облачное сохранение

`saveProgress` читает `purchaseTotals/{uid}` и учитывает проверенные гемы в границе `checkEvidence`, иначе купленные гемы выглядели бы как накрутка.

## Android

`StoreAccess.RealMoney` на Android выключен: паки и герои за доллары скрыты. Чтобы включить, нужны товары в Play Console и проверка покупки на сервере через Google Play Developer API в `verifyPurchase`.

## Проверка в редакторе

В Unity Editor работает Fake Store со своим диалогом подтверждения, цена $0.01, подпись не проверяется (`#if UNITY_EDITOR` в `IapStore.Verified`). На настоящих товарах проверять только на устройстве с sandbox-аккаунтом.
