extends Node2D

signal pressed
signal released

@export var key_size := Vector2(32, 32) : set = _set_key_size
@export var key_text := "" : set = _set_key_text
@export var key_normal := Color(0.1, 0.1, 0.1)
@export var key_highlight := Color(0.2, 0.2, 0.2)
@export var text_normal := Color(1.0, 1.0, 1.0)
@export var text_highlight := Color(0.0, 0.0, 0.0)
@export var highlighted := false : set = _set_highlighted

var _button: Button


func _ready() -> void:
	_button = Button.new()
	_button.flat = true
	_button.focus_mode = Control.FOCUS_NONE
	_button.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_button)
	_button.button_down.connect(func(): pressed.emit())
	_button.button_up.connect(func(): released.emit())
	_apply_size()
	_apply_text()
	_apply_style()


func _set_key_size(v: Vector2) -> void:
	key_size = v
	if is_inside_tree(): _apply_size()


func _set_key_text(v: String) -> void:
	key_text = v
	if is_inside_tree(): _apply_text()


func _set_highlighted(v: bool) -> void:
	highlighted = v
	if is_inside_tree(): _apply_style()


func _apply_size() -> void:
	if not _button: return
	_button.position = Vector2.ZERO
	_button.size = key_size


func _apply_text() -> void:
	if not _button: return
	_button.text = key_text


func _apply_style() -> void:
	if not _button: return
	var bg := key_highlight if highlighted else key_normal
	var fg := text_highlight if highlighted else text_normal
	var sb := StyleBoxFlat.new()
	sb.bg_color = bg
	sb.corner_radius_top_left = 4
	sb.corner_radius_top_right = 4
	sb.corner_radius_bottom_left = 4
	sb.corner_radius_bottom_right = 4
	sb.border_width_left = 1
	sb.border_width_top = 1
	sb.border_width_right = 1
	sb.border_width_bottom = 1
	sb.border_color = Color(1.0, 1.0, 1.0, 0.35)
	_button.add_theme_stylebox_override("normal", sb)
	_button.add_theme_stylebox_override("hover", sb)
	_button.add_theme_stylebox_override("pressed", sb)
	_button.add_theme_stylebox_override("focus", sb)
	_button.add_theme_color_override("font_color", fg)
	_button.add_theme_color_override("font_hover_color", fg)
	_button.add_theme_color_override("font_pressed_color", fg)
