using System;
using System.Collections.Generic;

using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Weapons
{
	public class Cluster
	{

		private readonly Logger m_logger;

		public readonly IMyEntity Master;
		public readonly List<IMyEntity> Slaves;
		public readonly List<Vector3> SlaveOffsets;
		private readonly float MinOffMult;
		public float OffsetMulti;

		public Cluster(List<IMyEntity> missiles)
		{
			m_logger = new Logger(GetType().Name);

			Vector3 centre = Vector3.Zero;
			foreach (IMyEntity miss in missiles)
				centre += miss.GetPosition();
			centre /= missiles.Count;

			float masterDistSq = float.MaxValue;
			foreach (IMyEntity miss in missiles)
			{
				float distSq = Vector3.DistanceSquared(centre, miss.GetPosition());
				if (distSq < masterDistSq)
				{
					Master = miss;
					masterDistSq = distSq;
				}
			}

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
				m_logger.debugLog("slave: " + miss + ", offset: " + SlaveOffsets[SlaveOffsets.Count - 1], "Cluster()", Logger.severity.TRACE);
				if (distSq > Furthest)
					Furthest = distSq;
			}
			Furthest = (float)Math.Sqrt(Furthest);
			for (int i = 0; i < SlaveOffsets.Count; i++)
				SlaveOffsets[i] = SlaveOffsets[i] / Furthest;

			MinOffMult = Furthest * 2f;
			OffsetMulti = Furthest;
			m_logger.debugLog("created new cluster, missiles: " + missiles.Count + ", slaves: " + Slaves.Count + ", offsets: " + SlaveOffsets.Count + ", furthest: " + Furthest, "Cluster()", Logger.severity.DEBUG);
		}

		public void AdjustMulti(float target)
		{
			OffsetMulti = MathHelper.Max(MinOffMult, target);
		}

	}
}
