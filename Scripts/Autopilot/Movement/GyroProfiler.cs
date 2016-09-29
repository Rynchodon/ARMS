using System;
using System.Collections.Generic;
using Rynchodon.Attached;
using Rynchodon.Threading;
using Rynchodon.Utility.Vectors;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage;
using VRage.Game.ModAPI;
using VRage.Library.Utils;
using VRageMath;

namespace Rynchodon.Autopilot.Movement
{
	/// <remarks>
	/// Space engineers only uses the smallest inverse of the moments of inertia so GyroProfiler does not calculate the products of inertia.
	/// </remarks>
	public class GyroProfiler
	{

		private static readonly ThreadManager Thread = new ThreadManager(2, true, "GyroProfiler");
		private static ITerminalProperty<bool> TP_GyroOverrideToggle;

		private readonly Logger m_logger;
		private readonly IMyCubeGrid myGrid;

		private PositionGrid m_centreOfMass;
		private float m_mass;

		private Vector3 m_inertiaMoment;
		private Vector3 m_invertedInertiaMoment;
		private Vector3 m_calcInertiaMoment;

		private bool m_updating;
		private FastResourceLock m_lock = new FastResourceLock();

		public float GyroForce { get; private set; }

		/// <summary>The three moments of inertial for the coordinate axes</summary>
		public Vector3 InertiaMoment
		{
			get
			{
				using (m_lock.AcquireSharedUsing())
					return m_inertiaMoment;
			}
		}

		/// <summary>Inverse of InertiaMoment</summary>
		public Vector3 InvertedInertiaMoment
		{
			get
			{
				using (m_lock.AcquireSharedUsing())
					return m_invertedInertiaMoment;
			}
		}

		public GyroProfiler(IMyCubeGrid grid)
		{
			this.m_logger = new Logger(() => grid.DisplayName);
			this.myGrid = grid;

			ClearOverrides();
		}

		private void CalcGyroForce()
		{
			ReadOnlyList<IMyCubeBlock> gyros = CubeGridCache.GetFor(myGrid).GetBlocksOfType(typeof(MyObjectBuilder_Gyro));
			if (gyros == null)
			{
				GyroForce = 0f;
			}

			float force = 0f;
			foreach (MyGyro g in gyros)
				if (g.IsWorking)
					force += g.MaxGyroForce; // MaxGyroForce accounts for power ratio and modules

			GyroForce = force;
		}

		public void Update()
		{
			CalcGyroForce();

			PositionGrid centre = ((PositionWorld)myGrid.Physics.CenterOfMassWorld).ToGrid(myGrid);

			if (Vector3.DistanceSquared(centre, m_centreOfMass) > 1f || Math.Abs(myGrid.Physics.Mass - m_mass) > 10f)
			{
				using (m_lock.AcquireExclusiveUsing())
				{
					if (m_updating)
					{
						m_logger.debugLog("already updating", Logger.severity.DEBUG);
						return;
					}
					m_updating = true;
				}

				m_centreOfMass = centre;
				m_mass = myGrid.Physics.Mass;

				Thread.EnqueueAction(CalculateInertiaMoment);
			}
		}

		private void CalculateInertiaMoment()
		{
			m_logger.debugLog("recalculating inertia moment", Logger.severity.INFO);
			MyGameTimer timer = new MyGameTimer();

			List<IMySlimBlock> blocks = ResourcePool<List<IMySlimBlock>>.Get();
			m_calcInertiaMoment = Vector3.Zero;
			AttachedGrid.RunOnAttachedBlock(myGrid, AttachedGrid.AttachmentKind.Physics, block => {
				blocks.Add(block);
				return false;
			}, true);

			foreach (var block in blocks)
				AddToInertiaMoment(block);
			blocks.Clear();
			ResourcePool<List<IMySlimBlock>>.Return(blocks);

			using (m_lock.AcquireExclusiveUsing())
			{
				m_inertiaMoment = m_calcInertiaMoment;
				m_invertedInertiaMoment = 1f / m_inertiaMoment;
			}

			m_logger.debugLog("Calculated in " + timer.Elapsed.ToPrettySeconds());
			m_logger.debugLog("Inertia moment: " + m_inertiaMoment, Logger.severity.DEBUG);
			m_logger.debugLog("Inverted inertia moment: " + m_invertedInertiaMoment, Logger.severity.DEBUG);

			m_updating = false;
		}

		private void AddToInertiaMoment(IMySlimBlock block)
		{
			float stepSize = myGrid.GridSize * 0.5f;
			Vector3 Min, Max;
			IMyCubeBlock fatblock = block.FatBlock;
			if (fatblock != null)
			{
				Min = ((Vector3)fatblock.Min - 0.5f) * myGrid.GridSize + stepSize * 0.5f;
				Max = ((Vector3)fatblock.Max + 0.5f) * myGrid.GridSize - stepSize * 0.5f;
			}
			else
			{
				Vector3 position = block.Position;
				Min = (position - 0.5f) * myGrid.GridSize + stepSize * 0.5f;
				Max = (position + 0.5f) * myGrid.GridSize - stepSize * 0.5f;
			}

			Vector3 size = (Max - Min) / stepSize + 1f;
			float mass = block.Mass / size.Volume;

			//m_logger.debugLog("block: " + block.getBestName() + ", block mass: " + block.Mass + ", min: " + Min + ", max: " + Max + ", step: " + stepSize + ", size: " + size + ", volume: " + size.Volume + ", sub mass: " + mass, "AddToInertiaMoment()");

			Vector3 current = Vector3.Zero;
			for (current.X = Min.X; current.X <= Max.X + stepSize * 0.5f; current.X += stepSize)
				for (current.Y = Min.Y; current.Y <= Max.Y + stepSize * 0.5f; current.Y += stepSize)
					for (current.Z = Min.Z; current.Z <= Max.Z + stepSize * 0.5f; current.Z += stepSize)
						AddToInertiaMoment(current, mass);
		}

		private void AddToInertiaMoment(Vector3 point, float mass)
		{
			Vector3 relativePosition = point - m_centreOfMass;
			Vector3 moment = mass * relativePosition.LengthSquared() * Vector3.One - relativePosition * relativePosition;
			m_calcInertiaMoment += moment;
		}

		///// <summary>
		///// Sets the overrides of gyros to match RotateVelocity. Should be called on game thread.
		///// </summary>
		//public void SetOverrides(ref DirectionWorld RotateVelocity)
		//{
		//	ReadOnlyList<IMyCubeBlock> gyros = CubeGridCache.GetFor(myGrid).GetBlocksOfType(typeof(MyObjectBuilder_Gyro));
		//	if (gyros == null)
		//		return;

		//	foreach (MyGyro gyro in gyros)
		//	{
		//		if (!gyro.GyroOverride)
		//			SetOverride(gyro, true);
		//		gyro.SetGyroTorque(RotateVelocity.ToBlock(gyro));
		//	}
		//}

		/// <summary>
		/// Disable overrides for every gyro. Should be called on game thread.
		/// </summary>
		public void ClearOverrides()
		{
			ReadOnlyList<IMyCubeBlock> gyros = CubeGridCache.GetFor(myGrid).GetBlocksOfType(typeof(MyObjectBuilder_Gyro));
			if (gyros == null)
				return;

			foreach (MyGyro gyro in gyros)
				if (gyro.GyroOverride)
				{
					SetOverride(gyro, false);
					gyro.SetGyroTorque(Vector3.Zero);
				}
		}

		private void SetOverride(IMyTerminalBlock gyro, bool enable)
		{
			if (TP_GyroOverrideToggle == null)
				TP_GyroOverrideToggle = gyro.GetProperty("Override") as ITerminalProperty<bool>;
			TP_GyroOverrideToggle.SetValue(gyro, enable);
		}

	}
}
