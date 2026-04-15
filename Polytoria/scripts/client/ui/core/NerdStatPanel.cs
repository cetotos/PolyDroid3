// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Humanizer;
using Polytoria.Datamodel;
using Polytoria.Shared.AssetLoaders;
using System;
using System.Collections.Generic;

namespace Polytoria.Client.UI;

public partial class NerdStatPanel : Control
{
	private readonly HashSet<Action> _actions = [];
	private Control _layout = null!;
	private World _root = null!;

	public override void _Ready()
	{
		_root = CoreUIRoot.Singleton.Root;
		_layout = GetNode<Control>("Layout");
		Visible = false;
		ClientSettings.Singleton.OnSettingChanged += OnSettingChanged;
		UpdateVisible();

		CreateLabel("FPS", () =>
		{
			return Engine.GetFramesPerSecond().ToString();
		});
		CreateLabel("Ping", () =>
		{
			return (_root.Players.LocalPlayer?.NetworkPing ?? 0) + "ms";
		});
		CreateLabel("Time Process", () =>
		{
			return Math.Round(Performance.Singleton.GetMonitor(Performance.Monitor.TimeProcess) * 1000) + "ms";
		});
		CreateLabel("Physics Process", () =>
		{
			return Math.Round(Performance.Singleton.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000) + "ms";
		});
		CreateDivider();
		CreateLabel("Nodes", () =>
		{
			return Performance.Singleton.GetMonitor(Performance.Monitor.ObjectNodeCount).ToString();
		});
		CreateLabel("Orphan Nodes", () =>
		{
			return Performance.Singleton.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount).ToString();
		});
		CreateLabel("Resources", () =>
		{
			return Performance.Singleton.GetMonitor(Performance.Monitor.ObjectResourceCount).ToString();
		});
		CreateDivider();
		CreateLabel("Non-DMB Parts", () => _root.Bridge.SeparatedPartCount.ToString());
		CreateDivider();
		CreateLabel("Loaded Assets", () => AssetLoader.Singleton.AssetCacheCount.ToString());
		CreateLabel("Pending Assets", () => AssetLoader.Singleton.PendingAssetsCount.ToString());
		CreateDivider();
		CreateLabel("Data Send", () =>
		{
			if (_root.Network.NetInstance == null) return "N/A";
			return _root.Network.NetInstance.PopStatistic(ENetConnection.HostStatistic.SentData).Bytes().Kilobytes.ToString("0.##") + " kb/s";
		});
		CreateLabel("Data Receive", () =>
		{
			if (_root.Network.NetInstance == null) return "N/A";
			return _root.Network.NetInstance.PopStatistic(ENetConnection.HostStatistic.ReceivedData).Bytes().Kilobytes.ToString("0.##") + " kb/s";
		});
		CreateLabel("Packet Send", () =>
		{
			if (_root.Network.NetInstance == null) return "N/A";
			return _root.Network.NetInstance.PopStatistic(ENetConnection.HostStatistic.SentPackets).ToString();
		});
		CreateLabel("Packet Receive", () =>
		{
			if (_root.Network.NetInstance == null) return "N/A";
			return _root.Network.NetInstance.PopStatistic(ENetConnection.HostStatistic.ReceivedPackets).ToString();
		});
		CreateLabel("Network Clock", () =>
		{
			return TimeSpan.FromSeconds((double)_root.ServerTime).ToString(@"hh\:mm\:ss");
		});

		MainUpdateLoop();
		base._Ready();
	}

	public override void _ExitTree()
	{
		ClientSettings.Singleton.OnSettingChanged -= OnSettingChanged;

		base._ExitTree();
	}

	private void OnSettingChanged(string name)
	{
		if (name == "PerformanceOverlayMode")
		{
			UpdateVisible();
		}
	}

	private void UpdateVisible()
	{
		Visible = ClientSettings.Singleton.Settings.PerformanceOverlayMode == ClientSettingsData.PerformanceOverlayModeEnum.Full;
	}

	private void CreateLabel(string startText, Func<string> getter)
	{
		Label label = new();
		_layout.AddChild(label);
		_actions.Add(() =>
		{
			label.Text = startText + ": " + getter();
		});
	}
	private void CreateDivider()
	{
		Label label = new()
		{
			Text = "---------"
		};
		_layout.AddChild(label);
	}

	private async void MainUpdateLoop()
	{
		while (true)
		{
			UpdateAll();
			await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
		}
	}

	private void UpdateAll()
	{
		foreach (Action item in _actions)
		{
			item();
		}
	}
}
