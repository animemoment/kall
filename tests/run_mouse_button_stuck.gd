extends SceneTree

# 20-01: драг обязан завершаться, даже если событие отпускания кнопки потерялось.
# Запуск: godot --headless --path . --script res://tests/run_mouse_button_stuck.gd
#
# Тесту нужен ЖИВОЙ SceneTree.root (менеджер добавляется в дерево, чтобы работал
# GetViewport в _UnhandledInput), поэтому запуск отложен на первый кадр: в _init
# root ещё не готов.

func _init():
	call_deferred("_run")

func _run():
	var t = load("res://tests/TestMouseButtonStuck.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)