# Zero Hour — снайперская игра (ветка `sniper`)

Обновлено: 2026-10-11. Рабочее название, владелец его не утверждал.

## Почему она появилась

Владелец 2026-10-11 отказался публиковать Skyforge («детская игрушка») и заказал новую снайперскую игру. Причина низкого качества Skyforge была в способе производства: модели запекались своим скриптом без скелета, анимации и текстур, уровни генерировались. Здесь модели и анимации импортирует сам Unity в CI, а места расставлены вручную.

## Как устроено

- Правила без Unity: `Assets/Skyforge/Core/Sniper/` — `Rifles` (6 винтовок, 5 улучшений), `Level` (описание места и контракта), `Sim` (люди, пули, тревога, исход), `Campaign` (3 места, 18 контрактов). Тесты: `Tests/SniperSimTests.cs`; один из них проходит каждый контракт «стрелком без промаха».
- Игра: `Assets/Skyforge/Runtime/Sniper/` — `SniperGame` (меню, брифинг, выстрел, камера пули, итог, арсенал), `SniperHud`, `SniperWorld` (сцена из префабов), `SniperSave` (`sniper.json`), `SniperShots` (съёмка кадров в CI). `Bootstrap` запускает `SniperGame`; код Skyforge компилируется, но не стартует.
- Модели: `Assets/Sniper/Chars/*.glb` (8 персонажей с общим скелетом и 24 клипами), `Assets/Sniper/Props/*.glb`. Пакет glTFast импортирует их; `BuildScript.PrepareCharacters/PrepareProps` делают префабы в `Assets/Generated/Resources`, `Repaint` заменяет материалы на URP/Lit (родные материалы glTFast под URP розовые). Клипы приходят как legacy — их играет `Runtime/Actor.cs`.
- Размер и опору модели `SniperWorld.Place` меряет в сцене; `Piece.Height` — высота в метрах.
- Сборка: `gh workflow run build-android.yml --ref sniper -f version=X`; релиз `dev-<N>` с `zerohour-X-N.apk` и `screenshots.zip`.

## Что проверено и что нет

Проверено по кадрам CI (сборки 40–43): весь цикл игры, три места, анимации, прицел. Тестами: правила, проходимость 18 контрактов.

НЕ проверено (нужен телефон): управление пальцем, частота кадров, звук, читаемость целей на маленьком экране, метки имён над целями (на кадрах CI они смещены — особенность съёмки).

Сделано после 0.3.0 (в сборке 45): записанные звуки (`Resources/Audio`, играет `SniperAudio`), замедление на попадании в голову, облачко попадания, реакция на ранение, движущиеся машины и буксир (`LocationDef.Traffic`), прохожие в каждом контракте (`LocationDef.Crowd`), фото цели в брифинге, контракт дня с двойной оплатой и плата за вход по дням.

Не сделано: реклама и покупки, карта мира вместо вкладок, отдельный репозиторий, текст авторства в самой игре.

## Авторство моделей

Персонажи, машины, контейнеры, мебель улиц — Quaternius (CC0). Дома — Kay Lousberg, Kenney, Quaternius (CC0). Кран — cg_world (CC0). По лицензии CC-BY 3.0, автора нужно указать в игре и в описании: Container Ship — Alex Safayan; Tugboat — Poly by Google; Warehouse — Lavender Harmony. Текстуры земли — ambientCG (CC0). Всё взято с poly.pizza; исходники и скрипт загрузки — в `~/sniper-assets` на ноутбуке владельца.

Звуки: выстрелы Mosin и SKS — Vincent Sevedge (CC-BY 3.0, opengameart.org/content/gunshot-sounds); перезарядка — opengameart.org/content/gun-reload-sounds (CC0); удары — Kenney Impact Sounds (CC0); шум улицы — opengameart.org/content/high-traffic-road-sounds (CC0); ветер — opengameart.org/content/wind-loop (CC-BY 3.0). Исходники в `~/sniper-assets/audio`.
