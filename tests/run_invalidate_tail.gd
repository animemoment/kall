extends SceneTree

# 05-01: дебаунс инвалидации пасфайндера обязан иметь «хвост» (вторая стена не теряется).
# Запуск: godot --headless --path . --script res://tests/run_invalidate_tail.gd

func _init():
	var t = load("res://tests/TestInvalidateTail.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)