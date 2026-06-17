extends "res://addons/xr_keyboard/virtual_key.gd"

@export var scan_code_text := ""
@export var unicode := 0
@export var shift_modifier := false

var _keyboard


func _ready() -> void:
	super()
	var p := get_parent()
	while p != null and not p.has_method("on_key_pressed"):
		p = p.get_parent()
	_keyboard = p
	pressed.connect(_on_pressed)
	released.connect(_on_released)


func _on_pressed() -> void:
	highlighted = true
	if _keyboard:
		_keyboard.on_key_pressed(scan_code_text, unicode, shift_modifier)


func _on_released() -> void:
	highlighted = false
