// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client;
using Polytoria.Datamodel.Data;
using Polytoria.Networking;
using Polytoria.Schemas.Debugger;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Polytoria.Datamodel.Services;

[Static("Worlds")]
[ExplorerExclude]
[SaveIgnore]
public sealed partial class WorldsService : Instance
{
	private const string WorldAPINonServerMsg = "Worlds API can only be called on server";
	private readonly Dictionary<string, MessageNewServerResponse> _testServers = [];

	[ScriptMethod]
	public async Task<string?> NewServerAsync(string placePath)
	{
		return await NewServerAsync(new NewServerRequestData() { PlacePath = placePath });
	}

	[ScriptMethod]
	public async Task<string?> NewServerAsync(NewServerRequestData data)
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException(WorldAPINonServerMsg);
		if (Root.IsLocalTest)
		{
			if (Root.Entry == null) throw new Exception("No client entry");
			if (Root.Entry.DebugAgent == null) throw new Exception("Debugger not attached, could not start new server");

			string newID = Guid.NewGuid().ToString();
			MessageNewServerResponse newServer = await Root.Entry.DebugAgent.CreateServerInstance(data.PlacePath);
			_testServers.Add(newID, newServer);
			return newID;
		}
		return null;
	}

	[ScriptMethod]
	public async Task JoinPlaceAsync(Player plr, string to)
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException(WorldAPINonServerMsg);
		if (plr.teleporting) throw new Exception("Player is already teleporting");
		plr.teleporting = true;
		if (Root.IsLocalTest)
		{
			if (Root.Entry == null) throw new Exception("No client entry");
			if (Root.Entry.DebugAgent == null) throw new Exception("Debugger not attached, could not start new server");

			MessageNewServerResponse newServer = await Root.Entry.DebugAgent.CreateServerInstance(to);
			_ = TeleportPlayerToTest(plr, newServer);
		}
	}

	[ScriptMethod]
	public async Task JoinPlacePartyAsync(Player[] plrs, string to)
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException(WorldAPINonServerMsg);

		if (Root.IsLocalTest)
		{
			if (Root.Entry == null) throw new Exception("No client entry");
			if (Root.Entry.DebugAgent == null) throw new Exception("Debugger not attached, could not start new server");

			MessageNewServerResponse newServer = await Root.Entry.DebugAgent.CreateServerInstance(to);
			foreach (Player plr in plrs)
			{
				if (plr.teleporting) continue;
				_ = TeleportPlayerToTest(plr, newServer);
			}
		}
	}

	[ScriptMethod]
	public async Task JoinPrivateAsync(Player plr, string accessID)
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException(WorldAPINonServerMsg);

		if (Root.IsLocalTest)
		{
			if (_testServers.TryGetValue(accessID, out MessageNewServerResponse? res))
			{
				if (plr.teleporting) throw new Exception("Player is already teleporting");
				plr.teleporting = true;
				if (Root.IsLocalTest)
				{
					_ = TeleportPlayerToTest(plr, res);
				}
			}
			else
			{
				throw new InvalidOperationException($"Private instance {accessID} not found");
			}
		}
	}

	[ScriptMethod]
	public async Task JoinPrivatePartyAsync(Player[] players, string accessID)
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException(WorldAPINonServerMsg);

		if (Root.IsLocalTest)
		{
			if (_testServers.TryGetValue(accessID, out MessageNewServerResponse? res))
			{
				foreach (Player plr in players)
				{
					if (plr.teleporting) continue;
					_ = TeleportPlayerToTest(plr, res);
				}
			}
			else
			{
				throw new InvalidOperationException($"Private instance {accessID} not found");
			}
		}
	}

	private async Task TeleportPlayerToTest(Player plr, MessageNewServerResponse newServer)
	{
		plr.teleporting = true;
		RpcId(plr.PeerID, nameof(NetRecvJoinTestPlace), newServer.Address, newServer.Port, newServer.DebugID);

		// Disconnect the player
		await Globals.Singleton.WaitAsync(1);
		Root.Network.DisconnectPeer(plr.PeerID, "Teleporting to other world", NetworkService.DisconnectionCodeEnum.Teleport);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetRecvJoinTestPlace(string address, int port, string debugID)
	{
		PT.Print("Teleporting to Test: ", address, ":", port);
		int userID = Root.Players.LocalPlayer.UserID;
		Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
		if (app is ClientEntry ce)
		{
			ClientEntry.ClientEntryData entryData = new()
			{
				ConnectAddress = address,
				ConnectPort = port,
				TestIsServer = false,
				TestUserID = userID,
				TestDebugID = debugID
			};
			ce.Entry(entryData);
		}
	}
}
