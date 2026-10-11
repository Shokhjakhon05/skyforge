# Zero Hour — снайперская игра (ветка `sniper`)

Обновлено: 2026-10-12. Рабочее название, владелец его не утверждал.

## Почему она появилась

Владелец 2026-10-11 отказался публиковать Skyforge («детская игрушка») и заказал новую снайперскую игру. Причина низкого качества Skyforge была в способе производства: модели запекались своим скриптом без скелета, анимации и текстур, уровни генерировались. Здесь модели и анимации импортирует сам Unity в CI, а места расставлены вручную.

## Как устроено

- Правила без Unity: `Assets/Skyforge/Core/Sniper/` — `Rifles` (6 винтовок, 5 улучшений), `Level` (описание места и контракта), `Sim` (люди, пули, тревога, исход), `Campaign` (3 места, 18 контрактов). Тесты: `Tests/SniperSimTests.cs`; один из них проходит каждый контракт «стрелком без промаха».
- Игра: `Assets/Skyforge/Runtime/Sniper/` — `SniperGame` (меню, брифинг, выстрел, камера пули, итог, арсенал), `SniperHud`, `SniperWorld` (сцена из префабов), `SniperSave` (`sniper.json`), `SniperShots` (съёмка кадров в CI). `Bootstrap` запускает `SniperGame`; код Skyforge компилируется, но не стартует.
- Модели: `Assets/Sniper/Chars/*.glb` (8 персонажей с общим скелетом и 24 клипами), `Assets/Sniper/Props/*.glb`. Пакет glTFast импортирует их; `BuildScript.PrepareCharacters/PrepareProps` делают префабы в `Assets/Generated/Resources`, `Repaint` заменяет материалы на URP/Lit (родные материалы glTFast под URP розовые). Клипы приходят как legacy — их играет `Runtime/Actor.cs`.
- Размер и опору модели `SniperWorld.Place` меряет в сцене; `Piece.Height` — высота в метрах.
- Сборка: `gh workflow run build-android.yml --ref sniper -f version=X`; релиз `dev-<N>` с `zerohour-X-N.apk` и `screenshots.zip`.

## Состояние на ночь 2026-10-11/12

Владелец поиграл в сборку 44 и заказал «к утру готовую игру»: звук выстрела был как у автомата (стоял синтезированный), цели нереалистичные, нет заставки, слабый значок; велел тестировать самому. Репозиторий по его разрешению открыт (минуты Actions кончались), основная ветка — `sniper`.

В игре: 6 мест (доки, центр города, база в пустыне, ночной аэродром, вокзал, зимняя деревня) по 6 контрактов; бочки с топливом, которые взрываются от пули; режим «Охота» (`Core/Sniper/Extras.cs`); серии попаданий в голову, рекорд дальности; карьера с достижениями и текстом авторства (`Resources/sniper_credits.txt`); настройки (чувствительность, доводка гироскопом — по умолчанию выключена); режим «Профи» без имён над головами; контракт дня и плата за вход; заставка `SniperSplash`; значок `tools/gen_icon_sniper.py`; записанные звуки `Resources/Audio`.

Инструменты проверки без телефона:
- `tools/sniper_preview.py` — рисует места и вид с каждой позиции стрелка на ноутбуке из тех же GLB (выгрузка расстановки: `SNIPER_DUMP=файл tools/check/test.sh SniperSimTests`). Совпал с кадрами CI; нашёл «ангар»-вешалку и «снеговика»-бургер до сборки.
- `.github/workflows/smoke-android.yml` + `tools/smoke.sh` — ставит APK на эмулятор Android в CI, проходит до выстрела, сохраняет экраны и logcat (`smoke.zip` в релизе). APK должен быть собран с `-f emulator=true` (добавляет x86-64): ARM-код через трансляцию падает в потоке FMOD.
- Сборка кладёт APK и `screenshots.zip` в артефакты запуска (`gh run download <id> -n build-<N>`): создание релиза токеном Actions один раз вернуло 403 (сборка 46), причина не найдена.

## Что проверено и что нет

Проверено по кадрам CI: весь цикл игры, первые три места, анимации, прицел, брифинг с фото цели. Тестами: правила, проходимость всех 36 контрактов «стрелком без промаха», охота во всех местах. Эмулятор: сборка 44 ставится и запускается.

НЕ проверено на телефоне: управление после 44, частота кадров с тенями на 340 м и фонарями ночью, звук, гироскоп.

Не сделано: стрельба по колёсам машин, карта мира, отдельный репозиторий, более реалистичные люди (у реалистичных CC-моделей нет полного набора анимаций). Реклама — на ветке `sniper-ads` (пакет Google Mobile Ads 10.6.0 через OpenUPM, id приложения пишет `BuildScript.PrepareAds`, код за `#if SNIPER_ADS` в `SniperAds.cs`); в основную ветку не влита, пока не проверена.

## Авторство моделей

Персонажи, машины, контейнеры, мебель улиц — Quaternius (CC0). Дома — Kay Lousberg, Kenney, Quaternius (CC0). Кран — cg_world (CC0). По лицензии CC-BY 3.0, автора нужно указать в игре (экран «Карьера») и в описании: Container Ship — Alex Safayan; Tugboat, Airplane, Jet — Poly by Google; Warehouse — Lavender Harmony; Small Airplane — Vojtěch Balák; Japanese Train Station — Andrew-John Hickman. Helicopter — kazuma, Cottage/Church/Cabin — CreativeTrio (CC0). Текстуры земли — ambientCG (CC0). Всё взято с poly.pizza; исходники и скрипт загрузки — в `~/sniper-assets` на ноутбуке владельца.

Звуки: выстрелы Mosin и SKS — Vincent Sevedge (CC-BY 3.0, opengameart.org/content/gunshot-sounds); перезарядка — opengameart.org/content/gun-reload-sounds (CC0); удары — Kenney Impact Sounds (CC0); шум улицы — opengameart.org/content/high-traffic-road-sounds (CC0); ветер — opengameart.org/content/wind-loop (CC-BY 3.0). Исходники в `~/sniper-assets/audio`.
