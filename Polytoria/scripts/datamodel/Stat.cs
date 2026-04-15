// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using MemoryPack;
using Polytoria.Attributes;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Utils;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Stat : Instance
{
	private string _displayName = "";

	internal Dictionary<Player, object?> PlayerToStat = [];
	public PTSignal<Player, object?> PlayerStatChanged = new();

	[Editable, ScriptProperty, DefaultValue("")]
	public string DisplayName
	{
		get => _displayName;
		set
		{
			_displayName = value;
			OnPropertyChanged();
		}
	}

	[ScriptMethod]
	public string GetDisplayName()
	{
		return _displayName == string.Empty ? Name : _displayName;
	}

	public override void Ready()
	{
		Root.Players.PlayerRemoved.Connect(OnPlayerRemoved);
		if (!Root.Network.IsServer)
			RpcId(1, nameof(NetReqPlayerStats));

		base.Ready();
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetReqPlayerStats()
	{
		List<PlayerStatData> data = [];
		foreach (var (player, obj) in PlayerToStat)
		{
			PlayerStatData s = new() { UserID = player.UserID };
			if (obj is double d)
			{
				s.NumberValue = d;
			}
			else if (obj is string str)
			{
				s.StringValue = str;
			}
			data.Add(s);
		}
		byte[] raw = SerializeUtils.Serialize<PlayerStatData[]>([.. data]);
		RpcId(RemoteSenderId, nameof(NetRecvPlayerStats), raw);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetRecvPlayerStats(byte[] raw)
	{
		var stats = SerializeUtils.Deserialize<PlayerStatData[]>(raw) ?? [];
		foreach (var stat in stats)
		{
			Player? plr = Root.Players.GetPlayerByID(stat.UserID);
			if (plr != null)
			{
				if (stat.StringValue != null)
				{
					InternalSet(plr, stat.StringValue);
				}
				else if (stat.NumberValue != null)
				{
					InternalSet(plr, stat.NumberValue.Value);
				}
			}
		}
	}

	private void OnPlayerRemoved(Player plr)
	{
		PlayerToStat.Remove(plr);
	}

	[ScriptMethod]
	public void Set(Player player, double val)
	{
		InternalSet(player, val);
		if (HasAuthority)
			Rpc(nameof(NetSetDouble), player.UserID, val);
	}

	[ScriptMethod]
	public void Set(Player player, string val)
	{
		InternalSet(player, val);
		if (HasAuthority)
			Rpc(nameof(NetSetString), player.UserID, val);
	}

	internal void InternalSet(Player player, double val)
	{
		PlayerToStat[player] = val;
		PlayerStatChanged?.Invoke(player, val);
		player.StatChanged.Invoke(this, val);
	}

	internal void InternalSet(Player player, string val)
	{
		PlayerToStat[player] = val;
		PlayerStatChanged?.Invoke(player, val);
		player.StatChanged.Invoke(this, val);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetSetDouble(int playerID, double val)
	{
		Player? plr = Root.Players.GetPlayerByID(playerID);
		if (plr != null)
		{
			InternalSet(plr, val);
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetSetString(int playerID, string val)
	{
		Player? plr = Root.Players.GetPlayerByID(playerID);
		if (plr != null)
		{
			InternalSet(plr, val);
		}
	}

	[ScriptMethod]
	public object? Get(Player player)
	{
		if (PlayerToStat.TryGetValue(player, out var val))
		{
			return val;
		}
		return null;
	}

	[ScriptMethod]
	public double GetTotalForTeam(Team team)
	{
		double total = 0;
		var plrs = team.GetPlayers();

		foreach (var plr in plrs)
		{
			var val = Get(plr);
			if (val is double d)
			{
				total += d;
			}
		}

		return total;
	}

	[ScriptMethod]
	public string GetDisplayValue(Player plr)
	{
		object? val = Get(plr);
		string displayTxt = "N/A";

		if (val != null)
		{
			if (val is double d)
			{
				displayTxt = d.ToKMB();
			}
			else
			{
				displayTxt = val.ToString() ?? "N/A";
			}
		}

		return displayTxt;
	}

	[MemoryPackable]
	public partial struct PlayerStatData
	{
		public int UserID;
		public string? StringValue;
		public double? NumberValue;
	}
}
