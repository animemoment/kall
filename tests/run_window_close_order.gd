extends SceneTree

# 03-02 / 03-06: порядок закрытия окна стройки и Escape.
# 03-02 — пустой CheckMark: окно закрывается, но инструмент черчения зоны больше не остаётся
#         активным (иначе игрок продолжает строить, думая, что вышел).
# 03-06 — один Escape при открытом окне стройки закрывает именно окно, а не только снимает
#         инструмент через PlayerInteractionManager; без окон Escape работает как раньше.
# Регресс-страж: окно фермы по-прежнему закрывается Escape (R-048).
# Запуск: godot --headless --path . --script res://tests/run_window_close_order.gd

func _init():
	var t = load("res://tests/TestWindowCloseOrder.cs").new()
	quit(0 if t.Run() else 1)