// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Data;
using Polytoria.Datamodel.Services;
using Polytoria.Shared;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Script = Polytoria.Datamodel.Script;

namespace Polytoria.Scripting.Luau;

public class LuaMetatable : LuaObject
{
	public LuaState Lua = null!;
	[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)]
	public Type TargetType = null!;
	public LuauProvider LangProvider = null!;

	public bool HasCustomIndex = false;
	public bool HasCustomNewIndex = false;
	public bool HasToString = false;

	private async void AsyncExec(object? targetObject, LuaState state, MethodInfo targetMethod, object?[] args, TaskCompletionSource<int> tcs)
	{
		object invokeResult = targetMethod.Invoke(targetObject, args)!;

		if (invokeResult is Task task)
		{
			if (!task.IsCompleted)
			{
				try
				{
					await task;
				}
				catch
				{
					tcs.SetException(task.Exception?.InnerException ?? task.Exception ?? new Exception("Something went wrong"));
				}
			}
			if (task.IsFaulted)
			{
				tcs.SetException(task.Exception.InnerException ?? task.Exception);
				return;
			}
			Type returnType = targetMethod.ReturnType;

			if (returnType == typeof(Task))
			{
				tcs.SetResult(0);
				return;
			}

			// --------------- HANDLE TASK --------------- 
			if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
			{
				try
				{
					if (task is Task<int> intTask)
					{
						LangProvider.PushValueToLua(state, intTask.Result);
					}
					else if (task is Task<string> stringTask)
					{
						LangProvider.PushValueToLua(state, stringTask.Result);
					}
					else if (task is Task<bool> boolTask)
					{
						LangProvider.PushValueToLua(state, boolTask.Result);
					}
					else if (task is Task<object> objectTask)
					{
						LangProvider.PushValueToLua(state, objectTask.Result);
					}
					else if (task is Task<Instance> instanceTask)
					{
						LangProvider.PushValueToLua(state, instanceTask.Result);
					}
					else if (task is Task<NetworkedObject> netObjTask)
					{
						LangProvider.PushValueToLua(state, netObjTask.Result);
					}
					else if (task is Task<Accessory> accessoryTask)
					{
						LangProvider.PushValueToLua(state, accessoryTask.Result);
					}
					else if (task is Task<Tool> toolTask)
					{
						LangProvider.PushValueToLua(state, toolTask.Result);
					}
					else if (task is Task<HttpResponseData> responseTask)
					{
						LangProvider.PushValueToLua(state, responseTask.Result);
					}
					else if (task is Task<byte[]> byteArrayTask)
					{
						LangProvider.PushValueToLua(state, byteArrayTask.Result);
					}
					else if (task is Task<object?[]> objectArrayTask)
					{
						foreach (object? r in objectArrayTask.Result)
						{
							LangProvider.PushValueToLua(state, r);
						}
						tcs.SetResult(objectArrayTask.Result.Length);
						return;
					}
					else
					{
						throw new NotSupportedException($"INTERNAL BUG: Task type {task.GetType()} is not supported in AOT");
					}
					tcs.SetResult(1);
				}
				catch (Exception ex)
				{
					tcs.SetException(ex.InnerException ?? ex);
				}
			}
		}
		else
		{
			tcs.SetResult(0);
		}
	}

	public virtual int Index(IntPtr L)
	{
		LuaState state = LuaState.FromIntPtr(L);
		Script script = LuauProvider.GetScriptInstance(state);

		object? targetObject = LangProvider.LuaToObject(state, 1, false);

		if (IsThisInvalid(targetObject))
		{
			state.PushNil();
			return 1;
		}

		if (targetObject is Instance instance)
		{
			LuaType t = state.Type(2);

			// Index child
			Instance? child = null;

			if (t == LuaType.String)
			{
				string? indexK = state.ToString(2);

				if (indexK != null)
				{
					if (script.Compatibility)
					{
						child = instance.LegacyFindChild(indexK);
					}
					else
					{
						child = instance.FindChild(indexK);
					}
				}
			}
			else if (t == LuaType.Number)
			{
				int indexK = (int)state.ToNumber(2) - 1;
				child = instance.FindChildByIndex(indexK);
			}

			if (child != null)
			{
				LangProvider.PushValueToLua(state, child);
				return 1;
			}
		}

		// stack[1] = table
		// stack[2] = key
		string? key = state.ToString(2);

		if (key == null) { return 0; }

		// Call/Retrieve other script's property/functions
		if (targetObject is Script scriptref && targetObject.GetType() == script.GetType())
		{
			if (scriptref.Ran && scriptref.LuauMainThread != null && scriptref.ShouldContinue)
			{
				LuaState targetState = scriptref.LuauMainThread;
				targetState.GetGlobal(key);

				if (targetState.IsNil(-1))
				{
					targetState.Pop(1);
				}
				else
				{
					object? obj = LangProvider.LuaToObject(targetState, -1, getAsFunction: true);

					LangProvider.PushValueToLua(state, obj);
					targetState.Pop(1);
					return 1;
				}
			}
		}

		// Handle Object.New for NetworkedObjects
		if (key == "New")
		{
			if (TargetType.IsDefined(typeof(InstantiableAttribute)))
			{
				int newFunc(IntPtr L)
				{
					LuaState state = LuaState.FromIntPtr(L);
					NetworkedObject? parentTo = null;
					object? parentObj = LangProvider.LuaToObject(state, 1);

					if (parentObj is NetworkedObject p)
					{
						parentTo = p;
					}

					LangProvider.PushValueToLua(state, NetworkedObject.NewFromScript(script, TargetType.Name, parentTo));
					return 1;
				}

				int safeNewFunc(IntPtr L)
				{
					Exception? caughtException;

					try
					{
						return newFunc(L);
					}
					catch (Exception ex)
					{
						caughtException = ex;
					}

					if (caughtException != null)
					{
						LuaState state = LuaState.FromIntPtr(L);
						return state.Error(caughtException.InnerException?.Message ?? caughtException.Message);
					}

					return 0;
				}

				state.PushCFunction(safeNewFunc, "New");

				return 1;
			}
		}

		PropertyInfo? prop = ScriptService.GetScriptPropertyOfName(TargetType, key, script.Compatibility);
		MethodInfo? method = ScriptService.ResolveMethod(script.Compatibility, key, TargetType);

		if (method != null)
		{
			HandlesLuaStateAttribute? handleLuaState = method.GetCustomAttribute<HandlesLuaStateAttribute>();

			if (handleLuaState != null)
			{
				return (int)method.Invoke(targetObject, [state])!;
			}
		}

		if (prop != null)
		{
			ScriptPropertyAttribute? propAttribute = prop.GetCustomAttribute<ScriptPropertyAttribute>();
			if (propAttribute != null)
			{
				if (propAttribute.Permissions != ScriptPermissionFlags.None)
				{
					if (!script.PermissionFlags.HasFlag(propAttribute.Permissions))
					{
						throw new UnauthorizedAccessException("script does not have permission to access the specified property (" + key + ")");
					}
				}
			}

			object? value = prop.GetValue(targetObject);
			LangProvider.PushValueToLua(state, value);

			return 1;
		}
		else if (method != null)
		{
			int methodFunc(IntPtr L)
			{
				LuaState state = LuaState.FromIntPtr(L);

				int top = state.GetTop();
				int argsCount = top;

				ScriptService.MethodsCacheData methodInfos = ScriptService.ResolveMethods(TargetType, key, script.Compatibility);

				if (methodInfos.Methods.Length == 0)
				{
					throw new InvalidOperationException("target method doesn't exist (" + key + ")");
				}

				// parse arguments
				List<object?> argList = [];
				for (int i = 0; i < top; i++)
				{
					object? arg = LangProvider.LuaToObject(state, i + 1, methodInfos.ConvertParamsToGD, methodInfos.GetParamsAsFunction);

					// ignore self
					if (arg == targetObject && i == 0)
					{
						argsCount -= 1;
					}
					else
					{
						argList.Add(arg);
					}
				}

				return ProcessMethod(state, methodInfos, targetObject, key, argList);
			}

			int safeMethodFunc(IntPtr L)
			{
				Exception? caughtException;

				try
				{
					return methodFunc(L);
				}
				catch (Exception ex)
				{
					caughtException = ex;
				}

				if (caughtException != null)
				{
					LuaState state = LuaState.FromIntPtr(L);
					return state.Error(caughtException.InnerException?.Message ?? caughtException.Message);
				}

				return 0;
			}

			state.PushCFunction(safeMethodFunc, key);

			return 1;
		}
		else
		{
			//GD.PushWarning(script.LuaPath, " couldn't find " + key);
			state.PushNil();
		}

		return 1;
	}

	public virtual int NewIndex(IntPtr L)
	{
		LuaState state = LuaState.FromIntPtr(L);
		Script script = LuauProvider.GetScriptInstance(state);

		object? targetObject = LangProvider.LuaToObject(state, 1, false);

		if (IsThisInvalid(targetObject))
		{
			state.PushNil();
			return 1;
		}

		string? key = state.ToString(2);

		if (key == null) { return 0; }

		// Set other script's globals
		if (targetObject is Script scriptref && targetObject.GetType() == script.GetType())
		{
			if (scriptref.Ran && scriptref.LuauMainThread != null && scriptref.ShouldContinue)
			{
				LuaState targetState = scriptref.LuauMainThread;

				object? setTo = LangProvider.LuaToObject(state, 3, getAsFunction: true);
				LangProvider.PushValueToLua(targetState, setTo);
				targetState.SetGlobal(key);
				targetState.Pop(1);
				return 0;
			}
		}

		PropertyInfo? prop = ScriptService.GetScriptPropertyOfName(TargetType, key, script.Compatibility);

		if (prop != null && prop.CanWrite)
		{
			MethodInfo? setter = prop.GetSetMethod();

			if (setter != null)
			{
				// Read value
				object? propSetTo = LangProvider.LuaToObject(state, 3, getAsFunction: true);
				object? convertedValue = ScriptService.ConvertToPropertyType(propSetTo, prop.PropertyType);

				if (convertedValue == null && !IsNullableType(prop.PropertyType))
				{
					throw new Exception("member " + prop.Name + " cannot be assigned to nil.");
				}

				prop.SetValue(targetObject, convertedValue);
				return 0;
			}
			else
			{
				throw new Exception("member " + prop.Name + " cannot be assigned to.");
			}
		}
		else
		{
			if (!script.Compatibility)
			{
				throw new Exception("member " + key + " doesn't exist");
			}
			else
			{
				GD.PushWarning("member " + key + " doesn't exist");
			}
			return 0;
		}
	}

	public virtual int NameCall(IntPtr L)
	{
		LuaState state = LuaState.FromIntPtr(L);
		Script script = LuauProvider.GetScriptInstance(state);

		string? key = state.GetNameCallAtom();

		if (string.IsNullOrEmpty(key))
		{
			// Fallback for edge cases where __namecall is invoked manually
			key = state.ToString(2);
		}

		if (key == null) { throw new InvalidOperationException($"namecall key is null"); }

		object? targetObject = LangProvider.LuaToObject(state, 1, false);

		if (IsThisInvalid(targetObject))
		{
			state.PushNil();
			return 1;
		}

		ScriptService.MethodsCacheData methodInfos = ScriptService.ResolveMethods(TargetType, key, script.Compatibility);

		if (methodInfos.Methods.Length == 0)
		{
			throw new InvalidOperationException($"target method doesn't exist ({key})");
		}

		// Process arguments
		int top = state.GetTop();
		List<object?> argList = [];

		for (int i = 1; i < top; i++)
		{
			argList.Add(LangProvider.LuaToObject(state, i + 1, methodInfos.ConvertParamsToGD, methodInfos.GetParamsAsFunction));
		}

		return ProcessMethod(state, methodInfos, targetObject, key, argList);
	}

	public int IndexWrapper(IntPtr L)
	{
		Exception? caughtException;
		try
		{
			return Index(L);
		}
		catch (Exception ex)
		{
			caughtException = ex;
		}

		if (caughtException != null)
		{
			LuaState state = LuaState.FromIntPtr(L);
			if (Globals.IsBetaBuild)
			{
				return state.Error(caughtException.InnerException?.ToString() ?? caughtException.ToString());
			}
			else
			{
				return state.Error(caughtException.InnerException?.Message ?? caughtException.Message);
			}
		}

		return 0;
	}

	public int NewIndexWrapper(IntPtr L)
	{
		Exception? caughtException;
		try
		{
			return NewIndex(L);
		}
		catch (Exception ex)
		{
			caughtException = ex;
		}

		if (caughtException != null)
		{
			LuaState state = LuaState.FromIntPtr(L);
			if (Globals.IsBetaBuild)
			{
				return state.Error(caughtException.InnerException?.ToString() ?? caughtException.ToString());
			}
			else
			{
				return state.Error(caughtException.InnerException?.Message ?? caughtException.Message);
			}
		}

		return 0;
	}

	public int NameCallWrapper(IntPtr L)
	{
		Exception? caughtException;
		try
		{
			return NameCall(L);
		}
		catch (Exception ex)
		{
			caughtException = ex;
		}

		if (caughtException != null)
		{
			LuaState state = LuaState.FromIntPtr(L);
			if (Globals.IsBetaBuild)
			{
				return state.Error(caughtException.InnerException?.ToString() ?? caughtException.ToString());
			}
			else
			{
				return state.Error(caughtException.InnerException?.Message ?? caughtException.Message);
			}
		}

		return 0;
	}

	public virtual void RegisterMetamethods()
	{
		foreach ((MethodInfo m, ScriptMetamethodAttribute attr) in ScriptService.GetMetamethods(TargetType))
		{
			HandlesLuaStateAttribute? handleLuaState = m.GetCustomAttribute<HandlesLuaStateAttribute>();

			string indexer = attr.Metamethod switch
			{
				ScriptObjectMetamethod.Add => "__add",
				ScriptObjectMetamethod.Sub => "__sub",
				ScriptObjectMetamethod.Call => "__call",
				ScriptObjectMetamethod.Concat => "__concat",
				ScriptObjectMetamethod.Div => "__div",
				ScriptObjectMetamethod.Eq => "__eq",
				ScriptObjectMetamethod.Iter => "__iter",
				ScriptObjectMetamethod.Le => "__le",
				ScriptObjectMetamethod.Len => "__len",
				ScriptObjectMetamethod.Lt => "__lt",
				ScriptObjectMetamethod.Mod => "__mod",
				ScriptObjectMetamethod.Mul => "__mul",
				ScriptObjectMetamethod.Pow => "__pow",
				ScriptObjectMetamethod.ToString => "__tostring",
				ScriptObjectMetamethod.Unm => "__unm",
				ScriptObjectMetamethod.Index => "__index",
				ScriptObjectMetamethod.NewIndex => "__newindex",
				_ => ""
			};

			// Allow nil in argument on some metamethods
			// Some callers incorrectly pass nil to metamethods, unsupporting metamethods will have nil stripped from their argument list.
			bool allowNilInArg = attr.Metamethod is
				ScriptObjectMetamethod.Call or
				ScriptObjectMetamethod.Index or
				ScriptObjectMetamethod.NewIndex or
				ScriptObjectMetamethod.Iter;

			if (attr.Metamethod == ScriptObjectMetamethod.Index)
			{
				HasCustomIndex = true;
			}
			else if (attr.Metamethod == ScriptObjectMetamethod.NewIndex)
			{
				HasCustomNewIndex = true;
			}
			else if (attr.Metamethod == ScriptObjectMetamethod.ToString)
			{
				HasToString = true;
			}

			// Metamethod implementation
			int metamethodFunc(IntPtr L)
			{
				LuaState state = LuaState.FromIntPtr(L);

				object? targetObject = LangProvider.LuaToObject(state, 1, false);

				if (handleLuaState != null)
				{
					return (int)m.Invoke(targetObject, [state])!;
				}

				int additional = 0;

				if (attr.Metamethod == ScriptObjectMetamethod.Index || attr.Metamethod == ScriptObjectMetamethod.NewIndex)
				{
					additional = 1;
				}

				int argsCount = state.GetTop() - additional;

				object? val;

				if (IsThisInvalid(targetObject))
				{
					state.PushNil();
					return 1;
				}

				if (targetObject == null && m.IsStatic)
				{
					state.PushNil();
					return 1;
				}

				ParameterInfo[] parameters = m.GetParameters();
				object?[] args;

				// Check if the last parameter is a params array
				bool hasParams = parameters.Length > 0 &&
								 parameters[^1].GetCustomAttribute<ParamArrayAttribute>() != null;

				if (hasParams)
				{
					int regularParamCount = parameters.Length - 1;
					int paramsArrayCount = argsCount - regularParamCount;

					args = new object?[parameters.Length];

					// Fill regular parameters
					for (int i = 0; i < regularParamCount; i++)
					{
						args[i] = LangProvider.LuaToObject(state, i + 2, attr.ConvertParamsToGD);
					}

					// Create and fill the params array
					object?[] paramsArray = new object?[Math.Max(0, paramsArrayCount)];

					for (int i = 0; i < paramsArrayCount; i++)
					{
						paramsArray[i] = LangProvider.LuaToObject(state, regularParamCount + i + 2);
					}

					args[regularParamCount] = paramsArray;
				}
				else
				{
					// non-params methods
					List<object?> argList = new(argsCount);
					for (int i = 0; i < argsCount; i++)
					{
						var v = LangProvider.LuaToObject(state, i + 1 + additional, attr.ConvertParamsToGD);
						if (v != null || allowNilInArg)
						{
							argList.Add(v);
						}
					}
					args = [.. argList];
				}

				try
				{
					val = m.Invoke(targetObject, args);

					// Handle __iter: push an iterator function, nil, nil
					if (attr.Metamethod is ScriptObjectMetamethod.Iter)
					{
						if (val is not IEnumerable enumerable)
						{
							state.PushNil();
							return 1;
						}

						IEnumerator enumerator = enumerable.GetEnumerator();

						int iteratorFunc(IntPtr iterL)
						{
							LuaState iterState = LuaState.FromIntPtr(iterL);

							if (!enumerator.MoveNext())
							{
								// Signal iteration end
								iterState.PushNil();
								return 1;
							}

							object current = enumerator.Current;

							// Unpack the tuple
							if (current is ITuple tuple && tuple.Length == 2)
							{
								LangProvider.PushValueToLua(iterState, tuple[0]);
								LangProvider.PushValueToLua(iterState, tuple[1]);
								return 2;
							}

							// Fallback: push the whole value
							LangProvider.PushValueToLua(iterState, current);
							return 1;
						}

						int safeIteratorFunc(IntPtr L)
						{
							Exception? caughtException;

							try
							{
								return iteratorFunc(L);
							}
							catch (Exception ex)
							{
								caughtException = ex;
							}

							if (caughtException != null)
							{
								LuaState state = LuaState.FromIntPtr(L);
								return state.Error(TargetType.Name + " metamethod error " + indexer + ": " + (caughtException.InnerException?.Message ?? caughtException.Message));
							}

							return 0;
						}

						// Push: iterator function, nil (state), nil (initial control value)
						Lua.PushCFunction(safeIteratorFunc, indexer + "_iter");
						state.PushNil();
						state.PushNil();
						return 3;
					}

					LangProvider.PushValueToLua(state, val);
					return 1;
				}
				catch (Exception ex)
				{
					PT.PrintErrV(ex.Message, " argn: ", args.Length);
					foreach (var item in args)
					{
						PT.PrintErrV(item, $" ({item?.GetType()})");
					}
					throw;
				}
			}

			int safeMetaMethodFunc(IntPtr L)
			{
				Exception? caughtException;

				try
				{
					return metamethodFunc(L);
				}
				catch (Exception ex)
				{
					caughtException = ex;
				}

				if (caughtException != null)
				{
					LuaState state = LuaState.FromIntPtr(L);
					return state.Error(TargetType.Name + " metamethod error " + indexer + ": " + (caughtException.InnerException?.Message ?? caughtException.Message));
				}

				return 0;
			}

			Lua.PushCFunction(safeMetaMethodFunc, indexer);

			Lua.SetField(-2, indexer);
		}
	}

	public int ToString(IntPtr L)
	{
		LuaState state = LuaState.FromIntPtr(L);

		object? targetObject = LangProvider.LuaToObject(state, 1, false);

		if (targetObject != null)
		{
			state.PushString("<" + targetObject.GetType().Name + ">");
		}
		else
		{
			state.PushString("<null>");
		}

		return 1;
	}

	private static bool IsThisInvalid(object? targetObject)
	{
		if (targetObject is NetworkedObject netObj && netObj.IsDeleted)
		{
			return true;
		}
		return false;
	}

	private int ProcessMethod(LuaState state, ScriptService.MethodsCacheData methodInfos, object? targetObject, string key, List<object?> argList)
	{
		Script script = LuauProvider.GetScriptInstance(state);
		object?[] args = [.. argList];
		int argsCount = args.Length;

		MethodInfo? targetMethod = null;
		bool hasParams = false;

		targetMethod = methodInfos.Methods.FirstOrDefault(m =>
		{
			ParameterInfo[] parameters = m.GetParameters();
			ParameterInfo[] eligibleParams = [.. parameters.Where(p => !p.IsDefined(typeof(ScriptingCallerAttribute)))];
			hasParams = eligibleParams.Length > 0 && Attribute.IsDefined(eligibleParams[^1], typeof(ParamArrayAttribute));
			int fixedParamCount = hasParams ? eligibleParams.Length - 1 : eligibleParams.Length;

			if (!hasParams && argsCount > eligibleParams.Length) { return false; }
			if (hasParams && argsCount < fixedParamCount) { return false; }

			for (int i = argsCount; i < fixedParamCount; i++)
				if (!eligibleParams[i].HasDefaultValue) { return false; }

			for (int i = 0; i < argsCount; i++)
			{
				Type paramType = (hasParams && i >= fixedParamCount)
					? eligibleParams[^1].ParameterType.GetElementType()!
					: eligibleParams[i].ParameterType;
				object? arg = args[i];

				if (arg == null)
				{
					if (!paramType.IsClass && Nullable.GetUnderlyingType(paramType) == null)
					{
						PT.PrintV(m.Name, " is null");
						return false;
					}
				}
				else
				{
					Type argType = arg.GetType();
					if (!paramType.IsAssignableFrom(argType) && !ScriptService.IsObjectConvertible(arg, paramType))
					{
						PT.PrintV(arg, " not convertible");
						return false;
					}
				}
			}

			return true;
		});

		bool methodHasParams = hasParams;

		if (targetMethod == null)
		{
			if (!script.Compatibility)
			{
				throw new InvalidOperationException("couldn't find method with matching signature (" + key + ")");
			}
			else
			{
				PT.PrintErr(script.LuaPath, " couldn't find method with matching signature (" + key + ")");
			}
			return 0;
		}

		bool methodIsStatic = targetMethod.IsStatic;

		if (targetObject == null && !methodIsStatic)
		{
			throw new Exception("attempt to call non static method on a static object (" + key + ")");
		}

		ScriptMethodAttribute? methodAttribute = targetMethod.GetCustomAttribute<ScriptMethodAttribute>();

		if (methodAttribute != null)
		{
			if (methodAttribute.Permissions != ScriptPermissionFlags.None)
			{
				if (!script.PermissionFlags.HasFlag(methodAttribute.Permissions))
				{
					throw new UnauthorizedAccessException("script does not have permission to call the specified method (" + targetMethod.Name + ")");
				}
			}
		}

		// Prepare args array formatted for params
		ParameterInfo[] parametersFinal = targetMethod.GetParameters();
		int callerParamIndex = Array.FindIndex(parametersFinal, p => p.IsDefined(typeof(ScriptingCallerAttribute)));

		List<object?> finalArgs = [];

		if (methodHasParams)
		{
			int fixedParamCount = parametersFinal.Length - 1;
			int paramsCount = Math.Max(0, argsCount - fixedParamCount);
			Type paramsElementType = parametersFinal[^1].ParameterType.GetElementType()!;

			// Convert fixed args
			for (int i = 0; i < fixedParamCount; i++)
				finalArgs.Add(ScriptService.ConvertToPropertyType(args[i], parametersFinal[i].ParameterType));

			// Convert params args
			Array paramsArray = new object[paramsCount];
			for (int i = 0; i < paramsCount; i++)
				paramsArray.SetValue(ScriptService.ConvertToPropertyType(args[fixedParamCount + i], paramsElementType), i);

			finalArgs.Add(paramsArray);
		}
		else
		{
			// No params, just convert normally
			int argIndex = 0;
			for (int i = 0; i < parametersFinal.Length; i++)
			{
				if (i == callerParamIndex)
					continue; // Skip the caller parameter

				if (argIndex < argsCount)
				{
					finalArgs.Add(ScriptService.ConvertToPropertyType(args[argIndex], parametersFinal[i].ParameterType));
					argIndex++;
				}
			}

			// Fill remaining with default values
			for (int i = argsCount; i < parametersFinal.Length; i++)
			{
				if (i == callerParamIndex)
					continue; // Skip the caller parameter

				int paramIndex = i + (callerParamIndex >= 0 && i >= callerParamIndex ? 1 : 0);
				if (paramIndex < parametersFinal.Length)
					finalArgs.Add(parametersFinal[paramIndex].HasDefaultValue ? parametersFinal[paramIndex].DefaultValue : null);
			}
		}

		// Apply caller if ScriptingCallerAttribute is found
		if (callerParamIndex >= 0)
			finalArgs.Insert(callerParamIndex, script);

		object?[] invokeArgs = [.. finalArgs];

		if (ScriptService.IsAsyncMethod(targetMethod))
		{
			TaskCompletionSource<int> tcs = new();
			LuauProvider.SetYieldTask(state, tcs.Task);
			AsyncExec(targetObject, state, targetMethod, invokeArgs, tcs);
			return state.Yield(1);
		}
		else
		{
			object? result = targetMethod.Invoke(targetObject, invokeArgs);
			LangProvider.PushValueToLua(state, result);
			return 1;
		}
	}

	private static bool IsNullableType(Type type)
	{
		// Reference types are always nullable
		if (!type.IsValueType) return true;

		// Check for Nullable<T> (e.g. int?, float?)
		return Nullable.GetUnderlyingType(type) != null;
	}
}
