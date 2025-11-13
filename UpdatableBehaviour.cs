using System;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Sisus
{
	/// <summary>
	/// A base class for components with an OnUpdate method that gets executed every frame.
	/// </summary>
	/// <remarks>
	/// <see cref="OnUpdate"/> is considerably more performant than MonoBehaviour.Update when you have many instances.
	/// </remarks>
	public abstract class UpdatableBehaviour : MonoBehaviour
	{
		const int InitialCapacity = 32;
		static UpdatableBehaviour[] instances = new UpdatableBehaviour[InitialCapacity];
		static int instanceCount;
		static int currentInstanceIndex;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void StartUpdatingAllInstances()
		{
			Debug.Assert(instanceCount is 0, instanceCount);
			var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
			var rootSystems = playerLoop.subSystemList;
			for(var i = rootSystems.Length - 1; i >= 0; i--)
			{
				var rootSystem = rootSystems[i];
				if(rootSystem.type != typeof(Update))
				{
					continue;
				}
				
				var oldUpdateSystems = rootSystem.subSystemList;
				if(Array.FindIndex(oldUpdateSystems, x => x.type == typeof(UpdatableBehaviour)) is not -1)
				{
					return;
				}

				var oldLength = oldUpdateSystems.Length;
				var newLength = oldLength + 1;
				var newUpdateSystems = new PlayerLoopSystem[newLength];
				Array.Copy(oldUpdateSystems, newUpdateSystems, oldLength);
				newUpdateSystems[oldLength] = new()
				{
					type = typeof(UpdatableBehaviour),
					updateDelegate = UpdateAllInstances
				};
				rootSystem.subSystemList = newUpdateSystems;
				rootSystems[i] = rootSystem;
				playerLoop.subSystemList = rootSystems;
				PlayerLoop.SetPlayerLoop(playerLoop);
				return;
			}

			static void UpdateAllInstances()
			{
				for(currentInstanceIndex = instanceCount - 1; currentInstanceIndex >= 0; currentInstanceIndex--)
				{
					instances[currentInstanceIndex].OnUpdate();
				}
			}
		}

		protected abstract void OnUpdate();

		void OnEnable()
		{
			instanceCount++;
			if(instanceCount >= instances.Length)
			{
				var capacity = instanceCount + instanceCount;
				Array.Resize(ref instances, capacity);
			}

			instances[instanceCount - 1] = this;
		}

		void OnDisable()
		{
			var removeAt = Array.IndexOf(instances, this, 0, instanceCount);
			if(removeAt is -1)
			{
				return;
			}

			instances[removeAt] = null;
			instanceCount--;
			if(removeAt >= instanceCount)
			{
				return;
			}

			// The number of elements to move is the total count minus the index of the item after the removed one.
			// Example: count is now 4, we removed index 1. We need to move elements from index 2 up to index 3.
			Array.Copy(instances, removeAt + 1, instances, removeAt, instanceCount - removeAt);
			if(currentInstanceIndex > removeAt)
			{
				currentInstanceIndex--;
			}
		}
	}
}
