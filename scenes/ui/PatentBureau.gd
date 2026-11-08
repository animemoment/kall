extends Control

## Patent bureau mockup screen.
## Visual-only prototype: generates fake lots, handles list scrolling,
## tab switching and dossier updates locally. No game systems are touched.

const ROW_HEIGHT := 78.0
const ROW_GAP := 8.0
const ROW_STEP := ROW_HEIGHT + ROW_GAP
const POOL_EXTRA := 5
const ATLAS_PATH := "res://ui/assets/textures/icons/Natural resource.png"

const VARIANTS := [
	"ОСНОВНОЙ", "УСКОРЕННЫЙ", "ЩЕЛОЧНОЙ", "КИСЛОТНЫЙ",
	"НЕПРЕРЫВНЫЙ", "ПЕРИОДИЧЕСКИЙ", "КОМБИНИРОВАННЫЙ", "МОКРЫЙ",
]

var CATEGORIES := [
	{
		"key": "oil",
		"region": Rect2(896, 128, 64, 64),
		"base": 3400,
		"effect": "ВЫХОД ЛЁГКОЙ НЕФТИ",
		"tech": [
			"КАТАЛИТИЧЕСКИЙ КРЕКИНГ", "ТЕРМИЧЕСКИЙ КРЕКИНГ", "РИФОРМИНГ",
			"ГИДРООЧИСТКА", "ДЕАСФАЛЬТИЗАЦИЯ", "ВАКУУМНАЯ ПЕРЕГОНКА",
		],
		"sellers": ["НЕФТЕСИНДИКАТ", "ВОЛГА-ОЙЛ", "БАКУ-ТРЕСТ"],
	},
	{
		"key": "coal",
		"region": Rect2(704, 128, 64, 64),
		"base": 2600,
		"effect": "ВЫХОД КОКСА",
		"tech": [
			"КОКСОВАНИЕ УГЛЯ", "ГАЗИФИКАЦИЯ УГЛЯ", "ПИРОЛИЗ УГЛЯ",
			"ОБОГАЩЕНИЕ УГЛЯ", "БРИКЕТИРОВАНИЕ УГЛЯ",
		],
		"sellers": ["ДОНБАСС-ТЕХ", "УГЛЕТРЕСТ", "КУЗБАСС-ПРОМ"],
	},
	{
		"key": "copper",
		"region": Rect2(896, 64, 64, 64),
		"base": 3600,
		"effect": "ЧИСТОТА МЕДИ",
		"tech": [
			"ЭЛЕКТРОЛИЗ МЕДИ", "ОБЖИГ МЕДНОГО КОНЦЕНТРАТА",
			"ФЛОТАЦИЯ МЕДИ", "КОНВЕРТИРОВАНИЕ ШТЕЙНА",
		],
		"sellers": ["ВОЛЬТ", "МЕДЬТРЕСТ", "УРАЛ-МЕТАЛЛ"],
	},
	{
		"key": "lime",
		"region": Rect2(960, 192, 64, 64),
		"base": 1900,
		"effect": "АКТИВНОСТЬ ИЗВЕСТИ",
		"tech": [
			"ОБЖИГ ИЗВЕСТИ", "ГИДРАТАЦИЯ ИЗВЕСТИ",
			"ШАХТНАЯ ПЕЧЬ", "ВРАЩАЮЩАЯСЯ ПЕЧЬ",
		],
		"sellers": ["ИЗВЕСТЬ-ЮГ", "СТРОЙМАТЕРИАЛЫ", "КАРЬЕР-ТРЕСТ"],
	},
	{
		"key": "wood",
		"region": Rect2(512, 256, 64, 64),
		"base": 2400,
		"effect": "СУХОСТЬ ДРЕВЕСИНЫ",
		"tech": [
			"СУШКА ДРЕВЕСИНЫ", "ПРОПИТКА ДРЕВЕСИНЫ",
			"КАМЕРНАЯ СУШКА", "ВАКУУМНАЯ СУШКА",
		],
		"sellers": ["СЕВЕРНЫЙ ЛЕС", "ЛЕСПРОМ", "ТАЙГА-ТРЕСТ"],
	},
	{
		"key": "sulfur",
		"region": Rect2(64, 192, 64, 64),
		"base": 4200,
		"effect": "ВЫХОД СЕРЫ",
		"tech": [
			"КОНТАКТНЫЙ СПОСОБ", "КЛАУС-ПРОЦЕСС",
			"ОЧИСТКА ГАЗА ОТ СЕРЫ", "ОБЖИГ КОЛЧЕДАНА",
		],
		"sellers": ["ХИМТРЕСТ", "ГОРНО-ХИМ", "СЕРНО-ГАЗОВЫЙ ЗАВОД"],
	},
]

var _atlas: Texture2D = null
var _icon_cache: Dictionary = {}
var _template: PanelContainer = null
var _pool: Array[PanelContainer] = []

var _all_items: Array[Dictionary] = []
var _owned_items: Array[Dictionary] = []
var _request_items: Array[Dictionary] = []
var _archive_items: Array[Dictionary] = []

var _tab_base: Array[Dictionary] = []
var _items: Array[Dictionary] = []

var _current_tab := 0
var _dossier_tab := 0
var _filter := 0
var _selected := -1
var _watch_state := false
var _compare: Array[int] = []

var _style_row: StyleBox = null
var _style_selected: StyleBoxFlat = null
var _tab_active_style: StyleBox = null
var _tab_inactive_style: StyleBox = null
var _filter_active_style: StyleBox = null
var _filter_inactive_style: StyleBox = null


func _ready() -> void:
	_template = $Frame/Margin/Main/Content/ListColumn/ListScroll/ListContent/LotTemplate
	_atlas = load(ATLAS_PATH) as Texture2D
	_build_styles()
	_connect_signals()
	_build_data()
	await get_tree().process_frame
	await get_tree().process_frame
	_switch_tab(0)


func _build_styles() -> void:
	_style_row = _template.get_theme_stylebox("panel")
	_style_selected = _style_row.duplicate() as StyleBoxFlat
	_style_selected.bg_color = Color(0.07, 0.07, 0.078, 1.0)
	_style_selected.border_color = Color(0.91, 0.91, 0.91, 1.0)
	_tab_active_style = %TabLots.get_theme_stylebox("normal")
	_tab_inactive_style = %TabOwned.get_theme_stylebox("normal")
	_filter_active_style = %FilterAll.get_theme_stylebox("normal")
	_filter_inactive_style = %FilterSale.get_theme_stylebox("normal")


func _connect_signals() -> void:
	%TabLots.pressed.connect(_switch_tab.bind(0))
	%TabOwned.pressed.connect(_switch_tab.bind(1))
	%TabRequests.pressed.connect(_switch_tab.bind(2))
	%TabArchive.pressed.connect(_switch_tab.bind(3))

	%FilterAll.pressed.connect(_set_filter.bind(0))
	%FilterSale.pressed.connect(_set_filter.bind(1))
	%FilterLicense.pressed.connect(_set_filter.bind(2))

	%TabEffect.pressed.connect(_set_dossier_tab.bind(0))
	%TabAdopt.pressed.connect(_set_dossier_tab.bind(1))
	%TabSeller.pressed.connect(_set_dossier_tab.bind(2))

	%ListScroll.get_v_scroll_bar().value_changed.connect(_on_scroll_changed)
	%ListScroll.resized.connect(_on_view_resized)

	%Watch.pressed.connect(_on_watch_pressed)
	%Offer.pressed.connect(_on_offer_pressed)
	%Buy.pressed.connect(_on_buy_pressed)
	%Check.pressed.connect(_on_check_pressed)
	%Close.pressed.connect(_on_close_pressed)


func _build_data() -> void:
	_all_items.clear()
	for i in range(500):
		_all_items.append(_make_item(i))

	_owned_items.clear()
	for i in range(6):
		var d := _make_item(700 + i)
		d["kind"] = "МОЙ ПАТЕНТ"
		d["status"] = ["ВНЕДРЁН", "АНАЛИЗ", "ОЖИДАНИЕ"][i % 3]
		_owned_items.append(d)

	_request_items.clear()
	for i in range(4):
		var d := _make_item(800 + i)
		d["kind"] = "ЗАЯВКА"
		d["status"] = "ОЖИДАНИЕ ОТВЕТА"
		_request_items.append(d)

	_archive_items.clear()
	for i in range(18):
		var d := _make_item(900 + i)
		d["kind"] = "АРХИВ"
		d["status"] = "АРХИВ"
		_archive_items.append(d)


func _make_item(index: int) -> Dictionary:
	var cat: Dictionary = CATEGORIES[index % CATEGORIES.size()]
	var techs: Array = cat["tech"]
	var sellers: Array = cat["sellers"]

	var tech: String = techs[int(index / CATEGORIES.size()) % techs.size()]
	var variant: String = VARIANTS[index % VARIANTS.size()]
	var seller: String = sellers[index % sellers.size()]

	var kind := "ЛИЦЕНЗИЯ" if index % 5 == 3 else "ПРОДАЖА"
	var effect := 6 + (index * 7) % 29
	var analysis := 5 + (index * 3) % 16
	var adopt := 15 + (index * 11) % 46
	var year := 1910 + (index * 5) % 18

	var base: int = int(cat["base"])
	var price_value := base + effect * 90 + (index % 9) * 130
	if kind == "ЛИЦЕНЗИЯ":
		price_value = int(price_value / 8.0)

	var price_text := _money(price_value) + (" ₽/МЕС" if kind == "ЛИЦЕНЗИЯ" else " ₽")
	var effect_text := "+%d%%" % effect
	var effect_caption: String = cat["effect"]
	var maintenance := "%d ₽ / МЕС" % (40 + (index * 13) % 160)
	var name := "%s %s" % [tech, variant]

	return {
		"id": index + 1,
		"name": name,
		"seller": seller,
		"kind": kind,
		"status": kind,
		"year": year,
		"effect": effect,
		"effect_text": effect_text,
		"effect_caption": effect_caption,
		"analysis_days": analysis,
		"adopt_days": adopt,
		"price_text": price_text,
		"price_value": price_value,
		"maintenance": maintenance,
		"cat_key": cat["key"],
		"region": cat["region"],
		"body": "Прирост показателя «%s» на %d%% для установок типа «%s». Требуется пар и обученный персонал." % [effect_caption.to_lower(), effect, tech.to_lower()],
		"adopt_body": "Сначала технологию нужно изучить: анализ займёт %d дн. После анализа открывается план модернизации на %d дн." % [analysis, adopt],
		"seller_body": "Продавец: %s. Готов обсуждать цену и сроки поставки. Отвечает в течение 1–2 дней." % seller,
	}


func _money(value: int) -> String:
	var s := str(value)
	var out := ""
	var counter := 0
	for i in range(s.length() - 1, -1, -1):
		out = s[i] + out
		counter += 1
		if counter % 3 == 0 and i > 0:
			out = " " + out
	return out


func _switch_tab(tab: int) -> void:
	_current_tab = tab
	match tab:
		0:
			_tab_base = _all_items
			%ListTitle.text = "ЛОТЫ"
		1:
			_tab_base = _owned_items
			%ListTitle.text = "МОИ ПАТЕНТЫ"
		2:
			_tab_base = _request_items
			%ListTitle.text = "ЗАЯВКИ"
		_:
			_tab_base = _archive_items
			%ListTitle.text = "АРХИВ"

	_filter = 0
	_compare.clear()
	%CompareLabel.text = "СРАВНИТЬ 0 / 3"
	_apply_filter()
	_selected = -1 if _items.is_empty() else 0
	_apply_tab_styles()
	_apply_filter_styles()
	_refresh_list()
	_update_dossier()


func _apply_filter() -> void:
	_items.clear()
	for item in _tab_base:
		if _current_tab == 0:
			if _filter == 1 and item["kind"] != "ПРОДАЖА":
				continue
			if _filter == 2 and item["kind"] != "ЛИЦЕНЗИЯ":
				continue
		_items.append(item)


func _set_filter(kind: int) -> void:
	_filter = kind
	_apply_filter()
	_selected = -1 if _items.is_empty() else 0
	_apply_filter_styles()
	_refresh_list()
	_update_dossier()


func _refresh_list() -> void:
	%ListCount.text = str(_items.size())
	%ListContent.custom_minimum_size = Vector2(0, max(_items.size() * ROW_STEP, 10.0))
	%ListScroll.scroll_vertical = 0
	_ensure_pool()
	_update_visible()


func _ensure_pool() -> void:
	var view_h: float = %ListScroll.size.y
	var needed := int(view_h / ROW_STEP) + POOL_EXTRA
	while _pool.size() < needed:
		var row := _template.duplicate() as PanelContainer
		row.visible = true
		row.mouse_filter = Control.MOUSE_FILTER_STOP
		_ignore_mouse_recursive(row)
		%ListContent.add_child(row)
		row.gui_input.connect(_on_row_gui_input.bind(row))
		_pool.append(row)


func _ignore_mouse_recursive(node: Node) -> void:
	for child in node.get_children():
		if child is Control:
			child.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_ignore_mouse_recursive(child)


func _on_scroll_changed(_value: float) -> void:
	_update_visible()


func _on_view_resized() -> void:
	_ensure_pool()
	_update_visible()


func _update_visible() -> void:
	var count := _items.size()
	if count == 0:
		for row in _pool:
			row.visible = false
		return

	var scroll: float = %ListScroll.scroll_vertical
	var view_h: float = %ListScroll.size.y
	var first := clampi(int(scroll / ROW_STEP) - 1, 0, max(count - 1, 0))
	var needed := int(view_h / ROW_STEP) + POOL_EXTRA + 1
	var row_width: float = max(%ListContent.size.x, %ListScroll.size.x - 2.0)

	for i in _pool.size():
		var row := _pool[i]
		var data_index := first + i
		if data_index >= count or i >= needed:
			row.visible = false
			continue
		row.visible = true
		row.set_meta("data_index", data_index)
		row.position = Vector2(0, data_index * ROW_STEP)
		row.size = Vector2(row_width, ROW_HEIGHT)
		_bind_row(row, data_index)


func _bind_row(row: PanelContainer, index: int) -> void:
	var item: Dictionary = _items[index]
	var icon := row.get_node("Row/Icon") as TextureRect
	var name_label := row.get_node("Row/Info/Name") as Label
	var seller_label := row.get_node("Row/Info/Seller") as Label
	var price_label := row.get_node("Row/PriceBlock/Price") as Label
	var term_label := row.get_node("Row/PriceBlock/Term") as Label

	icon.texture = _icon_for(item)
	name_label.text = item["name"]
	seller_label.text = "%s · %s" % [item["seller"], item["kind"]]
	price_label.text = item["price_text"]
	term_label.text = "АНАЛИЗ %d ДН" % int(item["analysis_days"])

	var style: StyleBox = _style_selected if index == _selected else _style_row
	row.add_theme_stylebox_override("panel", style)


func _icon_for(item: Dictionary) -> Texture2D:
	var key: String = item["cat_key"]
	if _icon_cache.has(key):
		return _icon_cache[key]
	if _atlas == null:
		return null
	var tex := AtlasTexture.new()
	tex.atlas = _atlas
	tex.region = item["region"]
	tex.filter_clip = true
	_icon_cache[key] = tex
	return tex


func _on_row_gui_input(event: InputEvent, row: PanelContainer) -> void:
	if not (event is InputEventMouseButton):
		return
	var mouse_event := event as InputEventMouseButton
	if not mouse_event.pressed or mouse_event.button_index != MOUSE_BUTTON_LEFT:
		return
	var index := int(row.get_meta("data_index", -1))
	if index < 0 or index >= _items.size():
		return
	if mouse_event.ctrl_pressed:
		_toggle_compare(index)
	else:
		_selected = index
		_update_visible()
		_update_dossier()


func _toggle_compare(index: int) -> void:
	if _compare.has(index):
		_compare.erase(index)
	elif _compare.size() < 3:
		_compare.append(index)
	%CompareLabel.text = "СРАВНИТЬ %d / 3" % _compare.size()


func _update_dossier() -> void:
	if _selected < 0 or _selected >= _items.size():
		%DossierTitle.text = "ЛОТ НЕ ВЫБРАН"
		%DossierMeta.text = ""
		%DossierStatus.text = ""
		%DossierText.text = "Выберите патент в списке слева."
		%StatEffectValue.text = "—"
		%StatEffectCaption.text = "ЭФФЕКТ"
		%StatAnalysisValue.text = "—"
		%StatAnalysisCaption.text = "АНАЛИЗ"
		%StatAdoptValue.text = "—"
		%StatAdoptCaption.text = "ВНЕДРЕНИЕ"
		%StatPriceValue.text = "—"
		%StatPriceCaption.text = "ЦЕНА"
		return

	var item: Dictionary = _items[_selected]
	%EyebrowText.text = "ЛОТ" if _current_tab == 0 else "МОЙ ПАТЕНТ"
	%DossierTitle.text = item["name"]
	%DossierMeta.text = "ПАТЕНТ № %04d · %d · %s" % [int(item["id"]), int(item["year"]), item["seller"]]
	%DossierStatus.text = str(item["status"])
	%StatEffectValue.text = item["effect_text"]
	%StatEffectCaption.text = item["effect_caption"]
	%StatAnalysisValue.text = "%d ДН" % int(item["analysis_days"])
	%StatAnalysisCaption.text = "АНАЛИЗ"
	%StatAdoptValue.text = "%d ДН" % int(item["adopt_days"])
	%StatAdoptCaption.text = "ВНЕДРЕНИЕ"
	%StatPriceValue.text = item["price_text"]
	%StatPriceCaption.text = "ЦЕНА"

	%Param1Key.text = "ГОД ТЕХНОЛОГИИ"
	%Param1Value.text = str(item["year"])
	%Param2Key.text = "ОБСЛУЖИВАНИЕ"
	%Param2Value.text = item["maintenance"]
	%Param3Key.text = "ПРАВО"
	%Param3Value.text = item["kind"]

	%Stage1Value.text = "ОТВЕТ 1–2 ДНЯ"
	%Stage2Value.text = "%d ДН" % int(item["analysis_days"])
	%Stage3Value.text = "%d ДН" % int(item["adopt_days"])

	%SellerName.text = item["seller"]
	%SellerState.text = "ГОТОВ ТОРГОВАТЬСЯ\nОТВЕТ 1–2 ДНЯ"
	%Budget.text = "100 000 ₽"
	if item["kind"] == "ЛИЦЕНЗИЯ":
		%Buy.text = "ЛИЦЕНЗИЯ ЗА %s" % item["price_text"]
	else:
		%Buy.text = "КУПИТЬ ЗА %s" % item["price_text"]
	_watch_state = false
	%Watch.text = "В ИЗБРАННОЕ"
	%FooterStatus.text = "ВЫБРАНО: %s" % item["name"]
	%FooterCount.text = "ВСЕГО: %d" % _items.size()
	_set_dossier_tab(_dossier_tab)


func _set_dossier_tab(mode: int) -> void:
	_dossier_tab = mode
	_apply_dossier_tab_styles()
	if _selected < 0 or _selected >= _items.size():
		return
	var item: Dictionary = _items[_selected]
	match mode:
		0:
			%DossierText.text = item["body"]
			%Params.visible = true
			%Stages.visible = false
		1:
			%DossierText.text = item["adopt_body"]
			%Params.visible = false
			%Stages.visible = true
		_:
			%DossierText.text = item["seller_body"]
			%Params.visible = false
			%Stages.visible = false


func _apply_tab_styles() -> void:
	var tabs := [%TabLots, %TabOwned, %TabRequests, %TabArchive]
	for i in tabs.size():
		var active := i == _current_tab
		var style: StyleBox = _tab_active_style if active else _tab_inactive_style
		var button := tabs[i] as Button
		button.add_theme_stylebox_override("normal", style)
		button.add_theme_stylebox_override("hover", style)
		button.add_theme_stylebox_override("pressed", style)
		var color := Color(0.949, 0.949, 0.949, 1.0) if active else Color(0.502, 0.502, 0.522, 1.0)
		button.add_theme_color_override("font_color", color)


func _apply_filter_styles() -> void:
	var filters := [%FilterAll, %FilterSale, %FilterLicense]
	for i in filters.size():
		var active := i == _filter
		var style: StyleBox = _filter_active_style if active else _filter_inactive_style
		var button := filters[i] as Button
		button.add_theme_stylebox_override("normal", style)
		button.add_theme_stylebox_override("hover", _filter_inactive_style)
		button.add_theme_stylebox_override("pressed", style)
		var color := Color(0.949, 0.949, 0.949, 1.0) if active else Color(0.502, 0.502, 0.522, 1.0)
		button.add_theme_color_override("font_color", color)


func _apply_dossier_tab_styles() -> void:
	var tabs := [%TabEffect, %TabAdopt, %TabSeller]
	for i in tabs.size():
		var active := i == _dossier_tab
		var style: StyleBox = _tab_active_style if active else _tab_inactive_style
		var button := tabs[i] as Button
		button.add_theme_stylebox_override("normal", style)
		button.add_theme_stylebox_override("hover", style)
		button.add_theme_stylebox_override("pressed", style)
		var color := Color(0.949, 0.949, 0.949, 1.0) if active else Color(0.502, 0.502, 0.522, 1.0)
		button.add_theme_color_override("font_color", color)


func _on_watch_pressed() -> void:
	_watch_state = not _watch_state
	%Watch.text = "В ИЗБРАННОМ" if _watch_state else "В ИЗБРАННОЕ"


func _on_offer_pressed() -> void:
	if _selected < 0 or _selected >= _items.size():
		return
	%FooterStatus.text = "ПРЕДЛОЖЕНИЕ ОТПРАВЛЕНО: %s" % _items[_selected]["price_text"]


func _on_buy_pressed() -> void:
	if _selected < 0 or _selected >= _items.size():
		return
	var item: Dictionary = _items[_selected]
	%DossierStatus.text = "ОЖИДАНИЕ ОТВЕТА"
	%FooterStatus.text = "ЗАЯВКА НА ПОКУПКУ: %s" % item["name"]


func _on_check_pressed() -> void:
	if _selected < 0 or _selected >= _items.size():
		return
	%FooterStatus.text = "ВЫБРАНО: %s" % _items[_selected]["name"]


func _on_close_pressed() -> void:
	hide()