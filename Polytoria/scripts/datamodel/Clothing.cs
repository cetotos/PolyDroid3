// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Clothing : Instance
{
	private ImageAsset? _asset;
	private PolytorianModel? _target;

	internal Texture2D? ClothTexture;

	[Editable, ScriptProperty]
	public ImageAsset? Image
	{
		get => _asset;
		set
		{
			if (_asset != null && _asset != value)
			{
				_asset.ResourceLoaded -= OnResourceLoaded;
				_asset.UnlinkFrom(this);
			}
			_asset = value;
			ClothTexture = null;
			if (_asset != null)
			{
				_asset.LinkTo(this);
				_asset.ResourceLoaded += OnResourceLoaded;
				if (_asset.IsResourceLoaded && _asset.Resource != null)
				{
					OnResourceLoaded(_asset.Resource);
				}
				else
				{
					_asset.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	private void OnResourceLoaded(Resource resource)
	{
		if (resource is Texture2D txt2d)
		{
			ClothTexture = txt2d;
			NotifyCharacter();
		}
	}

	private void NotifyCharacter()
	{
		_target?.QueueRenderCloth();
	}

	public override void EnterTree()
	{
		base.EnterTree();
		if (Parent is PolytorianModel c)
		{
			_target = c;
			NotifyCharacter();
		}
	}

	public override void PostIndexMove()
	{
		NotifyCharacter();
		base.PostIndexMove();
	}

	public override void ExitTree()
	{
		base.ExitTree();
		NotifyCharacter();
		_target = null;
	}
}
