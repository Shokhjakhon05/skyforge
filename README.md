# Skyforge: Global Operations

3D-игра про дроны для Android. Unity 6.3 LTS (6000.3.26f1) + URP.

- Что за игра и план этапов: [docs/SPEC.md](docs/SPEC.md)
- Где остановились и что дальше: [docs/STATE.md](docs/STATE.md)
- Принятые решения и причины: [docs/DECISIONS.md](docs/DECISIONS.md)

## Как устроен проект

Редактор Unity никто не открывает. В репозитории лежит только код:

- `Assets/Skyforge/Runtime` — игра. Точка входа `Bootstrap.cs`, всё создаётся кодом при запуске.
- `Assets/Skyforge/Editor/BuildScript.cs` — сборка. Создаёт пустую сцену, настройки URP и базовые материалы в `Assets/Generated` (в git не попадает) и собирает APK.
- `tools/gen_meta.py` — создаёт `.meta` для новых файлов. Запускать после добавления файлов в `Assets`; CI падает, если `.meta` не хватает.

## Сборка

Только в GitHub Actions, локально ничего не собирается.

```
gh workflow run build-android.yml -f version=0.0.1
gh run watch
gh release download dev-<номер> --pattern '*.apk'
```

APK публикуется как prerelease `dev-<номер запуска>`, хранятся пять последних.

Секреты репозитория: `UNITY_EMAIL`, `UNITY_PASSWORD`, `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASS`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASS`.
