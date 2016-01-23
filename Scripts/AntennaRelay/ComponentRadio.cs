using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.ModAPI;
using VRageMath;
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

		public static ComponentRadio CreateRadio(IMyEntity entity, float radius = 0f)
		{
			return new CR_SimpleAntenna(entity, radius);
		}

		/// <summary>The entity that has the radio.</summary>
		public abstract IMyEntity Entity { get; }
		/// <summary>Radio is functioning.</summary>
		public abstract bool IsWorking { get; }
		/// <summary>Radio is capable of receiving transmissions.</summary>
		public abstract bool CanReceive { get; }
		/// <summary>Radio is capable of broadcasting data and broadcasting is enabled.</summary>
		public abstract bool CanBroadcastData { get; }
		/// <summary>Radio is capable of broacasting its position.</summary>
		public abstract bool CanBroadcastPosition { get; }
		/// <summary>Broadcast radius (if broadcasting).</summary>
		public abstract float Radius { get; }

		/// <summary>
		/// Tests for a connection from this radio to another radio.
		/// </summary>
		/// <param name="other">The radio that may be connected.</param>
		public NetworkNode.CommunicationType TestConnection(ComponentRadio other)
		{
			if (!IsWorking || !other.IsWorking)
				return NetworkNode.CommunicationType.None;

			if (!CanBroadcastData || !other.CanReceive)
				return NetworkNode.CommunicationType.None;

			float distSquared = Vector3.DistanceSquared(Entity.GetPosition(), other.Entity.GetPosition());
			if (distSquared > Radius * Radius)
				return NetworkNode.CommunicationType.None;

			if (!CanReceive || !other.CanBroadcastData)
				return NetworkNode.CommunicationType.OneWay;

			if (distSquared > other.Radius * other.Radius)
				return NetworkNode.CommunicationType.OneWay;

			return NetworkNode.CommunicationType.TwoWay;
		}

		public bool CanBroadcastTo(ComponentRadio other)
		{
			if (!IsWorking || !other.IsWorking)
				return false;

			if (!CanBroadcastData || !other.CanReceive)
				return false;

			float distSquared = Vector3.DistanceSquared(Entity.GetPosition(), other.Entity.GetPosition());
			return distSquared <= Radius * Radius;
		}

		private class CR_AntennaBlock : ComponentRadio
		{

			private static ITerminalProperty<bool> s_prop_broadcasting;

			static CR_AntennaBlock()
			{
				MyAPIGateway.Entities.OnCloseAll += Entities_OnCloseAll;
			}

			static void Entities_OnCloseAll()
			{
				MyAPIGateway.Entities.OnCloseAll -= Entities_OnCloseAll;
				s_prop_broadcasting = null;
			}

			private Ingame.IMyRadioAntenna m_antenna;

			public override IMyEntity Entity
			{
				get { return m_antenna; }
			}

			public override bool IsWorking
			{
				get { return m_antenna.IsWorking; }
			}

			public override bool CanReceive
			{
				get { return true; }
			}

			public override bool CanBroadcastData
			{
				get { return s_prop_broadcasting.GetValue(m_antenna); }
			}

			public override bool CanBroadcastPosition
			{
				get { return CanBroadcastData; }
			}

			public override float Radius
			{
				get { return m_antenna.Radius; }
			}

			public CR_AntennaBlock(Ingame.IMyRadioAntenna antenna)
			{
				this.m_antenna = antenna;

				if (s_prop_broadcasting == null)
					s_prop_broadcasting = antenna.GetProperty("EnableBroadCast").AsBool();
			}

		}

		private class CR_BeaconBlock : ComponentRadio
		{

			private Ingame.IMyBeacon m_beacon;

			public override IMyEntity Entity
			{
				get { return m_beacon; }
			}

			public override bool IsWorking
			{
				get { return m_beacon.IsWorking; }
			}

			public override bool CanReceive
			{
				get { return false; }
			}

			public override bool CanBroadcastData
			{
				get { return false; }
			}

			public override bool CanBroadcastPosition
			{
				get { return true; }
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

			public override IMyEntity Entity
			{
				get { return m_character as IMyEntity; }
			}

			public override bool IsWorking
			{
				get { return !m_identity.IsDead; }
			}

			public override bool CanReceive
			{
				get { return true; }
			}

			public override bool CanBroadcastData
			{
				get { return (m_character as Sandbox.Game.Entities.IMyControllableEntity).EnabledBroadcasting; }
			}

			public override bool CanBroadcastPosition
			{
				get { return CanBroadcastData; }
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

		private class CR_SimpleAntenna : ComponentRadio
		{

			private IMyEntity m_entity;
			private float m_radius = 0;

			public override IMyEntity Entity
			{
				get { return m_entity; }
			}

			public override bool IsWorking
			{
				get { return true; }
			}

			public override bool CanReceive
			{
				get { return true; }
			}

			public override bool CanBroadcastData
			{
				get { return m_radius >= 1f; }
			}

			public override bool CanBroadcastPosition
			{
				get { return CanBroadcastData; }
			}

			public override float Radius
			{
				get { return m_radius; }
			}

			public CR_SimpleAntenna(IMyEntity entity, float radius = 0f)
			{
				this.m_entity = entity;
				this.m_radius = radius;
			}

		}

	}
}
