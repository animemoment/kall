extends SceneTree

# 24-08 / 23-03 / 23-01: ввод окна фермы.
# 24-08 — корень Farm.tscn на весь экран больше не ест клики по карте
#         (LoadScene/OnZoneSelected ставят Ignore корню, Stop панелям, Close возвращает как было).
# 23-03 — подсказка «i» работает и за кромкой острова: сначала «своя ли клетка»,
#         потом сэмпл (остров — массивы, кромка — дверь WorldModel).
# 23-01 — крестик JobDisplay, открытого из фермы, закрывает окно (снимает выбор фермы).
# Запуск: godot --headless --path . --script res://tests/run_farm_input.gd

func _init():
	var t = load("res://tests/TestFarmInput.cs").new()
	quit(0 if t.Run() else 1)