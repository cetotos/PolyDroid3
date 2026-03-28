// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using MemoryPack;
using Polytoria.Attributes;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Services;
using Polytoria.Networking;
using Polytoria.Shared;
using Polytoria.Utils;
using Polytoria.Utils.Compression;
using Polytoria.Utils.DTOs;
using System.Collections.Generic;
using System.Text.Json;
using static Polytoria.Datamodel.Services.NetworkService;

namespace Polytoria.Client.Networking;

[Internal]
public partial class NetworkTransformSync : Instance
{
	private const double BatchInterval = 0.05;

	internal NetworkService NetService = null!;
	private readonly Dictionary<string, KeyValuePair<Transform3D, int>> _pendingTransforms = [];

	// Pending Transform batch update
	private readonly Dictionary<string, PendingBatchTransform> _pendingBatchUpdate = [];
	private double _batchTimer = 0.0;

	private static readonly bool _useNetworkLog = false;

	static NetworkTransformSync()
	{
		if (Globals.IsInGDEditor) return;
		_useNetworkLog = OS.HasFeature("netlog");
	}

	public override void Process(double delta)
	{
		base.Process(delta);
		if (NetService.IsServer)
		{
			_batchTimer += delta;

			if (_batchTimer >= BatchInterval)
			{
				if (_pendingBatchUpdate.Count > 0)
					BroadcastBatchedTransforms();
				_pendingTransforms.Clear();
				_batchTimer = 0.0;
			}
		}
	}

	public void SyncAllTransformToPeer(int peerID)
	{
		NetworkedObject[] allNetObjs = NetService.Root.GetReplicateDescendants();

		byte[] rawData = ZstdCompressionUtils.Compress(JsonSerializer.Serialize(PackTransforms(allNetObjs), NetDataGenerationContext.Default.ListNetBatchTransformData).ToUtf8Buffer());
		RpcId(peerID, nameof(NetRecvAllTransform), rawData, true);
	}

	public void SendChunk(NetworkedObject[] netObjs, Player plr)
	{
		byte[] rawData = ZstdCompressionUtils.Compress(JsonSerializer.Serialize(PackTransforms(netObjs), NetDataGenerationContext.Default.ListNetBatchTransformData).ToUtf8Buffer());
		RpcId(plr.PeerID, nameof(NetRecvAllTransform), rawData, false);
	}

	public void BroadcastChunk(NetworkedObject[] netObjs)
	{
		byte[] rawData = ZstdCompressionUtils.Compress(JsonSerializer.Serialize(PackTransforms(netObjs), NetDataGenerationContext.Default.ListNetBatchTransformData).ToUtf8Buffer());
		Rpc(nameof(NetRecvAllTransform), rawData, false);
	}

	private static List<NetBatchTransformData> PackTransforms(NetworkedObject[] netObjs)
	{
		List<NetBatchTransformData> data = [];
		foreach (NetworkedObject item in netObjs)
		{
			if (item is Dynamic dyn)
			{
				Transform3D transform3D = dyn.GetLocalTransform();
				data.Add(new()
				{
					NetID = dyn.NetworkedObjectID,
					Value = new Transform3DDto(transform3D)
				});
			}
		}
		return data;
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetRecvAllTransform(byte[] rawBytes, bool isFirstInit)
	{
		List<NetBatchTransformData> netObjsData = JsonSerializer.Deserialize(ZstdCompressionUtils.Decompress(rawBytes), NetDataGenerationContext.Default.ListNetBatchTransformData)!;

		foreach (NetBatchTransformData item in netObjsData)
		{
			if (_pendingTransforms.ContainsKey(item.NetID)) { continue; }
			RecvUpdateTransformHandler(item.NetID, item.Value.ToTransform3D(), 1, true, false);
		}

		if (isFirstInit)
		{
			NetService.NetTransformSyncd();
		}
	}

	public void SendUpdateTransform(Dynamic dyn, bool isReliable = false, int sendTo = 0, bool lerpTransform = false)
	{
		// If not ready, return
		if (!dyn.IsNetworkReady) return;
		if (!dyn.Root.IsLoaded) return;

		// If is in creator, return
		if (NetService.NetworkMode == NetworkModeEnum.Creator) return;

		// Check if self has the network authority
		if (!CheckDynAuthor(dyn, NetService.LocalPeerID)) return;

		Transform3D current = dyn.GetLocalTransform();
		string objID = dyn.NetworkedObjectID;

		if (sendTo != 0)
		{
			if (isReliable)
			{
				RpcId(sendTo, nameof(NetRecvUpdateTransformReliable), objID, current, lerpTransform);
			}
			else
			{
				RpcId(sendTo, nameof(NetRecvUpdateTransform), objID, current, lerpTransform);
			}
		}
		else
		{
			if (isReliable)
			{
				if (_useNetworkLog) { PT.Print($"[Net] [Transform] {dyn.NetworkPath} Reliable update"); }

				Rpc(nameof(NetRecvUpdateTransformReliable), objID, current, lerpTransform);
			}
			else
			{
				Rpc(nameof(NetRecvUpdateTransform), objID, current, lerpTransform);
			}
		}
	}


	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.UnreliableOrdered)]
	private void NetRecvUpdateTransform(string objID, Transform3D transform, bool lerpTransform)
	{
		RecvUpdateTransformHandler(objID, transform, RemoteSenderId, false, lerpTransform);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.Reliable)]
	private void NetRecvUpdateTransformReliable(string objID, Transform3D transform, bool lerpTransform)
	{
		RecvUpdateTransformHandler(objID, transform, RemoteSenderId, true, lerpTransform);
	}

	private void RecvUpdateTransformHandler(string objID, Transform3D transform, int fromPeer, bool isReliable, bool lerpTransform)
	{
		if (NetService.Root.GetNetObjectFromID(objID) is Dynamic dyn)
		{
			dyn.UpdateTransformFromNet(dyn.TransformNetworkPass(fromPeer, transform), isReliable, lerpTransform);
		}
		else
		{
			if (_useNetworkLog) { PT.Print($"[Net] [Transform] [?] {objID} Pending"); }
			_pendingTransforms[objID] = new(transform, fromPeer);
		}
	}

	private static bool CheckDynAuthor(Dynamic dyn, int fromPeer)
	{
		return CheckAuthority(fromPeer, dyn.NetworkAuthority) || CheckAuthority(fromPeer, dyn.NetTransformAuthority);
	}

	internal void ApplyPendingTransforms(Dynamic dyn)
	{
		string objID = dyn.NetworkedObjectID;

		if (_pendingTransforms.TryGetValue(objID, out KeyValuePair<Transform3D, int> pending))
		{
			if (!CheckDynAuthor(dyn, pending.Value))
			{
				return;
			}
			dyn.UpdateTransformFromNet(pending.Key, true, false);
			_pendingTransforms.Remove(objID);
		}
	}

	public void SendTransformToServer(Dynamic dyn, bool lerpTransform = false)
	{
		// Return if not ready
		if (!dyn.IsNetworkReady || !dyn.Root.IsLoaded) return;

		// Ignore in creator
		if (NetService.NetworkMode == NetworkModeEnum.Creator) return;

		// Check authority
		if (!CheckDynAuthor(dyn, NetService.LocalPeerID)) return;

		Transform3D current = dyn.GetLocalTransform();
		string objID = dyn.NetworkedObjectID;

		RpcId(1, nameof(NetRecvTransformOnServer), objID, current, lerpTransform);
	}

	public void BroadcastTransformFromServer(Dynamic dyn, bool lerpTransform, int excludePeer = -1)
	{
		if (!NetService.IsServer) return;
		if (!dyn.IsNetworkReady) return;
		string objID = dyn.NetworkedObjectID;

		_pendingBatchUpdate[objID] = new(dyn, lerpTransform, excludePeer);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.UnreliableOrdered, CallLocal = false)]
	private void NetRecvTransformOnServer(string objID, Transform3D transform, bool lerpTransform)
	{
		int fromPeer = RemoteSenderId;

		if (NetService.Root.GetNetObjectFromID(objID) is Dynamic dyn)
		{
			if (!CheckDynAuthor(dyn, fromPeer))
			{
				PT.PrintErr($"[Net] Unauthorized transform from peer {fromPeer} for {objID}");
				return;
			}

			// server-side validation
			if (!dyn.TransformNetworkCheck(transform))
			{
				PT.PrintErr($"[Net] Invalid transform from peer {fromPeer}");

				// Send correction back
				SendUpdateTransform(dyn, true, fromPeer);
				return;
			}
			Transform3D processed = dyn.TransformNetworkPass(fromPeer, transform);

			// If is equal approx to last, return
			if (processed.IsEqualApprox(dyn.GetLocalTransform())) return;

			// Update on server
			dyn.UpdateTransformFromNet(dyn.TransformNetworkPass(fromPeer, transform), false, lerpTransform);

			// Add to batch pending
			_pendingBatchUpdate[objID] = new(dyn, lerpTransform, fromPeer);
		}
	}

	private void BroadcastBatchedTransforms()
	{
		if (NetService.NetInstance == null) return;

		Dictionary<int, List<BatchTransformData>> batchesByExcludedPeer = [];

		foreach (var (k, pending) in _pendingBatchUpdate)
		{
			if (pending.Dyn.IsDeleted)
			{
				_pendingBatchUpdate.Remove(k);
				continue;
			}
			BatchTransformData batchData = new(
				k,
				pending.Dyn.GetLocalTransform(),
				pending.LerpTransform
			);

			int excludePeer = pending.ExcludePeer;

			// Add to batches for all peers except the excluded one
			foreach (int peerID in NetService.NetInstance.PeerIds)
			{
				if (peerID == excludePeer)
					continue;

				if (!batchesByExcludedPeer.ContainsKey(peerID))
					batchesByExcludedPeer[peerID] = [];

				batchesByExcludedPeer[peerID].Add(batchData);
			}
		}

		// Send batches to each peer
		foreach (var (peerID, batch) in batchesByExcludedPeer)
		{
			if (batch.Count > 0)
			{
				RpcId(peerID, nameof(NetRecvBatchedTransforms), SerializeUtils.Serialize<BatchTransformData[]>([.. batch]));
			}
		}
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.UnreliableOrdered)]
	private void NetRecvBatchedTransforms(byte[] transformsRaw)
	{
		BatchTransformData[]? transforms = SerializeUtils.Deserialize<BatchTransformData[]>(transformsRaw);
		if (transforms == null) return;
		foreach (var data in transforms)
		{
			if (NetService.Root.GetNetObjectFromID(data.ObjID) is Dynamic dyn)
			{
				dyn.UpdateTransformFromNet(Transform3DDto.FromString(data.Transform).ToTransform3D(), false, data.Lerp);
			}
		}
	}

	private struct PendingBatchTransform(Dynamic dyn, bool lerpTransform, int excludePeer)
	{
		public Dynamic Dyn = dyn;
		public bool LerpTransform = lerpTransform;
		public int ExcludePeer = excludePeer;
	}

	[MemoryPackable]
	public partial class BatchTransformData
	{
		public string ObjID = null!;
		public string Transform = null!;
		public bool Lerp = false;

		[MemoryPackConstructor]
		public BatchTransformData() { }

		public BatchTransformData(string objID, Transform3D transform, bool lerp)
		{
			ObjID = objID;
			Transform = Transform3DDto.ToString(transform);
			Lerp = lerp;
		}
	}
}
