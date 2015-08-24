using Sandbox.ModAPI.Interfaces;

namespace Rynchodon
{
	public static class IMyControllableEntityExtensions
	{

		public static void SetDamping(this IMyControllableEntity entity, bool enable)
		{
			if (entity.EnabledDamping != enable)
				entity.SwitchDamping();
		}

	}
}
