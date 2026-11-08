extends SceneTree

# 25-01 / 25-06: отладочные TCP-порты (MCP 9090 и LiveBridge 9097) исполняют чужой код
# (eval / run_gdscript / run) и раньше слушали в ЛЮБОМ запуске игры — включая собранную
# игру. Любая программа на машине получала RCE, а через LiveBridge ещё и `quit`.
#
# Что проверяем по-настоящему, по сети (локально, свои порты 19090 / 19197):
#  (а) в debug-сборке порт поднимается, но команда БЕЗ токена отклоняется;
#  (б) с верным токеном — выполняется;
#  (в) `quit` без токена не закрывает игру (после отказа тест продолжает жить);
#  (г) в исходниках обоих серверов гейт `OS.is_debug_build()` стоит ДО `_server.listen(`,
#      то есть в релизной сборке порт не открывается вообще.
#
# Запуск: godot --headless --path . --script res://tests/run_debug_ports.gd

const LIVE_PORT := 19197
const MCP_PORT := 19090
const LIVE_TOKEN := "tok-live-25-06"
const MCP_TOKEN := "tok-mcp-25-01"

var _fails := 0


func _init():
	# root и autoload'ы готовы только к первому кадру.
	call_deferred("_run")


func _fail(msg: String) -> void:
	_fails += 1
	printerr("[PORTS] FAIL: " + msg)
	print("[PORTS] FAIL: " + msg)


func _ok(msg: String) -> void:
	print("[PORTS] OK: " + msg)


func _source_gate_before_listen(path: String, label: String) -> void:
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		_fail("%s: не открывается %s" % [label, path])
		return
	var text := f.get_as_text()
	f.close()
	var gate := text.find("OS.is_debug_build()")
	var listen := text.find("_server.listen(")
	if gate < 0:
		_fail("%s: нет гейта OS.is_debug_build() — в релизе порт откроется" % label)
		return
	if listen >= 0 and gate > listen:
		_fail("%s: гейт стоит ПОСЛЕ listen — в релизе порт всё равно поднимется" % label)
		return
	_ok("%s: гейт debug-сборки стоит до listen" % label)


func _connect(port: int) -> StreamPeerTCP:
	var peer := StreamPeerTCP.new()
	var err := peer.connect_to_host("127.0.0.1", port)
	if err != OK:
		return null
	var deadline := Time.get_ticks_msec() + 3000
	while peer.get_status() != StreamPeerTCP.STATUS_CONNECTED:
		peer.poll()
		if Time.get_ticks_msec() > deadline:
			return null
		OS.delay_msec(5)
	return peer


func _roundtrip(peer: StreamPeerTCP, pump: Callable, line: String) -> Dictionary:
	peer.put_data((line + "\n").to_utf8_buffer())
	var buf := ""
	for i in 400:
		pump.call()
		OS.delay_msec(3)
		var avail := peer.get_available_bytes()
		if avail > 0:
			var got: Array = peer.get_data(avail)
			if got[0] == OK:
				buf += (got[1] as PackedByteArray).get_string_from_utf8()
		if buf.find("\n") >= 0:
			break
	var first := buf.split("\n")[0]
	if first.strip_edges() == "":
		return {}
	var parsed = JSON.parse_string(first)
	if typeof(parsed) != TYPE_DICTIONARY:
		return {}
	return parsed


func _run() -> void:
	print("==================================================")
	print("[PORTS] Отладочные порты требуют токен и закрыты в релизе")
	print("==================================================")

	OS.set_environment("LIVE_BRIDGE_TOKEN", LIVE_TOKEN)
	OS.set_environment("GODOT_BRIDGE_TOKEN", MCP_TOKEN)
	OS.set_environment("GODOT_BRIDGE_PORT", str(MCP_PORT))

	# ---- (г) гейты в исходниках ----
	_source_gate_before_listen("res://addons/live_bridge/live_bridge.gd", "LiveBridge")
	_source_gate_before_listen("res://autoload/mcp_interaction_server.gd", "MCP")

	# ---- (а)-(в) LiveBridge ----
	var bridge = load("res://addons/live_bridge/live_bridge.gd").new()
	bridge.port = LIVE_PORT
	bridge.name = "PortsTestBridge"
	root.add_child(bridge)

	var peer := _connect(LIVE_PORT)
	if peer == null:
		_fail("LiveBridge не принял соединение на %d" % LIVE_PORT)
	else:
		var pump := func(): bridge._poll_server()
		var denied := _roundtrip(peer, pump, '{"id":1,"cmd":"ping"}')
		if String(denied.get("error", "")) != "unauthorized":
			_fail("ping без токена не отклонён: %s" % JSON.stringify(denied))
		else:
			_ok("LiveBridge: команда без токена отклонена")

		var allowed := _roundtrip(peer, pump,
			'{"id":2,"cmd":"ping","token":"%s"}' % LIVE_TOKEN)
		if allowed.get("ok", false) != true:
			_fail("ping с верным токеном не выполнен: %s" % JSON.stringify(allowed))
		else:
			_ok("LiveBridge: с верным токеном команда выполнена")

		# (в) quit без токена не должен закрыть игру
		var quit_denied := _roundtrip(peer, pump, '{"id":3,"cmd":"quit"}')
		if String(quit_denied.get("error", "")) != "unauthorized":
			_fail("quit без токена не отклонён: %s" % JSON.stringify(quit_denied))
		else:
			_ok("LiveBridge: quit без токена не закрывает игру")
		peer.disconnect_from_host()

	bridge.queue_free()

	# ---- (а)-(б) MCP ----
	var mcp = load("res://autoload/mcp_interaction_server.gd").new()
	mcp.name = "PortsTestMcp"
	root.add_child(mcp)

	var mcp_peer := _connect(MCP_PORT)
	if mcp_peer == null:
		_fail("MCP не принял соединение на %d" % MCP_PORT)
	else:
		var mcp_pump := func(): mcp._process(0.016)
		var mcp_denied := _roundtrip(mcp_peer, mcp_pump, '{"command":"get_instance_info"}')
		if String(mcp_denied.get("error", "")) != "unauthorized":
			_fail("MCP: get_instance_info без токена не отклонён: %s" % JSON.stringify(mcp_denied))
		else:
			_ok("MCP: команда без токена отклонена")

		var mcp_allowed := _roundtrip(mcp_peer, mcp_pump,
			'{"command":"get_instance_info","token":"%s"}' % MCP_TOKEN)
		if String(mcp_allowed.get("error", "")) == "unauthorized":
			_fail("MCP: с верным токеном команда всё равно отклонена")
		elif mcp_allowed.is_empty():
			_fail("MCP: пустой ответ на команду с верным токеном")
		else:
			_ok("MCP: с верным токеном команда выполнена")
		mcp_peer.disconnect_from_host()

	mcp.queue_free()

	print("==================================================")
	print("[PORTS] PASS" if _fails == 0 else "[PORTS] FAIL (%d)" % _fails)
	print("==================================================")
	quit(0 if _fails == 0 else 1)