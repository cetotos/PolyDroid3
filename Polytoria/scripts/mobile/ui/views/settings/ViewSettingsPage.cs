// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client;
using Polytoria.Client.Settings;
using Polytoria.Shared;
using Polytoria.Shared.Settings;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Polytoria.Mobile.UI;

public partial class ViewSettingsPage : MobileViewBase
{
	private const string DevLaunchPath = "user://devlaunch";

	[Export] private OptionButton _themeColorOption = null!;
	[Export] private LineEdit _devAddressField = null!;
	[Export] private Button _devConnectButton = null!;
	[Export] private Button _restartButton = null!;
	[Export] private Label _versionLabel = null!;

	private const string ClientSettingsPath = "user://settings_client.json";
	private const int EnumIdOffset = 1;
	private OptionButton _renderMethodOption = null!;
	private OptionButton _graphicsApiOption = null!;
	private OptionButton _uiScaleOption = null!;

	private static readonly float[] UiScaleValues = [0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f];

	private DevLaunchOptions _launchOptions = new();

	public override void _Ready()
	{
		_themeColorOption.Clear();
		_themeColorOption.AddItem("Dark", (int)MobileThemeColor.Dark);
		_themeColorOption.AddItem("Blue", (int)MobileThemeColor.Blue);
		_themeColorOption.Selected = _themeColorOption.GetItemIndex((int)MobileSettingsStore.ThemeColor);
		_themeColorOption.ItemSelected += OnThemeColorSelected;

		if (FileAccess.FileExists(DevLaunchPath))
		{
			_launchOptions = JsonSerializer.Deserialize(FileAccess.GetFileAsString(DevLaunchPath), DevLaunchOptionsGenerationContext.Default.DevLaunchOptions)!;
		}
		_devAddressField.Text = _launchOptions.ConnectAddress;
		_devConnectButton.Pressed += OnDevConnectPressed;
		_restartButton.Pressed += OnRestartPressed;

		_versionLabel.Text = $"v{Globals.AppVersion}";

		StyleAsTitle(GetNodeOrNull<Label>("ScrollContainer/PanelContainer/Layout/AppearanceHeader"));
		StyleAsTitle(GetNodeOrNull<Label>("ScrollContainer/PanelContainer/Layout/DeveloperHeader"));

		if (!Globals.IsMobileBuild)
			BuildRendererOptions();
		BuildUiScaleOption();
	}

	private static void StyleAsTitle(Label? label)
	{
		if (label == null)
		{
			return;
		}
		label.AddThemeFontSizeOverride("font_size", 28);
		label.AddThemeColorOverride("font_color", new Color(0.984f, 0.988f, 1f));

		Font themeFont = label.GetThemeDefaultFont();
		Font baseFont = themeFont is FontVariation fv && fv.BaseFont != null ? fv.BaseFont : themeFont;
		FontVariation title = new() { BaseFont = baseFont };
		Godot.Collections.Dictionary variation = new();
		variation[TextServerManager.GetPrimaryInterface().NameToTag("wght")] = 800;
		title.VariationOpentype = variation;
		label.AddThemeFontOverride("font", title);
	}

	private void BuildRendererOptions()
	{
		Node themeRow = _themeColorOption.GetParent();
		Node layout = themeRow.GetParent();
		int at = themeRow.GetIndex() + 1;

		Label header = new() { Text = "Graphics" };
		layout.AddChild(header);
		layout.MoveChild(header, at++);
		StyleAsTitle(header);

		_renderMethodOption = new OptionButton();
		_renderMethodOption.AddItem("Auto", (int)RenderingMethodOption.Auto + EnumIdOffset);
		_renderMethodOption.AddItem("Standard", (int)RenderingMethodOption.Standard + EnumIdOffset);
		_renderMethodOption.AddItem("Performance", (int)RenderingMethodOption.Performance + EnumIdOffset);
		_renderMethodOption.AddItem("Compatibility", (int)RenderingMethodOption.Compatibility + EnumIdOffset);
		HBoxContainer methodRow = MakeSettingRow("Rendering Method", _renderMethodOption);
		layout.AddChild(methodRow);
		layout.MoveChild(methodRow, at++);

		_graphicsApiOption = new OptionButton();
		_graphicsApiOption.AddItem("Auto", (int)GraphicsApiOption.Auto + EnumIdOffset);
		_graphicsApiOption.AddItem("Vulkan", (int)GraphicsApiOption.Vulkan + EnumIdOffset);
		_graphicsApiOption.AddItem("Direct3D 12", (int)GraphicsApiOption.Direct3D12 + EnumIdOffset);
		HBoxContainer apiRow = MakeSettingRow("Graphics API", _graphicsApiOption);
		layout.AddChild(apiRow);
		layout.MoveChild(apiRow, at++);

		Label note = new() { Text = "Restart required to apply changes." };
		note.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.6f));
		note.AddThemeFontSizeOverride("font_size", 14);
		layout.AddChild(note);
		layout.MoveChild(note, at++);

		RenderingMethodOption curMethod = GetSetting(SharedSettingKeys.Graphics.RenderingMethod, RenderingMethodOption.Auto);
		int methodIndex = _renderMethodOption.GetItemIndex((int)curMethod + EnumIdOffset);
		_renderMethodOption.Selected = methodIndex >= 0 ? methodIndex : _renderMethodOption.GetItemIndex((int)RenderingMethodOption.Auto + EnumIdOffset);

		GraphicsApiOption curApi = GetSetting(SharedSettingKeys.Graphics.GraphicsApi, GraphicsApiOption.Auto);
		int apiIndex = _graphicsApiOption.GetItemIndex((int)curApi + EnumIdOffset);
		_graphicsApiOption.Selected = apiIndex >= 0 ? apiIndex : _graphicsApiOption.GetItemIndex((int)GraphicsApiOption.Auto + EnumIdOffset);

		_renderMethodOption.ItemSelected += OnRenderMethodSelected;
		_graphicsApiOption.ItemSelected += OnGraphicsApiSelected;
		RefreshApiEnabled();
	}

	private static HBoxContainer MakeSettingRow(string labelText, OptionButton option)
	{
		HBoxContainer row = new();
		row.AddThemeConstantOverride("separation", 12);

		Label label = new()
		{
			Text = labelText,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
		};
		row.AddChild(label);

		option.CustomMinimumSize = new Vector2(200, 0);
		option.AddThemeConstantOverride("align_to_largest_stylebox", 1);
		row.AddChild(option);

		return row;
	}

	private void OnRenderMethodSelected(long index)
	{
		RenderingMethodOption value = (RenderingMethodOption)(_renderMethodOption.GetItemId((int)index) - EnumIdOffset);
		SetSetting(SharedSettingKeys.Graphics.RenderingMethod, value);
		RefreshApiEnabled();
	}

	private void OnGraphicsApiSelected(long index)
	{
		GraphicsApiOption value = (GraphicsApiOption)(_graphicsApiOption.GetItemId((int)index) - EnumIdOffset);
		SetSetting(SharedSettingKeys.Graphics.GraphicsApi, value);
	}

	private void RefreshApiEnabled()
	{
		bool compat = _renderMethodOption.GetSelectedId() == (int)RenderingMethodOption.Compatibility + EnumIdOffset;
		_graphicsApiOption.Disabled = compat;
		_graphicsApiOption.TooltipText = compat ? "Compatibility always uses OpenGL." : "Vulkan is required for ray tracing.";
	}

	private void BuildUiScaleOption()
	{
		Node themeRow = _themeColorOption.GetParent();
		Node layout = themeRow.GetParent();

		_uiScaleOption = new OptionButton();
		foreach (float value in UiScaleValues)
		{
			_uiScaleOption.AddItem(value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x", (int)Mathf.Round(value * 100f));
		}
		HBoxContainer scaleRow = MakeSettingRow("Mobile UI Scale", _uiScaleOption);
		layout.AddChild(scaleRow);
		layout.MoveChild(scaleRow, themeRow.GetIndex() + 1);

		int currentIndex = _uiScaleOption.GetItemIndex((int)Mathf.Round(MobileSettingsStore.UiScale * 100f));
		_uiScaleOption.Selected = currentIndex >= 0 ? currentIndex : _uiScaleOption.GetItemIndex(100);
		_uiScaleOption.ItemSelected += OnUiScaleSelected;
	}

	private void OnUiScaleSelected(long index)
	{
		float value = _uiScaleOption.GetItemId((int)index) / 100f;
		GetTree().Root.ContentScaleFactor = (Globals.IsMobileBuild ? Globals.MobileScale : 1f) * value;
		MobileSettingsStore.UiScale = value;
	}

	private static T GetSetting<T>(string key, T fallback) where T : struct, System.Enum
	{
		if (ClientSettingsService.Instance != null)
			return ClientSettingsService.Instance.Get<T>(key);
		string? s = ReadSettingJson(key);
		if (s != null && System.Enum.TryParse(s, true, out T parsed)) return parsed;
		return fallback;
	}

	private static void SetSetting<T>(string key, T value) where T : struct, System.Enum
	{
		if (ClientSettingsService.Instance != null)
			ClientSettingsService.Instance.Set(key, value);
		else
			WriteSettingJson(key, value.ToString());
	}

	private static string? ReadSettingJson(string key)
	{
		if (!FileAccess.FileExists(ClientSettingsPath)) return null;
		string json = FileAccess.GetFileAsString(ClientSettingsPath);
		if (string.IsNullOrEmpty(json)) return null;
		try
		{
			JsonObject? obj = JsonNode.Parse(json)?.AsObject();
			if (obj != null && obj.TryGetPropertyValue(key, out JsonNode? node) && node is JsonValue val && val.TryGetValue(out string? s))
				return s;
		}
		catch { }
		return null;
	}

	private static void WriteSettingJson(string key, string value)
	{
		JsonObject obj = new();
		if (FileAccess.FileExists(ClientSettingsPath))
		{
			string json = FileAccess.GetFileAsString(ClientSettingsPath);
			if (!string.IsNullOrEmpty(json))
			{
				try { obj = JsonNode.Parse(json)?.AsObject() ?? new JsonObject(); }
				catch { obj = new JsonObject(); }
			}
		}
		obj[key] = value;
		using FileAccess f = FileAccess.Open(ClientSettingsPath, FileAccess.ModeFlags.Write);
		if (f != null)
		{
			f.StoreString(obj.ToJsonString());
			f.Close();
		}
	}

	private void OnThemeColorSelected(long index)
	{
		int id = _themeColorOption.GetItemId((int)index);
		MobileSettingsStore.ThemeColor = (MobileThemeColor)id;
	}

	private void OnRestartPressed()
	{
		Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.MobileUI);
	}

	private void OnDevConnectPressed()
	{
		_launchOptions.ConnectAddress = _devAddressField.Text;
		using FileAccess devlaunch = FileAccess.Open(DevLaunchPath, FileAccess.ModeFlags.Write);
		devlaunch.StoreString(JsonSerializer.Serialize(_launchOptions, DevLaunchOptionsGenerationContext.Default.DevLaunchOptions));
		devlaunch.Close();

		Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
		if (app is ClientEntry ce)
		{
			ce.Entry(new ClientEntry.ClientEntryData { ConnectAddress = _launchOptions.ConnectAddress });
		}
	}

	[JsonSerializable(typeof(DevLaunchOptions))]
	internal partial class DevLaunchOptionsGenerationContext : JsonSerializerContext { }

	internal struct DevLaunchOptions
	{
		[JsonInclude]
		public string ConnectAddress = "";

		public DevLaunchOptions() { }
	}
}
