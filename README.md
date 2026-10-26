# RandomCar

Аркадная 3D-гонка на Unity 6: солнечный автодром, четыре машины и случайные события — от червей и ракет до трамплинов и ускорителей.

## Как играть

- Запуск: `build/RandomCar/RandomCar.exe` (после сборки) или Play в сцене `Assets/Scenes/Main.unity`.
- W / ↑ — газ, S / ↓ — тормоз и назад, A / D или ← / → — руль.
- Esc или P — пауза, R — заново.
- В меню: «Хаос» меняет частоту событий (выключен / умеренно / много).
- Красная плашка и красное кольцо на дороге — опасность, уходи с полосы. Бирюзовые — польза, заезжай.
- В редакторе F1–F12 запускают события на твою машину (отладка).

## Как пересобрать

| Что | Как |
|---|---|
| Модели | `py -3 -I Tools/Blender/blender_client.py Tools/Blender/rc_lib.py Tools/Blender/cars.py` (также `environment.py`, `events.py`), нужен открытый Blender с BlenderMCP |
| Сцена | меню `RandomCar/Build Main Scene` |
| Тесты | Test Runner → EditMode, или `unity command test run --mode EditMode` |
| Сборка Windows | меню `RandomCar/Build Windows` → `build/RandomCar/RandomCar.exe` |

Подробности для разработки — в `AGENTS.md`, стиль — в `Docs/ArtDirection.md`, события — в `Docs/EventLibrary.md`.
