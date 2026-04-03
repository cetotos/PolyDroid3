// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Shared.AssetLoaders;
using System;
using System.Threading.Tasks;

namespace Polytoria.Providers.AssetLoaders;

public interface IAssetProvider : IDisposable
{
	Task<CacheItem> LoadResource(CacheItem item);
	string GetAssetServeURL(uint id, ResourceType itemType);
}
