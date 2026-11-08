// Движение 2D MultiMesh агентов интерполируется на GPU.
// COLOR.rg = (cos, sin) направления взгляда/движения.
// INSTANCE_CUSTOM = (prev.x, prev.y, target.x, target.y).
shader_type canvas_item;

uniform float lerp_factor : hint_range(0.0, 1.0) = 1.0;
uniform bool is_macro_dots = false;
uniform vec4 agent_color : source_color = vec4(0.3, 0.65, 0.9, 1.0);

void vertex() {
	vec2 agent_position = mix(INSTANCE_CUSTOM.xy, INSTANCE_CUSTOM.zw, clamp(lerp_factor, 0.0, 1.0));
	
	if (is_macro_dots) {
		// Точечный режим для дальнего макро-зума (сжимаем 32px квад до ~6px без вращения)
		VERTEX = VERTEX * 0.2 + agent_position;
	} else {
		// Вращение квада вокруг собственного центра (0, 0)
		float c = COLOR.r;
		float s = COLOR.g;
		VERTEX = vec2(c * VERTEX.x - s * VERTEX.y, s * VERTEX.x + c * VERTEX.y) + agent_position;
	}
}

void fragment() {
	if (is_macro_dots) {
		COLOR = agent_color;
	} else {
		COLOR = texture(TEXTURE, UV);
	}
}
