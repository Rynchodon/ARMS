using System;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;

namespace Rynchodon.Utility
{
	/// <summary>
	/// Fix a bug with main cockpit that prevents control from being released.
	/// </summary>
	static class MainCockpitFix
	{

		public static void AddController(IMyShipController controller)
		{
			AddController((MyShipController)controller);
		}

		public static void AddController(MyShipController controller)
		{
			FieldInfo fieldMainCockpit = typeof(MyShipController).GetField("m_isMainCockpit", BindingFlags.Instance | BindingFlags.NonPublic);
			if (fieldMainCockpit == null)
				throw new NullReferenceException("MyShipController.m_isMainCockpit does not exist or has unexpected binding");
			VRage.Sync.SyncBase valueMainCockpit = (VRage.Sync.SyncBase)fieldMainCockpit.GetValue(controller);

			valueMainCockpit.ValueChanged += (obj) => MainCockpitChanged(controller);
		}

		private static void MainCockpitChanged(MyShipController controller)
		{
			MyGroupControlSystem system = controller.CubeGrid.GridSystems.ControlSystem;

			MethodInfo OnControlReleased = typeof(MyGridSelectionSystem).GetMethod("OnControlReleased", BindingFlags.Instance | BindingFlags.NonPublic);
			if (OnControlReleased == null)
				throw new NullReferenceException("MyGridSelectionSystem.OnControlReleased does not exist or has unexpected binding");

			MethodInfo OnControlAcquired = typeof(MyGridSelectionSystem).GetMethod("OnControlAcquired", BindingFlags.Instance | BindingFlags.NonPublic);
			if (OnControlAcquired == null)
				throw new NullReferenceException("MyGridSelectionSystem.OnControlAcquired does not exist or has unexpected binding");

			bool enabled = controller.IsMainCockpit;

			foreach (MySlimBlock block in controller.CubeGrid.GetBlocks())
			{
				MyShipController otherController = block.FatBlock as MyShipController;
				if (otherController != null && otherController.EnableShipControl && otherController != controller && otherController.ControllerInfo.Controller != null)
				{
					MyGridSelectionSystem selectSystem = otherController.GridSelectionSystem;

					if (enabled)
					{
						if (system != null)
							system.RemoveControllerBlock(otherController);
						if (selectSystem != null)
							OnControlReleased.Invoke(selectSystem, null);
					}
					else
					{
						if (system != null)
							system.AddControllerBlock(otherController);
						if (selectSystem != null)
							OnControlAcquired.Invoke(selectSystem, null);
					}
				}
			}
		}

	}
}
