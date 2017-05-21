using System;
using System.Collections.Generic;
using Rynchodon.Attached;
using Rynchodon.Threading;
using Rynchodon.Utility.Vectors;
using Rynchodon.Utility;
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

		private readonly IMyCubeGrid myGrid;

		private PositionGrid m_centreOfMass;
		private float m_mass;

		private Vector3 m_inertiaMoment;
		private Vector3 m_invertedInertiaMoment;
		private Vector3 m_calcInertiaMoment;

		private bool m_updating;
		private FastResourceLock m_lock = new FastResourceLock();

		public float GyroForce { get; private set; }

		private Logable Log { get { return new Logable(myGrid); } }

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
			this.myGrid = grid;

			ClearOverrides();
		}

		private void CalcGyroForce()
		{
			float force = 0f;
			CubeGridCache cache = CubeGridCache.GetFor(myGrid);
			if (cache == null)
				return;
			foreach (MyGyro g in cache.BlocksOfType(typeof(MyObjectBuilder_Gyro)))
				if (g.IsWorking)
					force += g.MaxGyroForce; // MaxGyroForce accounts for power ratio and modules

			GyroForce = force;
		}

		public void Update()
		{
			CalcGyroForce();

			PositionGrid centre = ((PositionWorld)myGrid.Physics.CenterOfMassWorld).ToGrid(myGrid);

			if (Vector3.DistanceSquared(centre, m_centreOfMass) > 1f || Math.Abs(myGrid.Physics.Mass - m_mass) > 1000f)
			{
				using (m_lock.AcquireExclusiveUsing())
				{
					if (m_updating)
					{
						Log.DebugLog("already updating", Logger.severity.DEBUG);
						return;
					}
					m_updating = true;
				}

				m_centreOfMass = centre;
				m_mass = myGrid.Physics.Mass;

				Thread.EnqueueAction(CalculateInertiaMoment);
			}
		}

		#region Inertia Moment

		private void CalculateInertiaMoment()
		{
			Log.DebugLog("recalculating inertia moment", Logger.severity.INFO);

			m_calcInertiaMoment = Vector3.Zero;
			foreach (IMySlimBlock slim in (AttachedGrid.AttachedSlimBlocks(myGrid, AttachedGrid.AttachmentKind.Physics, true)))
				AddToInertiaMoment(slim);

			using (m_lock.AcquireExclusiveUsing())
			{
				m_inertiaMoment = m_calcInertiaMoment;
				m_invertedInertiaMoment = 1f / m_inertiaMoment;
			}

			Log.DebugLog("Inertia moment: " + m_inertiaMoment, Logger.severity.DEBUG);
			Log.DebugLog("Inverted inertia moment: " + m_invertedInertiaMoment, Logger.severity.DEBUG);

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

			//Log.DebugLog("block: " + block.getBestName() + ", block mass: " + block.Mass + ", min: " + Min + ", max: " + Max + ", step: " + stepSize + ", size: " + size + ", volume: " + size.Volume + ", sub mass: " + mass, "AddToInertiaMoment()");

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

		#endregion Inertia Moment

		#region Override

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
			foreach (MyGyro gyro in CubeGridCache.GetFor(myGrid).BlocksOfType(typeof(MyObjectBuilder_Gyro)))
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

		#endregion Override

	}
}
