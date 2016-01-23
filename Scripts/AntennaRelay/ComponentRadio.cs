using Sandbox.ModAPI;
using VRage.ModAPI;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Rynchodon.AntennaRelay
{
	public abstract class ComponentRadio
	{

		public static ComponentRadio TryCreateRadio(IMyEntity obj)
		{
			Ingame.IMyRadioAntenna radioAnt = obj as Ingame.IMyRadioAntenna;
			if (radioAnt != null)
				return new CR_AntennaBlock(radioAnt);

			Ingame.IMyBeacon beacon = obj as Ingame.IMyBeacon;
			if (beacon != null)
				return new CR_BeaconBlock(beacon);

			IMyCharacter character = obj as IMyCharacter;
			if (character != null)
				return new CR_Character(character);

			return null;
		}

		public static ComponentRadio CreateRadio(IMyCharacter character)
		{
			return new CR_Character(character);
		}

		public static ComponentRadio CreateRadio(float radius = 0f)
		{
			return new CR_SimpleRadio(radius);
		}

		/// <summary>Radio is functioning.</summary>
		public abstract bool IsWorking { get; }
		/// <summary>Radio is working and capable of receiving transmissions.</summary>
		public abstract bool IsWorkReceive { get; }
		/// <summary>Radio is working and broadcasting transmissions.</summary>
		public abstract bool IsWorkBroad { get; }
		/// <summary>Radio is working, broadcasting, and capable of receiving.</summary>
		public abstract bool IsWorkBroadReceive { get; }
		/// <summary>Broadcast radius (if broadcasting).</summary>
		public abstract float Radius { get; }

		private class CR_AntennaBlock : ComponentRadio
		{

			private Ingame.IMyRadioAntenna m_antenna;

			public override bool IsWorking
			{
				get { return m_antenna.IsWorking; }
			}

			public override bool IsWorkReceive
			{
				get { return IsWorking; }
			}

			public override bool IsWorkBroad
			{
				get { return IsWorking && m_antenna.IsBroadcasting; }
			}

			public override bool IsWorkBroadReceive
			{
				get { return IsWorkBroad; }
			}

			public override float Radius
			{
				get { return m_antenna.Radius; }
			}

			public CR_AntennaBlock(Ingame.IMyRadioAntenna antenna)
			{
				this.m_antenna = antenna;
			}

		}

		private class CR_BeaconBlock : ComponentRadio
		{

			private Ingame.IMyBeacon m_beacon;

			public override bool IsWorking
			{
				get { return m_beacon.IsWorking; }
			}

			public override bool IsWorkReceive
			{
				get { return false; }
			}

			public override bool IsWorkBroad
			{
				get { return IsWorking; }
			}

			public override bool IsWorkBroadReceive
			{
				get { return false; }
			}

			public override float Radius
			{
				get { return m_beacon.Radius; }
			}

			public CR_BeaconBlock(Ingame.IMyBeacon beacon)
			{
				this.m_beacon = beacon;
			}

		}

		private class CR_Character : ComponentRadio
		{

			private IMyCharacter m_character;
			private IMyIdentity m_identity;

			public override bool IsWorking
			{
				get { return !m_identity.IsDead; }
			}

			public override bool IsWorkReceive
			{
				get { return IsWorking; }
			}

			public override bool IsWorkBroad
			{
				get { return IsWorking && (m_character as Sandbox.Game.Entities.IMyControllableEntity).EnabledBroadcasting; }
			}

			public override bool IsWorkBroadReceive
			{
				get { return IsWorkBroad; }
			}

			public override float Radius
			{
				get { return Globals.PlayerBroadcastRadius; }
			}

			public CR_Character(IMyCharacter character)
			{
				this.m_character = character;
				this.m_identity = character.GetIdentity_Safe();
			}

		}

		private class CR_SimpleRadio : ComponentRadio
		{

			private float m_radius = 0;

			public override bool IsWorking
			{
				get { return true; }
			}

			public override bool IsWorkReceive
			{
				get { return true; }
			}

			public override bool IsWorkBroad
			{
				get { return m_radius >= 1f; }
			}

			public override bool IsWorkBroadReceive
			{
				get { return IsWorkBroad; }
			}

			public override float Radius
			{
				get { return m_radius; }
			}

			public CR_SimpleRadio(float radius = 0f)
			{
				this.m_radius = radius;
			}

		}

	}
}
