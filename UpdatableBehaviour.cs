using System;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Scripting;

namespace Sisus
{
	/// <summary>
	/// A base class for components with an <see cref="OnUpdate"/> method that gets executed every frame.
	/// </summary>
	/// <typeparam name="T"> Type of the derived class. </typeparam>
	/// <remarks>
	/// <see cref="OnUpdate"/> is considerably more performant than MonoBehaviour.Update when you have many instances.
	/// </remarks>
	public abstract class UpdatableBehaviour<T> : UpdatableBehaviour where T : UpdatableBehaviour<T>
	{
		const int InitialCapacity = 32;
		static T[] instances = new T[InitialCapacity];
		static int instanceCount;
		static int currentInstanceIndex;

		int instanceIndex = -1;

		void OnEnable()
		{
			if(instanceCount >= instances.Length)
			{
				var capacity = instanceCount + instanceCount;
				Array.Resize(ref instances, capacity);
			}

			instanceIndex = instanceCount;
			instances[instanceCount] = (T)this;
			instanceCount++;
		}

		void OnDisable()
		{
			instanceCount--;

			// If we're removing the last item, we can just set it to null.
			if(instanceIndex >= instanceCount)
			{
				instances[instanceIndex] = null;
				return;
			}

			// If UpdateAllInstances is currently not executing, or if we're removing
			// an item after the current index, we can use a faster removal method,
			// where we just move the last item into the removed item's slot.
			if(currentInstanceIndex <= instanceIndex)
			{
				var lastItem = instances[instanceCount];
				instances[instanceIndex] = lastItem;
				lastItem.instanceIndex = instanceIndex;
				instances[instanceCount] = null;
				return;
			}

			// If UpdateAllInstances is currently executing, and the removed item is before
			// the current index, then we need to use a slower method that ensures no items
			// are skipped during the current batch of OnUpdate executions.

			// The number of elements to move is the total count minus the index of the item after the removed one.
			// Example: count is now 4, we removed index 1. We need to move elements from index 2 up to index 3.
			Array.Copy(instances, instanceIndex + 1, instances, instanceIndex, instanceCount - instanceIndex);
			instances[instanceIndex] = null;

			// Adjust UpdateAllInstances current instance index to avoid skipping past one instance.
			currentInstanceIndex--;
		}

		[Preserve, UsedImplicitly]
		internal static PlayerLoopSystem.UpdateFunction GetUpdateAllInstancesDelegate() => UpdateAllInstances;

		[Preserve, UsedImplicitly]
		internal static void UpdateAllInstances()
		{
			for(currentInstanceIndex = instanceCount - 1; currentInstanceIndex >= 0; currentInstanceIndex--)
			{
				Debug.Assert(Application.isPlaying);
				Debug.Assert(instances[currentInstanceIndex]);
				instances[currentInstanceIndex].OnUpdate();
			}
		}

		protected abstract void OnUpdate();
	}

	/// <summary>
	/// Non-generic base class of <see cref="UpdatableBehaviour{T}"/>.
	/// </summary>
	/// <remarks>
	/// You should always derive from the generic version <see cref="UpdatableBehaviour{T}"/> instead of this one directly.
	/// </remarks>
	public abstract class UpdatableBehaviour : MonoBehaviour
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void StartUpdatingAllInstances()
		{
			var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
			var rootSystems = playerLoop.subSystemList;
			for(var systemIndex = rootSystems.Length - 1; systemIndex >= 0; systemIndex--)
			{
				var rootSystem = rootSystems[systemIndex];
				if(rootSystem.type != typeof(Update))
				{
					continue;
				}

				var oldUpdateSystems = rootSystem.subSystemList;
				if(Array.FindIndex(oldUpdateSystems, x => x.type == typeof(UpdatableBehaviour)) is not -1)
				{
					return;
				}

				var updatableBehaviourTypes =
					#if UNITY_EDITOR
					UnityEditor.TypeCache.GetTypesDerivedFrom<UpdatableBehaviour>()
						.Where(type =>
						{
							if(type.ContainsGenericParameters)
							{
								return false;
							}

							for(var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
							{
								if(baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(UpdatableBehaviour<>))
								{
									return true;
								}
							}

							return false;
						})
						.Distinct()
						.ToArray();
					#else
					AppDomain.CurrentDomain.GetAssemblies()
						.Where(assembly => !assembly.IsDynamic)
						.SelectMany(assembly => assembly.GetTypes())
						.Where(type =>
						{
							if(!typeof(UpdatableBehaviour).IsAssignableFrom(type) || type.ContainsGenericParameters)
							{
								return false;
							}

							for(var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
							{
								if(baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(UpdatableBehaviour<>))
								{
									return true;
								}
							}

							return false;
						})
						.Distinct()
						.ToArray();
					#endif
				
				Debug.Log($"updatableBehaviourTypes ({updatableBehaviourTypes.Length}): {string.Join(", ", updatableBehaviourTypes.Select(t => t.FullName))}");

				var addCount = updatableBehaviourTypes.Length;
				var oldLength = oldUpdateSystems.Length;
				var newLength = oldLength + addCount;
				var newUpdateSystems = new PlayerLoopSystem[newLength];
				Array.Copy(oldUpdateSystems, newUpdateSystems, oldLength);

				for(var typeIndex = 0; typeIndex < addCount; typeIndex++)
				{
					var updatableBehaviourType = updatableBehaviourTypes[typeIndex];
					newUpdateSystems[oldLength + typeIndex] = new()
					{
						type = updatableBehaviourType,
						updateDelegate = GetUpdateAllInstancesDelegate(updatableBehaviourType)
					};
				}

				rootSystem.subSystemList = newUpdateSystems;
				rootSystems[systemIndex] = rootSystem;
				playerLoop.subSystemList = rootSystems;
				PlayerLoop.SetPlayerLoop(playerLoop);
				return;
			}

			static PlayerLoopSystem.UpdateFunction GetUpdateAllInstancesDelegate(Type updatableBehaviourType)
			{
				for(var baseType = updatableBehaviourType.BaseType; baseType != null; baseType = baseType.BaseType)
				{
					if(baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(UpdatableBehaviour<>))
					{
						var getUpdateAllInstancesDelegateMethod = baseType.GetMethod("GetUpdateAllInstancesDelegate", BindingFlags.NonPublic | BindingFlags.Static)!;
						return (PlayerLoopSystem.UpdateFunction)getUpdateAllInstancesDelegateMethod.Invoke(null, null);
					}
				}

				throw new InvalidOperationException($"Type {updatableBehaviourType.FullName} does not derive from UpdatableBehaviour<>");
			}
		}
	}
}
