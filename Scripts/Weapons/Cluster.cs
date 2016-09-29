using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Weapons
{
	public class Cluster
	{

		[Serializable]
		public class Builder_Cluster
		{
			public long[] Slaves;
			public Vector3[] SlaveOffsets;
			public float MinOffMult, OffsetMulti;
		}

		private readonly Logger m_logger;
		private readonly float MinOffMult;

		public readonly IMyEntity Master;
		public readonly List<IMyEntity> Slaves;
		public readonly List<Vector3> SlaveOffsets;
		public float OffsetMulti;
		public Vector3 masterVelocity;

		public Cluster(List<IMyEntity> missiles, IMyEntity launcher)
		{
			m_logger = new Logger();

			Vector3 centre = Vector3.Zero;
			foreach (IMyEntity miss in missiles)
				centre += miss.GetPosition();
			centre /= missiles.Count;

			float masterDistSq = float.MaxValue;
			foreach (IMyEntity miss in missiles)
			{
				if (miss.Closed)
				{
					m_logger.debugLog("missile is closed: " + miss.nameWithId());
					continue;
				}
				if (miss.Physics == null)
				{
					m_logger.debugLog("missile has no physics: " + miss.nameWithId());
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
				m_logger.debugLog("slave: " + miss + ", offset: " + SlaveOffsets[SlaveOffsets.Count - 1], Logger.severity.TRACE);
				if (distSq > Furthest)
					Furthest = distSq;
			}
			Furthest = (float)Math.Sqrt(Furthest);
			for (int i = 0; i < SlaveOffsets.Count; i++)
				SlaveOffsets[i] = SlaveOffsets[i] / Furthest;

			MinOffMult = Furthest * 2f;
			OffsetMulti = Furthest * 1e6f; // looks pretty
			m_logger.debugLog("created new cluster, missiles: " + missiles.Count + ", slaves: " + Slaves.Count + ", offsets: " + SlaveOffsets.Count + ", furthest: " + Furthest, Logger.severity.DEBUG);
		}

		public Cluster(IMyEntity master, Builder_Cluster builder)
		{
			this.m_logger = new Logger();
			this.Master = master;
			this.MinOffMult = builder.MinOffMult;
			this.OffsetMulti = builder.OffsetMulti;
			this.masterVelocity = master.Physics.LinearVelocity;

			this.Slaves = new List<IMyEntity>(builder.Slaves.Length);
			this.SlaveOffsets = new List<Vector3>(builder.Slaves.Length);
			for (int index = 0; index < builder.Slaves.Length; index++)
			{
				IMyEntity slave;
				if (!MyAPIGateway.Entities.TryGetEntityById(builder.Slaves[index], out slave))
				{
					m_logger.alwaysLog("Failed to get slave for " + builder.Slaves[index], Logger.severity.WARNING);
					continue;
				}
				this.Slaves[index] = slave;
				this.SlaveOffsets[index] = builder.SlaveOffsets[index];
			}
		}

		public void AdjustMulti(float target)
		{
			OffsetMulti = MathHelper.Max(MinOffMult, target);
		}

		public Builder_Cluster GetBuilder()
		{
			Builder_Cluster result = new Builder_Cluster()
			{
				SlaveOffsets = SlaveOffsets.ToArray(),
				MinOffMult = MinOffMult,
				OffsetMulti = OffsetMulti,
			};

			result.Slaves = new long[Slaves.Count];
			for (int index = 0; index < Slaves.Count; index++)
				result.Slaves[index] = Slaves[index].EntityId;

			return result;
		}

	}
}
