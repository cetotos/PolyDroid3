// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client;
using Polytoria.Schemas.API;
using Polytoria.Utils;
using System.Collections.Generic;

namespace Polytoria.Mobile.UI;

public partial class ContinueRoot : Control
{
	private const string PlaceCardPath = "res://scenes/mobile/components/shared/place_card.tscn";
	private const int CardMinWidth = 170;
	private const int HSeparation = 12;

	private PackedScene _placeCardPacked = null!;

	public override void _Ready()
	{
		_placeCardPacked = GD.Load<PackedScene>(PlaceCardPath);
		GetViewport().SizeChanged += UpdateColumns;
		Resized += UpdateColumns;
		CallDeferred(MethodName.Refresh);
		CallDeferred(MethodName.UpdateColumns);
	}

	private void UpdateColumns()
	{
		float width = GetAvailableWidth();
		int cols = Mathf.Max(1, Mathf.FloorToInt((width + HSeparation) / (CardMinWidth + HSeparation)));
		Set("columns", cols);
	}

	private float GetAvailableWidth()
	{
		Node? n = GetParent();
		ScrollContainer? topScroll = null;
		while (n != null)
		{
			if (n is ScrollContainer sc) topScroll = sc;
			n = n.GetParent();
		}
		return topScroll?.Size.X ?? Size.X;
	}

	public void Refresh()
	{
		foreach (Node child in GetChildren()) child.QueueFree();

		List<APIWorldsData> recent = RecentPlacesStore.GetRecent();
		foreach (APIWorldsData item in recent)
		{
			PlaceCard card = _placeCardPacked.Instantiate<PlaceCard>();
			card.PlaceData = item;
			AddChild(card);
			RefreshLivePlayerCount(card);
		}
	}

	private async void RefreshLivePlayerCount(PlaceCard card)
	{
		int id = card.PlaceData.Id;
		if (id <= 0) return;
		try
		{
			APIPlaceInfo info = await PolyAPI.GetWorldFromID(id);
			if (IsInstanceValid(card)) card.SetPlaying(info.Playing);
		}
		catch
		{
		}
	}
}
