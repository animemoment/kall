extends SceneTree

# 15-01/16-02: тип постройки в payload плана (сейв/загрузка).
# Запуск: godot --headless --path . --script res://tests/run_build_plan_payload.gd

func _init():
	var t = load("res://tests/TestBuildPlanPayload.cs").new()
	quit(0 if t.Run() else 1)