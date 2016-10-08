using System;
using System.Collections.Generic;
using System.Text;
using Rynchodon.Autopilot.Navigator;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class Orbit : ACommand
	{

		private enum Target : byte { none, asteroid, planet, grid }

		private Target m_target;
		private StringBuilder m_gridName;

		public override ACommand Clone()
		{
			return new Orbit() { m_target = m_target, m_gridName = m_gridName.Clone() };
		}

		public override string Identifier
		{
			get { return "orbit"; }
		}

		public override string AddName
		{
			get { return "Orbit"; }
		}

		public override string AddDescription
		{
			get { return "Orbit an asteroid, a planet, or a friendly ship"; }
		}

		public override string Description
		{
			get
			{
				if (m_target == Target.grid)
					return "Orbit a ship named: " + m_gridName;
				else
					return "Orbit the closest " + m_target;
			}
		}

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			IMyTerminalControlCheckbox asteroid = new MyTerminalControlCheckbox<MyShipController>("OrbitAsteroid", MyStringId.GetOrCompute("Asteroid"), MyStringId.GetOrCompute("Orbit the nearest asteroid"));
			IMyTerminalControlCheckbox planet = new MyTerminalControlCheckbox<MyShipController>("OrbitPlanet", MyStringId.GetOrCompute("Planet"), MyStringId.GetOrCompute("Orbit the nearest planet"));
			MyTerminalControlTextbox<MyShipController> gridName = new MyTerminalControlTextbox<MyShipController>("GridName", MyStringId.GetOrCompute("Grid"), MyStringId.GetOrCompute("Orbit the specified grid"));

			asteroid.Getter = block => m_target == Target.asteroid;
			asteroid.Setter = (block, value) => {
				m_target = Target.asteroid;
				m_gridName.Clear();
				planet.UpdateVisual();
				gridName.UpdateVisual();
			};

			planet.Getter = block => m_target == Target.planet;
			planet.Setter = (block, value) => {
				m_target = Target.planet;
				m_gridName.Clear();
				asteroid.UpdateVisual();
				gridName.UpdateVisual();
			};

			gridName.Getter = block => m_gridName;
			gridName.Setter = (block, value) => {
				m_target = Target.grid;
				m_gridName = value;
				asteroid.UpdateVisual();
				planet.UpdateVisual();
			};

			controls.Add(asteroid);
			controls.Add(planet);
			controls.Add(gridName);
		}

		protected override AutopilotActionList.AutopilotAction Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			if (string.IsNullOrWhiteSpace(command))
			{
				message = "No target specified";
				return null;
			}

			if (Enum.TryParse(command, true, out m_target) && (m_target == Target.asteroid || m_target == Target.planet))
			{
				m_gridName.Clear();
			}
			else
			{
				m_target = Target.grid;
				m_gridName = new StringBuilder(command);
			}

			message = null;
			return mover => new Orbiter(mover, command);
		}

		protected override string TermToString()
		{
			return Identifier + ' ' + TargetString();
		}

		private string TargetString()
		{
			return m_target == Target.grid ? m_gridName.ToString() : m_target.ToString();
		}

	}
}
