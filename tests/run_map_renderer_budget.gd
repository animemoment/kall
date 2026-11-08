extends SceneTree

# 02-04/R-02: дренаж _pendingCells в MapRenderer ограничен бюджетом кадра.
# Запуск: godot --headless --path . --script res://tests/run_map_renderer_budget.gd
# Проверка по источнику: настоящий бюджет меряется в живой игре (цена SetCell —
# перестройка меша квадранта), в headless рендер-путь пустой и замер не показателен.

func _init() -> void:
	var script = load("res://tests/TestMapRendererBudget.cs")
	if script == null:
		printerr("[GD ERR] Не загрузился tests/TestMapRendererBudget.cs")
		quit(1)
		return

	var t = script.new()
	if t == null or not t.has_method("Run"):
		printerr("[GD ERR] TestMapRendererBudget не создался или без Run()")
		quit(1)
		return

	quit(0 if t.Run() else 1)