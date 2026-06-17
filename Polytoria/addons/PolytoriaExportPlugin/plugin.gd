@tool
extends EditorPlugin

const TOOL_MENU_BUILD_XR = "Build Android XR"
const TOOL_MENU_BUILD = "Build Android"
const TOOL_MENU_BUILD_BOTH = "Build Android (All)"
const TOOL_MENU_BUILD_LINUX = "Build Linux"
const TOOL_MENU_BUILD_WINDOWS = "Build Windows"
const TOOL_MENU_BUILD_ALL = "Build All"
const TOOL_MENU_BUILD_CREATOR_ANDROID = "Build Creator Android"

const BUILD_PARALLELISM = 32

const PRESET_XR = "Client Android XR APK"
const PRESET_FLAT = "Client Android APK"
const PRESET_LINUX = "Client Linux"
const PRESET_WINDOWS = "Client Windows"
const PRESET_CREATOR_ANDROID = "Creator Android APK"

const PRESET_INFO = {
	PRESET_XR:               { "dir": "android-build", "ext": ".apk",    "suffix": "-XR" },
	PRESET_FLAT:             { "dir": "android-build", "ext": ".apk",    "suffix": "" },
	PRESET_LINUX:            { "dir": "linux-build",   "ext": ".x86_64", "suffix": "-Linux" },
	PRESET_WINDOWS:          { "dir": "windows-build", "ext": ".exe",    "suffix": "-Windows" },
	PRESET_CREATOR_ANDROID:  { "dir": "android-build", "ext": ".apk",    "suffix": "-Creator" },
}

const ANDROID_RID = "linux-bionic-arm64"
const LINUX_RID = "linux-x64"
const WINDOWS_RID = "win-x64"
const CSPROJ_RES_PATH = "res://Polytoria.csproj"
const PUBLISH_OUT_REL = "res://.godot/mono/temp/bin/ExportDebug/linux-bionic-arm64/publish/Polytoria.so"
const GRADLE_LIBS_REL = "res://android/build/libs/debug/arm64-v8a/"

const PRESET_AOT = {
	PRESET_LINUX:   { "rid": LINUX_RID,   "platform": "linuxbsd", "data_dir": "data_Polytoria_linuxbsd_x86_64", "bin_name": "Polytoria.so",  "config": "ExportRelease" },
	PRESET_WINDOWS: { "rid": WINDOWS_RID, "platform": "windows",  "data_dir": "data_Polytoria_windows_x86_64", "bin_name": "Polytoria.dll", "config": "ExportRelease" },
}
# I am aware its sort of stupid to just use WSL when some people might just not have WSL, or the specific distro/setup, or maybe not even be on Windows,
# but i doubt anyone will try to build the Linux client themselves, hopefully nobody does because it wont work.
const WSL_DISTRO = "Ubuntu-24.04"
const WSL_DOTNET_HOME = "$HOME/.dotnet10"

var mobile_export_plugin : PolytoriaMobileExportPlugin
var dllcpy_export_plugin : PolytoriaDllCpyExportPlugin
var execpy_export_plugin : PolytoriaExeCpyExportPlugin
var export_config_plugin : PolytoriaConfigExportPlugin

func _enter_tree():
	mobile_export_plugin = PolytoriaMobileExportPlugin.new()
	dllcpy_export_plugin = PolytoriaDllCpyExportPlugin.new()
	execpy_export_plugin = PolytoriaExeCpyExportPlugin.new()
	export_config_plugin = PolytoriaConfigExportPlugin.new()
	add_export_plugin(mobile_export_plugin)
	add_export_plugin(dllcpy_export_plugin)
	add_export_plugin(execpy_export_plugin)
	add_export_plugin(export_config_plugin)

	add_tool_menu_item(TOOL_MENU_BUILD, _on_build_flat)
	add_tool_menu_item(TOOL_MENU_BUILD_XR, _on_build_xr)
	add_tool_menu_item(TOOL_MENU_BUILD_CREATOR_ANDROID, _on_build_creator_android)
	add_tool_menu_item(TOOL_MENU_BUILD_BOTH, _on_build_both)
	add_tool_menu_item(TOOL_MENU_BUILD_WINDOWS, _on_build_windows)
	add_tool_menu_item(TOOL_MENU_BUILD_LINUX, _on_build_linux)
	add_tool_menu_item(TOOL_MENU_BUILD_ALL, _on_build_all)


func _exit_tree():
	remove_tool_menu_item(TOOL_MENU_BUILD)
	remove_tool_menu_item(TOOL_MENU_BUILD_XR)
	remove_tool_menu_item(TOOL_MENU_BUILD_CREATOR_ANDROID)
	remove_tool_menu_item(TOOL_MENU_BUILD_BOTH)
	remove_tool_menu_item(TOOL_MENU_BUILD_WINDOWS)
	remove_tool_menu_item(TOOL_MENU_BUILD_LINUX)
	remove_tool_menu_item(TOOL_MENU_BUILD_ALL)

	remove_export_plugin(mobile_export_plugin)
	remove_export_plugin(export_config_plugin)
	remove_export_plugin(execpy_export_plugin)
	remove_export_plugin(dllcpy_export_plugin)
	mobile_export_plugin = null
	dllcpy_export_plugin = null
	execpy_export_plugin = null
	export_config_plugin = null


func _on_build_xr():
	var prev_path: Variant = _push_ndk_path()
	if prev_path == null:
		return
	if _dotnet_publish():
		_export_preset(PRESET_XR)
	OS.set_environment("PATH", prev_path)


func _on_build_flat():
	var prev_path: Variant = _push_ndk_path()
	if prev_path == null:
		return
	if _dotnet_publish():
		_export_preset(PRESET_FLAT)
	OS.set_environment("PATH", prev_path)


func _on_build_both():
	var prev_path: Variant = _push_ndk_path()
	if prev_path == null:
		return
	if _dotnet_publish():
		for preset_name in [PRESET_XR, PRESET_FLAT, PRESET_CREATOR_ANDROID]:
			var ok := _export_preset(preset_name)
			if not ok:
				push_error("aborting batch! %s failed" % preset_name)
				break
	OS.set_environment("PATH", prev_path)


func _on_build_linux():
	if _dotnet_publish_desktop(PRESET_LINUX):
		_export_preset(PRESET_LINUX)


func _on_build_windows():
	if _dotnet_publish_desktop(PRESET_WINDOWS):
		_export_preset(PRESET_WINDOWS)


func _on_build_creator_android():
	var prev_path: Variant = _push_ndk_path()
	if prev_path == null:
		return
	if _dotnet_publish():
		_export_preset(PRESET_CREATOR_ANDROID)
	OS.set_environment("PATH", prev_path)


func _on_build_all():
	var prev_path: Variant = _push_ndk_path()
	if prev_path == null:
		return
	print("Build All started")
	print("dotnet publish android")
	if not _dotnet_publish():
		push_error("android publish failed! aborting Build All")
		OS.set_environment("PATH", prev_path)
		return

	var sequence: Array = [PRESET_XR, PRESET_FLAT, PRESET_CREATOR_ANDROID, PRESET_LINUX, PRESET_WINDOWS]
	var results: Dictionary = {}
	for preset_name in sequence:
		print("exporting ... %s" % preset_name)
		if PRESET_AOT.has(preset_name):
			if not _dotnet_publish_desktop(preset_name):
				results[preset_name] = "PUBLISH FAILED"
				push_error("%s publish failed! aborting remaining exports" % preset_name)
				break
		var ok := _export_preset(preset_name)
		results[preset_name] = "OK" if ok else "FAILED"
		if not ok:
			push_error("%s failed! aborting remaining exports" % preset_name)
			break

	OS.set_environment("PATH", prev_path)

	print("!!Build All Done!!")
	for p in sequence:
		print("  %s: %s" % [p, results.get(p, "skipped")])


func _push_ndk_path() -> Variant:
	var ndk_root: String = OS.get_environment("ANDROID_NDK_ROOT")
	if ndk_root == "":
		push_error("ANDROID_NDK_ROOT not set! cannot build")
		return null
	var ndk_bin: String = ndk_root.replace("\\", "/").path_join("toolchains/llvm/prebuilt/windows-x86_64/bin")
	if not FileAccess.file_exists(ndk_bin.path_join("clang.exe")):
		push_error("NDK clang not found at %s" % ndk_bin)
		return null
	var prev: String = OS.get_environment("PATH")
	OS.set_environment("PATH", "%s;%s" % [ndk_bin.replace("/", "\\"), prev])
	return prev


func _output_path_for(preset_name: String) -> String:
	var info: Dictionary = PRESET_INFO.get(preset_name, {})
	var dir_name: String = info.get("dir", "build")
	var ext: String = info.get("ext", "")
	var suffix: String = info.get("suffix", "")
	var version: String = str(ProjectSettings.get_setting("application/config/version", "0.0.0"))
	var filename: String = "PolyDroid3-%s%s%s" % [version, suffix, ext]
	return ProjectSettings.globalize_path("res://../out/%s/%s" % [dir_name, filename])


func _dotnet_publish() -> bool:
	var csproj: String = ProjectSettings.globalize_path(CSPROJ_RES_PATH)
	var args: PackedStringArray = [
		"publish", csproj,
		"-c", "ExportDebug",
		"-r", ANDROID_RID,
		"-p:GodotTargetPlatform=android",
		"-m:%d" % BUILD_PARALLELISM,
		"-p:IlcMaxThreads=%d" % BUILD_PARALLELISM,
		"-p:BuildInParallel=true",
		"--self-contained",
	]
	print("dotnet publish ...")
	var out: Array = []
	var exit_code: int = OS.execute("dotnet", args, out, true)
	for line in out:
		print(line)
	if exit_code != 0:
		push_error("dotnet publish failed (exit %d)" % exit_code)
		return false
	var native_abs: String = ProjectSettings.globalize_path(PUBLISH_OUT_REL)
	var dbg_abs: String = ProjectSettings.globalize_path("res://.godot/mono/temp/bin/ExportDebug/linux-bionic-arm64/native/Polytoria.so.dbg")
	var source_abs: String = dbg_abs if FileAccess.file_exists(dbg_abs) else native_abs
	if not FileAccess.file_exists(source_abs):
		push_error("publish ok but %s missing" % source_abs)
		return false
	var sz: int = FileAccess.get_file_as_bytes(source_abs).size()
	print("publish ok -> %d bytes (from %s)" % [sz, source_abs])
	var gradle_libs: String = ProjectSettings.globalize_path(GRADLE_LIBS_REL)
	DirAccess.make_dir_recursive_absolute(gradle_libs)
	var gradle_so: String = gradle_libs.path_join("Polytoria.so")
	var copy_err: int = DirAccess.copy_absolute(source_abs, gradle_so)
	if copy_err != OK:
		push_error("failed to stage Polytoria.so into %s (err %d)" % [gradle_so, copy_err])
		return false
	print("staged Polytoria.so -> %s" % gradle_so)
	return true


func _dotnet_publish_desktop(preset_name: String) -> bool:
	var info: Dictionary = PRESET_AOT.get(preset_name, {})
	if info.is_empty():
		return true
	var rid: String = info["rid"]
	var platform: String = info["platform"]
	var config: String = info["config"]

	var csproj_win: String = ProjectSettings.globalize_path(CSPROJ_RES_PATH)
	var out: Array = []
	var exit_code: int

	if rid == LINUX_RID and OS.get_name() == "Windows":
		var csproj_wsl: String = "/mnt/" + csproj_win.substr(0, 1).to_lower() + "/" + csproj_win.substr(3).replace("\\", "/")
		var sh: String = 'export PATH="%s:$PATH"; export DOTNET_ROOT="%s"; dotnet publish "%s" -c %s -r %s -p:GodotTargetPlatform=%s -p:IsMobile=true -p:IsCreator=true --self-contained' % [WSL_DOTNET_HOME, WSL_DOTNET_HOME, csproj_wsl, config, rid, platform]
		print("wsl dotnet publish (%s) ..." % rid)
		exit_code = OS.execute("wsl", ["-d", WSL_DISTRO, "--", "bash", "-c", sh], out, true)
	else:
		var args: PackedStringArray = [
			"publish", csproj_win,
			"-c", config,
			"-r", rid,
			"-p:GodotTargetPlatform=" + platform,
			"-p:IsMobile=true",
			"-p:IsCreator=true",
			"-m:%d" % BUILD_PARALLELISM,
			"-p:IlcMaxThreads=%d" % BUILD_PARALLELISM,
			"-p:BuildInParallel=true",
			"--self-contained",
		]
		print("dotnet publish (%s) ..." % rid)
		exit_code = OS.execute("dotnet", args, out, true)

	for line in out:
		print(line)
	if exit_code != 0:
		push_error("dotnet publish for %s failed (exit %d)" % [rid, exit_code])
		return false

	var publish_dir: String = ProjectSettings.globalize_path("res://.godot/mono/temp/bin/%s/%s/publish" % [config, rid])
	var bin_path: String = publish_dir.path_join(info["bin_name"])
	if not FileAccess.file_exists(bin_path):
		push_error("publish ok but %s missing" % bin_path)
		return false
	var sz: int = FileAccess.get_file_as_bytes(bin_path).size()
	print("publish ok %s -> %d bytes" % [info["bin_name"], sz])
	return true


func _strip_data_folder_to_aot(output_abs: String, preset_name: String) -> void:
	var info: Dictionary = PRESET_AOT.get(preset_name, {})
	if info.is_empty():
		return
	var publish_dir: String = ProjectSettings.globalize_path("res://.godot/mono/temp/bin/%s/%s/publish" % [info["config"], info["rid"]])
	var bin_path: String = publish_dir.path_join(info["bin_name"])
	if not FileAccess.file_exists(bin_path):
		push_warning("AOT publish output missing for %s!" % info["rid"])
		return

	var data_folder: String = output_abs.get_base_dir().path_join(info["data_dir"])
	DirAccess.make_dir_recursive_absolute(data_folder)

	print("stripping %s -> publish contents from %s" % [data_folder, publish_dir])
	for f in DirAccess.get_files_at(data_folder):
		DirAccess.remove_absolute(data_folder.path_join(f))
	for f in DirAccess.get_files_at(publish_dir):
		DirAccess.copy_absolute(publish_dir.path_join(f), data_folder.path_join(f))


func _export_preset(preset_name: String) -> bool:
	var output_abs: String = _output_path_for(preset_name)
	var output_dir: String = output_abs.get_base_dir()
	DirAccess.make_dir_recursive_absolute(output_dir)

	if FileAccess.file_exists(output_abs):
		DirAccess.remove_absolute(output_abs)

	var godot_exe: String = OS.get_executable_path()
	var project_dir: String = ProjectSettings.globalize_path("res://")
	var args: PackedStringArray = [
		"--headless",
		"--path", project_dir,
		"--export-debug", preset_name,
		output_abs,
	]
	print("exporting '%s' -> %s" % [preset_name, output_abs])
	var out: Array = []
	var exit_code: int = OS.execute(godot_exe, args, out, true)
	for line in out:
		print(line)
	if not FileAccess.file_exists(output_abs):
		return false
	var sz: int = FileAccess.get_file_as_bytes(output_abs).size()
	print("%s -> %d bytes" % [output_abs, sz])
	_strip_data_folder_to_aot(output_abs, preset_name)
	return true
