extends CanvasLayer

signal key_pressed(scan_code: String, unicode: int, shift: bool)

enum KeyboardMode { LOWER_CASE, UPPER_CASE, ALTERNATE }

@export var target_viewport: Viewport

var _shift_down := false
var _caps_down := false
var _alt_down := false
var _mode: int = KeyboardMode.LOWER_CASE


func on_key_pressed(scan_code_text: String, unicode_value: int, shift: bool) -> void:
	var scan_code := OS.find_keycode_from_string(scan_code_text)
	var down := InputEventKey.new()
	down.physical_keycode = scan_code
	down.keycode = scan_code
	down.unicode = unicode_value if unicode_value else scan_code
	down.shift_pressed = shift
	down.pressed = true

	var up := InputEventKey.new()
	up.physical_keycode = scan_code
	up.keycode = scan_code
	up.unicode = unicode_value if unicode_value else scan_code
	up.shift_pressed = shift
	up.pressed = false

	if target_viewport:
		target_viewport.push_input(down)
		target_viewport.push_input(up)
	else:
		Input.parse_input_event(down)
		Input.parse_input_event(up)

	key_pressed.emit(scan_code_text, unicode_value, shift)

	if _shift_down:
		_shift_down = false
		_update_visible()


func _on_toggle_shift_pressed() -> void:
	_shift_down = not _shift_down
	_caps_down = false
	_alt_down = false
	_update_visible()


func _on_toggle_caps_pressed() -> void:
	_caps_down = not _caps_down
	_shift_down = false
	_alt_down = false
	_update_visible()


func _on_toggle_alt_pressed() -> void:
	_alt_down = not _alt_down
	_shift_down = false
	_caps_down = false
	_update_visible()


func _update_visible() -> void:
	$Background/Standard/ToggleShift.highlighted = _shift_down
	$Background/Standard/ToggleCaps.highlighted = _caps_down
	$Background/Standard/ToggleAlt.highlighted = _alt_down

	var new_mode: int
	if _alt_down:
		new_mode = KeyboardMode.ALTERNATE
	elif _shift_down or _caps_down:
		new_mode = KeyboardMode.UPPER_CASE
	else:
		new_mode = KeyboardMode.LOWER_CASE

	if new_mode == _mode: return
	_mode = new_mode
	$Background/LowerCase.visible = _mode == KeyboardMode.LOWER_CASE
	$Background/UpperCase.visible = _mode == KeyboardMode.UPPER_CASE
	$Background/Alternate.visible = _mode == KeyboardMode.ALTERNATE
