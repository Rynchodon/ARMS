using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using VRage.Game;
using VRage.Game.Components;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Rynchodon.ResourceGroups
{
	[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
	public class Initializer : MySessionComponentBase
	{

		public Initializer()
		{
			// move radar blocks to radar resource group

			// first make sure radar group exists. If it does not, radar will stay in its original group.
			MyStringHash radar = MyStringHash.GetOrCompute("Radar");
			foreach (MyDefinitionBase radarGroupDefn in MyDefinitionManager.Static.GetAllDefinitions())
				if (radarGroupDefn is MyResourceDistributionGroupDefinition && radarGroupDefn.Id.SubtypeId == radar)
				{
					// find each radar block and move it to radar group
					foreach (MyDefinitionBase radarBlockDefn in MyDefinitionManager.Static.GetAllDefinitions())
						if (radarBlockDefn is MyCubeBlockDefinition &&
							radarBlockDefn.Id.SubtypeName.ToLower().Contains("radar")) // RadarEquipment.IsRadarOrJammer
						{
							MyBeaconDefinition beaconDefn = radarBlockDefn as MyBeaconDefinition;
							if (beaconDefn != null)
							{
								beaconDefn.ResourceSinkGroup = radar.ToString();
								continue;
							}
							MyLaserAntennaDefinition lasAntDefn = radarBlockDefn as MyLaserAntennaDefinition;
							if (lasAntDefn != null)
							{
								lasAntDefn.ResourceSinkGroup = radar;
								continue;
							}
							MyRadioAntennaDefinition radAntDefn = radarBlockDefn as MyRadioAntennaDefinition;
							if (radAntDefn != null)
							{
								radAntDefn.ResourceSinkGroup = radar;
								continue;
							}

							// stop trying to guess what the radar block is made of
						}

					break;
				}
		}

	}
}
