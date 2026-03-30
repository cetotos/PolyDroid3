// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Creator;
using Polytoria.Schemas.Debugger;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Polytoria.Creator;

public class DebugServer
{
	public static int Port { get; set; } = 24111;
	public static bool ServerStarted { get; private set; } = false;
	private static TcpListener _server = null!;

	private static readonly List<TcpClient> _tcpClients = [];
	private static readonly Dictionary<TcpClient, ClientData> _clientToData = [];
	private static readonly Dictionary<string, TcpClient> _idToClient = [];

	private static readonly Dictionary<string, TaskCompletionSource> _pendingServerInstance = [];

	public static void Start()
	{
		if (ServerStarted) return;
		IPAddress localAddr = IPAddress.Parse("127.0.0.1");
		_server = new TcpListener(localAddr, Port);
		_server.Start();
		_ = Task.Run(ServerMainLoop);
		ServerStarted = true;

		PT.Print($"-- Debug server started at {localAddr}:{Port} --");
	}

	private static async Task ServerMainLoop()
	{
		while (ServerStarted)
		{
			TcpClient client = await _server.AcceptTcpClientAsync();
			PT.Print("Debug client connected");
			_ = Task.Run(() => HandleClient(client));
		}
	}

	private static async Task HandleClient(TcpClient client)
	{
		_tcpClients.Add(client);
		try
		{
			NetworkStream stream = client.GetStream();
			byte[] buffer = new byte[1024];

			while (ServerStarted)
			{
				int bytesRead = await stream.ReadAsync(buffer);

				if (bytesRead == 0)
				{
					break; // Client disconnected gracefully
				}

				IDebugMessage? msg = SerializeUtils.Deserialize<IDebugMessage>(buffer);
				if (msg != null)
				{
					try
					{
						await OnMessageRecv(client, msg);
					}
					catch (Exception ex)
					{
						PT.PrintErr(ex);
					}
				}
			}
		}
		finally
		{
			client.Close();
			PT.Print("Debug client disconnected");
			_clientToData.Remove(client);
			_tcpClients.Remove(client);
			foreach ((string id, TcpClient c) in _idToClient)
			{
				if (c == client)
				{
					_idToClient.Remove(id);
					break;
				}
			}
		}
	}

	private static async Task OnMessageRecv(TcpClient from, IDebugMessage msg)
	{
		if (msg is MessageClientData data)
		{
			if (data.DebugID == null) return;
			PT.Print("has reported client data! ", data.DebugID);

			_clientToData.Add(from, new()
			{
				DebugID = data.DebugID
			});
		}
		else if (msg is MessageReportProcess procRe)
		{
			if (procRe.ProcessID != 0)
			{
				CreatorService.Singleton.LocalTestProcesses.Add(procRe.ProcessID);
			}
		}
		else if (msg is MessageLogDispatch log)
		{
			PT.DispatchLog(new() { Content = log.Content, LogFrom = log.LogFrom, LogType = log.LogType });
		}
		else if (msg is MessageNewServerRequest req)
		{
			if (_clientToData.TryGetValue(from, out ClientData cdata))
			{
				CreatorSession session = CreatorService.LocalTestIDToSession[cdata.DebugID];
				PT.Print("Server start request: ", req.WorldPath);
				PT.Print(session);
				string placePath = req.WorldPath;
				string originPlacePath = placePath;
				if (!placePath.EndsWith(".poly")) placePath += ".poly";

				// call on main thread
				Callable.From(() =>
				{
					// worrrkkkkarounnddd
					async void a()
					{
						try
						{
							int port = GD.RandRange(20000, 30000);

							TaskCompletionSource tcs = new();

							_pendingServerInstance.Add(cdata.DebugID, tcs);

							await CreatorService.Singleton.StartLocalTestOnEntry(session.ProjectFolderPath, placePath, cdata.DebugID, port, true);

							PT.Print("awaiting server start..");
							await tcs.Task;
							PT.Print("server started!");

							SendMessage(from, new MessageNewServerResponse() { WorldPath = originPlacePath, Address = "127.0.0.1", Port = port, DebugID = cdata.DebugID });
						}
						catch (Exception ex)
						{
							OS.Alert(ex.Message);
						}
					}
					a();
				}).CallDeferred();
			}
			else
			{
				PT.Print("Got no client data");
			}
		}
		else if (msg is MessageServerReady serverReady)
		{
			if (_clientToData.TryGetValue(from, out ClientData cdata))
			{
				if (_pendingServerInstance.TryGetValue(cdata.DebugID, out TaskCompletionSource? tcs))
				{
					PT.Print("Resolve server start");
					tcs.SetResult();
				}
				else
				{
					PT.Print(cdata.DebugID, " not found");
				}
			}
		}
	}

	public static async void BroadcastMessage(IDebugMessage msg)
	{
		PT.Print("Server Broadcast message ", msg);
		byte[] data = SerializeUtils.Serialize(msg);
		foreach (TcpClient client in _tcpClients)
		{
			NetworkStream stream = client.GetStream();
			await stream.WriteAsync(data);
		}
	}

	public static async void SendMessage(TcpClient client, IDebugMessage msg)
	{
		PT.Print("Server Sending message ", msg);
		byte[] data = SerializeUtils.Serialize(msg);
		NetworkStream stream = client.GetStream();
		await stream.WriteAsync(data);
	}

	public static void SendTerminateProgram()
	{
		BroadcastMessage(new MessageShutdown());
	}

	private struct ClientData
	{
		public string DebugID;
	}
}
