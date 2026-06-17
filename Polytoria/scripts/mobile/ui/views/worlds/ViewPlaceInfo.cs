// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client;
using Polytoria.Mobile;
using Polytoria.Shared;
using Polytoria.Shared.AssetLoaders;
using System;
using System.Net.Http;
using System.Text.Json;

namespace Polytoria.Mobile.UI;

public partial class ViewPlaceInfo : MobileViewBase
{
	[Export] private Button _playButton = null!;
	[Export] private Label _genreLabel = null!;
	[Export] private Label _placeNameLabel = null!;
	[Export] private LinkButton _creatorNameLabel = null!;
	private Label _descriptionLabel = null!;
	[Export] private TextureRect _thumbnailRect = null!;
	[Export] private Control _thumbnailGradient = null!;

	private static readonly PTHttpClient _http = new();

	private int _worldID;
	private string _creatorUsername = "";
	private string _placeName = "";
	private int _playing;
	private double? _rating;

	public override void _Ready()
	{
		_descriptionLabel = GetNodeOrNull<Label>("ScrollContainer/VBoxContainer/PanelContainer/Layout/Description")!;
		if (_descriptionLabel == null)
		{
			foreach (Node n in GetTree().Root.GetNode(GetPath()).GetChildren())
			{
				FindDescription(n);
				if (_descriptionLabel != null) break;
			}
		}
		if (_playButton != null) _playButton.Pressed += OnPlayButtonPressed;
		if (_creatorNameLabel != null) _creatorNameLabel.Pressed += OnCreatorNamePressed;

		ApplyThemeColor();
		MobileSettingsStore.ThemeColorChanged += ApplyThemeColor;
	}

	public override void _ExitTree()
	{
		MobileSettingsStore.ThemeColorChanged -= ApplyThemeColor;
		base._ExitTree();
	}

	private void ApplyThemeColor()
	{
		Color bg = MobileSettingsStore.GetBgColor();
		Set("color", bg);
		if (_thumbnailGradient != null) _thumbnailGradient.Modulate = bg;
	}

	private void FindDescription(Node n)
	{
		if (n.Name == "Description" && n is Label l) { _descriptionLabel = l; return; }
		foreach (Node c in n.GetChildren()) { FindDescription(c); if (_descriptionLabel != null) return; }
	}

	private void OnPlayButtonPressed()
	{
		RecentPlacesStore.Add(_worldID, _placeName, _creatorUsername, _playing, _rating);
		MobileUI.Singleton.LaunchGame(_worldID);
	}

	private void OnCreatorNamePressed()
	{
		if (string.IsNullOrWhiteSpace(_creatorUsername)) return;
		OS.ShellOpen($"https://polytoria.com/u/{Uri.EscapeDataString(_creatorUsername)}");
	}

	public override async void ShowView(object? args)
	{
		try
		{
			await ShowViewInner(args);
		}
		catch (Exception ex)
		{
			PT.PrintErr("ViewPlaceInfo top-level: ", ex);
			ShowPopup("ViewPlaceInfo crashed", ex.ToString());
		}
	}

	private async System.Threading.Tasks.Task ShowViewInner(object? args)
	{
		base.ShowView(args);
		_worldID = (int)args!;

		Stage("init");
		SafeSetText(_genreLabel, "…");
		SafeSetText(_placeNameLabel, "Loading…");
		SafeSetText(_creatorNameLabel, "");
		SafeSetText(_descriptionLabel, "");
		_creatorUsername = "";

		MobileUI.Singleton.LoadingScreen?.ShowScreen();
		Stage("bind-check");
		if (_genreLabel == null || _placeNameLabel == null || _creatorNameLabel == null
			|| _descriptionLabel == null || _thumbnailRect == null || _playButton == null)
		{
			MobileUI.Singleton.LoadingScreen?.HideScreen();
			ShowPopup("ViewPlaceInfo bind failed",
				$"playBtn={_playButton != null} genre={_genreLabel != null} name={_placeNameLabel != null} " +
				$"creator={_creatorNameLabel != null} desc={_descriptionLabel != null} thumb={_thumbnailRect != null}");
			return;
		}

		string url = Globals.ApiEndpoint + "v1/places/" + _worldID;
		Stage("fetch: " + url);
		string json;
		try
		{
			using HttpRequestMessage msg = new(HttpMethod.Get, url);
			msg.Headers.TryAddWithoutValidation("Accept", "application/json");
			using HttpResponseMessage resp = await _http.SendAsync(msg);
			Stage($"resp: {(int)resp.StatusCode}");
			resp.EnsureSuccessStatusCode();
			json = await resp.Content.ReadAsStringAsync();
		}
		catch (Exception ex)
		{
			MobileUI.Singleton.LoadingScreen?.HideScreen();
			ShowPopup("Fetch failed", $"{url}\n\n{ex.GetType().Name}: {ex.Message}");
			return;
		}

		Stage($"parse: {json.Length}b");
		string name = "", description = "", genre = "", creatorName = "", thumbnail = "";
		int playing = 0;
		double? rating = null;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;
			if (root.TryGetProperty("name", out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String)
				name = nameEl.GetString() ?? "";
			if (root.TryGetProperty("description", out JsonElement descEl) && descEl.ValueKind == JsonValueKind.String)
				description = descEl.GetString() ?? "";
			if (root.TryGetProperty("genre", out JsonElement genreEl) && genreEl.ValueKind == JsonValueKind.String)
				genre = genreEl.GetString() ?? "";
			if (root.TryGetProperty("thumbnail", out JsonElement thumbEl) && thumbEl.ValueKind == JsonValueKind.String)
				thumbnail = thumbEl.GetString() ?? "";
			if (root.TryGetProperty("playing", out JsonElement playingEl) && playingEl.ValueKind == JsonValueKind.Number)
				playing = playingEl.GetInt32();
			if (root.TryGetProperty("rating", out JsonElement ratingEl) && ratingEl.ValueKind == JsonValueKind.Number)
				rating = ratingEl.GetDouble();
			if (root.TryGetProperty("creator", out JsonElement creatorEl) && creatorEl.ValueKind == JsonValueKind.Object
				&& creatorEl.TryGetProperty("name", out JsonElement cNameEl) && cNameEl.ValueKind == JsonValueKind.String)
				creatorName = cNameEl.GetString() ?? "";
		}
		catch (Exception ex)
		{
			MobileUI.Singleton.LoadingScreen?.HideScreen();
			ShowPopup("Parse failed", $"{ex.GetType().Name}: {ex.Message}\n\nbody:\n{json}");
			return;
		}

		Stage("apply");
		_creatorUsername = creatorName;
		_placeName = name;
		_playing = playing;
		_rating = rating;
		SafeSetText(_genreLabel, genre);
		SafeSetText(_placeNameLabel, name);
		SafeSetText(_creatorNameLabel, "By " + (string.IsNullOrEmpty(creatorName) ? "Unknown" : creatorName));
		SafeSetText(_descriptionLabel, string.IsNullOrWhiteSpace(description) ? "No description." : description);

		if (!string.IsNullOrEmpty(thumbnail) && WebAssetLoader.Singleton != null)
		{
			try
			{
				WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = thumbnail }, OnThumbnailLoaded);
			}
			catch (Exception ex)
			{
				PT.PrintErr("ViewPlaceInfo thumbnail: ", ex);
			}
		}

		MobileUI.Singleton.LoadingScreen?.HideScreen();
		Stage("done");
	}

	private static void SafeSetText(Label? label, string text) { if (label != null) label.Text = text; }
	private static void SafeSetText(LinkButton? btn, string text) { if (btn != null) btn.Text = text; }

	private void Stage(string s) { }

	private void ShowPopup(string title, string body)
	{
		AcceptDialog dlg = new()
		{
			Title = title,
			DialogText = body,
			Exclusive = true,
		};
		dlg.Confirmed += () => dlg.QueueFree();
		dlg.Canceled += () => dlg.QueueFree();
		AddChild(dlg);
		dlg.PopupCentered();
	}

	private void OnThumbnailLoaded(Resource resource)
	{
		if (resource is Texture2D tex && _thumbnailRect != null) _thumbnailRect.Texture = tex;
	}
}
