// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using System.Collections.Generic;

namespace Polytoria.Client.UI;

public sealed partial class UIMenuPlayers : UIMenuViewBase
{
	[Export] private Control _playerCardContainer = null!;

	private Players _players = null!;
	private PackedScene _playerCardScene = null!;
	private readonly Dictionary<Player, UIPlayerCard> _playerCards = [];

	public override void _Ready()
	{
		_playerCardScene = GD.Load<PackedScene>("res://scenes/client/ui/menu/components/player_card.tscn");
		_players = CoreUIRoot.Singleton.Root.Players;
		_players.PlayerReady += AddPlayer;
		_players.PlayerRemoved.Connect(RemovePlayer);
		foreach (Instance item in _players.GetPlayers())
		{
			if (item is Player plr)
			{
				AddPlayer(plr);
			}
		}
		base._Ready();
	}

	private void AddPlayer(Player player)
	{
		if (_playerCards.ContainsKey(player))
			return; // Player card already exists

		UIPlayerCard card = _playerCardScene.Instantiate<UIPlayerCard>();
		card.TargetPlayer = player;

		_playerCardContainer.AddChild(card);
		_playerCards[player] = card;
	}

	private void RemovePlayer(Player player)
	{
		if (!_playerCards.TryGetValue(player, out UIPlayerCard? card))
			return; // Player card doesn't exist

		card.QueueFree();
		_playerCards.Remove(player);
	}
}
