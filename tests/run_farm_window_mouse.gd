extends SceneTree

# 03-04: open farm / JobDisplay windows must not eat the mouse.
# The full-rect window roots are Ignore (Farm root — from FarmController's journal,
# JobDisplay root and its VScrollBar — from the scene text, as in BuildMenuController
# §113.5/B1), while the visible panels and the buttons inside stay Stop.
# On the old code the test is red: the JobDisplay root was a default Stop full-screen
# Control, so the camera could not be dragged or zoomed until the window was closed.
# Run: godot --headless --path . --script res://tests/run_farm_window_mouse.gd

func _init():
	var t = load("res://tests/TestFarmWindowMouse.cs").new()
	quit(0 if t.Run() else 1)