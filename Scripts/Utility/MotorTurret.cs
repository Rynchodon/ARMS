using System;
using System.Collections.Generic;
using Rynchodon.Attached;
using Rynchodon.Utility.Vectors;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

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
			public ITerminalProperty<float> statorVelocity;
		}

		private static StaticVariables Static = new StaticVariables();
		public delegate void StatorChangeHandler(IMyMotorStator statorEl, IMyMotorStator statorAz);
		private const float def_RotationSpeedMultiplier = 100;

		static MotorTurret()
		{
			MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
		}

		private static void Entities_OnCloseAll()
		{
			MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
			Static = null;
		}

		private readonly IMyCubeBlock FaceBlock;
		private readonly Logger myLogger;
		private readonly StatorChangeHandler OnStatorChange;

		private float my_RotationSpeedMulitplier = def_RotationSpeedMultiplier;
		private float mySpeedLimit = 30f;

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
						myLogger.debugLog("Released claim on elevation stator: " + value_statorEl.nameWithId(), Logger.severity.DEBUG);
					else
						myLogger.alwaysLog("Failed to remove claim on elevation stator: " + value_statorEl.nameWithId(), Logger.severity.ERROR);
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
						myLogger.debugLog("Released claim on azimuth stator: " + value_statorAz.nameWithId(), Logger.severity.DEBUG);
					else
						myLogger.alwaysLog("Failed to remove claim on azimuth stator: " + value_statorAz.nameWithId(), Logger.severity.ERROR);
				}
				value_statorAz = value;
			}
		}

		public MotorTurret(IMyCubeBlock block, StatorChangeHandler handler = null)
		{
			this.FaceBlock = block;
			this.myLogger = new Logger("MotorTurret", block);
			this.OnStatorChange = handler;
			this.SetupStators();
		}

		~MotorTurret()
		{
			Dispose();
		}

		public void Dispose()
		{
			if (Static != null)
			{
				Stop();
				StatorEl = null;
				StatorAz = null;
			}
		}

		public float RotationSpeedMultiplier
		{
			get { return my_RotationSpeedMulitplier; }
			set { my_RotationSpeedMulitplier = Math.Abs(value); }
		}

		public float SpeedLimit
		{
			get { return mySpeedLimit; }
			set { mySpeedLimit = Math.Abs(value); }
		}

		public void FaceTowards(DirectionWorld target)
		{
			myLogger.debugLog(!MyAPIGateway.Multiplayer.IsServer, "Not server!", Logger.severity.FATAL);

			if (!SetupStators())
				return;

			DirectionBlock blockTarget = target.ToBlock((IMyCubeBlock)StatorAz);
			DirectionBlock blockCurrent = new DirectionWorld() { vector = FaceBlock.WorldMatrix.GetDirectionVector(FaceBlock.GetFaceDirection(target)) }.ToBlock((IMyCubeBlock)StatorAz);

			//myLogger.debugLog("block target: " + blockTarget.vector + ", block current: " + blockCurrent.vector);

			float targetElevation, targetAzimuth, currentElevation, currentAzimuth;
			GetElevationAndAzimuth(blockTarget.vector, out targetElevation, out targetAzimuth);
			GetElevationAndAzimuth(blockCurrent.vector, out currentElevation, out currentAzimuth);

			// essentially we have done a pseudo calculation of target/current elevation/azimuth but they are either correct or "opposite" of the actual values
			// we can determine which with a simple calculation

			PositionBlock faceBlockPosition = new PositionWorld() { vector = FaceBlock.GetPosition() }.ToBlock((IMyCubeBlock)StatorAz);
			if (blockTarget.vector.Z * faceBlockPosition.vector.X - blockTarget.vector.X * faceBlockPosition.vector.Z > 0f) // Y component of cross product > 0
			{
				//myLogger.debugLog("alternate side, target elevation: " + targetElevation + " => " + (MathHelper.Pi - targetElevation));
				targetElevation = MathHelper.Pi - targetElevation;

				//myLogger.debugLog("alternate side, target azimuth: " + targetAzimuth + " => " + (targetAzimuth + MathHelper.Pi));
				targetAzimuth += MathHelper.Pi;
			}

			float deltaAzimuth = MathHelper.WrapAngle(currentAzimuth - targetAzimuth);
			if (Math.Abs(deltaAzimuth) > MathHelper.PiOver2)
			{
				//myLogger.debugLog("flipping current elevation: " + currentElevation + " => " + (MathHelper.Pi - currentElevation));

				currentElevation = MathHelper.Pi - currentElevation;

				//myLogger.debugLog("flipping delta azimuth: " + deltaAzimuth + " => " + MathHelper.WrapAngle(deltaAzimuth + MathHelper.Pi));
				deltaAzimuth = MathHelper.WrapAngle(deltaAzimuth + MathHelper.Pi);
			}

			if (m_claimedElevation)
			{
				float deltaElevation = MathHelper.WrapAngle(currentElevation - targetElevation);
				//myLogger.debugLog("target elevation: " + targetElevation + ", current: " + currentElevation + ", rotor: " + MathHelper.WrapAngle(StatorEl.Angle) + ", delta: " + deltaElevation);
				SetVelocity(StatorEl, deltaElevation);
			}
			if (m_claimedAzimuth)
			{
				//myLogger.debugLog("target azimuth: " + targetAzimuth + ", current: " + currentAzimuth + ", rotor: " + MathHelper.WrapAngle(StatorAz.Angle) + ", delta: " + deltaAzimuth);
				SetVelocity(StatorAz, deltaAzimuth);
			}
		}

		private void GetElevationAndAzimuth(Vector3 vector, out float elevation, out float azimuth)
		{
			elevation = (float)Math.Asin(vector.Y);

			// azimuth is arbitrary when elevation is +/- 1
			if (Math.Abs(vector.Y) > 0.9999f)
				azimuth = float.NaN;
			else
				azimuth = (float)Math.Atan2(vector.X, vector.Z);
		}

		public void Stop()
		{
			myLogger.debugLog(!MyAPIGateway.Multiplayer.IsServer, "Not server!", Logger.severity.FATAL);

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
				myLogger.debugLog(m_claimedElevation, "claimed elevation stator: " + StatorEl.nameWithId(), Logger.severity.DEBUG);
			}
			if (!m_claimedAzimuth)
			{
				m_claimedAzimuth = Static.claimedStators.Add(StatorAz);
				myLogger.debugLog(m_claimedAzimuth, "claimed azimuth stator: " + StatorAz.nameWithId(), Logger.severity.DEBUG);
			}
			return m_claimedElevation || m_claimedAzimuth;
		}

		private bool SetupStators()
		{
			if (StatorOK())
			{
				//myLogger.debugLog("Stators are already set up", "SetupStators()");
				return ClaimStators();
			}

			// get StatorEl from FaceBlock's grid
			IMyMotorStator tempStator;
			IMyCubeBlock RotorEl;
			if (!GetStatorRotor(FaceBlock.CubeGrid, out tempStator, out RotorEl))
			{
				myLogger.debugLog("Failed to get StatorEl");
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

			IMyCubeBlock RotorAz;
			if (!GetStatorRotor(getBFrom, out tempStator, out RotorAz, StatorEl, RotorEl))
			{
				myLogger.debugLog("Failed to get StatorAz");
				StatorAz = null;
				if (OnStatorChange != null)
					OnStatorChange(StatorEl, StatorAz);
				return false;
			}
			StatorAz = tempStator;

			myLogger.debugLog("Successfully got stators. Elevation = " + StatorEl.DisplayNameText + ", Azimuth = " + StatorAz.DisplayNameText);
			if (OnStatorChange != null)
				OnStatorChange(StatorEl, StatorAz);
			Stop();
			return ClaimStators();
		}

		private bool GetStatorRotor(IMyCubeGrid grid, out IMyMotorStator Stator, out IMyCubeBlock Rotor, IMyMotorStator IgnoreStator = null, IMyCubeBlock IgnoreRotor = null)
		{
			CubeGridCache cache = CubeGridCache.GetFor(grid);

			foreach (MyObjectBuilderType type in Static.types_Rotor)
			{
				var blocksOfType = cache.GetBlocksOfType(type);
				if (blocksOfType == null || blocksOfType.Count == 0)
					continue;

				foreach (IMyCubeBlock motorPart in blocksOfType)
				{
					if (!FaceBlock.canControlBlock(motorPart))
						continue;

					Stator = motorPart as IMyMotorStator;
					if (Stator != null)
					{
						if (Stator == IgnoreStator)
							continue;
						if (StatorRotor.TryGetRotor(Stator, out Rotor) && FaceBlock.canControlBlock(Stator as IMyCubeBlock))
							return true;
					}
					else
					{
						Rotor = motorPart;
						if (Rotor == IgnoreRotor)
							continue;
						if (StatorRotor.TryGetStator(Rotor, out Stator) && FaceBlock.canControlBlock(Stator as IMyCubeBlock))
							return true;
					}
				}
			}

			Stator = null;
			Rotor = null;
			return false;
		}

		private void SetVelocity(IMyMotorStator Stator, float angle)
		{
			myLogger.debugLog(!MyAPIGateway.Multiplayer.IsServer, "Not server!", Logger.severity.FATAL);

			// keep in mind, azimuth is undefined if elevation is straight up or straight down
			float speed = angle.IsValid() ? MathHelper.Clamp(angle * RotationSpeedMultiplier, -mySpeedLimit, mySpeedLimit) : 0f;

			if (Static.statorVelocity == null)
				Static.statorVelocity = Stator.GetProperty("Velocity") as ITerminalProperty<float>;
			float currentSpeed = Static.statorVelocity.GetValue(Stator);
			if ((speed == 0f && currentSpeed != 0f) || Math.Abs(speed - currentSpeed) > 0.1f)
				Static.statorVelocity.SetValue(Stator, speed);
		}
	}
}
