#if DEBUG
#define TRACE
#endif

using System;
using System.Collections.Generic;
using Rynchodon.Attached;
using Rynchodon.Utility;
using Rynchodon.Utility.Vectors;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;
using System.Diagnostics;
using Sandbox.Game.Entities.Cube;

namespace Rynchodon
{
	/// <summary>
	/// This class controls rotors for the purposes of pointing a block at a target. The turret does not have to be armed, it could be solar panels facing the sun, for example.
	/// </summary>
	/// <remarks>
	/// <para>To limit confusion, motor shall refer to the rotor and stator,  rotor shall only refer to the part that rotates.</para>
	/// <para>Will only change motor velocity, other parameters need to be set by the player.</para>
	/// </remarks>
	public class MotorTurret : IDisposable
	{
		private class StaticVariables
		{
			public readonly MyObjectBuilderType[] types_Rotor = new MyObjectBuilderType[] { typeof(MyObjectBuilder_MotorRotor), typeof(MyObjectBuilder_MotorAdvancedRotor), typeof(MyObjectBuilder_MotorStator), typeof(MyObjectBuilder_MotorAdvancedStator) };
			public readonly HashSet<IMyMotorStator> claimedStators = new HashSet<IMyMotorStator>();
		}

		private static StaticVariables Static = new StaticVariables();
		public delegate void StatorChangeHandler(IMyMotorStator statorEl, IMyMotorStator statorAz);
		private const float speedLimit = MathHelper.Pi;

		private static void GetElevationAndAzimuth(Vector3 vector, out float elevation, out float azimuth)
		{
			elevation = (float)Math.Asin(vector.Y);

			// azimuth is arbitrary when elevation is +/- 1
			if (Math.Abs(vector.Y) > 0.9999f)
				azimuth = float.NaN;
			else
				azimuth = (float)Math.Atan2(vector.X, vector.Z);
		}

		/// <summary>
		/// Determines if a stator can rotate a specific amount.
		/// </summary>
		/// <returns>True iff the stator can rotate the specified amount.</returns>
		private static bool CanRotate(IMyMotorStator stator, float amount)
		{
			// NaN is treated as zero
			if (float.IsNaN(amount) || stator.UpperLimit - stator.LowerLimit > MathHelper.TwoPi)
				return true;

			return amount > 0f ? amount <= stator.UpperLimit - stator.Angle : amount >= stator.LowerLimit - stator.Angle;
		}

		private readonly IMyCubeBlock FaceBlock;
		private readonly StatorChangeHandler OnStatorChange;
		/// <summary>Conversion between angle delta and speed.</summary>
		private readonly float m_rotationSpeedMulitplier;

		private bool m_claimedElevation, m_claimedAzimuth;
		private IMyMotorStator value_statorEl, value_statorAz;

		/// <summary>Shall be the stator that is closer to the FaceBlock (by grids).</summary>
		public IMyMotorStator StatorEl
		{
			get { return value_statorEl; }
			private set
			{
				if (value_statorEl == value)
					return;
				if (m_claimedElevation)
				{
					m_claimedElevation = false;
					if (Static.claimedStators.Remove(value_statorEl))
						Log.DebugLog("Released claim on elevation stator: " + value_statorEl.nameWithId(), Logger.severity.DEBUG);
					else
						Log.AlwaysLog("Failed to remove claim on elevation stator: " + value_statorEl.nameWithId(), Logger.severity.ERROR);
				}
				value_statorEl = value;
			}
		}

		/// <summary>Shall be the stator that is further from the FaceBlock (by grids).</summary>
		public IMyMotorStator StatorAz
		{
			get { return value_statorAz; }
			private set
			{
				if (value_statorAz == value)
					return;
				if (m_claimedAzimuth)
				{
					m_claimedAzimuth = false;
					if (Static.claimedStators.Remove(value_statorAz))
						Log.DebugLog("Released claim on azimuth stator: " + value_statorAz.nameWithId(), Logger.severity.DEBUG);
					else
						Log.AlwaysLog("Failed to remove claim on azimuth stator: " + value_statorAz.nameWithId(), Logger.severity.ERROR);
				}
				value_statorAz = value;
			}
		}

		private Logable Log { get { return new Logable(FaceBlock); } }

		public MotorTurret(IMyCubeBlock block, StatorChangeHandler handler = null, int updateFrequency = 100)
		{
			this.FaceBlock = block;
			this.OnStatorChange = handler;
			this.SetupStators();

			m_rotationSpeedMulitplier = 60f / Math.Max(updateFrequency, 6);
		}

		~MotorTurret()
		{
			Dispose();
		}

		public void Dispose()
		{
			if (!Globals.WorldClosed)
			{
				Stop();
				StatorEl = null;
				StatorAz = null;
			}
		}

		/// <summary>
		/// Try to face in the target direction.
		/// </summary>
		public void FaceTowards(DirectionWorld target)
		{
			Log.DebugLog("Not server!", Logger.severity.FATAL, condition: !MyAPIGateway.Multiplayer.IsServer);
			Debug.Assert(Threading.ThreadTracker.IsGameThread, "not game thread");

			if (!SetupStators())
				return;

			float bestDeltaElevation;
			float bestDeltaAzimuth;
			CalcFaceTowards(target, out bestDeltaElevation, out bestDeltaAzimuth);

			Log.TraceLog("Face: " + target + ", elevation: " + bestDeltaElevation + ", azimuth: " + bestDeltaAzimuth);

			if (m_claimedElevation)
				SetVelocity(StatorEl, bestDeltaElevation);
			if (m_claimedAzimuth)
				SetVelocity(StatorAz, bestDeltaAzimuth);
		}

		public bool CanFaceTowards(DirectionWorld target)
		{
			if (!StatorOK())
				return false;

			float bestDeltaElevation;
			float bestDeltaAzimuth;
			return CalcFaceTowards(target, out bestDeltaElevation, out bestDeltaAzimuth);
		}

		private bool CalcFaceTowards(DirectionWorld target, out float bestDeltaElevation, out float bestDeltaAzimuth)
		{
			float bestSumDeltaMag = float.PositiveInfinity;
			bestDeltaElevation = bestDeltaAzimuth = float.NaN;
			Vector3 elTarget = target.ToBlock(StatorEl);

			foreach (var direction in FaceBlock.FaceDirections())
			{
				DirectionWorld faceDirection = new DirectionWorld() { vector = FaceBlock.WorldMatrix.GetDirectionVector(direction) };

				Vector3 elCurrent = faceDirection.ToBlock(StatorEl);

				Log.TraceLog(nameof(target) + ": " + target + ", " + nameof(faceDirection) + ": " + faceDirection + ", " + nameof(elTarget) + ": " + elTarget + ", " + nameof(elCurrent) + ": " + elCurrent);

				float firstElDelta, secondElDelta;
				CalcDelta(elCurrent, elTarget, out firstElDelta, out secondElDelta);

				float deltaAzimuth;

				if (CanRotate(StatorEl, firstElDelta) && CalcAzimuth(target, faceDirection, firstElDelta, out deltaAzimuth))
				{
					float sumDeltaMag = Math.Abs(firstElDelta) + Math.Abs(deltaAzimuth);
					Log.TraceLog(nameof(firstElDelta) + ": " + firstElDelta + ", " + nameof(deltaAzimuth) + ": " + deltaAzimuth + ", " + nameof(bestSumDeltaMag) + ": " + bestSumDeltaMag);
					if (sumDeltaMag < bestSumDeltaMag)
					{
						bestSumDeltaMag = sumDeltaMag;
						bestDeltaElevation = firstElDelta;
						bestDeltaAzimuth = deltaAzimuth;
					}
				}
				else if (CanRotate(StatorEl, secondElDelta) && CalcAzimuth(target, faceDirection, secondElDelta, out deltaAzimuth))
				{
					float sumDeltaMag = Math.Abs(secondElDelta) + Math.Abs(deltaAzimuth);
					Log.TraceLog(nameof(secondElDelta) + ": " + secondElDelta + ", " + nameof(deltaAzimuth) + ": " + deltaAzimuth + ", " + nameof(bestSumDeltaMag) + ": " + bestSumDeltaMag);
					if (sumDeltaMag < bestSumDeltaMag)
					{
						bestSumDeltaMag = sumDeltaMag;
						bestDeltaElevation = secondElDelta;
						bestDeltaAzimuth = deltaAzimuth;
					}
				}
				Log.TraceLog("cannot rotate to target");
			}

			if (bestSumDeltaMag != float.PositiveInfinity)
				return true;

			Log.TraceLog("Cannot rotate to target");
			bestDeltaElevation = bestDeltaAzimuth = float.NaN;
			return false;
		}

		private bool CalcAzimuth(DirectionWorld target, DirectionWorld faceDirection, float elDelta, out float azDelta)
		{
			ApplyElevationChange(ref faceDirection, elDelta);
			Vector3 azCurrent = faceDirection.ToBlock(StatorAz);
			Vector3 azTarget = target.ToBlock(StatorAz);

			Log.TraceLog(nameof(target) + ": " + target + ", " + nameof(faceDirection) + ": " + faceDirection + ", " + nameof(azTarget) + ": " + azTarget + ", " + nameof(azCurrent) + ": " + azCurrent);

			float firstAzDelta, secondAzDelta;
			CalcDelta(azCurrent, azTarget, out firstAzDelta, out secondAzDelta);

			if (CanRotate(StatorAz, firstAzDelta))
			{
				Log.TraceLog("First azimuth delta approved: " + firstAzDelta);
				azDelta = firstAzDelta;
				return true;
			}

			if (CanRotate(StatorAz, secondAzDelta))
			{
				Log.TraceLog("Second azimuth delta approved: " + secondAzDelta);
				azDelta = secondAzDelta;
				return true;
			}

			Log.TraceLog("Neither azimuth delta approved");
			azDelta = float.NaN;
			return false;
		}

		/// <summary>
		/// Rotate current direction by elevation stator's delta.
		/// </summary>
		/// <param name="facing">The current direction.</param>
		/// <param name="elDelta">The change in angle of elevation stator.</param>
		private void ApplyElevationChange(ref DirectionWorld facing, float elDelta)
		{
			Log.TraceLog(nameof(facing) + " is " + facing);
			Vector3 axis = StatorEl.WorldMatrix.Down;
			Log.TraceLog(nameof(axis) + " is " + axis);
			Log.TraceLog(nameof(elDelta) + " is " + elDelta);
			Quaternion rotation; Quaternion.CreateFromAxisAngle(ref axis, elDelta, out rotation);
			Vector3.Transform(ref facing.vector, ref rotation, out facing.vector);
			Log.TraceLog(nameof(facing) + " is " + facing);
		}

		/// <summary>
		/// Calculate the angular difference in X/Z plane from one vector to another.
		/// </summary>
		/// <param name="current">The vector that represents the current position.</param>
		/// <param name="target">The vector that represents the target position.</param>
		/// <param name="delta1">The change in angle from current to target, rotating the shorter distance.</param>
		/// <param name="delta2">The change in angle from current to target, rotating the longer distance.</param>
		private static void CalcDelta(Vector3 current, Vector3 target, out float delta1, out float delta2)
		{
			Logger.TraceLog(nameof(current) + " is " + current);
			Logger.TraceLog(nameof(target) + " is " + target);

			current.Y = 0f;
			float temp = current.Normalize();
			if (temp == 0f || !temp.IsValid())
			{
				delta1 = delta2 = temp;
				return;
			}
			target.Y = 0f;
			temp = target.Normalize();
			if (temp == 0f || !temp.IsValid())
			{
				delta1 = delta2 = temp;
				return;
			}

			//Debug.Assert(current.Y == 0f, nameof(current) + " does not have Y == 0");
			//Debug.Assert(target.Y == 0f, nameof(target) + " does not have Y == 0");
			//current.AssertNormalized(nameof(current));
			//target.AssertNormalized(nameof(target));

			Logger.TraceLog("normalized " + nameof(current) + " is " + current);
			Logger.TraceLog("normalized " + nameof(target) + " is " + target);

			float dot; Vector3.Dot(ref current, ref target, out dot);
			if (dot > 0.99999f)
			{
				Logger.TraceLog("Already facing desired direction");
				// already facing the desired direction
				delta1 = delta2 = 0f;
				return;
			}

			float angle = (float)Math.Acos(dot);
			if (target.Z * current.X - target.X * current.Z > 0f) // Y component of cross product > 0
			{
				delta1 = angle;
				delta2 = angle - MathHelper.TwoPi;
			}
			else
			{
				delta1 = -angle;
				delta2 = MathHelper.TwoPi - angle;
			}

			Logger.TraceLog(nameof(delta1) + " is " + delta1);
			Logger.TraceLog(nameof(delta2) + " is " + delta2);
			return;
		}

		public void Stop()
		{
			Log.DebugLog("Not server!", Logger.severity.FATAL, condition: !MyAPIGateway.Multiplayer.IsServer);

			if (!StatorOK())
				return;

			if (m_claimedElevation)
				SetVelocity(StatorEl, 0);
			if (m_claimedAzimuth)
				SetVelocity(StatorAz, 0);
		}

		private bool StatorOK()
		{ return StatorEl != null && !StatorEl.Closed && StatorEl.IsAttached && StatorAz != null && !StatorAz.Closed && StatorAz.IsAttached; }

		private bool ClaimStators()
		{
			if (!m_claimedElevation)
			{
				m_claimedElevation = Static.claimedStators.Add(StatorEl);
				Log.DebugLog("claimed elevation stator: " + StatorEl.nameWithId(), Logger.severity.DEBUG, condition: m_claimedElevation);
			}
			if (!m_claimedAzimuth)
			{
				m_claimedAzimuth = Static.claimedStators.Add(StatorAz);
				Log.DebugLog("claimed azimuth stator: " + StatorAz.nameWithId(), Logger.severity.DEBUG, condition: m_claimedAzimuth);
			}
			return m_claimedElevation || m_claimedAzimuth;
		}

		private bool SetupStators()
		{
			if (StatorOK())
			{
				//Log.DebugLog("Stators are already set up", "SetupStators()");
				return ClaimStators();
			}

			// get StatorEl from FaceBlock's grid
			IMyMotorStator tempStator;
			IMyMotorRotor RotorEl;
			if (!GetStatorRotor(FaceBlock.CubeGrid, out tempStator, out RotorEl))
			{
				Log.DebugLog("Failed to get StatorEl");
				StatorEl = null;
				if (OnStatorChange != null)
					OnStatorChange(StatorEl, StatorAz);
				return false;
			}
			StatorEl = tempStator;

			// get StatorAz from grid from either StatorEl or RotorEl, whichever is not on Faceblock's grid
			IMyCubeGrid getBFrom;
			if (RotorEl.CubeGrid == FaceBlock.CubeGrid)
				getBFrom = StatorEl.CubeGrid as IMyCubeGrid;
			else
				getBFrom = RotorEl.CubeGrid;

			IMyMotorRotor RotorAz;
			if (!GetStatorRotor(getBFrom, out tempStator, out RotorAz, StatorEl, RotorEl))
			{
				Log.DebugLog("Failed to get StatorAz");
				StatorAz = null;
				if (OnStatorChange != null)
					OnStatorChange(StatorEl, StatorAz);
				return false;
			}
			StatorAz = tempStator;

			Log.DebugLog("Successfully got stators. Elevation = " + StatorEl.DisplayNameText + ", Azimuth = " + StatorAz.DisplayNameText);
			if (OnStatorChange != null)
				OnStatorChange(StatorEl, StatorAz);
			Stop();
			return ClaimStators();
		}

		private bool GetStatorRotor(IMyCubeGrid grid, out IMyMotorStator Stator, out IMyMotorRotor Rotor, IMyMotorStator IgnoreStator = null, IMyCubeBlock IgnoreRotor = null)
		{
			CubeGridCache cache = CubeGridCache.GetFor(grid);

			// first stator & rotater that are found that are not working
			// returned if a working set is not found
			IMyMotorStator offStator = null;
			IMyMotorRotor offRotor = null;

			foreach (MyObjectBuilderType type in Static.types_Rotor)
				foreach (IMyCubeBlock motorPart in cache.BlocksOfType(type))
				{
					if (!FaceBlock.canControlBlock(motorPart))
						continue;

					Stator = motorPart as IMyMotorStator;
					if (Stator != null)
					{
						if (Stator == IgnoreStator)
							continue;
						Rotor = (IMyMotorRotor)Stator.Top;
						if (Rotor != null)
						{
							if (Stator.IsWorking && Rotor.IsWorking)
								return true;
							else if (offStator == null || offRotor == null)
							{
								offStator = Stator;
								offRotor = Rotor;
							}
						}
					}
					else
					{
						Rotor = motorPart as IMyMotorRotor;
						if (Rotor == null || Rotor == IgnoreRotor)
							continue;
						Stator = (IMyMotorStator)Rotor.Base;
						if (Stator != null)
						{
							if (Stator.IsWorking && Rotor.IsWorking)
								return true;
							else if (offStator == null || offRotor == null)
							{
								offStator = Stator;
								offRotor = Rotor;
							}
						}
					}
				}

			if (offStator != null && offRotor != null)
			{
				Stator = offStator;
				Rotor = offRotor;
				return true;
			}

			Stator = null;
			Rotor = null;
			return false;
		}

		private void SetVelocity(IMyMotorStator stator, float angle)
		{
			SetVelocity((MyMotorStator)stator, angle);
		}

		private void SetVelocity(MyMotorStator stator, float angle)
		{
			Log.DebugLog("Not server!", Logger.severity.FATAL, condition: !MyAPIGateway.Multiplayer.IsServer);

			// keep in mind, azimuth is undefined if elevation is straight up or straight down
			float speed = angle.IsValid() ? MathHelper.Clamp(angle * m_rotationSpeedMulitplier, -speedLimit, speedLimit) : 0f;

			float currentSpeed = stator.TargetVelocity;
			if (Math.Abs(speed - currentSpeed) > 0.001f)
			{
				Log.TraceLog("Setting speed. current speed: " + currentSpeed + ", target: " + speed + ", angle: " + angle);
				stator.TargetVelocity.Value = speed;
			}
			else
				Log.TraceLog("Not changing speed. current speed: " + currentSpeed + ", target: " + speed + ", angle: " + angle);
		}
	}
}
