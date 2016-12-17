using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Data;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace Rynchodon.Autopilot.Aerodynamics
{
	/// <summary>
	/// Makes it easier to fly an aircraft by managing flight control rotors.
	/// </summary>
	class FlightControlAssist
	{

		private struct FlightControlStator
		{
			private static ITerminalProperty<float> VelocityProperty;

			private float m_previousTargetAngle;

			public IMyMotorStator Stator;
			public float Sensitivity, Trim;

			public FlightControlStator(IMyMotorStator Stator, float Sensitivity, float Trim)
			{
				this.m_previousTargetAngle = Trim;
				this.Stator = Stator;
				this.Sensitivity = Sensitivity;
				this.Trim = Trim;

				((ITerminalProperty<float>)Stator.GetProperty("LowerLimit")).SetValue(Stator, -45f);
				((ITerminalProperty<float>)Stator.GetProperty("UpperLimit")).SetValue(Stator, 45f);
			}

			public void Flip()
			{
				Sensitivity = -Sensitivity;
				Trim = -Trim;
			}

			public void SetTarget(float targetAcceleration)
			{
				float targetAngle = (targetAcceleration * Sensitivity + Trim) * 0.25f + m_previousTargetAngle * 0.75f;
				m_previousTargetAngle = targetAngle;
				if (VelocityProperty == null)
					VelocityProperty = (ITerminalProperty<float>)Stator.GetProperty("Velocity");
				float targetVelocity = (targetAngle - Stator.Angle) * 60f;
				float currentVelocity = Stator.Velocity;
				if (((targetVelocity == 0f) != (currentVelocity == 0f)) || Math.Abs(targetVelocity - currentVelocity) > 0.1f)
					VelocityProperty.SetValue(Stator, targetVelocity);
				Logger.TraceLog("target acceleration: " + targetAcceleration + ", target angle: " + targetAngle + ", stator angle: " + Stator.Angle + ", target velocity: " + targetVelocity + ", current velocity: " + currentVelocity,
					context: Stator.CubeGrid.nameWithId(), primaryState: Stator.nameWithId());
			}
		}

		private const byte StopAfter = 4;
		private readonly MyCockpit m_cockpit;
		private readonly Logger m_logger;
		private readonly PseudoBlock Pseudo;

		private FlightControlStator[] Aileron, Elevator, Rudder;
		private byte m_noElevatorInput, m_noRudderInput;

		private MyCubeGrid m_grid { get { return m_cockpit.CubeGrid; } }

		public FlightControlAssist(MyCockpit cockpit)
		{
			this.m_cockpit = cockpit;
			this.m_logger = new Logger(m_grid);

			CubeGridCache cache = CubeGridCache.GetFor(m_grid);

			Pseudo = new PseudoBlock(cockpit);

			Update.UpdateManager.Register(10, Update10, m_grid);
		}

		public void Disable()
		{
			Update.UpdateManager.Unregister(10, Update10);
		}

		public void SetAilerons(IEnumerable<IMyMotorStator> rotors, float sensitivity, float trim)
		{
			List<FlightControlStator> statorList = new List<FlightControlStator>();
			foreach (IMyMotorStator stator in rotors)
			{
				Vector3 facing = stator.LocalMatrix.Up;
				if (facing == Pseudo.LocalMatrix.Forward || facing == Pseudo.LocalMatrix.Backward)
				{
					m_logger.alwaysLog("Facing the wrong way: " + stator.nameWithId() + ", facing: " + facing + ", local flight matrix: " + Pseudo.LocalMatrix, Logger.severity.WARNING);
					continue;
				}

				FlightControlStator flightControl = new FlightControlStator(stator, sensitivity, trim);
				if (!stator.IsOnSide(facing))
				{
					m_logger.debugLog("On " + Base6Directions.GetDirection(-facing) + " side and facing " + Base6Directions.GetDirection(facing));
					flightControl.Flip();
				}

				statorList.Add(flightControl);
			}
			if (statorList.Count != 0)
				Aileron = statorList.ToArray();
			else
				Aileron = null;
		}

		public void SetElevators(IEnumerable<IMyMotorStator> rotors, float sensitivity, float trim)
		{
			List<FlightControlStator> statorList = new List<FlightControlStator>();
			foreach (IMyMotorStator stator in rotors)
			{
				Vector3 facing = stator.LocalMatrix.Up;
				bool isForward = stator.IsOnSide(Pseudo.LocalMatrix.Forward);
				m_logger.debugLog(stator.DisplayNameText + " is on " + (isForward ? "forward" : "backward") + " side");

				FlightControlStator flightControl = new FlightControlStator(stator, sensitivity, trim);
				if (facing == Pseudo.LocalMatrix.Left)
				{
					if (!isForward)
					{
						m_logger.debugLog("Aft and facing port: " + stator.DisplayNameText);
						flightControl.Flip();
					}
				}
				else if (facing == Pseudo.LocalMatrix.Right)
				{
					if (isForward)
					{
						m_logger.debugLog("Fore and facing starboard: " + stator.DisplayNameText);
						flightControl.Flip();
					}
				}
				else
				{
					m_logger.alwaysLog("Facing the wrong way: " + stator.nameWithId() + ", facing: " + facing + ", local flight matrix: " + Pseudo.LocalMatrix, Logger.severity.WARNING);
					continue;
				}

				statorList.Add(flightControl);
			}
			if (statorList.Count != 0)
				Elevator = statorList.ToArray();
			else
				Elevator = null;
		}

		public void SetRudders(IEnumerable<IMyMotorStator> rotors, float sensitivity, float trim)
		{
			List<FlightControlStator> statorList = new List<FlightControlStator>();
			foreach (IMyMotorStator stator in rotors)
			{
				Vector3 facing = stator.LocalMatrix.Up;
				bool isForward = stator.IsOnSide(Pseudo.LocalMatrix.Forward);
				m_logger.debugLog(stator.DisplayNameText + " is on " + (isForward ? "forward" : "backward") + " side");

				FlightControlStator flightControl = new FlightControlStator(stator, sensitivity, trim);
				if (facing == Pseudo.LocalMatrix.Up)
				{
					if (!isForward)
					{
						m_logger.debugLog("Aft and facing up: " + stator.DisplayNameText);
						flightControl.Flip();
					}
				}
				else if (facing == Pseudo.LocalMatrix.Down)
				{
					if (isForward)
					{
						m_logger.debugLog("Fore and facing down: " + stator.DisplayNameText);
						flightControl.Flip();
					}
				}
				else
				{
					m_logger.alwaysLog("Facing the wrong way: " + stator.nameWithId() + ", facing: " + facing + ", local flight matrix: " + Pseudo.LocalMatrix, Logger.severity.WARNING);
					continue;
				}

				statorList.Add(flightControl);
			}
			if (statorList.Count != 0)
				Rudder = statorList.ToArray();
			else
				Rudder = null;
		}

		private void Update10()
		{
			MyPlayer player = MySession.Static.LocalHumanPlayer;
			if (player == null)
				return;

			MyCubeBlock block = player.Controller.ControlledEntity as MyCubeBlock;
			if (block == null || block != m_cockpit)
				return;

			bool skipInput = MyGuiScreenGamePlay.ActiveGameplayScreen != null;

			Vector3D gridCentre = m_grid.GetCentre();
			MyPlanet closest = MyPlanetExtensions.GetClosestPlanet(gridCentre);
			if (closest.GetAirDensity(gridCentre) == 0f)
				return;
			Vector3 gravityDirection = closest.GetWorldGravityNormalized(ref gridCentre);

			if (Aileron != null)
				AileronHelper(ref gravityDirection, skipInput);
			if (Elevator != null)
				ElevatorHelper(skipInput);
			if (Rudder != null)
				RudderHelper(skipInput);
		}

		private void AileronHelper(ref Vector3 gravityDirection, bool skipInput)
		{
			float targetRollVelocity;
			if (skipInput)
				targetRollVelocity = 0f;
			else
			{
				if (MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROLL_RIGHT))
					targetRollVelocity = 1f;
				else if (MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROLL_LEFT))
					targetRollVelocity = -1f;
				else if (Vector3.Dot(gravityDirection, Pseudo.WorldMatrix.Down) > 0.5f)
				{
					float currentRoll = Vector3.Dot(gravityDirection, Pseudo.WorldMatrix.Left);
					if (-0.1f < currentRoll && currentRoll < 0.1f)
						targetRollVelocity = currentRoll * 20f;
					else
						targetRollVelocity = 0f;
				}
				else
					targetRollVelocity = 0f;
			}

			float currentRollVelocity = Vector3.Dot(m_grid.Physics.AngularVelocity, Pseudo.WorldMatrix.Forward);
			float acceleration = targetRollVelocity - currentRollVelocity;
			for (int index = 0; index < Aileron.Length; ++index)
				Aileron[index].SetTarget(acceleration);
		}

		private void ElevatorHelper(bool skipInput)
		{
			Vector3 angularVelocity = m_grid.Physics.AngularVelocity;
			float targetPitchVelocity = skipInput ? 0f : MyAPIGateway.Input.GetMouseYForGamePlay() * -0.1f;
			float currentPitchVelocity = Vector3.Dot(angularVelocity, Pseudo.WorldMatrix.Right);
			float acceleration = targetPitchVelocity - currentPitchVelocity;
			if (targetPitchVelocity == 0f)
			{
				if (m_noElevatorInput> StopAfter)
					acceleration *= 10f;
				else
					++m_noElevatorInput;
			}
			else
				m_noElevatorInput = 0;
			for (int index = 0; index < Elevator.Length; ++index)
				Elevator[index].SetTarget(acceleration);
		}

		private void RudderHelper(bool skipInput)
		{
			Vector3 angularVelocity = m_grid.Physics.AngularVelocity;
			float targetYawVelocity = skipInput ? 0f : MyAPIGateway.Input.GetMouseXForGamePlay() * 0.1f;
			float currentYawVelocity = Vector3.Dot(angularVelocity, Pseudo.WorldMatrix.Down);
			float acceleration = targetYawVelocity - currentYawVelocity;
			if (targetYawVelocity == 0f)
			{
				if (m_noRudderInput > StopAfter)
					acceleration *= 10f;
				else
					++m_noRudderInput;
			}
			else
				m_noRudderInput = 0;
			for (int index = 0; index < Rudder.Length; ++index)
				Rudder[index].SetTarget(acceleration);
		}

	}
}
