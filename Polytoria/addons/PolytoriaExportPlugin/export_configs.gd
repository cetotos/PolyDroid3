@tool
extends EditorExportPlugin
class_name PolytoriaConfigExportPlugin

var original_settings := {}

func _get_name() -> String:
	return "ExportConfigs"

func _export_begin(features: PackedStringArray, is_debug: bool, path: String, flags: int) -> void:
	print("[PTCONFIG] Applying settings...")

	# Save original settings before changing
	_store_original("display/window/size/mode")
	_store_original("debug/settings/stdout/verbose_stdout")
	_store_original("rendering/renderer/rendering_method")
	
	# Apply overrides
	ProjectSettings.set_setting("display/window/size/mode", DisplayServer.WindowMode.WINDOW_MODE_MAXIMIZED)
	if features.has("beta"):
		ProjectSettings.set_setting("debug/settings/stdout/verbose_stdout", true)
	
	# Lower the rendering method for arm
	if features.has("arm64"):
		ProjectSettings.set_setting("rendering/renderer/rendering_method", "mobile")
	
	ProjectSettings.save()

func _export_end() -> void:
	if not original_settings.is_empty():
		print("[PTCONFIG] Restoring original settings...")

		for key in original_settings.keys():
			if original_settings[key] == null:
				ProjectSettings.clear(key)
			else:
				ProjectSettings.set_setting(key, original_settings[key])

		ProjectSettings.save()
		original_settings.clear()

func _store_original(key: String) -> void:
	if not original_settings.has(key):
		if ProjectSettings.has_setting(key):
			original_settings[key] = ProjectSettings.get_setting(key)
		else:
			original_settings[key] = null
