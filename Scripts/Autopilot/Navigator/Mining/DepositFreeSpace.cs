using Rynchodon.Threading;
using Sandbox.Game.Entities;
using VRageMath;

namespace Rynchodon.Autopilot.Navigator.Mining
{
	/// <summary>
	/// Performs a background task to get a free space near a deposit.
	/// </summary>
	class DepositFreeSpace
	{

		private static readonly ThreadManager Thread = new ThreadManager(2, true, typeof(DepositFreeSpace).Name);

		public readonly MyVoxelBase Voxel;
		public readonly Vector3D Deposit;
		public readonly double MinRadius;

		public bool Completed { get; private set; }
		public bool NearSurface { get; private set; }
		private Vector3D m_freePosition;
		public Vector3D FreePosition { get { return m_freePosition; } }

		public DepositFreeSpace(MyVoxelBase voxel, ref Vector3D deposit, double minRadius)
		{
			this.Voxel = voxel;
			this.Deposit = deposit;
			this.MinRadius = minRadius;

			Thread.EnqueueAction(Run);
		}

		private void Run()
		{
			Voxel.FindFreeSpace(Deposit, MinRadius, out m_freePosition);
			CapsuleD capsule = new CapsuleD(m_freePosition, Deposit, 1f);
			Vector3D closest; CapsuleDExtensions.Intersects(ref capsule, Voxel, out closest);
			NearSurface = Vector3D.DistanceSquared(Deposit, closest) < 100d;
			Completed = true;
		}

	}
}
