// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client;
using Polytoria.Shared;
using System;

namespace Polytoria.Mobile.UI;

public partial class ViewHomePage : MobileViewBase
{
	private const string UsernameLabelPath = "ScrollContainer/VBoxContainer/Control/Layout/Username";
	private const string ContinueRootPath = "ScrollContainer/VBoxContainer/PanelContainer/Layout/Continue/VBoxContainer2";
	private const string ContentLayoutPath = "ScrollContainer/VBoxContainer/PanelContainer/Layout";
	private const string LocalServerCacheDir = "user://local_server_cache";

	private Label? _usernameLabel;
	private ContinueRoot? _continueRoot;

	public override void _Ready()
	{
		_usernameLabel = GetNodeOrNull<Label>(UsernameLabelPath);
		_continueRoot = GetNodeOrNull<ContinueRoot>(ContinueRootPath);
		AddLocalPlayButton();
		RefreshUsername();
	}

	private void AddLocalPlayButton()
	{
		Node? layout = GetNodeOrNull(ContentLayoutPath);
		if (layout == null)
		{
			return;
		}
		Button button = new() { Text = "Play .poly File" };
		button.Pressed += OnPlayLocalPressed;
		layout.AddChild(button);
		layout.MoveChild(button, 0);
	}

	private void OnPlayLocalPressed()
	{
		DisplayServer.FileDialogShow(
			"Select a Polytoria place",
			"",
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			["*.poly;Polytoria Place"],
			Callable.From<bool, string[], int>((status, paths, _) =>
			{
				if (!status || paths == null || paths.Length == 0)
				{
					return;
				}
				string resolved = ResolvePickedPath(paths[0]);
				if (string.IsNullOrEmpty(resolved))
				{
					return;
				}
				MobileUI.Singleton.LaunchLocalServer(resolved);
			}));
	}

	private static string ResolvePickedPath(string raw)
	{
		if (string.IsNullOrEmpty(raw) || !raw.StartsWith("content://"))
		{
			return raw;
		}
		DirAccess.MakeDirRecursiveAbsolute(LocalServerCacheDir);
		string decoded = Uri.UnescapeDataString(raw);
		string fileName = decoded.GetFile();
		if (string.IsNullOrEmpty(fileName))
		{
			fileName = $"pick_{Time.GetUnixTimeFromSystem():F0}.poly";
		}
		string localResPath = $"{LocalServerCacheDir}/{fileName}";
		using FileAccess src = FileAccess.Open(raw, FileAccess.ModeFlags.Read);
		if (src == null)
		{
			PT.PrintErr($"Local server: failed to read picked file {raw} (err {FileAccess.GetOpenError()})");
			return "";
		}
		long len = (long)src.GetLength();
		byte[] bytes = src.GetBuffer(len);
		using FileAccess dst = FileAccess.Open(localResPath, FileAccess.ModeFlags.Write);
		if (dst == null)
		{
			PT.PrintErr($"Local server: failed to cache picked file to {localResPath} (err {FileAccess.GetOpenError()})");
			return "";
		}
		dst.StoreBuffer(bytes);
		dst.Flush();
		return ProjectSettings.GlobalizePath(localResPath);
	}

	public override void ShowView(object? args)
	{
		RefreshUsername();
		_continueRoot?.Refresh();
		base.ShowView(args);
	}

	private void RefreshUsername()
	{
		if (_usernameLabel == null) return;
		string name = UsernameStore.Username;
		_usernameLabel.Text = string.IsNullOrWhiteSpace(name) ? "Guest" : name;
	}
}
