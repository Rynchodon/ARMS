#if DEBUG
#define TRACE
#endif

using System;
using System.Collections.Generic;
using Rynchodon.Utility;
using Rynchodon.Utility.Vectors;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
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

		/// <summary>
		/// Determines if a stator can rotate a specific amount.
		/// </summary>
		/// <returns>True iff the stator can rotate the specified amount.</returns>
		private static bool WithinLimits(IMyMotorStator stator, float delta)
		{
			if (stator.UpperLimit - stator.LowerLimit >= MathHelper.TwoPi)
				return true;

			float target = stator.Angle + delta;
			return delta > 0f ? target <= stator.UpperLimit : target >= stator.LowerLimit;
		}

		/// <summary>
		/// Clamp an angle change by the angle limits of a stator.
		/// </summary>
		private static float ClampToLimits(IMyMotorStator stator, float delta)
		{
			if (stator.UpperLimit - stator.LowerLimit >= MathHelper.TwoPi)
				return delta;

			return delta > 0f ? Math.Min(delta, stator.UpperLimit - stator.Angle) : Math.Max(delta, stator.LowerLimit - stator.Angle);
		}

		/// <summary>
		/// Given two deltas that are not within angle limits, find the delta angle that gets closest to either delta.
		/// </summary>
		/// <param name="accuracy">How close delta is to either of the original deltas.</param>
		private static void BestEffort(IMyMotorStator stator, float firstDelta, float secondDelta, out float delta, out float accuracy)
		{
			float clampFirstAzDelta = ClampToLimits(stator, firstDelta);
			float clampSecondAzDelta = ClampToLimits(stator, secondDelta);

			float firstAccuracy = Math.Abs(firstDelta - clampFirstAzDelta);
			float secondAccuracy = Math.Abs(secondDelta - clampSecondAzDelta);

			if (firstAccuracy <= secondAccuracy)
			{
				delta = clampFirstAzDelta;
				accuracy = firstAccuracy;
			}
			else
			{
				delta = clampSecondAzDelta;
				accuracy = secondAccuracy;
			}
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
			angle.AssertIsValid();

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

		private readonly IMyCubeBlock FaceBlock;
		private readonly StatorChangeHandler OnStatorChange;
		/// <summary>Conversion between angle delta and speed.</summary>
		private readonly float m_rotationSpeedMulitplier;
		/// <summary>The maximum difference between the direction the turret is facing and the target where rotation is considered to be useful.</summary>
		/// <remarks>
		/// For solar facing, float.PositiveInfinity is used, indicating that the turret should make a best effort.
		/// For weapons, a lower value is used because the turret must point accurately enough that the weapon can hit the target.
		/// </remarks>
		private readonly float m_requiredAccuracyRadians;

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
					lock (Static.claimedStators)
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
					lock (Static.claimedStators)
						if (Static.claimedStators.Remove(value_statorAz))
							Log.DebugLog("Released claim on azimuth stator: " + value_statorAz.nameWithId(), Logger.severity.DEBUG);
						else
							Log.AlwaysLog("Failed to remove claim on azimuth stator: " + value_statorAz.nameWithId(), Logger.severity.ERROR);
				}
				value_statorAz = value;
			}
		}

		private Logable Log { get { return new Logable(FaceBlock); } }

		public MotorTurret(IMyCubeBlock block, StatorChangeHandler handler = null, int updateFrequency = 100, float requiredAccuracyRadians = float.PositiveInfinity)
		{
			this.FaceBlock = block;
			this.OnStatorChange = handler;

			m_rotationSpeedMulitplier = 60f / Math.Max(updateFrequency, 6);
			m_requiredAccuracyRadians = requiredAccuracyRadians;

			this.SetupStators();
		}

		~MotorTurret()
		{
			Dispose();
		}

		public void Dispose()
		{
			GC.SuppressFinalize(this);
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

			FaceResult bestResult;
			if (!CalcFaceTowards(target, out bestResult))
			{
				Stop();
				return;
			}

			Log.TraceLog("Face: " + target + ", elevation: " + bestResult.DeltaElevation + ", azimuth: " + bestResult.DeltaAzimuth);

			if (m_claimedElevation)
				SetVelocity(StatorEl, bestResult.DeltaElevation);
			if (m_claimedAzimuth)
				SetVelocity(StatorAz, bestResult.DeltaAzimuth);
		}

		/// <summary>
		/// Determine if the motor turret can face the target direction.
		/// </summary>
		/// <param name="target">The direction to face.</param>
		/// <returns>True iff the turret can face the target direction.</returns>
		public bool CanFaceTowards(DirectionWorld target)
		{
			if (!StatorOK())
				return false;

			FaceResult bestResult;
			return CalcFaceTowards(target, out bestResult);
		}

		private struct FaceResult
		{
			public static readonly FaceResult Default = new FaceResult()
			{
				AccuracySquared = float.PositiveInfinity,
				SumDeltaMag = float.PositiveInfinity,
				DeltaElevation = float.NaN,
				DeltaAzimuth = float.NaN
			};

			public float AccuracySquared;
			public float SumDeltaMag;
			public float DeltaElevation;
			public float DeltaAzimuth;

			public bool ReplaceBy(float accSq, float sumDelta)
			{
				if (accSq + 0.01f < AccuracySquared)
					return true;
				if (accSq - 0.01f > AccuracySquared)
					return false;
				return sumDelta < SumDeltaMag;
			}

			public override string ToString()
			{
				return nameof(AccuracySquared) + ": " + AccuracySquared + ", " + 
					nameof(SumDeltaMag) + ": " + SumDeltaMag + ", " + 
					nameof(DeltaElevation) + ": " + DeltaElevation + ", " + 
					nameof(DeltaAzimuth) + ": " + DeltaAzimuth;
			}
		}

		private bool CalcFaceTowards(DirectionWorld target, out FaceResult bestResult)
		{
			bestResult = FaceResult.Default;
			Vector3 elTarget = target.ToBlock(StatorEl);

			foreach (var direction in FaceBlock.FaceDirections())
			{
				DirectionWorld faceDirection = new DirectionWorld() { vector = FaceBlock.WorldMatrix.GetDirectionVector(direction) };
				Vector3 elCurrent = faceDirection.ToBlock(StatorEl);
				Log.TraceLog(nameof(target) + ": " + target + ", " + nameof(faceDirection) + ": " + faceDirection + ", " + nameof(elTarget) + ": " + elTarget + ", " + nameof(elCurrent) + ": " + elCurrent);

				float firstElDelta, secondElDelta;
				CalcDelta(elCurrent, elTarget, out firstElDelta, out secondElDelta);

				if (m_claimedElevation)
				{
					// elevation has been claimed, check limits
					if (CalcFaceTowards_Claimed(ref bestResult, target, faceDirection, firstElDelta))
						Log.TraceLog("First elevation delta reachable: " + firstElDelta);
					else if (CalcFaceTowards_Claimed(ref bestResult, target, faceDirection, secondElDelta))
						Log.TraceLog("Second elevation delta reachable: " + secondElDelta);
					else
						Log.TraceLog("Neither elevation delta acceptable");
				}
				else if (CalcFaceTowards_NotClaimed(ref bestResult, target, faceDirection, firstElDelta)) // elevation has not been claimed, check that current elevation is close enough
					Log.TraceLog("Elevation within tolerance: " + firstElDelta);
				else
					Log.TraceLog("Elevation outside tolerance: " + firstElDelta);
			}

			if (bestResult.AccuracySquared != float.PositiveInfinity)
			{
				Log.TraceLog("Best: " + bestResult);
				return true;
			}

			Log.TraceLog("Cannot rotate to target");
			return false;
		}

		private bool CalcFaceTowards_Claimed(ref FaceResult bestResult, DirectionWorld target, DirectionWorld faceDirection, float elDelta)
		{
			float elAccuracy;
			if (WithinLimits(StatorEl, elDelta))
				elAccuracy = 0f;
			else
			{
				float clamped = ClampToLimits(StatorEl, elDelta);
				elAccuracy = Math.Abs(elDelta - clamped);
				if (elAccuracy > m_requiredAccuracyRadians)
					return false;
				elDelta = clamped;
			}

			float azDelta, azAccuracy;
			if (CalcAzimuth(target, faceDirection, elDelta, out azDelta, out azAccuracy))
			{
				float accSq = elAccuracy * elAccuracy + azAccuracy * azAccuracy;
				float sumDeltaMag = Math.Abs(elDelta) + Math.Abs(azDelta);
				Log.TraceLog("Best: " + bestResult + ", current: " + new FaceResult() { AccuracySquared = accSq, SumDeltaMag = sumDeltaMag, DeltaElevation = elDelta, DeltaAzimuth = azDelta });
				if (bestResult.ReplaceBy(accSq, sumDeltaMag))
				{
					bestResult.AccuracySquared = accSq;
					bestResult.SumDeltaMag = sumDeltaMag;
					bestResult.DeltaElevation = elDelta;
					bestResult.DeltaAzimuth = azDelta;
				}
				return true;
			}
			return false;
		}

		private bool CalcFaceTowards_NotClaimed(ref FaceResult bestResult, DirectionWorld target, DirectionWorld faceDirection, float elDelta)
		{
			if (Math.Abs(elDelta) > m_requiredAccuracyRadians)
				return false;

			float azDelta, azAccuracy;
			if (CalcAzimuth(target, faceDirection, elDelta, out azDelta, out azAccuracy))
			{
				float accSq = elDelta * elDelta + azAccuracy * azAccuracy;
				if (accSq > m_requiredAccuracyRadians * m_requiredAccuracyRadians)
					return false;
				float sumDeltaMag = Math.Abs(azDelta);
				Log.TraceLog("Best: " + bestResult + ", current: " + new FaceResult() { AccuracySquared = accSq, SumDeltaMag = sumDeltaMag, DeltaElevation = elDelta, DeltaAzimuth = azDelta });
				if (bestResult.ReplaceBy(accSq, sumDeltaMag))
				{
					bestResult.AccuracySquared = accSq;
					bestResult.SumDeltaMag = sumDeltaMag;
					bestResult.DeltaElevation = 0f;
					bestResult.DeltaAzimuth = azDelta;
				}
				return true;
			}
			return false;
		}

		private bool CalcAzimuth(DirectionWorld target, DirectionWorld faceDirection, float elDelta, out float azDelta, out float azAccuracy)
		{
			ApplyElevationChange(ref faceDirection, elDelta);
			Vector3 azCurrent = faceDirection.ToBlock(StatorAz);
			Vector3 azTarget = target.ToBlock(StatorAz);

			Log.TraceLog(nameof(target) + ": " + target + ", " + nameof(faceDirection) + ": " + faceDirection + ", " + nameof(azTarget) + ": " + azTarget + ", " + nameof(azCurrent) + ": " + azCurrent);

			float firstAzDelta, secondAzDelta;
			CalcDelta(azCurrent, azTarget, out firstAzDelta, out secondAzDelta);

			if (m_claimedAzimuth)
			{
				// azimuth has been claimed, check limits
				if (WithinLimits(StatorAz, firstAzDelta))
				{
					Log.TraceLog("First azimuth delta reachable: " + firstAzDelta);
					azDelta = firstAzDelta;
					azAccuracy = 0f;
					return true;
				}
				if (WithinLimits(StatorAz, secondAzDelta))
				{
					Log.TraceLog("Second azimuth delta reachable: " + secondAzDelta);
					azDelta = secondAzDelta;
					azAccuracy = 0f;
					return true;
				}
				BestEffort(StatorAz, firstAzDelta, secondAzDelta, out azDelta, out azAccuracy);
				if (azAccuracy < m_requiredAccuracyRadians)
				{
					Log.TraceLog("Best effort: " + azDelta);
					return true;
				}
			}
			else
			{
				// azimuth not claimed, check that the current azimuth is close enough
				azAccuracy = Math.Abs(firstAzDelta);
				if (azAccuracy < m_requiredAccuracyRadians)
				{
					Log.TraceLog("Azimuth within tolerance: " + firstAzDelta);
					azDelta = 0f; // not claimed, no rotation
					return true;
				}
			}

			Log.TraceLog("Neither azimuth delta acceptable");
			azDelta = float.NaN;
			azAccuracy = float.PositiveInfinity;
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
				lock (Static.claimedStators)
					m_claimedElevation = Static.claimedStators.Add(StatorEl);
				Log.DebugLog("claimed elevation stator: " + StatorEl.nameWithId(), Logger.severity.DEBUG, condition: m_claimedElevation);
			}
			if (!m_claimedAzimuth)
			{
				lock (Static.claimedStators)
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

		/// <exception cref="ArithmeticException">If angle is not a number.</exception>
		private void SetVelocity(MyMotorStator stator, float angle)
		{
			Log.DebugLog("Not server!", Logger.severity.FATAL, condition: !MyAPIGateway.Multiplayer.IsServer);

			float velocity = MathHelper.Clamp(angle * m_rotationSpeedMulitplier, -speedLimit, speedLimit);

			float currentVelocity = stator.TargetVelocity;
			if (Math.Sign(velocity) != Math.Sign(currentVelocity) || Math.Abs(velocity - currentVelocity) > 0.01f)
			{
				Log.TraceLog("Stator: " + stator.nameWithId() + ", Setting velocity. " + nameof(currentVelocity) + ": " + currentVelocity + ", target: " + velocity + ", angle: " + angle);
				stator.TargetVelocity.Value = velocity;
			}
			//else
			//	Log.TraceLog("Not changing velocity. " + nameof(currentVelocity) + ": " + currentVelocity + ", target: " + velocity + ", angle: " + angle);
		}
	}
}
