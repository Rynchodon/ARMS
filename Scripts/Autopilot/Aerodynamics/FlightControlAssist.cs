using System;
using System.Collections.Generic;
using System.Linq;
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
			public bool Flipped;

			public FlightControlStator(IMyMotorStator Stator)
			{
				this.m_previousTargetAngle = 0f;
				this.Stator = Stator;
				this.Flipped = false;


				((ITerminalProperty<float>)Stator.GetProperty("LowerLimit")).SetValue(Stator, -45f);
				((ITerminalProperty<float>)Stator.GetProperty("UpperLimit")).SetValue(Stator, 45f);
			}

			public void Flip()
			{
				this.Flipped = !Flipped;
			}

			public void SetTarget(float targetAngle)
			{
				targetAngle = (targetAngle) * 0.2f + m_previousTargetAngle * 0.8f; // smooth transition
				m_previousTargetAngle = targetAngle;
				if (Flipped)
					targetAngle = -targetAngle;
				if (VelocityProperty == null)
					VelocityProperty = (ITerminalProperty<float>)Stator.GetProperty("Velocity");
				float targetVelocity = (targetAngle - Stator.Angle) * 60f;
				float currentVelocity = Stator.Velocity;
				if (((targetVelocity == 0f) != (currentVelocity == 0f)) || Math.Abs(targetVelocity - currentVelocity) > 0.1f)
					VelocityProperty.SetValue(Stator, targetVelocity);
				Logger.TraceLog("target acceleration: " + targetAngle + ", target angle: " + targetAngle + ", stator angle: " + Stator.Angle + ", target velocity: " + targetVelocity + ", current velocity: " + currentVelocity,
					context: Stator.CubeGrid.nameWithId(), primaryState: Stator.nameWithId());
			}
		}

		private const byte StopAfter = 4;
		private readonly MyCockpit m_cockpit;
		private readonly Logger m_logger;
		private readonly PseudoBlock Pseudo;

		private FlightControlStator[] m_aileron, m_elevator, m_rudder;
		/// <summary>How sensitive the ship will be to input. pitch, yaw, roll.</summary>
		private Vector3 m_controlSensitivity = new Vector3(1f, 1f, 1f);
		/// <summary>Default position for rotors. pitch, yaw, roll.</summary>
		private Vector3 m_trim;

		private MyCubeGrid m_grid { get { return m_cockpit.CubeGrid; } }

		public FlightControlAssist(MyCockpit cockpit)
		{
			this.m_cockpit = cockpit;
			this.m_logger = new Logger(m_grid);
			this.m_aileron = m_elevator = m_rudder = new FlightControlStator[0];

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
			m_controlSensitivity.Z = sensitivity;
			m_trim.Z = trim;

			List<FlightControlStator> statorList = new List<FlightControlStator>();
			foreach (IMyMotorStator stator in rotors)
			{
				Vector3 facing = stator.LocalMatrix.Up;
				if (facing == Pseudo.LocalMatrix.Forward || facing == Pseudo.LocalMatrix.Backward)
				{
					m_logger.alwaysLog("Facing the wrong way: " + stator.nameWithId() + ", facing: " + facing + ", local flight matrix: " + Pseudo.LocalMatrix, Logger.severity.WARNING);
					continue;
				}

				FlightControlStator flightControl = new FlightControlStator(stator);
				if (!stator.IsOnSide(facing))
				{
					m_logger.debugLog("On " + Base6Directions.GetDirection(-facing) + " side and facing " + Base6Directions.GetDirection(facing));
					flightControl.Flip();
				}

				statorList.Add(flightControl);
			}
			m_aileron = statorList.ToArray();
		}

		public void SetElevators(IEnumerable<IMyMotorStator> rotors, float sensitivity, float trim)
		{
			m_controlSensitivity.X = sensitivity;
			m_trim.X = trim;

			List<FlightControlStator> statorList = new List<FlightControlStator>();
			foreach (IMyMotorStator stator in rotors)
			{
				Vector3 facing = stator.LocalMatrix.Up;
				bool isForward = stator.IsOnSide(Pseudo.LocalMatrix.Forward);
				m_logger.debugLog(stator.DisplayNameText + " is on " + (isForward ? "forward" : "backward") + " side");

				FlightControlStator flightControl = new FlightControlStator(stator);
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
			m_elevator = statorList.ToArray();
		}

		public void SetRudders(IEnumerable<IMyMotorStator> rotors, float sensitivity, float trim)
		{
			m_controlSensitivity.Y = sensitivity;
			m_trim.Y = trim;

			List<FlightControlStator> statorList = new List<FlightControlStator>();
			foreach (IMyMotorStator stator in rotors)
			{
				Vector3 facing = stator.LocalMatrix.Up;
				bool isForward = stator.IsOnSide(Pseudo.LocalMatrix.Forward);
				m_logger.debugLog(stator.DisplayNameText + " is on " + (isForward ? "forward" : "backward") + " side");

				FlightControlStator flightControl = new FlightControlStator(stator);
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
			m_rudder = statorList.ToArray();
		}

		public void AileronParams(out IEnumerable<IMyMotorStator> rotors, out float sensitivity, out float trim)
		{
			rotors = m_aileron.Select(a => a.Stator);
			sensitivity = m_controlSensitivity.Z;
			trim = m_trim.Z;
		}

		public void ElevatorParams(out IEnumerable<IMyMotorStator> rotors, out float sensitivity, out float trim)
		{
			rotors = m_elevator.Select(a => a.Stator);
			sensitivity = m_controlSensitivity.X;
			trim = m_trim.X;
		}

		public void RudderParams(out IEnumerable<IMyMotorStator> rotors, out float sensitivity, out float trim)
		{
			rotors = m_rudder.Select(a => a.Stator);
			sensitivity = m_controlSensitivity.Y;
			trim = m_trim.Y;
		}

		private void Update10()
		{
			MyPlayer player = MySession.Static.LocalHumanPlayer;
			if (player == null)
				return;

			MyCubeBlock block = player.Controller.ControlledEntity as MyCubeBlock;
			if (block == null || block != m_cockpit)
				return;

			bool skipInput = MyGuiScreenGamePlay.ActiveGameplayScreen != null || MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.LOOKAROUND);

			Vector3D gridCentre = m_grid.GetCentre();
			MyPlanet closest = MyPlanetExtensions.GetClosestPlanet(gridCentre);
			if (closest.GetAirDensity(gridCentre) == 0f)
				return;
			Vector3 gravityDirection = closest.GetWorldGravityNormalized(ref gridCentre);

			if (m_aileron != null)
				AileronHelper(ref gravityDirection, skipInput);
			if (m_elevator != null)
				ElevatorHelper(skipInput);
			if (m_rudder != null)
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
					targetRollVelocity = m_controlSensitivity.Z;
				else if (MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROLL_LEFT))
					targetRollVelocity = -m_controlSensitivity.Z;
				// No input. If nearly level, level off. Otherwise, stop rolling.
				else if (Vector3.Dot(gravityDirection, Pseudo.WorldMatrix.Down) > 0.5f)
				{
					float invCurrentRoll = Vector3.Dot(gravityDirection, Pseudo.WorldMatrix.Left);
					if (-0.1f < invCurrentRoll && invCurrentRoll < 0.1f)
						targetRollVelocity = invCurrentRoll;
					else
						targetRollVelocity = 0f;
				}
				else
					targetRollVelocity = 0f;
			}

			float currentRollVelocity = Vector3.Dot(m_grid.Physics.AngularVelocity, Pseudo.WorldMatrix.Forward);
			float targetAngle = SqrtMag(targetRollVelocity - currentRollVelocity) + m_trim.Z;
			for (int index = 0; index < m_aileron.Length; ++index)
				m_aileron[index].SetTarget(targetAngle);
		}

		private void ElevatorHelper(bool skipInput)
		{
			Vector3 angularVelocity = m_grid.Physics.AngularVelocity;
			float targetPitchVelocity = skipInput ? 0f : m_controlSensitivity.X * MyAPIGateway.Input.GetMouseYForGamePlay() * -0.1f;
			float currentPitchVelocity = Vector3.Dot(angularVelocity, Pseudo.WorldMatrix.Right);
			float targetAngle = SqrtMag(targetPitchVelocity - currentPitchVelocity) + m_trim.X;
			m_logger.debugLog("angularVelocity: " + angularVelocity + ", targetPitchVelocity: " + targetPitchVelocity + ", currentPitchVelocity: " + currentPitchVelocity + ", acceleration: " + targetAngle);
			for (int index = 0; index < m_elevator.Length; ++index)
				m_elevator[index].SetTarget(targetAngle);
		}

		private void RudderHelper(bool skipInput)
		{
			Vector3 angularVelocity = m_grid.Physics.AngularVelocity;
			float targetYawVelocity = skipInput ? 0f : m_controlSensitivity.Y * MyAPIGateway.Input.GetMouseXForGamePlay() * 0.1f;
			float currentYawVelocity = Vector3.Dot(angularVelocity, Pseudo.WorldMatrix.Down);
			float targetAngle = SqrtMag(targetYawVelocity - currentYawVelocity) + m_trim.Y;
			for (int index = 0; index < m_rudder.Length; ++index)
				m_rudder[index].SetTarget(targetAngle);
		}

		private float SqrtMag(float value)
		{
			return (float)(Math.Sign(value) * Math.Sqrt(Math.Abs(value)));
		}

	}
}
