// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;
using System.Collections.Generic;
using System.Linq;

namespace Polytoria.Client.UI.Playerlist;

public partial class UILeaderboard : Control
{
	private const int LeaderboardMaxHeight = 300;
	private const string ItemPath = "res://scenes/client/ui/playerlist/leaderboard_user_item.tscn";
	private const string TeamItemPath = "res://scenes/client/ui/playerlist/leaderboard_team_item.tscn";
	private PackedScene _itemPacked = null!;

	[Export] private Control _container = null!;
	[Export] private Control _layout = null!;
	[Export] private UIUserCard _userCard = null!;
	[Export] public UILeaderboardUserOptions UserOptions = null!;
	public CoreUIRoot CoreUI = null!;

	private Datamodel.Stats Stats => CoreUI.Root.Stats;
	private Teams Teams => CoreUI.Root.Teams;

	public bool IsLeaderboardShown = true;

	private readonly Dictionary<Player, UILeaderboardUserItem> _playerToItem = [];
	private readonly Dictionary<Team, UILeaderboardTeamItem> _teamToItem = [];
	private Players _players = null!;
	private int _shownPlrCount = 0;
	private bool _queueResort = false;

	public override void _Ready()
	{
		_players = CoreUI.Root.Players;

		_players.PlayerReady += AddPlayer;
		_players.PlayerRemoved.Connect(RemovePlayer);

		Stats.StatAdded.Connect(StatChanged);
		Stats.StatRemoved.Connect(StatChanged);

		Teams.TeamAdded.Connect(TeamChanged);
		Teams.TeamRemoved.Connect(TeamChanged);

		Teams.TeamUpdateDispatch += QueueSortList;

		Refresh();
		LeaderboardUpdate();
	}

	public override void _ExitTree()
	{
		_players.PlayerReady -= AddPlayer;
		_players.PlayerRemoved.Disconnect(RemovePlayer);

		Stats.StatAdded.Disconnect(StatChanged);
		Stats.StatRemoved.Disconnect(StatChanged);

		Teams.TeamAdded.Disconnect(TeamChanged);
		Teams.TeamAdded.Disconnect(TeamChanged);

		Teams.TeamUpdateDispatch -= QueueSortList;

		base._ExitTree();
	}

	public override void _Process(double delta)
	{
		if (_queueResort)
		{
			_queueResort = false;
			SortList();
		}
		base._Process(delta);
	}

	private void StatChanged(Stat _)
	{
		Refresh();
	}

	private void TeamChanged(Team _)
	{
		Refresh();
	}

	public void Refresh()
	{
		foreach (var item in _playerToItem)
		{
			item.Value.QueueFree();
		}
		foreach (var item in _teamToItem)
		{
			item.Value.QueueFree();
		}
		_playerToItem.Clear();
		_teamToItem.Clear();

		foreach (Player plr in _players.GetPlayers())
		{
			AddPlayer(plr);
		}
		foreach (Team team in Teams.GetTeams())
		{
			AddTeam(team);
		}
		SortList();
	}

	private void AddPlayer(Player player)
	{
		if (player.IsLocal || _playerToItem.ContainsKey(player))
			return;

		UILeaderboardUserItem card = Globals.CreateInstanceFromScene<UILeaderboardUserItem>(ItemPath);
		card.TargetPlayer = player;
		card.Leaderboard = this;

		_layout.AddChild(card);

		foreach (var st in Stats.GetStats())
		{
			card.AddStat(st);
		}

		_playerToItem[player] = card;
		_shownPlrCount++;
		LeaderboardUpdate();
	}

	private void AddTeam(Team team)
	{
		UILeaderboardTeamItem card = Globals.CreateInstanceFromScene<UILeaderboardTeamItem>(TeamItemPath);
		card.TargetTeam = team;
		card.Leaderboard = this;

		_layout.AddChild(card);

		foreach (var st in Stats.GetStats())
		{
			card.AddStat(st);
		}

		_teamToItem[team] = card;
		LeaderboardUpdate();
	}

	private void RemovePlayer(Player player)
	{
		if (!_playerToItem.TryGetValue(player, out UILeaderboardUserItem? card))
			return;

		card.QueueFree();
		_playerToItem.Remove(player);
		_shownPlrCount--;
		LeaderboardUpdate();
	}

	public void SortList()
	{
		Stat? stat = Stats.FindChildByIndex(0) as Stat;

		// Create a list to hold all items (teams and players) with sorting info
		var allItems = new List<(string TeamName, double? Value, int ItemType, Node Item)>();

		// Add team items
		foreach (var kvp in _teamToItem)
		{
			var teamName = kvp.Key.Name ?? string.Empty;
			var value = stat?.GetTotalForTeam(kvp.Key);
			allItems.Add((teamName, value, 0, kvp.Value));
		}

		// Add player items
		foreach (var kvp in _playerToItem)
		{
			var teamName = kvp.Key.Team?.Name ?? string.Empty;
			var value = stat?.Get(kvp.Key) as double?;
			allItems.Add((teamName, value, 1, kvp.Value));
		}

		// Sort: by team name, then teams before players, then by stat value if exists
		var sortedItems = allItems.OrderBy(x => x.TeamName).ThenBy(x => x.ItemType);

		if (stat != null)
		{
			sortedItems = sortedItems
				.ThenByDescending(x => x.Value.HasValue)
				.ThenByDescending(x => x.Value ?? double.MinValue);
		}

		var itemsList = sortedItems.ToList();

		// Reorder the UI items
		for (int i = 0; i < itemsList.Count; i++)
		{
			itemsList[i].Item.GetParent()?.MoveChild(itemsList[i].Item, i);
		}
	}

	public void QueueSortList()
	{
		_queueResort = true;
	}

	private async void LeaderboardUpdate()
	{
		if (_shownPlrCount == 0)
		{
			_container.Visible = false;
		}
		else
		{
			_container.Visible = true;
		}

		// Resize based on container, need to be resized on next frame
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		float ys = _layout.Size.Y + 16;

		if (ys > LeaderboardMaxHeight)
		{
			ys = LeaderboardMaxHeight;
		}

		_container.Size = new(_userCard.Size.X, ys);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		_container.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.KeepSize);
	}
}
