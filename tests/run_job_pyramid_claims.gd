extends SceneTree

# 06-01/06-04/08-01: клейм, удаление и счётчики JobSpatialPyramid.
# Запуск: godot --headless --path . --script res://tests/run_job_pyramid_claims.gd

func _init():
	var t = load("res://tests/TestJobPyramidClaims.cs").new()
	quit(0 if t.Run() else 1)