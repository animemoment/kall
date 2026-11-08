# PRODUCTION_TREE — полное дерево производства (1920–1930)

> Роль: @architect. Основано на правилах П-1…П-4 из `AGENTS.md`.
> Сырьё: 90 ресурсов из `ui/assets/textures/resources_raw/resources_raw_atlas.json` — все задействованы.
> Столбец «Оборудование» — это механизм П-2 (товар не расходуется, но без него цех стоит).

## 0. Тира и обозначения

| Тир | Что | Пример |
|---|---|---|
| **T0** | сырьё (добыча), 90 шт. | гематит, коксующийся уголь |
| **T1** | первичная обработка (1 вход) | кокс, известь, чугун |
| **T2** | вторичная (2 входа) | сталь, латунь, цемент |
| **T3** | материал / компонент (2–4 входа) | прокат, пряжа, подшипник |
| **T4** | узел | двигатель, шасси, ткань-отделка |
| **T5** | изделие (конечный товар) | трактор, лампа, одежда |

Правила именования: `snake_case`, английский, стабильные ID (никогда не переиспользуются).

---

## A. ЧЁРНАЯ МЕТАЛЛУРГИЯ

Источники: `iron_hematite`, `iron_magnetite`, `coking_coal`, `limestone`, `manganese_ore`, `chromite`, `tungsten_wolframite`, `molybdenum_ore`, `vanadium_ore`, `nickel_ore`, `cobalt_ore`, `titanium_ilmenite`, `anthracite`.

### T1

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `coke` | Кокс | `coking_coal` ×2 | Коксовая батарея | — |
| `lime` | Известь | `limestone` ×2 + `anthracite` | Обжиговая печь | — |

### T2

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `pig_iron` | Чугун | `iron_hematite` ×2 + `coke` + `lime` | Доменная печь | Воздуходувка |
| `pig_iron_mag` | Чугун (из магнетита) | `iron_magnetite` ×2 + `coke` + `lime` | Доменная печь | Воздуходувка |
| `ferro_mn` | Ферромарганец | `manganese_ore` ×2 + `coke` | Ферросплавная печь | — |
| `ferro_cr` | Феррохром | `chromite` ×2 + `coke` | Ферросплавная печь | — |
| `ferro_w` | Ферровольфрам | `tungsten_wolframite` ×2 + `coke` | Ферросплавная печь | — |
| `ferro_mo` | Ферромолибден | `molybdenum_ore` ×2 + `coke` | Ферросплавная печь | — |
| `ferro_v` | Феррованадий | `vanadium_ore` ×2 + `coke` | Ферросплавная печь | — |
| `ferro_ni` | Ферроникель | `nickel_ore` ×2 + `coke` | Ферросплавная печь | — |
| `cobalt` | Кобальт | `cobalt_ore` ×2 + `coke` | Обжиг + выщелачивание | — |
| `steel` | Сталь | `pig_iron` ×2 + `lime` | Мартеновская печь | Разливочный кран |
| `alloy_steel` | Легированная сталь | `steel` ×2 + `ferro_mn` + `ferro_cr` | Мартен | — |
| `stainless` | Нержавеющая сталь | `steel` ×2 + `ferro_cr` + `ferro_ni` | Мартен | — |
| `tool_steel` | Инструментальная сталь | `steel` ×2 + `ferro_w` | Тигельная печь | — |
| `heat_steel` | Жаропрочная сталь | `steel` ×2 + `ferro_mo` + `ferro_v` | Мартен | — |
| `titanium` | Титан | `titanium_ilmenite` ×2 + `coke` + `chlorine` | Хлоридный цех | Ректификация |

### T3

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `steel_plate` | Стальной лист | `steel` ×2 | Прокатный цех | **Прокатный стан** |
| `steel_beam` | Брус / рельс | `steel` ×2 | Прокатный цех | **Прокатный стан** |
| `steel_rod` | Пруток | `steel` | Прокатный цех | **Прокатный стан** |
| `steel_pipe` | Труба | `steel` ×2 | Трубопрокатный | — |
| `wire_steel` | Стальная проволока | `steel_rod` | Волочильный цех | **Волочильный стан** |
| `fastener` | Крепёж (болт, гайка, гвоздь) | `wire_steel` | Штамповочный | Пресс |
| `forging` | Поковка | `steel` ×2 | Кузнечный | Молот |
| `cast_iron_part` | Чугунная отливка | `pig_iron` ×2 | Литейный | Формы |
| `cast_steel_part` | Стальная отливка | `steel` ×2 | Литейный | Формы |
| `spring` | Пружина | `tool_steel` | Термоцех | Навивка + закалка |
| `bearing` | Подшипник | `stainless` + `tool_steel` | Шлифовальный | **Шлифовальный станок** |
| `gear` | Шестерня | `alloy_steel` | Зубофрезерный | **Фрезерный станок** |
| `crankshaft` | Коленвал | `alloy_steel` ×2 | Механический | **Токарный + шлифовальный** |
| `boiler_plate` | Котельный лист | `steel_plate` ×2 | Клёпальный | — |
| `valve` | Клапан / арматура | `stainless` + `brass` | Механический | **Токарный станок** |
| `electrode` | Электрод | `graphite` ×2 + `rosin` | Электродный | — |

---

## B. ЦВЕТНАЯ МЕТАЛЛУРГИЯ

Источники: `copper_chalcopyrite`, `copper_oxide`, `tin_cassiterite`, `lead_galena`, `zinc_sphalerite`, `bauxite`, `mercury_cinnabar`, `pyrite`, `gold_ore`, `silver_ore`, `platinum_ore`, `uranium_pitchblende`, `monazite`.

### T1

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `copper` | Медь | `copper_chalcopyrite` ×2 + `coke` | Конвертер | — |
| `copper_ox` | Медь (из оксидной) | `copper_oxide` ×2 + `coke` | Обжиг + рафинирование | — |
| `tin` | Олово | `tin_cassiterite` ×2 + `coke` | Плавильная печь | — |
| `lead` | Свинец | `lead_galena` ×2 + `coke` | Плавильная печь | — |
| `zinc` | Цинк | `zinc_sphalerite` ×2 + `coke` | Плавильная печь | — |
| `silver` | Серебро | `silver_ore` ×2 / попутно с `lead` | Рафинировочный | — |
| `gold` | Золото | `gold_ore` ×2 | Рафинировочный | — |
| `mercury` | Ртуть | `mercury_cinnabar` ×2 | Обжиговая печь | — |
| `platinum` | Платина | `platinum_ore` ×2 | Рафинировочный | — |
| `alumina` | Глинозём | `bauxite` ×2 + `soda` + `lime` | Выщелачивание | Автоклав |
| `sulfur_ores` | Сера (из пирита) | `pyrite` ×2 | Обжиговая печь | — |
| `radium` | Радий | `uranium_pitchblende` ×2 + `sulfuric_acid` | Химлаборатория | — |
| `ferrocerium` | Ферроцерий | `monazite` + `steel_rod` | Сплавильная печь | — |
| `thorium` | Нитрат тория | `monazite` ×2 + `nitric_acid` | Выщелачивание | — |

### T2

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `aluminum` | Алюминий | `alumina` ×2 + `electricity` ×4 + `fluorite` | Электролизный цех | **Электролизёр** |
| `brass` | Латунь | `copper` + `zinc` | Плавильная печь | — |
| `bronze` | Бронза | `copper` + `tin` | Плавильная печь | — |
| `babbitt` | Баббит | `tin` + `lead` + `copper` | Плавильная печь | — |
| `solder` | Припой | `tin` + `lead` | Плавильная печь | — |
| `duralumin` | Дюралюминий | `aluminum` + `copper` + `manganese_ore` | Плавильная печь | — |
| `galvanized` | Оцинкованный лист | `steel_plate` + `zinc` | Оцинковочная ванна | — |
| `copper_wire` | Медная проволока | `copper` | Волочильный цех | **Волочильный стан** |
| `tungstic_acid` | Вольфрамовая кислота | `tungsten_wolframite` ×2 + `soda` | Выщелачивание | — |
| `tungsten_powder` | Вольфрамовый порошок | `tungstic_acid` + `hydrogen` | Восстановление | — |
| `tungsten_bar` | Штабик вольфрама | `tungsten_powder` | Спекание | — |
| `tungsten_wire` | Нить накала | `tungsten_bar` | Волочильный цех | **Алмазные фильеры** |

### T3

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `cable` | Кабель | `copper_wire` + `rubber` + `paper` | Кабельный цех | Изолировочный пресс |
| `cable_underwater` | Подводный кабель | `copper_wire` + `gutta_percha` | Кабельный цех | — |
| `foil` | Фольга | `aluminum` | Прокатный цех | — |
| `lead_sheet` | Свинцовый лист | `lead` | Прокатный цех | — |
| `battery` | Аккумулятор | `lead_sheet` + `sulfuric_acid` + `bakelite` | Аккумуляторный | — |
| `contact` | Контакт | `silver` + `copper` | Штамповочный | Пресс |
| `mirror` | Зеркало | `silver` + `glass` | Стекольный | — |
| `coin` | Монета | `gold` / `silver` / `copper` | Монетный двор | Пресс |
| `capacitor` | Конденсатор | `foil` + `paper` + `paraffin` | Электроцех | — |
| `resistor` | Резистор | `graphite` + `porcelain` | Электроцех | — |
| `catalyst` | Катализатор | `platinum` + `asbestos_card` | Химцех | — |
| `brush_carbon` | Угольная щётка | `graphite` + `copper_wire` | Электроцех | — |

---

## C. ЛЕС

Источники: `wood_softwood`, `wood_hardwood`, `cork_bark`, `resin`, `rubber_latex`, `gutta_percha`.

### T1

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `log_soft` | Бревно хвойное | `wood_softwood` | Лесоповал | — |
| `log_hard` | Бревно лиственное | `wood_hardwood` | Лесоповал | — |
| `chips` | Щепа | `wood_softwood` ×2 | Дробилка | Дробилка |
| `rosin` | Канифоль | `resin` ×2 | Канифольный | — |
| `turpentine` | Скипидар | `resin` ×2 | Канифольный | — |
| `tannin` | Танины | `wood_hardwood` (кора) ×2 | Экстрактор | — |
| `caoutchouc` | Каучук | `rubber_latex` ×2 | Коагуляция | — |
| `cork` | Пробка | `cork_bark` ×2 | Пробковый цех | — |
| `gutta_ins` | Гуттаперчевая изоляция | `gutta_percha` ×2 | Кабельный цех | — |
| `charcoal` | Древесный уголь | `chips` ×2 | Сухая перегонка | Реторта |
| `wood_tar` | Дёготь | `chips` ×2 | Сухая перегонка | Реторта |
| `acetic_acid` | Уксусная кислота | `chips` ×2 | Сухая перегонка | Реторта |
| `methanol` | Метанол | `chips` ×2 | Сухая перегонка | Реторта |
| `formaldehyde` | Формальдегид | `methanol` | Каталитическое окисление | — |

### T2

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `plank` | Доска | `log_soft` / `log_hard` | Пилорама | **Пилорама** |
| `beam_wood` | Брус | `log_hard` | Пилорама | **Пилорама** |
| `veneer` | Шпон | `log_hard` | Лущильный | Лущильный станок |
| `plywood` | Фанера | `veneer` ×3 + `glue_bone` | Прессовочный | Пресс |
| `parquet` | Паркет | `plank` + `lacquer` | Столярный | — |
| `pulp` | Целлюлоза | `chips` ×3 + `soda` | Варочный цех | Котёл |
| `rubber` | Резина | `caoutchouc` + `sulfur` + `carbon_black` | Вулканизатор | — |
| `ebonite` | Эбонит | `caoutchouc` + `sulfur` ×3 | Вулканизатор | — |
| `tyre` | Покрышка | `rubber` ×3 + `fabric` + `carbon_black` | Шинный цех | Формовочный пресс |
| `belt` | Приводной ремень | `rubber` ×2 + `fabric` | Шинный цех | — |
| `hose` | Шланг | `rubber` | Шинный цех | — |
| `linoleum` | Линолеум | `cork` + `oil_fat` + `jute` | Линолеумный цех | — |

### T3

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `paper` | Бумага | `pulp` ×2 + `kaolin` | Бумагоделательная | — |
| `cardboard` | Картон | `pulp` ×2 | Бумагоделательная | — |
| `wax_paper` | Вощёная бумага | `paper` + `beeswax` | — | — |
| `viscose` | Вискозное волокно | `pulp` + `caustic_soda` + `sulfuric_acid` | Вискозный цех | — |
| `nitrocellulose` | Нитроцеллюлоза | `pulp` + `nitric_acid` + `sulfuric_acid` | Нитрационный | — |
| `celluloid` | Целлулоид | `nitrocellulose` + `acetone` | Пластификатор | Пресс |
| `bakelite` | Бакелит | `phenol` + `formaldehyde` | Прессовочный | **Пресс** |
| `lacquer` | Лак | `rosin` + `turpentine` + `oil_fat` | Лаковарка | — |
| `glue_bone` | Костяной клей | `bone` ×2 | Клееварка | — |
| `gelatin` | Желатин | `bone` ×2 | Желатиновый цех | — |
| `bone_meal` | Костяная мука | `bone` ×2 | Дробилка | Дробилка |

---

## D. КАМЕНЬ, СТРОЙКА, СТЕКЛО

Источники: `stone`, `granite`, `limestone`, `marble`, `basalt`, `slate`, `sand`, `gravel`, `clay`, `fireclay`, `marl`, `gypsum`, `asbestos`, `kaolin`, `magnesite`, `borax`, `fluorite`.

### T1

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `lime` | Известь | `limestone` ×2 + `anthracite` | Обжиговая печь | — |
| `alabaster` | Алебастр | `gypsum` ×2 | Обжиговая печь | — |
| `clinker` | Цементный клинкер | `limestone` ×2 + `marl` + `clay` | Клинкерная печь | — |
| `brick_raw` | Кирпич-сырец | `clay` ×2 | Формовочный | Пресс |
| `refractory_raw` | Огнеупорная масса | `fireclay` + `magnesite` + `chromite` | Смеситель | — |
| `sand_forming` | Формовочный песок | `sand` ×2 | Сушилка | — |

### T2

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `cement` | Цемент | `clinker` ×2 + `gypsum` | Цементная мельница | **Мельница** |
| `brick` | Кирпич | `brick_raw` ×2 + `anthracite` | Обжиговая печь | — |
| `tile` | Черепица | `clay` ×2 | Формовка + обжиг | — |
| `roof_slate` | Кровельный сланец | `slate` ×2 | Камнерезный | — |
| `rubble` | Щебень | `stone` ×2 | Дробилка | **Дробилка** |
| `cobble` | Брусчатка | `granite` ×2 | Камнерезный | — |
| `marble_slab` | Мраморная плита | `marble` ×2 | Камнерезный | Пила |
| `granite_block` | Гранитный блок | `granite` ×2 | Камнерезный | — |
| `basalt_wool` | Минеральная вата | `basalt` ×2 | Плавка + раздув | — |
| `refractory_brick` | Огнеупорный кирпич | `refractory_raw` ×2 | Прессовочный | Пресс |
| `asbestos_card` | Асбестовый картон | `asbestos` + `cement` | Смеситель | — |
| `glass` | Стекло | `sand` ×3 + `soda` + `limestone` | Стекловаренная печь | — |
| `borosilicate` | Боросиликатное стекло | `sand` ×2 + `soda` + `borax` + `limestone` | Стекловаренная печь | — |
| `porcelain` | Фарфор | `kaolin` ×2 + `sand` + `fluorite` | Фарфоровая печь | — |
| `faience` | Фаянс | `clay` + `kaolin` | Печь | — |

### T3

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `concrete` | Бетон | `cement` + `sand` + `gravel` + `water` | Бетономешалка | — |
| `rebar` | Арматура | `steel_rod` | Прокатный цех | **Прокатный стан** |
| `reinforced` | Железобетон | `concrete` + `rebar` | Бетонный цех | — |
| `mortar` | Раствор | `cement` + `sand` + `lime` | — | — |
| `plaster` | Штукатурка | `alabaster` + `sand` | — | — |
| `window_glass` | Оконное стекло | `glass` ×2 | Вытяжной цех | — |
| `bottle` | Бутылка / банка | `glass` | Выдувной цех | — |
| `lamp_glass` | Ламповое стекло | `borosilicate` | Выдувной цех | — |
| `insulator_el` | Электроизолятор | `porcelain` | Обжиг | — |
| `sanitary` | Сантехника | `faience` | Обжиг | — |
| `ceramic_pipe` | Керамическая труба | `clay` ×2 | Экструдер + обжиг | — |
| `enamel` | Эмаль | `glass` + `fluorite` + `borax` | Плавильная печь | — |

---

## E. ХИМИЯ

Источники: `sulfur`, `rock_salt`, `potash`, `phosphate_rock`, `nitrate_chile`, `gypsum`, `asbestos`, `graphite`, `kaolin`, `magnesite`, `fluorite`, `borax`, `pyrite`, `cobalt_ore`, `titanium_ilmenite`, `natural_gas_dry`, `water`.

### T1

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `sulfuric_acid` | Серная кислота | `sulfur` ×2 + `water` | Контактный цех | **Катализатор** |
| `sulfuric_acid_alt` | Серная кислота (из пирита) | `pyrite` ×2 + `water` | Обжиг + контакт | — |
| `soda` | Сода кальцинированная | `rock_salt` ×2 + `limestone` + `ammonia` | Сольвеевский цех | — |
| `chlorine` | Хлор | `rock_salt` ×2 + `electricity` | Электролизный цех | **Электролизёр** |
| `caustic_soda` | Каустическая сода | `rock_salt` ×2 + `electricity` | Электролизный цех | **Электролизёр** |
| `ammonia` | Аммиак | `natural_gas_dry` + `water` + `electricity` | Синтез аммиака | **Компрессор** |
| `nitric_acid` | Азотная кислота | `ammonia` ×2 + `oxygen` | Окислительный цех | — |
| `nitric_acid_alt` | Азотная кислота (из селитры) | `nitrate_chile` ×2 + `sulfuric_acid` | Реторта | — |
| `phosphoric_acid` | Фосфорная кислота | `phosphate_rock` ×2 + `sulfuric_acid` | Выщелачивание | — |
| `hydrofluoric` | Плавиковая кислота | `fluorite` ×2 + `sulfuric_acid` | Реторта | — |
| `salt_food` | Соль пищевая | `rock_salt` ×2 | Очистка | — |
| `oxygen` | Кислород | воздух + `electricity` | Воздухоразделение | **Компрессор** |
| `argon` | Аргон | воздух + `electricity` | Воздухоразделение | **Компрессор** |
| `hydrogen` | Водород | `water` + `electricity` | Электролизный цех | **Электролизёр** |
| `phenol` | Фенол | `wood_tar` / `coal_tar` ×2 | Перегонный | — |
| `benzene` | Бензол | `coal_tar` ×2 | Перегонный | — |
| `carbon_black` | Сажа | `natural_gas_dry` | Сажевая печь | — |

### T2

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `fertilizer_n` | Азотные удобрения | `ammonia` + `nitric_acid` | Тукосмесительный | — |
| `fertilizer_p` | Фосфорные удобрения | `phosphate_rock` + `phosphoric_acid` | Тукосмесительный | — |
| `fertilizer_k` | Калийные удобрения | `potash` ×2 | Тукосмесительный | — |
| `soap` | Мыло | `tallow` ×2 + `caustic_soda` | Мыловаренный | — |
| `glycerin` | Глицерин | `tallow` ×2 + `caustic_soda` | Омыление | — |
| `nitroglycerin` | Нитроглицерин | `glycerin` + `sulfuric_acid` + `nitric_acid` | Нитрационный | — |
| `dynamite` | Динамит | `nitroglycerin` + `sand_forming` | Смеситель | — |
| `gunpowder` | Бездымный порох | `nitrocellulose` + `spirit` | Пороховой | — |
| `dye_aniline` | Анилиновый краситель | `benzene` + `nitric_acid` | Красильный | — |
| `pigment_white` | Белый пигмент | `titanium_ilmenite` + `sulfuric_acid` | Химцех | — |
| `pigment_blue` | Синий пигмент | `cobalt` ×2 | Химцех | — |
| `paint` | Краска | `pigment_white` + `oil_fat` + `turpentine` + `lacquer` | Лаковарка | — |
| `acetone` | Ацетон | `acetic_acid` | Каталитический | — |
| `paraffin` | Парафин | `oil_light_paraffinic` ×2 | Нефтеперегонный | — |
| `candle` | Свеча | `paraffin` / `beeswax` / `tallow` | Свечной цех | — |
| `matches` | Спички | `paraffin` + `plank` + `sulfur` | Спичечная фабрика | — |
| `polish` | Полироль | `beeswax` + `turpentine` | — | — |
| `film` | Фотоплёнка | `celluloid` + `silver` | Фотоцех | — |
---

## F. ЭНЕРГИЯ И ТОПЛИВО

Источники: `bituminous_coal`, `anthracite`, `coking_coal`, `oil_light_paraffinic`, `oil_heavy_sour`, `natural_gas_dry`, `water`, `ice`.

### T1 — переработка топлива

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `coal_gas` | Коксовый газ | `bituminous_coal` | Коксовая батарея | — |
| `coal_tar` | Каменноугольная смола | `bituminous_coal` ×2 | Коксовая батарея | — |
| `gasoline` | Бензин | `oil_light_paraffinic` ×2 | Нефтеперегонный | Ректификация |
| `kerosene` | Керосин | `oil_light_paraffinic` ×2 | Нефтеперегонный | — |
| `diesel` | Дизельное топливо | `oil_heavy_sour` ×2 | Крекинг-цех | — |
| `fuel_oil` | Мазут | `oil_heavy_sour` ×2 | Нефтеперегонный | — |
| `lubricant` | Смазочное масло | `oil_heavy_sour` ×2 | Маслоочистка | — |
| `bitumen` | Битум | `oil_heavy_sour` ×2 | Нефтеперегонный | — |

### T2 — энергия

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `steam` | Пар | `water` ×2 + `bituminous_coal` | Котельная | **Котёл** |
| `electricity` | Электричество (ТЭС) | `steam` + `generator` | Генераторная станция | **Генератор** |
| `electricity_hydro` | Электричество (ГЭС) | `water` | ГЭС | **Турбина** |
| `asphalt` | Асфальт | `bitumen` + `gravel` | Асфальтовый завод | — |

### T3 — инфраструктура

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `road` | Асфальтовая дорога | `asphalt` ×3 + `rubble` | Дорожный цех | — |
| `cold_store` | Холодильный склад | `ice` + `cork` + `cement` | Строительный цех | — |

---

## G. ЕДА

Источники: `wheat`, `rye`, `barley`, `oats`, `maize`, `rice`, `potato`, `sugar_beet`, `sugar_cane`, `peas`, `beans`, `sunflower`, `rapeseed`, `flax_seed`, `milk`, `eggs`, `beef`, `pork`, `poultry`, `fish_sea`.

### T1

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `flour` | Мука | `wheat` ×2 / `rye` ×2 | Мельница | **Вальцовая мельница** |
| `groats` | Крупа | `barley`/`oats`/`rice`/`maize`/`peas`/`beans` ×2 | Крупорушка | — |
| `starch` | Крахмал | `potato` ×2 / `maize` ×2 | Крахмальный завод | — |
| `sugar_raw` | Сахар-сырец | `sugar_beet` ×2 / `sugar_cane` ×2 | Сахарный завод | — |
| `oil_crude` | Масло-сырец | `sunflower`/`rapeseed`/`flax_seed` ×2 | Маслобойня | **Пресс** |
| `malt` | Солод | `barley` ×2 | Солодовня | — |
| `cream` | Сливки | `milk` ×2 | Молочный завод | Сепаратор |
| `fish_meal` | Рыбная мука | `fish_sea` ×2 | Сушилка | — |
| `yeast` | Дрожжи | `sugar_raw` + закваска | Бродильный цех | — |

### T2

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `bread` | Хлеб | `flour` ×2 + `salt_food` + `yeast` | Пекарня | **Печь** |
| `pasta` | Макароны | `flour` + `eggs` | Макаронный цех | Пресс |
| `pastry` | Кондитерка | `flour` + `sugar_raw` + `oil_crude` | Кондитерская | — |
| `sugar_refined` | Сахар-рафинад | `sugar_raw` | Рафинадный цех | — |
| `oil_refined` | Масло рафинированное | `oil_crude` | Рафинадный цех | — |
| `oilcake` | Жмых | `oil_crude` | Маслобойня | — |
| `butter` | Масло сливочное | `cream` | Маслодельня | — |
| `cheese` | Сыр | `milk` + `salt_food` | Сыроварня | — |
| `cottage` | Творог | `milk` | Сыроварня | — |
| `casein` | Казеин | `milk` ×2 | Химцех | — |
| `spirit` | Спирт | `starch` / `sugar_raw` + `yeast` | Винокурня | **Дистиллятор** |
| `vinegar` | Уксус | `spirit` | Бродильный цех | — |
| `beer` | Пиво | `malt` + `water` | Пивоварня | — |
| `canned_meat` | Консервы мясные | `beef`/`pork`/`poultry` + `salt_food` + `bottle` | Консервный завод | **Автоклав** |
| `canned_fish` | Консервы рыбные | `fish_sea` + `salt_food` + `bottle` | Консервный завод | **Автоклав** |
| `sausage` | Колбаса | `beef` + `pork` + `salt_food` | Мясокомбинат | — |
| `feed` | Корм | `oilcake` + `bone_meal` + `fish_meal` + `oats` | Комбикормовый | — |

### T3

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `candy` | Конфеты | `sugar_refined` + `oil_refined` + `flour` | Кондитерская | — |

---

## H. ТЕКСТИЛЬ И КОЖА

Источники: `cotton`, `flax_fiber`, `hemp_fiber`, `jute`, `wool`, `silk`, `hides`, `beeswax`, `tallow`, `bone`.

### T1 — подготовка волокна

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `cotton_carded` | Чесаная лента | `cotton` ×2 | Чесальня | — |
| `wool_carded` | Чесаная шерсть | `wool` ×2 | Чесальня | — |
| `flax_scutched` | Трепаный лён | `flax_fiber` ×2 | Трепальный цех | — |
| `hemp_scutched` | Трепаная пенька | `hemp_fiber` ×2 | Трепальный цех | — |
| `jute_scutched` | Трепаный джут | `jute` ×2 | Трепальный цех | — |
| `silk_raw` | Шёлк-сырец | `silk` ×2 | Кокономотальный | — |

### T2 — пряжа

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `yarn_cotton` | Хлопковая пряжа | `cotton_carded` ×2 | Прядильный цех | **Прядильная машина** |
| `yarn_wool` | Шерстяная пряжа | `wool_carded` ×2 | Прядильный цех | **Прядильная машина** |
| `yarn_flax` | Льняная пряжа | `flax_scutched` ×2 | Прядильный цех | **Прядильная машина** |
| `yarn_silk` | Шёлковая нить | `silk_raw` ×2 | Крутильный цех | — |
| `twine` | Шпагат | `hemp_scutched` ×2 | Крутильный цех | — |
| `burlap_yarn` | Мешочная пряжа | `jute_scutched` ×2 | Прядильный цех | — |

### T3 — ткань и кожа

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `fabric_raw` | Суровьё | `yarn_cotton`/`yarn_wool`/`yarn_flax` ×2 | Ткацкий цех | **Ткацкий станок** |
| `fabric_bleached` | Отбелённая ткань | `fabric_raw` + `chlorine` | Отбельный цех | — |
| `fabric_dyed` | Окрашенная ткань | `fabric_bleached` + `dye_aniline` | Красильня | — |
| `cloth_wool` | Сукно | `yarn_wool` ×3 + `water` | Валяльный цех | — |
| `felt` | Войлок | `wool` ×3 + `water` | Валяльный цех | — |
| `knitwear` | Трикотаж | `yarn_cotton` ×2 | Вязальный цех | Вязальная машина |
| `canvas` | Парусина | `yarn_flax`/`hemp_scutched` ×2 | Ткацкий цех | **Ткацкий станок** |
| `burlap` | Мешковина | `burlap_yarn` ×2 | Ткацкий цех | **Ткацкий станок** |
| `rope` | Канат | `twine` ×3 + `rosin` | Канатный цех | — |
| `net` | Сеть | `twine` ×2 | Сетевязальный | — |
| `leather` | Кожа | `hides` ×2 + `tannin` + `lime` | Кожевня | — |
| `leather_chrome` | Хромовая кожа | `hides` ×2 + `chromite` + `soda` | Кожевня | — |

---

## I. МАШИНЫ И ОБОРУДОВАНИЕ (T4)

Это **сердце П-2**: почти каждый станок здесь — оборудование чужого рабочего места.

### Электрические машины

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `motor` | Электродвигатель | `copper_wire` ×2 + `alloy_steel` + `bearing` + `cast_iron_part` + `bakelite` | Электроцех | Намоточный станок |
| `generator` | Генератор | `copper_wire` ×3 + `alloy_steel` + `bearing` + `cast_iron_part` | Электроцех | Намоточный станок |
| `transformer` | Трансформатор | `copper_wire` ×2 + `alloy_steel` + `paper` | Электроцех | — |
| `magneto` | Магнето | `copper_wire` + `alloy_steel` + `bakelite` + `capacitor` | Электроцех | — |
| `spark_plug` | Свеча зажигания | `porcelain` + `ferro_ni` + `copper` | Электроцех | — |
| `lamp_socket` | Патрон ламповый | `brass` + `porcelain` | Электроцех | — |

### Силовые машины

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `steam_engine` | Паровая машина | `cast_iron_part` + `steel_rod` + `valve` + `bearing` + `fastener` | Машинный цех | **Токарный + сверлильный** |
| `boiler` | Паровой котёл | `boiler_plate` + `fastener` + `steel_pipe` + `asbestos_card` | Котельный цех | Клёпальный |
| `pump` | Насос | `cast_iron_part` + `steel_rod` + `bearing` + `valve` | Машинный цех | **Токарный** |
| `compressor` | Компрессор | `cast_iron_part` + `steel_rod` + `valve` + `motor` | Машинный цех | **Токарный** |
| `vacuum_pump` | Вакуумный насос | `cast_iron_part` + `steel_rod` + `valve` + `motor` | Машинный цех | **Токарный** |
| `ice_machine` | Льдогенератор | `compressor` + `steel_pipe` + `motor` | Машинный цех | — |

### Станки (оборудование для всего остального)

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `lathe` | Токарный станок | `cast_iron_part` + `steel_rod` + `gear` + `bearing` + `motor` | Станкостроительный | — |
| `milling_machine` | Фрезерный станок | `cast_iron_part` + `steel_rod` + `gear` + `bearing` + `motor` | Станкостроительный | — |
| `drill_press` | Сверлильный станок | `cast_iron_part` + `steel_rod` + `bearing` + `motor` | Станкостроительный | — |
| `grinder` | Шлифовальный станок | `cast_iron_part` + `tool_steel` + `bearing` + `motor` | Станкостроительный | — |
| `press_machine` | Пресс | `cast_iron_part` + `steel_rod` + `pump` + `valve` | Станкостроительный | — |
| `rolling_mill` | Прокатный стан | `cast_iron_part` + `steel_rod` ×3 + `gear` + `motor` | Станкостроительный | — |
| `wire_draw_bench` | Волочильный стан | `cast_iron_part` + `tool_steel` + `motor` | Станкостроительный | — |
| `loom` | Ткацкий станок | `cast_iron_part` + `steel_rod` + `gear` + `plank` | Станкостроительный | — |
| `spinning_machine` | Прядильная машина | `cast_iron_part` + `steel_rod` + `gear` + `motor` | Станкостроительный | — |
| `crane` | Кран | `steel_beam` + `cable` + `motor` + `gear` | Металлоконструкции | — |
| `conveyor` | Конвейер | `belt` + `steel_rod` + `bearing` + `motor` | Металлоконструкции | — |

### Узлы двигателя и транспорта

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `carburetor` | Карбюратор | `brass` + `steel_rod` + `cork` | Машинный цех | **Токарный** |
| `radiator` | Радиатор | `copper` + `brass` + `steel_pipe` | Машинный цех | — |
| `engine_block` | Блок цилиндров | `cast_iron_part` ×2 | Литейный | Расточной станок |
| `piston` | Поршень | `aluminum` + `cast_iron_part` | Механический | **Токарный** |
| `chassis` | Шасси | `steel_beam` + `forging` + `spring` + `bearing` | Сборочный | — |
| `body` | Кузов | `steel_plate` + `window_glass` + `paint` + `upholstery` | Сборочный | **Пресс** |
| `wheel` | Колесо | `steel_rod` + `tyre` | Сборочный | — |
| `propeller` | Пропеллер | `plank` ×2 + `lacquer` + `fastener` | Авиацех | — |

---

## J. ИЗДЕЛИЯ (T5)

### Сложные машины (эталон П-3/П-4)

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `lamp_incandescent` | Лампа накаливания | `lamp_glass` + `tungsten_wire` + `brass` + `argon` + `solder` | Ламповый цех | **Вакуумный насос + волочильный** |
| `radio` | Радиоприёмник | `lamp_incandescent` ×3 + `capacitor` ×4 + `resistor` ×3 + `copper_wire` + `transformer` + `bakelite` + `plank` | Радиозавод | — |
| `telephone` | Телефон | `brush_carbon` + `copper_wire` + `cable` + `bakelite` | Электроцех | — |
| `engine` | Двигатель | `engine_block` + `crankshaft` + `piston` ×4 + `bearing` ×2 + `carburetor` + `magneto` + `spark_plug` ×4 + `valve` ×4 + `fastener` + `radiator` | Моторный цех | **Токарный, шлифовальный, сверлильный** |
| `automobile` | Автомобиль | `engine` + `chassis` + `body` + `wheel` ×4 + `lamp_incandescent` ×2 + `battery` + `upholstery` | Автосборочный | **Конвейер + кран** |
| `truck` | Грузовик | `engine` + `chassis` + `steel_plate` ×4 + `wheel` ×6 + `battery` | Автосборочный | **Конвейер** |
| `tractor` | Трактор | `engine` + `chassis` + `wheel` ×4 + `pump` + `plough` | Автосборочный | **Конвейер** |
| `locomotive` | Паровоз | `boiler` + `steam_engine` ×2 + `steel_beam` ×4 + `wheel` ×6 + `cabin(plank+steel_plate)` | Локомотивостроительный | **Кран** |
| `wagon` | Вагонетка | `steel_beam` ×2 + `plank` ×2 + `bearing` ×2 + `wheel` | Вагоностроительный | — |
| `airplane` | Самолёт | `engine` + `propeller` + `plank` ×6 + `canvas` ×4 + `wire_steel` + `steel_pipe` | Авиазавод | — |
| `sewing_machine` | Швейная машина | `cast_iron_part` + `tool_steel` (игла) + `gear` ×2 + `bearing` + `plank` + `lacquer` | Машинный цех | **Токарный, фрезерный** |

### Сельхозмашины

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `plough` | Плуг | `tool_steel` + `steel_beam` + `wheel` | Сельхозмаш | — |
| `thresher` | Молотилка | `steel_beam` ×2 + `gear` ×3 + `plank` + `bearing` | Сельхозмаш | — |
| `seeder` | Сеялка | `steel_beam` + `gear` + `plank` | Сельхозмаш | — |

### Одежда и обувь

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `clothing` | Одежда | `fabric_dyed` ×2 + `twine` + `fastener` (пуговицы) | Швейный цех | **Швейная машина** |
| `coat` | Пальто | `cloth_wool` ×2 + `fabric_dyed` + `fastener` | Швейный цех | **Швейная машина** |
| `boots` | Обувь | `leather` ×2 + `rubber` + `fastener` | Обувной цех | — |
| `belt` | Ремень | `leather` + `brass` | Кожевенный | — |
| `gloves` | Перчатки | `leather` / `knitwear` | Швейный цех | — |
| `upholstery` | Обивка | `fabric_dyed` + `spring` + `felt` | Мебельный | — |

### Быт и прочее

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `furniture` | Мебель | `plank` ×3 + `glue_bone` + `lacquer` + `upholstery` | Мебельный | — |
| `dish` | Посуда | `porcelain` + `enamel` | Фарфоровый | — |
| `clock` | Часы | `brass` + `tool_steel` + `glass` | Приборостроительный | — |
| `phonograph` | Патефон | `spring` + `gear` ×2 + `steel_rod` + `plank` | Приборостроительный | — |
| `thermometer` | Термометр | `mercury` + `lamp_glass` | Приборостроительный | — |
| `lighter` | Зажигалка | `ferrocerium` + `brass` + `gasoline` | Приборостроительный | — |
| `gas_mantle` | Газовая сетка | `thorium` + `knitwear` | Химцех | — |
| `luminous_paint` | Светящаяся краска | `radium` + `paint` | Химцех | — |
| `barrel` | Бочка | `plank` ×3 + `steel_rod` (обруч) | Бондарный | — |
| `crate` | Ящик | `plank` ×2 + `fastener` | Столярный | — |
| `hand_tool` | Ручной инструмент | `tool_steel` + `plank` | Кузнечный | — |
| `window_unit` | Оконный блок | `window_glass` ×2 + `plank` + `lacquer` | Столярный | — |
| `bicycle` | Велосипед | `steel_pipe` ×2 + `tyre` ×2 + `chain(gear)` + `spring` + `leather` | Сборочный | — |
| `motorcycle` | Мотоцикл | `engine` + `chassis` + `wheel` ×2 + `gasoline` | Сборочный | — |

---

## K. ВОЕННОЕ (опционально, за скобками баланса)

| ID | Товар | Входы | Рабочее место | Оборудование |
|---|---|---|---|---|
| `rifle` | Винтовка | `tool_steel` ×2 + `plank` + `brass` | Оружейный | **Станки** |
| `ammo` | Патроны | `brass` + `lead` + `gunpowder` | Патронный | Пресс |
| `armor_plate` | Бронелист | `alloy_steel` ×2 + `nickel_ore` | Прокатный | **Прокатный стан** |

---

# МАТРИЦА ПОКРЫТИЯ: все 90 ресурсов

## Еда (20)

| Ресурс | Куда идёт |
|---|---|
| `wheat` | `flour` |
| `rye` | `flour` |
| `barley` | `groats`, `malt` |
| `oats` | `groats`, `feed` |
| `maize` | `groats`, `starch` |
| `rice` | `groats` |
| `potato` | `starch` |
| `sugar_beet` | `sugar_raw` |
| `sugar_cane` | `sugar_raw` |
| `peas` | `groats` |
| `beans` | `groats` |
| `sunflower` | `oil_crude` |
| `rapeseed` | `oil_crude` |
| `flax_seed` | `oil_crude` |
| `milk` | `cream`, `cheese`, `cottage`, `casein` |
| `eggs` | `pasta` |
| `beef` | `canned_meat`, `sausage` |
| `pork` | `canned_meat`, `sausage` |
| `poultry` | `canned_meat` |
| `fish_sea` | `canned_fish`, `fish_meal` |

## Волокно (10)

| Ресурс | Куда идёт |
|---|---|
| `cotton` | `cotton_carded` |
| `flax_fiber` | `flax_scutched` |
| `hemp_fiber` | `hemp_scutched`, `canvas` |
| `jute` | `jute_scutched`, `linoleum` |
| `wool` | `wool_carded`, `felt` |
| `silk` | `silk_raw` |
| `hides` | `leather`, `leather_chrome` |
| `bone` | `glue_bone`, `gelatin`, `bone_meal` |
| `beeswax` | `candle`, `wax_paper`, `polish` |
| `tallow` | `soap`, `glycerin`, `candle` |

## Камень (6)

| Ресурс | Куда идёт |
|---|---|
| `stone` | `rubble` |
| `granite` | `cobble`, `granite_block` |
| `limestone` | `lime`, `clinker` (цемент), `glass` |
| `marble` | `marble_slab` |
| `basalt` | `basalt_wool` |
| `slate` | `roof_slate` |

## Заполнитель (5)

| Ресурс | Куда идёт |
|---|---|
| `sand` | `glass`, `concrete`, `sand_forming` |
| `gravel` | `concrete`, `asphalt` |
| `clay` | `brick_raw`, `tile`, `faience`, `ceramic_pipe`, `clinker` |
| `fireclay` | `refractory_raw` |
| `marl` | `clinker` |

## Лес (6)

| Ресурс | Куда идёт |
|---|---|
| `wood_softwood` | `log_soft`, `chips` |
| `wood_hardwood` | `log_hard`, `tannin` |
| `cork_bark` | `cork` |
| `resin` | `rosin`, `turpentine` |
| `rubber_latex` | `caoutchouc` |
| `gutta_percha` | `gutta_ins`, `cable_underwater` |

## Руда (23)

| Ресурс | Куда идёт |
|---|---|
| `iron_hematite` | `pig_iron` |
| `iron_magnetite` | `pig_iron_mag` |
| `manganese_ore` | `ferro_mn`, `duralumin` |
| `chromite` | `ferro_cr`, `refractory_raw`, `leather_chrome` |
| `tungsten_wolframite` | `ferro_w`, `tungstic_acid` |
| `molybdenum_ore` | `ferro_mo` |
| `vanadium_ore` | `ferro_v` |
| `nickel_ore` | `ferro_ni`, `armor_plate` |
| `cobalt_ore` | `cobalt`, `pigment_blue` |
| `titanium_ilmenite` | `titanium`, `pigment_white` |
| `copper_chalcopyrite` | `copper` |
| `copper_oxide` | `copper_ox` |
| `lead_galena` | `lead`, `silver` |
| `zinc_sphalerite` | `zinc` |
| `tin_cassiterite` | `tin` |
| `bauxite` | `alumina` |
| `mercury_cinnabar` | `mercury` |
| `pyrite` | `sulfur_ores`, `sulfuric_acid_alt` |
| `gold_ore` | `gold`, `coin` |
| `silver_ore` | `silver`, `contact`, `mirror` |
| `platinum_ore` | `platinum`, `catalyst` |
| `uranium_pitchblende` | `radium` |
| `monazite` | `ferrocerium`, `thorium` |

## Химия (12)

| Ресурс | Куда идёт |
|---|---|
| `sulfur` | `sulfuric_acid`, `rubber`, `matches` |
| `rock_salt` | `soda`, `chlorine`, `caustic_soda`, `salt_food` |
| `potash` | `fertilizer_k` |
| `phosphate_rock` | `phosphoric_acid`, `fertilizer_p` |
| `nitrate_chile` | `nitric_acid_alt`, `fertilizer_n` |
| `gypsum` | `alabaster`, `cement` |
| `asbestos` | `asbestos_card`, `catalyst` |
| `graphite` | `electrode`, `resistor`, `brush_carbon` |
| `kaolin` | `porcelain`, `paper` |
| `magnesite` | `refractory_raw` |
| `fluorite` | `hydrofluoric`, `aluminum`, `porcelain`, `enamel` |
| `borax` | `borosilicate`, `enamel` |

## Энергия (6)

| Ресурс | Куда идёт |
|---|---|
| `bituminous_coal` | `coke`, `coal_gas`, `coal_tar`, `steam` |
| `anthracite` | топливо для `lime`, `brick`, ферросплавов |
| `coking_coal` | `coke` |
| `oil_light_paraffinic` | `gasoline`, `kerosene`, `paraffin` |
| `oil_heavy_sour` | `diesel`, `fuel_oil`, `lubricant`, `bitumen` |
| `natural_gas_dry` | `ammonia`, `carbon_black` |

## Утилита (2)

| Ресурс | Куда идёт |
|---|---|
| `water` | `steam`, `sulfuric_acid`, `ammonia`, `hydrogen`, `concrete`, `electricity_hydro` |
| `ice` | `cold_store` |

**Итог: 90 из 90 задействованы. Сирот нет.**

---

# ПРОМТОВАР → ПРОМТОВАР (механизм П-2)

## Оборудование: что без чего не работает

| Оборудование | Открывает |
|---|---|
| `lathe` (токарный) | `crankshaft`, `valve`, `piston`, `carburetor`, `spring`, `steam_engine`, `pump` |
| `milling_machine` (фрезерный) | `gear`, `sewing_machine` |
| `grinder` (шлифовальный) | `bearing`, `crankshaft` |
| `drill_press` (сверлильный) | `steam_engine`, `engine` |
| `press_machine` (пресс) | `fastener`, `body`, `refractory_brick`, `bakelite` |
| `rolling_mill` (прокатный) | `steel_plate`, `steel_beam`, `steel_rod`, `rebar` |
| `wire_draw_bench` (волочильный) | `wire_steel`, `copper_wire`, `tungsten_wire` |
| `loom` (ткацкий) | `fabric_raw`, `canvas`, `burlap` |
| `spinning_machine` (прядильная) | `yarn_cotton`, `yarn_wool`, `yarn_flax` |
| `vacuum_pump` | `lamp_incandescent` |
| `compressor` | `oxygen`, `argon`, `ammonia` |
| `boiler` | `steam` |
| `generator` | `electricity` |
| `motor` | почти все станки |
| `crane` | `locomotive`, `automobile` |
| `conveyor` | `automobile`, `truck`, `tractor` |
| `sewing_machine` | `clothing`, `coat` |
| `ice_machine` | `cold_store` |

## Расходуемые компоненты: кто в кого входит

| Товар | Входит в |
|---|---|
| `steel_plate` | `body`, `boiler_plate`, `truck`, `locomotive`, `reinforced` |
| `steel_beam` | `chassis`, `crane`, `locomotive`, `wagon`, `plough`, `thresher` |
| `steel_rod` | `wire_steel`, `rebar`, `spring`, `gear`, `wheel`, `lathe` |
| `copper_wire` | `cable`, `motor`, `generator`, `transformer`, `magneto`, `radio` |
| `bearing` | все вращающиеся узлы |
| `gear` | `lathe`, `loom`, `rolling_mill`, `sewing_machine`, `clock` |
| `fastener` | почти все сборки |
| `bakelite` | `radio`, `telephone`, `magneto`, `motor`, `battery` |
| `glass` / `window_glass` | `body`, `mirror`, `window_unit`, `clock`, `lamp_glass` |
| `rubber` / `tyre` | `wheel`, `belt`, `hose`, `boots` |
| `fabric_dyed` | `clothing`, `coat`, `upholstery`, `body` |
| `leather` | `boots`, `belt`, `bicycle`, `upholstery` |
| `paint` | `body`, `luminous_paint` |
| `sulfuric_acid` | удобрения, `battery`, `nitrocellulose`, `radium` |
| `soda` | `glass`, `soap`, `paper`, `alumina` |
| `chlorine` | `fabric_bleached`, `titanium` |
| `ammonia` | `nitric_acid`, `fertilizer_n`, `soda` |

---

# СЧЁТ

| Блок | Позиций |
|---|---|
| T0 — сырьё | **90** |
| T1 — первичная обработка | ~70 |
| T2 — вторичная | ~80 |
| T3 — материалы и компоненты | ~70 |
| T4 — узлы и оборудование | ~35 |
| T5 — изделия | ~45 |
| **Итого** | **~390** |

Из них **90 сырья + ~300 переделов и изделий**.

Это выше целевых 185, которые мы обсуждали. Причина — глубина по П-3/П-4:
сложные изделия требуют 4–6 уровней. Если понадобится ужать, резать по П-1:
первыми кандидатами идут `pasta`, `pastry`, `candy`, `net`, `glove`, `parquet`,
`wax_paper`, `polish`, `sanitary`, `ceramic_pipe`, `granite_block`, `cobble`,
`ferro_v`, `ferro_mo`, `duralumin`, `babbitt`, `coin`, `mirror`.

# СТАТУС

- [x] Принципы П-1…П-4 зафиксированы в `AGENTS.md`
- [x] Все 90 ресурсов задействованы
- [x] Промтовары связаны между собой (2 механизма)
- [x] Сложные изделия имеют глубину 3–4 уровня
- [ ] Числа: время, энергия, количество входов/выходов — **не заданы**
- [ ] Площади рабочих мест и шаблоны зданий — **не заданы**
- [ ] Фазировка по годам (1920–1930) — **не закреплена за позициями**