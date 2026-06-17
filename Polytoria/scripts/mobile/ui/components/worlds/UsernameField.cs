// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;

namespace Polytoria.Mobile.UI;

public partial class UsernameField : LineEdit
{
	private const int UsernameMaxLength = 20;

	public override void _Ready()
	{
		MaxLength = UsernameMaxLength;
		PlaceholderText = "Username";
		Text = UsernameStore.Username;

		TextChanged += OnTextChanged;
		FocusExited += OnFinalize;
		TextSubmitted += _ => OnFinalize();
	}

	private void OnTextChanged(string newText)
	{
		UsernameStore.Username = newText.Trim();
	}

	private async void OnFinalize()
	{
		string typed = Text.Trim();
		if (string.IsNullOrEmpty(typed))
		{
			UsernameStore.UserId = 0;
			return;
		}
		try
		{
			APIUserInfo info = await PolyAPI.FindUserByUsername(typed);
			if (info.Id > 0)
			{
				UsernameStore.UserId = info.Id;
				if (!string.IsNullOrEmpty(info.Username) && info.Username != typed)
				{
					Text = info.Username;
					UsernameStore.Username = info.Username;
				}
			}
			else
			{
				UsernameStore.UserId = 0;
			}
		}
		catch (Exception ex)
		{
			UsernameStore.UserId = 0;
			PT.PrintErr($"username resolve failed: {ex.Message}");
		}
	}
}
