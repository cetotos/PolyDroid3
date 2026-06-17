// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Resources;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using System;
using System.Threading.Tasks;

namespace Polytoria.Client.UI.Purchases;

public partial class UIPurchasePrompt : Control
{
	private const float PurchaseDelaySec = 1.25f;

	[Export] private Label _purchaseText = null!;
	[Export] private Label _priceLabel = null!;
	[Export] private Button _purchaseButton = null!;
	[Export] private Button _cancelButton = null!;
	[Export] private TextureRect _iconRect = null!;
	[Export] private AnimationPlayer _animPlay = null!;
	private PTImageAsset? _iconImg;

	public event Action<bool>? Requested;

	public CoreUIRoot CoreUI = null!;

	public override void _Ready()
	{
		Hook();
		base._Ready();
	}

	public override void _EnterTree()
	{
		if (IsNodeReady())
		{
			Hook();
		}
		base._EnterTree();
	}

	public override void _ExitTree()
	{
		Unhook();
		base._ExitTree();
	}

	private bool _hooked;

	private void Hook()
	{
		if (_hooked) return;
		_hooked = true;
		_purchaseButton.Pressed += OnPurchase;
		_cancelButton.Pressed += OnCancel;
		if (_iconImg != null)
		{
			_iconImg.ResourceLoaded += OnIconImgLoaded;
		}
	}

	private void Unhook()
	{
		if (!_hooked) return;
		_hooked = false;
		_purchaseButton.Pressed -= OnPurchase;
		_cancelButton.Pressed -= OnCancel;
		if (_iconImg != null)
		{
			_iconImg.ResourceLoaded -= OnIconImgLoaded;
		}
	}

	private void OnPurchase()
	{
		Requested?.Invoke(true);
		_purchaseButton.Disabled = true;
		_cancelButton.Disabled = true;
	}

	private void OnCancel()
	{
		Requested?.Invoke(false);
		Close();
	}

	public async Task Prompt(APIStoreItem item)
	{
		_purchaseText.Text = $"Would you like to buy {item.Name}?";
		_priceLabel.Text = item.Price!.Value.ToString();

		// Reset button state
		_cancelButton.Disabled = false;
		_purchaseButton.Disabled = true;
		_purchaseButton.GrabFocus();

		_iconImg?.ResourceLoaded -= OnIconImgLoaded;
		_iconImg?.Delete();

		_iconRect.Texture = null;

		_iconImg = new();
		_iconImg.ResourceLoaded += OnIconImgLoaded;
		_iconImg.ImageType = ImageTypeEnum.AssetThumbnail;
		_iconImg.ImageID = (uint)item.Id;
		_iconImg.LoadResource();

		_animPlay.Play("RESET");
		await ToSignal(_animPlay, AnimationPlayer.SignalName.AnimationFinished);
		_animPlay.Play("appear");

		await Globals.Singleton.WaitAsync(PurchaseDelaySec);
		_purchaseButton.Disabled = false;
	}

	public async void PlayPurchaseSuccess()
	{
		_animPlay.Play("bought");
		await ToSignal(_animPlay, AnimationPlayer.SignalName.AnimationFinished);
		Close();
	}

	public void Close()
	{
		_animPlay.Play("disappear");
	}

	private void OnIconImgLoaded(Resource resource)
	{
		_iconRect.Texture = (Texture2D)resource;
	}
}
