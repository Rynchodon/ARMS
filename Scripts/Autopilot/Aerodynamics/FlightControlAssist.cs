using System;
using System.Collections.Generic;
using System.Linq;
using Rynchodon.Autopilot.Data;
using Rynchodon.Utility;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
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

			public IMyMotorStator Stator;
			public bool Flipped;

			public FlightControlStator(IMyMotorStator Stator)
			{
				this.Stator = Stator;
				this.Flipped = false;

				if (VelocityProperty == null)
					VelocityProperty = (ITerminalProperty<float>)Stator.GetProperty("Velocity");
				((ITerminalProperty<float>)Stator.GetProperty("LowerLimit")).SetValue(Stator, -45f);
				((ITerminalProperty<float>)Stator.GetProperty("UpperLimit")).SetValue(Stator, 45f);
			}

			public void Flip()
			{
				this.Flipped = !Flipped;
			}

			public void SetTarget(float targetAngle)
			{
				if (Flipped)
					targetAngle = -targetAngle;

				float targetVelocity = (targetAngle - Stator.Angle) * 60f;
				float currentVelocity = ((MyMotorStator)Stator).TargetVelocity;
				if (((targetVelocity == 0f) != (currentVelocity == 0f)) || Math.Abs(targetVelocity - currentVelocity) > 0.1f)
					VelocityProperty.SetValue(Stator, targetVelocity);
				Logger.TraceLog("target acceleration: " + targetAngle + ", target angle: " + targetAngle + ", stator angle: " + Stator.Angle + ", target velocity: " + targetVelocity + ", current velocity: " + currentVelocity,
					context: Stator.CubeGrid.nameWithId(), primaryState: Stator.nameWithId());
			}
		}

		private const byte StopAfter = 4;
		private const float InputSmoothing = 0.2f, OppInputSmooth = 1f - InputSmoothing;
		private readonly MyCockpit m_cockpit;
		private readonly PseudoBlock Pseudo;

		private FlightControlStator[] m_aileron, m_elevator, m_rudder;
		/// <summary>How sensitive the ship will be to input. pitch, yaw, roll.</summary>
		private Vector3 m_controlSensitivity = new Vector3(1f, 1f, 1f);
		/// <summary>Default position for rotors. pitch, yaw, roll.</summary>
		private Vector3 m_trim;

		private Vector3 m_previousInput;
		private Vector3 m_previousVelocity;
		private Vector3 m_previousTargetVelocity;
		private Vector3 m_stabilizeFactor = new Vector3(1f, 1f, 1f);

		private MyCubeGrid m_grid { get { return m_cockpit.CubeGrid; } }

		private Logable Log { get { return new Logable(m_grid); } }

		public FlightControlAssist(MyCockpit cockpit)
		{
			this.m_cockpit = cockpit;
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
					Log.AlwaysLog("Facing the wrong way: " + stator.nameWithId() + ", facing: " + facing + ", local flight matrix: " + Pseudo.LocalMatrix, Logger.severity.WARNING);
					continue;
				}

				FlightControlStator flightControl = new FlightControlStator(stator);
				if (!stator.IsOnSide(facing))
				{
					Log.DebugLog("On " + Base6Directions.GetDirection(-facing) + " side and facing " + Base6Directions.GetDirection(facing));
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
				Log.DebugLog(stator.DisplayNameText + " is on " + (isForward ? "forward" : "backward") + " side");

				FlightControlStator flightControl = new FlightControlStator(stator);
				if (facing == Pseudo.LocalMatrix.Left)
				{
					if (!isForward)
					{
						Log.DebugLog("Aft and facing port: " + stator.DisplayNameText);
						flightControl.Flip();
					}
				}
				else if (facing == Pseudo.LocalMatrix.Right)
				{
					if (isForward)
					{
						Log.DebugLog("Fore and facing starboard: " + stator.DisplayNameText);
						flightControl.Flip();
					}
				}
				else
				{
					Log.AlwaysLog("Facing the wrong way: " + stator.nameWithId() + ", facing: " + facing + ", local flight matrix: " + Pseudo.LocalMatrix, Logger.severity.WARNING);
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
				Log.DebugLog(stator.DisplayNameText + " is on " + (isForward ? "forward" : "backward") + " side");

				FlightControlStator flightControl = new FlightControlStator(stator);
				if (facing == Pseudo.LocalMatrix.Up)
				{
					if (!isForward)
					{
						Log.DebugLog("Aft and facing up: " + stator.DisplayNameText);
						flightControl.Flip();
					}
				}
				else if (facing == Pseudo.LocalMatrix.Down)
				{
					if (isForward)
					{
						Log.DebugLog("Fore and facing down: " + stator.DisplayNameText);
						flightControl.Flip();
					}
				}
				else
				{
					Log.AlwaysLog("Facing the wrong way: " + stator.nameWithId() + ", facing: " + facing + ", local flight matrix: " + Pseudo.LocalMatrix, Logger.severity.WARNING);
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

			Log.DebugLog("active screen: " + MyGuiScreenGamePlay.ActiveGameplayScreen, condition: MyGuiScreenGamePlay.ActiveGameplayScreen != null);
			Log.DebugLog("Lookaround", condition: MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.LOOKAROUND));

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
			float input;
			if (skipInput)
				input = 0f;
			else if (MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROLL_RIGHT))
				input = 1f;
			else if (MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROLL_LEFT))
				input = -1f;
			else
				input = 0f;
			input = input * InputSmoothing + m_previousInput.Z * OppInputSmooth;

			float currentVelocity = Vector3.Dot(m_grid.Physics.AngularVelocity, Pseudo.WorldMatrix.Forward);
			float targetVelocity, targetAngle;

			if (Math.Abs(input) > 0.01f)
			{
				targetVelocity = input * m_controlSensitivity.Z;
				targetAngle = SqrtMag(targetVelocity - currentVelocity) + m_trim.Z;
			}
			else
			{
				// No input. If nearly level, level off. Otherwise, stop rolling.
				if (Vector3.Dot(gravityDirection, Pseudo.WorldMatrix.Down) > 0.5f)
				{
					float invCurrentRoll = Vector3.Dot(gravityDirection, Pseudo.WorldMatrix.Left);
					if (-0.1f < invCurrentRoll && invCurrentRoll < 0.1f)
						targetVelocity = invCurrentRoll;
					else
						targetVelocity = 0f;
				}
				else
					targetVelocity = 0f;

				ModStabilizeFactor(2, targetVelocity, currentVelocity);
				targetAngle = m_stabilizeFactor.Z * SqrtMag(targetVelocity - currentVelocity) + m_trim.Z;

				Log.TraceLog("targetVelocity: " + targetVelocity + ", currentVelocity: " + currentVelocity + ", stabilizeFactor: " + m_stabilizeFactor.Z + ", targetAngle: " + targetAngle);
			}

			for (int index = 0; index < m_aileron.Length; ++index)
				m_aileron[index].SetTarget(targetAngle);

			m_previousInput.Z = input;
			m_previousVelocity.Z = currentVelocity;
			m_previousTargetVelocity.Z = targetVelocity;
		}

		private void ElevatorHelper(bool skipInput)
		{
			float input = skipInput ? 0f : MyAPIGateway.Input.GetMouseYForGamePlay() * -0.01f;
			input = input * InputSmoothing + m_previousInput.X * OppInputSmooth;

			float currentVelocity = Vector3.Dot(m_grid.Physics.AngularVelocity, Pseudo.WorldMatrix.Right);
			float targetVelocity, targetAngle;

			if (Math.Abs(input) > 0.01f)
			{
				targetVelocity = input * m_controlSensitivity.X;
				targetAngle = SqrtMag(targetVelocity - currentVelocity) + m_trim.X;
			}
			else
			{
				// No input. Stop pitching.
				targetVelocity = 0f;
				ModStabilizeFactor(0, targetVelocity, currentVelocity);
				targetAngle = m_stabilizeFactor.X * SqrtMag(targetVelocity - currentVelocity) + m_trim.X;

				Log.TraceLog("targetVelocity: " + targetVelocity + ", currentVelocity: " + currentVelocity + ", stabilizeFactor: " + m_stabilizeFactor.X + ", targetAngle: " + targetAngle);
			}

			for (int index = 0; index < m_elevator.Length; ++index)
				m_elevator[index].SetTarget(targetAngle);

			m_previousInput.X = input;
			m_previousVelocity.X = currentVelocity;
			m_previousTargetVelocity.X = targetVelocity;
		}

		private void RudderHelper(bool skipInput)
		{
			float input = skipInput ? 0f : MyAPIGateway.Input.GetMouseXForGamePlay() * 0.01f;
			input = input * InputSmoothing + m_previousInput.Y * OppInputSmooth;

			float currentVelocity = Vector3.Dot(m_grid.Physics.AngularVelocity, Pseudo.WorldMatrix.Down);
			float targetVelocity, targetAngle;

			if (Math.Abs(input) > 0.01f)
			{
				targetVelocity = input * m_controlSensitivity.Y;
				targetAngle = SqrtMag(targetVelocity - currentVelocity) + m_trim.Y;
			}
			else
			{
				// No input. Stop yawing.
				targetVelocity = 0f;
				ModStabilizeFactor(1, targetVelocity, currentVelocity);
				targetAngle = m_stabilizeFactor.Y * SqrtMag(targetVelocity - currentVelocity) + m_trim.Y;

				Log.TraceLog("targetVelocity: " + targetVelocity + ", currentVelocity: " + currentVelocity + ", stabilizeFactor: " + m_stabilizeFactor.Y + ", targetAngle: " + targetAngle);
			}

			for (int index = 0; index < m_rudder.Length; ++index)
				m_rudder[index].SetTarget(targetAngle);

			m_previousInput.Y = input;
			m_previousVelocity.Y = currentVelocity;
			m_previousTargetVelocity.Y = targetVelocity;
		}

		private float SqrtMag(float value)
		{
			return (float)(Math.Sign(value) * Math.Sqrt(Math.Abs(value)));
		}

		private void ModStabilizeFactor(int index, float targetVelocity, float currentVelocity)
		{
			if (Math.Abs(targetVelocity - currentVelocity) < 0.1f)
				return;

			float previousTargetVelocity = m_previousTargetVelocity.GetDim(index);

			if (Math.Abs(targetVelocity - previousTargetVelocity) > 0.01f)
			{
				Log.DebugLog("target velocity changed. Index: " + index);
				return;
			}

			float previousVelocity = m_previousVelocity.GetDim(index);

			float currDiff = targetVelocity - currentVelocity;
			float prevDiff = targetVelocity - previousVelocity;

			float ratio = currDiff / prevDiff;

			if (ratio < 0.5f)
			{
				m_stabilizeFactor.SetDim(index, 0.99f * m_stabilizeFactor.GetDim(index));
				Log.DebugLog("ratio is low: " + ratio + ", stabilize factor: " + m_stabilizeFactor.GetDim(index) + ", targetVelocity: " + targetVelocity + ", previousTargetVelocity: " + previousTargetVelocity +
					", currentVelocity: " + currentVelocity + ", previousVelocity: " + previousVelocity + ", index: " + index);
			}
			else if (ratio > 0.9f)
			{
				m_stabilizeFactor.SetDim(index, 1.01f * m_stabilizeFactor.GetDim(index));
				Log.DebugLog("ratio is high: " + ratio + ", stabilize factor: " + m_stabilizeFactor.GetDim(index) + ", targetVelocity: " + targetVelocity + ", previousTargetVelocity: " + previousTargetVelocity +
					", currentVelocity: " + currentVelocity + ", previousVelocity: " + previousVelocity + ", index: " + index);
			}
		}

	}
}
