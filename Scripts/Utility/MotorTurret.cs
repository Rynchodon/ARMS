#if DEBUG
//#define TRACE
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
		public delegate void StatorChangeHandler(IMyMotorStator statorEl, IMyMotorStator statorAz);
		private const float speedLimit = MathHelper.Pi;

		private static readonly MyObjectBuilderType[] types_Rotor = new MyObjectBuilderType[] { typeof(MyObjectBuilder_MotorRotor), typeof(MyObjectBuilder_MotorAdvancedRotor) };

		#region Stator Claiming

		private static readonly Dictionary<IMyMotorStator, List<MotorTurret>> claimedStator = new Dictionary<IMyMotorStator, List<MotorTurret>>();

		private static int GetMiddle(List<MotorTurret> users)
		{
			Vector3D middle = new Vector3D();
			int count = 0;
			for (int index = users.Count - 1; index >= 0; --index)
			{
				middle += users[index].FaceBlock.GetPosition();
				++count;
			}
			middle /= count;

			double closeSq = double.PositiveInfinity;
			int closest = 0;
			for (int index = users.Count - 1; index >= 0; --index)
			{
				double distSq = Vector3D.DistanceSquared(users[index].FaceBlock.GetPosition(), middle);
				Logger.DebugLog("user: " + users[index].FaceBlock.nameWithId() + ", distSq: " + distSq + ", closestSq: " + closeSq);
				if (distSq < closeSq)
				{
					closeSq = distSq;
					closest = index;
				}
			}

			return closest;
		}

		private static void UpdateClaim(IMyMotorStator stator)
		{
			List<MotorTurret> users = claimedStator[stator];

			MotorTurret currentClaimant = users[0];
			if (currentClaimant.StatorEl == stator)
			{
				if (!currentClaimant.m_claimedElevation)
					currentClaimant = null;
			}
			else if (currentClaimant.StatorAz == stator)
			{
				if (!currentClaimant.m_claimedAzimuth)
					currentClaimant = null;
			}
			else
				throw new Exception(currentClaimant.FaceBlock.nameWithId() + " does not have " + stator.nameWithId());

			int middle = GetMiddle(users);
			MotorTurret middleClaimant = users[middle];
			if (currentClaimant == middleClaimant)
			{
				Logger.DebugLog("Claim for " + stator.nameWithId() + " is up-to-date. Claimed by " + currentClaimant.FaceBlock.nameWithId());
				return;
			}

			Logger.DebugLog("claim of " + stator.nameWithId() + " changed from " + currentClaimant?.FaceBlock.nameWithId() + " to " + middleClaimant.FaceBlock.nameWithId());
			if (middle != 0)
				users.Swap(0, middle);

			if (currentClaimant != null)
				SetClaim(stator, currentClaimant, false);

			SetClaim(stator, middleClaimant, true);
		}

		private static void SetClaim(IMyMotorStator stator, MotorTurret turret, bool claimed)
		{
			Logger.DebugLog(nameof(stator) + ": " + stator.nameWithId() + ", " + nameof(turret) + ": " + turret.FaceBlock.nameWithId() + ", " + nameof(claimed) + ": " + claimed);
			if (turret.StatorEl == stator)
				turret.m_claimedElevation = claimed;
			else if (turret.StatorAz == stator)
				turret.m_claimedAzimuth = claimed;
			else
				throw new Exception(turret.FaceBlock.nameWithId() + " does not have " + stator.nameWithId());
		}

		private static void RegisterClaim(IMyMotorStator stator, MotorTurret turret)
		{
			lock (claimedStator)
			{
				List<MotorTurret> users;
				if (claimedStator.TryGetValue(stator, out users))
				{
					Debug.Assert(!users.Contains(turret), turret.FaceBlock.nameWithId() + " is already in users list");
					Logger.DebugLog("registered claim: " + stator.nameWithId() + "/" + turret.FaceBlock.nameWithId());
					users.Add(turret);
					UpdateClaim(stator);
				}
				else
				{
					Logger.DebugLog("first claim: " + stator.nameWithId() + "/" + turret.FaceBlock.nameWithId());
					users = new List<MotorTurret>();
					claimedStator.Add(stator, users);
					users.Add(turret);
					SetClaim(stator, turret, true);
				}
			}
		}

		private static void RescindClaim(IMyMotorStator stator, MotorTurret turret)
		{
			lock (claimedStator)
			{
				List<MotorTurret> users;
				if (claimedStator.TryGetValue(stator, out users))
				{
					int index = users.IndexOf(turret);
					if (index < 0)
					{
						Logger.DebugLog("Cannot remove claim on " + stator.nameWithId() + " by " + turret.FaceBlock.nameWithId() + ", not in users list");
						return;
					}
					SetClaim(stator, turret, false);
					if (users.Count == 1)
					{
						Logger.DebugLog("removing only claim: " + stator.nameWithId() + "/" + turret.FaceBlock.nameWithId());
						claimedStator.Remove(stator);
						return;
					}
					users.RemoveAtFast(index);
					UpdateClaim(stator);
				}
				else
				{
					Logger.DebugLog("Cannot remove claim on " + stator.nameWithId() + " by " + turret.FaceBlock.nameWithId() + ", no users list");
					return;
				}
			}
		}

		#endregion

		#region Static Calc Face

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

		#endregion

		private static bool StatorOk(IMyMotorStator stator)
		{
			return stator != null && !stator.Closed && stator.IsAttached;
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
				if (value_statorEl != null)
					RescindClaim(value_statorEl, this);
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
				if (value_statorAz != null)
					RescindClaim(value_statorAz, this);
				value_statorAz = value;
			}
		}

		private Logable Log { get { return new Logable(FaceBlock); } }

		public MotorTurret(IMyCubeBlock block, StatorChangeHandler handler = null, int updateFrequency = 100, float requiredAccuracyRadians = float.PositiveInfinity)
		{
			this.FaceBlock = block;
			this.OnStatorChange = handler;

			m_rotationSpeedMulitplier = 30f / Math.Max(updateFrequency, 6);
			m_requiredAccuracyRadians = requiredAccuracyRadians;

			FaceBlock.OnClose += (x) => Dispose();
			FaceBlock.IsWorkingChanged += FaceBlock_IsWorkingChanged;
			if (FaceBlock.IsWorking)
				FaceBlock_IsWorkingChanged(FaceBlock);
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

		private void FaceBlock_IsWorkingChanged(IMyCubeBlock obj)
		{
			if (obj.IsWorking)
			{
				SetupStators();
				ClaimStators();
			}
			else
			{
				Stop();
				StatorEl = null;
				StatorAz = null;
			}
		}

		#region Calc Face

		/// <summary>
		/// Try to face in the target direction. Must execute on game thread.
		/// </summary>
		public void FaceTowards(DirectionWorld target)
		{
			Log.DebugLog("Not server!", Logger.severity.FATAL, condition: !MyAPIGateway.Multiplayer.IsServer);
			Debug.Assert(Threading.ThreadTracker.IsGameThread, "not game thread");

			if (!m_claimedElevation && !m_claimedAzimuth)
				// nothing to do
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
		/// Determine if the motor turret can face the target direction. Thread-safe.
		/// </summary>
		/// <param name="target">The direction to face.</param>
		/// <returns>True iff the turret can face the target direction.</returns>
		public bool CanFaceTowards(DirectionWorld target)
		{
			if (!StatorOk(StatorEl))
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
				const float accuracyMulti = 100f;

				float myScore = accuracyMulti * AccuracySquared + SumDeltaMag * SumDeltaMag;
				float otherScore = accuracyMulti * accSq + sumDelta * sumDelta;

				return otherScore < myScore;
			}

			public override string ToString()
			{
				return nameof(AccuracySquared) + ": " + AccuracySquared + ", " + 
					nameof(SumDeltaMag) + ": " + SumDeltaMag + ", " + 
					nameof(DeltaElevation) + ": " + DeltaElevation + ", " + 
					nameof(DeltaAzimuth) + ": " + DeltaAzimuth;
			}
		}

		private bool CalcFaceTowards(DirectionWorld targetWorldSpace, out FaceResult bestResult)
		{
			if (StatorOk(StatorAz))
				return CalcFaceTowards_AzimuthOk(targetWorldSpace, out bestResult);
			else
				return CalcFaceTowards_NoAzimuth(targetWorldSpace, out bestResult);
		}

		private bool CalcFaceTowards_AzimuthOk(DirectionWorld targetWorldSpace, out FaceResult bestResult)
		{
			bestResult = FaceResult.Default;
			Vector3 target = targetWorldSpace.ToBlock(StatorAz);

			foreach (var direction in FaceBlock.FaceDirections())
			{
				DirectionWorld faceDirection = new DirectionWorld() { vector = FaceBlock.WorldMatrix.GetDirectionVector(direction) };
				Vector3 current = faceDirection.ToBlock(StatorAz);

				float firstDelta, secondDelta;
				CalcDelta(current, target, out firstDelta, out secondDelta);
				if (m_claimedAzimuth)
				{
					// azimuth has been claimed, check limits
					if (CalcFaceTowards_Claimed(ref bestResult, targetWorldSpace, faceDirection, firstDelta))
						Log.TraceLog("First azimuth delta is reachable: " + firstDelta);
					else if (CalcFaceTowards_Claimed(ref bestResult, targetWorldSpace, faceDirection, secondDelta))
						Log.TraceLog("Second azimuth delta is reachable: " + secondDelta);

					if (bestResult.AccuracySquared > 0.1f)
					{
						// try flipped
						float clamped = ClampToLimits(StatorAz, firstDelta);
						if (clamped > 0f)
						{
							firstDelta = clamped - MathHelper.Pi;
							secondDelta = clamped + MathHelper.Pi;
						}
						else
						{
							firstDelta = clamped + MathHelper.Pi;
							secondDelta = clamped - MathHelper.Pi;
						}

						if (CalcFaceTowards_Claimed(ref bestResult, targetWorldSpace, faceDirection, firstDelta))
							Log.TraceLog("First flipped azimuth delta is reachable: " + firstDelta);
						else if (CalcFaceTowards_Claimed(ref bestResult, targetWorldSpace, faceDirection, secondDelta))
							Log.TraceLog("Second flipped azimuth delta is reachable: " + secondDelta);
					}
				}
				else if (CalcFaceTowards_NotClaimed(ref bestResult, targetWorldSpace, faceDirection, firstDelta)) // azimuth has not been claimed, check that current azimuth is close enough
					Log.TraceLog("Azimuth is within tolerance: " + firstDelta);
				else
					Log.TraceLog("Azimuth is outside tolerance: " + firstDelta);
			}

			if (bestResult.AccuracySquared != float.PositiveInfinity)
			{
				Log.TraceLog("Best: " + bestResult);
				return true;
			}

			Log.TraceLog("Cannot rotate to target");
			return false;
		}

		private bool CalcFaceTowards_NoAzimuth(DirectionWorld targetWorldSpace, out FaceResult bestResult)
		{
			bestResult = FaceResult.Default;

			foreach (var direction in FaceBlock.FaceDirections())
			{
				DirectionWorld faceDirection = new DirectionWorld() { vector = FaceBlock.WorldMatrix.GetDirectionVector(direction) };
				CalcFaceTowards_NotClaimed(ref bestResult, targetWorldSpace, faceDirection, MathHelper.TwoPi);
			}

			if (bestResult.AccuracySquared != float.PositiveInfinity)
			{
				Log.TraceLog("Best: " + bestResult);
				return true;
			}

			Log.TraceLog("Cannot rotate to target");
			return false;
		}

		private bool CalcFaceTowards_Claimed(ref FaceResult bestResult, DirectionWorld target, DirectionWorld faceDirection, float azimuthDelta)
		{
			float azimuthAccuracy;
			if (WithinLimits(StatorAz, azimuthDelta))
				azimuthAccuracy = 0f;
			else
			{
				float clamped = ClampToLimits(StatorAz, azimuthDelta);
				azimuthAccuracy = Math.Abs(azimuthDelta - clamped);
				if (azimuthAccuracy > m_requiredAccuracyRadians)
					return false;
				azimuthDelta = clamped;
			}

			float elevationDelta, elevationAccuracy;
			if (CalcElevation(target, faceDirection, azimuthDelta, out elevationDelta, out elevationAccuracy))
			{
				float accSq = azimuthAccuracy * azimuthAccuracy + elevationAccuracy * elevationAccuracy;
				float sumDeltaMag = Math.Abs(azimuthDelta) + Math.Abs(elevationDelta);
				Log.TraceLog("Best: " + bestResult + ", current: " + new FaceResult() { AccuracySquared = accSq, SumDeltaMag = sumDeltaMag, DeltaElevation = elevationDelta, DeltaAzimuth = azimuthDelta });
				if (bestResult.ReplaceBy(accSq, sumDeltaMag))
				{
					bestResult.AccuracySquared = accSq;
					bestResult.SumDeltaMag = sumDeltaMag;
					bestResult.DeltaAzimuth = azimuthDelta;
					bestResult.DeltaElevation = elevationDelta;
				}
				return true;
			}
			return false;
		}

		private bool CalcFaceTowards_NotClaimed(ref FaceResult bestResult, DirectionWorld target, DirectionWorld faceDirection, float azimuthDelta)
		{
			if (Math.Abs(azimuthDelta) > m_requiredAccuracyRadians)
				return false;

			float elevationDelta, elevationAccuracy;
			if (CalcElevation(target, faceDirection, azimuthDelta, out elevationDelta, out elevationAccuracy))
			{
				float accSq = azimuthDelta * azimuthDelta + elevationAccuracy * elevationAccuracy;
				if (accSq > m_requiredAccuracyRadians * m_requiredAccuracyRadians)
					return false;
				float sumDeltaMag = Math.Abs(elevationDelta);
				Log.TraceLog("Best: " + bestResult + ", current: " + new FaceResult() { AccuracySquared = accSq, SumDeltaMag = sumDeltaMag, DeltaElevation = elevationDelta, DeltaAzimuth = 0f });
				if (bestResult.ReplaceBy(accSq, sumDeltaMag))
				{
					bestResult.AccuracySquared = accSq;
					bestResult.SumDeltaMag = sumDeltaMag;
					bestResult.DeltaAzimuth = 0f;
					bestResult.DeltaElevation = elevationDelta;
				}
				return true;
			}
			return false;
		}

		private bool CalcElevation(DirectionWorld targetWorldSpace, DirectionWorld faceDirection, float azimuthDelta, out float elevationDelta, out float elevationAccuracy)
		{
			if (StatorOk(StatorAz) && m_claimedAzimuth)
				ApplyAzimuthChange(ref faceDirection, azimuthDelta);
			Vector3 current = faceDirection.ToBlock(StatorEl);
			Vector3 target = targetWorldSpace.ToBlock(StatorEl);

			Log.TraceLog(nameof(targetWorldSpace) + ": " + targetWorldSpace + ", " + nameof(faceDirection) + ": " + faceDirection + ", " + nameof(target) + ": " + target + ", " + nameof(current) + ": " + current);

			float firstDelta, secondDelta;
			CalcDelta(current, target, out firstDelta, out secondDelta);

			if (m_claimedElevation)
			{
				// elevation has been claimed, check limits
				if (WithinLimits(StatorEl, firstDelta))
				{
					Log.TraceLog("First elevation delta is reachable: " + firstDelta);
					elevationDelta = firstDelta;
					elevationAccuracy = 0f;
					return true;
				}
				if (WithinLimits(StatorEl, secondDelta))
				{
					Log.TraceLog("Second elevation delta is reachable: " + secondDelta);
					elevationDelta = secondDelta;
					elevationAccuracy = 0f;
					return true;
				}
				BestEffort(StatorEl, firstDelta, secondDelta, out elevationDelta, out elevationAccuracy);
				if (elevationAccuracy < m_requiredAccuracyRadians)
				{
					Log.TraceLog("Best effort: " + elevationDelta);
					return true;
				}
			}
			else
			{
				// elevation not claimed, check that the current elevation is close enough
				elevationAccuracy = Math.Abs(firstDelta);
				if (elevationAccuracy < m_requiredAccuracyRadians)
				{
					Log.TraceLog("Elevation within tolerance: " + firstDelta);
					elevationDelta = 0f; // not claimed, no rotation
					return true;
				}
			}

			Log.TraceLog("Neither elevation delta acceptable");
			elevationDelta = float.NaN;
			elevationAccuracy = float.PositiveInfinity;
			return false;
		}

		/// <summary>
		/// Rotate current direction by azimuth stator's delta.
		/// </summary>
		/// <param name="facing">The current direction.</param>
		/// <param name="azDelta">The change in angle of azimuth stator.</param>
		private void ApplyAzimuthChange(ref DirectionWorld facing, float azDelta)
		{
			Log.TraceLog(nameof(facing) + " is " + facing);
			Vector3 axis = StatorAz.WorldMatrix.Down;
			Log.TraceLog(nameof(axis) + " is " + axis);
			Log.TraceLog(nameof(azDelta) + " is " + azDelta);
			Quaternion rotation; Quaternion.CreateFromAxisAngle(ref axis, azDelta, out rotation);
			Vector3.Transform(ref facing.vector, ref rotation, out facing.vector);
			Log.TraceLog(nameof(facing) + " is " + facing);
		}

		#endregion

		#region Setup

		/// <summary>
		/// One-time setup for stators.
		/// </summary>
		private void SetupStators()
		{
			// get StatorEl from FaceBlock's grid
			IMyMotorStator tempStator;
			if (!GetStatorRotor(FaceBlock.CubeGrid, out tempStator))
			{
				Log.DebugLog("Failed to get StatorEl");
				StatorEl = null;
				OnStatorChange?.Invoke(StatorEl, StatorAz);
				return;
			}
			StatorEl = tempStator;

			if (!GetStatorRotor(StatorEl.CubeGrid, out tempStator, StatorEl))
			{
				Log.DebugLog("Failed to get StatorAz");
				StatorAz = null;
				OnStatorChange?.Invoke(StatorEl, StatorAz);
				return;
			}
			StatorAz = tempStator;

			Log.DebugLog("Successfully got stators. Elevation = " + StatorEl.nameWithId() + ", Azimuth = " + StatorAz.nameWithId());
			OnStatorChange?.Invoke(StatorEl, StatorAz);
			Stop();
			return;
		}

		/// <summary>
		/// One-time registration of stator claims.
		/// </summary>
		private void ClaimStators()
		{
			if (StatorOk(StatorEl))
				RegisterClaim(StatorEl, this);
			else if (StatorEl != null)
				StatorEl = null;

			if (StatorOk(StatorAz))
				RegisterClaim(StatorAz, this);
			else if (StatorAz != null)
				StatorAz = null;
		}

		private static bool ArePerpendicular(IMyMotorStator statorOne, IMyMotorStator statorTwo)
		{
			Logger.TraceLog(nameof(statorOne) + ": " + statorOne.nameWithId() + ", Up: " + statorOne.WorldMatrix.Up + ", " + nameof(statorTwo) + ": " + statorTwo.nameWithId() + ", Up: " + statorTwo.WorldMatrix.Up + ", dot: " + statorOne.WorldMatrix.Up.Dot(statorTwo.WorldMatrix.Up));
			return Math.Abs(statorOne.WorldMatrix.Up.Dot(statorTwo.WorldMatrix.Up)) < 0.1f;
		}

		private bool GetStatorRotor(IMyCubeGrid grid, out IMyMotorStator Stator, IMyMotorStator IgnoreStator = null)
		{
			CubeGridCache cache = CubeGridCache.GetFor(grid);

			// first stator that are found that are not working
			// returned if a working set is not found
			IMyMotorStator offStator = null;

			foreach (MyObjectBuilderType type in types_Rotor)
				foreach (IMyMotorRotor rotor in cache.BlocksOfType(type))
				{
					if (!FaceBlock.canControlBlock(rotor))
					{
						Log.DebugLog("Cannot control: " + rotor.nameWithId());
						continue;
					}

					Stator = (IMyMotorStator)rotor.Base;
					if (Stator == null || Stator == IgnoreStator)
						continue;

					if (IgnoreStator != null && !ArePerpendicular(IgnoreStator, Stator))
					{
						Log.DebugLog("Not perpendicular: " + IgnoreStator.nameWithId() + " & " + Stator.nameWithId());
						continue;
					}

					if (Stator.IsWorking)
						return true;
					else if (offStator == null)
						offStator = Stator;
				}

			if (offStator != null)
			{
				Stator = offStator;
				return true;
			}

			Stator = null;
			return false;
		}

		#endregion

		/// <summary>
		/// Must execute on game thread.
		/// </summary>
		public void Stop()
		{
			Log.DebugLog("Not server!", Logger.severity.FATAL, condition: !MyAPIGateway.Multiplayer.IsServer);

			if (m_claimedElevation)
				SetVelocity(StatorEl, 0);
			if (m_claimedAzimuth)
				SetVelocity(StatorAz, 0);
		}

		private void SetVelocity(IMyMotorStator stator, float angle)
		{
			SetVelocity((MyMotorStator)stator, angle);
		}

		/// <exception cref="ArithmeticException">If angle is not a number.</exception>
		private void SetVelocity(MyMotorStator stator, float angle)
		{
			Log.DebugLog("Not server!", Logger.severity.FATAL, condition: !MyAPIGateway.Multiplayer.IsServer);

			if (!StatorOk(stator))
				return;

			if (!stator.Enabled)
				stator.Enabled = true;

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
