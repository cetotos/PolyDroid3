// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Providers.AssetLoaders;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Shared.AssetLoaders;

public partial class AssetLoader : Node
{
	public AssetLoader()
	{
		Singleton = this;
		AssetProvider = new PTAssetProvider();

		_ = Task.Run(Process);
	}

	public static AssetLoader Singleton { get; private set; } = null!;
	public bool UseAssetLoader { get; set; } = true;

	private const int MaxConcurrentRequests = 1;

	private long _assetSizeBytes = 0;
	internal long AssetSizeBytes => _assetSizeBytes;
	internal int PendingAssetsCount => _queue.Count;
	internal int AssetCacheCount => _cache.Count;

	private readonly BlockingCollection<(CacheItem Item, dynamic Callback)> _queue = [];

	private readonly ConcurrentDictionary<int, CacheItem> _cache = [];
	private readonly ConcurrentDictionary<int, TaskCompletionSource<CacheItem>> _pendingRequests = [];

	public IAssetProvider AssetProvider = null!;

	private void Process()
	{
		List<Task> workers = [];
		for (int i = 0; i < MaxConcurrentRequests; i++)
		{
			workers.Add(Task.Run(async () =>
			{
				while (true)
				{
					(CacheItem item, dynamic callback) = _queue.Take();

					try
					{
						CacheItem result;
						int hash = item.GetHashCode();
						if (_pendingRequests.TryGetValue(hash, out TaskCompletionSource<CacheItem>? pi))
						{
							result = await pi.Task;
						}
						else if (!_cache.TryGetValue(hash, out result))
						{
							TaskCompletionSource<CacheItem> ci = new();
							_pendingRequests.TryAdd(hash, ci);
							try
							{
								result = await LoadResource(item);
								ci.SetResult(result);
								Interlocked.Add(ref _assetSizeBytes, result.SizeBytes);
							}
							catch
							{
								ci.TrySetException(new Exception("Failed to load asset ID: " + item.ID));
								throw; // rethrow so the outer catch can log it
							}
							finally
							{
								_pendingRequests.TryRemove(hash, out _);
							}
						}

						Callable.From(() =>
						{
							if (callback is Action<CacheItem> cl)
							{
								cl(result);
							}
							else if (callback is Action<Resource> cr)
							{
								cr(result.Resource);
							}
						}).CallDeferred();
					}
					catch (Exception exception)
					{
						PT.PrintErr("Failed to load resource (Type: " + item.Type + ", ID: " + item.ID + "): " + exception.Message);
					}
				}
			}));
		}
	}

	private async Task<CacheItem> LoadResource(CacheItem item)
	{
		if (item.ID == 0)
		{
			return new CacheItem();
		}

		if (!UseAssetLoader)
		{
			return new CacheItem();
		}
		CacheItem result = await AssetProvider.LoadResource(item);

		_cache.TryAdd(result.GetHashCode(), result);
		return result;
	}

	public void GetResource(CacheItem item, Action<Resource> callback)
	{
		int hash = item.GetHashCode();

		// Return cached asset
		if (_cache.TryGetValue(hash, out CacheItem cached))
		{
			Callable.From(() => callback(cached.Resource)).CallDeferred();
			return;
		}

		_queue.Add((item, callback));
	}

	public void GetRawCache(CacheItem item, Action<CacheItem> callback)
	{
		int hash = item.GetHashCode();

		// Return cached asset
		if (_cache.TryGetValue(hash, out CacheItem cached))
		{
			Callable.From(() => callback(cached)).CallDeferred();
			return;
		}

		_queue.Add((item, callback));
	}
}

public enum ResourceType
{
	Mesh,
	Decal,
	Audio,
	AssetThumbnail,
	PlaceThumbnail,
	PlaceIcon,
	UserThumbnail,
	UserHeadshot,
	GuildThumbnail,
	GuildBanner,
	Asset,
	Font
}

public struct CacheItem
{
	public ResourceType Type { get; set; }
	public uint ID { get; set; }
	public string DirectURL { get; set; }
	public Vector2I? Resize { get; set; }
	public Resource Resource { get; set; }
	public long SizeBytes { get; set; }

	public override readonly bool Equals(object? obj)
	{
		return obj is CacheItem item && item.Type == Type && item.ID == ID;
	}

	public override readonly int GetHashCode()
	{
		return HashCode.Combine(Type, ID);
	}

	public static bool operator ==(CacheItem left, CacheItem right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(CacheItem left, CacheItem right)
	{
		return !(left == right);
	}
}

