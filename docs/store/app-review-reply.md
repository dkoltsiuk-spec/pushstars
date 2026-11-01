# App Review: ответ на Guideline 2.1 (Information Needed)

Запрос Apple от 2026-10-31, сабмит 2026-10-22, сборка 1.0.0 (261022.722), статус версии REJECTED.

Текст ниже верен для новой сборки: с покупками, фильтром ников, жалобами и блокировкой. Для сборки 261022.722 он неправдив.

Один и тот же текст идёт в ответ Resolution Center и в поле Notes раздела App Review Information (лимит 4000 знаков, текст укладывается).

## Текст для Apple

```
1. SCREEN RECORDING
Attached: captured on a physical iPhone running the latest iOS, starting from launching the app. It shows onboarding, the camera-based push-up count, a boss fight, a duel, reporting and blocking a player, an in-app purchase in the sandbox, restoring purchases, sign-in and account deletion.

2. PURPOSE AND AUDIENCE
Pushup Stars is a fitness game that helps people build a home push-up habit without a gym, equipment or a wearable. The phone camera counts full reps on the device, and every rep deals damage to an opponent, which makes a repetitive exercise motivating. It is for teens and adults who want a short daily workout at home. It is not a medical device.

3. HOW TO TEST
No login is required and no demo account is needed. The app starts in guest mode with every feature available.
1) Launch the app, follow the coach and choose a hero.
2) Allow camera access when asked.
3) Place the iPhone on the floor, leaning against something, 1.5-2 m in front of you, so your whole body is visible.
4) Do push-ups facing the camera. Only full reps count. The 60-second level test can be skipped with the Skip button.
5) On the main screen, the fight button starts a duel search. A live opponent is used when one is searching; otherwise the game offers a recorded set to race against, either another player's or one recorded by the developer. Boss islands are on the map. A friend duel by code needs two devices.
Without doing push-ups, a fight still runs and ends in a loss, so all screens can be reached.

Accounts: signing in with Apple, Google or email is optional (profile, gear icon, Settings) and keeps progress across devices. Delete account is in the same Settings screen and permanently removes the account and its server data.

User-generated content: the only one is the player's nickname, visible to other players; there is no chat or image upload.
- Filter: names are checked on the device and on the server. Try "fuck" in profile, Edit name: it is refused.
- Terms: saving a name states the no-tolerance rule and links to the Terms of Use.
- Report and block: tap a player's name in the league (bottom bar), in Friends, or the "..." after the opponent's name on a fight's result. A sheet offers Report name, Report cheating and Block player. Reports are reviewed within 24 hours. Blocked players are never matched again and are listed in Settings, Blocked players.
- Contact: Settings, Support, and beastcoredev@gmail.com.

Paid content: gem packs (consumable) and three heroes (non-consumable): shop icon on the main screen. Heroes open from their card, then BUY. Restore Purchases is at the bottom of the shop. Gems can also be earned by playing.

4. EXTERNAL SERVICES
- Google Firebase: Authentication, Firestore, Cloud Functions, Cloud Storage, Hosting, Cloud Messaging (push), Crashlytics.
- Apple In-App Purchase, verified on our server.
- Photon Realtime (Exit Games): real-time connection for live duels. It carries rep events and the 3D avatar's animation values, never camera data.
- Sign in with Apple and Google Sign-In.
- PostHog: anonymous usage statistics, can be turned off in Settings.
- MediaPipe (Google): open-source pose model that runs entirely on the device.
Camera frames are processed on the device and are never recorded or uploaded. There are no ad networks or server-side AI services.

5. REGIONAL DIFFERENCES
The app works the same in all regions. The interface language follows the device (English, Russian, Brazilian Portuguese).

6. REGULATED INDUSTRY / THIRD-PARTY MATERIAL
The app does not operate in a regulated industry. It contains no third-party brands or licensed characters. Models, animations, art and music were made for the app or are licensed for commercial use.
```

## Сценарий записи экрана

iPhone с последней iOS, сборка из TestFlight та же, что уйдёт на ревью. В Настройки → App Store → Sandbox Account войти песочным аккаунтом Apple. Приложение удалить и поставить заново, чтобы был первый запуск. Запись включить в Пункте управления, начать с домашнего экрана.

1. Тап по иконке, загрузка, онбординг, выбор героя.
2. Системный запрос камеры, разрешить.
3. Телефон на пол, тест уровня: несколько полных отжиманий, видно, как растёт счётчик.
4. Главный экран: кнопка боя, поиск, бой, экран результата.
5. На результате тап по «···» после имени соперника: REPORT NAME, потом BLOCK PLAYER. Закрыть.
6. Карта островов: один бой с боссом, хватит нескольких повторов.
7. Лига: тап по чужой строке, то же окно.
8. Магазин: пак гемов → BUY → окно Apple → покупка в песочнице → баланс вырос.
9. Герой за деньги: карточка → BUY → покупка → герой открыт. Внизу магазина RESTORE PURCHASES.
10. Профиль, редактирование ника: ввести запрещённое слово, показать отказ, потом нормальный ник.
11. Шестерёнка, Settings: BLOCKED PLAYERS, разблокировать. Sign in with Apple, виден вход.
12. Delete account, подтверждение, приложение снова гость.

Запись одним куском, 4-8 минут, без монтажа. Приложить к ответу в Resolution Center.

## Что сделать перед отправкой

- Сервер, правила Firestore и правовые страницы выложены 2026-11-01, 6 товаров созданы в App Store Connect.
- Владельцу принять обновлённое лицензионное соглашение Apple Developer Program (раздел Business).
- Новая сборка в Unity Build Automation, выбрать её в версии 1.0, проверить покупки на iPhone песочным аккаунтом.
- При отправке добавить все 6 товаров на страницу версии 1.0: первый товар уходит на ревью только вместе с версией.
- Пункт 6: владельцу подтвердить, что лицензии на музыку и модели на самом деле есть.
- Очередь жалоб смотреть каждый день: `node tools/Moderation/reports.js`. В Условиях обещано 24 часа.
