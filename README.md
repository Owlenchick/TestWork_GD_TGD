# DuskYard — тестовое задание Technical Artist / Game Designer

Сумеречный двор у тракта (реализм, low-fantasy), камера от третьего лица. Герой бьёт мечом соломенное чучело-мишень:
у каждого удара есть «сигнал» перед контактом, вспышка, вторичные частицы, декаль и реакция материала. Окружение —
HDRP со смешанным освещением (baked + dynamic), Light Probes, Reflection Probes и Global Volume.

| | |
|---|---|
| Unity | **6000.3.3f1** (HDRP 17.3.0). В ТЗ указана 6000.3.5f — на машине разработки был установлен 6000.3.3f1 того же минорного релиза; проект открывается в 6000.3.5f с автоматическим апгрейдом без изменений контента |
| Проект | `DuskYard/` (Unity-проект), `Tools/` (генератор Shader Graph), `Docs/` (скриншоты, отчёты бенчмарка) |
| Сцена | `Assets/_Project/Scenes/DuskYard.unity` (единственная сцена в Build Settings) |
| Build | macOS (Apple Silicon/Intel, Metal) и Windows x64 (D3D11/12) — ссылка на диск: **_вставить ссылку_** |

![overview](Docs/Screenshots/01_Overview.jpg)

---

## 1. Управление и сценарий воспроизведения

| Действие | Клавиатура / мышь | Геймпад |
|---|---|---|
| Захват курсора | ЛКМ по окну (Esc — отпустить) | — |
| Движение / бег | WASD / Shift | левый стик / L3 или LT |
| Камера | мышь | правый стик |
| Удар (повторное нажатие в окне восстановления — обратный удар, комбо из 2) | ЛКМ / Space / F | X (West) / RB |
| Lock-on: кадрирование на мишень | Q / Tab / СКМ | R3 |
| Подсказка / статистика (FPS, частицы, декали, состояние мишени) | H / F1 | — |

**Сценарий 20–40 с, воспроизводящий все эффекты:**
1. Старт: герой в 5,6 м к югу от чучела. Нажать **Q** — камера кадрирует мишень (герой в левой трети кадра, мишень в центре).
   Мишень в состоянии **Ready**: на груди тлеет пульсирующий огненный сигил (Shader Graph, эмиссия + пульс).
2. Подойти на ~1,3 м (W) и нажать **ЛКМ**:
   - замах 0,17 с — к концу замаха на кромке меча вспыхивает **блик (contact cue)** и начинается **след** клинка;
   - активная фаза 0,11 с — в точке контакта: **вспышка** (60–70 мс) + **динамический point light** (0,1 с), **солома и пыль**
     летят между нормалью и отражённым направлением клинка, **декаль-разрез** ориентирована вдоль удара и прикреплена к мишени,
     hit-stop 55 мс и короткий импульс камеры;
   - мишень → **Hit**: пружинный отклон от клинка, тёплая вспышка соломы (`_HitFlash`), сигил выгорает (dissolve 0→1 с огненной кромкой);
   - через 0,55 с → **Recovering**: возврат в вертикаль, сигил **проявляется** за светящимся фронтом (dissolve 1→0) → снова **Ready**.
3. Нажать ЛКМ два раза подряд — forehand + backhand (комбо), удары приходят с разных сторон, декали ложатся под разным углом.
4. Ударить кирпичную стену, рунный камень или ящик — **искры** (stretched billboards) / щепа + сколы и **скафф-декаль** на камне.
5. Лужи у мишени показывают SSR-отражения героя, чучела и огня; жаровня за мишенью — динамический контровой свет.

Автоматическое воспроизведение (то же самое без участия человека, сохраняет скриншоты):
```bash
Builds/macOS/DuskYard.app/Contents/MacOS/DuskYard -showcase -benchOut ./shots
```
Windows: `DuskYard.exe -showcase -benchOut shots`. Результат — кадры как в `Docs/Screenshots`.

| Ready | Contact cue | Вспышка контакта | Вторичка + декаль |
|---|---|---|---|
| ![](Docs/Screenshots/06_Gameplay_Ready.jpg) | ![](Docs/Screenshots/07_Swing_ContactCue.jpg) | ![](Docs/Screenshots/08_Contact_Flash.jpg) | ![](Docs/Screenshots/09_Contact_Secondary.jpg) |
| **Hit** (сигил выгорел) | **Recovering** (сигил проявляется) | Декаль на соломе | Искры по камню |
| ![](Docs/Screenshots/10_Dummy_Hit.jpg) | ![](Docs/Screenshots/12_Dummy_Recovering.jpg) | ![](Docs/Screenshots/11_Decal_Straw_Closeup.jpg) | ![](Docs/Screenshots/15_Stone_Sparks.jpg) |

---

## 2. Архитектура интеграции

```
MeleeAttack ──(sphere-sweep клинка, HitInfo: точка, нормаль, направление, поверхность)──┬─> IHittable.ReceiveHit   (TrainingDummy: пружина + состояния + SG-параметры)
                                                                                        └─> HitFeedbackSystem.Play  ── SurfaceFxProfile (Straw / Wood / Stone)
                                                                                               ├─ ParticleFxPool  (кольцевой пул one-shot эффектов)
                                                                                               ├─ DecalPool        (кольцевой пул DecalProjector, свой на материал)
                                                                                               ├─ contact lights   (2 HD point light, без теней)
                                                                                               └─ ThirdPersonCamera.AddShake + HitStop
```

* **Точность контакта.** Клинок сэмплируется в 4 точках; каждый кадр активной фазы делается `SphereCastNonAlloc` от позиции
  прошлого кадра к текущей. Точка/нормаль/направление — фактические, не зависят от FPS. Один контакт за взмах.
* **Данные отделены от кода.** Реакция поверхности описана `SurfaceFxProfile` (ScriptableObject): префаб частиц, размер
  пула, материал/размер/время жизни декали, цвет и световой поток вспышки, сила тряски, hit-stop. Поверхность коллайдера
  задаётся компонентом `HitSurface`.
* **Нулевые аллокации в игровом цикле.** Всё создаётся в `Awake`; проекторы декалей и контактные источники никогда не
  включаются/выключаются (в HDRP это перерегистрация в DecalSystem / LightRenderDatabase с аллокацией) — неактивные гасятся
  `fadeFactor = 0` + `drawDistance = 0` и уводом света из фрустума.
* **Без аниматора.** Удар — процедурная интерполяция поз плечевого пивота (windup → active → recovery, easing), локомоция —
  CharacterController с процедурным покачиванием/наклоном.
* **Воспроизводимый контент.** Всё, что не нарисовано руками, генерируется редакторным пайплайном
  (`DuskYard/1..4` в меню или `-executeMethod DuskYard.EditorTools.DuskYardBuilder.CliAll`): процедурные текстуры, материалы,
  меши луж, VFX-префабы, профили, префабы, сцена, Volume, Lighting Settings, бейк, билд.
  Shader Graph-ы генерируются `Tools/make_shadergraphs.py` (сериализация нод берётся из графов, поставляемых в пакете
  Shader Graph 17.3, поэтому файл — обычный `.shadergraph`, редактируемый в Shader Graph Editor).

### Слои, папки, иерархия
* Слои: `Player` (8), `Hittable` (9), `VFX` (10). Атака не бьёт по `Player`/`VFX`/`Ignore Raycast`; камера сталкивается только с `Default`.
* Сцена: `--- Environment` / `--- Lighting` / `--- PostProcess` / `--- Gameplay` (Player, TrainingDummy, Main Camera) / `--- Systems` (CombatFX, Diagnostics).
* `Assets/_Project/`: `Art/{Textures/Generated, Materials/{Environment,Characters,ShaderGraphPresets,VFX,Decals}, Meshes, ThirdParty}`,
  `Shaders`, `Prefabs/{Characters,Environment,VFX,Systems}`, `Data/SurfaceFX`, `Settings/{Volumes,Lighting}`, `Scenes`, `Scripts/{Runtime,Editor}`
  (asmdef `DuskYard.Runtime` и `DuskYard.Editor`).

### Ключевые ассеты

| Тип | Ассет | Назначение |
|---|---|---|
| Shader Graph | `Shaders/SG_DissolveEmissiveLit` | HDRP Lit, opaque + alpha clip. **Все три признака**: маска растворения/проявления с огненным фронтом, управляемая эмиссия (маска × цвет × интенсивность в нитах × пульс + hit flash), triplanar-модуляция «грязью» (object space — не «плывёт» на движущемся чучеле). 21 свойство в 5 категориях, группы и заметка в графе |
| Пресеты SG | `M_SG_Straw_Dummy`, `M_SG_Ember_Sigil`, `M_SG_Waystone_Runes` | 3 визуально разных пресета: солома (без эмиссии, тёплый hit flash), тлеющий сигил (пульс, dissolve-реакция на удар), рунный камень окружения (холодные руны, мох) |
| Shader Graph | `SG_ParticleAdditive`, `SG_ParticleAlpha` | HDRP Unlit для Shuriken: vertex color × текстура × HDR-тинт × интенсивность → **Emission (ниты, учитывает экспозицию)**, soft particles по Scene Depth Difference |
| Скрипты | `MeleeAttack`, `TrainingDummy`, `HitFeedbackSystem`, `ParticleFxPool`, `DecalPool`, `SurfaceFxProfile`, `HitSurface`, `HitStop` | бой и фидбэк |
| | `ThirdPersonController`, `ThirdPersonCamera`, `PlayerInputReader` | герой, камера (коллизия, lock-on, shake), ввод (Input System) |
| | `TorchFlicker`, `GameBootstrap`, `DebugHud`, `PerfHarness` | огонь, VSync off, оверлей, бенчмарк/showcase |
| | `Editor/Builder/DuskYardBuilder.*`, `ProjectValidator` | генерация контента, бейк, билд, проверка шейдеров/материалов/ссылок |
| Префабы | `PF_Player`, `PF_TrainingDummy` | герой (меч, след, блик), мишень (коллайдеры на unit-scale детях для декалей, kinematic RB) |
| | `PF_FX_Hit_Straw/Wood/Stone`, `PF_FX_SwingGlint`, `PF_FX_TorchFlame`, `PF_FX_BrazierFlame` | эффекты |
| | `PF_CombatFX`, `PF_Diagnostics` | системы |
| | `PF_WallTorch` (baked), `PF_Brazier` (realtime), `PF_Well`, `PF_Waystone`, `PF_Crate`, `PF_Barrel`, `PF_HayBale`, `PF_WeaponRack`, `PF_Chest`, `PF_Rocks`, `PF_Puddle_0..2` | окружение |
| Данные | `Data/SurfaceFX/SFX_Straw/Wood/Stone` | профили реакции поверхностей |
| Настройки | `Settings/Volumes/VP_DuskYard_Global`, `Settings/Lighting/LS_DuskYard` | Volume и Lightmapper |

### Освещение (HDRP, физические единицы)
| Вклад | Источник | Режим |
|---|---|---|
| **Key** | Солнце 3,5° над горизонтом с ЮЗ, 100 000 lx у верхней границы атмосферы; Physically Based Sky сам ослабляет и «краснит» свет | Mixed (Baked Indirect): прямой свет и тени realtime, отскок в лайтмапах/пробах |
| **Fill** | Небо (PB Sky → лайтмапы, пробы, ambient) + мягкий холодный area light 8×4 м с востока (9000 K) | Baked |
| **Rim** | Жаровня за мишенью, 2600 lm, 1800 K, мерцание, мягкие тени низкого разрешения | **Realtime (dynamic)** |
| События | 2 контактных point light на удар (500–1100 lm, 0,09–0,14 с, без теней) | Realtime |
| Факелы | 6 настенных, 650 lm, 1900 K; анимируются только частицы | Baked |

* **Light Probes:** регулярная сетка 2×2 м на 4 высотах (0,35 / 1,4 / 2,6 / 4,2 м) + плотное кольцо вокруг мишени/жаровни;
  точки внутри геометрии отброшены. Пропсы — static, но GI получают от проб (у мелких примитивов не было бы нормальных лайтмапов).
  **Проверка движущегося объекта:** бенчмарк проходит по маршрутам героя (тень→солнце, навес→двор, тракт→ворота, круг вокруг
  мишени) с шагом 5 см и сравнивает интерполированную облучённость соседних точек: средний шаг 0,40 %, **максимальный 1,58 %** — скачков нет.
* **Reflection Probes:** 2 baked box-projected (двор и тракт) + SSR только для гладких поверхностей (min smoothness 0,8: лужи, вода в колодце, сталь).
  Режим baked — свет статичен, обновлять пробы в рантайме нечего.
* **Global Volume:** Exposure (Automatic Histogram, EV 6–10, 45–92 % гистограммы — вспышки удара не «качают» экспозицию),
  Tonemapping ACES, White Balance −6, Color Adjustments (contrast 6, saturation −4), Bloom 0,12 (threshold 1 — форма VFX сохраняется),
  Fog с volumetrics (вечерняя дымка, лучи солнца), SSAO, SSR, Shadows 55 м. Vignette/CA/grain/DOF не используются.
* Light Probe System переключён с Adaptive Probe Volumes (по умолчанию в шаблоне) на legacy Light Probes — по требованию ТЗ.

---

## 3. Производительность

**Конфигурация:** Apple MacBook, **Apple M5 Pro** (15 потоков CPU, интегрированный GPU), 24 ГБ RAM, macOS 26.6.2, Metal.
**Quality preset:** `High Fidelity` (HDRP asset шаблона: SSR включён, Light Probes legacy). Разрешение **1920×1080** (окно, backbuffer
в пикселях), **VSync отключён** (`vSyncCount = 0`, `targetFrameRate = -1`). Билд release (Mono).

**Метод.** Встроенный в билд `PerfHarness`, запуск `DuskYard -benchmark` (опционально `-benchOut DIR`, `-benchScale k`):
прогрев 6 с → **Baseline** 20 с (герой стоит, VFX нет — стоимость освещённой сцены) → **Gameplay** 30 с (автопилот ходит
вокруг мишени и бьёт комбо, ~0,7 контакта/с) → **Stress** 60 с (непрерывные комбо + 30 синтетических контактов/с по мишени,
стенам, камню, ящику — все пулы насыщены, hit-stop отключён). Перед каждым сегментом `GC.Collect()`. Пишутся время каждого кадра
(медиана, 1 % low = средний FPS худшего 1 % кадров, p95/p99/max), `GC.CollectionCount`, Mono heap, число Transform в сцене,
пик живых частиц и активных декалей; посекундный CSV. Аллокации на кадр (`GC Allocated In Frame`) доступны только в
development-билде, поэтому они замерены отдельным прогоном development-плеера. Сырые отчёты — `Docs/Benchmark/`.

**Release, прогон 1** (прогон 2 отличается < 1,5 %: Baseline 106,1 / Gameplay 109,2 / Stress 105,9 FPS):

| Сегмент | Кадров | **Медиана FPS** | 1 % low | p95 / p99 мс | GC.Collect | Mono heap, МБ | Частиц пик (бюджет) | Декалей пик (ёмкость) | Transform начало→конец | Контактов |
|---|---|---|---|---|---|---|---|---|---|---|
| Baseline | 2142 | **106,2** | 96,9 | 9,97 / 10,16 | 0 | 5,8 → 5,8 | 0 (538) | 0 (20) | 351 → 351 | 0 |
| Gameplay | 3279 | **110,4** | 92,5 | 9,95 / 10,31 | 0 | 5,8 → 5,8 | 55 (538) | 7 (20) | 351 → 351 | 20 |
| Stress | 6331 | **105,6** | 93,3 | 10,30 / 10,61 | 0 | 5,8 → 5,8 | 416 (538) | 20 (20) | 351 → 351 | 1646 |

**Development-билд (аллокации):** GC alloc на кадр — Baseline 0,008 КБ, Gameplay 0,004 КБ, Stress 0,003 КБ (это одна строка
посекундного CSV самого харнесса); GC.Collect = 0, Mono heap 7,0 → 7,1 МБ.

Вывод: цель «медиана ≥ 60 FPS» выполнена с запасом ~75 %; в Stress нет роста частиц (жёсткий потолок 538), объектов,
памяти и сборок мусора. Одиночный кадр 20–23 мс в Gameplay — первый показ эффекта (прогрев PSO/шейдерного варианта), повторно не возникает.

---

## 4. Оптимизации, предупреждения, ограничения

**Применённые оптимизации**
* Пулы фиксированного размера для частиц (кольцо, переиспользуется самый старый), декалей и контактного света; `Instantiate`/`Destroy` в игре нет.
* У каждого hit-эффекта `maxParticles`, только burst, без циклов → суммарный бюджет известен заранее (538).
* Декали: один материал на пул (нет перерегистрации в DecalSystem), проекторы не переключаются, неактивные отсечены по drawDistance.
* Контактные огни без теней, 2 шт., живут ~0,1 с. Тени есть только у солнца и жаровни (низкий тир разрешения).
* Факелы и fill — baked (нулевая стоимость в рантайме). Мелкие пропсы освещаются пробами (меньше лайтмап, нет швов).
* Static batching/occlusion flags для окружения; материалы на инстансах (не MaterialPropertyBlock) — совместимость с SRP Batcher.
* SSR только для поверхностей со smoothness ≥ 0,8; частицы — Unlit, без теней/проб/motion vectors; soft particles вместо sorting-хаков.
* Антиалиасинг SMAA вместо TAA: TAA размазывает быстрые частицы и след клинка без motion vectors.
* Эмиссия партиклов идёт в Emission (ниты, с экспозицией), а не в Unlit Color: яркость VFX согласована со светом сцены.

**Console Error:** нет ни в редакторе (батч-импорт, генерация, бейк, валидация), ни в логах release/development-плеера.
**Предупреждения:**
* Windows-билд, собранный на macOS: 24 × «Platform doesn't support Ray Tracing Shader compilation» — ray tracing-шейдеры
  HDRP не компилируются кросс-платформенно; ray tracing в проекте выключен, на работу билда не влияет.
* В батч-режиме редактор пишет сетевые сообщения сервисов Unity (`Curl error 42`, timeout CDN-конфига) — окружение, не проект.
* `ProjectValidator` (меню `DuskYard/Validate Project`) проверяет ошибки компиляции всех шейдеров проекта, битые шейдеры
  материалов, null-слоты материалов и missing scripts в префабах: 0 ошибок.

**Известные ограничения**
* Персонаж и мишень собраны из примитивов (по ТЗ модели не требуются); анимации процедурные.
* Частицы Unlit: дым/пыль не освещаются динамическими источниками (яркость подобрана под сумерки в нитах).
* Проверено на Apple M5 Pro (Metal). Windows-билд собран и прошёл компиляцию шейдеров D3D, но не запускался на Windows-машине.
* Скриншоты в `Docs/Screenshots` сняты автоматическим showcase-прогоном; положение солнца фиксировано (без смены времени суток).
* Сцена и большинство ассетов генерируются кодом: ручные правки сгенерированных ассетов перезапишутся при повторном
  запуске `DuskYard/1..3` (это сделано намеренно ради воспроизводимости; правки — через билдер или после отключения шага).

---

## 5. Сторонние ассеты

Только материалы Unity из пакетов, установленных в проект (лицензия **Unity Companion License**), скопированы в
`Assets/_Project/Art/ThirdParty/UnitySamples`:
* HDRP 17.3 `Samples~/MaterialSamples/Textures`: `Bricks_BC/M/N`, `woodflooring_basecolor/normal/output`, `Leather_M/N`.
* HDRP 17.3 `Samples~/WaterSamples`: меши `chest.fbx`, `Rocks.fbx`; текстуры `Chest_BC/M/N`, `Rock_BC`.
* Shader Graph 17.3 `Samples~/NodeReference/Textures`: `cobblestones_d/n/h`.
* Шаблон проекта `com.unity.template.3d-high-end` (HDRP-ассеты качества и Global Settings).

Всё остальное — собственное: процедурные текстуры (солома, шум, сигил, руны, спрайты частиц, декали), Shader Graph-ы,
меши луж, префабы, код. Starter Assets не использовались.

---
