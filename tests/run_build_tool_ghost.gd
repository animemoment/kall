extends SceneTree

# 21-02/21-05/21-06: призрак стены перерисовывается инкрементально (разница полос),
# отсечка лимита площади стоит до тяжёлой работы и сообщает причину игроку,
# в BuildTool остался один гейт «остров/мир».
# Запуск: godot --headless --path . --script res://tests/run_build_tool_ghost.gd

func _init():
	var t = load("res://tests/TestBuildToolGhost.cs").new()
	quit(0 if t.Run() else 1)