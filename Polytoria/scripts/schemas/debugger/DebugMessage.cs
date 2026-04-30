// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using MemoryPack;
using static Polytoria.Scripting.LogDispatcher;

namespace Polytoria.Schemas.Debugger;

[MemoryPackable]
[MemoryPackUnion(0, typeof(MessageClientData))]
[MemoryPackUnion(1, typeof(MessageShutdown))]
[MemoryPackUnion(2, typeof(MessageLaunchWorld))]
[MemoryPackUnion(3, typeof(MessageNewServerRequest))]
[MemoryPackUnion(4, typeof(MessageNewServerResponse))]
[MemoryPackUnion(5, typeof(MessageServerReady))]
[MemoryPackUnion(6, typeof(MessageLogDispatch))]
[MemoryPackUnion(7, typeof(MessageObjPropChange))]
public partial interface IDebugMessage
{
}

[MemoryPackable]
public partial class MessageClientData : IDebugMessage
{
	public string DebugID = "";
	public int ProcessID = 0;
}

[MemoryPackable]
public partial class MessageShutdown : IDebugMessage { }


[MemoryPackable]
public partial class MessageLaunchWorld : IDebugMessage { }

[MemoryPackable]
public partial class MessageNewServerRequest : IDebugMessage
{
	public string WorldPath = "";
}

[MemoryPackable]
public partial class MessageNewServerResponse : IDebugMessage
{
	public string WorldPath = "";
	public string DebugID = "";
	public string Address = "";
	public int Port = 0;
}

[MemoryPackable]
public partial class MessageServerReady : IDebugMessage
{
}

[MemoryPackable]
public partial class MessageObjPropChange : IDebugMessage
{
	public string ObjectID = "";
	public string PropertyName = "";
	public byte[] PropertyValue = [];
}

[MemoryPackable]
public partial class MessageLogDispatch : IDebugMessage
{
	public LogTypeEnum LogType;
	public LogFromEnum LogFrom;
	public string Content = "";
}
