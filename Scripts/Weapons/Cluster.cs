using System;
using System.Collections.Generic;
using Rynchodon.Utility;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Weapons
{
	public class Cluster
	{
		private readonly float MinOffMult;

		public readonly IMyEntity Master;
		public readonly List<IMyEntity> Slaves;
		public readonly List<Vector3> SlaveOffsets;
		public float OffsetMulti;
		public Vector3 masterVelocity;

		public Cluster(List<IMyEntity> missiles, IMyEntity launcher)
		{
			Vector3 centre = Vector3.Zero;
			foreach (IMyEntity miss in missiles)
				centre += miss.GetPosition();
			centre /= missiles.Count;

			float masterDistSq = float.MaxValue;
			foreach (IMyEntity miss in missiles)
			{
				if (miss.Closed)
				{
					Logger.DebugLog("missile is closed: " + miss.nameWithId());
					continue;
				}
				if (miss.Physics == null)
				{
					Logger.DebugLog("missile has no physics: " + miss.nameWithId());
					continue;
				}

				float distSq = Vector3.DistanceSquared(centre, miss.GetPosition());
				if (distSq < masterDistSq)
				{
					Master = miss;
					masterDistSq = distSq;
				}
			}

			if (Master == null)
				return;

			masterVelocity = Master.Physics.LinearVelocity;

			// master must initially have same orientation as launcher or rail will cause a rotation
			MatrixD masterMatrix = launcher.WorldMatrix;
			masterMatrix.Translation = Master.WorldMatrix.Translation;
			MainLock.UsingShared(() => Master.WorldMatrix = masterMatrix);

			Vector3 masterPos = Master.GetPosition();
			MatrixD masterInv = Master.WorldMatrixNormalizedInv;
			float Furthest = 0f;
			Slaves = new List<IMyEntity>(missiles.Count - 1);
			SlaveOffsets = new List<Vector3>(missiles.Count - 1);
			foreach (IMyEntity miss in missiles)
			{
				if (miss == Master)
					continue;
				Slaves.Add(miss);
				SlaveOffsets.Add(Vector3.Transform(miss.GetPosition(), masterInv));
				float distSq = Vector3.DistanceSquared(miss.GetPosition(), masterPos);
				Logger.DebugLog("slave: " + miss + ", offset: " + SlaveOffsets[SlaveOffsets.Count - 1], Rynchodon.Logger.severity.TRACE);
				if (distSq > Furthest)
					Furthest = distSq;
			}
			Furthest = (float)Math.Sqrt(Furthest);
			for (int i = 0; i < SlaveOffsets.Count; i++)
				SlaveOffsets[i] = SlaveOffsets[i] / Furthest;

			MinOffMult = Furthest * 2f;
			OffsetMulti = Furthest * 1e6f; // looks pretty
			Logger.DebugLog("created new cluster, missiles: " + missiles.Count + ", slaves: " + Slaves.Count + ", offsets: " + SlaveOffsets.Count + ", furthest: " + Furthest, Rynchodon.Logger.severity.DEBUG);
		}

		public void AdjustMulti(float target)
		{
			OffsetMulti = MathHelper.Max(MinOffMult, target);
		}

	}
}
