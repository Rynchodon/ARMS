using System;
using System.Collections.Generic;
using Rynchodon.Autopilot.Navigator;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace Rynchodon.Autopilot.Instruction.Command
{
	public class LandVoxel : ACommand
	{

		private enum Target : byte { none, asteroid, planet }

		private Target m_target;

		public override ACommand Clone()
		{
			return new LandVoxel() { m_target = m_target };
		}

		public override string Identifier
		{
			get { return "land"; }
		}

		public override string AddName
		{
			get { return "Land on Surface"; }
		}

		public override string AddDescription
		{
			get { return "Land the ship on an asteroid or a planet."; }
		}

		public override string Description
		{
			get { return "Land on the nearest " + m_target; }
		}

		public override void AddControls(List<Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl> controls)
		{
			IMyTerminalControlCheckbox asteroid = new MyTerminalControlCheckbox<MyShipController>("LandAsteroid", MyStringId.GetOrCompute("Asteroid"), MyStringId.GetOrCompute("Land on the nearest asteroid"));
			IMyTerminalControlCheckbox planet = new MyTerminalControlCheckbox<MyShipController>("LandPlanet", MyStringId.GetOrCompute("Planet"), MyStringId.GetOrCompute("Land on the nearest planet"));

			asteroid.Getter = block => m_target == Target.asteroid;
			planet.Getter = block => m_target == Target.planet;
			asteroid.Setter = (block, value) => {
				m_target = Target.asteroid;
				planet.UpdateVisual();
			};
			planet.Setter = (block, value) => {
				m_target = Target.planet;
				asteroid.UpdateVisual();
			};

			controls.Add(asteroid);
			controls.Add(planet);
		}

		protected override AutopilotActionList.AutopilotAction Parse(VRage.Game.ModAPI.IMyCubeBlock autopilot, string command, out string message)
		{
			if (Enum.TryParse(command, true, out m_target))
			{
				message = null;
				return mover => new VoxelLander(mover, m_target == Target.planet);
			}

			message = "Neither asteroid nor planet: " + command;
			return null;
		}

		protected override string TermToString(out string message)
		{
			if (m_target == Target.none)
			{
				message = "Must select either asteroid or planet";
				return null;
			}

			message = null;
			return Identifier + ' ' + m_target;
		}
	}
}
